using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PS4PKGTool.Shell
{
    /// <summary>One value to write into the per-user registry.</summary>
    public sealed record RegistryEntry(string KeyPath, string? ValueName, string? Value)
    {
        public const string DefaultValueName = "";

        /// <summary>Creates a bare key with no values.</summary>
        public static RegistryEntry KeyOnly(string keyPath) => new(keyPath, null, null);

        /// <summary>Creates a REG_DWORD value (e.g. CommandFlags).</summary>
        public static RegistryEntry DWord(string keyPath, string valueName, int value)
            => new(keyPath, valueName, null) { DWordValue = value };

        /// <summary>Non-null only for REG_DWORD values (see DWord()).</summary>
        public int? DWordValue { get; init; }

        /// <summary>True when this entry writes a REG_DWORD.</summary>
        public bool IsDWord => DWordValue.HasValue;
    }

    /// <summary>
    /// Optional per-user File Explorer integration for .pkg files (HKCU only,
    /// no admin). Context-menu registration and the optional default-program
    /// association are kept separate so either can be removed independently.
    ///
    /// Registration lives ONLY under SystemFileAssociations (verified: this is
    /// the single tree Explorer renders on Windows 11; the Classes\.pkg
    /// mirror used by earlier versions never rendered here). ONE flat
    /// cascade - nested cascades truncate to ~5 items in the Win11 menu
    /// (verified on build 26200), so Rename/Copy formats live directly in
    /// the root cascade, grouped by grayed header items with separators:
    ///   HKCU\Software\Classes\SystemFileAssociations\.pkg\shell\PS4PKGTool
    ///       MUIVerb, Icon, MultiSelectModel, ExtendedSubCommandsKey = itself
    ///       shell\01aHdrRename  MUIVerb="RENAME", CommandStateHandler+attrs
    ///                            (renders disabled; click does nothing, menu
    ///                             stays open), CommandFlags=0x20 (separator)
    ///       shell\01bRename01..05, 01bRename11   (curated formats + custom)
    ///       shell\02aHdrCopy    MUIVerb="COPY",  (disabled header)
    ///       shell\02bCopy01..03                    (Title/Title ID/Content ID)
    ///       shell\03aHdrOps     MUIVerb="PACKAGE",(disabled header)
    ///       shell\03bOps01..03                    (Validate/Extract/Install)
    ///
    /// Header "disabled" mechanism (verified on Windows 11 26200): the same
    /// registry-only state pattern the system's own disabled Encrypt/Decrypt
    /// verbs use - CommandStateHandler = system EDP handler CLSID,
    /// AttributeMask/AttributeValue = a pair that never matches the file,
    /// ShowAsDisabledIfHidden = "". The handler reports the verb disabled,
    /// so it renders grayed, is not invokable, and clicking it does not
    /// dismiss the menu. No CommandStateHandler = the item would render
    /// enabled and a click would close the menu.
    ///
    /// ExtendedSubCommandsKey is a VALUE pointing back at its own verb key
    /// (the documented Microsoft form). Writing it as a bare KEY renders a
    /// dead menu item with no submenu - the original bug.
    ///
    /// Windows 11 places the menu under "Show more options" - accepted.
    /// Removal deletes ONLY the PS4PKGTool verb tree (plus the legacy mirror
    /// location, in case an old install left one behind).
    /// </summary>
    public static class ShellRegistry
    {
        /// <summary>Root of the working registration (the only tree that renders).</summary>
        public const string PkgVerbKeySystemFileAssociations =
            @"Software\Classes\SystemFileAssociations\.pkg\shell\PS4PKGTool";

        /// <summary>
        /// System EDP command-state handler CLSID - the same one the shell's
        /// own disabled Encrypt/Decrypt verbs use. It evaluates the verb's
        /// AttributeMask/AttributeValue pair against the file and reports the
        /// verb hidden/disabled accordingly.
        /// </summary>
        internal const string DisabledStateHandlerClsid = "{5B6D1451-B1E1-4372-90F5-88E541B4DAB9}";

        /// <summary>
        /// A mask/value pair that never matches a file's attributes, so the
        /// state handler ALWAYS reports the verb disabled. Verified on
        /// Windows 11 26200: an item in this state renders grayed, clicking
        /// it does nothing and the context menu stays open.
        /// </summary>
        internal const int NeverMatchingAttributeMask = 0x2000;
        internal const int NeverMatchingAttributeValue = 0x4000;

        /// <summary>Legacy mirror location - never rendered on Windows 11,
        /// written by old builds. Removed whenever we install or remove.</summary>
        private const string PkgVerbKey = @"Software\Classes\.pkg\shell\PS4PKGTool";

        /// <summary>HKCU-relative registry path of the verb root, without the
        /// Software\Classes prefix - the form ExtendedSubCommandsKey expects
        /// (the shell resolves it against HKEY_CLASSES_ROOT, which merges
        /// HKCU\Software\Classes).</summary>
        private const string VerbKeyHkcrPath =
            @"SystemFileAssociations\.pkg\shell\PS4PKGTool";

        /// <summary>The executable Explorer launches; the shell-mode entry point.</summary>
        public static string ExecutablePath { get; } =
            Path.Combine(AppContext.BaseDirectory, "PS4 PKG Tool.exe");

        /// <summary>Use the application icon already embedded in the executable.</summary>
        public static string IconPath { get; } =
            ExecutablePath + ",0";

        /// <summary>
        /// Test hook: redirects the verb root to a throwaway HKCU path so
        /// Install/Remove round-trip tests never touch the live registration.
        /// Production leaves this null. Like OrbisSafePkgOperation's
        /// StagingRootOverride - set in the test, cleared in cleanup.
        /// </summary>
        internal static string? VerbRootOverride = null;

        /// <summary>HKCU path of the verb root actually used (test override aware).</summary>
        private static string VerbRoot => VerbRootOverride ?? PkgVerbKeySystemFileAssociations;

        public static bool IsInstalled()
        {
            try
            {
                using var sfa = Registry.CurrentUser.OpenSubKey(VerbRoot);
                return sfa != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Writes the .pkg context menu. Old registrations (including the
        /// broken pre-cascade shapes and the dead mirror tree) are deleted
        /// first so Install is always a clean, idempotent rebuild.
        /// </summary>
        public static void Install(string? executablePath = null)
        {
            Remove();
            string exe = executablePath ?? ExecutablePath;
            foreach (RegistryEntry entry in BuildEntries(exe))
                WriteEntry(entry);
        }

        /// <summary>Deletes ONLY the PS4PKGTool verb trees; nothing else.
        /// The legacy Classes\.pkg mirror is only removed when operating on
        /// the real root (a test-scoped override never touches it).</summary>
        public static void Remove()
        {
            // Idempotent by design: uninstall must succeed even when the
            // keys are already gone or ACL-locked - hence best-effort.
            try { Registry.CurrentUser.DeleteSubKeyTree(VerbRoot, throwOnMissingSubKey: false); }
            catch { /* best-effort: idempotent uninstall */ }
            if (VerbRootOverride == null)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(PkgVerbKey, throwOnMissingSubKey: false); }
                catch { /* best-effort: idempotent uninstall */ }
            }
        }

        /// <summary>Quoted command line for one action, with the "%1" package placeholder.</summary>
        public static string BuildCommandLine(string executablePath, string verb, string? argument = null)
        {
            string exe = "\"" + executablePath + "\"";
            string action = argument == null ? $"{exe} --shell {verb}" : $"{exe} --shell {verb} {argument}";
            return action + " \"%1\"";
        }

        /// <summary>
        /// Rename format ids shown in the Explorer menu - a curated subset of
        /// the shared PkgRenameFormats list (formats 6-10 are app-only: the
        /// context menu stays short). Ids are the fixed shared ids, so the
        /// command lines keep using PkgRenameFormats.GetFormat unchanged.
        /// </summary>
        private static readonly HashSet<int> ShellRenameFormatIds = new() { 1, 2, 3, 4, 5 };

        /// <summary>
        /// Every registry value the integration writes, as pure data
        /// (testable without touching the real registry).
        ///
        /// Layout - ONE flat cascade (nested cascades truncate to ~5 items
        /// in the Windows 11 context menu, verified on build 26200):
        ///   PS4 PKG Tool â–¸   â”€â”€â”€â”€â”€
        ///                    RENAME            (grayed header + separator)
        ///                    5 curated formats + Custom Format...
        ///                    â”€â”€â”€â”€â”€
        ///                    COPY              (grayed header + separator)
        ///                    Package Info ... Full Path (7 fields)
        ///                    â”€â”€â”€â”€â”€
        ///                    PACKAGE           (grayed header + separator)
        ///                    Validate / Extract / Install
        ///
        /// ORDERING: static cascade children render in ALPHABETICAL
        /// KEY-NAME order (no registry value changes it), so every verb key
        /// carries an ordinal prefix ("01a...", "01b...", "02a...") that
        /// makes the sort equal the display order above.
        /// </summary>
        public static IReadOnlyList<RegistryEntry> BuildEntries(string executablePath)
        {
            var entries = new List<RegistryEntry>();

            void Command(string keyPath, string display, string verb, string? argument = null)
            {
                entries.Add(new RegistryEntry(keyPath, "MUIVerb", display));
                entries.Add(new RegistryEntry(keyPath + @"\command", RegistryEntry.DefaultValueName,
                    BuildCommandLine(executablePath, verb, argument)));
            }

            // A grayed, non-clickable section header that KEEPS THE MENU OPEN
            // when clicked. Verified shape (Windows 11 26200): the same
            // registry-only state pattern the system's own disabled
            // Encrypt/Decrypt verbs use -
            //   CommandStateHandler  = system EDP handler CLSID
            //   AttributeMask/Value  = a pair that never matches the file
            //   ShowAsDisabledIfHidden = (empty string)
            // The state handler evaluates the attributes, finds no match,
            // and reports the verb disabled. A disabled item is not
            // invokable, so clicking it neither runs a command nor
            // dismisses the menu. ECF_SEPARATORBEFORE (CommandFlags 0x20)
            // draws the separator line above the header.
            void Header(string keyPath, string display)
            {
                entries.Add(new RegistryEntry(keyPath, "MUIVerb", display));
                entries.Add(new RegistryEntry(keyPath, "CommandStateHandler", DisabledStateHandlerClsid));
                entries.Add(RegistryEntry.DWord(keyPath, "AttributeMask", NeverMatchingAttributeMask));
                entries.Add(RegistryEntry.DWord(keyPath, "AttributeValue", NeverMatchingAttributeValue));
                entries.Add(new RegistryEntry(keyPath, "ShowAsDisabledIfHidden", ""));
                entries.Add(RegistryEntry.DWord(keyPath, "CommandFlags", 0x20));
                // A command key matches the verified working shape (the
                // state handler disables the verb, so this never runs).
                entries.Add(new RegistryEntry(keyPath + @"\command", RegistryEntry.DefaultValueName,
                    BuildCommandLine(executablePath, "validate")));
            }

            string root = VerbRoot;

            // The root itself is a cascade: ExtendedSubCommandsKey VALUE
            // pointing at the verb key, children under <root>\shell\<verb>.
            // Without this value the item renders but opens nothing.
            // The HKCR-relative path the value must carry: the real root is
            // "SystemFileAssociations\.pkg\shell\PS4PKGTool"; a test override
            // echoes its own HKCU path stripped of the Software\Classes prefix.
            string rootHkcr = VerbRootOverride == null
                ? VerbKeyHkcrPath
                : VerbRootOverride.StartsWith(@"Software\Classes\", StringComparison.OrdinalIgnoreCase)
                    ? VerbRootOverride.Substring(@"Software\Classes\".Length)
                    : VerbRootOverride;
            entries.Add(new RegistryEntry(root, "MUIVerb", "PS4 PKG Tool"));
            entries.Add(new RegistryEntry(root, "ExtendedSubCommandsKey", rootHkcr));
            entries.Add(new RegistryEntry(root, "Icon", IconPath));
            // One invocation per selected file; the shell command handler
            // accepts any number of paths.
            entries.Add(new RegistryEntry(root, "MultiSelectModel", "Player"));

            string verbs = root + @"\shell";

            // â”€â”€ 01: RENAME â”€â”€ 01a header, 01b formats (key-name sort = display order)
            Header(verbs + @"\01aHdrRename", "RENAME");
            foreach (var (id, format) in PS4PKGTool.Utilities.Constants.PkgRenameFormats.Predefined)
            {
                if (!ShellRenameFormatIds.Contains(id)) continue; // app-only format
                Command(verbs + @"\01bRename" + id.ToString("00"),
                    PS4PKGTool.Utilities.Constants.PkgRenameFormats.DisplayLabel(format), "rename", id.ToString());
            }
            Command(verbs + @"\01bRename" + PS4PKGTool.Utilities.Constants.PkgRenameFormats.CustomFormatId.ToString("00"),
                "Custom Format...", "rename",
                PS4PKGTool.Utilities.Constants.PkgRenameFormats.CustomFormatId.ToString());

            // â”€â”€ 02: COPY â”€â”€ 02a header, 02b fields (curated subset)
            Header(verbs + @"\02aHdrCopy", "COPY");
            var copyActions = new (string Display, string Field)[]
            {
                ("Title", "title"),
                ("Title ID", "titleid"),
                ("Content ID", "contentid"),
            };
            for (int i = 0; i < copyActions.Length; i++)
            {
                var (display, field) = copyActions[i];
                Command(verbs + @"\02bCopy" + (i + 1).ToString("00"), display, "copy", field);
            }

            // â”€â”€ 03: PACKAGE â”€â”€ 03a header, 03b operations
            Header(verbs + @"\03aHdrOps", "PACKAGE");
            Command(verbs + @"\03bOps01", "Validate PKG", "validate");
            Command(verbs + @"\03bOps02", "Extract PKG...", "extract");
            Command(verbs + @"\03bOps03", "Install to shadPS4...", "install-shadps4");

            return entries;
        }

        private static void WriteEntry(RegistryEntry entry)
        {
            try
            {
                string keyPath = entry.KeyPath;
                string? parent = keyPath.Contains('\\')
                    ? keyPath.Substring(0, keyPath.LastIndexOf('\\'))
                    : null;
                if (parent != null)
                    Registry.CurrentUser.CreateSubKey(parent);

                using var key = Registry.CurrentUser.CreateSubKey(keyPath);
                // KeyOnly entries create the bare key; everything else sets
                // one value ("" = the key's (default) value).
                if (entry.ValueName != null)
                {
                    if (entry.IsDWord)
                        key?.SetValue(entry.ValueName, entry.DWordValue!.Value, RegistryValueKind.DWord);
                    else
                        key?.SetValue(entry.ValueName, entry.Value ?? "");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("ShellRegistry: could not write " + entry.KeyPath + " (" + entry.ValueName + "): " + ex.Message);
            }
        }

    }
}
