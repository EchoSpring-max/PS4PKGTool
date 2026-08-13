using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Utilities.Shadps4
{
    public enum Shadps4InstallStatus
    {
        Success,
        /// <summary>The destination CUSA folder already exists and the user did not approve a replacement.</summary>
        ExistingInstall,
        InsufficientSpace,
        PkgMissing,
        LibraryMissing,
        ExtractionFailed,
        ValidationFailed,
        Cancelled,
        Failed,
    }

    public sealed record Shadps4InstallResult(
        Shadps4InstallStatus Status,
        string Message,
        string? InstalledPath = null);

    /// <summary>
    /// Transactional "Install Game to shadPS4 Library": extracts a base-game
    /// PKG into a staging folder inside the destination library, validates the
    /// extracted layout, then atomically renames it to the CUSA folder.
    ///
    /// Never extracts directly into the final directory: on cancellation,
    /// extraction failure, validation failure or disk-full the staging folder
    /// is removed and any existing installation is left untouched.
    /// The real extractor uses the same orbis-pub-cmd pattern as the app's
    /// existing full-PKG extraction (verified: img_extract --passcode &lt;code&gt;
    /// &lt;pkg&gt; &lt;out&gt; extracts the whole image).
    /// </summary>
    public sealed class Shadps4InstallService
    {
        /// <summary>Same passcode the app uses everywhere else.</summary>
        public const string DefaultPasscode = "00000000000000000000000000000000";

        /// <summary>Extra margin on top of the estimated extracted size.</summary>
        public const long SpaceMarginBytes = 1L * 1024 * 1024 * 1024; // 1 GB

        public string OrbisExePath { get; set; } = "";

        /// <summary>Test seam: replaces the orbis-based extraction. (pkg, destDir, ct) -> success.</summary>
        public Func<string, string, CancellationToken, bool>? ExtractOverride { get; set; }

        /// <summary>Test seam: free bytes on the destination volume. (libraryDir) -> bytes.</summary>
        public Func<string, long>? FreeSpaceOverride { get; set; }

        /// <summary>
        /// Installs the PKG into the given shadPS4 library as
        /// &lt;library&gt;\&lt;CUSAxxxxx&gt;. replaceExisting approves overwriting an
        /// existing installation (the UI must confirm first).
        /// mergeIntoExisting is the update/patch path: instead of rejecting or
        /// replacing an existing folder, the extracted (flattened) patch files
        /// are merged over it - same-named files are overwritten, base-only
        /// files remain. Without an existing folder it behaves like a fresh
        /// install, so a patch can also be installed before its base game.
        /// </summary>
        public Shadps4InstallResult Install(
            string pkgPath, string titleId, string libraryDir, bool replaceExisting,
            IProgress<string>? progress = null, CancellationToken ct = default,
            bool mergeIntoExisting = false)
        {
            Logger.LogInformation($"Shadps4Install: {pkgPath}");
            Logger.LogInformation($"Shadps4Install: target = {Path.Combine(libraryDir, titleId)} (replace={replaceExisting}, merge={mergeIntoExisting})");
            if (string.IsNullOrWhiteSpace(pkgPath) || !File.Exists(pkgPath))
                return Fail(Shadps4InstallStatus.PkgMissing, $"PKG not found: {pkgPath}");
            if (string.IsNullOrWhiteSpace(libraryDir) || !Directory.Exists(libraryDir))
                return Fail(Shadps4InstallStatus.LibraryMissing, $"shadPS4 library not found: {libraryDir}");

            string finalDir = Path.Combine(libraryDir, titleId);
            if (Directory.Exists(finalDir) && !replaceExisting && !mergeIntoExisting)
                return Fail(Shadps4InstallStatus.ExistingInstall,
                    $"Game {titleId} is already installed in {libraryDir}.");

            // Free-space check BEFORE writing anything.
            long free = FreeSpaceOverride != null
                ? FreeSpaceOverride(libraryDir)
                : new DriveInfo(Path.GetPathRoot(Path.GetFullPath(libraryDir))!).AvailableFreeSpace;
            long estimate = EstimatedExtractedSize(pkgPath);
            Logger.LogInformation($"Shadps4Install: free={HelperBytes(free)}, estimate={HelperBytes(estimate)}");
            if (free < estimate + SpaceMarginBytes)
                return Fail(Shadps4InstallStatus.InsufficientSpace,
                    $"Not enough free space in {libraryDir}: needs ~{HelperBytes(estimate + SpaceMarginBytes)}, has {HelperBytes(free)}.");

            string staging = Path.Combine(libraryDir, $".ps4pkgtool-{titleId}.tmp");
            try
            {
                if (Directory.Exists(staging))
                {
                    Logger.LogWarning($"Shadps4Install: removing leftover staging {staging}");
                    Directory.Delete(staging, true); // leftovers from a previous interrupted run
                }
                Directory.CreateDirectory(staging);

                if (ct.IsCancellationRequested)
                    return Fail(Shadps4InstallStatus.Cancelled, "Installation cancelled.");

                progress?.Report("Extracting PKG...");
                Logger.LogInformation("Shadps4Install: extracting via orbis-pub-cmd img_extract...");
                string extractDetail = "";
                bool extracted;
                if (ExtractOverride != null)
                {
                    extracted = ExtractOverride(pkgPath, staging, ct);
                }
                else
                {
                    (extracted, extractDetail) = ExtractWithOrbis(pkgPath, staging, ct);
                }
                if (!extracted)
                {
                    string detail = string.IsNullOrWhiteSpace(extractDetail)
                        ? "orbis-pub-cmd img_extract failed (see log)."
                        : extractDetail;
                    return Fail(Shadps4InstallStatus.ExtractionFailed,
                        "PKG extraction failed: " + detail);
                }

                if (ct.IsCancellationRequested)
                    return Fail(Shadps4InstallStatus.Cancelled, "Installation cancelled.");

                // shadPS4 libraries hold DUMP-LAYOUT game folders (eboot.bin and
                // sce_sys at the folder root), not the PKG Image0/Sc0 tree the
                // extractor produces - flatten before validating/finalizing.
                progress?.Report("Arranging game files...");
                Logger.LogInformation("Shadps4Install: flattening to dump layout...");
                FlattenToDumpLayout(staging);

                progress?.Report("Validating extracted game...");
                string? eboot = Shadps4Launcher.FindEboot(staging, depth: 3);
                if (eboot == null || !Directory.Exists(Path.Combine(staging, "sce_sys")))
                    return Fail(Shadps4InstallStatus.ValidationFailed,
                        "The extracted PKG does not contain a valid game layout (eboot.bin / sce_sys missing).");
                Logger.LogInformation($"Shadps4Install: validated eboot at {eboot}");

                progress?.Report("Finalizing installation...");
                if (mergeIntoExisting && Directory.Exists(finalDir))
                {
                    // Update path: merge the patch over the existing dump.
                    Logger.LogInformation($"Shadps4Install: merging patch into {finalDir}");
                    MergeOverwrite(staging, finalDir);
                    return Success($"Updated {titleId} in {finalDir}.", finalDir);
                }
                if (Directory.Exists(finalDir))
                {
                    Logger.LogInformation($"Shadps4Install: replacing existing install {finalDir}");
                    Directory.Delete(finalDir, true);
                }
                Directory.Move(staging, finalDir);

                return Success($"Installed {titleId} into {finalDir}.", finalDir);
            }
            catch (OperationCanceledException)
            {
                Cleanup(staging);
                Logger.LogWarning("Shadps4Install: cancelled - staging cleaned.");
                return new Shadps4InstallResult(Shadps4InstallStatus.Cancelled, "Installation cancelled.");
            }
            catch (IOException ex) when ((uint)ex.HResult == 0x80070070) // ERROR_DISK_FULL
            {
                Cleanup(staging);
                Logger.LogError("Shadps4Install: disk full - " + ex.Message);
                return new Shadps4InstallResult(Shadps4InstallStatus.InsufficientSpace,
                    $"Disk full during extraction: {ex.Message}");
            }
            catch (Exception ex)
            {
                Cleanup(staging);
                Logger.LogError("Shadps4Install failed: " + ex);
                return new Shadps4InstallResult(Shadps4InstallStatus.Failed, ex.Message);
            }
            finally
            {
                // Safety net: the staging folder must never survive failure.
                if (Directory.Exists(staging))
                {
                    try { Directory.Delete(staging, true); } catch { }
                }
            }
        }

        /// <summary>
        /// Extracts a PKG to a user-chosen folder (Extract &amp; Launch flow) and
        /// returns the eboot.bin path, or null when extraction/validation failed.
        /// The game root must follow the dump layout, so the extraction is
        /// flattened before the eboot is located.
        /// </summary>
        public string? ExtractToFolder(string pkgPath, string destinationDir,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (!Directory.Exists(destinationDir)) Directory.CreateDirectory(destinationDir);
            progress?.Report("Extracting PKG...");
            bool ok;
            if (ExtractOverride != null)
            {
                ok = ExtractOverride(pkgPath, destinationDir, ct);
            }
            else
            {
                var (extracted, detail) = ExtractWithOrbis(pkgPath, destinationDir, ct);
                ok = extracted;
                if (!ok)
                    Logger.LogWarning("Shadps4Install: ExtractToFolder failed: " + detail);
            }
            if (!ok || ct.IsCancellationRequested) return null;
            FlattenToDumpLayout(destinationDir);
            return Shadps4Launcher.FindEboot(destinationDir, depth: 3);
        }

        /// <summary>
        /// Dedicated "extract as PS4 game dump" layout transformation.
        ///
        /// The raw PKG extraction tree (Image0/, Sc0/) is NOT the layout
        /// shadPS4 libraries use. The final game folder must resemble a
        /// decrypted dump from a jailbroken PS4:
        ///
        ///   CUSAxxxxx\
        ///   ├── eboot.bin
        ///   ├── sce_module\
        ///   ├── data\
        ///   └── sce_sys\          (param.sfo, icon0.png, pic0.png, ...)
        ///
        /// - Image0\ contents become the ROOT of the game folder.
        /// - Sc0\ contents are system metadata and merge into sce_sys\
        ///   (a Sc0\sce_sys\ subtree merges its contents directly).
        /// - Image0 copies win on conflicts.
        /// A no-op when the tree is already flat (a future extractor may
        /// emit the dump layout directly).
        /// </summary>
        public static void FlattenToDumpLayout(string folder)
        {
            // Image0 contents become the game-folder root.
            string image0 = Path.Combine(folder, "Image0");
            if (Directory.Exists(image0))
            {
                foreach (string entry in Directory.GetFileSystemEntries(image0))
                {
                    string dest = Path.Combine(folder, Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                    {
                        if (Directory.Exists(dest))
                            MergeDirectory(entry, dest);
                        else
                            Directory.Move(entry, dest);
                    }
                    else
                    {
                        if (!File.Exists(dest)) File.Move(entry, dest);
                    }
                }
                Directory.Delete(image0, true);
            }

            // Sc0 contents belong under sce_sys.
            string sc0 = Path.Combine(folder, "Sc0");
            if (Directory.Exists(sc0))
            {
                string sceSys = Path.Combine(folder, "sce_sys");
                Directory.CreateDirectory(sceSys);
                foreach (string entry in Directory.GetFileSystemEntries(sc0))
                {
                    if (Directory.Exists(entry) &&
                        Path.GetFileName(entry).Equals("sce_sys", StringComparison.OrdinalIgnoreCase))
                    {
                        // Already a sce_sys subtree - merge its contents directly.
                        foreach (string sub in Directory.GetFileSystemEntries(entry))
                        {
                            string target = Path.Combine(sceSys, Path.GetFileName(sub));
                            if (Directory.Exists(sub))
                            {
                                if (Directory.Exists(target)) MergeDirectory(sub, target);
                                else Directory.Move(sub, target);
                            }
                            else
                            {
                                if (!File.Exists(target)) File.Move(sub, target);
                            }
                        }
                        Directory.Delete(entry, true);
                        continue;
                    }

                    string dest = Path.Combine(sceSys, Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                    {
                        if (Directory.Exists(dest)) MergeDirectory(entry, dest);
                        else Directory.Move(entry, dest);
                    }
                    else
                    {
                        if (!File.Exists(dest)) File.Move(entry, dest); // Image0 copy wins
                    }
                }
                Directory.Delete(sc0, true);
            }
        }

        /// <summary>
        /// Moves the source tree over an existing destination tree: same-named
        /// files are OVERWRITTEN by the source (patch wins), files only in the
        /// destination remain. Used for update/patch installs - the base-game
        /// merge inside FlattenToDumpLayout uses the opposite rule.
        /// </summary>
        private static void MergeOverwrite(string source, string dest)
        {
            foreach (string entry in Directory.GetFileSystemEntries(source))
            {
                string target = Path.Combine(dest, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    if (!Directory.Exists(target)) Directory.Move(entry, target);
                    else MergeOverwrite(entry, target);
                }
                else
                {
                    File.Move(entry, target, overwrite: true);
                }
            }
            Directory.Delete(source, true);
        }

        private static void MergeDirectory(string source, string dest)
        {
            foreach (string entry in Directory.GetFileSystemEntries(source))
            {
                string target = Path.Combine(dest, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    if (!Directory.Exists(target)) Directory.Move(entry, target);
                    else MergeDirectory(entry, target);
                }
                else
                {
                    if (!File.Exists(target)) File.Move(entry, target);
                }
            }
            Directory.Delete(source, true);
        }

        /// <summary>Available free space on the volume that hosts the given directory.</summary>
        public static long FreeSpace(string directory)
            => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;

        /// <summary>Estimated extracted size: 1.5x the PKG size (very rough lower bound).</summary>
        public static long EstimatedExtractedSize(string pkgPath)
        {
            try
            {
                var fi = new FileInfo(pkgPath);
                return (long)(fi.Length * 1.5);
            }
            catch
            {
                return 20L * 1024 * 1024 * 1024;
            }
        }

        private Shadps4InstallResult Fail(Shadps4InstallStatus status, string message)
        {
            Logger.LogWarning($"Shadps4Install: {message}");
            return new Shadps4InstallResult(status, message);
        }

        private static Shadps4InstallResult Success(string message, string finalDir)
        {
            Logger.LogInformation($"Shadps4Install: {message}");
            return new Shadps4InstallResult(Shadps4InstallStatus.Success, message, finalDir);
        }

        /// <summary>
        /// Real extraction: orbis-pub-cmd bare img_extract (whole image), with the
        /// same ASCII-rename + short-temp-root pattern as the app's existing
        /// full-PKG extraction. Captures BOTH stdout and stderr and returns a
        /// short detail string so a failure shows the real orbis error instead
        /// of pointing at an empty log.
        /// </summary>
        private (bool Ok, string Detail) ExtractWithOrbis(string pkgPath, string destinationDir, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(OrbisExePath) || !File.Exists(OrbisExePath))
            {
                Logger.LogError($"Shadps4Install: orbis-pub-cmd not found at {OrbisExePath}");
                return (false, "orbis-pub-cmd.exe was not found at " + OrbisExePath);
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "p4t_x_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            string tempPkg = Path.Combine(tempRoot, "pkg.pkg");
            string tempOut = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(tempOut);

            bool renamed = false;
            try
            {
                File.Move(pkgPath, tempPkg);
                renamed = true;

                var psi = new ProcessStartInfo
                {
                    FileName = OrbisExePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("img_extract");
                psi.ArgumentList.Add("--passcode");
                psi.ArgumentList.Add(DefaultPasscode);
                psi.ArgumentList.Add(tempPkg);
                psi.ArgumentList.Add(tempOut);

                using var extract = new Process { StartInfo = psi };
                extract.Start();
                Logger.LogInformation($"Shadps4Install: orbis started (pid {extract.Id}), extracting {Path.GetFileName(pkgPath)}...");
                Task<string> stdoutTask = extract.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = extract.StandardError.ReadToEndAsync();
                while (!extract.WaitForExit(1000))
                {
                    if (ct.IsCancellationRequested)
                    {
                        try { extract.Kill(); extract.WaitForExit(); } catch { }
                        Logger.LogWarning("Shadps4Install: orbis killed by cancellation.");
                        return (false, "cancelled");
                    }
                }
                string stdout = stdoutTask.Result;
                string stderr = stderrTask.Result;

                if (extract.ExitCode != 0)
                {
                    Logger.LogError($"Shadps4Install: orbis img_extract exited {extract.ExitCode}.");
                    Logger.LogError("Shadps4Install: orbis stdout: " + Tail(stdout));
                    Logger.LogError("Shadps4Install: orbis stderr: " + Tail(stderr));
                    return (false, $"orbis-pub-cmd img_extract exited {extract.ExitCode}. {Tail(stderr)}".Trim());
                }
                Logger.LogInformation("Shadps4Install: orbis img_extract completed.");

                // Move the extracted entries into the destination.
                foreach (string entry in Directory.GetFileSystemEntries(tempOut))
                {
                    string dest = Path.Combine(destinationDir, Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                        Directory.Move(entry, dest);
                    else
                        File.Move(entry, dest);
                }
                return (true, "");
            }
            catch (Exception ex)
            {
                Logger.LogError("Shadps4Install: orbis extraction threw: " + ex);
                return (false, ex.Message);
            }
            finally
            {
                if (renamed && File.Exists(tempPkg))
                {
                    try
                    {
                        if (!File.Exists(pkgPath)) File.Move(tempPkg, pkgPath);
                    }
                    catch { }
                }
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }

        /// <summary>Last ~800 characters of a captured process output (single log line).</summary>
        private static string Tail(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "(no output)";
            return text.Length <= 800 ? text : "..." + text.Substring(text.Length - 800);
        }

        private static void Cleanup(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        private static string HelperBytes(long bytes)
            => bytes >= 1024 * 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";
    }

    /// <summary>
    /// Minimal PSF (param.sfo) reader for the APP_VER string, used to show the
    /// installed version when a game folder already exists. Deterministic
    /// format: header (magic/version/key/data offsets/count) + entry table.
    /// </summary>
    public static class ParamSfoReader
    {
        private const int HeaderSize = 20;

        public static string? ReadAppVersion(string paramSfoPath)
        {
            try
            {
                var data = File.ReadAllBytes(paramSfoPath);
                if (data.Length < HeaderSize) return null;
                if (data[0] != 0x00 || data[1] != 0x50 || data[2] != 0x53 || data[3] != 0x46) return null; // "\0PSF"

                uint keyOffset = BitConverter.ToUInt32(data, 8);
                uint dataOffset = BitConverter.ToUInt32(data, 12);
                int count = BitConverter.ToInt32(data, 16);
                if (count <= 0 || count > 1000) return null;

                for (int i = 0; i < count; i++)
                {
                    int entry = (int)keyOffset + i * 16;
                    if (entry + 16 > data.Length) break;
                    ushort keyOff = BitConverter.ToUInt16(data, entry);
                    ushort fmt = BitConverter.ToUInt16(data, entry + 2);
                    int length = BitConverter.ToInt32(data, entry + 4);
                    int valueOff = BitConverter.ToInt32(data, entry + 12);

                    int keyStart = (int)keyOffset + keyOff;
                    int keyEnd = keyStart;
                    while (keyEnd < data.Length && data[keyEnd] != 0) keyEnd++;
                    string key = Encoding.ASCII.GetString(data, keyStart, keyEnd - keyStart);

                    if (key != "APP_VER") continue;
                    if (fmt != 0x0204) continue; // PSF string
                    if (length <= 0 || dataOffset + valueOff + length > data.Length) return null;
                    return Encoding.UTF8.GetString(data, (int)dataOffset + valueOff, length).TrimEnd('\0');
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
