using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PS4PKGTool
{
    /// <summary>
    /// Staged PKG Recovery - shows package files left behind by previous
    /// staging operations (crashes, kills, power loss, older versions) and
    /// restores only those whose exact original path is known and free.
    /// Terminology: "recovery", never "cleanup" - a staged PKG may be the
    /// only remaining copy of the package. There is deliberately no
    /// destructive "Delete All" action here.
    /// </summary>
    public partial class StagedPkgRecoveryForm : DarkUI.Forms.DarkForm
    {
        private readonly IReadOnlyList<string> _scanRoots;
        private List<StagedPkgRecoveryItem> _items;

        public StagedPkgRecoveryForm(List<StagedPkgRecoveryItem> items, IReadOnlyList<string> scanRoots)
        {
            InitializeComponent();
            Icon = Helper.AppIcon;
            _items = items;
            _scanRoots = scanRoots;
            Populate();
        }

        /// <summary>Opens the recovery dialog; the only entry point (startup + Settings).</summary>
        public static void ShowRecoveryDialog(IWin32Window? owner, List<StagedPkgRecoveryItem> items,
            IReadOnlyList<string> scanRoots)
        {
            using var form = new StagedPkgRecoveryForm(items, scanRoots);
            form.ShowDialog(owner);
        }

        private void Populate()
        {
            lvItems.BeginUpdate();
            try
            {
                lvItems.Items.Clear();
                foreach (StagedPkgRecoveryItem item in _items)
                {
                    var row = new ListViewItem(item.Title.Length > 0 ? item.Title : Path.GetFileName(item.CurrentPath));
                    row.SubItems.Add(item.TitleId);
                    row.SubItems.Add(item.PkgType);
                    row.SubItems.Add(item.Size > 0 ? Helper.RoundBytes(item.Size) : "");
                    row.SubItems.Add(StatusText(item));
                    row.SubItems.Add(item.CurrentPath);
                    row.SubItems.Add(item.OriginalPath);
                    row.Tag = item;
                    // Full details in the tooltip - the Status column truncates long text.
                    row.ToolTipText =
                        $"Status: {StatusText(item)}\n" +
                        $"Current: {item.CurrentPath}\n" +
                        $"Original: {(item.OriginalPath.Length > 0 ? item.OriginalPath : "Unknown")}\n" +
                        (item.ConflictReason.Length > 0 ? item.ConflictReason + "\n" : "") +
                        (item.CorrelationEvidence.Length > 0 ? item.CorrelationEvidence + "\n" : "");
                    lvItems.Items.Add(row);
                }
            }
            finally { lvItems.EndUpdate(); }
            UpdateCounts();
            lvItems_SelectedIndexChanged(this, EventArgs.Empty);
        }

        private void UpdateCounts()
        {
            int recoverable = 0;
            foreach (StagedPkgRecoveryItem item in _items)
                if (item.CanAutoRecover) recoverable++;
            lblCounts.Text = $"{_items.Count} staged item(s), {recoverable} safely recoverable.";
        }

        private static string StatusText(StagedPkgRecoveryItem item) => item.Status switch
        {
            StagedPkgRecoveryStatus.ReadyToRecover => "Ready to recover",
            StagedPkgRecoveryStatus.Conflict => "Conflict",
            StagedPkgRecoveryStatus.UnknownOriginal => "Original filename unavailable",
            StagedPkgRecoveryStatus.MalformedMetadata => "Recovery metadata unreadable",
            StagedPkgRecoveryStatus.MissingPkg => "Package missing",
            StagedPkgRecoveryStatus.ActiveOperation => "In use by a running operation",
            StagedPkgRecoveryStatus.MatchedLegacyResidue => "Legacy package ready to recover",
            _ => item.Status.ToString(),
        };

        /// <summary>Per-item button enablement: Recover Selected only for
        /// recoverable items, Manual Recovery only for one unmatched orphan,
        /// Remove Stale Record only for a genuinely unresolved MissingPkg
        /// whose directory holds only known recovery files.</summary>
        private void lvItems_SelectedIndexChanged(object sender, EventArgs e)
        {
            List<StagedPkgRecoveryItem> selected = SelectedItems();
            bool anyRecoverable = false;
            foreach (StagedPkgRecoveryItem item in selected)
            {
                if (!item.CanAutoRecover)
                {
                    anyRecoverable = false;
                    break;
                }
                anyRecoverable = true;
            }

            btnRecoverSelected.Enabled = selected.Count > 0 && anyRecoverable;

            bool singleUnknownOrphan = selected.Count == 1
                && selected[0].RecoveryType == StagedPkgRecoveryType.LegacyStandalone
                && selected[0].Status == StagedPkgRecoveryStatus.UnknownOriginal;
            btnManualRecovery.Enabled = singleUnknownOrphan;

            bool removableMissingPkg = selected.Count == 1
                && selected[0].Status == StagedPkgRecoveryStatus.MissingPkg
                && !string.IsNullOrEmpty(selected[0].StagingDirectory)
                && StagedPkgRecoveryScanner.IsStaleRecordRemovable(selected[0].StagingDirectory);
            btnRemoveStale.Enabled = removableMissingPkg;
        }

        private List<StagedPkgRecoveryItem> SelectedItems()
        {
            var items = new List<StagedPkgRecoveryItem>();
            foreach (ListViewItem row in lvItems.SelectedItems)
                if (row.Tag is StagedPkgRecoveryItem item)
                    items.Add(item);
            return items;
        }

        private void btnRecoverSelected_Click(object sender, EventArgs e)
        {
            List<StagedPkgRecoveryItem> selected = SelectedItems();
            if (selected.Count == 0)
            {
                MessageBoxHelper.ShowWarning("Select one or more staged packages first.", false);
                return;
            }
            RunRecovery(selected);
        }

        private void btnRecoverAll_Click(object sender, EventArgs e)
        {
            int recoverable = 0;
            foreach (StagedPkgRecoveryItem item in _items)
                if (item.CanAutoRecover)
                    recoverable++;
            if (recoverable == 0)
            {
                MessageBoxHelper.ShowInformation(
                    "No staged package can be recovered automatically.\nConflicts and items with an unknown original filename are left untouched.",
                    false);
                return;
            }

            // Batch recovery automatically unwinds nested legacy staging chains:
            // scan -> recover everything safe -> re-scan until nothing new is
            // recoverable (bounded by a max-pass and a zero-progress guard).
            try
            {
                StagedPkgRecoveryScanner.StagedPkgRecoveryBatchResult batch =
                    StagedPkgRecoveryScanner.RecoverAllSafe(_scanRoots);

                string summary = $"Recovered: {batch.Recovered} | Failed: {batch.Failed}" +
                    (batch.Passes > 1 ? $" | Recovery passes: {batch.Passes}" : "");
                if (batch.Failures.Count > 0)
                    MessageBoxHelper.ShowWarning(summary + "\n\n" + string.Join("\n", batch.Failures.Take(8)), false);
                else
                    MessageBoxHelper.ShowInformation(summary, false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Staged PKG batch recovery failed: " + ex.Message);
                MessageBoxHelper.ShowError("The batch recovery failed:\n" + ex.Message, false);
            }

            // Refresh the list from disk so the dialog state always matches reality.
            try { _items = StagedPkgRecoveryScanner.Scan(_scanRoots); }
            catch (Exception ex) { Logger.LogWarning("Staged PKG rescan failed: " + ex.Message); }
            Populate();
        }

        private void RunRecovery(List<StagedPkgRecoveryItem> items)
        {
            int recovered = 0, skipped = 0, failed = 0;
            var failures = new List<string>();
            foreach (StagedPkgRecoveryItem item in items)
            {
                if (!item.CanAutoRecover)
                {
                    skipped++;
                    continue;
                }
                (bool ok, string error) = StagedPkgRecoveryScanner.Recover(item);
                if (ok)
                {
                    recovered++;
                    Logger.LogInformation($"Staged PKG recovery: restored {Path.GetFileName(item.OriginalPath)} to {Path.GetDirectoryName(item.OriginalPath)}");
                }
                else
                {
                    failed++;
                    failures.Add(Path.GetFileName(item.CurrentPath) + ": " + error);
                    Logger.LogWarning("Staged PKG recovery failed: " + error);
                }
            }

            // Refresh the list from disk so the dialog state always matches reality.
            try { _items = StagedPkgRecoveryScanner.Scan(_scanRoots); }
            catch (Exception ex) { Logger.LogWarning("Staged PKG rescan failed: " + ex.Message); }
            Populate();

            string summary = $"Recovered: {recovered} | Skipped: {skipped} | Failed: {failed}";
            if (failures.Count > 0)
                MessageBoxHelper.ShowWarning(summary + "\n\n" + string.Join("\n", failures.Take(8)), false);
            else
                MessageBoxHelper.ShowInformation(summary, false);
        }

        private void btnReview_Click(object sender, EventArgs e)
        {
            List<StagedPkgRecoveryItem> selected = SelectedItems();
            if (selected.Count == 0)
            {
                MessageBoxHelper.ShowWarning("Select a staged package to review.", false);
                return;
            }
            StagedPkgRecoveryItem item = selected[0];

            string details;
            if (item.RecoveryType == StagedPkgRecoveryType.LegacyRecoveryCandidate)
            {
                // Combined legacy pair: show the correlation evidence and paths.
                details =
                    $"Title:    {(item.Title.Length > 0 ? item.Title : "(unknown)")}\n" +
                    $"Title ID: {(item.TitleId.Length > 0 ? item.TitleId : "(unknown)")}\n" +
                    $"Content:  {(item.ContentId.Length > 0 ? item.ContentId : "(unknown)")}\n" +
                    $"Type:     {(item.PkgType.Length > 0 ? item.PkgType : "(unknown)")}\n" +
                    $"Size:     {(item.Size > 0 ? Helper.RoundBytes(item.Size) : "(unknown)")}\n\n" +
                    $"Recovery type: Legacy recovery record + staged package\n" +
                    $"Correlation:   {item.CorrelationConfidence}\n" +
                    $"Evidence:      {item.CorrelationEvidence}\n\n" +
                    $"Recovery record:\n{item.RecoveryRecordPath}\n\n" +
                    $"Current PKG:\n{item.CurrentPath}\n\n" +
                    $"Recorded original:\n{item.OriginalPath}\n\n" +
                    $"Status: {StatusText(item)}" +
                    (item.ConflictReason.Length > 0 ? "\n\n" + item.ConflictReason : "") +
                    (item.CanAutoRecover
                        ? "\n\nThis package was matched to an older recovery record using its recovery path and package metadata. The recorded original path is known, so it can be restored safely."
                        : "\n\nThis item is shown for review only - no automatic action is available.");
            }
            else if (item.Status == StagedPkgRecoveryStatus.MissingPkg)
            {
                details =
                    $"Recovery directory:\n{item.StagingDirectory}\n\n" +
                    $"Recorded original path:\n{(item.RecordedOriginalPath ?? "Unknown")}\n\n" +
                    $"Expected staged package:\n{(item.CurrentPath.Length > 0 ? item.CurrentPath : "unknown")}\n\n" +
                    $"Status: {StatusText(item)}\n\n" +
                    $"Metadata path:\n{(item.StagingDirectory != null ? Path.Combine(item.StagingDirectory, OrbisTempRecovery.SidecarName) : "unknown")}\n\n" +
                    (item.ConflictReason.Length > 0 ? item.ConflictReason + "\n\n" : "") +
                    "Recovery metadata was found, but no staged PKG could be located or safely matched to this record." +
                    (item.CorrelationEvidence.Length > 0 ? "\n\n" + item.CorrelationEvidence : "");
            }
            else
            {
                details =
                    $"Title:    {(item.Title.Length > 0 ? item.Title : "(unknown)")}\n" +
                    $"Title ID: {(item.TitleId.Length > 0 ? item.TitleId : "(unknown)")}\n" +
                    $"Content:  {(item.ContentId.Length > 0 ? item.ContentId : "(unknown)")}\n" +
                    $"Type:     {(item.PkgType.Length > 0 ? item.PkgType : "(unknown)")}\n" +
                    $"Size:     {(item.Size > 0 ? Helper.RoundBytes(item.Size) : "(unknown)")}\n\n" +
                    $"Current:\n{item.CurrentPath}\n\n" +
                    $"Original:\n{(item.OriginalPath.Length > 0 ? item.OriginalPath : "Unknown")}\n\n" +
                    $"Status:\n{StatusText(item)}" +
                    (item.ConflictReason.Length > 0 ? "\n\n" + item.ConflictReason : "") +
                    (item.CanAutoRecover
                        ? ""
                        : "\n\nThis item is shown for review only - no automatic action is available.");
            }
            MessageBoxHelper.ShowInformation(details, false);
        }

        /// <summary>Manual recovery for an unmatched legacy orphan: the user
        /// explicitly chooses the destination folder and filename - the app
        /// never invents the original name.</summary>
        private void btnManualRecovery_Click(object sender, EventArgs e)
        {
            List<StagedPkgRecoveryItem> selected = SelectedItems();
            if (selected.Count != 1)
                return;
            StagedPkgRecoveryItem item = selected[0];

            string? target = null;
            using (var form = new ManualRecoveryForm(item))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                    return;
                target = form.ChosenDestinationPath;
            }
            if (string.IsNullOrEmpty(target))
                return;

            // Validate again right before the move (state may have changed).
            if (!File.Exists(item.CurrentPath))
            {
                MessageBoxHelper.ShowError("The staged package no longer exists at:\n" + item.CurrentPath, false);
                return;
            }
            if (OrbisSafePkgOperation.IsPathInActiveOperation(item.CurrentPath))
            {
                MessageBoxHelper.ShowError("The staged package is in use by an active operation.", false);
                return;
            }
            if (File.Exists(target))
            {
                MessageBoxHelper.ShowError("The destination already exists - it will not be overwritten:\n" + target, false);
                return;
            }

            try
            {
                File.Move(item.CurrentPath, target);
                if (!File.Exists(target))
                {
                    MessageBoxHelper.ShowError("The move did not produce the destination file. Nothing was deleted.", false);
                    return;
                }
                Logger.LogInformation($"Manual recovery: moved {Path.GetFileName(item.CurrentPath)} to {target}");
                MessageBoxHelper.ShowInformation("The package was moved to:\n" + target, false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Manual recovery failed: " + ex.Message);
                MessageBoxHelper.ShowError("The move failed - the package was preserved at:\n" + item.CurrentPath + "\n\n" + ex.Message, false);
            }

            try { _items = StagedPkgRecoveryScanner.Scan(_scanRoots); }
            catch (Exception ex) { Logger.LogWarning("Staged PKG rescan failed: " + ex.Message); }
            Populate();
        }

        /// <summary>Removes a stale recovery record directory - only enabled
        /// when it contains known metadata and no package.</summary>
        private void btnRemoveStale_Click(object sender, EventArgs e)
        {
            List<StagedPkgRecoveryItem> selected = SelectedItems();
            if (selected.Count != 1 || string.IsNullOrEmpty(selected[0].StagingDirectory))
                return;

            string directory = selected[0].StagingDirectory;
            if (!StagedPkgRecoveryScanner.IsStaleRecordRemovable(directory))
            {
                MessageBoxHelper.ShowWarning(
                    "This recovery record cannot be removed automatically:\n" +
                    "it contains a package, unknown files, or subdirectories.", false);
                return;
            }

            if (MessageBoxHelper.DialogResultYesNo(
                    "Remove the stale recovery record?\n\n" + directory +
                    "\n\nOnly known recovery metadata will be removed - no package files exist in it.") != DialogResult.Yes)
                return;

            (bool removed, string reason) = StagedPkgRecoveryScanner.TryRemoveStaleRecoveryDirectory(directory);
            if (removed)
            {
                Logger.LogInformation("Removed stale recovery record: " + directory);
                MessageBoxHelper.ShowInformation("The stale recovery record was removed.", false);
            }
            else
            {
                Logger.LogWarning("Stale recovery record removal failed: " + reason);
                MessageBoxHelper.ShowError(reason, false);
            }

            try { _items = StagedPkgRecoveryScanner.Scan(_scanRoots); }
            catch (Exception ex) { Logger.LogWarning("Staged PKG rescan failed: " + ex.Message); }
            Populate();
        }

        private void btnIgnore_Click(object sender, EventArgs e) => Close();
    }
}
