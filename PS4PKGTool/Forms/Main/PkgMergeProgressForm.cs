using DarkUI.Forms;
using OrbisPkgTool;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PS4PKGTool
{
    internal sealed partial class PkgMergeProgressForm : DarkForm
    {
        private readonly PkgMergeRequest _request;
        private readonly CancellationTokenSource _cancellation = new();
        private bool _finished;

        public PkgMergeResult? Result { get; private set; }
        public Exception? Error { get; private set; }
        public bool WasCancelled { get; private set; }

        public PkgMergeProgressForm(PkgMergeRequest request)
        {
            _request = request;
            InitializeComponent();
        }

        private async void PkgMergeProgressForm_Shown(object sender, EventArgs e) => await RunMergeAsync();

        private void cancel_Click(object sender, EventArgs e)
        {
            _cancellation.Cancel();
            _cancel.Enabled = false;
            _stage.Text = "Cancelling merge...";
        }

        private async Task RunMergeAsync()
        {
            using var progress = new UiProgress<PkgMergeProgress>(UpdateProgress);
            try
            {
                Result = await Task.Run(() => new PkgMergeService().Merge(_request with
                {
                    Progress = progress,
                    CancellationToken = _cancellation.Token
                }));
                _finished = true;
                DialogResult = DialogResult.OK;
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                _finished = true;
                DialogResult = DialogResult.Cancel;
            }
            catch (Exception ex)
            {
                Error = ex;
                _finished = true;
                DialogResult = DialogResult.Abort;
            }
            finally
            {
                _finished = true;
                Close();
            }
        }

        private void UpdateProgress(PkgMergeProgress progress)
        {
            _stage.Text = progress.Stage;
            _detail.Text = progress.CurrentFile ?? "";
            if (progress.Percentage is not double percent)
            {
                _progress.Marquee = true;
                return;
            }

            _progress.Marquee = false;
            _progress.Value = Math.Clamp((int)Math.Round(percent), 0, 100);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_finished)
            {
                _cancellation.Cancel();
                e.Cancel = true;
                return;
            }

            _cancellation.Dispose();
            base.OnFormClosing(e);
        }
    }
}
