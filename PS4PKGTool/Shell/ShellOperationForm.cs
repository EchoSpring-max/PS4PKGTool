using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4PKGTool.Utilities;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Shell
{
    /// <summary>Progress reported by a shell operation backend.</summary>
    public sealed record ShellOperationProgress(string Stage, int? Percent = null, bool Cancellable = true);

    /// <summary>Outcome of a shell operation. Details holds a long/technical text.</summary>
    public sealed record ShellOperationResult(
        bool Succeeded, string Message, string? Details = null, string? OpenFolder = null)
    {
        /// <summary>True when the operation was aborted by the user (e.g.
        /// cancelled the passcode prompt). Treated as a non-error cancel that
        /// closes the operation window without a "Failed" state.</summary>
        public bool Cancelled { get; init; }

        /// <summary>Convenience factory for a user-cancelled operation.</summary>
        public static ShellOperationResult CancelledResult(string message = "Cancelled.")
            => new(false, message) { Cancelled = true };
    }

    /// <summary>
    /// One reusable compact operation window for Explorer shell actions
    /// (Validate, Extract, Install to shadPS4). Hosts a background operation,
    /// shows its real stages (marquee when the backend has no percentage),
    /// allows safe cancellation and ends in a success or failure state with
    /// optional Details / Copy Result / Open Folder actions. It is a plain
    /// modal window - never the full application.
    /// </summary>
    public partial class ShellOperationForm : DarkUI.Forms.DarkForm
    {
        private readonly Func<IProgress<ShellOperationProgress>, CancellationToken, Task<ShellOperationResult>> _operation;
        private readonly CancellationTokenSource _cts = new();
        private bool _finished;

        private ShellOperationForm(
            string title, string? packageLine, string? metaLine, string? destination,
            Func<IProgress<ShellOperationProgress>, CancellationToken, Task<ShellOperationResult>> operation)
        {
            InitializeComponent();
            Text = title;
            lblShellTitle.Text = title;
            lblShellPackage.Text = packageLine ?? "";
            lblShellMeta.Text = metaLine ?? "";
            lblShellDestination.Text = destination ?? "";
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Shown += async (_, _) => await RunOperationAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Runs the operation and shows the window modally. The operation
        /// must observe ct for cancellation and report stages via progress.
        /// </summary>
        public static DialogResult Run(
            string title, string? packageLine, string? metaLine, string? destination,
            Func<IProgress<ShellOperationProgress>, CancellationToken, Task<ShellOperationResult>> operation)
        {
            using var form = new ShellOperationForm(title, packageLine, metaLine, destination, operation);
            return form.ShowDialog();
        }

        /// <summary>
        /// The operation backend's bridge to the UI thread while the modal
        /// window's message loop is pumping. Await this from the background
        /// task to show a modal dialog over the operation window (the
        /// ShowDialog pumps messages, so progress keeps updating).
        /// </summary>
        public static Task<T> RunOnUiAsync<T>(Func<T> uiWork)
        {
            var ctx = SynchronizationContext.Current;
            if (ctx == null)
                return Task.Run(uiWork); // no UI context (unit tests): just run it

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            ctx.Post(_ =>
            {
                try { tcs.SetResult(uiWork()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task;
        }

        private async Task RunOperationAsync()
        {
            using var progress = new UiProgress<ShellOperationProgress>(p =>
            {
                lblShellStage.Text = p.Stage;
                prgShellProgress.Marquee = p.Percent == null;
                if (p.Percent != null)
                    prgShellProgress.Value = Math.Max(prgShellProgress.Minimum, Math.Min(prgShellProgress.Maximum, p.Percent.Value));
                btnShellCancel.Enabled = p.Cancellable && !_finished;
            });

            ShellOperationResult result;
            try
            {
                result = await _operation(progress, _cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                result = new ShellOperationResult(false, "Cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogError("Shell operation failed: " + ex);
                result = new ShellOperationResult(false, "The operation failed: " + ex.Message, ex.ToString());
            }

            _finished = true;
            ShowResult(result);
        }

        private void ShowResult(ShellOperationResult result)
        {
            prgShellProgress.Marquee = false;
            btnShellCancel.Visible = false;

            // A user-initiated cancel (e.g. passcode prompt cancelled) closes
            // the operation window immediately - no "Failed" state, no Close
            // button to chase. The operation services already cleaned up.
            if (result.Cancelled)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            if (result.Succeeded)
            {
                lblShellStage.Text = "Complete";
                lblShellResult.Text = result.Message;
            }
            else
            {
                lblShellStage.Text = "Failed";
                lblShellStage.ForeColor = System.Drawing.Color.FromArgb(220, 80, 80);
                lblShellResult.Text = result.Message;
            }

            if (!string.IsNullOrEmpty(result.Details))
            {
                btnShellDetails.Visible = true;
                btnShellCopyResult.Visible = true;
            }
            if (!string.IsNullOrEmpty(result.OpenFolder))
            {
                btnShellOpenFolder.Visible = true;
            }

            btnShellClose.Visible = true;
            btnShellClose.Focus();
            lblShellResult.Tag = result;
        }

        private void btnShellCancel_Click(object sender, EventArgs e)
        {
            if (_finished) return;
            _cts.Cancel();
            btnShellCancel.Enabled = false;
            lblShellStage.Text = "Cancelling...";
        }

        private void btnShellClose_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnShellOpenFolder_Click(object sender, EventArgs e)
        {
            if (lblShellResult.Tag is not ShellOperationResult { OpenFolder: { } folder }) return;
            try { Process.Start("explorer.exe", folder); } catch { /* best-effort: Explorer refused to open */ }
        }

        private void btnShellDetails_Click(object sender, EventArgs e)
        {
            if (lblShellResult.Tag is not ShellOperationResult { Details: { } details }) return;
            AppMessageBox.Show(Text, details, AppMessageType.Info, AppMessageButtons.OK);
        }

        private void btnShellCopyResult_Click(object sender, EventArgs e)
        {
            if (lblShellResult.Tag is not ShellOperationResult result) return;
            try
            {
                Clipboard.SetText(string.IsNullOrEmpty(result.Details) ? result.Message : result.Details);
                btnShellCopyResult.Text = "Copied";
            }
            catch { /* best-effort: clipboard locked by another process */ }
        }
    }
}
