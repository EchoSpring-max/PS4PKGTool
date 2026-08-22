using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4PKGTool.Startup;
using PS4PKGTool.Util.Constants;
using PS4PKGTool.Utilities;
using PS4PKGTool.Utilities.Constants;
using PS4PKGTool.Utilities.PkgInspection;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using PS4PKGTool.Utilities.Settings;
using PS4PKGTool.Utilities.Shadps4;

namespace PS4PKGTool.Shell
{
    /// <summary>
    /// Executes a shell request (Explorer right-click integration) against
    /// the EXISTING PS4PKGTool services - no second pipelines, no main form,
    /// no library scan. Only what the requested command needs is touched.
    /// </summary>
    public static class ShellCommands
    {
        /// <summary>Entry point; runs on the UI thread with a live message pump.</summary>
        public static void Run(ShellRequest request)
        {
            string? pathError = ShellCommandRouter.ValidatePaths(request.PackagePaths);
            if (pathError != null)
            {
                MessageBoxHelper.ShowError(pathError, false);
                return;
            }

            switch (request.Kind)
            {
                case ShellCommandKind.Copy:
                    RunCopy(request.Field, request.PackagePaths);
                    break;
                case ShellCommandKind.Rename:
                    RunRename(request.FormatId, request.PackagePaths);
                    break;
                case ShellCommandKind.Validate:
                    RunValidate(request.PackagePaths);
                    break;
                case ShellCommandKind.Extract:
                    RunExtract(request.PackagePaths);
                    break;
                case ShellCommandKind.InstallShadps4:
                    RunInstallShadps4(request.PackagePaths);
                    break;
            }
        }

        /// <summary>Actions that currently accept one package at a time show this instead of ignoring the rest.</summary>
        private static bool GuardSinglePackage(IReadOnlyList<string> paths)
        {
            if (paths.Count <= 1) return true;
            MessageBoxHelper.ShowInformation(
                "This action currently supports one PKG at a time.\n\nThe other selected files were not touched.", false);
            return false;
        }

        // â”€â”€ metadata â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static PkgInspectionSnapshot? ReadMetadata(string path)
        {
            try
            {
                return new PkgInspectionService()
                    .InspectAsync(path, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Shell: could not read PKG metadata for {path}: {ex.Message}");
                return null;
            }
        }

        // â”€â”€ Copy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void RunCopy(string field, IReadOnlyList<string> paths)
        {
            var parts = new List<string>();
            foreach (string path in paths)
            {
                var meta = ReadMetadata(path);
                if (meta == null)
                {
                    MessageBoxHelper.ShowError(
                        "Could not read PKG metadata.\n\n" + path, false);
                    return;
                }
                parts.Add(ExtractField(field, meta));
            }

            string text = field == "info" && parts.Count == 1 ? parts[0] : string.Join(Environment.NewLine, parts);
            try { Clipboard.SetText(text); }
            catch (Exception ex)
            {
                Logger.LogWarning("Shell: clipboard write failed: " + ex.Message);
                MessageBoxHelper.ShowError("Could not write to the clipboard.", false);
                return;
            }

            string label = parts.Count == 1 ? parts[0] : parts.Count + " values";
            using (var confirm = new ShellConfirmForm(
                (field == "info" ? "PKG info copied" : CopyFieldLabel(field) + " copied") + ":\n" + Truncate(label, 60)))
                confirm.ShowDialog();
        }

        internal static string ExtractField(string field, PkgInspectionSnapshot meta) => field switch
        {
            "info" => BuildPackageInfo(meta),
            "title" => meta.Title,
            "titleid" => meta.TitleId,
            "contentid" => meta.ContentId,
            "version" => meta.PackageVersion,
            "appversion" => meta.ApplicationVersion,
            "fullpath" => meta.PackagePath,
            _ => meta.PackagePath,
        };

        internal static string BuildPackageInfo(PkgInspectionSnapshot m)
        {
            var lines = new List<string>
            {
                "Title: " + m.Title,
                "Title ID: " + m.TitleId,
                "Content ID: " + m.ContentId,
                "Version: " + m.PackageVersion,
                "App Version: " + m.ApplicationVersion,
                "Type: " + m.PackageCategory,
                "Size: " + m.PackageSize,
                "Path: " + m.PackagePath,
            };
            return string.Join(Environment.NewLine, lines);
        }

        private static string CopyFieldLabel(string field) => field switch
        {
            "title" => "Title",
            "titleid" => "Title ID",
            "contentid" => "Content ID",
            "version" => "Version",
            "appversion" => "App Version",
            "fullpath" => "Full path",
            _ => "Value",
        };

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value.Substring(0, max) + "...";

        // â”€â”€ Rename â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void RunRename(int formatId, IReadOnlyList<string> paths)
        {
            // Custom format may come from settings or a small dialog - the
            // same placeholder language as the main app.
            string? format = PkgRenameFormats.GetFormat(formatId, SettingsManager.appSettings_.RenameCustomName);
            if (string.IsNullOrWhiteSpace(format))
            {
                if (formatId == PkgRenameFormats.CustomFormatId)
                {
                    using var dlg = new ShellRenameDialog(SettingsManager.appSettings_.RenameCustomName ?? "");
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    format = dlg.FormatValue;
                    SettingsManager.appSettings_.RenameCustomName = format;
                    SettingsManager.SaveSettings(SettingsManager.appSettings_, SettingsManager.SettingFilePath);
                }
                else
                {
                    MessageBoxHelper.ShowWarning("The rename format is not available.", false);
                    return;
                }
            }

            string? lastRenamed = null;
            foreach (string path in paths)
            {
                string? renamed = RenameOne(path, format);
                if (renamed == null) return; // error already reported
                lastRenamed = renamed;
            }

            if (lastRenamed != null)
            {
                using var confirm = new ShellConfirmForm(
                    (paths.Count > 1 ? paths.Count + " PKGs renamed" : "Renamed to") + ":\n" + Path.GetFileName(lastRenamed));
                confirm.ShowDialog();
            }
        }

        /// <summary>Renames one package via the same engine as the Mini Viewer; never overwrites silently.</summary>
        private static string? RenameOne(string sourcePath, string format)
        {
            try
            {
                string destinationFolder = Path.GetDirectoryName(sourcePath) + @"\";
                (_, _, string targetPkg) =
                    PS4PKGTool.Utilities.PkgRename.PkgRenameService.GetNewPKGName(sourcePath, destinationFolder, format);

                if (File.Exists(targetPkg))
                {
                    MessageBoxHelper.ShowWarning(
                        "A file with the target name already exists. The PKG was not renamed.\n\n" +
                        Path.GetFileName(targetPkg), false);
                    return null;
                }

                File.Move(sourcePath, targetPkg);
                Logger.LogInformation($"Shell renamed: {Path.GetFileName(sourcePath)} -> {Path.GetFileName(targetPkg)}");
                return targetPkg;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Shell rename failed: " + ex.Message);
                MessageBoxHelper.ShowError("The PKG could not be renamed: " + ex.Message, false);
                return null;
            }
        }

        // â”€â”€ Validate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void RunValidate(IReadOnlyList<string> paths)
        {
            if (!GuardSinglePackage(paths)) return;
            string path = paths[0];
            string fileName = Path.GetFileName(path);

            // The passcode for this validation. Starts as the standard
            // all-zero default; a rejected code prompts for the real one and
            // retries (max 5 attempts - a typo'd custom passcode re-prompts).
            string passcode = PkgFileListingService.DefaultPasscode;

            ShellOperationForm.Run("Validating PKG", fileName, ReadMetaLine(path), null,
                (progress, ct) => Task.Run(async () =>
                {
                    progress.Report(new ShellOperationProgress("Reading PKG metadata..."));
                    var meta = await new PkgInspectionService()
                        .InspectAsync(path, ct).ConfigureAwait(false);

                    progress.Report(new ShellOperationProgress("Checking PKG structure..."));
                    var listing = await new PkgFileListingService()
                        .ListAsync(path, passcode, ct).ConfigureAwait(false);

                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        if (listing.Succeeded || listing.RestoreFailed
                            || !IsPasscodeFailure(listing.ErrorMessage))
                            break;

                        progress.Report(new ShellOperationProgress("Passcode required..."));
                        string? custom = await PromptForPasscodeAsync().ConfigureAwait(false);
                        if (custom == null)
                            return ShellOperationResult.CancelledResult(
                                "Validation cancelled. The PKG requires a passcode."); // prompt cancelled - close all

                        passcode = custom;
                        progress.Report(new ShellOperationProgress("Checking PKG structure..."));
                        listing = await new PkgFileListingService()
                            .ListAsync(path, passcode, ct).ConfigureAwait(false);
                    }

                    if (listing.Succeeded)
                    {
                        string message =
                            $"{fileName}\n\nWarnings: 0\nErrors: 0\n\nTitle: {meta.Title}\n" +
                            $"Title ID: {meta.TitleId}\nEntries: {listing.Entries.Count}";
                        return new ShellOperationResult(true, message, listing.MalformedLineCount > 0
                            ? $"Malformed listing lines: {listing.MalformedLineCount}" : null);
                    }
                    return new ShellOperationResult(false, "Validation failed:\n" + listing.ErrorMessage);
                }, ct));
        }

        /// <summary>orbis-pub-cmd reports a rejected passcode in its error
        /// output - same detection as the Mini Viewer.</summary>
        internal static bool IsPasscodeFailure(string errorMessage) =>
            !string.IsNullOrWhiteSpace(errorMessage) &&
            errorMessage.IndexOf("passcode", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Shows the passcode prompt over the running shell operation window.
        /// Returns the entered passcode, PkgFileListingService.NoPasscode for
        /// the "no passcode" option, or null when cancelled.
        /// </summary>
        private static async Task<string?> PromptForPasscodeAsync()
        {
            try
            {
                return await ShellOperationForm.RunOnUiAsync(() =>
                {
                    using var prompt = new PasscodePromptForm();
                    if (prompt.ShowDialog() != DialogResult.OK)
                        return null;
                    if (prompt.IsNoPasscode)
                        return PkgFileListingService.NoPasscode;
                    return string.IsNullOrWhiteSpace(prompt.Passcode) ? null : prompt.Passcode;
                }).ConfigureAwait(false);
            }
            catch
            {
                return null; // a failed prompt must not kill the operation reporting
            }
        }

        // â”€â”€ Extract â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void RunExtract(IReadOnlyList<string> paths)
        {
            if (!GuardSinglePackage(paths)) return;
            string path = paths[0];

            var meta = ReadMetadata(path);
            string title = meta?.Title ?? Path.GetFileNameWithoutExtension(path);

            using var fbd = new FolderBrowserDialog
            {
                Description = "Choose where to extract the PKG.",
                ShowNewFolderButton = true,
            };
            if (fbd.ShowDialog() != DialogResult.OK) return;

            string extractLocation = Path.Combine(fbd.SelectedPath, PkgExtractionService.SanitizeFolderName(title));
            string fileName = Path.GetFileName(path);

            // Passcode for this extraction: default all-zero first; when the
            // package rejects it, prompt and retry. A wrong custom passcode
            // re-prompts (max 5 attempts) so a typo does not end the run.
            string passcode = PkgFileListingService.DefaultPasscode;

            ShellOperationForm.Run("Extracting PKG", title, MetaLine(meta), extractLocation,
                (progress, ct) => Task.Run(async () =>
                {
                    var p = new Progress<string>(s => progress.Report(new ShellOperationProgress(s)));
                    var fileProgress = new Progress<(int Current, int Total, string CurrentFile)>(entry =>
                    {
                        if (entry.Total <= 0)
                            return;

                        int completed = Math.Min(entry.Current + 1, entry.Total);
                        int percent = (int)Math.Round(completed * 100.0 / entry.Total);
                        progress.Report(new ShellOperationProgress(
                            $"Extracting {completed}/{entry.Total}: {entry.CurrentFile}", percent));
                    });
                    var (succeeded, message) = await new PkgExtractionService(passcode: passcode)
                        .ExtractFullAsync(path, extractLocation, p, ct, fileProgress).ConfigureAwait(false);

                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        if (succeeded || !IsPasscodeFailure(message))
                            break;

                        progress.Report(new ShellOperationProgress("Passcode required..."));
                        string? custom = await PromptForPasscodeAsync().ConfigureAwait(false);
                        if (custom == null)
                            return ShellOperationResult.CancelledResult(
                                "Extraction cancelled. The PKG requires a passcode."); // prompt cancelled - close all

                        passcode = custom;
                        progress.Report(new ShellOperationProgress("Extracting game files..."));
                        (succeeded, message) = await new PkgExtractionService(passcode: passcode)
                            .ExtractFullAsync(path, extractLocation, p, ct, fileProgress).ConfigureAwait(false);
                    }

                    if (succeeded)
                        return new ShellOperationResult(true, "Extraction complete", null, extractLocation);
                    return new ShellOperationResult(false, message);
                }, ct));
        }

        // â”€â”€ Install to shadPS4 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void RunInstallShadps4(IReadOnlyList<string> paths)
        {
            if (!GuardSinglePackage(paths)) return;
            string path = paths[0];

            var meta = ReadMetadata(path);
            if (meta == null)
            {
                MessageBoxHelper.ShowError("Could not read PKG metadata.", false);
                return;
            }

            string titleId = meta.TitleId;
            string title = meta.Title;
            bool isGame = string.Equals(meta.PackageCategory, PKGCategory.GAME, StringComparison.OrdinalIgnoreCase);
            bool isPatch = string.Equals(meta.PackageCategory, PKGCategory.PATCH, StringComparison.OrdinalIgnoreCase);
            if (!isGame && !isPatch)
            {
                MessageBoxHelper.ShowWarning(
                    "Select a base game or update (patch) PKG. DLC/addon installation is not supported yet.", false);
                return;
            }

            var settings = SettingsManager.appSettings_;
            bool confirmed = false;

            // No known shadPS4 compatibility status: warn (this dialog also
            // doubles as the install confirmation), same as the main window.
            string compat = Shadps4Compat.Lookup(titleId, settings.Shadps4Os);
            if (string.IsNullOrWhiteSpace(compat))
            {
                var compatChoice = AppMessageBox.Show("shadPS4",
                    $"No shadPS4 compatibility status is known for {title} ({titleId}).\n\n" +
                    "The game may not run in the emulator. Continue with the installation?",
                    AppMessageType.Warning, AppMessageButtons.YesNo);
                if (compatChoice != DialogResult.Yes) return;
                confirmed = true;
            }

            string library = settings.Shadps4InstallDirectory?.Trim() ?? "";
            if (string.IsNullOrEmpty(library) || !Directory.Exists(library))
            {
                using var fbd = new FolderBrowserDialog
                {
                    Description = "Choose the shadPS4 game install folder.",
                    ShowNewFolderButton = true,
                };
                if (fbd.ShowDialog() != DialogResult.OK) return;
                library = fbd.SelectedPath;
            }

            string finalDir = Path.Combine(library, titleId);

            // Patch without a base game cannot boot - warn, do not block
            // (same policy as Main.InstallToShadps4Library).
            bool hasBase = Directory.Exists(finalDir) && File.Exists(Path.Combine(finalDir, "eboot.bin"));
            if (isPatch && !hasBase)
            {
                var warnBase = AppMessageBox.Show("shadPS4",
                    $"No base game detected for {titleId} in\n{library}\n\n" +
                    "A patch alone cannot boot, it only updates the base game's files, which are not installed yet.\n\n" +
                    "Install the patch anyway?",
                    AppMessageType.Warning, AppMessageButtons.YesNo);
                if (warnBase != DialogResult.Yes) return;
                confirmed = true;
            }

            bool replace = false;
            if (Directory.Exists(finalDir))
            {
                string installedVer = ParamSfoReader.ReadAppVersion(Path.Combine(finalDir, "sce_sys", "param.sfo")) ?? "unknown";
                string selectedVer = meta.ApplicationVersion;

                if (Shadps4Launcher.IsEmulatorRunning())
                {
                    var warn = AppMessageBox.Show("shadPS4",
                        "shadPS4 is currently running.\n\nModifying an installed game while the emulator is using it may fail or leave inconsistent files.\n\nContinue?",
                        AppMessageType.Warning, AppMessageButtons.YesNo);
                    if (warn != DialogResult.Yes) return;
                }

                if (isPatch)
                {
                    var mergeChoice = AppMessageBox.Show("shadPS4",
                        $"Game already installed\n\nInstalled version: {installedVer}\nThis patch: {selectedVer}\n\nThe patch will be merged into the existing installation. Continue?",
                        AppMessageType.Info, AppMessageButtons.YesNo);
                    if (mergeChoice != DialogResult.Yes) return;
                    confirmed = true;
                }
                else
                {
                    var replaceChoice = AppMessageBox.Show("shadPS4",
                        $"Game already installed\n\nInstalled version: {installedVer}\nSelected PKG: {selectedVer}\n\nReplace the existing installation?",
                        AppMessageType.Info, AppMessageButtons.YesNo);
                    if (replaceChoice != DialogResult.Yes) return;
                    replace = true;
                    confirmed = true;
                }
            }

            if (!confirmed)
            {
                var go = AppMessageBox.Show("shadPS4",
                    $"Install {title} ({titleId}) into\n{library}?",
                    AppMessageType.Info, AppMessageButtons.YesNo);
                if (go != DialogResult.Yes) return;
            }

            // All safety decisions are done - the SAME install service the
            // Manager uses (same in-process extraction pipeline), presented
            // through the shell operation window. The Manager is never
            // opened. A passcode-protected package prompts and retries
            // the whole install (max 5 attempts; a wrong custom passcode
            // re-prompts so a typo does not end the run).
            string passcode = PkgFileListingService.DefaultPasscode;
            ShellOperationForm.Run("Installing to shadPS4", title,
                $"{titleId} Â· {(isPatch ? "Update" : "Base Game")} Â· v{meta.ApplicationVersion}", finalDir,
                (progress, ct) => Task.Run(() =>
                {
                    var stageProgress = new Progress<string>(s => progress.Report(
                        new ShellOperationProgress(s, null, !s.StartsWith("Finalizing", StringComparison.Ordinal))));
                    var fileProgress = new Progress<(int Current, int Total, string CurrentFile)>(entry =>
                    {
                        if (entry.Total <= 0)
                            return;

                        int completed = Math.Min(entry.Current + 1, entry.Total);
                        int percent = (int)Math.Round(completed * 100.0 / entry.Total);
                        progress.Report(new ShellOperationProgress(
                            $"Extracting {completed}/{entry.Total}: {entry.CurrentFile}", percent));
                    });
                    var svc = new Shadps4InstallService
                    {
                        Passcode = passcode,
                    };
                    var result = svc.Install(
                        path, titleId, library, replace, stageProgress, ct, mergeIntoExisting: isPatch,
                        fileProgress: fileProgress);

                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        if (result.Status != Shadps4InstallStatus.ExtractionFailed
                            || !IsPasscodeFailure(result.Message))
                            break;

                        progress.Report(new ShellOperationProgress("Passcode required..."));
                        string? custom = PromptForPasscodeAsync().GetAwaiter().GetResult();
                        if (custom == null)
                            return Task.FromResult(ShellOperationResult.CancelledResult(
                                "Installation cancelled. The PKG requires a passcode.")); // prompt cancelled - close all

                        passcode = custom;
                        svc.Passcode = custom;
                        result = svc.Install(
                            path, titleId, library, replace, stageProgress, ct, mergeIntoExisting: isPatch,
                            fileProgress: fileProgress);
                    }

                    return Task.FromResult(result.Status switch
                    {
                        Shadps4InstallStatus.Success => new ShellOperationResult(true, "Installation complete", null, finalDir),
                        Shadps4InstallStatus.Cancelled => new ShellOperationResult(false, "Installation cancelled"),
                        _ => new ShellOperationResult(false, string.IsNullOrWhiteSpace(result.Message)
                            ? "Installation failed." : result.Message),
                    });
                }, ct));
        }

        // â”€â”€ display helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static string? ReadMetaLine(string path)
        {
            var meta = ReadMetadata(path);
            return meta == null ? null : $"{meta.TitleId} Â· {meta.PackageCategory}";
        }

        private static string MetaLine(PkgInspectionSnapshot? meta)
            => meta == null ? "" : $"{meta.TitleId} Â· {meta.PackageCategory} Â· v{meta.ApplicationVersion}";
    }
}
