using ByteSizeLib;
using ClosedXML.Excel;
using DarkUI.Config;
using DarkUI.Controls;
using DarkUI.Forms;
using GitHubUpdate;
using Irony;
using Newtonsoft.Json;
using PS4_Tools.LibOrbis.PKG;
using PS4_Tools.LibOrbis.Util;
using PS4_Trophy_xdpx;
using PS4PKGTool.Util;
using PS4PKGTool.Util.Constants;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using PS4PKGTool.Utilities.Settings;
using PS4PKGTool.Utilities.TrophyMetadata;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MethodInvoker = System.Windows.Forms.MethodInvoker;
using TRPViewer;
using static PS4_Tools.PKG.SceneRelated;
using static PS4PKGTool.Utilities.PS4PKGToolHelper.Helper;
using static PS4PKGTool.Utilities.PS4PKGToolHelper.Helper.Backport;
using static PS4PKGTool.Utilities.PS4PKGToolHelper.Helper.Entry;
using static PS4PKGTool.Utilities.PS4PKGToolHelper.Helper.TreeView;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Bitmap = System.Drawing.Bitmap;
using Entry = PS4PKGTool.Utilities.PS4PKGToolHelper.Helper.Entry;
using ListView = System.Windows.Forms.ListView;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;
using TreeView = PS4PKGTool.Utilities.PS4PKGToolHelper.Helper.TreeView;

namespace PS4PKGTool
{
    public partial class Main : DarkUI.Forms.DarkForm
    {
        private const string DefaultOrbisPasscode = "00000000000000000000000000000000";
        private MemoryMappedFile pkgFile;
        private dynamic send_pkg_json;
        private string TEMPFILENAMESENDPKG;
        private bool renameBackFile;
        internal static string filenameDLC;
        private static string ApplicationVersion { get; set; }
        private readonly List<string> ExcludedDirectoryList = new List<string>() { "System Volume Information", "$RECYCLE.BIN", "$Recycle.Bin" };

        // Filter state for tree/list view filtering (tabPage7)
        private ListViewItem _upItem;
        private readonly List<ListViewItem> _allItems = new();
        private bool _populating;
        private bool _filtering;
        private TreeNode _currentNode;
        private readonly Dictionary<string, long> _fileSizes = new();   // PKG path → file size

        /// <summary>
        /// Clears the file-browser tree, list view, and all filter state.
        /// MUST be used instead of bare Nodes.Clear() - the filter state
        /// (rootNodes/_currentNode/_allItems) references nodes that throw
        /// InvalidOperationException on FullPath once detached from the tree.
        /// </summary>
        private void ClearFileBrowser()
        {
            PKGTreeView.Nodes.Clear();
            listView1.Items.Clear();
            rootNodes = null;
            _currentNode = null;
            _allItems.Clear();
            _upItem = null;
            _populating = false;
            _filtering = false;
            // Reset the viewer pane
            txtPreview.Visible = false;
            txtHexPreview.Visible = false;
            picPreview.Visible = false;
            picPreview.Image?.Dispose();
            picPreview.Image = null;
            lblFileViewerInfo.Text = "Select a file to preview it.";
            if (btnExportTextures != null) btnExportTextures.Enabled = false;
            _previewEntryPath = null;
            _previewIsUnityFile = false;
            CleanupContainerBrowse();
        }
        private int _trophyLoadVersion;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _trophyExtractionLocks = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _pkgDirectories = new();   // paths that are directories (from orbis D lines)
        private int _glvGroupHeaderIndex = -1;   // group header row index last right-clicked in GLV

        [DllImport("winmm.dll")]
        private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);

        [DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilter(uint msg, uint flags);

        private byte[] old_byte;

        public static string GetApplicationVersion()
        {
            // Get the entry assembly (usually represents the current application)
            Assembly entryAssembly = Assembly.GetEntryAssembly();

            // Get the custom attribute for the AssemblyInformationalVersionAttribute
            // This attribute should be set in the project's Properties/AssemblyInfo.cs file
            AssemblyInformationalVersionAttribute versionAttribute =
                entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            // Retrieve and return the version string
            return versionAttribute?.InformationalVersion ?? "Version information not available";
        }

        public Main()
        {
            InitializeComponent();
            this.Icon = AppIcon;

            listView1.Columns.AddRange(new ColumnHeader[] { columnHeader7, columnHeader8, columnHeader9, columnHeader10 });

            // Asset workspace list (container children).
            assetListView.Columns.AddRange(new ColumnHeader[]
            {
                new ColumnHeader { Text = "Name", Width = 110 },
                new ColumnHeader { Text = "Type", Width = 60 },
                new ColumnHeader { Text = "Size", Width = 55 },
            });

            // Tag-bind image/icon extraction menu items for data-driven dispatch
            globalExtractImagesAndIconToolStripMenuItem1.Tag = $"{ImageIconExtractionType.ALL}|{PKGSelectionType.ALL}";
            globalExtractImagesAndIconToolStripMenuItem2.Tag = $"{ImageIconExtractionType.ALL}|{PKGSelectionType.ALL}";
            globalExtractImageOnlyToolStripMenuItem1.Tag     = $"{ImageIconExtractionType.IMAGE}|{PKGSelectionType.ALL}";
            globalExtractImageOnlyToolStripMenuItem2.Tag     = $"{ImageIconExtractionType.IMAGE}|{PKGSelectionType.ALL}";
            globalExtractIconOnlyToolStripMenuItem1.Tag      = $"{ImageIconExtractionType.ICON}|{PKGSelectionType.ALL}";
            globalExtractIconOnlyToolStripMenuItem2.Tag      = $"{ImageIconExtractionType.ICON}|{PKGSelectionType.ALL}";
            selectedExtractImagesAndIconToolStripMenuItem1.Tag = $"{ImageIconExtractionType.ALL}|{PKGSelectionType.SELECTED}";
            selectedExtractImagesAndIconToolStripMenuItem2.Tag = $"{ImageIconExtractionType.ALL}|{PKGSelectionType.SELECTED}";
            selectedExtractImageOnlyToolStripMenuItem1.Tag     = $"{ImageIconExtractionType.IMAGE}|{PKGSelectionType.SELECTED}";
            selectedExtractImageOnlyToolStripMenuItem2.Tag     = $"{ImageIconExtractionType.IMAGE}|{PKGSelectionType.SELECTED}";
            selectedExtractIconOnlyToolStripMenuItem1.Tag      = $"{ImageIconExtractionType.ICON}|{PKGSelectionType.SELECTED}";
            selectedExtractIconOnlyToolStripMenuItem2.Tag      = $"{ImageIconExtractionType.ICON}|{PKGSelectionType.SELECTED}";

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; 
            CheckForIllegalCrossThreadCalls = false;
            PKGGridView.ScrollBars = ScrollBars.Vertical;
            darkDataGridView2.ScrollBars = ScrollBars.Vertical;
            TrophyGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            TrophyGridView.ScrollBars = ScrollBars.Vertical;

            this.MinimumSize = new System.Drawing.Size(1000, 700);
            this.ActiveControl = null;  //this = form
            toolStripProgressBar1.MarqueeAnimationSpeed = 30;

            // Bypass UIPI: allow drag-drop from Explorer when running as admin
            try { ChangeWindowMessageFilter(0x0233, 1); ChangeWindowMessageFilter(0x0049, 1); } catch (Exception ex) { Logger.LogWarning("UIPI bypass failed (non-critical): " + ex.Message); }

            // Wire drag-drop on the form and all child controls
            this.AllowDrop = true;
            this.DragEnter += PKGGridView_DragEnter;
            this.DragOver += (s, ev) => { if (ev.Data.GetDataPresent(DataFormats.FileDrop)) ev.Effect = DragDropEffects.Copy; };
            this.DragDrop += PKGGridView_DragDrop;
            WireAllControls(this);

            // Suppress DataGridView DataError dialogs (occurs during rapid DataSource changes)
            PKGGridView.DataError += (_, args) => { args.Cancel = true; }; // suppress column mismatch errors during DataSource changes

            // Right-click selects the full row under the cursor so context-menu state is correct
            PKGGridView.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var hit = PKGGridView.HitTest(e.X, e.Y);
                    if (hit.RowIndex >= 0 && hit.RowIndex < PKGGridView.Rows.Count)
                    {
                        if (!PKGGridView.Rows[hit.RowIndex].Selected)
                        {
                            PKGGridView.ClearSelection();
                            PKGGridView.Rows[hit.RowIndex].Selected = true;
                        }
                        PKGGridView.CurrentCell = PKGGridView.Rows[hit.RowIndex].Cells[hit.ColumnIndex >= 0 ? hit.ColumnIndex : 0];
                        UpdateOfficialUpdateMenuState();
                    }
                }
            };

            ApplicationVersion = GetApplicationVersion();

            BalanceListViewColumns();

            // Filter textbox for tree/list view (built-in clear ✕ button)
            tbFilterTreeView.SearchTextChanged += (_, _) => ApplyFilter();

            // Load treeview file-type icons from embedded resources
            try
            {
                var icons = new (string key, Bitmap bmp)[]
                {
                    ("folder",       Properties.Resources.tv_folder),
                    ("document",     Properties.Resources.tv_document),
                    ("image",        Properties.Resources.tv_image),
                    ("config",       Properties.Resources.tv_config),
                    ("binary",       Properties.Resources.tv_binary),
                    ("folder-open",  Properties.Resources.tv_folder_open),
                    ("audio",        Properties.Resources.tv_audio),
                    ("file-unknown", Properties.Resources.tv_file_unknown),
                    ("package",      Properties.Resources.tv_package),
                    ("video",        Properties.Resources.tv_video),
                    ("code",         Properties.Resources.tv_code),
                };
                foreach (var (key, bmp) in icons)
                    imageList1.Images.Add(key, new Bitmap(bmp));
            }
            catch (Exception ex) { Logger.LogWarning($"Icon load: {ex.Message}"); }

            // Collapse GLV groups on initial population (control must be visible first),
            // then keep the currently selected PKG visible by re-expanding its group.
            var glvCollapseDone = false;
            subTabControl.SelectedIndexChanged += (_, _) =>
            {
                if (!glvCollapseDone && subTabControl.SelectedTab == tabPageGroup && groupedListView != null)
                {
                    glvCollapseDone = true;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        groupedListView.CollapseAll();
                        if (!string.IsNullOrEmpty(PKG.SelectedPKGFilename))
                            groupedListView.SelectFilePath(PKG.SelectedPKGFilename);
                    });
                }
            };

            // GLV filter removed - the Table tab's tbSearchGame controls both views
            // via PopulateGroupedView() which rebuilds from the DGV's filtered DefaultView.
            groupedListView.SelectedItemChanged += GroupedListView_SelectedItemChanged;
            btnGroupExpand.Click += (_, _) =>
            {
                if (groupedListView != null)
                {
                    if (btnGroupExpand.Text == "Expand All")
                    {
                        groupedListView.ExpandAll();
                        btnGroupExpand.Text = "Collapse All";
                    }
                    else
                    {
                        groupedListView.CollapseAll();
                        btnGroupExpand.Text = "Expand All";
                    }
                }
            };

            // Group-by ComboBox
            cbGroupBy.Items.AddRange(new object[] { "Title", "Title ID", "System Version", "PKG Type", "Category", "ShadPS4 (Windows)" });
            cbGroupBy.SelectedIndex = 1; // default: Title ID
            cbGroupBy.SelectedIndexChanged += (_, _) => PopulateGroupedView();

            // ── GLV context menu ──────────────────────────────
            glvRenamePriMenuItem.Click += (_, _) => GlvRenameByPriority();

            groupedListView.ContextMenuStrip = contextMenuGLV;
            groupedListView.GroupHeaderClicked += (headerIdx, groupName, args) =>
            {
                _glvGroupHeaderIndex = headerIdx;
                glvRenamePriMenuItem.Visible = true;
                contextMenuGLV.Show(Cursor.Position);
            };
            // Rename by Install Priority is available for both item and group right-clicks
            contextMenuGLV.Opening += (_, _) =>
            {
                glvRenamePriMenuItem.Visible = true;
                UpdateGlvMenuLabel(); // always the selected/highlighted PKG's title
            };
            // Clear the group-target hand-off when the menu closes so no stale state leaks
            contextMenuGLV.Closed += (_, _) => _glvGroupHeaderIndex = -1;

            Logger.OnLog += Logger_OnLog;
        }

        private void Logger_OnLog(string line)
        {
            try
            {
                if (_tbLogBox != null)
                    _tbLogBox.BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            _tbLogBox.AppendText(line + Environment.NewLine);
                            if (_tbLogBox.Text.Length > 50000)
                                _tbLogBox.Text = _tbLogBox.Text.Substring(_tbLogBox.Text.Length - 40000);
                        }
                        catch { }
                    }));
            }
            catch { }
        }

        private static void SafeMoveDirectory(string src, string dst)
        {
            try { Directory.Move(src, dst); }
            catch (IOException)
            {
                // Cross-volume move fails - fall back to copy+delete
                foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(src.Length).TrimStart('\\', '/');
                    string target = Path.Combine(dst, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? dst);
                    File.Copy(f, target, true);
                }
                Directory.Delete(src, true);
            }
        }

        private string GroupByColumn =>
            cbGroupBy.SelectedItem?.ToString() ?? "Category";

        /// <summary>Extract [Error]/[Warn] lines from orbis-pub-cmd output.</summary>
        private static string FormatOrbisError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "(no output from orbis-pub-cmd)";
            var errors = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(l => l.Trim())
                .ToList();
            return errors.Count > 0 ? string.Join("\n", errors) : raw.Trim();
        }

        private void LogToTextBox(string logMessage)
        {
            //if (tbLog.InvokeRequired)
            //{
            //    // If the call is not on the UI thread, invoke it on the UI thread
            //    Invoke(new Action<string>(LogToTextBox), logMessage);
            //}
            //else
            //{
            //    // Append the log message to the TextBox
            //    tbLog.AppendText(logMessage + Environment.NewLine);
            //}
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            Logger.LogInformation("App closing.");
            Tool.KillNodeJS();
            //try
            //{
            //    if (Directory.Exists(WorkingDirectory))
            //    {
            //        Directory.Delete(WorkingDirectory, recursive: true);
            //    }
            //}
            //catch { }
            SettingsManager.SaveSettings(appSettings_, SettingFilePath);
            Application.Exit();
        }

        private static bool IsIgnorable(string dir)
        {
            string[] ignorableFolders = { "System Volume Information", "$RECYCLE.BIN", "$Recycle.Bin" };
            return ignorableFolders.Any(folder => dir.EndsWith(folder));
        }

        private void PKGListGridView_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                BGM.isBGMPlaying = false;
                BGM.At9Player.Stop();

                PKG.SelectedPKGFilename = "";

                if (PKGGridView.SelectedCells.Count > 0)
                {
                    GetSelectedPKGPath();

                    // Mirror the selection into the grouped view (DGV → GLV sync)
                    if (groupedListView != null && !string.IsNullOrEmpty(PKG.SelectedPKGFilename))
                        groupedListView.SelectFilePath(PKG.SelectedPKGFilename);

                    if (PKG.isDeletingPkg)
                        SelectFirstRowPkg();

                    LoadPKGDetails();
                }

                UpdateOfficialUpdateMenuState();
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"ERROR: Selection changed: {ex.Message}");
                Logger.LogError($"Error on selection change: {ex.Message}");
            }
        }

        private void UpdateOfficialUpdateMenuState()
        {
            bool canOpen = false;
            try
            {
                if (PKGGridView.SelectedRows.Count == 1)
                {
                    string pkgType = PKGGridView.SelectedRows[0].Cells[8].Value?.ToString() ?? "";
                    canOpen = pkgType == PKGCategory.GAME || pkgType == PKGCategory.PATCH;
                }
            }
            catch (Exception ex) { Logger.LogWarning("Error reading PKG type from grid: " + ex.Message); }
            downloadOfficialUpdateToolStripMenuItem1.Enabled = canOpen;
            if (downloadOfficialUpdateToolStripMenuItem2 != null)
                downloadOfficialUpdateToolStripMenuItem2.Enabled = canOpen;
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            Logger.LogInformation("App started.");
            try
            {
                WindowState = FormWindowState.Maximized;
                this.Text = "PS4 PKG Tool " + ApplicationVersion;
                await Task.Run(() =>
            {
                Logger.LogInformation("Selected directory: ");

                foreach (var folder in appSettings_.PkgDirectories)
                {
                    Logger.LogInformation(folder);
                }

                Logger.LogInformation("Checking Node.js and server module...");

                bool isNodeJsInstalled = NodeJsHttpServer.IsSoftwareInstalled("Node.js");
                appSettings_.NodeJsInstalled = isNodeJsInstalled;
                Logger.LogInformation(isNodeJsInstalled ? "Node.js installed." : "Node.js not installed.");

                bool isHttpServerModuleInstalled = Directory.Exists(NodeJsHttpServer.HttpServerModulePath);
                appSettings_.HttpServerInstalled = isHttpServerModuleInstalled;
                Logger.LogInformation(isHttpServerModuleInstalled ? "Module installed." : "Module not installed.");

                this.Invoke((MethodInvoker)delegate
                {
                    this.Enabled = false;

                    // If BGM playing, stop it (with timeout safety)
                    int bgmAttempts = 0;
                    while (BGM.isBGMPlaying && bgmAttempts++ < 10)
                    {
                        BGM.isBGMPlaying = false;
                        try { BGM.At9Player.Stop(); } catch { break; }
                    }

                    // Note: missing directory warnings are shown in PostPkgLoad after scan completes,
                    // so we don't show them here to avoid double prompts.

                    // Update UI
                    PKGGridView.Enabled = false;
                    darkDataGridView2.Enabled = false;

                    Logger.LogInformation("Scanning PKG...");

                    if (Helper.LaunchEmpty)
                    {
                        Logger.LogInformation("Launch Empty - skipping PKG scan.");
                        this.Invoke((MethodInvoker)(() =>
                        {
                            this.Enabled = true;
                            PKGGridView.Enabled = true;
                            darkDataGridView2.Enabled = true;
                            SetOperationMenusEnabled(false);
                            toolStripStatusLabel2.Text = "Ready (empty)";
                        }));
                    }
                    else
                    {
                        LoadPKGGridView();
                    }
                    //LoadPKGListView();
                });
            });
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"FATAL: App startup failed: {ex.Message}");
                Logger.LogError($"Form1_Load crashed: {ex}");
                MessageBox.Show($"Startup failed:\n{ex.Message}", "Fatal Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private BackgroundWorker _detailWorker;
        private int _detailLoadVersion;
        private readonly Dictionary<string, SortOrder> _colSortDir = new();

        private void LoadPKGDetails()
        {
            if (!File.Exists(PKG.SelectedPKGFilename))
            {
                SelectFirstRowPkg();
                if (string.IsNullOrEmpty(PKG.SelectedPKGFilename)) return;
            }

            // Invalidate any in-flight load - a stale completion must never clobber the UI.
            int loadVersion = Interlocked.Increment(ref _detailLoadVersion);
            _detailWorker?.CancelAsync();
            var worker = new BackgroundWorker { WorkerSupportsCancellation = true };
            _detailWorker = worker;
            string pkgPath = PKG.SelectedPKGFilename; // capture for closure

            // Show loading indicator
            toolStripStatusLabel2.Text = "Loading PKG details...";

            worker.DoWork += (_, args) =>
            {
                if (worker.CancellationPending) { args.Cancel = true; return; }
                args.Result = PS4_Tools.PKG.SceneRelated.Read_PKG(pkgPath);
            };

            worker.RunWorkerCompleted += (_, args) =>
            {
                if (loadVersion != _detailLoadVersion) return; // stale - a newer load started
                if (args.Cancelled || args.Error != null || args.Result == null) return;
                var ps4Pkg = (PS4_Tools.PKG.SceneRelated.Unprotected_PKG)args.Result;

                UpdateFormTitle(ps4Pkg.PS4_Title, ps4Pkg.PKG_Type.ToString());
                PKG.CurrentPKGTitle = ps4Pkg.PS4_Title;
                PKG.CurrentPKGType = ps4Pkg.PKG_Type.ToString();

                int selCount = PKGGridView.SelectedRows.Count;
                GroupActionTitleStripMenuItem.Text = selCount > 1 ? "Group Action" : ps4Pkg.PS4_Title;
                toolStripMenuItem2.Text = selCount > 1 ? "Group Action" : ps4Pkg.PS4_Title;

                RpiUninstallBasePKGToolStripMenuItem1.Enabled = true;
                RpiUninstallBasePKGToolStripMenuItem2.Enabled = true;
                RpiUninstallPatchPKGToolStripMenuItem1.Enabled = true;
                RpiUninstallPatchPKGToolStripMenuItem2.Enabled = true;
                RpiUninstallDlcPKGToolStripMenuItem1.Enabled = true;
                RpiUninstallDlcPKGToolStripMenuItem2.Enabled = true;
                RpiUninstallThemePKGToolStripMenuItem1.Enabled = true;
                RpiUninstallThemePKGToolStripMenuItem2.Enabled = true;

                string pkgType = ps4Pkg.PKG_Type.ToString();
                if (pkgType == PKGCategory.GAME)
                {
                    RpiUninstallDlcPKGToolStripMenuItem1.Enabled = false;
                    RpiUninstallDlcPKGToolStripMenuItem2.Enabled = false;
                    RpiUninstallThemePKGToolStripMenuItem1.Enabled = false;
                    RpiUninstallThemePKGToolStripMenuItem2.Enabled = false;
                }
                else if (pkgType == PKGCategory.PATCH)
                {
                    RpiUninstallDlcPKGToolStripMenuItem1.Enabled = false;
                    RpiUninstallDlcPKGToolStripMenuItem2.Enabled = false;
                    RpiUninstallThemePKGToolStripMenuItem1.Enabled = false;
                    RpiUninstallThemePKGToolStripMenuItem2.Enabled = false;
                }
                else if (pkgType == PKGCategory.ADDON)
                {
                    RpiUninstallBasePKGToolStripMenuItem1.Enabled = false;
                    RpiUninstallBasePKGToolStripMenuItem2.Enabled = false;
                    RpiUninstallPatchPKGToolStripMenuItem1.Enabled = false;
                    RpiUninstallPatchPKGToolStripMenuItem2.Enabled = false;
                }

                ShowPackageIcon(ps4Pkg);
                UpdateParamInfoGrid(ps4Pkg);
                LoadBackgroundImages(ps4Pkg);
                LoadTrophyInfo(ps4Pkg, pkgPath);
                LoadHeaderInfo(ps4Pkg);
                LoadPKGEntries(ps4Pkg);
                LoadPubToolInfo(ps4Pkg);

                ClearFileBrowser();

                if (appSettings_.PlayBgm) PlayBGM(pkgPath);
                toolStripStatusLabel2.Text = "...";
            };

            worker.RunWorkerAsync();
        }

        private void UpdateFormTitle(string pkgTitle, string pkgType)
        {
            string category = pkgType switch
            {
                PKGCategory.GAME => "Game",
                PKGCategory.PATCH => "Patch",
                PKGCategory.ADDON => "Addon",
                PKGCategory.APP => "App",
                PKGCategory.UNKNOWN => "Unknown",
                _ => pkgType
            };
            this.Text = $"PS4 PKG Tool {ApplicationVersion} - Viewing [{category}] \"{pkgTitle}\"";
        }

        private OfficialUpdateForm _officialUpdateForm;

        private void OpenOfficialUpdateForm()
        {
            try
            {
                if (PKGGridView.SelectedRows.Count != 1)
                {
                    ShowInformation("Please select a single PKG.", false);
                    return;
                }

                DataGridViewRow row = PKGGridView.SelectedRows[0];
                string pkgType = row.Cells[8].Value?.ToString() ?? "";
                if (pkgType != PKGCategory.GAME && pkgType != PKGCategory.PATCH)
                {
                    ShowInformation("Official updates are only available for Game and Patch PKGs.", false);
                    return;
                }

                string filename = row.Cells[0].Value?.ToString();
                string directory = row.Cells[13].Value?.ToString();
                if (string.IsNullOrEmpty(filename) || string.IsNullOrEmpty(directory))
                {
                    ShowError("Could not determine PKG path.", false);
                    return;
                }

                string pkgPath = Path.Combine(directory, filename);
                if (!File.Exists(pkgPath))
                {
                    ShowError("Selected PKG file not found.", false);
                    return;
                }

                var pkg = PS4_Tools.PKG.SceneRelated.Read_PKG(pkgPath);
                if (pkg?.Param?.TITLEID == null)
                {
                    ShowError("Could not read PKG title ID.", false);
                    return;
                }

                if (_officialUpdateForm == null || _officialUpdateForm.IsDisposed)
                    _officialUpdateForm = new OfficialUpdateForm();

                _officialUpdateForm.SetLogCallback(Logger.LogInformation);
                _officialUpdateForm.LoadUpdate(pkg.Param.TITLEID, pkgType, appSettings_.OfficialUpdateDownloadDirectory);
                _officialUpdateForm.Show();
                _officialUpdateForm.BringToFront();
            }
            catch (Exception ex)
            {
                Logger.LogError("Error opening official update form: " + ex.Message);
                ShowError("Error opening official update form: " + ex.Message, true);
            }
        }

        private void DownloadOfficialUpdate_Click(object sender, EventArgs e)
        {
            OpenOfficialUpdateForm();
        }

        private void LoadPubToolInfo(PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg)
        {
            try
            {
                List<string> array = pkg.Param.Tables
                    .Where(item => item.Name == "PUBTOOLINFO")
                    .Select(item => item.Value)
                    .FirstOrDefault()
                    ?.Split(',')
                    .Reverse()
                    .ToList();

                List<string> value = array?.Select(item => item.Substring(item.LastIndexOf('=') + 1)).ToList();
                List<string> type = array?.Select(items => items.Split('=')[0]).ToList();

                DataTable dtPubtool = new DataTable();
                foreach (var tv in type)
                {
                    dtPubtool.Columns.Add(tv.Replace("c_date", "Creation Date")
                        .Replace("sdk_ver", "PS4 SDK Version")
                        .Replace("st_type", "Storage Type")
                        .Replace("c_time", "Creation Time"));
                }

                var row = dtPubtool.NewRow();

                for (int i = 0; i < value?.Count; i++)
                {
                    row[i] = value[i];
                }
                dtPubtool.Rows.Add(row);

                darkDataGridView4.DataSource = dtPubtool;
            }
            catch (Exception ex) { Logger.LogWarning("Error loading pub-tool info: " + ex.Message); }
        }

        private void LoadHeaderInfo(PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg)
        {
            try
            {
                List<string> type = pkg.Header.DisplayType().ToList();
                List<string> value = pkg.Header.DisplayValue().ToList();

                DataTable dtHeader = new DataTable();
                dtHeader.Columns.Add("Type");
                dtHeader.Columns.Add("Value");

                var typeAndValue = type.Zip(value, (t, v) => new { Type = t, Value = v });
                foreach (var tv in typeAndValue)
                {
                    dtHeader.Rows.Add(tv.Type, tv.Value);
                }

                dgvHeader.DataSource = dtHeader;
            }
            catch (Exception ex) { Logger.LogWarning("Error loading header info: " + ex.Message); }
        }

        private void LoadPKGEntries(PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg)
        {
            dgvEntryList.DataSource = null;
            dgvEntryList.Rows.Clear();
            dgvEntryList.Refresh();
            dgvEntryList.ScrollBars = ScrollBars.Vertical;

            try
            {
                using (var file = File.OpenRead(PKG.SelectedPKGFilename))
                {
                    var pkgReader = new PkgReader(file);
                    var pkgData = pkgReader.ReadPkg();
                    var dt = new DataTable();
                    var i = 0;
                    Entry.EntryIdNameDictionary.Clear();
                    Entry.EncryptedEntryOffsetNameDictionary.Clear();
                    var entryId = "";
                    var entryName = "";

                    dt.Columns.Add("Name");
                    dt.Columns.Add("Offset");
                    dt.Columns.Add("Size");
                    dt.Columns.Add("Flags 1");
                    dt.Columns.Add("Flags 2");
                    dt.Columns.Add("Encrypted?");

                    foreach (var meta in pkgData.Metas.Metas)
                    {
                        entryId = $"{i++,-6}";
                        entryName = meta.id.ToString();
                        EntryIdNameDictionary.Add(entryId, entryName);
                        if (meta.Encrypted)
                        {
                            EncryptedEntryOffsetNameDictionary.Add($"0x{meta.DataOffset:X8}", $"{meta.id}");
                        }
                    }

                    i = 0;

                    foreach (var meta in pkgData.Metas.Metas)
                    {
                        var finalSize = ByteSizeLib.ByteSize.FromBytes(Convert.ToDouble(meta.DataSize));
                        dt.Rows.Add($"{meta.id}", $"0x{meta.DataOffset:X}", finalSize, $"0x{meta.Flags1:X}", $"0x{meta.Flags2:X}", $"{meta.Encrypted:X}");
                    }

                    dgvEntryList.DataSource = dt;
                }

                foreach (DataGridViewColumn column in dgvEntryList.Columns)
                {
                    column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load PKG entries : {ex.Message}.");
            }
        }

        private void LoadBackgroundImages(PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg)
        {
            try
            {
                pbPIC0.Image = null;
                pbPIC1.Image = null;
                pbPIC0.Refresh();
                pbPIC1.Refresh();

                if (pkg.PKG_Type.ToString() == PKGCategory.GAME || pkg.PKG_Type.ToString() == PKGCategory.PATCH)
                {
                    if (pkg.Image != null)
                    {
                        pbPIC0.Click += pictureBox_click;
                        pbPIC0.Visible = true;
                        pbPIC0.SizeMode = PictureBoxSizeMode.StretchImage;
                        pbPIC0.Image = Helper.Bitmap.BytesToBitmap(pkg.Image);
                        Helper.Bitmap.pic0.Image = pbPIC0.Image;
                    }
                    else
                    {
                        pbPIC0.Click -= pictureBox_click;
                        pbPIC0.Visible = false;
                        pbPIC0.Image = null;
                    }

                    if (pkg.Image2 != null)
                    {
                        if (old_byte == pkg.Image2)
                        {
                            pbPIC1.Click -= pictureBox_click;
                            pbPIC1.Visible = false;
                            pbPIC1.Image = null;
                        }
                        else
                        {
                            old_byte = pkg.Image2;
                            pbPIC1.Click += pictureBox_click;
                            pbPIC1.Visible = true;
                            pbPIC1.SizeMode = PictureBoxSizeMode.StretchImage;
                            pbPIC1.Image = Helper.Bitmap.BytesToBitmap(pkg.Image2);
                            Helper.Bitmap.pic1.Image = pbPIC1.Image;
                        }
                    }
                    else
                    {
                        pbPIC1.Click -= pictureBox_click;
                        pbPIC1.Visible = false;
                        pbPIC1.Image = null;
                    }
                }
            }
            catch (Exception ex) { Logger.LogWarning("Error displaying package icons: " + ex.Message); }
        }

        private async Task<string> EnsureTrophyFileExtractedAsync(
            string pkgPath,
            PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg)
        {
            string outputPath = Path.Combine(Trophy.TrophyTempFolder, pkg.Content_ID + "_" + pkg.PKG_Type + ".TRP");
            if (File.Exists(outputPath)) return outputPath;

            SemaphoreSlim extractionLock = _trophyExtractionLocks.GetOrAdd(outputPath, _ => new SemaphoreSlim(1, 1));
            await extractionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (File.Exists(outputPath)) return outputPath;
                bool extracted = await Task.Run(() => TryExtractTrophyFile(pkgPath, outputPath, pkg)).ConfigureAwait(false);
                return extracted ? outputPath : null;
            }
            finally { extractionLock.Release(); }
        }

        private static bool TryExtractTrophyFile(
            string pkgPath,
            string outputPath,
            PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg)
        {
            Tool.CreateDirectoryIfNotExists(Trophy.TrophyTempFolder);
            string temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using var pkgFile = File.OpenRead(pkgPath);
                var pkgReader = new PkgReader(pkgFile);
                var pkgData = pkgReader.ReadPkg();
                var meta = pkgData.Metas.Metas.FirstOrDefault(item =>
                    string.Equals(item.id.ToString(), "TROPHY__TROPHY00_TRP", StringComparison.Ordinal));
                if (meta == null)
                {
                    Logger.LogWarning("No TROPHY__TROPHY00_TRP entry found in PKG: " + pkg.PS4_Title + " (" + pkg.Content_ID + ")");
                    return false;
                }
                if (meta.Encrypted)
                {
                    Logger.LogWarning("Trophy TRP entry is encrypted and cannot be extracted: " + pkg.PS4_Title + " (" + pkg.Content_ID + ")");
                    return false;
                }

                using (var outFile = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    new SubStream(pkgFile, meta.DataOffset, meta.DataSize).CopyTo(outFile);

                if (new FileInfo(temporaryPath).Length != meta.DataSize)
                    throw new InvalidDataException("Extracted trophy length does not match the PKG metadata entry.");
                File.Move(temporaryPath, outputPath, overwrite: true);
                Logger.LogInformation("Trophy extracted: " + pkg.PS4_Title + " -> " + outputPath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Error extracting trophy data from PKG: " + pkgPath, ex);
                return false;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    try { File.Delete(temporaryPath); } catch { }
            }
        }

        private async void LoadTrophyInfo(PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg, string pkgPath)
        {
            int loadVersion = Interlocked.Increment(ref _trophyLoadVersion);
            try
            {
                TrophyGridView.DataSource = null;
                TrophyGridView.Rows.Clear();
                Trophy.ImageToExtractList.Clear();
                Trophy.TrophyFilenameToExtractList.Clear();

                string trophyFile = await EnsureTrophyFileExtractedAsync(pkgPath, pkg);
                if (loadVersion != _trophyLoadVersion) return;
                if (trophyFile == null)
                {
                    Logger.LogWarning("Trophy metadata unavailable because no extractable TRP was found for " + pkg.PS4_Title + " (" + pkg.Content_ID + ").");
                    return;
                }

                var service = new TrophyMetadataService();
                string cachePath = Path.Combine(AppDataDirectory, "TrophyMetadata", "np-communication-ids.json");
                var cache = new NpCommunicationIdCache(cachePath);
                string npCommunicationId = null;
                if (cache.TryGet(pkg.Content_ID, out string cachedId))
                {
                    npCommunicationId = cachedId;
                    Logger.LogInformation("Using cached NP Communication ID for " + pkg.Content_ID + ": " + cachedId);
                }

                TRPReader legacyReader = null;
                TrophyMetadataResult result = await Task.Run(() =>
                {
                    legacyReader = new TRPReader { ThrowError = false };
                    legacyReader.Load(trophyFile);
                    return service.Read(trophyFile, npCommunicationId);
                });
                if (loadVersion != _trophyLoadVersion) return;

                Trophy.trophy = legacyReader;
                PopulateTrophyGrid(result);
                Logger.LogInformation(result.StatusMessage + (result.NpCommunicationId == null ? string.Empty : " NP Communication ID: " + result.NpCommunicationId));
            }
            catch (Exception ex)
            {
                Logger.LogError("Error in LoadTrophyInfo", ex);
            }
        }

        private void PopulateTrophyGrid(TrophyMetadataResult result)
        {
            var table = new DataTable();
            table.Columns.Add("ID", typeof(string));
            table.Columns.Add("Image", typeof(Image));
            table.Columns.Add("Grade", typeof(string));
            table.Columns.Add("Hidden", typeof(string));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Description", typeof(string));
            table.Columns.Add("Group", typeof(string));
            table.Columns.Add("File", typeof(string));
            table.Columns.Add("Size", typeof(string));
            table.Columns.Add("Offset", typeof(string));

            var reader = new PS4PKGTool.Utilities.TrophyMetadata.TrpReader();
            var detector = new TrophyResourceDetector();
            var pngEntries = new Dictionary<string, TrpEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (TrpEntry entry in result.Archive.Entries)
            {
                if (!entry.Name.EndsWith(".PNG", StringComparison.OrdinalIgnoreCase)) continue;
                byte[] bytes = reader.ReadEntry(result.Archive, entry);
                if (detector.Detect(bytes, entry) != TrophyResourceKind.Png) continue;
                pngEntries[entry.Name] = entry;
                Trophy.TrophyFilenameToExtractList.Add(entry.Name);
                Trophy.ImageToExtractList.Add(ImageFromDetachedBytes(bytes));
            }

            if (result.Trophies.Count > 0)
            {
                foreach (TrophyInfo trophy in result.Trophies.OrderBy(item => item.Id))
                {
                    pngEntries.TryGetValue(trophy.IconEntryName, out TrpEntry iconEntry);
                    Image image = trophy.IconData == null ? null : ResizeTrophyImage(ImageFromDetachedBytes(trophy.IconData));
                    table.Rows.Add(
                        trophy.Id.ToString("000"), image, trophy.Grade.ToString(), trophy.IsHidden ? "Yes" : "No",
                        trophy.Name, trophy.Description, trophy.GroupName, trophy.IconEntryName,
                        iconEntry == null ? string.Empty : RoundBytes(iconEntry.Size),
                        iconEntry == null ? string.Empty : "0x" + iconEntry.Offset.ToString("X"));
                }
            }
            else
            {
                foreach ((string name, TrpEntry entry) in pngEntries)
                {
                    byte[] bytes = reader.ReadEntry(result.Archive, entry);
                    Match idMatch = Regex.Match(name, @"^TROP(?<id>\d{3})\.PNG$", RegexOptions.IgnoreCase);
                    table.Rows.Add(idMatch.Success ? idMatch.Groups["id"].Value : string.Empty,
                        ResizeTrophyImage(ImageFromDetachedBytes(bytes)), "Unknown", "Unknown", "Metadata unavailable",
                        result.StatusMessage, "Unknown", name, RoundBytes(entry.Size), "0x" + entry.Offset.ToString("X"));
                }
            }

            TrophyGridView.DataSource = table;
            foreach (DataGridViewColumn column in TrophyGridView.Columns)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            TrophyGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        }

        private static Image ImageFromDetachedBytes(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using Image source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            return new Bitmap(source);
        }

        private static Image ResizeTrophyImage(Image source)
        {
            using (source)
                return Trophy.ResizeImage(source, Math.Max(1, source.Width / 2), Math.Max(1, source.Height / 2));
        }

        //private void LoadTrophies(PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg)
        //{
        //    try
        //    {
        //        TrophyGridView.DataSource = null;
        //        TrophyGridView.Rows.Clear();

        //        DataTable trophyDataTable = new DataTable();

        //        // Add columns to the DataTable if needed
        //        trophyDataTable.Columns.Add("Image", typeof(Image));  // Example column, adjust as needed
        //        trophyDataTable.Columns.Add("Name", typeof(string));
        //        trophyDataTable.Columns.Add("Size", typeof(string));
        //        trophyDataTable.Columns.Add("Offset", typeof(string));

        //        if (pkg.Trophy_File != null)
        //        {
        //            BackgroundWorker bgwTrophy = new BackgroundWorker();
        //            bgwTrophy.WorkerSupportsCancellation = true;
        //            bgwTrophy.DoWork += (s, e) =>
        //            {
        //                try
        //                {
        //                    Logger.LogError("Loading trophies for " + pkg.PS4_Title + "..");

        //                    List<string> idEntryList = new List<string>();
        //                    List<string> nameEntryList = new List<string>();

        //                    using (var file = File.OpenRead(PKG.SelectedPKGFilename))
        //                    {
        //                        var pkgReader = new PkgReader(file);
        //                        var pkgData = pkgReader.ReadPkg();
        //                        var i = 0;

        //                        foreach (var meta in pkgData.Metas.Metas)
        //                        {
        //                            idEntryList.Add($"{i++,-6}");
        //                            nameEntryList.Add($"{meta.id}");
        //                        }

        //                        idEntryList.ToArray();
        //                        nameEntryList.ToArray();
        //                    }

        //                    string path = Trophy.TrophyTempFolder;
        //                    Directory.CreateDirectory(path);

        //                    var numbersAndWords = idEntryList.Zip(nameEntryList, (n, w) => new { id = n, name = w });
        //                    foreach (var nw in numbersAndWords)
        //                    {
        //                        if (nw.name == "TROPHY__TROPHY00_TRP")
        //                        {
        //                            var pkgPath = PKG.SelectedPKGFilename;
        //                            var idx = int.Parse(nw.id);
        //                            var name = nw.name;
        //                            Trophy.outPath = Path.Combine(path, name.Replace("_SHA", ".SHA").Replace("_DAT", ".DAT").Replace("_SFO", ".SFO").Replace("_XML", ".XML").Replace("_SIG", ".SIG").Replace("_PNG", ".PNG").Replace("_JSON", ".JSON").Replace("_DDS", ".DDS").Replace("_TRP", ".TRP").Replace("_AT9", ".AT9"));

        //                            using (var pkgFile = File.OpenRead(pkgPath))
        //                            {
        //                                var pkgReader = new PkgReader(pkgFile);
        //                                var pkgData = pkgReader.ReadPkg();
        //                                if (idx < 0 || idx >= pkgData.Metas.Metas.Count)
        //                                {
        //                                    return;
        //                                }
        //                                using (var outFile = File.Create(Trophy.outPath))
        //                                {
        //                                    var meta = pkgData.Metas.Metas[idx];
        //                                    outFile.SetLength(meta.DataSize);
        //                                    if (meta.Encrypted)
        //                                    {
        //                                        // Decrypt encrypted bytes if needed
        //                                    }
        //                                    new SubStream(pkgFile, meta.DataOffset, meta.DataSize).CopyTo(outFile);
        //                                }
        //                            }
        //                        }
        //                    }



        //                    if (File.Exists(Trophy.outPath))
        //                    {
        //                        Trophy.trophy = new TRPReader();
        //                        Trophy.trophy.Load(Trophy.outPath);

        //                        if (!Trophy.trophy.IsError)
        //                        {


        //                            foreach (var current in Trophy.trophy.TrophyList)
        //                            {
        //                                if (current.Name.ToUpper().EndsWith(".PNG"))
        //                                {
        //                                    var imageBytes = Trophy.trophy.ExtractFileToMemory(current.Name);
        //                                    Image image = Helper.Bitmap.BytesToImage(imageBytes);
        //                                    Image resize = Trophy.ResizeImage(image, image.Width / 2, image.Height / 2);

        //                                    trophyDataTable.Rows.Add(resize, current.Name, RoundBytes(current.Size), "0x" + current.Offset);
        //                                }
        //                                Application.DoEvents();
        //                            }

        //                            TrophyGridView.DataSource = trophyDataTable;
        //                            TrophyGridView.Columns[0].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        //                            TrophyGridView.Columns[1].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        //                            TrophyGridView.Columns[2].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        //                            TrophyGridView.Columns[3].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

        //                            TrophyGridView.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        //                            TrophyGridView.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        //                            TrophyGridView.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        //                        }
        //                    }
        //                }
        //                catch { }
        //            };
        //            bgwTrophy.RunWorkerCompleted += (s, e) =>
        //            {

        //            };
        //            bgwTrophy.RunWorkerAsync();
        //        }
        //        else
        //        {
        //            Logger.LogError(pkg.PS4_Title + " has no trophy.");
        //        }
        //    }
        //    catch { }
        //}


        private void PlayBGM(string selectedPkgFilename)
        {
            BackgroundWorker bgw = new BackgroundWorker();
            bgw.WorkerSupportsCancellation = true;
            bgw.DoWork += (s, e) =>
            {
                try
                {
                    BGM.PlayAt9(selectedPkgFilename);
                }
                catch (Exception ex) { Logger.LogWarning("BGM playback failed: " + ex.Message); }
            };
            bgw.RunWorkerCompleted += (s, e) =>
            {

            };
            bgw.RunWorkerAsync();
        }

        private void ShowPackageIcon(PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg)
        {
            darkLabel1.Text = "";

            if (pkg.Icon != null)
            {
                pictureBox1.Visible = true;
                label3.Text = "";
                pictureBox1.Image = Helper.Bitmap.BytesToBitmap(pkg.Icon);
            }
            else
            {
                pictureBox1.Visible = false;
                label3.Visible = true;
                label3.Text = "Image not available";
            }

            darkLabel1.Text = pkg.PS4_Title;
        }


        private void UpdateParamInfoGrid(PS4_Tools.PKG.SceneRelated.Unprotected_PKG pkg)
        {
            DataTable dg2 = new DataTable();
            dg2.Columns.Add("PARAM");
            dg2.Columns.Add("VALUE");

            for (int i = 0; i < pkg.Param.Tables.Count; i++)
            {
                dg2.Rows.Add(pkg.Param.Tables[i].Name, pkg.Param.Tables[i].Value);
            }

            darkDataGridView2.DataSource = dg2;
            darkDataGridView2.Columns[0].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            darkDataGridView2.Columns[1].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        private void pictureBox_click(object sender, EventArgs e)
        {
            if (!(sender is PictureBox pictureBox)) return;

            if (pictureBox == null)
                return;

            contextMenuBackgroundImage.Show(pictureBox, pictureBox.PointToClient(Cursor.Position));
        }

        /// <summary>
        /// Return PKG directory of selected/all PKG from gridview
        /// </summary>
        /// <param name="selectionType"></param>
        /// <returns></returns>
        private List<string> GetSelectedPKGDirectoryList(string selectionType, bool sortAddon = false)
        {
            var list = new List<string>();
            try
            {
                IEnumerable<DataGridViewRow> rowsToProcess = null;

                if (selectionType == PKGSelectionType.SELECTED)
                {
                    if (PKGGridView.SelectedRows.Count == 0) return list;
                    rowsToProcess = PKGGridView.SelectedRows.Cast<DataGridViewRow>();
                }
                else if (selectionType == PKGSelectionType.ALL)
                {
                    if (PKGGridView.Rows.Count == 0) return list;
                    rowsToProcess = PKGGridView.Rows.Cast<DataGridViewRow>();
                }

                if (rowsToProcess == null) return list;

                if (sortAddon)
                {
                    rowsToProcess = rowsToProcess.OrderByDescending(row => row.Cells[8].Value);
                }

                foreach (DataGridViewRow row in rowsToProcess)
                {
                    if (row.Cells[0].Value == null || row.Cells[13].Value == null) continue;
                    string filename = row.Cells[0].Value.ToString();
                    string path = row.Cells[13].Value.ToString();
                    string pkgPath = Path.Combine(path, filename);
                    list.Add(pkgPath);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting PKG list: {ex.Message}");
            }
            return list;
        }

        private void ImageIconExtractor(string imageType, List<string> pkgFilesList, string outputDirectory, bool respectiveExtract)
        {
            int countPkg = 0;
            int total = pkgFilesList.Count;
            int batchSize = 10;
            foreach (string pkgPath in pkgFilesList)
            {
                try
                {
                    string pkgName = Path.GetFileNameWithoutExtension(pkgPath);
                    string finalPath = respectiveExtract
                        ? $"{outputDirectory}\\{pkgName}\\"
                        : outputDirectory;

                    Directory.CreateDirectory(finalPath);

                    byte[] icon = null, pic0 = null, pic1 = null;
                    switch (imageType)
                    {
                        case ImageIconExtractionType.ALL:
                            icon = PkgImageReader.ReadIcon0Png(pkgPath);
                            pic0 = PkgImageReader.ReadPic0Png(pkgPath);
                            pic1 = PkgImageReader.ReadPic1Png(pkgPath);
                            break;
                        case ImageIconExtractionType.ICON:
                            icon = PkgImageReader.ReadIcon0Png(pkgPath);
                            break;
                        case ImageIconExtractionType.IMAGE:
                            pic0 = PkgImageReader.ReadPic0Png(pkgPath);
                            pic1 = PkgImageReader.ReadPic1Png(pkgPath);
                            break;
                    }

                    string baseFileName = respectiveExtract ? "" : pkgName + "_";
                    if (icon != null) SavePng(icon, Path.Combine(finalPath, $"{baseFileName}ICON.PNG"));
                    if (pic0 != null) SavePng(pic0, Path.Combine(finalPath, $"{baseFileName}PIC0.PNG"));
                    if (pic1 != null) SavePng(pic1, Path.Combine(finalPath, $"{baseFileName}PIC1.PNG"));
                }
                catch (Exception a)
                {
                    Helper.Bitmap.FailExtractImageList += Path.GetFileNameWithoutExtension(pkgPath) + " : " + a.Message + "\n";
                }
                countPkg++;
                if (countPkg % batchSize == 0 || countPkg == total)
                {
                    int current = countPkg;
                    int max = total;
                    this.Invoke((MethodInvoker)delegate
                    {
                        toolStripProgressBar1.Minimum = 0;
                        toolStripProgressBar1.Maximum = 100;
                        toolStripProgressBar1.Value = (int)(100.0 * current / max);
                        toolStripStatusLabel2.Text = $"Saving artwork.. ({current}/{max})";
                    });
                }
            }
        }

        private void SavePng(byte[] imageBytes, string filePath)
        {
            using (var ms = new MemoryStream(imageBytes))
            using (var bmp = Image.FromStream(ms))
            {
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 50L);
                var pngCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Png.Guid);
                bmp.Save(filePath, pngCodec, encoderParams);
            }
        }

        private void ImageIconPostExtraction()
        {
            if (!string.IsNullOrEmpty(Helper.Bitmap.FailExtractImageList))
            {
                Logger.LogInformation("Artwork extraction completed with errors.");
                ShowWarning("Some PKG fail to extract : \n\n" + Helper.Bitmap.FailExtractImageList, false);
                Logger.LogWarning("Some PKG fail to extract : \n\n" + Helper.Bitmap.FailExtractImageList);
            }
            else
            {
                Logger.LogInformation("Artwork saved successfully.");
                ShowInformation("Artwork saved.", true);
            }

            toolStripStatusLabel2.Text = "... ";
            toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
            toolStripProgressBar1.Value = 0;
            this.Enabled = true;
        }

        private void MorePKGTool(string type, DataTable dataTable = null, string excelFilename = null)
        {
            this.Enabled = false;
            PS4_Tools.PKG.SceneRelated.Unprotected_PKG PS4_PKG = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);

            switch (type)
            {
                case "ENTRY":
                    // Code for the "ENTRY" button
                    break;

                case "TROPHY":
                    // Code for the "TROPHY" button
                    break;

                case PKGCategory.ADDON:
                    if (Tool.CheckForInternetConnection())
                    {
                        if (PKGGridView.GetCellCount(DataGridViewElementStates.Selected) > 0)
                        {
                            try
                            {
                                string CUSA_DLC = "";
                                string CONTENTID_DLC = "";
                                foreach (DataGridViewCell cell in PKGGridView.SelectedCells)
                                {
                                    int selectedrowindex = cell.RowIndex;
                                    DataGridViewRow selectedRow = PKGGridView.Rows[selectedrowindex];
                                    CUSA_DLC = Convert.ToString(selectedRow.Cells[2].Value);
                                    CONTENTID_DLC = Convert.ToString(selectedRow.Cells[2].Value);
                                }

                                if (CUSA_DLC != null && CONTENTID_DLC != null)
                                {
                                    try
                                    {
                                        PKG.StoreItems = PS4_Tools.PKG.Official.Get_All_Store_Items("CUSA07022");
                                    }
                                    catch
                                    {
                                        PKG.StoreItems = null;
                                    }

                                    if (PKG.StoreItems.Count > 0)
                                    {
                                        DLC grid = new DLC(PKG.StoreItems);
                                        toolStripStatusLabel2.Text = "Viewing addon.. ";
                                        BeginInvoke((MethodInvoker)delegate
                                        {
                                            grid.ShowDialog();
                                        });
                                    }
                                    else
                                    {
                                        ShowInformation("\"" + PS4_PKG.PS4_Title + "\" has no Addon", true);
                                    }
                                }
                                else
                                {
                                    ShowError("An error occurred", true);

                                }
                            }
                            catch (System.Runtime.InteropServices.ExternalException)
                            {
                                ShowError("The Clipboard could not be accessed. Please try again.", true);
                            }
                        }
                    }
                    else
                    {
                        ShowError("Network is not Available", true);
                    }
                    break;

                case "EXPORT":
                    try
                    {
                        int rows = dataTable?.Rows.Count ?? 0;
                        Logger.LogInformation($"Exporting {rows} PKG(s) to Excel...");
                        toolStripStatusLabel2.Text = "Exporting PKG list.. ";
                        var wb = new XLWorkbook();
                        wb.Worksheets.Add(dataTable, "PS4 PKG");
                        wb.SaveAs(excelFilename);
                        Logger.LogInformation($"Exported {rows} PKG(s) to \"{excelFilename}\".");
                        ShowInformation($"PKG list exported.", false);
                    }
                    catch (Exception s)
                    {
                        Logger.LogInformation($"ERROR: Export failed: {s.Message}");
                        ShowError(s.Message, true);
                    }
                    break;
            }
            this.Enabled = true; // re-enable on every exit path (ADDON/ENTRY/TROPHY leaves the form disabled otherwise)
        }

        private void CopyContentID()
        {
            var ids = new List<string>();
            foreach (DataGridViewRow row in PKGGridView.SelectedRows)
            {
                string cid = row.Cells[3].Value?.ToString();
                if (!string.IsNullOrEmpty(cid)) ids.Add(cid);
            }
            if (ids.Count == 0) { ShowError("No PKG file selected.", false); return; }
            Clipboard.SetText(string.Join("\n", ids));
            ShowInformation($"{ids.Count} Content ID(s) copied to clipboard.", true);
        }

        private void CopyTitle()
        {
            var titles = new List<string>();
            foreach (DataGridViewRow row in PKGGridView.SelectedRows)
            {
                string title = row.Cells[1].Value?.ToString();
                if (!string.IsNullOrEmpty(title)) titles.Add(title);
            }
            if (titles.Count == 0) { ShowError("No PKG file selected.", false); return; }
            Clipboard.SetText(string.Join("\n", titles));
            ShowInformation($"{titles.Count} Title(s) copied to clipboard.", true);
        }

        private void CopyFilename()
        {
            var filenames = new List<string>();
            foreach (DataGridViewRow row in PKGGridView.SelectedRows)
            {
                string fn = row.Cells[0].Value?.ToString();
                if (!string.IsNullOrEmpty(fn)) filenames.Add(fn);
            }
            if (filenames.Count == 0) { ShowError("No PKG file selected.", false); return; }
            Clipboard.SetText(string.Join("\n", filenames));
            ShowInformation($"{filenames.Count} Filename(s) copied to clipboard.", true);
        }

        private void RefreshPkgList()
        {
            flatTabControl1.SelectedIndex = 0;
            Logger.LogInformation("Refreshing PKG list..");
            Logger.LogInformation("Refreshing PKG list...");

            // Clear existing data
            PKGGridView.DataSource = null;
            darkDataGridView2.DataSource = null;
            FinalizePkgProcess = true;

            // Keep existing source (manifest vs directory), just re-scan
            Helper.LaunchEmpty = false;

            this.Enabled = false;
            PKGGridView.Enabled = false;
            darkDataGridView2.Enabled = false;
            LoadPKGGridView();
        }

        private void toolStripMenuItem78_Click(object sender, EventArgs e)
        {
            DialogResult dialog = DialogResultYesNo("Are you sure you wish to exit?");
            if (dialog == DialogResult.Yes)
            {
                Application.Exit();
            }
        }

        /// <summary>
        /// File → Load from manifest: reload the cached manifest without rescanning directories.
        /// Falls back to a directory scan if the manifest is invalid or outdated (handled in LoadPKGGridView).
        /// </summary>
        private void loadFromManifestToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ManifestHelper.ManifestExists())
            {
                ShowWarning("No manifest found. Scan a PKG directory first.", false);
                return;
            }
            Logger.LogInformation("Loading PKG list from manifest..");
            Helper.LoadFromManifest = true;
            RefreshPkgList();
        }

        /// <summary>
        /// File → Load from saved directory: force a full rescan of the configured PKG
        /// directories (picks up newly added files), regardless of the current source.
        /// Mirrors the "Scan from directory" startup mode.
        /// </summary>
        private void loadFromDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Logger.LogInformation("Loading PKG list from saved directories..");
            Helper.LoadFromManifest = false;
            RefreshPkgList();
        }

        /// <summary>
        /// File → Save manifest: manually write the manifest cache from the current grid.
        /// Useful after operations that move/rename files outside the app, or to snapshot
        /// the current library state.
        /// </summary>
        private void saveManifestToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var dt = PKGGridView.DataSource as DataTable;
            if (dt == null || dt.Rows.Count == 0)
            {
                ShowWarning("Nothing to save - the PKG list is empty.", false);
                return;
            }
            Logger.LogInformation("Saving manifest manually..");
            SaveManifestAfterScan();
            ShowInformation("Manifest saved.", true);
        }

        /// <summary>
        /// File → Empty list: clear the library (grid, grouped view, tree, counters) and the
        /// manifest cache. PKG files on disk are NOT touched. Mirrors the "Launch empty" startup mode.
        /// </summary>
        private void emptyListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (DialogResultYesNo("Clear the PKG list and the cached manifest?\n\nPKG files on disk will NOT be deleted.") != DialogResult.Yes)
                return;

            Logger.LogInformation("Emptying PKG list..");
            Helper.LoadFromManifest = false;
            _detailWorker?.CancelAsync();                       // invalidate in-flight detail loads
            Interlocked.Increment(ref _trophyLoadVersion);      // invalidate in-flight trophy loads
            PKG.SelectedPKGFilename = "";
            PKG.VerifiedPs4PkgList.Clear();
            PKG.EntryIdList.Clear();
            PKG.EntryNameList.Clear();
            PKG.pkgCount = 0;
            PKG.game = 0;
            PKG.patch = 0;
            PKG.addon = 0;
            PKG.app = 0;
            PKG.unknown = 0;
            PKG.official = 0;
            PKG.fake = 0;
            PKG.unlockerAddon = 0;
            _fileSizes.Clear();
            _pkgDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ClearFileBrowser();
            groupedListView?.Clear();
            ManifestHelper.DeleteManifest();                    // without this the library would resurrect on restart
            InitializeEmptyGrid();
            labelDisplayTotalPKG.Text = "Displaying 0 PS4 PKG";
            toolStripStatusLabel2.Text = "Ready (empty)";
            Logger.LogInformation("PKG list emptied.");
        }

        private void toolStripMenuItem160_Click(object sender, EventArgs e)
        {
            Tool.OpenWebLink("https://ko-fi.com/pearlxcore");
        }

        private async void toolStripMenuItem158_Click(object sender, EventArgs e)
        {
            if (!Tool.CheckForInternetConnection())
            {
                ShowWarning("No internet connection detected. Cannot check for updates.", false);
                return;
            }

            try
            {
                Logger.LogInformation("Checking for latest PS4 PKG Tool..");
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var checker = new UpdateChecker("pearlxcore", "PS4-PKG-Tool", "v" + ApplicationVersion);

                UpdateType update = await checker.CheckUpdate();

                if (update == UpdateType.None)
                {
                    ShowInformation("The program is up to date.", true);
                }
                else
                {
                    var result = new UpdateNotifyDialog(checker).ShowDialog();
                    if (result == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start("https://github.com/pearlxcore/PS4-PKG-Tool/releases");
                    }
                }
            }
            catch (Exception ex)
            {
                // GitHubUpdate throws on API errors, rate limits (60 req/hr unauthenticated),
                // or malformed release tags - never let that crash the app.
                Logger.LogError("Update check failed: " + ex.Message);
                ShowWarning("Could not check for updates. Please try again later.\n\n" + ex.Message, false);
            }
        }

        private void toolStripMenuItem159_Click(object sender, EventArgs e)
        {
            using (var about = new AboutForm(ApplicationVersion))
            {
                about.ShowDialog();
            }
        }

        private static string GetRenameFormat(int formatIndex)
        {
            return formatIndex switch
            {
                1 => NamingFormat.TITLE,
                2 => $"{NamingFormat.TITLE} [{NamingFormat.TITLE_ID}]",
                3 => $"{NamingFormat.TITLE} [{NamingFormat.TITLE_ID}] [{NamingFormat.APP_VERSION}]",
                4 => $"{NamingFormat.TITLE} [{NamingFormat.CATEGORY}]",
                5 => NamingFormat.TITLE_ID,
                6 => $"{NamingFormat.TITLE_ID} [{NamingFormat.TITLE}]",
                7 => $"[{NamingFormat.TITLE_ID}] [{NamingFormat.CATEGORY}] [{NamingFormat.APP_VERSION}] {NamingFormat.TITLE}",
                8 => $"{NamingFormat.TITLE} [{NamingFormat.CATEGORY}] [{NamingFormat.VERSION}]",
                9 => NamingFormat.CONTENT_ID,
                10 => NamingFormat.CONTENT_ID2,
                _ => null, // 11 = custom
            };
        }

        private void RenamePkg_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            // Detect format index and scope from the menu item field name
            // Names follow: rename{All|Selected}Pkg{N}ToolStripMenuItem{1|2}
            string name = clickedMenuItem.Name;
            bool isAll = name.StartsWith("renameAllPkg", StringComparison.Ordinal);
            string selectionType = isAll ? PKGSelectionType.ALL : PKGSelectionType.SELECTED;

            // Extract format number from name (between "Pkg" and "ToolStripMenuItem")
            int pkgIdx = name.IndexOf("Pkg", StringComparison.Ordinal) + 3;
            int toolIdx = name.IndexOf("ToolStripMenuItem", StringComparison.Ordinal);
            if (pkgIdx < 3 || toolIdx < 0 || !int.TryParse(name.Substring(pkgIdx, toolIdx - pkgIdx), out int fmtNum))
                return;

            // Format 12 = Sort by Install Priority (handled by RenamePKGByPriority)
            if (fmtNum == 12)
            {
                var priorityList = GetSelectedPKGDirectoryList(selectionType);
                if (priorityList.Count == 0) { ShowError("No PKG files to rename.", false); return; }
                var priorityConfirm = DialogResultYesNo(
                    $"Re-sort {priorityList.Count} PKG file{(priorityList.Count == 1 ? "" : "s")} by install priority?\n\n" +
                    "Files will be grouped by Title ID and renamed with sequence prefixes:\n" +
                    "  00 - Base -> 01 - Update\n\nAdd-on and App PKGs are skipped.\n\nContinue?");
                if (priorityConfirm == DialogResult.No) return;
                Logger.LogInformation($"Rename by priority: {priorityList.Count} PKG(s)...");
                var priorityBg = new BackgroundWorker { WorkerReportsProgress = true };
                priorityBg.DoWork += (_, _) => RenamePKGByPriority(priorityList, priorityBg);
                priorityBg.ProgressChanged += (_, e) =>
                {
                    toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                    toolStripProgressBar1.Maximum = 100;
                    toolStripProgressBar1.Value = e.ProgressPercentage;
                    toolStripStatusLabel2.Text = e.UserState?.ToString() ?? "...";
                };
                priorityBg.RunWorkerCompleted += (_, _) =>
                {
                    try
                    {
                        if (PKG.CountFailRename > 0)
                            ShowWarning(PKG.CountFailRename + " PKG failed to rename by priority. See program log to view the errors.", false);
                        else
                            ShowInformation("PKG rename by priority done.", true);
                        Logger.LogInformation($"Rename by priority: done.");
                        SaveManifestAfterScan();
                        // GLV cells updated in-place by UpdatePKGFilename during rename
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Priority rename completion failed: " + ex.Message);
                    }
                    finally
                    {
                        // Always restore the form and menus - otherwise the GLV context menu
                        // stays disabled after this operation.
                        toolStripStatusLabel2.Text = "...";
                        toolStripProgressBar1.Value = 0;
                        this.Enabled = true;
                        SetOperationMenusEnabled(true);
                    }
                };
                this.Invoke((Action)(() => this.Enabled = false));
                priorityBg.RunWorkerAsync();
                return;
            }

            string format;
            if (fmtNum == 11)
            {
                if (string.IsNullOrEmpty(appSettings_.RenameCustomName))
                { ShowError("Set custom name format in settings.", true); return; }
                format = appSettings_.RenameCustomName;
            }
            else
            {
                format = GetRenameFormat(fmtNum);
                if (format == null) return;
            }

            // Warn if a filter is active for "All" rename
            if (isAll)
            {
                var dv = (PKGGridView.DataSource as DataTable)?.DefaultView;
                if (dv != null && !string.IsNullOrEmpty(dv.RowFilter))
                {
                    var result = DialogResultYesNo(
                        "A filter is currently active. Rename All will affect ALL PKGs including those hidden by the filter.\n\nContinue?");
                    if (result == DialogResult.No) return;
                }
            }

            var pkgList = GetSelectedPKGDirectoryList(selectionType);
            if (pkgList.Count == 0) { ShowError("No PKG files to rename.", false); return; }

            // Build preview example for confirmation
            string previewName = format
                .Replace("{TITLE}", "{Title}")
                .Replace("{TITLE_ID}", "CUSA00000")
                .Replace("{APP_VERSION}", "1.00")
                .Replace("{VERSION}", "1.00")
                .Replace("{CATEGORY}", "Game")
                .Replace("{CONTENT_ID}", "xxxxx")
                .Replace("{CONTENT_ID2}", "xxxxx")
                .Replace("{REGION}", "EU")
                .Replace("{SYSTEM_VERSION}", "9.00");

            var confirmResult = DialogResultYesNo(
                $"Rename {pkgList.Count} PKG file{(pkgList.Count == 1 ? "" : "s")}?\n\nFormat: {format}\nExample: {previewName}.pkg");
            if (confirmResult == DialogResult.No) return;

            RenamePKG(format, pkgList);
        }

        private void ExportPKGToExcel_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            // global
            if (clickedMenuItem == globalExportPKGListToExcelToolStripMenuItem1 || clickedMenuItem == globalExportPKGListToExcelToolStripMenuItem2)
            {
                InitializedExportPKGToExcel(GenerateDatatableFromSelectedPKG(PKGSelectionType.ALL));
            }

            // selected
            if (clickedMenuItem == selectedExportPKGListToExcelToolStripMenuItem1 || clickedMenuItem == selectedExportPKGListToExcelToolStripMenuItem2)
            {
                InitializedExportPKGToExcel(GenerateDatatableFromSelectedPKG(PKGSelectionType.SELECTED));
            }
        }

        private void InitializedExportPKGToExcel(DataTable dataTable = null)
        {
            if (ShowSaveFileDialog("Export PKG List to Excel", "*.xlsx|*.xlsx", out SaveFileDialog sfd))
            {
                string excelFilename = sfd.FileName;
                var bg = new BackgroundWorker();
                bg.DoWork += delegate
                {
                    MorePKGTool("EXPORT", dataTable, excelFilename);
                };
                bg.RunWorkerCompleted += delegate
                {
                    toolStripStatusLabel2.Text = "... ";
                    this.Enabled = true;
                };
                bg.RunWorkerAsync();
            }
        }

        private void CopyTitleID()
        {
            var ids = new List<string>();
            foreach (DataGridViewRow row in PKGGridView.SelectedRows)
            {
                string tid = row.Cells[2].Value?.ToString();
                if (!string.IsNullOrEmpty(tid)) ids.Add(tid);
            }
            if (ids.Count == 0) { ShowError("No PKG file selected.", false); return; }
            Clipboard.SetText(string.Join("\n", ids));
            ShowInformation($"{ids.Count} Title ID(s) copied to clipboard.", true);
        }

        private void CopyID_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            if (clickedMenuItem == copyTitleIdtoolStripMenuItem1 || clickedMenuItem == copyTitleIdtoolStripMenuItem2)
            {
                CopyTitleID();
            }
            if (clickedMenuItem == copyContentIdtoolStripMenuItem1 || clickedMenuItem == copyContentIdtoolStripMenuItem2)
            {
                CopyContentID();
            }
            if (clickedMenuItem == copyTitleToolStripMenuItem1 || clickedMenuItem == copyTitleToolStripMenuItem2)
            {
                CopyTitle();
            }
            if (clickedMenuItem == copyFilenameToolStripMenuItem1 || clickedMenuItem == copyFilenameToolStripMenuItem2)
            {
                CopyFilename();
            }
        }

        private void ViewPKGExplorer_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            if (clickedMenuItem == viewPkgExplorerStripMenuItem1 || clickedMenuItem == viewPkgExplorerStripMenuItem2)
            {
                ViewPKGInExplorer();
            }
        }

        private void panel5_Paint(object sender, PaintEventArgs e)
        {
            ControlPaint.DrawBorder(e.Graphics, panel5.ClientRectangle, Color.Black, ButtonBorderStyle.Solid);
        }

        #region ImageIconExtractor
        private void InitializedImageIconExtractor(string imageType, string selectionType)
        {
            DialogResult extractionDialog = DialogResultYesNoCancel("Create subfolder for each PKG?");

            if (extractionDialog == DialogResult.Cancel)
                return;

            var respectiveExtract = (extractionDialog == DialogResult.Yes);

            if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
            {
                Logger.LogInformation("Saving artwork..");
                toolStripStatusLabel2.Text = "Saving artwork..";
                var outputDirectory = fbd.SelectedPath;
                var pkgList = GetSelectedPKGDirectoryList(selectionType);
                Logger.LogInformation($"Save artwork: {pkgList.Count} PKG(s)");
                toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                toolStripProgressBar1.Minimum = 0;
                toolStripProgressBar1.Maximum = 100;
                toolStripProgressBar1.Value = 0;
                var backgroundWorker = new BackgroundWorker();
                backgroundWorker.DoWork += (s, e) =>
                {
                    ImageIconExtractor(imageType, pkgList, outputDirectory, respectiveExtract);
                };
                backgroundWorker.RunWorkerCompleted += (s, e) =>
                {
                    ImageIconPostExtraction();
                };
                backgroundWorker.RunWorkerAsync();
            }
        }

        private void ExtractImageIcon_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem item && item.Tag is string tag)
            {
                var parts = tag.Split('|');
                InitializedImageIconExtractor(parts[0], parts[1]);
            }
        }
        #endregion ImageIconExtractor

        #region PKGScanning
        private void toolStripMenuItem15_Click(object sender, EventArgs e)
        {
            OpenPKGDirectorySettings();
        }

        private void OpenPKGDirectorySettings()
        {
            OpenProgramSettings();
        }

        private void managePS4PKGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenPKGDirectorySettings();
        }

        static bool IsExcluded(List<string> exludedDirList, string target)
        {
            return exludedDirList.Any(d => new DirectoryInfo(target).Name.Equals(d));
        }

        private bool IsRootDirectory(string path)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            return directoryInfo.Parent == null;
        }

        public static void autoResizeColumns(ListView lv)
        {
            lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
            ListView.ColumnHeaderCollection cc = lv.Columns;
            for (int i = 0; i < cc.Count; i++)
            {
                int colWidth = TextRenderer.MeasureText(cc[i].Text, lv.Font).Width + 10;
                if (colWidth > cc[i].Width)
                {
                    cc[i].Width = colWidth;
                }
            }
        }


        private void PKGGridView_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void WireAllControls(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Form) continue; // already wired separately
                c.AllowDrop = true;
                c.DragEnter += PKGGridView_DragEnter;
                c.DragOver += (s, ev) => { if (ev.Data.GetDataPresent(DataFormats.FileDrop)) ev.Effect = DragDropEffects.Copy; };
                c.DragDrop += PKGGridView_DragDrop;
                WireAllControls(c);
            }
        }

        private void PKGGridView_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = null;
            try { files = (string[])e.Data.GetData(DataFormats.FileDrop); } catch (Exception ex) { Logger.LogWarning("Drag-drop data retrieval failed: " + ex.Message); }
            if (files == null || files.Length == 0) return;

            // Collect valid folder paths first.
            var folders = new List<string>();
            foreach (string path in files)
            {
                string folderPath = path;
                if (File.Exists(path))
                {
                    if (Path.GetExtension(path).ToUpperInvariant() != ".PKG") continue;
                    folderPath = Path.GetDirectoryName(path);
                }
                else if (!Directory.Exists(folderPath))
                    continue;
                if (!folders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                    folders.Add(folderPath);
            }

            if (folders.Count == 0) return;

            // One dialog for all folders - recursive + add-to-directories options.
            using (var prompt = new DropFolderPrompt(folders))
            {
                prompt.ShowDialog();
                if (!prompt.Confirmed) return;

                if (prompt.AddToDirectories)
                {
                    foreach (var f in prompt.FolderPaths)
                    {
                        if (!appSettings_.PkgDirectories.Any(d =>
                            string.Equals(d, f, StringComparison.OrdinalIgnoreCase)))
                        {
                            appSettings_.PkgDirectories.Add(f);
                        }
                    }
                    SettingsManager.SaveSettings(appSettings_, SettingFilePath);
                }

                // Scan sequentially (shared DataTable, no race).
                var pending = new Queue<string>(prompt.FolderPaths);
                var first = pending.Dequeue();
                ScanDroppedFolder(first, prompt.ScanRecursively, pending);
            }
        }

        private void ScanDroppedFolder(string folderPath, bool recursive,
            Queue<string> pending = null)
        {
            this.Invoke((MethodInvoker)delegate
            {
                this.Enabled = false;
                PKGGridView.Enabled = false;
                darkDataGridView2.Enabled = false;
                SetOperationMenusEnabled(false);
                toolStripProgressBar1.Visible = true;
                toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                toolStripProgressBar1.Value = 0;
            });

            DataTable scanDt = null;
            bool anyPkgsFound = false; // track whether any files were added
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += (s, args) =>
            {
                var pkgFiles = new List<string>();
                try
                {
                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    pkgFiles = Directory.EnumerateFiles(folderPath, "*.PKG", searchOption).ToList();
                }
                catch (UnauthorizedAccessException ex)
                {
                    Logger.LogError(ex.Message);
                    return;
                }

                int totalFiles = pkgFiles.Count;
                anyPkgsFound = totalFiles > 0;
                this.Invoke((MethodInvoker)delegate
                {
                    toolStripProgressBar1.Maximum = totalFiles;
                });

                if (totalFiles == 0)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        toolStripStatusLabel2.Text = $"No PKG files found in {folderPath}.";
                    });
                    return;
                }

                DataTable dt = PKGGridView.DataSource as DataTable;
                if (dt == null)
                {
                    dt = new DataTable();
                    dt.Columns.Add("Filename");
                    dt.Columns.Add("Title");
                    dt.Columns.Add("Title ID");
                    dt.Columns.Add("Content ID");
                    dt.Columns.Add("Region", typeof(byte[]));
                    dt.Columns.Add("System Version");
                    dt.Columns.Add("Version [App Version]");
                    dt.Columns.Add("PKG Type");
                    dt.Columns.Add("Category");
                    dt.Columns.Add("Size");
                    dt.Columns.Add("PSVR");
                    dt.Columns.Add("PS4 Pro Enhanced");
                    dt.Columns.Add("PS5 BC");
                    dt.Columns.Add("Directory");
                    dt.Columns.Add("Backported");
                    dt.Columns.Add("Latest Update");
                    dt.Columns.Add("ShadPS4 (Windows)");
                    this.Invoke((MethodInvoker)delegate { PKGGridView.DataSource = dt; });
                }
                // Detach DataTable from DGV while adding rows on background thread to prevent STA exceptions
                scanDt = dt;
                this.Invoke((MethodInvoker)delegate { PKGGridView.DataSource = null; });

                // ── Pre-compute loop-invariant data ────────────────
                var verRegex2 = new Regex(@"^0+(?=\d+\.)", RegexOptions.Compiled);
                var imgCvt2 = new ImageConverter();
                var regionIconCache = new Dictionary<string, byte[]>
                {
                    [PKGRegion.EU] = (byte[])imgCvt2.ConvertTo(Properties.Resources.eu, typeof(byte[])),
                    [PKGRegion.US] = (byte[])imgCvt2.ConvertTo(Properties.Resources.us, typeof(byte[])),
                    [PKGRegion.UK] = (byte[])imgCvt2.ConvertTo(Properties.Resources.us, typeof(byte[])),
                    [PKGRegion.JAPAN] = (byte[])imgCvt2.ConvertTo(Properties.Resources.jp, typeof(byte[])),
                    [PKGRegion.HONG_KONG] = (byte[])imgCvt2.ConvertTo(Properties.Resources.hk, typeof(byte[])),
                    [PKGRegion.ASIA] = (byte[])imgCvt2.ConvertTo(Properties.Resources.asia, typeof(byte[])),
                    [PKGRegion.KOREA] = (byte[])imgCvt2.ConvertTo(Properties.Resources.kr, typeof(byte[])),
                };
                bool chkBackport2 = File.Exists(Backport.BackportInfoFile);
                var backportCache2 = chkBackport2 ? Backport.LoadCache() : null;
                dynamic ps5BcCache2 = null;
                bool usePs5Bc2 = appSettings_.psvr_neo_ps5bc_check && File.Exists(Ps5BcJsonFile);
                if (usePs5Bc2) { try { ps5BcCache2 = JsonConvert.DeserializeObject(File.ReadAllText(Ps5BcJsonFile)); } catch { usePs5Bc2 = false; } }
                // HashSet for O(1) duplicate detection
                var existingSet = new HashSet<string>(PKG.VerifiedPs4PkgList, StringComparer.OrdinalIgnoreCase);

                int added = 0;
                int processed = 0;
                foreach (string pkgFile in pkgFiles)
                {
                    processed++;
                    if (processed % 10 == 0 || processed == totalFiles)
                    {
                        int p = processed, t = totalFiles;
                        this.Invoke((MethodInvoker)delegate { toolStripStatusLabel2.Text = $"Loading PS4 PKG.. ({p}/{t})"; toolStripProgressBar1.Increment(10); });
                    }
                    // O(1) duplicate check
                    if (!existingSet.Add(pkgFile)) continue;

                    try
                    {
                        PS4_Tools.PKG.SceneRelated.Unprotected_PKG ps4Pkg = PS4_Tools.PKG.SceneRelated.Read_PKG(pkgFile);
                        string pkgAppVersion = verRegex2.Replace(ps4Pkg.Param.APP_VER, "");
                        string pkgMinFirmware = ps4Pkg.PKG_Type.ToString() == PKGCategory.ADDON ? "NA" : "";
                        string pkgVersion = "";
                        foreach (Param_SFO.PARAM_SFO.Table t in ps4Pkg.Param.Tables.ToList())
                        {
                            if (t.Name == "SYSTEM_VER")
                            {
                                int value = Convert.ToInt32(t.Value);
                                if (value != 0)
                                    pkgMinFirmware = $"{(value >> 8) & 0xFF}.{value & 0xFF:D2}";
                                else pkgMinFirmware = t.Value;
                            }
                            if (t.Name == "VERSION") pkgVersion = verRegex2.Replace(t.Value, "");
                        }
                        pkgAppVersion = (pkgAppVersion == string.Empty) ? "NA" : pkgAppVersion;

                        string pkgFileName = Path.GetFileName(pkgFile);
                        string pkgDirectoryName = Path.GetDirectoryName(pkgFile);
                        string pkgSize = ByteSize.FromBytes(new System.IO.FileInfo(pkgFile).Length).ToString();
                        string pkgState = ps4Pkg.PKGState.ToString();
                        string pkgType = ps4Pkg.PKG_Type.ToString();

                        byte[] pkgRegionIcon = null;
                        regionIconCache.TryGetValue(ps4Pkg.Region, out pkgRegionIcon);

                        string psVr = "", neoEnable = "", ps5bc = "";
                        if (usePs5Bc2 && ps5BcCache2 != null && pkgType == PKGCategory.GAME)
                        {
                            foreach (var item in ps5BcCache2)
                            {
                                if (item.npTitleIdshort == ps4Pkg.Param.TITLEID)
                                {
                                    string psvr = item.psVr, neo = item.neoEnable, pbc = item.ps5bc;
                                    psVr = (psvr == "1" || psvr == "2") ? "Yes" : (psvr == "0") ? "No" : (psvr != "null") ? "NA" : "";
                                    neoEnable = (neo == "1") ? "Yes" : (neo == "0") ? "No" : (neo != "null") ? "NA" : "";
                                    ps5bc = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(pbc.Replace("_", " ").ToLower());
                                }
                            }
                        }
                        else if (usePs5Bc2) { psVr = neoEnable = ps5bc = "-"; }

                        // Backport check
                        string pkgIsBackported = (backportCache2 != null && backportCache2.TryGetValue(pkgFile, out var bp2)) ? bp2 : "No";

                        dt.Rows.Add(pkgFileName, ps4Pkg.PS4_Title, ps4Pkg.Param.TITLEID, ps4Pkg.Param.ContentID,
                            pkgRegionIcon, pkgMinFirmware, pkgVersion + $" [{pkgAppVersion}]",
                            pkgState, pkgType, pkgSize, psVr, neoEnable, ps5bc,
                            pkgDirectoryName, pkgIsBackported, "NA");

                        // Update type counts
                        switch (ps4Pkg.PKG_Type.ToString())
                        {
                            case PKGCategory.GAME: PKG.game++; break;
                            case PKGCategory.PATCH: PKG.patch++; break;
                            case PKGCategory.APP: PKG.app++; break;
                            case PKGCategory.ADDON: PKG.addon++; break;
                            default: PKG.unknown++; break;
                        }
                        PKG.pkgCount++;
                        added++;
                        lock (PKG.VerifiedPs4PkgList)
                            PKG.VerifiedPs4PkgList.Add(pkgFile); // keep the verified list in sync with the grid
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to process dropped PKG {pkgFile}: {ex.Message}");
                    }
                }
                ApplyShadps4Status(scanDt);

                int finalAdded = added;
                this.Invoke((MethodInvoker)delegate
                {
                    toolStripProgressBar1.Value = 0;
                    toolStripStatusLabel2.Text = $"Added {finalAdded} PKG(s) from {folderPath}. Total: {dt.Rows.Count}";
                    labelDisplayTotalPKG.Text = $"Displaying {dt.Rows.Count} PS4 PKG";
                });

                Logger.LogInformation($"Dropped folder scan complete: {finalAdded} new PKGs from {folderPath}.");
            };
            bw.RunWorkerCompleted += (s, args) =>
            {
                // Consume the one-shot BGM flag so a subsequent directory scan
                // doesn't fire BGM extraction at the wrong time (the flag was
                // set during app startup and drag-drop never consumed it).
                FinalizePkgProcess = false;

                if (!anyPkgsFound)
                {
                    // No PKG files in the dropped folder - re-enable the form
                    // but don't run the success finalization (no data changed).
                    this.Invoke((MethodInvoker)delegate
                    {
                        this.Enabled = true;
                        PKGGridView.Enabled = true;
                        darkDataGridView2.Enabled = true;
                        SetOperationMenusEnabled(PKGGridView.Rows.Count > 0);
                    });
                    return;
                }

                Logger.LogInformation($"Drag-drop scan done. Folder: {folderPath}");
                this.Invoke((MethodInvoker)delegate
                {
                    this.Enabled = true;
                    PKGGridView.Enabled = true;
                    darkDataGridView2.Enabled = true;
                });
                // Re-attach DataTable to DGV on UI thread
                this.Invoke((MethodInvoker)delegate { if (scanDt != null) PKGGridView.DataSource = scanDt; });
                // Enable menus first - a failure below must never leave the GLV context menu disabled
                SetOperationMenusEnabled(true);
                try
                {
                    UpdateDataGridViewColumnVisibility();
                    SaveManifestAfterScan();
                    PopulateGroupedView();
                    PKGGridView.Sort(PKGGridView.Columns[0], ListSortDirection.Ascending);
                    if (appSettings_.AutoFetchUpdate) FetchAllUpdateVersions();
                }
                catch (Exception ex)
                {
                    Logger.LogError("Post-scan refresh failed: " + ex.Message);
                }
                // Finalize the UI: all steps the directory-scan path runs via
                // PostPkgLoad + FinalizePkgLoadingProcess, replicated here so
                // drag-drop behaves identically to the other loading methods.
                this.Invoke((MethodInvoker)delegate
                {
                    GetDrivesFreeSpace();
                    RebuildFilterDropDown();
                    labelDisplayTotalPKG.Text = $"Displaying {PKGGridView.Rows.Count} PS4 PKG";
                    SetBackgroundMusicVolume();
                    SetDataGridViewCellStyle();
                    toolStripStatusLabel2.Text = "... ";
                    toolStripProgressBar1.Value = 0;
                });

                // Chain to the next dropped folder (if any)
                if (pending != null && pending.Count > 0)
                    ScanDroppedFolder(pending.Dequeue(), recursive, pending);
            };
            bw.RunWorkerAsync();
        }

        private void LoadPKGGridView()
        {
            var bw = new BackgroundWorker();
            bw.DoWork += delegate
            {
                #region loadPkgProcess
                this.Invoke((MethodInvoker)delegate
                {
                    PKG.SelectedPKGFilename = null;
                    pictureBox1.Image = null;
                    darkLabel1.Text = "";
                });

                PKG.VerifiedPs4PkgList.Clear();
                PKG.EntryIdList.Clear();
                PKG.EntryNameList.Clear();
                //PKG.totalPkg = 0;
                PKG.pkgCount = 0;
                PKG.game = 0;
                PKG.patch = 0;
                PKG.app = 0;
                PKG.unknown = 0;
                PKG.addon = 0;
                toolStripProgressBar1.Value = 0;

                // Load from manifest cache if available (fast startup, no PKG reading)
                if (Helper.LoadFromManifest && ManifestHelper.ManifestExists())
                {
                    try
                    {
                        var manifest = ManifestHelper.LoadManifest();
                        if (manifest != null && ManifestHelper.ValidateManifest(manifest, appSettings_).IsValid)
                        {
                            var (validEntries, removed) = ManifestHelper.FilterValidEntries(manifest.Entries);
                            Logger.LogInformation($"Manifest loaded: {validEntries.Count} entries" + (removed > 0 ? $", {removed} removed" : ""));
                            var dt = ManifestHelper.BuildDataTableFromManifest(validEntries);
                            var pkgPaths = ManifestHelper.BuildPkgPathList(validEntries);

                            PKG.VerifiedPs4PkgList = pkgPaths;
                            PKG.pkgCount = pkgPaths.Count;
                            PKG.game = validEntries.Count(e => e.Category == "Game");
                            PKG.patch = validEntries.Count(e => e.Category == "Patch");
                            PKG.addon = validEntries.Count(e => e.Category == "Addon");
                            PKG.app = validEntries.Count(e => e.Category == "App");
                            PKG.unknown = validEntries.Count(e => e.Category == "Unknown");
                            PKG.official = validEntries.Count(e => e.PkgType == "Official");
                            PKG.fake = validEntries.Count(e => e.PkgType == "Fake");

                            ApplyShadps4Status(dt);
                            this.Invoke((MethodInvoker)delegate
                            {
                                PKGGridView.DataSource = dt;
                            });
                            return; // Skip directory scan - PostPkgLoad runs in RunWorkerCompleted
                        }
                        else
                        {
                            Logger.LogInformation("Manifest invalid or expired. Falling back to directory scan.");
                            ManifestHelper.DeleteManifest();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to load manifest: {ex.Message}. Falling back to directory scan.");
                    }
                }

                List<string> PkgFileList = new List<string>();
                var PkgDirectoryList = appSettings_.PkgDirectories;
                foreach (var directory in PkgDirectoryList)
                {
                    var searchOption = appSettings_.ScanRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    try
                    {
                        var allDirectories = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly);

                        var directoriesToScan = new List<string> { directory };
                        directoriesToScan.AddRange(allDirectories.Where(dir => !ExcludedDirectoryList.Contains(Path.GetFileName(dir))));

                        foreach (var directory_ in directoriesToScan)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                toolStripStatusLabel2.Text = "Scanning directory.. " + "(" + directory_ + ") ";
                            });

                            try
                            {
                                var pkgFiles = Directory.EnumerateFiles(directory_, "*.PKG", searchOption);
                                PkgFileList.AddRange(pkgFiles);
                            }
                            catch (UnauthorizedAccessException e)
                            {
                                Logger.LogError(e.Message);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        Logger.LogError(e.Message);
                    }
                }

                PkgFileList = PkgFileList.Distinct().ToList();

                // datatable for gridview control
                DataTable dttemp = new DataTable();
                dttemp.Columns.Add("Filename");
                dttemp.Columns.Add("Title");
                dttemp.Columns.Add("Title ID");
                dttemp.Columns.Add("Content ID");
                dttemp.Columns.Add("Region", typeof(byte[]));
                dttemp.Columns.Add("System Version");
                dttemp.Columns.Add("Version [App Version]");
                dttemp.Columns.Add("PKG Type");
                dttemp.Columns.Add("Category");
                dttemp.Columns.Add("Size");
                dttemp.Columns.Add("PSVR");
                dttemp.Columns.Add("PS4 Pro Enhanced");
                dttemp.Columns.Add("PS5 BC");
                dttemp.Columns.Add("Directory");
                dttemp.Columns.Add("Backported");
                dttemp.Columns.Add("Latest Update");
                dttemp.Columns.Add("ShadPS4 (Windows)");

                // verify scanned ps4 pkg and count it
                foreach (var item in PkgFileList)
                {
                    //filter ps4 pkg by checking magic byte
                    byte[] bufferA = new byte[16];

                    bufferA = PKG.GetPkgHeaderBuffer(item);
                    if (PKG.CompareBytes(bufferA, PKG.PkgHeader) || PKG.CompareBytes(bufferA, PKG.PkgHeader1) ||
                        PKG.CompareBytes(bufferA, PKG.PkgHeader2) || PKG.CompareBytes(bufferA, PKG.PkgHeader3) ||
                        PKG.CompareBytes(bufferA, PKG.PkgHeader4))
                    {
                        lock (PKG.VerifiedPs4PkgList)
                            PKG.VerifiedPs4PkgList.Add(item);
                        //PKG.totalPkg++;
                    }
                }

                this.Invoke((MethodInvoker)delegate
                {
                    toolStripProgressBar1.Visible = true;
                    //toolStripProgressBar1.Maximum = PKG.totalPkg;
                    toolStripProgressBar1.Maximum = PKG.VerifiedPs4PkgList.Count;
                });

                Logger.LogInformation($"Scanning {PKG.VerifiedPs4PkgList.Count} PKGs from directories...");
                // ── Pre-compute loop-invariant data ────────────────
                var verRegex = new Regex(@"^0+(?=\d+\.)", RegexOptions.Compiled);
                var imageCvt = new ImageConverter();
                // Pre-convert all region icons once
                var regionIcons = new Dictionary<string, byte[]>
                {
                    [PKGRegion.EU] = (byte[])imageCvt.ConvertTo(Properties.Resources.eu, typeof(byte[])),
                    [PKGRegion.US] = (byte[])imageCvt.ConvertTo(Properties.Resources.us, typeof(byte[])),
                    [PKGRegion.UK] = (byte[])imageCvt.ConvertTo(Properties.Resources.us, typeof(byte[])),
                    [PKGRegion.JAPAN] = (byte[])imageCvt.ConvertTo(Properties.Resources.jp, typeof(byte[])),
                    [PKGRegion.HONG_KONG] = (byte[])imageCvt.ConvertTo(Properties.Resources.hk, typeof(byte[])),
                    [PKGRegion.ASIA] = (byte[])imageCvt.ConvertTo(Properties.Resources.asia, typeof(byte[])),
                    [PKGRegion.KOREA] = (byte[])imageCvt.ConvertTo(Properties.Resources.kr, typeof(byte[])),
                };
                bool checkBackport = File.Exists(Backport.BackportInfoFile);
                var backportCache = checkBackport ? Backport.LoadCache() : null;
                // Cache PS5 BC JSON once (not per PKG)
                dynamic ps5BcJsonCache = null;
                bool usePs5Bc = appSettings_.psvr_neo_ps5bc_check && File.Exists(Ps5BcJsonFile);
                if (usePs5Bc)
                {
                    try { ps5BcJsonCache = JsonConvert.DeserializeObject(File.ReadAllText(Ps5BcJsonFile)); }
                    catch { usePs5Bc = false; }
                }

                // Bulk load: suppress index/constraint maintenance during insert
                dttemp.BeginLoadData();

                // process every verified pkg and display into gridview control
                foreach (var pkg in PKG.VerifiedPs4PkgList)
                {
                    PS4_Tools.PKG.SceneRelated.Unprotected_PKG ps4Pkg;
                    try
                    {
                        ps4Pkg = PS4_Tools.PKG.SceneRelated.Read_PKG(pkg);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to read PKG, skipping: {Path.GetFileName(pkg)} - {ex.Message}");
                        continue;
                    }
                    Param_SFO.PARAM_SFO psfo = ps4Pkg.Param;

                    string pkgVersion = "";
                    string pkgAppVersion = verRegex.Replace(psfo.APP_VER, "");
                    string pkgTitleId = ps4Pkg.Param.TITLEID;
                    string pkgFileName = Path.GetFileName(pkg);
                    string pkgDirectoryName = Path.GetDirectoryName(pkg);
                    string psVr = "";
                    string neoEnable = "";
                    string ps5bc = "";
                    string pkgSystemVersion = "";
                    byte[] pkgRegionIcon = null;
                    string pkgState = ps4Pkg.PKGState.ToString();
                    string pkgType = ps4Pkg.PKG_Type.ToString();

                    // get pkg's minimum system fw + version
                    foreach (Param_SFO.PARAM_SFO.Table t in ps4Pkg.Param.Tables.ToList())
                    {
                        if (t.Name == "SYSTEM_VER")
                        {
                            int value = Convert.ToInt32(t.Value);
                            if (value != 0)
                                pkgSystemVersion = $"{(value >> 8) & 0xFF}.{value & 0xFF:D2}";
                            else pkgSystemVersion = t.Value;
                        }
                        if (t.Name == "VERSION")
                            pkgVersion = verRegex.Replace(t.Value, "");
                    }

                    // get pkg full size
                    long fileSizeBytes = new System.IO.FileInfo(pkg).Length;
                    string pkgSize = ByteSize.FromBytes(fileSizeBytes).ToString();

                    // backward compatible info (cached JSON)
                    if (usePs5Bc && ps5BcJsonCache != null)
                    {
                        if (pkgType == PKGCategory.GAME)
                        {
                            foreach (var item in ps5BcJsonCache)
                            {
                                if (item.npTitleIdshort == ps4Pkg.Param.TITLEID)
                                {
                                    string psvr = item.psVr;
                                    string neo = item.neoEnable;
                                    string ps5bc_ = item.ps5bc;
                                    psVr = (psvr == "1" || psvr == "2") ? "Yes" : (psvr == "0") ? "No" : (psvr != "null") ? "NA" : "";
                                    neoEnable = (neo == "1") ? "Yes" : (neo == "0") ? "No" : (neo != "null") ? "NA" : "";
                                    ps5bc = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(ps5bc_.Replace("_", " ").ToLower());
                                }
                            }
                        }
                        else { psVr = neoEnable = ps5bc = "-"; }
                    }

                    // get region icon (pre-computed)
                    var region = ps4Pkg.Region;
                    regionIcons.TryGetValue(region, out pkgRegionIcon);

                    // check if pkg is backported (cached file existence)
                    string pkgIsBackported = (backportCache != null && backportCache.TryGetValue(pkg, out var bp)) ? bp : "No";


                    // add items to datatable
                    string pkgMinFirmware = ps4Pkg.PKG_Type.ToString() == PKGCategory.ADDON ? "NA" : $"{pkgSystemVersion}";
                    pkgAppVersion = (pkgAppVersion == string.Empty) ? "NA" : pkgAppVersion;
                    dttemp.Rows.Add(pkgFileName, ps4Pkg.PS4_Title, pkgTitleId, ps4Pkg.Param.ContentID, pkgRegionIcon, pkgMinFirmware, pkgVersion + $" [{pkgAppVersion}]", pkgState, pkgType, pkgSize, psVr, neoEnable, ps5bc, pkgDirectoryName, pkgIsBackported, "NA");

                    switch (ps4Pkg.PKG_Type.ToString())
                    {
                        case PKGCategory.GAME: PKG.game++; break;
                        case PKGCategory.PATCH: PKG.patch++; break;
                        case PKGCategory.APP: PKG.app++; break;
                        case PKGCategory.ADDON: PKG.addon++; break;
                        default: PKG.unknown++; break;
                    }

                    switch (ps4Pkg.PKGState.ToString())
                    {
                        case PKGState.OFFICIAL: PKG.official++; break;
                        case PKGState.FAKE: PKG.fake++; break;
                        case PKGState.ADDON_UNLOCKER: PKG.unlockerAddon++; break;
                    }

                    PKG.pkgCount++;
                    // Batch progress updates every 10 files to reduce UI overhead
                    if (PKG.pkgCount % 10 == 0 || PKG.pkgCount == PKG.VerifiedPs4PkgList.Count)
                    {
                        darkStatusStrip1.Invoke((MethodInvoker)delegate
                        {
                            toolStripStatusLabel2.Text = "Loading PS4 PKG.. " + "(" + PKG.pkgCount.ToString() + "/" + PKG.VerifiedPs4PkgList.Count.ToString() + ") ";
                            toolStripProgressBar1.Increment(10);
                        });
                    }
                }
                dttemp.EndLoadData();
                ApplyShadps4Status(dttemp);

                // Set DataSource ONCE after loop - NOT inside every iteration
                darkStatusStrip1.Invoke((MethodInvoker)delegate
                {
                    PKGGridView.SuspendLayout();
                    PKGGridView.DataSource = dttemp;
                    for (int i = 10; i <= 12; i++) // PSVR, PS4 Pro Enhanced, PS5 BC (col 9 is Size - keep it)
                        PKGGridView.Columns[i].Visible = appSettings_.psvr_neo_ps5bc_check;
                    foreach (DataGridViewColumn column in PKGGridView.Columns)
                        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    PKGGridView.ResumeLayout();
                });
                #endregion loadPkgProcess1
            };
            bw.RunWorkerCompleted += delegate
            {
                PostPkgLoad();
            };
            bw.RunWorkerAsync();
        }

        /// <summary>
        /// Wraps a DataRow with a FilePath property so the GLV can extract
        /// the PKG path on selection change.
        /// </summary>
        private class GlvItem
        {
            public DataRow Row { get; }
            public string Path => FilePath;
            public string FilePath => System.IO.Path.Combine(
                Row["Directory"]?.ToString() ?? "",
                Row["Filename"]?.ToString() ?? "");
            public GlvItem(DataRow row) { Row = row; }
        }

        private void PopulateGroupedView()
        {
            try
            {
                if (groupedListView == null) return;

                var dt = PKGGridView.DataSource as DataTable;
                if (dt == null || dt.Rows.Count == 0) return;

                // Respect the DGV's active RowFilter so the GLV mirrors what the table shows -
                // otherwise GLV selection can target rows hidden by the filter and never focus them.
                var visibleRows = dt.DefaultView.Cast<DataRowView>().Select(v => v.Row).ToList();

                // Build column list based on DGV visibility settings
                var glvColumns = GetGlvColumns();
                groupedListView.DefineColumns(glvColumns.ToArray());

                var items = visibleRows
                    .Select(r => new GlvItem(r))
                    .OrderBy(i => i.Row["Filename"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string groupCol = GroupByColumn;
                groupedListView.SetGroups(
                    items,
                    item => item.Row[groupCol]?.ToString() ?? "Other",
                    item => BuildGlvRowData(item)
                );

                darkLabelGroupCount.Text = items.Count > 0 ? $"({items.Count})" : "";
                // Groups start expanded; collapse on first tab switch to Group
            }
            catch (Exception ex)
            {
                // A GLV refresh failure must never break the enclosing operation's completion
                // (which would leave the form and the GLV context menu disabled).
                Logger.LogWarning("Failed to refresh grouped view: " + ex.Message);
            }
        }

        private static List<(string, int)> GetGlvColumns()
        {
            var cols = new List<(string, int)> { ("Filename", 0), ("Title", 0) };
            if (appSettings_.pkgtitleIdColumn) cols.Add(("Title ID", 0));
            if (appSettings_.pkgcontentIdColumn) cols.Add(("Content ID", 0));
            if (appSettings_.pkgregionColumn) cols.Add(("Region", 0));
            if (appSettings_.pkgminimumFirmwareColumn) cols.Add(("System Version", 0));
            if (appSettings_.pkgversionColumn) cols.Add(("Version [App Version]", 0));
            if (appSettings_.pkgTypeColumn) cols.Add(("PKG Type", 0));
            if (appSettings_.pkgcategoryColumn) cols.Add(("Category", 0));
            if (appSettings_.pkgsizeColumn) cols.Add(("Size", 0));
            if (appSettings_.psvr_neo_ps5bc_check) { cols.Add(("PSVR", 0)); cols.Add(("PS4 Pro Enhanced", 0)); cols.Add(("PS5 BC", 0)); }
            if (appSettings_.pkgDirectoryColumn) cols.Add(("Directory", 0));
            if (appSettings_.pkgBackportColumn) cols.Add(("Backported", 0));
            if (appSettings_.AutoFetchUpdate) cols.Add(("Latest Update", 0));
            if (appSettings_.Shadps4Check) cols.Add(("ShadPS4 (Windows)", 0));
            return cols;
        }

        private static string[] BuildGlvRowData(GlvItem item)
        {
            string Cell(string name) => item.Row[name]?.ToString() ?? "";
            var data = new List<string> { Cell("Filename"), Cell("Title") };
            if (appSettings_.pkgtitleIdColumn) data.Add(Cell("Title ID"));
            if (appSettings_.pkgcontentIdColumn) data.Add(Cell("Content ID"));
            if (appSettings_.pkgregionColumn) data.Add(GetRegionString(item.Row));
            if (appSettings_.pkgminimumFirmwareColumn) data.Add(Cell("System Version"));
            if (appSettings_.pkgversionColumn) data.Add(Cell("Version [App Version]"));
            if (appSettings_.pkgTypeColumn) data.Add(Cell("PKG Type"));
            if (appSettings_.pkgcategoryColumn) data.Add(Cell("Category"));
            if (appSettings_.pkgsizeColumn) data.Add(Cell("Size"));
            if (appSettings_.psvr_neo_ps5bc_check) { data.Add(Cell("PSVR")); data.Add(Cell("PS4 Pro Enhanced")); data.Add(Cell("PS5 BC")); }
            if (appSettings_.pkgDirectoryColumn) data.Add(Cell("Directory"));
            if (appSettings_.pkgBackportColumn) data.Add(Cell("Backported"));
            if (appSettings_.AutoFetchUpdate) data.Add(Cell("Latest Update"));
            if (appSettings_.Shadps4Check) data.Add(Cell("ShadPS4 (Windows)"));
            return data.ToArray();
        }

        private static string GetRegionString(DataRow row)
        {
            var icon = row["Region"] as byte[];
            if (icon == null || icon.Length == 0) return "";
            // Use cached lookup - region icons are static resources
            if (_regionLookup == null)
            {
                var cvt = new System.Drawing.ImageConverter();
                _regionLookup = new Dictionary<byte[], string>(ByteArrayComparer.Instance)
                {
                    [(byte[])cvt.ConvertTo(Properties.Resources.eu, typeof(byte[]))] = "EU",
                    [(byte[])cvt.ConvertTo(Properties.Resources.us, typeof(byte[]))] = "US",
                    [(byte[])cvt.ConvertTo(Properties.Resources.jp, typeof(byte[]))] = "JAPAN",
                    [(byte[])cvt.ConvertTo(Properties.Resources.hk, typeof(byte[]))] = "HONG_KONG",
                    [(byte[])cvt.ConvertTo(Properties.Resources.asia, typeof(byte[]))] = "ASIA",
                    [(byte[])cvt.ConvertTo(Properties.Resources.kr, typeof(byte[]))] = "KOREA",
                };
            }
            return _regionLookup.TryGetValue(icon, out var name) ? name : "";
        }

        private static Dictionary<byte[], string> _regionLookup;

        private class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public static readonly ByteArrayComparer Instance = new();
            public bool Equals(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
            public int GetHashCode(byte[] a) { if (a == null) return 0; int h = 0; for (int i = 0; i < Math.Min(a.Length, 16); i++) h = (h * 31) ^ a[i]; return h; }
        }

        private void GroupedListView_SelectedItemChanged(object sender, EventArgs e)
        {
            string filePath = groupedListView?.SelectedFilePath;
            if (string.IsNullOrEmpty(filePath))
            {
                Logger.LogInformation("GLV selection: SelectedFilePath is null/empty");
                return;
            }
            if (!File.Exists(filePath))
            {
                Logger.LogInformation($"GLV selection: file not found: {filePath}");
                return;
            }

            // Select the same PKG in the DataGridView to trigger detail loading
            var dt = PKGGridView.DataSource as DataTable;
            if (dt == null)
            {
                Logger.LogInformation("GLV selection: DataSource is null");
                return;
            }

            // Find the row index matching this file path
            for (int i = 0; i < PKGGridView.Rows.Count; i++)
            {
                var row = PKGGridView.Rows[i];
                if (row.Cells[0].Value == null || row.Cells[13].Value == null) continue;
                string dir = row.Cells[13].Value.ToString();
                string fn = row.Cells[0].Value.ToString();
                string rowPath = System.IO.Path.Combine(dir, fn);

                if (string.Equals(rowPath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    PKGGridView.ClearSelection();
                    PKGGridView.Rows[i].Selected = true;
                    PKGGridView.CurrentCell = PKGGridView.Rows[i].Cells[0];
                    return;
                }
            }
            Logger.LogInformation($"GLV selection: no grid row matches '{filePath}'");
        }

        // ── GLV context menu helpers ─────────────────────────

        /// <summary>
        /// Updates the GLV context menu's disabled header label (PKG title or group count).
        /// Reads _glvGroupHeaderIndex WITHOUT consuming it - the index must stay valid for
        /// the menu item click handlers (GetGLVTargetPaths) to act on the right group.
        /// </summary>
        private void UpdateGlvMenuLabel()
        {
            try
            {
                // Always show the title of the HIGHLIGHTED (selected) PKG - the label must
                // not follow which row was right-clicked. Fall back to the right-clicked row
                // only when nothing is selected.
                string path = null;
                var selected = groupedListView?.GetSelectedFilePaths();
                if (selected != null && selected.Count == 1)
                    path = selected[0];
                if (string.IsNullOrEmpty(path))
                    path = groupedListView?.SelectedFilePath;

                if (string.IsNullOrEmpty(path))
                {
                    glvTitleStripMenuItem.Text = "PKG";
                    return;
                }

                string title = FindPkgTitleByPath(path);
                glvTitleStripMenuItem.Text = string.IsNullOrEmpty(title) ? Path.GetFileName(path) : title;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to update GLV menu label: " + ex.Message);
            }
        }

        /// <summary>
        /// Looks up the PKG title from the grid by full file path (for the GLV menu header).
        /// </summary>
        private string FindPkgTitleByPath(string filePath)
        {
            try
            {
                if (PKGGridView.DataSource is DataTable dt)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string fn = row["Filename"]?.ToString() ?? "";
                        string dir = row["Directory"]?.ToString() ?? "";
                        if (string.Equals(System.IO.Path.Combine(dir, fn), filePath, StringComparison.OrdinalIgnoreCase))
                            return row["Title"]?.ToString() ?? "";
                    }
                }
            }
            catch { }
            return "";
        }

        private List<string> GetGLVTargetPaths()
        {
            // Group header right-click → all files in that group
            if (_glvGroupHeaderIndex >= 0)
            {
                int idx = _glvGroupHeaderIndex;
                _glvGroupHeaderIndex = -1;
                var paths = groupedListView.GetGroupFilePaths(idx);
                return paths;
            }

            // Multi-select → selected rows
            var selected = groupedListView.GetSelectedFilePaths();
            if (selected.Count > 0)
                return selected;

            // Single right-click on an item row → SelectedFilePath is already set
            // by CellMouseClick (but row wasn't "selected" in the DataGridView sense)
            var single = groupedListView.SelectedFilePath;
            if (!string.IsNullOrEmpty(single))
                return new List<string> { single };

            return new List<string>();
        }

        private void GlvCopyContentId()
        {
            var paths = GetGLVTargetPaths();
            if (paths.Count == 0) { ShowError("No PKG selected.", false); return; }
            var ids = paths.Select(p => { var r = PS4_Tools.PKG.SceneRelated.Read_PKG(p); return r.Param.ContentID; });
            Clipboard.SetText(string.Join("\n", ids));
            ShowInformation($"{paths.Count} Content ID(s) copied.", true);
        }

        private void GlvViewInExplorer()
        {
            var paths = GetGLVTargetPaths();
            if (paths.Count == 0) { ShowError("No PKG selected.", false); return; }
            foreach (var p in paths) Process.Start("explorer.exe", "/select," + p);
        }

        private void GlvViewChangeInfo()
        {
            var paths = GetGLVTargetPaths();
            if (paths.Count == 0) { ShowError("No PKG selected.", false); return; }
            if (!CheckOrbisPubCmdExists()) return;
            PKG.SelectedPKGFilename = paths[0];
            ViewUpdateChangelog();
            string changeInfoFile = AppDataDirectory + "changeinfo.xml";
            if (File.Exists(changeInfoFile))
            {
                try
                {
                    string data = File.ReadAllText(changeInfoFile);
                    File.Delete(changeInfoFile);
                    using (var viewer = new PKGChangeInfoViewer(data)) { viewer.ShowDialog(); }
                }
                catch (Exception ex) { ShowError("Error viewing change info: " + ex.Message, true); }
            }
        }

        private void GlvDeletePkg()
        {
            var paths = GetGLVTargetPaths();
            if (paths.Count == 0) { ShowError("No PKG selected.", false); return; }
            var confirm = DialogResultYesNo(
                $"{paths.Count} PKG file{(paths.Count == 1 ? "" : "s")} will be permanently deleted.\n\nContinue?");
            if (confirm != DialogResult.Yes) return;
            Logger.LogInformation($"Delete: {paths.Count} PKG(s)");
            PKG.isDeletingPkg = true;
            toolStripProgressBar1.Visible = true;
            toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
            toolStripStatusLabel2.Text = "Deleting...";
            foreach (var p in paths)
            {
                try { File.Delete(p); }
                catch (Exception ex) { Logger.LogError($"Failed to delete {p}: {ex.Message}"); }
            }
            Logger.LogInformation($"Delete completed: {paths.Count} PKG(s)");
            PKG.isDeletingPkg = false;
            toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
            RefreshPkgList();
        }

        private void GlvRenameByPriority()
        {
            // Always target the whole group - right-click an item or a header,
            // the entire group gets renamed by install priority.
            if (_glvGroupHeaderIndex < 0 && !string.IsNullOrEmpty(groupedListView?.SelectedFilePath))
                _glvGroupHeaderIndex = groupedListView.FindGroupForPath(groupedListView.SelectedFilePath);
            var paths = GetGLVTargetPaths();
            if (paths.Count == 0) { ShowError("No PKG selected.", false); return; }
            var confirm = DialogResultYesNo(
                $"Re-sort {paths.Count} PKG file{(paths.Count == 1 ? "" : "s")} by install priority?\n\n" +
                "Files will be grouped by Title ID and renamed with sequence prefixes:\n" +
                "  00 - Base -> 01 - Update\n\nAdd-on and App PKGs are skipped.\n\nContinue?");
            if (confirm != DialogResult.Yes) return;
            var bg = new BackgroundWorker { WorkerReportsProgress = true };
            bg.DoWork += (_, _) => RenamePKGByPriority(paths, bg);
            bg.ProgressChanged += (_, e) =>
            {
                toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                toolStripProgressBar1.Maximum = 100;
                toolStripProgressBar1.Value = e.ProgressPercentage;
                toolStripStatusLabel2.Text = e.UserState?.ToString() ?? "...";
            };
            bg.RunWorkerCompleted += (_, _) =>
            {
                try
                {
                    if (PKG.CountFailRename > 0)
                        ShowWarning(PKG.CountFailRename + " PKG failed to rename by priority. See program log to view the errors.", false);
                    else
                        ShowInformation("PKG rename by priority done.", true);
                    SaveManifestAfterScan();
                    // GLV cells updated in-place by UpdatePKGFilename during rename.
                }
                catch (Exception ex)
                {
                    Logger.LogError("GLV priority rename completion failed: " + ex.Message);
                }
                finally
                {
                    toolStripStatusLabel2.Text = "...";
                    toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                    toolStripProgressBar1.Value = 0;
                    this.Enabled = true;
                    SetOperationMenusEnabled(true);
                }
            };
            this.Enabled = false;
            toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
            toolStripProgressBar1.Maximum = 100;
            toolStripProgressBar1.Value = 0;
            toolStripStatusLabel2.Text = "Renaming by install priority...";
            bg.RunWorkerAsync();
        }

        private void GlvExtractImages()
        {
            var paths = GetGLVTargetPaths();
            if (paths.Count == 0) { ShowError("No PKG selected.", false); return; }
            using var fbd = new FolderBrowserDialog { Description = "Select output folder for artwork" };
            if (fbd.ShowDialog() != DialogResult.OK) return;
            Logger.LogInformation($"Save artwork: {paths.Count} PKG(s) to {fbd.SelectedPath}");
            toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
            var bg = new BackgroundWorker();
            bg.DoWork += (_, _) =>
            {
                ImageIconExtractor(ImageIconExtractionType.ICON, paths, fbd.SelectedPath, false);
            };
            bg.RunWorkerCompleted += (_, _) =>
            {
                Logger.LogInformation("Artwork saved successfully.");
                ShowInformation($"Artwork saved to {fbd.SelectedPath}", true);
                toolStripStatusLabel2.Text = "...";
                toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                toolStripProgressBar1.Value = 0;
            };
            toolStripStatusLabel2.Text = "Saving artwork...";
            bg.RunWorkerAsync();
        }

        private void FetchAllUpdateVersions()
        {
            if (!Tool.CheckForInternetConnection())
            {
                Logger.LogInformation("No internet connection. Skipping update version check.");
                return;
            }

            var bg = new BackgroundWorker();
            bg.DoWork += (s, e) =>
            {
                int checked_ = 0;
                int fetched = 0;
                foreach (var pkg in PKG.VerifiedPs4PkgList)
                {
                    try
                    {
                        var pkgData = PS4_Tools.PKG.SceneRelated.Read_PKG(pkg);
                        string titleId = pkgData.Param.TITLEID;
                        string pkgType = pkgData.PKG_Type.ToString();
                        if (!string.IsNullOrEmpty(titleId))
                        {
                            string latestValue;
                            if (pkgType == PKGCategory.ADDON)
                            {
                                latestValue = "NA";
                            }
                            else if (pkgType == PKGCategory.GAME || pkgType == PKGCategory.PATCH)
                            {
                                var updateInfo = PS4_Tools.PKG.Official.CheckForUpdate(titleId);
                                if (updateInfo != null && updateInfo.Tag?.Package?.Manifest_url != null)
                                {
                                    fetched++;
                                    latestValue = updateInfo.Tag?.Package?.Version ?? "No Update";
                                }
                                else
                                {
                                    latestValue = "No Update";
                                }
                            }
                            else
                            {
                                latestValue = "NA";
                            }
                            this.Invoke((Action)(() =>
                            {
                                var dt = PKGGridView.DataSource as DataTable;
                                if (dt != null)
                                {
                                    foreach (DataRow row in dt.Rows)
                                    {
                                        if (row["Title ID"]?.ToString() == titleId)
                                        {
                                            row["Latest Update"] = latestValue;
                                            break;
                                        }
                                    }
                                }
                            }));
                        }
                        checked_++;
                    }
                    catch (Exception ex) { Logger.LogWarning("Update check failed for PKG: " + pkg + " - " + ex.Message); }
                }
                this.Invoke((MethodInvoker)PopulateGroupedView); // refresh grouped view with updated Latest Update values
            };
            bg.RunWorkerAsync();
        }

        /// <summary>
        /// Fills the "ShadPS4 (Windows)" column from the local compatibility
        /// cache (by Title ID, column-name based - index-safe). No-op when the
        /// check is disabled or the cache is missing.
        /// </summary>
        private static void ApplyShadps4Status(DataTable dt)
        {
            try
            {
                if (!appSettings_.Shadps4Check) return;
                if (dt == null || !dt.Columns.Contains("ShadPS4 (Windows)") || !dt.Columns.Contains("Title ID")) return;
                foreach (DataRow row in dt.Rows)
                {
                    string tid = row["Title ID"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(tid)) continue;
                    string status = Shadps4Compat.Lookup(tid);
                    if (!string.IsNullOrEmpty(status))
                        row["ShadPS4 (Windows)"] = status;
                }
            }
            catch (Exception ex) { Logger.LogWarning("Failed to apply shadPS4 status: " + ex.Message); }
        }

        private void PostPkgLoad()
        {
            if (PKG.VerifiedPs4PkgList.Count == 0)
            {
                PKGGridView.DataSource = null;
                darkDataGridView2.DataSource = null;
                SetOperationMenusEnabled(false);

                if (Helper.LoadFromManifest)
                {
                    var result = DialogResultYesNo(
                        "Manifest loaded but no PKG files are currently accessible.\n\n" +
                        "The PKG files may be on a network drive or external storage\n" +
                        "that is not currently connected.\n\n" +
                        "Would you like to scan directories instead?");
                    if (result == DialogResult.Yes)
                    {
                        Helper.LoadFromManifest = false;
                        Helper.LaunchEmpty = false;
                        RefreshPkgList();
                        return;
                    }
                }
                else
                {
                    var missingDirs = GetMissingDirectories();
                    if (missingDirs.Count > 0)
                    {
                        var result = DialogResultYesNo(
                            "No PKG files found. Some configured directories do not exist:\n" +
                            string.Join("\n", missingDirs) +
                            "\n\nWould you like to open Program Settings to reconfigure?");
                        if (result == DialogResult.Yes)
                            OpenPKGDirectorySettings();
                    }
                    else
                    {
                        ShowInformation("No PKG files found in the configured directories.", true);
                    }
                }
            }
            else
            {
                SetOperationMenusEnabled(true);
                FinalizePkgLoadingProcess();
                UpdateDataGridViewColumnVisibility();
                SetBackgroundMusicVolume();
                SetDataGridViewCellStyle();
                PopulateGroupedView();
                SaveManifestAfterScan();
                if (appSettings_.AutoFetchUpdate) FetchAllUpdateVersions();
            }
            //PKGListGridView.SelectionChanged += PKGListGridView_SelectionChanged;
            toolStripStatusLabel2.Text = "... ";
            toolStripProgressBar1.Value = 0;
            PKGGridView.Enabled = true;
            darkDataGridView2.Enabled = true;
            this.Enabled = true;
        }

        /// <summary>
        /// Disables or enables operation menus/buttons based on whether PKGs are loaded.
        /// </summary>
        private void SetOperationMenusEnabled(bool enabled)
        {
            this.Invoke((MethodInvoker)delegate
            {
                // File->Manage
                if (managePS4PKGToolStripMenuItem != null) managePS4PKGToolStripMenuItem.Enabled = enabled;
                // Status bar
                ToolStripSplitButtonTotalPKG.Enabled = enabled;
                // TabPage7 buttons
                if (btnExtractFullPKG != null) btnExtractFullPKG.Enabled = enabled;
                if (btnViewPKGData != null) btnViewPKGData.Enabled = enabled;
                if (btnSearchFileInTreeView != null) btnSearchFileInTreeView.Enabled = enabled;
                // All context menus
                if (contextMenuPKGGridView != null) contextMenuPKGGridView.Enabled = enabled;
                if (contextMenuGLV != null) contextMenuGLV.Enabled = enabled;
                if (contextMenuTrophy != null) contextMenuTrophy.Enabled = enabled;
                if (contextMenuEntry != null) contextMenuEntry.Enabled = enabled;
                if (contextMenuOfficialUpdate != null) contextMenuOfficialUpdate.Enabled = enabled;
                if (contextMenuBackgroundImage != null) contextMenuBackgroundImage.Enabled = enabled;
                if (contextMenuExtractNode != null) contextMenuExtractNode.Enabled = enabled;
                if (contextMenuExtractListView != null) contextMenuExtractListView.Enabled = enabled;
                // GLV controls
                if (cbGroupBy != null) cbGroupBy.Enabled = enabled;
                // TreeView filter controls
                if (tbFilterTreeView != null) tbFilterTreeView.Enabled = enabled;
                // Table tab filter controls
                if (tbSearchGame != null) tbSearchGame.Enabled = enabled;
                // Group tab expand/collapse button
                if (btnGroupExpand != null) btnGroupExpand.Enabled = enabled;
            });
        }

        /// <summary>
        /// Returns configured PKG directories that do not exist on disk.
        /// </summary>
        private List<string> GetMissingDirectories()
        {
            var missing = new List<string>();
            if (appSettings_?.PkgDirectories != null)
            {
                foreach (string dir in appSettings_.PkgDirectories)
                {
                    if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                        missing.Add(dir);
                }
            }
            return missing;
        }

        private void InitializeEmptyGrid()
        {
            PKGGridView.DataSource = null;
            PKGGridView.Rows.Clear();
            darkDataGridView2.DataSource = null;
            darkDataGridView2.Rows.Clear();
            SetOperationMenusEnabled(false);
        }

        private void SaveManifestAfterScan()
        {
            var dt = PKGGridView.DataSource as DataTable;
            if (dt != null)
            {
                ManifestHelper.SaveManifest(dt, PKG.VerifiedPs4PkgList);
            }
        }

        private void SetDataGridViewCellStyle()
        {
            this.Invoke((MethodInvoker)delegate
            {
                try
                {
                    PKGGridView.Sort(PKGGridView.Columns[0], ListSortDirection.Ascending);

                    // Set header cell alignment
                    for (int columnIndex = 0; columnIndex < PKGGridView.Columns.Count; columnIndex++)
                    {
                        PKGGridView.Columns[columnIndex].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    }

                    // Set cell alignment
                    for (int columnIndex = 1; columnIndex <= 14; columnIndex++)
                    {
                        PKGGridView.Columns[columnIndex].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    }
                }
                catch
                {
                }
            });
        }

        private static void SetBackgroundMusicVolume()
        {
            if (!appSettings_.PlayBgm)
            {
                int newVolume = 0; // Set 0 to unmute
                uint newVolumeAllChannels = (((uint)newVolume & 0x0000ffff) | ((uint)newVolume << 16));
                waveOutSetVolume(IntPtr.Zero, newVolumeAllChannels);
            }
            else
            {
                int newVolume = 65535; // Set 65535 to unmute
                uint newVolumeAllChannels = (((uint)newVolume & 0x0000ffff) | ((uint)newVolume << 16));
                waveOutSetVolume(IntPtr.Zero, newVolumeAllChannels);
            }
        }

        private void UpdateDataGridViewColumnVisibility()
        {
            try
            {
                PKGGridView.Columns[1].Visible = true; // Title always visible
                PKGGridView.Columns[2].Visible = appSettings_.pkgtitleIdColumn;
                PKGGridView.Columns[3].Visible = appSettings_.pkgcontentIdColumn;
                PKGGridView.Columns[4].Visible = appSettings_.pkgregionColumn;
                PKGGridView.Columns[5].Visible = appSettings_.pkgminimumFirmwareColumn;
                PKGGridView.Columns[6].Visible = appSettings_.pkgversionColumn;
                PKGGridView.Columns[7].Visible = appSettings_.pkgTypeColumn;
                PKGGridView.Columns[8].Visible = appSettings_.pkgcategoryColumn;
                PKGGridView.Columns[9].Visible = appSettings_.pkgsizeColumn;
                PKGGridView.Columns[10].Visible = appSettings_.psvr_neo_ps5bc_check;
                PKGGridView.Columns[11].Visible = appSettings_.psvr_neo_ps5bc_check;
                PKGGridView.Columns[12].Visible = appSettings_.psvr_neo_ps5bc_check;
                PKGGridView.Columns[13].Visible = appSettings_.pkgDirectoryColumn;
                PKGGridView.Columns[14].Visible = appSettings_.pkgBackportColumn;
                PKGGridView.Columns[15].Visible = appSettings_.AutoFetchUpdate;
                if (PKGGridView.Columns.Count > 16)
                    PKGGridView.Columns[16].Visible = appSettings_.Shadps4Check;
            }
            catch (Exception ex) { Logger.LogWarning("Error updating column visibility: " + ex.Message); }
        }

        private void RebuildFilterDropDown()
        {
            ToolStripSplitButtonTotalPKG.DropDownItems.Clear();
            if (PKG.game != 0)
                ToolStripSplitButtonTotalPKG.DropDownItems.Add($"Show only Game PKG ({PKG.game})", null, new System.EventHandler(GridViewFilterPKG_Click));
            if (PKG.patch != 0)
                ToolStripSplitButtonTotalPKG.DropDownItems.Add($"Show only Patch PKG ({PKG.patch})", null, new System.EventHandler(GridViewFilterPKG_Click));
            if (PKG.addon != 0)
                ToolStripSplitButtonTotalPKG.DropDownItems.Add($"Show only Addon PKG ({PKG.addon})", null, new System.EventHandler(GridViewFilterPKG_Click));
            if (PKG.app != 0)
                ToolStripSplitButtonTotalPKG.DropDownItems.Add($"Show only App PKG ({PKG.app})", null, new System.EventHandler(GridViewFilterPKG_Click));
            if (PKG.unknown != 0)
                ToolStripSplitButtonTotalPKG.DropDownItems.Add($"Show only Unknown PKG ({PKG.unknown})", null, new System.EventHandler(GridViewFilterPKG_Click));
            ToolStripSplitButtonTotalPKG.DropDownItems.Add("Show all PKG", null, new System.EventHandler(GridViewFilterPKG_Click));
        }

        private void FinalizePkgLoadingProcess()
        {
            if (FinalizePkgProcess)
            {
                FinalizePkgProcess = false;
                BackgroundWorker bgw = new BackgroundWorker
                {
                    WorkerSupportsCancellation = true
                };
                bgw.DoWork += (s, a) =>
                {
                    Logger.LogInformation("Extracting PKG background music..");
                    BGM.ExtractBgm();
                };
                bgw.RunWorkerCompleted += (s, a) =>
                {
                    BGM.extractAt9Done = true;
                };
                bgw.RunWorkerAsync();
                toolStripSplitButton1.DropDownItems.Clear();
                GetDrivesFreeSpace();
                RebuildFilterDropDown();
                labelDisplayTotalPKG.Text = $"Displaying {PKGGridView.Rows.Count} PS4 PKG";
                Logger.LogInformation($"Loading PKG done. {PKGGridView.Rows.Count} PKG found.");
            }
        }

        private void GetDrivesFreeSpace()
        {
            Logger.LogInformation("Checking hard disk free space..");
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    long freeSpace = drive.TotalFreeSpace;
                    long totalSpace = drive.TotalSize;
                    double freeSpaceGB = ByteSize.FromBytes(freeSpace).GigaBytes;
                    double totalSpaceGB = ByteSize.FromBytes(totalSpace).GigaBytes;

                    string formattedFreeSpace = $"{freeSpaceGB:F2} GB";
                    string formattedTotalSpace = $"{totalSpaceGB:F2} GB";

                    toolStripSplitButton1.DropDownItems.Add($"[{drive}] Free Space: {formattedFreeSpace}/{formattedTotalSpace}");
                    Logger.LogInformation($"[{drive}] Free Space: {formattedFreeSpace}/{formattedTotalSpace}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("An error occurred while getting hard disk free space: " + ex.Message);
            }
        }
        #endregion PKGScanning

        #region PKGGridViewFiltering

        private void GridViewFilterPKG_Click(object sender, EventArgs e)
        {
            string text = sender.ToString();
            if (text.Contains(PKGCategory.GAME))
            {
                (PKGGridView.DataSource as DataTable).DefaultView.RowFilter = string.Format("[Category] LIKE '%{0}%'", PKGCategory.GAME);
            }
            else if (text.Contains(PKGCategory.PATCH))
            {
                (PKGGridView.DataSource as DataTable).DefaultView.RowFilter = string.Format("[Category] LIKE '%{0}%'", PKGCategory.PATCH);
            }
            else if (text.Contains(PKGCategory.ADDON))
            {
                (PKGGridView.DataSource as DataTable).DefaultView.RowFilter = string.Format("[Category] LIKE '%{0}%'", PKGCategory.ADDON);
            }
            else if (text.Contains(PKGCategory.APP))
            {
                (PKGGridView.DataSource as DataTable).DefaultView.RowFilter = string.Format("[Category] LIKE '%{0}%'", PKGCategory.APP);
            }
            else if (text.Contains(PKGCategory.UNKNOWN))
            {
                (PKGGridView.DataSource as DataTable).DefaultView.RowFilter = string.Format("[Category] LIKE '%{0}%'", "Unknown");
            }
            else if (text.Contains("all"))
            {
                (PKGGridView.DataSource as DataTable).DefaultView.RowFilter = string.Format("[Category] LIKE '%{0}%'", "");
            }
            PopulateGroupedView(); // keep the GLV in sync with the active DGV filter
        }

        #endregion PKGGridViewFiltering

        #region PKGBasicOperation_Copy_Rename_Delete_ViewExplorer
        #region PKG_Copy_delete_View
        private void ViewPKGInExplorer()
        {
            var pkgList = GetSelectedPKGDirectoryList(PKGSelectionType.SELECTED);
            pkgList.ForEach(pkg => Logger.LogInformation($"Opening {pkg} PKG file in Explorer.."));
            pkgList.ForEach(pkg => Process.Start("explorer.exe", "/select," + pkg));
        }

        private void OpenTempDirectory()
        {
            Process.Start("explorer.exe", Helper.AppDataDirectory);
        }

        private void DeletePkg()
        {
            var pkgList = GetSelectedPKGDirectoryList(PKGSelectionType.SELECTED);
            DialogResult dialog = DialogResultYesNo("PKG file will be permanently deleted. This operation cannot be undone. Are you sure you want to continue?");

            if (dialog == DialogResult.Yes)
            {
                Logger.LogInformation($"Delete: {pkgList.Count} PKG(s)");
                PKG.isDeletingPkg = true;
                toolStripProgressBar1.Visible = true;
                toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
                toolStripStatusLabel2.Text = "Deleting...";
                try
                {
                    foreach (var pkg in pkgList)
                    {
                        var pkgFileName = Path.GetFileName(pkg);
                        var directoryName = Path.GetDirectoryName(pkg);
                        var fullPkgPath = Path.Combine(directoryName, pkgFileName);
                        var matchingRow = PKGGridView.Rows
                            .Cast<DataGridViewRow>()
                            .FirstOrDefault(row => row.Cells[0].Value.ToString() == pkgFileName && row.Cells[13].Value.ToString() == directoryName);

                        // remove pkg from gridview
                        if (matchingRow != null)
                        {
                            PKGGridView.Rows.Remove(matchingRow);
                        }

                        // remove pkg from VerifiedPs4PkgList
                        PKG.VerifiedPs4PkgList.Remove(fullPkgPath);

                        File.Delete(pkg);
                        Logger.LogInformation($"\"{pkg}\" deleted.");
                        //PKG.totalPkg--;
                    }

                    PKG.SelectedPKGFilename = ""; // Reset
                    labelDisplayTotalPKG.Text = "Displaying " + PKG.VerifiedPs4PkgList.Count.ToString() + " PS4 PKG";
                    toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                    ShowInformation("PKG file deleted.", true);
                    PopulateGroupedView(); // remove deleted entries from the grouped view
                    PKG.isDeletingPkg = false;
                }
                catch (Exception a)
                {
                    ShowError("An error occurred: " + a.Message, true);
                }
            }
        }

        private void DeletePKG_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            if (clickedMenuItem == deletePKGtoolStripMenuItem1 || clickedMenuItem == deletePkgtoolStripMenuItem2)
            {
                DeletePkg();
            }
        }

        #endregion PKG_Copy_delete_View

        #region renamePKG

        private void CheckForDuplicatePKG_Click(object sender, EventArgs args)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            Logger.LogInformation("Checking for duplicate PKG..");
            if (clickedMenuItem == checkForDuplicatePKGToolStripMenuItem1 ||  clickedMenuItem == checkForDuplicatePKGToolStripMenuItem2)
            {
                var result = FindDuplicatePKG();
                if (result.Count == 0)
                {
                    ShowInformation("No duplicate PKG detected.", true);
                }
                else
                {
                    ShowWarning($"Found {result.Count} duplicate PKG file(s):\n\n{string.Join("\n", result)}", true);
                }
            }
        }

        private List<string> FindDuplicatePKG()
        {
            // Key: filename + size - same name AND same size = duplicate
            var map = new Dictionary<string, List<string>>();
            var dupGroups = new List<List<string>>();

            foreach (DataGridViewRow row in PKGGridView.Rows)
            {
                string fn = row.Cells[0].Value?.ToString() ?? "";
                string sz = row.Cells[9].Value?.ToString() ?? ""; // Size column
                string dir = row.Cells[13].Value?.ToString() ?? "";
                string key = $"{fn}|{sz}"; // filename + size = reliable duplicate check
                string path = string.IsNullOrEmpty(dir) ? fn : Path.Combine(dir, fn);

                if (map.TryGetValue(key, out var list))
                {
                    list.Add(path);
                }
                else
                {
                    var newList = new List<string> { path };
                    map[key] = newList;
                    dupGroups.Add(newList);
                }
            }

            var result = dupGroups.Where(g => g.Count > 1).SelectMany(g => g).ToList();
            Logger.LogInformation($"Duplicate check: {result.Count} file(s) in {dupGroups.Count(g => g.Count > 1)} group(s).");
            return result;
        }

        private void UpdatePKGFilename(string newPkgName, string sourcePkg, string targetPkg)
        {
            string pkgFileName_ = Path.GetFileName(sourcePkg);
            string directoryName = Path.GetDirectoryName(sourcePkg);

            foreach (DataGridViewRow row in PKGGridView.Rows)
            {
                string cell0 = row.Cells[0].Value?.ToString();
                string cell12 = row.Cells[13].Value?.ToString();

                if (string.Equals(cell0, pkgFileName_, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(cell12, directoryName, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(sourcePkg, targetPkg);
                    string newFileName = newPkgName + ".pkg";
                    PKGGridView.Invoke((Action)(() =>
                    {
                        row.Cells[0].Value = newFileName;
                    }));
                    // Update the GLV cell in-place (no full rebuild, no selection loss).
                    // Use targetPkg - after the DGV cell update above, GlvItem.FilePath
                    // is recomputed from the DataRow and equals the new path, not sourcePkg.
                    groupedListView?.Invoke((Action)(() =>
                        groupedListView.UpdateCellForPath(targetPkg, 0, newFileName)));
                    return;
                }
            }
            // Row not found - the grid may have been refreshed since this rename started.
            // Try the File.Move anyway so the file on disk is still renamed.
            if (File.Exists(sourcePkg) && !File.Exists(targetPkg))
                File.Move(sourcePkg, targetPkg);
        }

        #region RenameAllPkg

        #endregion RenameAllPkg

        #region RenameSelectedPKG

        #endregion RenameSelectedPKG
        #endregion renamePKG
        #endregion PKGBasicOperation_Copy_Rename_Delete_ViewExplorer

        #region PKGSender
        private void DisableControls_PkgSender()
        {
            //status bar - file
            managePS4PKGToolStripMenuItem.Enabled = false;
            exitToolStripMenuItem1.Enabled = false;

            //contextmenu
            toolStripMenuItem111.Enabled = false;
            toolStripMenuItem3.Enabled = false;
            globalExportPKGListToExcelToolStripMenuItem2.Enabled = false;
            deletePkgtoolStripMenuItem2.Enabled = false;
            toolStripMenuItem133.Enabled = false;
            toolStripMenuItem127.Enabled = false;
            viewPkgExplorerStripMenuItem2.Enabled = false;
            RpiCheckPkgInstalledtoolStripMenuItem2.Enabled = false;
            toolStripMenuItem21.Enabled = false;
        }

        private void EnableControls_PkgSender()
        {
            //status bar - file
            managePS4PKGToolStripMenuItem.Enabled = true;
            exitToolStripMenuItem1.Enabled = true;

            //contextmenu
            toolStripMenuItem111.Enabled = true;
            toolStripMenuItem3.Enabled = true;
            globalExportPKGListToExcelToolStripMenuItem2.Enabled = true;
            deletePkgtoolStripMenuItem2.Enabled = true;
            toolStripMenuItem133.Enabled = true;
            toolStripMenuItem127.Enabled = true;
            viewPkgExplorerStripMenuItem2.Enabled = true;
            RpiCheckPkgInstalledtoolStripMenuItem2.Enabled = true;
            toolStripMenuItem21.Enabled = true;
        }

        private void InitializePKGSender()
        {
            if (RpiSendPkgtoolStripMenuItem2.Text == "Send PKG to PS4")
            {
                DisableTabPages(flatTabControl1, "tabPage1");
                DisableControls(darkMenuStrip1);
                DisableControls_PkgSender();

                PS4_Tools.PKG.SceneRelated.Unprotected_PKG read = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);
                Param_SFO.PARAM_SFO psfo = read.Param;

                Logger.LogInformation("Sending " + read.PS4_Title + " to PS4..");

                // Update 'Settings.PKG.SelectedPKGFilename'
                GetSelectedPKGPath();

                PKGSENDER.taskMonitorIsCancelling = false;

                // Check if pkg installed
                if (read.PKG_Type.ToString() == PKGCategory.GAME || read.PKG_Type.ToString() == PKGCategory.PATCH)
                {
                    // Check if pkg exists for game pkg
                    PKGSENDER.JSON.CHECKAPPEXISTS.baseAppExist = true;

                    dynamic appExistsJson = null;
                    appExistsJson = PKGSENDER.CheckIfPkgInstalled(psfo);
                    if (appExistsJson == null)
                    {
                        ShowError("An error occurred while trying to communicate with PS4. Launch/restart Remote Package Installer application on PS4 and don't minimize it.", true);
                        EnableControls_PkgSender();
                        EnableTabPages(flatTabControl1);
                        EnableControls(darkMenuStrip1);
                        return;
                    }

                    PKGSENDER.JSON.CHECKAPPEXISTS.status = appExistsJson.status.ToString();

                    if (PKGSENDER.JSON.CHECKAPPEXISTS.status == "success")
                    {
                        PKGSENDER.JSON.CHECKAPPEXISTS.exists = appExistsJson.exists.ToString();
                        if (PKGSENDER.JSON.CHECKAPPEXISTS.exists == "true")
                        {
                            if (read.PKG_Type.ToString() == PKGCategory.GAME)
                            {
                                ShowInformation("PKG already installed.", true);
                                EnableControls_PkgSender();
                                EnableTabPages(flatTabControl1);
                                EnableControls(darkMenuStrip1);
                                return;
                            }
                        }
                        else
                        {
                            if (read.PKG_Type.ToString() == PKGCategory.PATCH)
                            {
                                PKGSENDER.JSON.CHECKAPPEXISTS.baseAppExist = false;
                            }
                        }
                    }
                }

                if (read.PKG_Type.ToString() == PKGCategory.ADDON)
                {
                    PKGSENDER.JSON.CHECKAPPEXISTS.baseAppExist = true;
                }

                toolStripMenuItem18.Text = "Remote PKG Installer | Status : Running";
                RpiSendPkgtoolStripMenuItem2.Text = "Stop Current Operation";
                toolStripMenuItem16.Text = "Remote PKG Installer | Status : Running";
                RpiSendPkgtoolStripMenuItem1.Text = "Stop Current Operation";
                toolStripStatusLabel2.Text = "Sending " + read.PS4_Title + " to PS4..";
                SendPKG();
            }
            else
            {
                Logger.LogInformation("Cancelling operation..");
                // Cancel current operation
                if (PKGSENDER.isPreparing)
                {
                    ShowWarning("Cannot cancel operation while preparing.", true);
                    return;
                }

                dynamic stopTaskJson = null;
                stopTaskJson = PKGSENDER.StopTask();
                if (stopTaskJson == null)
                {
                    ShowError("An error occurred while trying to communicate with PS4. Launch/restart Remote Package Installer application on PS4 and don't minimize it.", true);
                    EnableControls_PkgSender();
                    EnableTabPages(flatTabControl1);
                    EnableControls(darkMenuStrip1);
                    return;
                }

                PS4_Tools.PKG.SceneRelated.Unprotected_PKG read = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);
                Param_SFO.PARAM_SFO psfo = read.Param;

                PKGSENDER.JSON.STOPTASK.status = stopTaskJson.status.ToString();
                if (PKGSENDER.JSON.STOPTASK.status == "success")
                {
                    // If stopping success, uninstall stopped game 
                    dynamic uninstallAppJson = null;
                    uninstallAppJson = PKGSENDER.UninstallGame(psfo);
                    if (uninstallAppJson == null)
                    {
                        ShowError("An error occurred while trying to communicate with PS4. Launch/restart Remote Package Installer application on PS4 and don't minimize it.", true);
                        EnableControls_PkgSender();
                        EnableTabPages(flatTabControl1);
                        EnableControls(darkMenuStrip1);
                        return;
                    }

                    PKGSENDER.JSON.UNINTSALLAPP.status = uninstallAppJson.status.ToString();

                    // Cancel running background workers
                    PKGSENDER.MonitorPkgSenderTaskBackgroundWorker.CancelAsync();
                    SendPKG();
                    EnableControls_PkgSender();
                    EnableTabPages(flatTabControl1);
                    EnableControls(darkMenuStrip1);
                    darkStatusStrip1.Invoke((MethodInvoker)delegate
                    {
                        toolStripStatusLabel2.Text = "...";
                        toolStripProgressBar1.Value = 0;
                    });

                    ShowInformation("Operation stopped.", true);
                }
                else
                {
                    ShowError("Failed to stop current operation.", true);
                }
            }
        }

        private async Task CheckIfAppInstalledOnPS4()
        {
            PS4_Tools.PKG.SceneRelated.Unprotected_PKG read = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);
            Param_SFO.PARAM_SFO psfo = read.Param;

            Logger.LogInformation("Checking if base PKG installed on PS4 (" + read.PS4_Title + ")..");

            DisableTabPages(flatTabControl1, "tabPage1");
            DisableControls(darkMenuStrip1);
            DisableControls_PkgSender();

            dynamic app_exists_json = null;

            try
            {
                app_exists_json = await PKGSENDER.CheckIfPkgInstalled(psfo);
                if (app_exists_json == null)
                {
                    ShowError("An error occurred while trying to communicate with PS4. Launch/restart Remote Package Installer application on PS4 and don't minimize it.", true);
                    EnableControls_PkgSender();
                    EnableTabPages(flatTabControl1);
                    EnableControls(darkMenuStrip1);
                    return;
                }

                PKGSENDER.JSON.CHECKAPPEXISTS.status = app_exists_json.status.ToString();

                if (PKGSENDER.JSON.CHECKAPPEXISTS.status == "success")
                {
                    EnableControls_PkgSender();
                    EnableTabPages(flatTabControl1);
                    EnableControls(darkMenuStrip1);
                    PKGSENDER.JSON.CHECKAPPEXISTS.exists = app_exists_json.exists.ToString();
                    if (PKGSENDER.JSON.CHECKAPPEXISTS.exists == "true")
                    {
                        ShowInformation("PKG already installed.", true);
                    }
                    else
                    {
                        ShowInformation("PKG is not installed.", true);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("An error occurred: " + ex.Message, true);
            }
        }

        private void SendPKG()
        {
            var bg = new BackgroundWorker();
            bg.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                renameBackFile = false;
                var currentPkgFile = PKG.SelectedPKGFilename;
                send_pkg_json = null;
                PKGSENDER.pkgSendDone = false;
                PKGSENDER.pkgSendStopped = false;
                PKGSENDER.JSON.SENDPKG.status = "";
                PKGSENDER.JSON.SENDPKG.task_id = "";
                PKGSENDER.JSON.SENDPKG.title = "";
                PKGSENDER.JSON.SENDPKG.title_id = "";

                // Kill server if running
                Tool.KillNodeJS();

                PS4_Tools.PKG.SceneRelated.Unprotected_PKG read = PS4_Tools.PKG.SceneRelated.Read_PKG(currentPkgFile);
                Param_SFO.PARAM_SFO psfo = read.Param;
                var tempFilename = read.Content_ID + "_" + read.PKG_Type.ToString() + "_Send_To_PS4.pkg";

                // Get directory
                var directory = Path.GetDirectoryName(currentPkgFile);

                // Get original filename
                var originalName = Path.GetFileName(currentPkgFile);

                // Rename to temp filename
                if (currentPkgFile != directory + @"\" + tempFilename)
                {
                    File.Move(currentPkgFile, directory + @"\" + tempFilename);
                }

                // Update filename in gridview
                foreach (DataGridViewCell cell in PKGGridView.SelectedCells)
                {
                    int selectedRowIndex = cell.RowIndex;
                    DataGridViewRow selectedRow = PKGGridView.Rows[selectedRowIndex];
                    selectedRow.Cells[0].Value = tempFilename;
                }

                TEMPFILENAMESENDPKG = directory + @"\" + tempFilename;
                PKG.SelectedPKGFilename = TEMPFILENAMESENDPKG;

                // Run server
                PKGSENDER.RunServer(directory);

                // Send pkg
                send_pkg_json = PKGSENDER.SendPKG(tempFilename);
                if (send_pkg_json == null)
                {
                    ShowError("An error occurred while trying to communicate with PS4. Launch/restart Remote Package Installer application on PS4 and don't minimize it.", true);
                    EnableControls_PkgSender();
                    EnableTabPages(flatTabControl1);
                    EnableControls(darkMenuStrip1);
                    toolStripMenuItem18.Text = "Remote PKG Installer | Status : Idle";
                    RpiSendPkgtoolStripMenuItem2.Text = "Send PKG to PS4";
                    toolStripMenuItem16.Text = "Remote PKG Installer | Status : Idle";
                    RpiSendPkgtoolStripMenuItem1.Text = "Send PKG to PS4";
                    toolStripStatusLabel2.Text = "...";
                    toolStripProgressBar1.Value = 0;
                    return;
                }

                PKGSENDER.JSON.SENDPKG.status = send_pkg_json.status.ToString();

                if (PKGSENDER.JSON.SENDPKG.status == "success")
                {
                    PKGSENDER.JSON.SENDPKG.status = send_pkg_json.status.ToString();
                    PKGSENDER.JSON.SENDPKG.task_id = send_pkg_json.task_id.ToString();
                    PKGSENDER.JSON.SENDPKG.title = send_pkg_json.title.ToString();
                    PKGSENDER.JSON.SENDPKG.title_id = psfo.TitleID.ToUpper();
                    PKGSENDER.MonitorPkgSenderTaskBackgroundWorker = new BackgroundWorker();
                    PKGSENDER.MonitorPkgSenderTaskBackgroundWorker.WorkerSupportsCancellation = true;
                    MonitorPKGSenderTask(PKGSENDER.MonitorPkgSenderTaskBackgroundWorker);
                }
                else if (PKGSENDER.JSON.SENDPKG.status != "fail")
                {
                    PKGSENDER.JSON.SENDPKG.error = send_pkg_json.error.ToString();
                    ShowError("Operation failed : \n\nStatus : " + PKGSENDER.JSON.SENDPKG.status + "\n" + PKGSENDER.JSON.SENDPKG.error, true);
                    toolStripMenuItem18.Text = "Remote PKG Installer | Status : Idle";
                    RpiSendPkgtoolStripMenuItem2.Text = "Send PKG to PS4";
                    toolStripMenuItem16.Text = "Remote PKG Installer | Status : Idle";
                    RpiSendPkgtoolStripMenuItem1.Text = "Send PKG to PS4";
                    toolStripStatusLabel2.Text = "...";
                    toolStripProgressBar1.Value = 0;
                    EnableControls_PkgSender();
                    EnableTabPages(flatTabControl1);
                    EnableControls(darkMenuStrip1);
                    return;
                }

                while (true)
                {
                    if (bg.CancellationPending)
                    {
                        e.Cancel = true;
                        break;
                    }

                    if (PKGSENDER.pkgSendStopped)
                    {
                        break;
                    }

                    if (renameBackFile)
                    {
                        break;
                    }
                }

                // Update original filename in gridview
                foreach (DataGridViewRow row in PKGGridView.Rows)
                {
                    if (row.Cells[0].Value.ToString().Equals(tempFilename))
                    {
                        row.Cells[0].Value = originalName;
                    }
                }

                // Rename original filename
                File.Move(TEMPFILENAMESENDPKG, currentPkgFile);
                PKG.SelectedPKGFilename = currentPkgFile;
            };
            bg.RunWorkerCompleted += delegate
            {
            };
            bg.RunWorkerAsync();
        }

        private async void Rpi_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            Logger.LogInformation("Checking RPI requirement..");
            var CheckRequirement = PKGSENDER.CheckRequirement();
            if (CheckRequirement != "OK")
            {
                ShowError(CheckRequirement, true);
                EnableControls_PkgSender();
                EnableTabPages(flatTabControl1);
                EnableControls(darkMenuStrip1);
                return;
            }

            if (clickedMenuItem == RpiCheckPkgInstalledtoolStripMenuItem1 || clickedMenuItem == RpiCheckPkgInstalledtoolStripMenuItem2)
            {
                await CheckIfAppInstalledOnPS4();
            }
            if (clickedMenuItem == RpiSendPkgtoolStripMenuItem1 || clickedMenuItem == RpiSendPkgtoolStripMenuItem2)
            {
                InitializePKGSender();
            }
            if (clickedMenuItem == RpiUninstallBasePKGToolStripMenuItem1 || clickedMenuItem == RpiUninstallBasePKGToolStripMenuItem2)
            {
                UninstallBasePkgFromPs4();
            }
            if (clickedMenuItem == RpiUninstallPatchPKGToolStripMenuItem1 || clickedMenuItem == RpiUninstallPatchPKGToolStripMenuItem2)
            {
                UninstallPatchPkgFromPs4();
            }
            if (clickedMenuItem == RpiUninstallDlcPKGToolStripMenuItem1 || clickedMenuItem == RpiUninstallDlcPKGToolStripMenuItem2)
            {
                UninstallDlcPkgFromPs4();
            }
            if (clickedMenuItem == RpiUninstallThemePKGToolStripMenuItem1 || clickedMenuItem == RpiUninstallThemePKGToolStripMenuItem2)
            {
                UninstallThemePkgFromPs4();
            }
        }

        private void MonitorPKGSenderTask(BackgroundWorker bg)
        {
            bg.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                dynamic taskProgressJson = null;

                darkStatusStrip1.Invoke((MethodInvoker)delegate
                {
                    toolStripStatusLabel2.Text = "Preparing download..";
                    Logger.LogInformation("Preparing download..");
                    darkStatusStrip1.Refresh();
                });

                for (int i = 0; i < 100; i++)
                {
                    try
                    {
                        PKGSENDER.isPreparing = true;

                        // Monitor task progress
                        taskProgressJson = PKGSENDER.GetTaskProgress();
                        if (taskProgressJson == null)
                        {
                            // Handle the null case
                        }

                        PKGSENDER.JSON.MONITORTASK.packagePreparingTotal = Convert.ToInt32(taskProgressJson.preparing_percent.ToString());

                        if (PKGSENDER.JSON.MONITORTASK.packagePreparingTotal == 100)
                        {
                            break;
                        }
                    }
                    catch
                    {
                        // Handle exceptions
                    }
                }

                PKGSENDER.isPreparing = false;

                taskProgressJson = PKGSENDER.GetTaskProgress();
                if (taskProgressJson == null)
                {
                    // Handle the null case
                }

                PKGSENDER.JSON.MONITORTASK.packageFilesizeTotal = taskProgressJson.length.ToString();
                PKGSENDER.JSON.MONITORTASK.packageTransferredTotal = taskProgressJson.transferred.ToString();
                PKGSENDER.JSON.MONITORTASK.TimeRemainingTotal = taskProgressJson.rest_sec_total.ToString();
                toolStripProgressBar1.Maximum = Convert.ToInt32(PKGSENDER.JSON.MONITORTASK.TimeRemainingTotal);
                int totalRemainTime = toolStripProgressBar1.Maximum;
                int increment = 0;

                for (long i = Convert.ToInt64(PKGSENDER.JSON.MONITORTASK.packageTransferredTotal); i < Convert.ToInt64(PKGSENDER.JSON.MONITORTASK.packageFilesizeTotal); i++)
                {
                    try
                    {
                        PKGSENDER.isPreparing = false;

                        if (bg.CancellationPending)
                        {
                            e.Cancel = true;
                            PKGSENDER.taskMonitorIsCancelling = false;
                            break;
                        }

                        if (PKGSENDER.taskMonitorIsCancelling)
                        {
                            PKGSENDER.taskMonitorIsCancelling = false;
                            break;
                        }

                        taskProgressJson = PKGSENDER.GetTaskProgress();
                        if (taskProgressJson == null)
                        {
                            // Handle the null case
                        }

                        if (taskProgressJson.status.ToString() == "fail")
                        {
                            PKGSENDER.pkgSendStopped = true;
                            break;
                        }

                        PKGSENDER.JSON.MONITORTASK.packageFilesizeTotal = taskProgressJson.length.ToString();
                        PKGSENDER.JSON.MONITORTASK.packageTransferredTotal = taskProgressJson.transferred.ToString();
                        PKGSENDER.JSON.MONITORTASK.TimeRemainingTotal = taskProgressJson.rest_sec_total.ToString();

                        long transferredTotal = Convert.ToInt64(PKGSENDER.JSON.MONITORTASK.packageTransferredTotal);
                        long filesizeTotal = Convert.ToInt64(PKGSENDER.JSON.MONITORTASK.packageFilesizeTotal);
                        var packageTransferredTotalFormatted = ByteSize.FromBytes(transferredTotal).ToString();
                        var packageFilesizeTotalFormatted = ByteSize.FromBytes(filesizeTotal).ToString();

                        if (Convert.ToInt32(PKGSENDER.JSON.MONITORTASK.TimeRemainingTotal) == 0)
                        {
                            toolStripProgressBar1.Value = 0;
                            break;
                        }

                        increment = totalRemainTime - Convert.ToInt32(PKGSENDER.JSON.MONITORTASK.TimeRemainingTotal);

                        darkStatusStrip1.Invoke((MethodInvoker)delegate
                        {
                            toolStripProgressBar1.Increment(increment);
                            toolStripStatusLabel2.Text = $"Installing.. ({packageTransferredTotalFormatted}/{packageFilesizeTotalFormatted})";
                            Logger.LogInformation($"Installing.. ({packageTransferredTotalFormatted}/{packageFilesizeTotalFormatted})");
                            darkStatusStrip1.Refresh();
                        });

                        totalRemainTime = Convert.ToInt32(PKGSENDER.JSON.MONITORTASK.TimeRemainingTotal);
                    }
                    catch
                    {
                        // Handle exceptions
                    }
                }

                if (Convert.ToInt32(PKGSENDER.JSON.MONITORTASK.TimeRemainingTotal) == 0)
                {
                    PKGSENDER.pkgSendDone = true;
                }

            };
            bg.ProgressChanged += delegate (object sender, ProgressChangedEventArgs progressChangedEventArgs)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    darkStatusStrip1.Invoke((MethodInvoker)delegate
                    {
                        toolStripStatusLabel2.Text = progressChangedEventArgs.UserState.ToString();
                        toolStripProgressBar1.Value = progressChangedEventArgs.ProgressPercentage;
                        darkStatusStrip1.Refresh();
                    });
                });
            };
            bg.RunWorkerCompleted += delegate
            {
                this.Invoke((MethodInvoker)delegate
                {
                    EnableControls_PkgSender();
                    EnableTabPages(flatTabControl1);
                    EnableControls(darkMenuStrip1);
                    toolStripStatusLabel2.Text = "...";
                    toolStripProgressBar1.Value = 0;

                    if (PKGSENDER.pkgSendStopped || PKGSENDER.taskMonitorIsCancelling)
                    {
                        ShowError("Operation stopped.", true);
                    }

                    if (PKGSENDER.pkgSendDone)
                    {
                        if (!PKGSENDER.JSON.CHECKAPPEXISTS.baseAppExist)
                        {
                            ShowInformation("Patch PKG sent to PS4. Manually install it after base PKG is installed : Notifications -> Downloads", true);
                        }
                        else
                        {
                            ShowInformation("PKG installed.", true);
                        }
                    }

                    renameBackFile = true;

                    // Kill the server if it is running.
                    // killNodeJS();

                    toolStripMenuItem18.Text = "Remote PKG Installer | Status : Idle";
                    RpiSendPkgtoolStripMenuItem2.Text = "Send PKG to PS4";
                    toolStripMenuItem16.Text = "Remote PKG Installer | Status : Idle";
                    RpiSendPkgtoolStripMenuItem1.Text = "Send PKG to PS4";
                });
            };
            bg.RunWorkerAsync();
        }

        private void UninstallDlcPkgFromPs4()
        {
            DisableTabPages(flatTabControl1, "tabPage1");
            DisableControls(darkMenuStrip1);
            DisableControls_PkgSender();

            PS4_Tools.PKG.SceneRelated.Unprotected_PKG read = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);
            Param_SFO.PARAM_SFO psfo = read.Param;

            Logger.LogInformation("Uninstalling addon PKG (" + read.PS4_Title + ")..");

            // Uninstall installed addon pkg

            dynamic uninstall_patch_json = null;

            uninstall_patch_json = PKGSENDER.UninstallAddonTheme(psfo);
            if (uninstall_patch_json == null)
            {
                ShowError("An error occurred while trying to communicate with the PS4. Launch/restart the Remote Package Installer application on the PS4 and do not minimize it.", true);
                EnableControls_PkgSender();
                EnableTabPages(flatTabControl1);
                EnableControls(darkMenuStrip1);
                return;
            }

            EnableControls_PkgSender();
            EnableTabPages(flatTabControl1);
            EnableControls(darkMenuStrip1);
            PKGSENDER.JSON.UNINTSALLADDON.status = uninstall_patch_json.status.ToString();

            if (PKGSENDER.JSON.UNINTSALLADDON.status == "success")
            {
                ShowInformation("PKG uninstalled.", true);
            }
            else
            {
                ShowError("Uninstall failed.", true);
            }
        }

        private void UninstallThemePkgFromPs4()
        {
            DisableTabPages(flatTabControl1, "tabPage1");
            DisableControls(darkMenuStrip1);
            DisableControls_PkgSender();

            PS4_Tools.PKG.SceneRelated.Unprotected_PKG read = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);
            Param_SFO.PARAM_SFO psfo = read.Param;

            Logger.LogInformation("Uninstalling theme PKG (" + read.PS4_Title + ")..");

            // Uninstall installed theme pkg

            dynamic uninstall_theme_json = null;

            uninstall_theme_json = PKGSENDER.UninstallAddonTheme(psfo);
            if (uninstall_theme_json == null)
            {
                ShowError("An error occurred while trying to communicate with the PS4. Launch/restart the Remote Package Installer application on the PS4 and do not minimize it.", true);
                EnableControls_PkgSender();
                EnableTabPages(flatTabControl1);
                EnableControls(darkMenuStrip1);
                return;
            }

            PKGSENDER.JSON.UNINTSALLTHEME.status = uninstall_theme_json.status.ToString();

            EnableControls_PkgSender();
            EnableTabPages(flatTabControl1);
            EnableControls(darkMenuStrip1);

            if (PKGSENDER.JSON.UNINTSALLTHEME.status == "success")
            {
                ShowInformation("PKG uninstalled.", true);
            }
            else
            {
                ShowError("Uninstall failed.", true);
            }
        }

        private void toolStripMenuItem26_Click(object sender, EventArgs e)
        {
            UninstallDlcPkgFromPs4();
        }

        private void toolStripMenuItem27_Click(object sender, EventArgs e)
        {
            UninstallThemePkgFromPs4();
        }

        private void UninstallBasePkgFromPs4()
        {
            DisableTabPages(flatTabControl1, "tabPage1");
            DisableControls(darkMenuStrip1);
            DisableControls_PkgSender();

            PS4_Tools.PKG.SceneRelated.Unprotected_PKG read = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);
            Param_SFO.PARAM_SFO psfo = read.Param;
            Logger.LogInformation("Uninstalling base PKG (" + read.PS4_Title + ")..");

            // Check if pkg is installed

            dynamic app_exists_json = null;

            app_exists_json = PKGSENDER.CheckIfPkgInstalled(psfo);
            if (app_exists_json == null)
            {
                ShowError("An error occurred while trying to communicate with the PS4. Launch/restart the Remote Package Installer application on the PS4 and do not minimize it.", true);
                EnableControls_PkgSender();
                EnableTabPages(flatTabControl1);
                EnableControls(darkMenuStrip1);
                return;
            }

            PKGSENDER.JSON.CHECKAPPEXISTS.status = app_exists_json.status.ToString();

            if (PKGSENDER.JSON.CHECKAPPEXISTS.status == "success")
            {
                PKGSENDER.JSON.CHECKAPPEXISTS.exists = app_exists_json.exists.ToString();
                if (PKGSENDER.JSON.CHECKAPPEXISTS.exists == "false")
                {
                    ShowInformation("PKG is not installed.", true);
                }
                else
                {
                    // Uninstall installed pkg

                    dynamic uninstall_app_json = null;
                    uninstall_app_json = PKGSENDER.UninstallGame(psfo);
                    if (uninstall_app_json == null)
                    {
                        ShowError("An error occurred while trying to communicate with the PS4. Launch/restart the Remote Package Installer application on the PS4 and do not minimize it.", true);
                        EnableControls_PkgSender();
                        EnableTabPages(flatTabControl1);
                        EnableControls(darkMenuStrip1);
                        return;
                    }

                    EnableControls_PkgSender();
                    EnableTabPages(flatTabControl1);
                    EnableControls(darkMenuStrip1);
                    PKGSENDER.JSON.UNINTSALLAPP.status = uninstall_app_json.status.ToString();

                    if (PKGSENDER.JSON.UNINTSALLAPP.status == "success")
                    {
                        ShowInformation("PKG uninstalled.", true);
                    }
                    else
                    {
                        ShowError("Uninstall failed.", true);
                    }
                }
            }
        }

        private void UninstallPatchPkgFromPs4()
        {
            DisableTabPages(flatTabControl1, "tabPage1");
            DisableControls(darkMenuStrip1);
            DisableControls_PkgSender();

            PS4_Tools.PKG.SceneRelated.Unprotected_PKG read = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);
            Param_SFO.PARAM_SFO psfo = read.Param;

            Logger.LogInformation("Uninstalling patch PKG (" + read.PS4_Title + ")..");

            // Uninstall installed patch pkg

            dynamic uninstall_patch_json = null;

            uninstall_patch_json = PKGSENDER.UninstallPatch(psfo);
            if (uninstall_patch_json == null)
            {
                ShowError("An error occurred while trying to communicate with the PS4. Launch/restart the Remote Package Installer application on the PS4 and do not minimize it.", true);
                EnableControls_PkgSender();
                EnableTabPages(flatTabControl1);
                EnableControls(darkMenuStrip1);
                return;
            }

            PKGSENDER.JSON.UNINTSALLPATCH.status = uninstall_patch_json.status.ToString();

            EnableControls_PkgSender();
            EnableTabPages(flatTabControl1);
            EnableControls(darkMenuStrip1);

            if (PKGSENDER.JSON.UNINTSALLPATCH.status == "success")
            {
                ShowInformation("PKG uninstalled.", true);
            }
            else
            {
                ShowError("Uninstall failed.", true);
            }
        }
        #endregion PKGSender


        private void OpenProgramSettings()
        {
            Logger.LogInformation("Opening Program Settings..");

            ProgramSetting form = new ProgramSetting();
            form.ShowDialog();
            this.BringToFront();

            UpdatePKGColorLabel();

            if (form.Refresh)
            {
                RefreshPkgList();
            }
            else
            {
                #region checkGridHideUnhide
                UpdateDataGridViewColumnVisibility();
                SetBackgroundMusicVolume();
                PopulateGroupedView(); // reflect column-visibility changes in the grouped view
                // Fill shadPS4 statuses immediately after a download/toggle
                if (appSettings_.Shadps4Check && PKGGridView.DataSource is DataTable shadDt)
                    ApplyShadps4Status(shadDt);
                #endregion checkGridHideUnhide
            }
        }

        private void ExtractTrophyIcon()
        {
            if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
            {
                Logger.LogInformation("Extracting trophy icon..");
                string failExtract = "";

                if (Trophy.TrophyFilenameToExtractList.Count > 1)
                {
                    var trophyInfo = Trophy.TrophyFilenameToExtractList.Zip(Trophy.ImageToExtractList, (name, image) => new { Name = name, Image = image });

                    foreach (var info in trophyInfo)
                    {
                        try
                        {
                            using (Bitmap tempImage = new Bitmap(Helper.Bitmap.BytesToImage(Trophy.trophy.ExtractFileToMemory(info.Name))))
                            {
                                tempImage.Save(Path.Combine(fbd.SelectedPath, info.Name), ImageFormat.Png);
                            }
                        }
                        catch (Exception ex)
                        {
                            failExtract += ex.Message + "\n";
                        }
                    }

                    if (string.IsNullOrEmpty(failExtract))
                    {
                        ShowInformation("Trophy icons extracted.", true);
                    }
                    else
                    {
                        ShowError("Some trophy icons failed to extract.", true);
                        //Logger.LogError(failExtract);
                    }
                }
                else
                {
                    ShowError("Error occured when trying to extract trophy icons.", true);
                }
            }
        }

        private void ExtractTrophyImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExtractTrophyIcon();
        }

        private void ContextMenuBackgroundImage_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            // save image
            if (clickedMenuItem == saveImageToolStripMenuItem && flatTabControlBgi.SelectedTab == tabPagePic0)
            {
                SaveBackgroundImage(pbPIC0);
            }
            if (clickedMenuItem == saveImageToolStripMenuItem && flatTabControlBgi.SelectedTab == tabPagePic1)
            {
                SaveBackgroundImage(pbPIC1);
            }

            // set image as background 
            if (clickedMenuItem == SetImageAsDesktopBackgroundToolStripMenuItem && flatTabControlBgi.SelectedTab == tabPagePic0)
            {
                SetImageAsDesktopBackground(pbPIC0);
            }
            if (clickedMenuItem == SetImageAsDesktopBackgroundToolStripMenuItem && flatTabControlBgi.SelectedTab == tabPagePic1)
            {
                SetImageAsDesktopBackground(pbPIC1);
            }
        }

        private void SaveBackgroundImage(PictureBox pb)
        {
            try
            {
                if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
                {
                    using (Bitmap tempImage = new Bitmap(pb.Image))
                    {
                        string pic = pb.Name == "pbPIC0" ? "PIC0" : "PIC1";
                        string filePath = Path.Combine(fbd.SelectedPath, $"{PKG.CurrentPKGTitle}_{pic}.PNG");
                        tempImage.Save(filePath, ImageFormat.Png);
                        ShowInformation("Background image saved.", false);
                        Logger.LogInformation($"Background image saved to \"{fbd.SelectedPath}\".");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to save background image: {ex.Message}", true);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SystemParametersInfo(uint uiAction, uint uiParam, String pvParam, uint fWinIni);

        private const uint SPI_SETDESKWALLPAPER = 0x14;
        private const uint SPIF_UPDATEINIFILE = 0x1;
        private const uint SPIF_SENDWININICHANGE = 0x2;

        private void SetImageAsDesktopBackground(PictureBox pb)
        {
            try
            {
                if (pb.Image == null)
                    return;

                using (Bitmap tempImage = new Bitmap(pb.Image))
                {
                    string savedImagePath = Path.Combine(AppDataDirectory, "Wallpaper");
                    Directory.CreateDirectory(savedImagePath);
                    string imagePath = Path.Combine(savedImagePath, "Wallpaper.JPG");

                    tempImage.Save(imagePath, ImageFormat.Jpeg);

                    SystemParametersInfo(SPI_SETDESKWALLPAPER, 1, imagePath, SPIF_UPDATEINIFILE);
                    Logger.LogInformation("Image set as desktop background.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("An error occurred: " + ex.Message);
            }
        }


        private void darkDataGridView3_SelectionChanged(object sender, EventArgs e)
        {
            this.TrophyGridView.ClearSelection();
        }

        private void ExtractDecryptedEntry()
        {
            if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
            {
                PS4_Tools.PKG.SceneRelated.Unprotected_PKG PS4_PKG = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);

                Logger.LogInformation("Extracting decrypted items..");
                //load pkg file
                string itemIndex = "";
                var IO = new EndianIO(PKG.SelectedPKGFilename, EndianType.BigEndian, true);
                long file_length = IO.Length;
                if (file_length < 0x1C)
                {
                    IO.Close();
                    return;
                }

                //set output path for extracted files
                string path2pkg = Path.GetDirectoryName(PKG.SelectedPKGFilename);
                string fullpkgpath = Path.GetFullPath(PKG.SelectedPKGFilename);
                string pkgbasename = Path.GetFileNameWithoutExtension(PKG.SelectedPKGFilename);
                string pkgfilename = Path.GetFileName(PKG.SelectedPKGFilename);
                string outputpath = fbd.SelectedPath; // Path.Combine(path2pkg, pkgbasename);
                // textBox1.AppendText("\r\n\r\npath2pkg:   " + path2pkg);     //  C:\Downloads\ps4packages\
                // textBox1.AppendText("\r\nfullpkgpath:   " + fullpkgpath);   //  C:\Downloads\ps4packages\Up1018...V0100.pkg
                // textBox1.AppendText("\r\npkgbasename:   " + pkgbasename);   //  Up1018...V0100
                // textBox1.AppendText("\r\npkgfilename:   " + pkgfilename);   //  Up1018...V0100.pkg
                //textBox1.AppendText("\r\n\r\noutput path:\r\n" + outputpath); //  C:\Downloads\ps4packages\Up1018...V0100 
                Tool.CreateDirectoryIfNotExists(outputpath);

                //read and decrypt part 1 of key seed
                if (file_length < (0x2400 + 0x100))
                {
                    IO.Close();
                    return;
                }
                IO.SeekTo(0x2400);
                byte[] data = Entry.Decrypt(IO.In.ReadBytes(256));
                for (int j = 0; j < data.Length; j++)
                {

                }

                //read file entry table
                uint entry_count = IO.In.SeekNReadUInt32(0x10);
                if (entry_count == 0) { IO.Close(); return; }
                uint file_table_offset = IO.In.SeekNReadUInt32(0x18);
                uint padded_size;

                uint strtab_count = 0;
                uint strtab_offset = 0;
                uint strtab_size = 0;

                if (file_length < (file_table_offset + (0x20 * entry_count)))
                {
                    IO.Close();
                    return;
                }
                IO.SeekTo(file_table_offset);
                PackageEntry[] entry = new PackageEntry[entry_count];
                for (int i = 0; i < entry_count; i++)
                {
                    entry[i].type = IO.In.ReadUInt32();
                    entry[i].unk1 = IO.In.ReadUInt32();
                    entry[i].flags1 = IO.In.ReadUInt32();
                    entry[i].flags2 = IO.In.ReadUInt32();
                    entry[i].offset = IO.In.ReadUInt32();
                    entry[i].size = IO.In.ReadUInt32();
                    entry[i].padding = IO.In.ReadBytes(8);

                    //set key index, encryption flag, string table properties
                    entry[i].key_index = ((entry[i].flags2 & 0xF000) >> 12);
                    entry[i].is_encrypted = ((entry[i].flags1 & 0x80000000) != 0) ? true : false;
                    if (entry[i].unk1 != 0) strtab_count++;
                    if (entry[i].type == 0x200)
                    {
                        strtab_offset = entry[i].offset;
                        strtab_size = entry[i].size;
                    }
                }

                //read strtab
                if (file_length < (strtab_offset + strtab_size))
                {
                    IO.Close();
                    return;
                }
                string[] entry_name = new string[entry_count];
                if (strtab_count > 0)
                {
                    IO.SeekTo(strtab_offset);
                    byte[] string_table = IO.In.ReadBytes(strtab_size);
                    for (int i = 0; i < entry_count - 1; i++)
                    {
                        if (entry[i].unk1 != 0x00)
                        { //has strtab entry
                            entry_name[i] = System.Text.Encoding.UTF8.GetString(string_table, Convert.ToInt32(entry[i].unk1), (Convert.ToInt32(entry[i + 1].unk1) - 1) - Convert.ToInt32(entry[i].unk1));
                        }
                        else
                        {
                            entry_name[i] = "";
                        }
                    }
                    if (entry[entry_count - 1].unk1 != 0x00)
                    {
                        entry_name[entry_count - 1] = System.Text.Encoding.UTF8.GetString(string_table, Convert.ToInt32(entry[entry_count - 1].unk1), (Convert.ToInt32(strtab_size) - 1) - Convert.ToInt32(entry[entry_count - 1].unk1));
                    }
                    else
                    {
                        entry_name[entry_count - 1] = "";
                    }
                }
                else
                {
                    for (int i = 0; i < entry_count; i++) entry_name[i] = "";
                }

                var errorExtract = new Dictionary<string, string>();

                for (int i = 0; i < entry_count; i++)
                {
                    string savepath;
                    string savename;
                    string extrasavepath;

                    if (file_length < (entry[i].offset + entry[i].size))
                    {
                        IO.Close();
                        return;
                    }

                    if (entry[i].is_encrypted != false)
                    {
                        //print file attributes


                        //combine file entry and rsa decrypted data to form key seed
                        byte[] entry_data = new byte[0x40];
                        Array.Copy(entry[i].ToArray(), entry_data, 0x20);
                        Array.Copy(data, 0, entry_data, 0x20, 0x20);

                        //use sha256 to transform seed into aes iv and key
                        byte[] iv = new byte[0x10], key = new byte[0x10];
                        byte[] hash = Sha256(entry_data, 0, entry_data.Length);
                        Array.Copy(hash, 0, iv, 0, 0x10);
                        Array.Copy(hash, 0x10, key, 0, 0x10);

                        //output aes key and iv for current file


                        //read and decrypt current file
                        IO.In.BaseStream.Position = entry[i].offset;
                        if ((entry[i].size % 16) != 0)
                            padded_size = entry[i].size + (16 - (entry[i].size % 16));
                        else padded_size = entry[i].size;

                        //decrypt file
                        byte[] file_data = DecryptAes(key, iv, IO.In.ReadBytes(padded_size));

                        var entryOffset = $"0x{entry[i].offset:X8}";
                        if (!EncryptedEntryOffsetNameDictionary.TryGetValue(entryOffset, out var entryName))
                        {
                            entryName = $"entry_{i:D4}_{entryOffset}.bin";
                        }

                        try
                        {
                            if (entry[i].size > int.MaxValue)
                                throw new InvalidDataException("The decrypted entry is too large to hold in memory.");

                            savepath = Path.Combine(outputpath, entryName);
                            Array.Resize(ref file_data, checked((int)entry[i].size));
                            File.WriteAllBytes(savepath, file_data);
                        }
                        catch (Exception a)
                        {
                            errorExtract[entryName] = a.Message;
                        }

                    }
                }
                IO.Close();
                if (errorExtract.Count > 0)
                {
                    ShowWarning("Failed to extract some entries. See logs.", false);
                    Logger.LogWarning($"Failed to extract some entries:\n{string.Join("\n", errorExtract)}");
                }
                else
                {
                    ShowInformation("All decrypted entries extracted.", true);
                }
            }
        }

        private void ExtractDecryptedEntryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExtractDecryptedEntry();
        }

        private void ExtractAllEntry()
        {
            if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
            {
                Logger.LogInformation("Extracting PKG entries..");

                try
                {
                    List<string> encryptedEntries = new List<string>();
                    Dictionary<string, string> failedEntries = new Dictionary<string, string>();
                    var pkgPath = PKG.SelectedPKGFilename;

                    using (var pkgFile = File.OpenRead(pkgPath))
                    {
                        var pkg = new PkgReader(pkgFile).ReadPkg();

                        foreach (var entry in EntryIdNameDictionary)
                        {
                            var name = entry.Value;
                            try
                            {
                                var idx = int.Parse(entry.Key);
                                if (idx < 0 || idx >= pkg.Metas.Metas.Count)
                                {
                                    failedEntries[name] = "Entry number is out of range.";
                                    continue;
                                }

                                var outName = name.Replace("_SHA", ".SHA").Replace("_DAT", ".DAT").Replace("_SFO", ".SFO").Replace("_XML", ".XML").Replace("_SIG", ".SIG").Replace("_PNG", ".PNG").Replace("_JSON", ".JSON").Replace("_DDS", ".DDS").Replace("_TRP", ".TRP").Replace("_AT9", ".AT9");
                                var outPath = Path.Combine(fbd.SelectedPath, outName);
                                var meta = pkg.Metas.Metas[idx];

                                using (var outFile = File.Create(outPath))
                                {
                                    outFile.SetLength(meta.DataSize);
                                    if (meta.Encrypted)
                                    {
                                        encryptedEntries.Add(name);
                                    }

                                    new SubStream(pkgFile, meta.DataOffset, meta.DataSize).CopyTo(outFile);
                                }
                            }
                            catch (Exception ex)
                            {
                                failedEntries[name] = ex.Message;
                                Logger.LogError($"Error extracting {name} : {ex.Message}");
                            }
                        }
                    }

                    if (failedEntries.Count > 0)
                    {
                        ShowWarning("Failed to extract some entries. See logs.", false);
                        Logger.LogWarning($"Failed to extract some entries:\n{string.Join("\n", failedEntries.Select(item => $"{item.Key}: {item.Value}"))}");
                    }
                    else if (encryptedEntries.Count > 0)
                    {
                        ShowWarning("All entries extracted. Encrypted entries were saved as encrypted bytes.", false);
                        Logger.LogWarning($"Encrypted entries were saved without decryption:\n{string.Join("\n", encryptedEntries)}");
                    }
                    else
                    {
                        ShowInformation($"All entries extracted.", true);
                    }
                }
                catch (Exception a)
                {
                    ShowError(a.Message, true);
                }
            }
        }

        private void ExtractAllEntryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExtractAllEntry();
        }

        private void dgvEntryList_SelectionChanged(object sender, EventArgs e)
        {
            this.dgvEntryList.ClearSelection();
        }

        private void dgvHeader_SelectionChanged(object sender, EventArgs e)
        {
            this.dgvHeader.ClearSelection();
        }

        private void PopulatePKGDataToTreeView()
        {
            string orbisPubCmdErrorMessage = "";
            bool renamed = false;
            string origPath = null;
            string tempPath = null;
            var bg = new BackgroundWorker();
            bg.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                Logger.LogInformation("Viewing PKG file list..");
                DisableControls(darkMenuStrip1);
                DisableControls(PKGTreeView);

                List<string> allFilePaths = new List<string>();
                List<string> fileListWithExtensions = new List<string>();
                List<string> dirList = new List<string>();

                origPath = PKG.SelectedPKGFilename;
                string dir = GetOrbisTempDirFor(origPath);
                tempPath = Path.Combine(dir, "ps4pkgtool_orbis_" + Guid.NewGuid().ToString("N") + ".pkg");
                File.Move(origPath, tempPath);
                renamed = true;
                string safePkgPath = tempPath;

                try
                {

                    var pkgListStartInfo = new ProcessStartInfo
                    {
                        FileName = Helper.AppDataDirectory + "orbis-pub-cmd.exe",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    pkgListStartInfo.ArgumentList.Add("img_file_list");
                    pkgListStartInfo.ArgumentList.Add("--passcode");
                    pkgListStartInfo.ArgumentList.Add(DefaultOrbisPasscode);
                    pkgListStartInfo.ArgumentList.Add("--oformat");
                    pkgListStartInfo.ArgumentList.Add("long+original_size");
                    pkgListStartInfo.ArgumentList.Add(safePkgPath);
                    using Process pkgListProcess = new Process { StartInfo = pkgListStartInfo };

                    pkgListProcess.Start();
                    // Drain stdout concurrently so a stalled process can't deadlock the pipe,
                    // then bound the wait and kill on timeout.
                    Task<string> listReadTask = pkgListProcess.StandardOutput.ReadToEndAsync();
                    if (!pkgListProcess.WaitForExit(30000))
                    {
                        try { pkgListProcess.Kill(); pkgListProcess.WaitForExit(); } catch { }
                    }
                    _fileSizes.Clear();
                    string stdoutText = listReadTask.Result;
                    int exitCode = pkgListProcess.ExitCode;
                    // Only flag as error on non-zero exit OR explicit [Error] tag (not filenames containing "error")
                    bool hasError = exitCode != 0 || stdoutText.Contains("[Error]");
                    if (hasError)
                    {
                        e.Cancel = true;
                        orbisPubCmdErrorMessage = !string.IsNullOrWhiteSpace(stdoutText)
                            ? stdoutText.Trim()
                            : $"(exit code {exitCode}, no output)";
                        Logger.LogInformation($"ERROR: orbis exit={exitCode}: {orbisPubCmdErrorMessage}");
                        return;
                    }

                    // Parse stdout lines for file listing
                    _pkgDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in stdoutText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.Contains("[Error]"))
                            continue;
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        // Format: "F  12345678 2018-11-21 06:00:00 Image0/path/file.bin"
                        //     or: "D            0                     Image0/path/dir"
                        char entryType = line[0];
                        string entryPath = line;
                        long size = 0;
                        int pathIdx = line.IndexOf("Image0");
                        if (pathIdx < 0) pathIdx = line.IndexOf("Sc0");
                        if (pathIdx >= 0)
                        {
                            entryPath = line.Substring(pathIdx);
                            string prefix = line.Substring(0, pathIdx).Trim();
                            string[] parts = prefix.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && long.TryParse(parts[1], out long s))
                                size = s;
                            _fileSizes[entryPath] = size;
                        }
                        if (entryType == 'D' || entryType == 'd')
                            _pkgDirectories.Add(entryPath);
                        allFilePaths.Add(entryPath);
                    }

                    var array = allFilePaths.ToArray();

                    // Build tree on UI thread in a single batch - not per-node Invoke
                    PKGTreeView.Invoke((MethodInvoker)delegate
                    {
                        PKGTreeView.BeginUpdate();
                        PKGTreeView.PathSeparator = @"/";
                        PKGTreeView.ImageList = imageList1;
                        TreeNode lastNode = null;
                        string subPathAgg;
                        int count = 0;
                        foreach (string path in array)
                        {
                            subPathAgg = string.Empty;
                            string[] segments = path.Split('/');
                            for (int i = 0; i < segments.Length; i++)
                            {
                                string subPath = segments[i];
                                subPathAgg += subPath + '/';
                                TreeNode[] nodes = PKGTreeView.Nodes.Find(subPathAgg, true);
                                if (nodes.Length == 0)
                                {
                                    lastNode = lastNode == null
                                        ? PKGTreeView.Nodes.Add(subPathAgg, subPath)
                                        : lastNode.Nodes.Add(subPathAgg, subPath);
                                    bool isDir = i < segments.Length - 1 || _pkgDirectories.Contains(path);
                                    int iconIdx = isDir ? 0 : IconFor(subPath);
                                    lastNode.ImageIndex = iconIdx;
                                    lastNode.SelectedImageIndex = iconIdx;
                                }
                                else
                                {
                                    lastNode = nodes[0];
                                    lastNode.ImageIndex = 0;
                                    lastNode.SelectedImageIndex = 0;
                                }
                            }
                            lastNode = null;
                            count++;
                            if (count % 100 == 0)
                                toolStripStatusLabel2.Text = $"Reading {count}/{array.Length}";
                        }
                        PKGTreeView.EndUpdate();
                    });
                    toolStripStatusLabel2.Text = $"...";
                }
                finally
                {
                    if (renamed && File.Exists(tempPath) && !File.Exists(origPath))
                    {
                        try { File.Move(tempPath, origPath); } catch { }
                    }
                    DeleteOrbisTempDir(tempPath);
                }
            };
            bg.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
            {
                // Restore original filename
                if (renamed && File.Exists(tempPath) && !File.Exists(origPath))
                {
                    try { File.Move(tempPath, origPath); }
                    catch (Exception ex) { Logger.LogError("Failed to restore PKG filename. Recover from " + tempPath + ": " + ex.Message); }
                }
                DeleteOrbisTempDir(tempPath);

                if (e.Error != null)
                {
                    Logger.LogError($"View PKG list worker failed: {e.Error.Message}");
                    ShowError($"Failed to list PKG files:\n{e.Error.Message}", true);
                }
                else if (e.Cancelled)
                {
                    string msg = FormatOrbisError(orbisPubCmdErrorMessage);
                    ShowError($"orbis-pub-cmd error:\n{msg}", true);
                }
                Logger.LogInformation("PKG file list loaded.");
                EnableControls(darkMenuStrip1);
                EnableControls(PKGTreeView);

                toolStripStatusLabel2.Text = "...";
                toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                toolStripProgressBar1.Value = 0;
                // Select the first root node (Image0) so the ListView populates
                if (PKGTreeView.Nodes.Count > 0)
                {
                    rootNodes = new List<TreeNode>();
                    foreach (TreeNode rootNode in PKGTreeView.Nodes)
                        rootNodes.Add(rootNode);
                    PKGTreeView.SelectedNode = PKGTreeView.Nodes[0];
                    TreeView.currentNode = PKGTreeView.Nodes[0];
                    PKG.NodeFullPath = PKGTreeView.Nodes[0].FullPath;
                    PopulateListView();
                    var fixTimer = new System.Windows.Forms.Timer { Interval = 200 };
                    fixTimer.Tick += (_, _) =>
                    {
                        fixTimer.Stop(); fixTimer.Dispose();
                        listView1.RefreshLayout();
                        listView1.Refresh();
                    };
                    fixTimer.Start();
                }
            };
            bg.RunWorkerAsync();
        }

        private void extractToToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CheckOrbisPubCmdExists()) return;
            if (PKGTreeView.SelectedNode == null) return;
            if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
            {
                string extractLocation = fbd.SelectedPath;
                string path = PKGTreeView.SelectedNode.FullPath;
                // If it's a directory, add trailing slash
                if (PKGTreeView.SelectedNode.Nodes.Count > 0 && !path.EndsWith("/"))
                    path += "/";
                ExtractSelectedPKGData(new List<string> { path }, extractLocation, preserveStructure: true);
            }
        }

        private void KillProcess(string processName)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                        Logger.LogInformation($"{processName} killed.");
                    }
                    catch (Exception ex)
                    {
                        // Handle any exception that occurred during process termination
                        Logger.LogError($"Error while killing process {processName}: {ex.Message}");
                    }
                }
            }
            catch
            {

            }

        }

        private BackgroundWorker _extractWorker;
        private BackgroundWorker _selectedExtractWorker;
        // Set on the UI thread when Stop Extract is clicked; read by the workers after orbis dies.
        // BackgroundWorker.CancellationPending has no memory barrier, so it can read stale false -
        // this volatile flag is the reliable stop signal.
        private volatile bool _extractionStopRequested;

        /// <summary>
        /// During extraction only the Stop Extract button (and the status strip, which hosts
        /// the progress bar) stay enabled - everything else is disabled so no other operation
        /// can run or disturb the progress display.
        /// </summary>
        private void SetExtractionUiEnabled(bool enabled)
        {
            foreach (Control c in Controls)
            {
                if (c is StatusStrip) continue; // keep the progress bar visible
                if (c is System.Windows.Forms.TabControl tabs)
                {
                    foreach (TabPage page in tabs.TabPages)
                    {
                        if (page.Name == "tabPage7")
                        {
                            // File-browser tab stays reachable (it hosts the stop button);
                            // its other controls are toggled individually.
                            SetControlsEnabledRecursive(page, enabled, btnExtractFullPKG);
                        }
                        else
                        {
                            page.Enabled = enabled;
                        }
                    }
                    continue;
                }
                if (c != btnExtractFullPKG)
                    c.Enabled = enabled;
            }
        }

        /// <summary>
        /// Toggles every control under parent except keepEnabled and its ancestor chain
        /// (a disabled parent would disable the kept control too). Ancestors are still
        /// recursed into, so their OTHER children (e.g. the file-browser buttons that
        /// share panel6 with the stop button) get toggled.
        /// </summary>
        private static void SetControlsEnabledRecursive(Control parent, bool enabled, Control keepEnabled)
        {
            foreach (Control c in parent.Controls)
            {
                bool keep = (c == keepEnabled || IsAncestorOf(keepEnabled, c));
                if (c.HasChildren)
                    SetControlsEnabledRecursive(c, enabled, keepEnabled);
                if (!keep)
                    c.Enabled = enabled;
            }
        }

        private static bool IsAncestorOf(Control child, Control candidate)
        {
            for (Control p = child.Parent; p != null; p = p.Parent)
                if (p == candidate) return true;
            return false;
        }

        private void ExtractFullPKG()
        {
            if (_extractWorker == null || !_extractWorker.IsBusy)
            {
                if (Helper.IsOperationRunning)
                {
                    ShowWarning("Another operation is already running. Please wait for it to complete.", false);
                    return;
                }
                _extractWorker = new BackgroundWorker { WorkerSupportsCancellation = true };
                if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
                {
                    Helper.IsOperationRunning = true;
                    string extractLocation = fbd.SelectedPath;
                    btnExtractFullPKG.Text = "Stop Extract";
                    SetExtractionUiEnabled(false); // lock the app - only the stop button stays usable
                    listView1?.Invalidate();       // force the DLV to repaint in its disabled (grey) state
                    listView1?.Refresh();
                    _extractWorker.DoWork += (sender, e) =>
                    {
                        this.Invoke((Action)(() =>
                        {
                            toolStripProgressBar1.Visible = true;
                            toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
                            toolStripProgressBar1.MarqueeAnimationSpeed = 30;
                        }));
                        // Extraction code here
                        PS4_Tools.PKG.SceneRelated.Unprotected_PKG PS4_PKG = PS4_Tools.PKG.SceneRelated.Read_PKG(PKG.SelectedPKGFilename);
                        string origPath = PKG.SelectedPKGFilename;
                        Logger.LogInformation($"Extracting: {Path.GetFileName(origPath)}");
                        Logger.LogInformation($"Extracting PKG ({origPath})..");
                        toolStripStatusLabel2.Text = $"Extracting PKG ({origPath})..";
                        extractLocation = $@"{extractLocation}\{PS4_PKG.PS4_Title.SanitizeFileName()}";
                        Tool.CreateDirectoryIfNotExists(extractLocation);

                        // Create ASCII-safe temp output dir (orbis-pub-cmd garbles non-ANSI paths)
                        // Short temp root - the deep AppData path + long PKG paths (e.g. Ultrawings'
                        // MSMixedReality schemas) exceed the 260-char MAX_PATH that orbis-pub-cmd allows.
                        string tempOutputDir = CreateOrbisTempDir("e");
                        Directory.CreateDirectory(tempOutputDir);

                        // Temp rename for Unicode-safe orbis-pub-cmd path (input)
                        string dir = GetOrbisTempDirFor(origPath);
                        string tempPath = Path.Combine(dir, "ps4pkgtool_orbis_" + Guid.NewGuid().ToString("N") + ".pkg");
                        bool renamed = false;
                        File.Move(origPath, tempPath);
                        renamed = true;
                        string pkgPath = tempPath;

                        try
                        {
                            var extractStartInfo = new ProcessStartInfo
                            {
                                FileName = AppDataDirectory + "orbis-pub-cmd.exe",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true
                            };
                            extractStartInfo.ArgumentList.Add("img_extract");
                            extractStartInfo.ArgumentList.Add("--passcode");
                            extractStartInfo.ArgumentList.Add(DefaultOrbisPasscode);
                            extractStartInfo.ArgumentList.Add(pkgPath);
                            extractStartInfo.ArgumentList.Add(tempOutputDir);
                            using Process extract = new Process { StartInfo = extractStartInfo };
                            extract.Start();
                            Task<string> extractReadTask = extract.StandardOutput.ReadToEndAsync();
                            if (!extract.WaitForExit(600000))
                            {
                                try { extract.Kill(); extract.WaitForExit(); } catch { }
                            }
                            string extractOutput = extractReadTask.Result;

                            int exitCode = extract.ExitCode;
                            // exit -1 = killed process (Stop button or timeout) - orbis returns 0 on
                            // success and 1 on errors, so -1 is the deterministic kill signal.
                            if (exitCode == -1 || _extractionStopRequested || _extractWorker.CancellationPending)
                            {
                                e.Cancel = true; // so RunWorkerCompleted shows "Extraction cancelled."
                            }
                            else if (exitCode != 0)
                            {
                                string errMsg = FormatOrbisError(extractOutput);
                                Logger.LogError($"orbis-pub-cmd exit code: {exitCode}, cancelPending={_extractWorker.CancellationPending}\n{errMsg}");
                                this.Invoke(() => ShowError($"orbis-pub-cmd error:\n{errMsg}", true));
                            }
                            else
                            {
                                // Move extracted files from ASCII temp dir to actual output path
                                if (Directory.Exists(tempOutputDir))
                                {
                                    foreach (string entry in Directory.GetFileSystemEntries(tempOutputDir))
                                    {
                                        string dest = Path.Combine(extractLocation, Path.GetFileName(entry));
                                        try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch (Exception ex) { Logger.LogWarning("Failed to delete dest dir: " + ex.Message); }
                                        if (Directory.Exists(entry))
                                            SafeMoveDirectory(entry, dest);
                                        else
                                        {
                                            try { if (File.Exists(dest)) File.Delete(dest); } catch (Exception ex) { Logger.LogWarning("Failed to delete dest file: " + ex.Message); }
                                            File.Move(entry, dest);
                                        }
                                    }
                                }
                                Logger.LogInformation($"PKG extracted to \"{extractLocation}\".");

                                this.Invoke(() => ShowInformation($"PKG extracted.", false));
                            }

                            // Clean up temp output dir
                            try { if (Directory.Exists(tempOutputDir)) Directory.Delete(tempOutputDir, true); } catch (Exception ex) { Logger.LogWarning("Failed to clean temp output dir: " + ex.Message); }
                        }
                        finally
                        {
                            if (renamed && File.Exists(tempPath))
                            {
                                try
                                {
                                    if (File.Exists(origPath))
                                        Logger.LogError("Failed to restore PKG filename because the original path already exists. Recover the PKG from: " + tempPath);
                                    else
                                        File.Move(tempPath, origPath);
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError("Failed to restore PKG filename. Recover the PKG from " + tempPath + ": " + ex.Message);
                                }
                            }
                            DeleteOrbisTempDir(tempPath);
                        }
                    };
                    _extractWorker.RunWorkerCompleted += (sender, e) =>
                    {
                        Helper.IsOperationRunning = false;
                        SetExtractionUiEnabled(true); // unlock the app (covers cancel/error/success)
                        this.Invoke((Action)(() =>
                        {
                            toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                            toolStripProgressBar1.Value = 0;
                        }));
                        toolStripStatusLabel2.Text = "...";
                        btnExtractFullPKG.Text = "Extract full PKG";
                        if (e.Cancelled)
                        {
                            ShowInformation("Extraction cancelled.", true);
                        }
                        else if (e.Error != null)
                        {
                            Logger.LogError($"Extraction failed: {e.Error.Message}");
                            ShowError($"Extraction failed:\n{e.Error.Message}", true);
                        }
                    };
                    _extractionStopRequested = false; // new extraction starts clean
                    _extractWorker.RunWorkerAsync();
                }
                else
                {
                    Logger.LogInformation("Stopping extraction...");
                    Helper.IsOperationRunning = false;
                    KillProcess("orbis-pub-cmd");
                    _extractWorker?.CancelAsync();
                    toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                    toolStripProgressBar1.Value = 0;
                    toolStripStatusLabel2.Text = "...";
                    btnExtractFullPKG.Text = "Extract full PKG";
                    ShowInformation("Operation cancelled.", true);
                }
            }
        }

        private void ExtractSelectedPKGData(List<string> nodeList, string extractLocation, bool preserveStructure = false)
        {
            var bgw = new BackgroundWorker();
            bgw.WorkerSupportsCancellation = true;
            _selectedExtractWorker = bgw;
            this.Invoke((Action)(() => btnExtractFullPKG.Text = "Stop Extract"));
            this.Invoke((Action)(() => SetExtractionUiEnabled(false))); // lock the app during extraction
            this.Invoke((Action)(() => { listView1?.Invalidate(); listView1?.Refresh(); })); // repaint DLV grey
            bgw.DoWork += (_, args) =>
            {
                this.Invoke((Action)(() =>
                {
                    toolStripProgressBar1.Visible = true;
                    toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
                    toolStripProgressBar1.MarqueeAnimationSpeed = 30;
                }));
                foreach (var targ_path in nodeList)
                {
                    if (_extractionStopRequested || bgw.CancellationPending)
                    {
                        KillProcess("orbis-pub-cmd");
                        args.Cancel = true;
                        break;
                    }
                    string in_path = PKG.SelectedPKGFilename;
                    string out_path = "";
                    bool isDirectory = targ_path.EndsWith("/") || targ_path.EndsWith("\\");

                    if (isDirectory)
                    {
                        if (preserveStructure)
                        {
                            string dirPath = targ_path.TrimEnd('/').Replace("/", @"\");
                            out_path = $@"{extractLocation}\{dirPath}";
                        }
                        else
                        {
                            string dirName = Path.GetFileName(targ_path.TrimEnd('/').Replace("/", @"\"));
                            out_path = $@"{extractLocation}\{dirName}";
                        }
                        Tool.CreateDirectoryIfNotExists(out_path);
                    }
                    else
                    {
                        bool isFileWithoutExtension = !Path.HasExtension(targ_path);
                        if (isFileWithoutExtension)
                        {
                            string normPath = targ_path.Replace("/", @"\");
                            string itemName = Path.GetFileName(normPath);
                            if (preserveStructure)
                            {
                                string itemRelativeDir = Path.GetDirectoryName(normPath);
                                out_path = string.IsNullOrEmpty(itemRelativeDir)
                                    ? $@"{extractLocation}\{itemName}"
                                    : $@"{extractLocation}\{itemRelativeDir}\{itemName}";
                            }
                            else
                            {
                                out_path = $@"{extractLocation}\{itemName}";
                            }
                        }
                        else
                        {
                            string itemName = Path.GetFileName(targ_path.Replace("/", @"\"));
                            if (preserveStructure)
                            {
                                string itemRelativeDir = Path.GetDirectoryName(targ_path.Replace("/", @"\"));
                                out_path = string.IsNullOrEmpty(itemRelativeDir)
                                    ? $@"{extractLocation}\{itemName}"
                                    : $@"{extractLocation}\{itemRelativeDir}\{itemName}";
                            }
                            else
                            {
                                out_path = $@"{extractLocation}\{itemName}";
                            }
                        }
                    }

                    // Ensure parent output directory exists
                    string outDir = Path.GetDirectoryName(out_path);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    Logger.LogInformation($"Extracting {targ_path} ({in_path})..");
                    toolStripStatusLabel2.Text = $"Extracting {targ_path} ({in_path})..";

                    // Temp rename PKG for Unicode-safe orbis-pub-cmd path
                    string renameDir = GetOrbisTempDirFor(in_path);
                    string renameTmp = Path.Combine(renameDir, "ps4pkgtool_orbis_" + Guid.NewGuid().ToString("N") + ".pkg");
                    bool wasRenamed = false;
                    File.Move(in_path, renameTmp);
                    wasRenamed = true;
                    string safeIn = renameTmp;

                    // Create ASCII-safe temp output path (orbis-pub-cmd garbles non-ANSI paths)
                    // Short temp root - see ExtractFullPKG: deep AppData paths + long PKG paths exceed MAX_PATH.
                    string tempBase = CreateOrbisTempDir("x");
                    Directory.CreateDirectory(tempBase);
                    string tempOutPath = isDirectory
                        ? tempBase
                        : Path.Combine(tempBase, Path.GetFileName(out_path));

                    var extractStartInfo = new ProcessStartInfo
                    {
                        FileName = AppDataDirectory + "orbis-pub-cmd.exe",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    extractStartInfo.ArgumentList.Add("img_extract");
                    extractStartInfo.ArgumentList.Add("--passcode");
                    extractStartInfo.ArgumentList.Add(DefaultOrbisPasscode);
                    extractStartInfo.ArgumentList.Add(safeIn + ":" + targ_path);
                    extractStartInfo.ArgumentList.Add(tempOutPath.Replace(@"/", @"\"));
                    using Process extract = new Process { StartInfo = extractStartInfo };
                    extract.Start();
                    Task<string> extractReadTask = extract.StandardOutput.ReadToEndAsync();
                    if (!extract.WaitForExit(600000))
                    {
                        try { extract.Kill(); extract.WaitForExit(); } catch { }
                    }
                    string extractOutput = extractReadTask.Result;

                    try
                    {
                        int exitCode = extract.ExitCode;
                        // exit -1 = killed process (Stop button or timeout) - see ExtractFullPKG.
                        if (exitCode == -1 || _extractionStopRequested || bgw.CancellationPending)
                        {
                            // User stopped - the killed process returns a non-zero exit code; not a real failure.
                            // The outer loop's cancellation check will set args.Cancel and the completion shows "Extraction cancelled."
                        }
                        else if (exitCode == 0)
                        {
                            // Move extracted files from ASCII temp dir to actual output path
                            if (isDirectory)
                            {
                                if (Directory.Exists(tempOutPath))
                                {
                                    foreach (string entry in Directory.GetFileSystemEntries(tempOutPath))
                                    {
                                        string dest = Path.Combine(out_path, Path.GetFileName(entry));
                                        try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch (Exception ex) { Logger.LogWarning("Failed to delete dest dir: " + ex.Message); }
                                        if (Directory.Exists(entry))
                                            SafeMoveDirectory(entry, dest);
                                        else
                                        {
                                            try { if (File.Exists(dest)) File.Delete(dest); } catch (Exception ex) { Logger.LogWarning("Failed to delete dest file: " + ex.Message); }
                                            File.Move(entry, dest);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (File.Exists(tempOutPath))
                                {
                                    string destDir = Path.GetDirectoryName(out_path);
                                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                                        Directory.CreateDirectory(destDir);
                                    try { if (File.Exists(out_path)) File.Delete(out_path); } catch (Exception ex) { Logger.LogWarning("Failed to delete output file: " + ex.Message); }
                                    File.Move(tempOutPath, out_path);
                                }
                            }
                            Logger.LogInformation($"File extracted to \"{out_path}\"");
                        }
                        else
                        {
                            string errMsg = FormatOrbisError(extractOutput);
                            Logger.LogError($"orbis-pub-cmd failed for \"{targ_path}\": exit={exitCode}, cancelPending={bgw.CancellationPending}\n{errMsg}");
                            this.Invoke(() => ShowError($"orbis-pub-cmd error:\n{errMsg}", true));
                        }
                    }
                    finally
                    {
                        if (wasRenamed && File.Exists(renameTmp))
                        {
                            try
                            {
                                if (File.Exists(in_path))
                                    Logger.LogError("Failed to restore PKG filename because the original path already exists. Recover the PKG from: " + renameTmp);
                                else
                                    File.Move(renameTmp, in_path);
                            }
                            catch (Exception rex)
                            {
                                Logger.LogInformation($"CRITICAL: Failed to restore PKG! File at: {renameTmp}");
                                Logger.LogError($"Rename-back failed: {renameTmp} → {in_path}: {rex.Message}");
                            }
                        }
                        DeleteOrbisTempDir(renameTmp);
                    }

                    // Clean up temp dir
                    try { if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true); } catch (Exception ex) { Logger.LogWarning("Failed to clean temp base dir: " + ex.Message); }
                }
            };
            bgw.RunWorkerCompleted += delegate (object s, RunWorkerCompletedEventArgs e)
            {
                _selectedExtractWorker = null;
                SetExtractionUiEnabled(true); // unlock the app (covers cancel/error/success)
                this.Invoke((Action)(() =>
                {
                    btnExtractFullPKG.Text = "Extract full PKG";
                    toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                    toolStripProgressBar1.Value = 0;
                }));
                toolStripStatusLabel2.Text = $"...";
                if (e.Cancelled)
                {
                    ShowInformation("Extraction cancelled.", true);
                }
                else if (e.Error != null)
                {
                    Logger.LogError($"Extraction failed: {e.Error.Message}");
                    ShowError($"Extraction failed:\n{e.Error.Message}", true);
                }
                else
                {
                    Logger.LogInformation($"Extraction complete: {nodeList.Count} item(s)");
                    ShowInformation($"Extraction complete: {nodeList.Count} item(s) extracted.", false);
                }
            };
            _extractionStopRequested = false; // new extraction starts clean
            bgw.RunWorkerAsync();
        }

        /// <summary>
        /// Synchronous version of ExtractSelectedPKGData for drag-drop.
        /// orbis-pub-cmd needs the target directory to exist, so we pre-create it.
        /// </summary>
        private void ExtractFilesSync(List<string> nodeList, string extractLocation, bool preserveStructure)
        {
            string inPath = PKG.SelectedPKGFilename;
            string renameDir = GetOrbisTempDirFor(inPath);
            string renameTmp = Path.Combine(renameDir, "ps4pkgtool_orbis_" + Guid.NewGuid().ToString("N") + ".pkg");
            bool wasRenamed = false;
            File.Move(inPath, renameTmp);
            wasRenamed = true;
            string safeIn = renameTmp;

            try
            {
                foreach (string targ_path in nodeList)
                {
                    bool isDirectory = targ_path.EndsWith("/") || targ_path.EndsWith("\\");

                    string out_path = preserveStructure
                        ? Path.Combine(extractLocation, targ_path.TrimEnd('/').Replace("/", @"\"))
                        : Path.Combine(extractLocation, Path.GetFileName(targ_path.TrimEnd('/')));

                    // Pre-create output directory/file path
                    if (isDirectory)
                        Directory.CreateDirectory(out_path);
                    else
                        Directory.CreateDirectory(Path.GetDirectoryName(out_path) ?? extractLocation);

                    string arcPath = isDirectory ? targ_path : targ_path.TrimEnd('/');
                    var extractStartInfo = new ProcessStartInfo
                    {
                        FileName = AppDataDirectory + "orbis-pub-cmd.exe",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    extractStartInfo.ArgumentList.Add("img_extract");
                    extractStartInfo.ArgumentList.Add("--passcode");
                    extractStartInfo.ArgumentList.Add(DefaultOrbisPasscode);
                    extractStartInfo.ArgumentList.Add(safeIn + ":" + arcPath);
                    extractStartInfo.ArgumentList.Add(out_path);
                    using var proc = new Process { StartInfo = extractStartInfo };
                    proc.Start();
                    Task<string> procReadTask = proc.StandardOutput.ReadToEndAsync();
                    if (!proc.WaitForExit(600000))
                    {
                        try { proc.Kill(); proc.WaitForExit(); } catch { }
                    }
                    _ = procReadTask.Result; // drain so a stalled process cannot deadlock the pipe
                }
            }
            finally
            {
                if (wasRenamed && File.Exists(renameTmp))
                {
                    try
                    {
                        if (File.Exists(inPath))
                            Logger.LogError("Failed to restore PKG filename because the original path already exists. Recover the PKG from: " + renameTmp);
                        else
                            File.Move(renameTmp, inPath);
                    }
                    catch (Exception rex)
                    {
                        Logger.LogInformation($"CRITICAL: Failed to restore PKG! File at: {renameTmp}");
                        Logger.LogError($"Rename-back failed: {renameTmp} → {inPath}: {rex.Message}");
                    }
                }
                DeleteOrbisTempDir(renameTmp);
            }
        }

        private void PKGTreeView_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var node = PKGTreeView.GetNodeAt(e.Location);
                if (node != null) PKGTreeView.SelectedNode = node;
                contextMenuExtractNode.Show(PKGTreeView, e.Location);
            }
        }

        private void PKGTreeView_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (e.Item is not TreeNode node) return;
            if (!CheckOrbisPubCmdExists()) return;

            if (node.Nodes.Count > 0) return; // directories disabled
            var nodeList = new List<string> { node.FullPath };

            // Short temp root - see ExtractFullPKG: deep AppData paths + long PKG paths exceed MAX_PATH.
            string dragDir = CreateOrbisTempDir("d");

            Cursor.Current = Cursors.WaitCursor;
            try
            {
                ExtractFilesSync(nodeList, dragDir, preserveStructure: true);

                var extractedFiles = Directory.GetFiles(dragDir, "*", SearchOption.AllDirectories).ToList();
                if (extractedFiles.Count == 0) return;

                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, extractedFiles.ToArray());
                DoDragDrop(data, DragDropEffects.Copy);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                try { Directory.Delete(dragDir, true); } catch (Exception ex) { Logger.LogWarning("Failed to clean drag temp dir: " + ex.Message); }
            }
        }

        private void listView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (!CheckOrbisPubCmdExists()) return;

            // Collect paths same as CtxExtractFolder_Click
            var nodeList = new List<string>();
            foreach (ListViewItem item in listView1.SelectedItems)
            {
                if (item.Text == "...") continue;
                if (item.Tag is not TreeNodeInfo info || info.Node == null) continue;
                bool isDir = info.Node.Nodes.Count > 0;
                if (isDir) continue; // drag-drop for directories disabled
                nodeList.Add(info.Node.FullPath);
            }
            if (nodeList.Count == 0) return;

            // Short temp root - see ExtractFullPKG: deep AppData paths + long PKG paths exceed MAX_PATH.
            string dragDir = CreateOrbisTempDir("d");

            Cursor.Current = Cursors.WaitCursor;
            try
            {
                ExtractFilesSync(nodeList, dragDir, preserveStructure: true);

                var extractedFiles = Directory.GetFiles(dragDir, "*", SearchOption.AllDirectories).ToList();
                if (extractedFiles.Count == 0) return;

                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, extractedFiles.ToArray());
                DoDragDrop(data, DragDropEffects.Copy);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                try { Directory.Delete(dragDir, true); } catch (Exception ex) { Logger.LogWarning("Failed to clean drag temp dir: " + ex.Message); }
            }
        }

        private void PKGTreeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            Helper.TreeView.Nodename = e.Node.Text;
        }

        private void darkDataGridView4_SelectionChanged(object sender, EventArgs e)
        {
            this.darkDataGridView4.ClearSelection();
        }

        private string ToTitleCase(string str)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str.ToLower());
        }

        private void DisableTabPages(Control con, string name)
        {
            foreach (Control tab in flatTabControl1.TabPages)
            {
                if (tab.Name != name)
                {
                    tab.Enabled = false;
                }
            }
        }

        private void EnableTabPages(Control con)
        {
            foreach (Control tab in flatTabControl1.TabPages)
            {
                tab.Enabled = true;
            }
        }

        private void DisableControls(Control con)
        {
            if (con != null)
            {
                con.Enabled = false;
            }
        }

        private void EnableControls(Control con)
        {
            if (con != null)
            {
                con.Enabled = true;
            }
        }

        private void btnViewPKGData_Click(object sender, EventArgs e)
        {
            if (!CheckOrbisPubCmdExists())
                return;
            tbPasscode.Text = DefaultOrbisPasscode;
            PKG.Passcode = DefaultOrbisPasscode;

            Logger.LogInformation($"View PKG files: {Path.GetFileName(PKG.SelectedPKGFilename)}");
            // Clear the nodes of the PKGTreeView control + filter state
            ClearFileBrowser();
            toolStripStatusLabel2.Text = "Listing PKG files...";
            toolStripProgressBar1.Visible = true;
            toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
            // Populate PKG data to the tree view
            PopulatePKGDataToTreeView();
        }

        /// <summary>
        /// Returns a writable directory on the same drive for the orbis temp rename.
        /// If the PKG's own directory is already ASCII, it's used (in-place rename).
        /// Otherwise walks up to the nearest ASCII-named ancestor and creates a
        /// short temp dir there - the drive root is NOT used (not writable without
        /// admin on C:).
        /// </summary>
        private static string GetOrbisTempDirFor(string pkgPath)
        {
            string dir = Path.GetDirectoryName(pkgPath);
            if (!string.IsNullOrEmpty(dir) && dir.All(c => c < 128))
                return dir;

            while (!string.IsNullOrEmpty(dir) && !dir.All(c => c < 128))
                dir = Path.GetDirectoryName(dir);

            if (string.IsNullOrEmpty(dir))
                dir = Path.GetTempPath(); // fallback - rare, all-ancestors-Unicode

            string temp = Path.Combine(dir, "p4t_v_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(temp);
            return temp;
        }

        /// <summary>
        /// Deletes the temp dir created by GetOrbisTempDirFor (only the "p4t_v_" ones).
        /// No-op when the file was renamed in place (its parent is a real directory).
        /// </summary>
        private static void DeleteOrbisTempDir(string tempFilePath)
        {
            try
            {
                string d = Path.GetDirectoryName(tempFilePath);
                if (!string.IsNullOrEmpty(d) && Path.GetFileName(d).StartsWith("p4t_v_"))
                    Directory.Delete(d, true);
            }
            catch { }
        }

        /// <summary>
        /// Exports all decodeable textures of the currently previewed Unity .assets
        /// entry as PNGs. Re-extracts the entry + .resS companion to a temp dir,
        /// runs the framework exporter, reports the count.
        /// </summary>
        private void btnExportTextures_Click(object sender, EventArgs e)
        {
            string entryPath = _previewEntryPath ?? "";
            if (string.IsNullOrEmpty(entryPath) || string.IsNullOrEmpty(PKG.SelectedPKGFilename))
            {
                ShowWarning("No PKG entry to export from.", false);
                return;
            }

            using var fbd = new FolderBrowserDialog { Description = "Choose the folder for the exported texture PNGs" };
            if (fbd.ShowDialog() != DialogResult.OK) return;
            string outDir = fbd.SelectedPath;

            var bg = new BackgroundWorker();
            bg.DoWork += (_, _) =>
            {
                string tempDir = CreateOrbisTempDir("e");
                try
                {
                    string extracted = ExtractSingleEntryForPreview(PKG.SelectedPKGFilename, entryPath, tempDir);
                    if (string.IsNullOrEmpty(extracted)) return;

                    string companionEntry = entryPath + ".resS";
                    string companionFile = Path.Combine(tempDir, Path.GetFileName(companionEntry));
                    if (!File.Exists(companionFile))
                        ExtractSingleEntryForPreview(PKG.SelectedPKGFilename, companionEntry, tempDir);

                    var source = new Assets.IO.FileAssetSource(extracted, "PKG entry",
                        rel => File.Exists(Path.Combine(tempDir, Path.GetFileName(rel)))
                            ? new Assets.IO.FileAssetSource(Path.Combine(tempDir, Path.GetFileName(rel)), "Unity .resS stream")
                            : null);
                    var detection = _assetService.Detect(source);
                    if (detection == null) return;
                    var descriptor = _assetService.InspectAsync(source, detection).GetAwaiter().GetResult();

                    int before = Directory.GetFiles(outDir, "*.png").Length;
                    new Assets.Unity.UnityTextureExporter()
                        .ExportConvertedAsync(source, descriptor, outDir, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    int count = Directory.GetFiles(outDir, "*.png").Length - before;

                    this.Invoke((MethodInvoker)delegate
                    {
                        ShowInformation($"Exported {count} texture PNG(s) to {outDir}.", true);
                    });
                }
                catch (Exception ex)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        ShowError("Texture export failed: " + ex.Message, false);
                    });
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            };
            bg.RunWorkerCompleted += (_, _) =>
            {
                toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                toolStripProgressBar1.Visible = false;
                toolStripStatusLabel2.Text = "...";
                this.Enabled = true;
            };
            this.Enabled = false;
            toolStripStatusLabel2.Text = "Exporting textures...";
            toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
            toolStripProgressBar1.Visible = true;
            bg.RunWorkerAsync();
        }

        private void btnExportTreeView_Click(object sender, EventArgs e)
        {
            if (PKGTreeView.Nodes.Count == 0) { ShowError("No data to export.", false); return; }
            using var sfd = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = Path.GetFileNameWithoutExtension(PKG.SelectedPKGFilename ?? "export") + "_file_list.txt"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                // Root level: just the folder names, then their content
                foreach (TreeNode root in PKGTreeView.Nodes)
                {
                    sb.AppendLine(root.Text + "/");
                    WriteTree(sb, root, "", true);
                }
                File.WriteAllText(sfd.FileName, sb.ToString());
                ShowInformation("Tree exported.", true);
            }
            catch (Exception ex) { ShowError("Export failed: " + ex.Message, false); }
        }

        /// <summary>
        /// Writes a folder's children using box-drawing tree characters.
        /// Directories get a trailing "/"; files are plain. Example:
        /// ├── sce_sys/
        /// │   └── param.sfo
        /// └── eboot.bin
        /// </summary>
        private static void WriteTree(System.Text.StringBuilder sb, TreeNode folder, string indent, bool isLast)
        {
            var children = folder.Nodes.Cast<TreeNode>()
                .OrderBy(n => n.Nodes.Count > 0 ? 0 : 1)   // dirs first
                .ThenBy(n => n.Text, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                bool last = i == children.Count - 1;
                sb.Append(indent).Append(last ? "└── " : "├── ")
                  .Append(child.Text).Append(child.Nodes.Count > 0 ? "/" : "").AppendLine();
                WriteTree(sb, child, indent + (last ? "    " : "│   "), last);
            }
        }

        private void listView1_SizeChanged(object sender, EventArgs e)
        {
            BalanceListViewColumns();
        }

        /// <summary>
        /// Splits the list view width equally across columns, leaving room for
        /// the vertical scrollbar so the last column (Size) is never clipped.
        /// </summary>
        private void BalanceListViewColumns()
        {
            if (listView1.Columns.Count == 0 || listView1.Width <= 0) return;
            int avail = listView1.Width - SystemInformation.VerticalScrollBarWidth;
            int per = avail / listView1.Columns.Count;
            for (int i = 0; i < listView1.Columns.Count; i++)
                listView1.Columns[i].Width = per;
            // last column absorbs the remainder
            listView1.Columns[listView1.Columns.Count - 1].Width += avail - per * listView1.Columns.Count;
        }

        private void listView1_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            e.Cancel = true;
            e.NewWidth = listView1.Columns[e.ColumnIndex].Width;
        }

        private void toolStripMenuItem32_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
            {
                if (!CheckOrbisPubCmdExists())
                    return;

                if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
                {
                    List<string> nodeList = new List<string>();
                    foreach (ListViewItem item in listView1.SelectedItems)
                    {
                        if (item.Tag is TreeNodeInfo info && info.Path != "...")
                        {
                            string path = info.Node?.FullPath ?? info.Path;
                            bool isDir = info.Node != null && info.Node.Nodes.Count > 0;
                            nodeList.Add(isDir ? path + "/" : path);
                        }
                    }

                    if (nodeList.Count > 0)
                    {
                        string extractLocation = fbd.SelectedPath;
                        ExtractSelectedPKGData(nodeList, extractLocation);
                    }
                }
            }
        }

        private void listView1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ListViewItem clickedItem = listView1.GetItemAt(e.X, e.Y);

                // Check if the clicked item is not null and its text value is not "..."
                if (clickedItem != null && clickedItem.Text != "...")
                {
                    contextMenuExtractListView.Show(listView1, e.Location);
                }
            }
        }

        private void ViewUpdateChangelog()
        {
            string origPath = PKG.SelectedPKGFilename;
            string renameDir = GetOrbisTempDirFor(origPath);
            string renameTmp = Path.Combine(renameDir, "ps4pkgtool_orbis_" + Guid.NewGuid().ToString("N") + ".pkg");
            bool renamed = false;
            string safePath = renameTmp;
            string tempDir = null;

            try
            {
                File.Move(origPath, renameTmp);
                renamed = true;
                tempDir = CreateOrbisTempDir("c"); // short ASCII temp root (see CreateOrbisTempDir)
                string orbisPubCmdErrorMessage = "";
                var extractStartInfo = new ProcessStartInfo
                {
                    FileName = AppDataDirectory + "orbis-pub-cmd.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                extractStartInfo.ArgumentList.Add("img_extract");
                extractStartInfo.ArgumentList.Add("--passcode");
                extractStartInfo.ArgumentList.Add(DefaultOrbisPasscode);
                extractStartInfo.ArgumentList.Add(safePath + ":Sc0/changeinfo/changeinfo.xml");
                extractStartInfo.ArgumentList.Add(tempDir);
                using Process extract = new Process { StartInfo = extractStartInfo };

                var orbisErrBuilder = new System.Text.StringBuilder();
                extract.ErrorDataReceived += (_, ev) => { if (ev.Data != null) orbisErrBuilder.AppendLine(ev.Data); };
                extract.Start();
                extract.BeginErrorReadLine();
                Task<string> extractStdoutTask = extract.StandardOutput.ReadToEndAsync();
                if (!extract.WaitForExit(600000))
                {
                    try { extract.Kill(); extract.WaitForExit(); } catch { }
                }
                string extractStdout = extractStdoutTask.Result;

                foreach (string line in extractStdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("[Error]"))
                    {
                        orbisPubCmdErrorMessage = line;
                        break;
                    }
                }
                // If no error on stdout, check stderr
                if (string.IsNullOrEmpty(orbisPubCmdErrorMessage))
                    orbisPubCmdErrorMessage = orbisErrBuilder.ToString().Trim();

                if (orbisPubCmdErrorMessage == "[Error]\tCould not find file or directory. (Sc0/changeinfo/changeinfo.xml)")
                {
                    ShowInformation("Change info not available.", true);
                    return;
                }
                else if (orbisPubCmdErrorMessage != "")
                {
                    ShowError($"orbis-pub-cmd error:\n{FormatOrbisError(orbisPubCmdErrorMessage)}", true);
                    return;
                }

                // Move the extracted changeinfo.xml into AppData where the viewer expects it
                string foundChangeInfo = Directory.EnumerateFiles(tempDir, "changeinfo.xml", SearchOption.AllDirectories).FirstOrDefault();
                if (foundChangeInfo != null)
                    File.Copy(foundChangeInfo, AppDataDirectory + "changeinfo.xml", true);
            }
            catch (Exception ex)
            {
                ShowError("An error occurred while viewing the update changelog: " + ex.Message, true);
            }
            finally
            {
                if (renamed && File.Exists(renameTmp))
                {
                    try
                    {
                        if (File.Exists(origPath))
                            Logger.LogError("Failed to restore PKG filename because the original path already exists. Recover the PKG from: " + renameTmp);
                        else
                            File.Move(renameTmp, origPath);
                    }
                    catch (Exception rex)
                    {
                        Logger.LogInformation($"CRITICAL: Failed to restore PKG! File at: {renameTmp}");
                        Logger.LogError($"Rename-back failed: {renameTmp} → {origPath}: {rex.Message}");
                    }
                }
                DeleteOrbisTempDir(renameTmp);
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private void toolStripMenuItem34_Click(object sender, EventArgs e)
        {
            OpenProgramSettings();
        }

        private void ViewPatchChangelog_Click(object sender, EventArgs e)
        {
            Logger.LogInformation("Viewing patch PKG changelog..");

            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            if (clickedMenuItem == viewPkgChangeInfotoolStripMenuItem1 || clickedMenuItem == viewPkgChangeInfotoolStripMenuItem2)
            {
                if (!CheckOrbisPubCmdExists())
                    return;

                ViewUpdateChangelog();

                string changeInfoFile = AppDataDirectory + "changeinfo.xml";
                if (File.Exists(changeInfoFile))
                {
                    try
                    {
                        string changeInfoData = File.ReadAllText(changeInfoFile);
                        File.Delete(changeInfoFile);
                        using (PKGChangeInfoViewer updateChangelog = new PKGChangeInfoViewer(changeInfoData))
                        {
                            updateChangelog.ShowDialog();
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowError("An error occurred while viewing the update changelog: " + ex.Message, true);
                    }
                }
            }
        }

        private bool CheckOrbisPubCmdExists()
        {
            if (!File.Exists(OrbisPubCmd))
            {
                ShowError($"Missing {Path.GetFileName(OrbisPubCmd)} in AppData.", true);
                return false;
            }

            return true;
        }

        private TreeNode SearchFileInTreeView(string p_sSearchTerm, TreeNodeCollection p_Nodes)
        {
            foreach (TreeNode node in p_Nodes)
            {
                if (node.Name == p_sSearchTerm) // Use the 'Name' property for comparison
                    return node;

                if (node.Nodes.Count > 0)
                {
                    TreeNode child = SearchFileInTreeView(p_sSearchTerm, node.Nodes);
                    if (child != null)
                        return child;
                }
            }

            return null;
        }

        private void expandAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PKGTreeView.ExpandAll();
        }

        private void SearchFileInTreeView_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(tbSearchTreeView.Text))
                return;

            TreeNode selectedNode = SearchFileInTreeView(tbSearchTreeView.Text, PKGTreeView.Nodes);
            if (selectedNode != null)
            {
                PKGTreeView.SelectedNode = selectedNode;
                PKGTreeView.Focus();
            }
            else
            {
                // Handle case when no matching node is found
                // For example, display a message to the user
            }
        }

        private void collapseAllNodeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PKGTreeView.CollapseAll();
        }

        private void CtxExpandNode_Click(object sender, EventArgs e)
        {
            if (PKGTreeView.SelectedNode != null)
                PKGTreeView.SelectedNode.Expand();
        }

        private void CtxCollapseNode_Click(object sender, EventArgs e)
        {
            if (PKGTreeView.SelectedNode != null)
                PKGTreeView.SelectedNode.Collapse();
        }

        private void CtxExtractFolder_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0) return;
            if (!CheckOrbisPubCmdExists()) return;
            if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
            {
                List<string> nodeList = new List<string>();
                foreach (ListViewItem item in listView1.SelectedItems)
                {
                    if (item.Text == "...") continue;
                    if (item.Tag is not TreeNodeInfo info || info.Node == null) continue;
                    bool isDir = info.Node.Nodes.Count > 0;
                    nodeList.Add(isDir ? info.Node.FullPath + "/" : info.Node.FullPath);
                }
                ExtractSelectedPKGData(nodeList, fbd.SelectedPath, preserveStructure: true);
            }
        }

        private void CtxCopyPath_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0) return;
            var paths = listView1.SelectedItems.Cast<ListViewItem>()
                .Select(i => ((TreeNodeInfo)i.Tag).Path).Where(p => p != "...");
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
        }

        private void CtxCopyName_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0) return;
            var names = listView1.SelectedItems.Cast<ListViewItem>()
                .Select(i => i.Text).Where(n => n != "...");
            Clipboard.SetText(string.Join(Environment.NewLine, names));
        }

        private void RenamePKG(string namingFormat, List<string> pkgList)
        {
            var bg = new BackgroundWorker();
            bg.DoWork += delegate
            {
                int countPkg = 0;
                int lastInvoked = 0;
                this.Invoke((Action)(() => this.Enabled = false));
                PKG.pkgCount = 0;
                this.Invoke((Action)(() => toolStripProgressBar1.Maximum = pkgList.Count));
                PKG.CountFailRename = 0;
                PKG.ListFailRename = "";
                Logger.LogInformation($"Rename: {pkgList.Count} PKG(s) to {namingFormat} format");
                Logger.LogInformation($"Renaming PKG file to {namingFormat} format..");
                foreach (var pkg in pkgList)
                {
                    string targetPkgName = null;
                    try
                    {
                        string destinationFolder = Path.GetDirectoryName(pkg) + @"\";
                        string newPkgName = "";
                        string sourcePkg = "";
                        string targetPkg = "";
                        (newPkgName, sourcePkg, targetPkg) = PS4_Tools.PKG.SceneRelated.GetNewPKGName(pkg, destinationFolder, namingFormat);
                        targetPkgName = Path.GetFileName(targetPkg);
                        UpdatePKGFilename(newPkgName, sourcePkg, targetPkg);
                        countPkg++;

                        if (countPkg % 10 == 0 || countPkg == pkgList.Count)
                        {
                            var current = countPkg;
                            var increment = countPkg - lastInvoked;
                            lastInvoked = countPkg;
                            PKGGridView.Invoke((Action)(() =>
                            {
                                toolStripStatusLabel2.Text = $"Renaming PKG.. ({current}/{pkgList.Count})";
                                toolStripProgressBar1.Increment(increment);
                            }));
                        }
                    }
                    catch (Exception a)
                    {
                        PKG.CountFailRename++;
                        PKG.ListFailRename += $"{Path.GetFileName(pkg)} → {targetPkgName ?? "?"} : {a.Message}\n";
                    }
                }
            };
            bg.RunWorkerCompleted += delegate
            {
                try
                {
                    if (PKG.CountFailRename > 0)
                    {
                        Logger.LogInformation($"Rename completed with {PKG.CountFailRename} failure(s).");
                        ShowWarning(PKG.CountFailRename + " PKG failed to rename. See program log to view the errors.", false);
                        Logger.LogWarning(PKG.CountFailRename + " PKG failed to rename:");
                        Logger.LogWarning(PKG.ListFailRename);
                    }
                    else
                    {
                        Logger.LogInformation("Rename completed successfully.");
                        ShowInformation("PKG rename done.", true);
                    }

                    SaveManifestAfterScan();
                    // GLV cells are updated in-place by UpdatePKGFilename -
                    // no full rebuild needed, selection and group state are preserved.
                }
                catch (Exception ex)
                {
                    Logger.LogError("Rename completion failed: " + ex.Message);
                }
                finally
                {
                    PKGGridView.Invoke((Action)(() =>
                    {
                        toolStripStatusLabel2.Text = "...";
                        toolStripProgressBar1.Value = 0;
                        this.Enabled = true;
                    }));
                    SetOperationMenusEnabled(true); // never leave the GLV context menu disabled
                }
            };
            bg.RunWorkerAsync();
        }

        /// <summary>
        /// Determines install-priority order for a PKG type string.
        /// Game (Base) -> Patch (Update) -> Addon -> App -> Other.
        /// </summary>
        private static int GetCategoryPriority(string pkgType)
        {
            return pkgType switch
            {
                "Game" => 0,
                "Patch" => 1,
                _ => 99
            };
        }

        /// <summary>
        /// Renames PKGs grouped by Title ID with sequence prefixes so that
        /// alphabetical sort matches the correct install priority:
        /// Base game -> Updates (sorted by version) -> Addons/DLC.
        /// </summary>
        private void RenamePKGByPriority(List<string> pkgList, BackgroundWorker progressWorker = null)
        {
            Logger.LogInformation($"Rename by priority started: {pkgList.Count} file(s)");
            PKG.CountFailRename = 0;
            PKG.ListFailRename = "";
            int totalRenamed = 0;
            int total = pkgList.Count;
            int processed = 0;

            // Group PKGs by Title ID so we can order each game's files together
            var groups = new Dictionary<string, List<(string path, string title, string appVer, string pkgType)>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var pkg in pkgList)
            {
                processed++;
                progressWorker?.ReportProgress((int)(100.0 * processed / total), $"Reading PKG {processed}/{total}...");
                try
                {
                    var readPkg = PS4_Tools.PKG.SceneRelated.Read_PKG(pkg);
                    string pkgType = readPkg.PKG_Type.ToString();
                    if (pkgType != "Game" && pkgType != "Patch")
                    {
                        Logger.LogInformation($"Priority rename: skipping {Path.GetFileName(pkg)} ({pkgType}).");
                        continue;
                    }
                    string titleId = readPkg.Param.TITLEID;
                    if (string.IsNullOrEmpty(titleId))
                    {
                        titleId = "UNKNOWN_TITLEID";
                        Logger.LogWarning($"PKG has no Title ID, using '{titleId}': {Path.GetFileName(pkg)}");
                    }

                    if (!groups.ContainsKey(titleId))
                        groups[titleId] = new List<(string, string, string, string)>();

                    groups[titleId].Add((pkg, readPkg.PS4_Title, readPkg.Param.APP_VER, readPkg.PKG_Type.ToString()));
                }
                catch (Exception ex)
                {
                    PKG.CountFailRename++;
                    PKG.ListFailRename += Path.GetFileName(pkg) + " : " + ex.Message + "\n";
                    Logger.LogError($"Failed to read PKG for priority rename: {pkg}: {ex.Message}");
                }
            }

            // Process each group sorted by install priority
            int renameDone = 0;
            foreach (var kvp in groups)
            {
                var sorted = kvp.Value
                    .OrderBy(x => GetCategoryPriority(x.pkgType))
                    .ThenBy(x => x.appVer)
                    .ToList();

                for (int i = 0; i < sorted.Count; i++)
                {
                    var (path, title, appVer, pkgType) = sorted[i];
                    renameDone++;
                    progressWorker?.ReportProgress((int)(100.0 * renameDone / total), $"Renaming {renameDone}/{total}...");

                    string targetPkg = null;
                    try
                    {
                        string tag = pkgType == "Game" ? "Base" : "Update";
                        string seq = $"{i:D2}";
                        string sanitizedTitle = title.SanitizeFileName();
                        string newName = $"{sanitizedTitle} [{kvp.Key}] {seq} - {tag}";

                        // Append version number for patches/updates
                        if (pkgType == "Patch" && !string.IsNullOrEmpty(appVer) && appVer != "0")
                            newName += $" v{appVer}";

                        newName += ".pkg";

                        string dir = Path.GetDirectoryName(path);
                        targetPkg = Path.Combine(dir, newName);

                        if (string.Equals(path, targetPkg, StringComparison.OrdinalIgnoreCase))
                            continue; // already has this name

                        UpdatePKGFilename(Path.GetFileNameWithoutExtension(newName), path, targetPkg);
                        totalRenamed++;
                    }
                    catch (Exception ex)
                    {
                        PKG.CountFailRename++;
                        string targetName = targetPkg == null ? "?" : Path.GetFileName(targetPkg);
                        PKG.ListFailRename += $"{Path.GetFileName(path)} → {targetName} : {ex.Message}\n";
                        Logger.LogError($"Failed to rename by priority: {path}: {ex.Message}");
                    }
                }
            }

            if (PKG.CountFailRename > 0)
                Logger.LogWarning($"Priority rename completed with {PKG.CountFailRename} failure(s):\n{PKG.ListFailRename}");
            else
                Logger.LogInformation($"Priority rename completed: {totalRenamed} PKG(s) renamed.");
        }

        private void GetSelectedPKGPath()
        {
            try
            {
                foreach (DataGridViewCell cell in PKGGridView.SelectedCells)
                {
                    int selectedRowIndex = cell.RowIndex;
                    if (selectedRowIndex < 0 || selectedRowIndex >= PKGGridView.Rows.Count) continue;
                    DataGridViewRow selectedRow = PKGGridView.Rows[selectedRowIndex];
                    if (selectedRow.Cells[0].Value == null || selectedRow.Cells[13].Value == null) continue;
                    PKG.SelectedPKGFilename = $"{selectedRow.Cells[13].Value}\\{selectedRow.Cells[0].Value}";
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting PKG path: {ex.Message}");
            }
        }

        private void SelectFirstRowPkg()
        {
            try
            {
                if (PKGGridView.Rows.Count > 0)
                {
                    DataGridViewRow firstRow = PKGGridView.Rows[0];
                    if (firstRow.Cells[0].Value != null && firstRow.Cells[13].Value != null)
                    {
                        string valueColumn0 = firstRow.Cells[0].Value.ToString();
                        string valueColumn12 = firstRow.Cells[13].Value.ToString();
                        PKG.SelectedPKGFilename = Path.Combine(valueColumn12, valueColumn0);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error selecting first row: {ex.Message}");
            }
        }

        private struct PkgMoveInfo
        {
            public string FilePath;
            public string FileName;
            public string Title;
            public string TitleId;
            public string Category;
            public string PkgState;
            public string Region;
        }

        private static PkgMoveInfo GetPkgMoveInfo(DataGridViewRow row)
        {
            var info = new PkgMoveInfo();
            info.FileName = row.Cells[0].Value?.ToString() ?? "";
            info.Title = row.Cells[1].Value?.ToString() ?? "";
            info.TitleId = row.Cells[2].Value?.ToString() ?? "";
            info.Region = GetRegionString(((DataRowView)row.DataBoundItem).Row);
            info.PkgState = row.Cells[7].Value?.ToString() ?? "";
            info.Category = row.Cells[8].Value?.ToString() ?? "";
            string dir = row.Cells[13].Value?.ToString() ?? "";
            info.FilePath = Path.Combine(dir, info.FileName);
            return info;
        }

        private void MovePkg_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            string moveBy;
            if (clickedMenuItem == movePkgTypeToolStripMenuItem1 || clickedMenuItem == movePkgTypeToolStripMenuItem2)
                moveBy = "Type";
            else if (clickedMenuItem == movePkgCategoryToolStripMenuItem1 || clickedMenuItem == movePkgCategoryToolStripMenuItem2)
                moveBy = "Category";
            else if (clickedMenuItem == movePkgRegionToolStripMenuItem1 || clickedMenuItem == movePkgRegionToolStripMenuItem2)
                moveBy = "Region";
            else if (clickedMenuItem == movePkgTitleToolStripMenuItem1 || clickedMenuItem == movePkgTitleToolStripMenuItem2)
                moveBy = "Title";
            else if (clickedMenuItem == movePkgSingleFolderToolStripMenuItem1 || clickedMenuItem == movePkgSingleFolderToolStripMenuItem2)
                moveBy = "flat";
            else if (clickedMenuItem == moveByPkgTitleIdToolStripMenuItem)
                moveBy = "Title Id";
            else
                return;

            int total = PKGGridView.Rows.Count;
            if (total == 0)
            {
                ShowError("No PKG files to move.", false);
                return;
            }

            if (!ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
                return;

            string confirmMessage = moveBy == "flat"
    ? $"Move {total} PKG(s) directly into:\n\n{fbd.SelectedPath}\n\nNo subfolders will be created.\n\nProceed?"
    : moveBy == "Title"
        ? $"Move {total} PKG(s) into subfolders under:\n\n{fbd.SelectedPath}\n\nGrouped by: {moveBy}\n\nA combined \"Base + Update\" main folder (grouped by title) and an \"Addon\" main folder (grouped by Title ID) are created.\n\nProceed?"
        : $"Move {total} PKG(s) into subfolders under:\n\n{fbd.SelectedPath}\n\nGrouped by: {moveBy}\n\nProceed?";
            var result = DialogResultYesNo(confirmMessage);
            if (result != DialogResult.Yes)
                return;

            MovePKG(moveBy, fbd.SelectedPath);
        }

        private void MovePKG(string moveBy, string outputFolder)
        {
            var backgroundWorker = new BackgroundWorker();
            backgroundWorker.DoWork += delegate
            {
                this.Enabled = false;
                PKG.pkgCount = 0;
                PKG.CountFailMove = 0;
                PKG.ListFailMove = "";
                toolStripProgressBar1.Maximum = PKGGridView.Rows.Count;
                int total = PKGGridView.Rows.Count;
                Logger.LogInformation($"Move PKG by {moveBy}: {total} PKG(s) → {outputFolder}");

                for (int i = 0; i < total; i++)
                {
                    try
                    {
                        PKG.pkgCount++;
                        toolStripStatusLabel2.Text = $"Moving PKG.. ({PKG.pkgCount}/{total})";
                        toolStripProgressBar1.Increment(1);

                        var row = PKGGridView.Rows[i];
                        if (row.Cells[0].Value == null) continue;

                        var info = GetPkgMoveInfo(row);
                        string dest = null;

                        switch (moveBy.ToLowerInvariant())
                        {
                            case "category":
                                dest = GetCategoryDest(info, outputFolder);
                                break;
                            case "type":
                                dest = GetTypeDest(info, outputFolder);
                                break;
                            case "region":
                                dest = GetRegionDest(info, outputFolder);
                                break;
                            case "title":
                                dest = GetTitleDest(info, outputFolder);
                                break;
                            case "title id":
                                dest = Path.Combine(outputFolder, info.TitleId);
                                break;
                            case "flat":
                                dest = outputFolder;
                                break;
                        }

                        if (string.IsNullOrEmpty(dest))
                        {
                            PKG.CountFailMove++;
                            string reason = moveBy == "region" ? $"Unknown region: {info.Region}" : $"Unknown {moveBy}";
                            Logger.LogWarning($"Move skipped: {info.FileName} - {reason}");
                            PKG.ListFailMove += $"{info.FileName} : {reason}\n";
                            continue;
                        }

                        Logger.LogInformation($"  {info.FileName} → {dest}");
                        Tool.CreateDirectoryIfNotExists(dest);
                        string newPath = Path.Combine(dest, info.FileName);
                        File.Move(info.FilePath, newPath);
                        CheckAndAddPathToList(dest);
                    }
                    catch (Exception ex)
                    {
                        PKG.CountFailMove++;
                        string name = "";
                        try { name = Path.GetFileNameWithoutExtension(PKGGridView.Rows[i].Cells[0].Value?.ToString() ?? ""); } catch { }
                        Logger.LogError($"Move failed: {name} - {ex.Message}");
                        PKG.ListFailMove += $"{name} : {ex.Message}\n";
                    }
                }
            };

            backgroundWorker.RunWorkerCompleted += delegate
            {
                if (PKG.CountFailMove > 0)
                {
                    Logger.LogWarning($"Move completed with {PKG.CountFailMove} failure(s):");
                    Logger.LogWarning(PKG.ListFailMove);
                    ShowWarning($"{PKG.CountFailMove} PKG failed to move. See program log for details.", false);
                }
                else
                {
                    Logger.LogInformation("Move completed successfully.");
                    ShowInformation("PKG moved to new directories.", true);
                }

                toolStripStatusLabel2.Text = "Refreshing PKG list.. ";
                LoadPKGGridView();
            };

            backgroundWorker.RunWorkerAsync();
        }

        private static string GetCategoryDest(PkgMoveInfo info, string outputFolder)
        {
            switch (info.Category)
            {
                case PKGCategory.GAME: return Path.Combine(outputFolder, "GAME");
                case PKGCategory.PATCH: return Path.Combine(outputFolder, "PATCH");
                case PKGCategory.ADDON: return Path.Combine(outputFolder, "ADDON");
                case PKGCategory.APP: return Path.Combine(outputFolder, "APP");
                default: return null;
            }
        }

        private static string GetTypeDest(PkgMoveInfo info, string outputFolder)
        {
            switch (info.PkgState)
            {
                case "Official": return Path.Combine(outputFolder, "OFFICIAL");
                case "Fake": return Path.Combine(outputFolder, "FAKE");
                case "Addon_Unlocker": return Path.Combine(outputFolder, "ADDON UNLOCKER");
                default: return null;
            }
        }

        private static string GetRegionDest(PkgMoveInfo info, string outputFolder)
        {
            var regionFolders = new Dictionary<string, string>
            {
                { PKGRegion.EU, "EU" },
                { PKGRegion.US, "US" },
                { PKGRegion.JAPAN, "JAPAN" },
                { PKGRegion.HONG_KONG, "HONG KONG" },
                { PKGRegion.ASIA, "ASIA" },
                { PKGRegion.KOREA, "KOREA" }
            };

            if (regionFolders.TryGetValue(info.Region, out string folder))
                return Path.Combine(outputFolder, folder);

            Logger.LogWarning($"Unknown region '{info.Region}' - moving to OTHER");
            return Path.Combine(outputFolder, "OTHER");
        }

        private string GetTitleDest(PkgMoveInfo info, string outputFolder)
        {
            // Games and patches share one main folder (grouped by title inside it);
            // add-ons get their own main folder grouped by Title ID - their only reliable
            // attribute (titles are often repacked/unreliable).
            // Original filenames are preserved, so files never collide within a folder.
            string mainFolder;
            switch (info.Category)
            {
                case PKGCategory.GAME:
                case PKGCategory.PATCH:
                    mainFolder = "Base + Update";
                    break;
                case PKGCategory.ADDON:
                    mainFolder = "Addon";
                    break;
                case PKGCategory.APP:
                    mainFolder = "App";
                    break;
                default: return null; // unknown category - skip
            }

            if (info.Category == PKGCategory.ADDON)
                return Path.Combine(outputFolder, mainFolder, string.IsNullOrEmpty(info.TitleId) ? "UNKNOWN_TITLEID" : info.TitleId);

            string safeTitle = info.Title.SanitizeFileName();
            if (string.IsNullOrEmpty(safeTitle))
                safeTitle = string.IsNullOrEmpty(info.TitleId) ? "UNKNOWN_TITLEID" : info.TitleId;
            return Path.Combine(outputFolder, mainFolder, safeTitle);
        }

        private void CheckAndAddPathToList(string path)
        {
            var match = appSettings_.PkgDirectories.FirstOrDefault(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                appSettings_.PkgDirectories.Add(path);
            }
        }

        private void PKGListGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex != 0)
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            // shadPS4 column: colored dot + status text (column-name based, index-safe)
            if (e.RowIndex >= 0 && e.RowIndex < PKGGridView.Rows.Count
                && PKGGridView.Columns[e.ColumnIndex].Name == "ShadPS4 (Windows)"
                && e.Value != null && e.Value.ToString() != "")
            {
                string status = e.Value.ToString();
                e.Value = "● " + status;
                e.CellStyle.ForeColor = Shadps4Compat.StatusColor(status);
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                return;
            }

            // Apply color label to this specific row only (not the entire grid)
            if (appSettings_.PkgColorLabel && e.RowIndex >= 0 && e.RowIndex < PKGGridView.Rows.Count)
            {
                var row = PKGGridView.Rows[e.RowIndex];
                if (row.Cells[8].Value == null) return;
                string category = row.Cells[8].Value.ToString();
                Color fore, back;
                switch (category)
                {
                    case PKGCategory.PATCH:
                        fore = appSettings_.PatchPkgForeColor;
                        back = appSettings_.PatchPkgBackColor;
                        break;
                    case PKGCategory.GAME:
                        fore = appSettings_.GamePkgForeColor;
                        back = appSettings_.GamePkgBackColor;
                        break;
                    case PKGCategory.ADDON:
                        fore = appSettings_.AddonPkgForeColor;
                        back = appSettings_.AddonPkgBackColor;
                        break;
                    case PKGCategory.APP:
                        fore = appSettings_.AppPkgForeColor;
                        back = appSettings_.AppPkgBackColor;
                        break;
                    default:
                        return;
                }
                e.CellStyle.ForeColor = fore;
                e.CellStyle.BackColor = back;
            }
        }

        private void UpdatePKGColorLabel()
        {
            if (appSettings_.PkgColorLabel)
            {
                foreach (DataGridViewRow row in PKGGridView.Rows)
                {
                    if (row.IsNewRow || row.Cells[8].Value == null) continue;
                    string category = row.Cells[8].Value.ToString();

                    switch (category)
                    {
                        case PKGCategory.PATCH:
                            row.DefaultCellStyle.ForeColor = appSettings_.PatchPkgForeColor;
                            row.DefaultCellStyle.BackColor = appSettings_.PatchPkgBackColor;
                            break;
                        case PKGCategory.GAME:
                            row.DefaultCellStyle.ForeColor = appSettings_.GamePkgForeColor;
                            row.DefaultCellStyle.BackColor = appSettings_.GamePkgBackColor;
                            break;
                        case PKGCategory.ADDON:
                            row.DefaultCellStyle.ForeColor = appSettings_.AddonPkgForeColor;
                            row.DefaultCellStyle.BackColor = appSettings_.AddonPkgBackColor;
                            break;
                        case PKGCategory.APP:
                            row.DefaultCellStyle.ForeColor = appSettings_.AppPkgForeColor;
                            row.DefaultCellStyle.BackColor = appSettings_.AppPkgBackColor;
                            break;
                    }
                }
            }
            else
            {
                foreach (DataGridViewRow row in PKGGridView.Rows)
                {
                    var isOdd = (row.Index % 2 != 0);
                    row.DefaultCellStyle = GetCellStyle(isFocused: false, isOdd, isHeader: false);
                }
            }
        }

        private static DataGridViewCellStyle GetCellStyle(bool isFocused, bool isOdd, bool isHeader)
        {
            return new DataGridViewCellStyle
            {
                BackColor = (isHeader ? Colors.DarkBackground : (isOdd ? Colors.GreyBackground : Colors.HeaderBackground)),
                ForeColor = Colors.LightText,
                SelectionBackColor = ((isFocused && isHeader) ? Colors.DarkBackground : Colors.BlueSelection),
                SelectionForeColor = Colors.LightText
            };
        }

        private void PKGListGridView_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            try
            {
                if (PKGGridView.DataSource is not DataTable dataTable) return;
                if (e.ColumnIndex < 0 || e.ColumnIndex >= PKGGridView.Columns.Count) return;

                string colName = PKGGridView.Columns[e.ColumnIndex].Name;
                if (string.IsNullOrEmpty(colName)) return;

                // Region column (byte[]) needs special handling
                if (colName == "Region")
                {
                    PKGGridView.Columns[e.ColumnIndex].SortMode = DataGridViewColumnSortMode.Automatic;
                    return;
                }

                // Size / System Version / Version - numerical sort
                // Title ID - alphanumeric (CUSA00010 after CUSA00002)
                if (colName == "Size" || colName == "System Version" || colName == "Version [App Version]"
                    || colName == "Title ID")
                {
                    SortOrder numPrev = _colSortDir.GetValueOrDefault(colName, SortOrder.None);
                    SortOrder numNext = numPrev == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
                    _colSortDir[colName] = numNext;
                    Func<DataRow, double> key = colName switch
                    {
                        "Size" => r => ParseSizeToBytes(r["Size"]?.ToString()),
                        "System Version" => r => ParseVersion(r["System Version"]?.ToString() ?? ""),
                        _ => r => ParseAppVersion(r["Version [App Version]"]?.ToString() ?? "")
                    };
                    var sorted = dataTable.Rows.Cast<DataRow>().ToList();
                    if (colName == "Title ID")
                        sorted.Sort((a, b) => NaturalCompare(
                            a["Title ID"]?.ToString() ?? "", b["Title ID"]?.ToString() ?? ""));
                    else
                        sorted = sorted.OrderBy(r => key(r)).ToList();
                    if (numNext == SortOrder.Descending) sorted.Reverse();
                    var newTable = dataTable.Clone();
                    foreach (var r in sorted) newTable.Rows.Add(r.ItemArray);
                    PKGGridView.DataSource = newTable;
                    ScrollToTop();
                    UpdateDataGridViewColumnVisibility();
                    PKGGridView.Columns[e.ColumnIndex].HeaderCell.SortGlyphDirection = numNext;
                    return;
                }

                // All other columns: alphabetical sort with explicit DataSource swap
                SortOrder prev = _colSortDir.GetValueOrDefault(colName, SortOrder.None);
                SortOrder next = prev == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
                _colSortDir[colName] = next;

                var sortedRows = dataTable.Rows.Cast<DataRow>()
                    .OrderBy(r => r[colName]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (next == SortOrder.Descending) sortedRows.Reverse();
                var sortedTable = dataTable.Clone();
                foreach (var r in sortedRows) sortedTable.Rows.Add(r.ItemArray);
                PKGGridView.DataSource = sortedTable;
                ScrollToTop();
                PKGGridView.Columns[e.ColumnIndex].HeaderCell.SortGlyphDirection = next;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error sorting column: {ex.Message}");
                ShowError($"Error sorting column: {ex.Message}", true);
            }
        }

        private void ScrollToTop()
        {
            if (PKGGridView.Rows.Count > 0)
            {
                PKGGridView.Rows[0].Selected = true;
                PKGGridView.CurrentCell = PKGGridView.Rows[0].Cells[0];
                try { PKGGridView.FirstDisplayedScrollingRowIndex = 0; } catch (Exception ex) { Logger.LogWarning("Failed to scroll grid to top: " + ex.Message); }
            }
        }

        private static int NaturalCompare(string a, string b)
        {
            // Alphanumeric: split into text/number chunks, compare segment by segment
            if (a == null) a = ""; if (b == null) b = "";
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
                {
                    // Extract numeric chunks
                    long na = 0, nb = 0;
                    while (ia < a.Length && char.IsDigit(a[ia])) { na = na * 10 + (a[ia] - '0'); ia++; }
                    while (ib < b.Length && char.IsDigit(b[ib])) { nb = nb * 10 + (b[ib] - '0'); ib++; }
                    if (na != nb) return na.CompareTo(nb);
                }
                else
                {
                    if (a[ia] != b[ib]) return a[ia].CompareTo(b[ib]);
                    ia++; ib++;
                }
            }
            return a.Length.CompareTo(b.Length);
        }

        private static double ParseVersion(string ver)
        {
            // "4.50" -> 4.5, "11.00" -> 11.0
            if (string.IsNullOrEmpty(ver) || ver == "NA") return -1;
            if (double.TryParse(ver, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v)) return v;
            return -1;
        }

        private static double ParseAppVersion(string ver)
        {
            // "1.00 [1.00]" -> extract first number, or "NA" -> -1
            if (string.IsNullOrEmpty(ver) || ver == "NA") return -1;
            int space = ver.IndexOf(' ');
            string num = space > 0 ? ver.Substring(0, space) : ver;
            if (double.TryParse(num, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v)) return v;
            return -1;
        }

        private static long ParseSizeToBytes(string sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr)) return 0;
            try
            {
                var parts = sizeStr.Split(' ');
                if (parts.Length != 2) return 0;
                double val = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                string unit = parts[1].ToUpperInvariant();
                return unit switch
                {
                    "B" or "BYTES" => (long)val,
                    "KB" => (long)(val * 1024),
                    "MB" => (long)(val * 1024 * 1024),
                    "GB" => (long)(val * 1024 * 1024 * 1024),
                    "TB" => (long)(val * 1024L * 1024 * 1024 * 1024),
                    _ => 0
                };
            }
            catch (Exception ex) { Logger.LogWarning("Error parsing size string '" + sizeStr + "': " + ex.Message); return 0; }
        }

        private void btnExtractFullPKG_Click(object sender, EventArgs e)
        {
            // If ANY extraction is running, kill it immediately
            bool anyRunning = false;
            if (_extractWorker != null && _extractWorker.IsBusy)
            {
                KillProcess("orbis-pub-cmd");
                Helper.IsOperationRunning = false;
                _extractionStopRequested = true; // volatile - reliable across the worker thread
                _extractWorker.CancelAsync();
                anyRunning = true;
            }
            if (_selectedExtractWorker != null && _selectedExtractWorker.IsBusy)
            {
                KillProcess("orbis-pub-cmd");
                _extractionStopRequested = true; // volatile - reliable across the worker thread
                _selectedExtractWorker.CancelAsync();
                anyRunning = true;
            }
            if (anyRunning)
            {
                toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                toolStripProgressBar1.Value = 0;
                toolStripStatusLabel2.Text = "Extraction cancelled.";
                btnExtractFullPKG.Text = "Extract full PKG";
                return;
            }

            if (!CheckOrbisPubCmdExists())
                return;
            ExtractFullPKG();
        }

        /// <summary>
        /// Generate and return datatable of selected/all PKG from gridview
        /// </summary>
        /// <param name="pKGSelectionType"></param>
        /// <returns></returns>
        private DataTable GenerateDatatableFromSelectedPKG(string pKGSelectionType)
        {
            // Create a new DataTable
            DataTable selectedPKGDatatable = new DataTable();

            // Add columns to the DataTable
            foreach (DataGridViewColumn column in PKGGridView.Columns)
            {
                selectedPKGDatatable.Columns.Add(column.HeaderText);
            }

            // Find Region column by name (not hardcoded index)
            int regionColIdx = -1;
            foreach (DataGridViewColumn col in PKGGridView.Columns)
                if (col.Name == "Region") { regionColIdx = col.Index; break; }

            if (pKGSelectionType == PKGSelectionType.ALL)
            {
                foreach (DataGridViewRow row in PKGGridView.Rows)
                {
                    DataRow dataRow = selectedPKGDatatable.NewRow();
                    for (int i = 0; i < row.Cells.Count; i++)
                    {
                        var cell = row.Cells[i];
                        if (i == regionColIdx && cell.Value is byte[] icon)
                            dataRow[i] = ConvertImageToRegion(icon);
                        else if (cell.Value is byte[])
                            dataRow[i] = "";
                        else
                            dataRow[i] = cell.Value;
                    }
                    selectedPKGDatatable.Rows.Add(dataRow);
                }
            }
            else
            {
                foreach (DataGridViewRow row in PKGGridView.SelectedRows)
                {
                    DataRow dataRow = selectedPKGDatatable.NewRow();
                    for (int i = 0; i < row.Cells.Count; i++)
                    {
                        var cell = row.Cells[i];
                        if (i == regionColIdx && cell.Value is byte[] icon)
                            dataRow[i] = ConvertImageToRegion(icon);
                        else if (cell.Value is byte[])
                            dataRow[i] = "";
                        else
                            dataRow[i] = cell.Value;
                    }
                    selectedPKGDatatable.Rows.Add(dataRow);
                }
            }

            return selectedPKGDatatable;
        }

        public static string ConvertImageToRegion(byte[] regionIcon)
        {
            var imageConverter = new ImageConverter();

            Dictionary<byte[], string> regionMapping = new Dictionary<byte[], string>
    {
        { (byte[])imageConverter.ConvertTo(Properties.Resources.eu, typeof(byte[])), PKGRegion.EU },
        { (byte[])imageConverter.ConvertTo(Properties.Resources.us, typeof(byte[])), PKGRegion.US },
        { (byte[])imageConverter.ConvertTo(Properties.Resources.jp, typeof(byte[])), PKGRegion.JAPAN },
        { (byte[])imageConverter.ConvertTo(Properties.Resources.hk, typeof(byte[])), "HONG KONG" },
        { (byte[])imageConverter.ConvertTo(Properties.Resources.asia, typeof(byte[])), PKGRegion.ASIA },
        { (byte[])imageConverter.ConvertTo(Properties.Resources.kr, typeof(byte[])), PKGRegion.KOREA }
    };

            foreach (var kvp in regionMapping)
            {
                if (Utils.ByteArraysEqual(regionIcon, kvp.Key))
                {
                    return kvp.Value;
                }
            }

            return string.Empty;
        }

        private void TbSearchGame_TextChanged(object sender, EventArgs e)
        {
            try
            {
                var dt = PKGGridView.DataSource as DataTable;
                if (dt == null) return;
                string text = tbSearchGame.SearchText.Replace("'", "''"); // escape single quotes for LIKE
                dt.DefaultView.RowFilter = string.IsNullOrEmpty(text)
                    ? string.Empty
                    : $"[Filename] LIKE '%{text}%' OR [Title] LIKE '%{text}%' OR [Title ID] LIKE '%{text}%' OR [Content ID] LIKE '%{text}%'";
                PopulateGroupedView(); // GLV mirrors the search result set
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error applying filter: {ex.Message}");
                ShowError($"Error applying filter: {ex.Message}", true);
            }
        }

        private static int IconFor(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) return 4; // no extension → binary
            return ext switch
            {
                ".png" or ".jpg" or ".dds" => 2,                          // image
                ".txt" => 1,                                              // document
                ".xml" or ".json" or ".sfo" or ".ini" => 3,              // config
                ".at9" or ".ogg" or ".mp3" or ".wav" => 6,              // audio
                ".mp4" or ".avi" => 9,                                    // video
                ".pkg" => 8,                                              // package
                _ => 4,                                                   // binary (default)
            };
        }

        private void PKGTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            TreeView.currentNode = e.Node;
            PKG.NodeFullPath = currentNode.FullPath;

            // Find all root nodes
            rootNodes = new List<TreeNode>();
            foreach (TreeNode rootNode in PKGTreeView.Nodes)
            {
                rootNodes.Add(rootNode);
            }

            PopulateListView();
        }

        private void listView1_ItemActivate(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0) return;
            var selectedItem = (TreeNodeInfo)listView1.SelectedItems[0].Tag;

            if (selectedItem.Path == "...")
            {
                // Handle navigating to the parent directory or showing both root nodes
                if (currentNode.Parent != null)
                {
                    currentNode = currentNode.Parent;
                    PKG.NodeFullPath = currentNode.FullPath;
                    PopulateListView();
                }
                else if (currentNode != null && !rootNodes.Contains(currentNode))
                {
                    currentNode = null;
                    PKG.NodeFullPath = ""; // You might want to set this to the appropriate default value
                    PopulateListView();
                }
                else
                {
                    currentNode = null;
                    PKG.NodeFullPath = ""; // You might want to set this to the appropriate default value
                    PopulateListView(true);
                }
            }
            else if (selectedItem.Node.Nodes.Count > 0) // Check if the clicked item is a directory
            {
                // Handle clicking on a directory in the ListView
                currentNode = selectedItem.Node;
                PKG.NodeFullPath = currentNode.FullPath;
                PopulateListView();
            }
        }

        private void listView1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            HandleListViewActivation();
        }

        private void HandleListViewActivation()
        {
            if (listView1.SelectedItems.Count > 0)
            {
                var selectedItem = (TreeNodeInfo)listView1.SelectedItems[0].Tag;

                if (selectedItem.Path == "...")
                {
                    // Handle navigating to the parent directory or showing both root nodes
                    if (currentNode.Parent != null)
                    {
                        currentNode = currentNode.Parent;
                        PKG.NodeFullPath = currentNode.FullPath;
                        PopulateListView();
                    }
                    else if (currentNode != null && !rootNodes.Contains(currentNode))
                    {
                        currentNode = null;
                        PKG.NodeFullPath = ""; // You might want to set this to the appropriate default value
                        PopulateListView();
                    }
                    else
                    {
                        currentNode = null;
                        PKG.NodeFullPath = ""; // You might want to set this to the appropriate default value
                        PopulateListView(true);
                    }
                }
                else if (selectedItem.Node.Nodes.Count > 0) // Check if the clicked item is a directory
                {
                    // Handle clicking on a directory in the ListView
                    currentNode = selectedItem.Node;
                    PKG.NodeFullPath = currentNode.FullPath;
                    PopulateListView();
                }
                else
                {
                    // Empty directories also have Nodes.Count == 0 - skip preview for them
                    bool isDirItem = listView1.SelectedItems[0].SubItems.Count > 1
                        && listView1.SelectedItems[0].SubItems[1].Text == "Directory";
                    if (!isDirItem)
                        PreviewEntry(selectedItem.Node.FullPath);
                }
            }
        }

        private string _previewError;

        /// <summary>Builds a classic hex dump (offset + hex + ASCII) for the first maxBytes of a file.</summary>
        private static string BuildHexDump(string path, int maxBytes)
        {
            var sb = new System.Text.StringBuilder();
            byte[] data;
            using (var fs = File.OpenRead(path))
            {
                int len = (int)Math.Min(fs.Length, maxBytes);
                data = new byte[len];
                fs.Read(data, 0, len);
            }
            for (int i = 0; i < data.Length; i += 16)
            {
                sb.Append(i.ToString("X8")).Append("  ");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < data.Length)
                        sb.Append(data[i + j].ToString("X2")).Append(' ');
                    else
                        sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }
                sb.Append(' ');
                for (int j = 0; j < 16 && i + j < data.Length; j++)
                {
                    byte b = data[i + j];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                sb.AppendLine();
            }
            if (data.Length == 0) sb.AppendLine("(empty file)");
            return sb.ToString();
        }

        /// <summary>
        /// Preview a file entry in the viewer pane (text or image).
        /// The entry is extracted to a temp dir, rendered, and cleaned up.
        /// </summary>
        private BackgroundWorker _previewWorker;
        private int _previewVersion;
        private string? _previewEntryPath;   // PKG entry currently previewed (for export)
        private bool _previewIsUnityFile;    // last previewed entry was a Unity serialized file

        // Asset workspace: the extracted container (pak/unity) kept alive while
        // the user browses its children in the asset list.
        private Assets.Abstractions.IAssetSource? _containerSource;
        private Assets.Models.AssetDetectionResult? _containerDetection;
        private string? _containerTempDir;
        private readonly List<Assets.Abstractions.IAssetSource> _containerChildren = new();
        private static readonly Assets.AssetInspectionService _assetService = Assets.GenericAssetRegistryBuilder.Build();

        private Assets.Models.TextureData _previewTexture; // prepared on the worker thread
        private string _previewText;         // prepared on the worker thread
        private string _previewHex;          // prepared on the worker thread
        private string _previewInfo;         // info-bar text
        private string _previewSizeStr;      // "123.45 MB" for the info bar

        private void PreviewEntry(string entryPath)
        {
            // Export applies to the entry about to be previewed.
            _previewEntryPath = entryPath;
            _previewIsUnityFile = false;
            if (btnExportTextures != null) btnExportTextures.Enabled = false;
            CleanupContainerBrowse(); // a new preview releases the previous container
            try
            {
                if (Helper.IsOperationRunning)
                {
                    ShowWarning("Cannot preview while an operation is running.", false);
                    return;
                }
                if (_previewWorker != null && _previewWorker.IsBusy)
                {
                    lblFileViewerInfo.Text = "Preview already in progress...";
                    return;
                }
                if (string.IsNullOrEmpty(PKG.SelectedPKGFilename)) return;

                // Size guard - don't fully extract giant entries just to preview the head.
                // Containers (pak/bundle/psarc) are exempt: the full extraction
                // IS the point, they are browsed rather than previewed.
                string entryExt = Path.GetExtension(entryPath).ToLowerInvariant();
                bool isContainerEntry = entryExt is ".pak" or ".assets" or ".bundle" or ".unity3d" or ".psarc";
                const long maxPreviewBytes = 200L * 1024 * 1024; // 200 MB
                long entrySize = _fileSizes.GetValueOrDefault(entryPath, 0);
                if (!isContainerEntry && entrySize > maxPreviewBytes)
                {
                    string tooLarge = $"{Path.GetFileName(entryPath)} File too large to preview ({Helper.RoundBytes(entrySize)})";
                    lblFileViewerInfo.Text = tooLarge;
                    ShowWarning(tooLarge, false);
                    return;
                }

                string fname = Path.GetFileName(entryPath);
                _previewSizeStr = Helper.RoundBytes(entrySize);
                _previewTexture = null;
                _previewText = null;
                _previewHex = null;
                _previewInfo = null;

                lblFileViewerInfo.Text = $"Previewing {fname}...";
                toolStripStatusLabel2.Text = $"Previewing {fname}...";
                toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
                toolStripProgressBar1.Visible = true;

                int version = Interlocked.Increment(ref _previewVersion);
                var bg = new BackgroundWorker();
                _previewWorker = bg;
                bg.DoWork += (_, _) =>
                {
                    try
                    {
                        // Re-check: an extraction may have started while we queued.
                        if (Helper.IsOperationRunning) return;

                        string tempDir = CreateOrbisTempDir("p");
                        string extracted = ExtractSingleEntryForPreview(PKG.SelectedPKGFilename, entryPath, tempDir);
                        if (string.IsNullOrEmpty(extracted)) return;

                        // Asset framework: detect -> inspect -> preview/hex. All heavy
                        // decoding happens OFF the UI thread.
                        var source = new Assets.IO.FileAssetSource(extracted, "PKG entry");
                        var detection = _assetService.Detect(source);
                        _previewIsUnityFile = detection?.Format == Assets.Handlers.UnitySerializedFileHandler.FormatId;

                        // Unity .assets files stream texture data to a .resS companion -
                        // extract it into the same temp dir and wire a resolver so
                        // streamed textures decode (PS4 builds stream everything).
                        if (detection?.Format == Assets.Handlers.UnitySerializedFileHandler.FormatId)
                        {
                            string companionEntry = entryPath + ".resS";
                            string companionFile = Path.Combine(tempDir, Path.GetFileName(companionEntry));
                            if (!File.Exists(companionFile))
                                ExtractSingleEntryForPreview(PKG.SelectedPKGFilename, companionEntry, tempDir);
                            if (File.Exists(companionFile))
                            {
                                source = new Assets.IO.FileAssetSource(extracted, "PKG entry",
                                    rel => File.Exists(Path.Combine(tempDir, Path.GetFileName(rel)))
                                        ? new Assets.IO.FileAssetSource(Path.Combine(tempDir, Path.GetFileName(rel)), "Unity .resS stream")
                                        : null);
                            }
                        }
                        if (detection != null)
                        {
                            var descriptor = _assetService.InspectAsync(source, detection).GetAwaiter().GetResult();
                            _previewInfo = BuildPreviewInfo(fname, descriptor);

                            if (descriptor.Capabilities.HasFlag(Assets.Abstractions.AssetCapabilities.Preview))
                            {
                                var preview = _assetService.TryPreviewAsync(source, detection).GetAwaiter().GetResult();
                                if (preview?.Texture != null) _previewTexture = preview.Texture;
                                else if (preview?.Text != null) _previewText = preview.Text;
                                else _previewHex = BuildHexDump(extracted, 1 << 20);
                            }
                            else
                            {
                                _previewHex = BuildHexDump(extracted, 1 << 20);
                            }
                        }
                        else
                        {
                            _previewHex = BuildHexDump(extracted, 1 << 20);
                        }

                        // Containers (pak/unity) stay alive for the asset workspace;
                        // everything else is cleaned up here (worker thread).
                        if (detection != null && _assetService.IsContainer(detection))
                        {
                            _containerSource = source;
                            _containerDetection = detection;
                            _containerTempDir = tempDir;
                        }
                        else
                        {
                            try { if (File.Exists(extracted)) File.Delete(extracted); } catch { }
                            try { string d = Path.GetDirectoryName(extracted); if (!string.IsNullOrEmpty(d) && Path.GetFileName(d).StartsWith("p4t_p_")) Directory.Delete(d, true); } catch { }
                        }
                    }
                    catch (Assets.Errors.UnsupportedAssetException uex)
                    {
                        // e.g. BC7 DDS: metadata + hex fallback, not a hard failure.
                        _previewError = null;
                        _previewInfo = $"{fname} ({_previewSizeStr}) - {uex.Message}";
                    }
                    catch (Exception ex)
                    {
                        _previewError = ex.Message;
                    }
                };
                bg.RunWorkerCompleted += (_, _) =>
                {
                    toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                    toolStripProgressBar1.Visible = false;
                    toolStripStatusLabel2.Text = "...";
                    _previewWorker = null;
                    if (version != _previewVersion) return; // a newer preview started
                    btnExportTextures.Enabled = _previewIsUnityFile; // export applies to Unity .assets entries
                    if (_previewError != null)
                    {
                        ShowError("Preview failed: " + _previewError, false);
                        _previewError = null;
                        return;
                    }
                    try
                    {
                        RenderPreviewResult(fname, _previewSizeStr);
                    }
                    catch (Exception ex)
                    {
                        ShowError("Preview failed: " + ex.Message, false);
                    }
                    // Containers expose their children in the asset workspace list.
                    if (_containerSource != null && _containerDetection != null)
                        PopulateAssetList();
                };
                bg.RunWorkerAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError("Preview error: " + ex.Message);
            }
        }

        /// <summary>Shows the prepared preview (texture/text/hex) in the viewer pane.</summary>
        private void RenderPreviewResult(string fname, string sizeStr)
        {
            assetListView.Visible = false;
            btnAssetBack.Visible = _containerSource != null && _containerDetection != null;
            if (_previewTexture != null)
            {
                picPreview.Visible = true;
                txtPreview.Visible = false;
                txtHexPreview.Visible = false;
                picPreview.Image?.Dispose();
                picPreview.Image = TextureToBitmap(_previewTexture);
                lblFileViewerInfo.Text = _previewInfo ?? $"{fname} ({sizeStr})";
            }
            else if (_previewText != null)
            {
                txtPreview.Visible = true;
                picPreview.Visible = false;
                txtHexPreview.Visible = false;
                txtPreview.Text = _previewText;
                lblFileViewerInfo.Text = _previewInfo ?? $"{fname} ({sizeStr})";
            }
            else if (_previewHex != null)
            {
                txtHexPreview.Visible = true;
                txtPreview.Visible = false;
                picPreview.Visible = false;
                txtHexPreview.Text = _previewHex;
                lblFileViewerInfo.Text = _previewInfo ?? $"{fname} ({sizeStr}) - hex, showing first {Helper.RoundBytes(1L << 20)}";
            }
            else
            {
                ShowWarning("Preview failed: entry could not be extracted.", false);
            }
        }

        /// <summary>Re-shows the cached asset list after a child preview (no re-extraction).</summary>
        private void btnAssetBack_Click(object sender, EventArgs e)
        {
            btnAssetBack.Visible = false;
            assetListView.Visible = true;
            picPreview.Visible = false;
            txtPreview.Visible = false;
            txtHexPreview.Visible = false;
            lblFileViewerInfo.Text = $"{_containerSource?.Name}: {_containerChildren.Count} entries (double-click to preview)";
        }

        /// <summary>Populates the asset workspace list with the container's children.</summary>
        private void PopulateAssetList()
        {
            _containerChildren.Clear();
            assetListView.Items.Clear();
            if (_containerSource == null || _containerDetection == null)
            {
                assetListView.Visible = false;
                return;
            }
            var children = _assetService.GetChildrenAsync(_containerSource, _containerDetection, 0).GetAwaiter().GetResult();
            assetListView.BeginUpdate();
            try
            {
                int shown = 0;
                foreach (var c in children)
                {
                    if (shown++ >= 2000)
                    {
                        assetListView.Items.Add(new ListViewItem(new[] { "...", "", "" }));
                        break;
                    }
                    _containerChildren.Add(c);
                    string type = c switch
                    {
                        Assets.Containers.UnityObjectAssetSource u => $"class {u.Object.ClassId}",
                        Assets.Containers.PakEntrySource p => p.Entry.Compression,
                        _ => "file",
                    };
                    assetListView.Items.Add(new ListViewItem(new[] { c.Name, type, Helper.RoundBytes(c.Length) }));
                }
            }
            finally { assetListView.EndUpdate(); }
            assetListView.Visible = true;
            picPreview.Visible = false;
            txtPreview.Visible = false;
            txtHexPreview.Visible = false;
            lblFileViewerInfo.Text = $"{_containerSource.Name}: {_containerChildren.Count} entries (double-click to preview)";
        }

        /// <summary>Releases the extracted container and clears the asset list.</summary>
        private void CleanupContainerBrowse()
        {
            _containerSource = null;
            _containerDetection = null;
            _containerChildren.Clear();
            if (assetListView != null)
            {
                assetListView.Items.Clear();
                assetListView.Visible = false;
            }
            if (btnAssetBack != null) btnAssetBack.Visible = false;
            if (_containerTempDir != null)
            {
                try { Directory.Delete(_containerTempDir, true); } catch { }
                _containerTempDir = null;
            }
        }

        private void assetListView_DoubleClick(object sender, EventArgs e)
        {
            if (assetListView.SelectedItems.Count == 0) return;
            int idx = assetListView.SelectedItems[0].Index;
            if (idx < 0 || idx >= _containerChildren.Count) return;
            PreviewChildAsset(_containerChildren[idx]);
        }

        /// <summary>Previews a container child: unity objects decode via the handler,
        /// pak entries / other children run the generic pipeline on a temp file.</summary>
        private void PreviewChildAsset(Assets.Abstractions.IAssetSource child)
        {
            int version = Interlocked.Increment(ref _previewVersion);
            var bg = new BackgroundWorker();
            _previewWorker = bg;
            bg.DoWork += (_, _) =>
            {
                try
                {
                    _previewTexture = null;
                    _previewText = null;
                    _previewHex = null;
                    _previewError = null;
                    _previewInfo = null;

                    if (child is Assets.Containers.UnityObjectAssetSource unityChild)
                    {
                        var handler = new Assets.Handlers.UnitySerializedFileHandler();
                        var preview = handler.PreviewAsync(unityChild, _containerDetection!).GetAwaiter().GetResult();
                        if (preview?.Texture != null) _previewTexture = preview.Texture;
                        else if (preview?.Text != null) _previewText = preview.Text;
                        else _previewError = "No preview available for this object.";
                        _previewInfo = preview?.Info;
                        return;
                    }

                    // Keep the entry's real filename (with extension) so the
                    // text/image detectors work on the temp copy.
                    string childName = Path.GetFileName(child.Name);
                    if (string.IsNullOrEmpty(Path.GetExtension(childName))) childName += ".bin";
                    string temp = Path.Combine(_containerTempDir!, "p4t_child_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + childName);
                    using (var s = child.OpenRead())
                    using (var fs = File.Create(temp))
                        s.CopyTo(fs);

                    var source = new Assets.IO.FileAssetSource(temp, "container member");
                    var detection = _assetService.Detect(source);
                    if (detection == null)
                    {
                        _previewHex = BuildHexDump(temp, 1 << 20);
                        return;
                    }
                    var descriptor = _assetService.InspectAsync(source, detection).GetAwaiter().GetResult();
                    _previewInfo = BuildPreviewInfo(child.Name, descriptor);
                    if (descriptor.Capabilities.HasFlag(Assets.Abstractions.AssetCapabilities.Preview))
                    {
                        var preview = _assetService.TryPreviewAsync(source, detection).GetAwaiter().GetResult();
                        if (preview?.Texture != null) _previewTexture = preview.Texture;
                        else if (preview?.Text != null) _previewText = preview.Text;
                        else _previewHex = BuildHexDump(temp, 1 << 20);
                    }
                    else
                    {
                        _previewHex = BuildHexDump(temp, 1 << 20);
                    }
                }
                catch (Exception ex)
                {
                    _previewError = ex.Message;
                }
            };
            bg.RunWorkerCompleted += (_, _) =>
            {
                toolStripProgressBar1.Style = ProgressBarStyle.Blocks;
                toolStripProgressBar1.Visible = false;
                toolStripStatusLabel2.Text = "...";
                _previewWorker = null;
                if (version != _previewVersion) return;
                // Always offer the way back while browsing a container, even
                // when the child preview failed (e.g. an unsupported entry).
                btnAssetBack.Visible = _containerSource != null;
                if (_previewError != null)
                {
                    ShowError("Preview failed: " + _previewError, false);
                    _previewError = null;
                    return;
                }
                try
                {
                    RenderPreviewResult(child.Name, Helper.RoundBytes(child.Length));
                }
                catch (Exception ex)
                {
                    ShowError("Preview failed: " + ex.Message, false);
                }
            };
            toolStripStatusLabel2.Text = "Previewing asset...";
            toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
            toolStripProgressBar1.Visible = true;
            bg.RunWorkerAsync();
        }

        /// <summary>Converts the neutral RGBA8 texture to a WinForms Bitmap (RGBA -> BGRA swap).</summary>
        private static Bitmap TextureToBitmap(Assets.Models.TextureData tex)
        {
            var bmp = new Bitmap(tex.Width, tex.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, tex.Width, tex.Height);
            var bits = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] rgba = tex.Rgba8;
                byte[] bgra = new byte[rgba.Length];
                for (int i = 0; i + 3 < rgba.Length; i += 4)
                {
                    bgra[i] = rgba[i + 2];     // B
                    bgra[i + 1] = rgba[i + 1]; // G
                    bgra[i + 2] = rgba[i];     // R
                    bgra[i + 3] = rgba[i + 3]; // A
                }
                System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bits.Scan0, bgra.Length);
            }
            finally
            {
                bmp.UnlockBits(bits);
            }
            return bmp;
        }

        /// <summary>Info-bar text from the descriptor: name (size) + format + key metadata.</summary>
        private static string BuildPreviewInfo(string fname, Assets.Models.AssetDescriptor d)
        {
            var sb = new System.Text.StringBuilder($"{fname} ({Helper.RoundBytes(d.Size)}) - {d.Format.ToUpperInvariant()}");
            int shown = 0;
            foreach (var kv in d.Metadata)
            {
                if (kv.Key is "Width" or "Height" or "Sample Rate" or "Channels" or "Bit Depth" or "Pixel Format")
                {
                    sb.Append(shown == 0 ? " [" : ", ").Append($"{kv.Key}={kv.Value}");
                    shown++;
                }
            }
            if (shown > 0) sb.Append(']');
            return sb.ToString();
        }

        private string ExtractSingleEntryForPreview(string pkgPath, string entryPath, string tempDir)
        {
            // Use the same orbis img_extract call pattern as ExtractFilesSync,
            // extracting a single entry path into tempDir.
            string dir = GetOrbisTempDirFor(pkgPath);
            string renameTmp = Path.Combine(dir, "ps4pkgtool_orbis_" + Guid.NewGuid().ToString("N") + ".pkg");
            File.Move(pkgPath, renameTmp);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Helper.AppDataDirectory + "orbis-pub-cmd.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("img_extract");
                psi.ArgumentList.Add("--passcode");
                psi.ArgumentList.Add(DefaultOrbisPasscode);
                psi.ArgumentList.Add(renameTmp + ":" + entryPath);
                psi.ArgumentList.Add(tempDir);
                using var proc = new Process { StartInfo = psi };
                proc.Start();
                var readTask = proc.StandardOutput.ReadToEndAsync();
                if (!proc.WaitForExit(600000))
                {
                    try { proc.Kill(); proc.WaitForExit(); } catch { }
                }
                _ = readTask.Result;
                string extracted = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories).FirstOrDefault();
                return extracted ?? "";
            }
            finally
            {
                try { if (File.Exists(renameTmp) && !File.Exists(pkgPath)) File.Move(renameTmp, pkgPath); }
                catch (Exception rex) { Logger.LogError($"Preview rename-back failed: {renameTmp} -> {pkgPath}: {rex.Message}"); }
                DeleteOrbisTempDir(renameTmp);
            }
        }

        private void PopulateListView(bool showRootNodes = false)
        {
            if (_populating) return;
            _populating = true;
            try
            {
                _allItems.Clear();
                _upItem = null;
                _currentNode = currentNode;

                // "..." navigation item
                if (currentNode != null && !showRootNodes)
                {
                    TreeNodeInfo parentItem = new TreeNodeInfo
                    {
                        Node = currentNode.Parent != null ? currentNode.Parent : null,
                        Path = "..."
                    };
                    _upItem = new ListViewItem("...");
                    _upItem.Tag = parentItem;
                    _upItem.ImageIndex = 5;
                    _upItem.SubItems.Add(""); _upItem.SubItems.Add(""); _upItem.SubItems.Add("");
                }

                listView1.SmallImageList = this.imageList1;

                List<TreeNode> list;
                if (currentNode != null)
                {
                    list = currentNode.Nodes.Cast<TreeNode>().ToList();
                }
                else if (showRootNodes)
                {
                    list = rootNodes;
                }
                else
                {
                    return;
                }

                foreach (var item in list)
                {
                    string fileName = Path.GetFileNameWithoutExtension(item.Text);
                    string dir = Path.GetDirectoryName(item.FullPath);
                    bool isDirectory = item.Nodes.Count > 0 || _pkgDirectories.Contains(item.FullPath);

                    TreeNodeInfo treeNodeInfo = new TreeNodeInfo
                    {
                        Node = item,
                        Path = item.FullPath
                    };

                    ListViewItem listViewItem = new ListViewItem(isDirectory ? "Directory" : "File");
                    listViewItem.Text = item.Text;
                    listViewItem.SubItems.Add(isDirectory ? "Directory" : Path.GetExtension(item.Text).Replace(".", ""));
                    listViewItem.SubItems.Add(dir);
                    listViewItem.SubItems.Add(isDirectory ? "" : Helper.RoundBytes(_fileSizes.GetValueOrDefault(item.FullPath, 0)));
                    listViewItem.Tag = treeNodeInfo;
                    listViewItem.ImageIndex = isDirectory ? 0 : IconFor(item.Text);

                    _allItems.Add(listViewItem);
                }

                ApplyFilter();
            }
            finally
            {
                _populating = false;
            }
        }

        // ── TreeView / ListView Filter ─────────────────────

        void ApplyFilter()
        {
            string q = tbFilterTreeView.SearchText.Trim();
            bool all = string.IsNullOrEmpty(q);
            listView1.BeginUpdate();
            listView1.Items.Clear();
            // The "..." up-folder item is navigation context, not a search match -
            // only show it when no filter is active.
            if (all && _upItem != null) listView1.Items.Add(_upItem);
            if (all)
            {
                foreach (var item in _allItems) listView1.Items.Add(item);
            }
            else
            {
                // Nodes detached from the tree (e.g. another PKG was selected)
                // throw on FullPath - nothing to filter against in that state.
                bool treeAttached = PKGTreeView.Nodes.Count > 0
                    || (rootNodes != null && rootNodes.Count > 0
                        && rootNodes[0].TreeView != null);
                if (treeAttached)
                {
                    // Search the WHOLE PKG (all root folders: Image0, Sc0, ...),
                    // not just the current folder - otherwise e.g. Sc0/param.sfo
                    // is unreachable while the list is focused on Image0.
                    if (rootNodes != null && rootNodes.Count > 0)
                        foreach (TreeNode root in rootNodes) CollectMatches(root, q);
                    else if (_currentNode != null)
                        CollectMatches(_currentNode, q);
                }
            }
            listView1.EndUpdate();
        }

        void CollectMatches(TreeNode node, string query)
        {
            var children = (node.Parent == null && node.TreeView == null)
                ? (rootNodes ?? Enumerable.Empty<TreeNode>())
                : node.Nodes.Cast<TreeNode>();
            foreach (TreeNode child in children)
            {
                bool isDir = child.Nodes.Count > 0;
                string type = isDir ? "Directory" : Path.GetExtension(child.Text).Replace(".", "");
                bool matches = child.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || type.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || child.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (matches)
                {
                    TreeNodeInfo info = new TreeNodeInfo { Node = child, Path = child.FullPath };
                    var item = new ListViewItem(isDir ? "Directory" : "File");
                    item.Text = child.Text;
                    item.SubItems.Add(type);
                    item.SubItems.Add(Path.GetDirectoryName(child.FullPath));
                    item.SubItems.Add(isDir ? "" : Helper.RoundBytes(_fileSizes.GetValueOrDefault(child.FullPath, 0)));
                    item.Tag = info;
                    item.ImageIndex = isDir ? 0 : IconFor(child.Text);
                    listView1.Items.Add(item);
                    ExpandAncestors(child);
                }
                if (isDir) CollectMatches(child, query);
            }
        }

        static void ExpandAncestors(TreeNode node)
        {
            var parent = node.Parent;
            while (parent != null) { parent.Expand(); parent = parent.Parent; }
        }

        private void settingstoolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenProgramSettings();
        }

        private void Backport_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            if (clickedMenuItem == setBackportedtoolStripMenuItem1 || clickedMenuItem == setBackportedToolStripMenuItem2)
            {
                if (PKGGridView.SelectedRows.Count > 0)
                {
                    foreach (DataGridViewRow row in PKGGridView.SelectedRows)
                    {
                        row.Cells["Backported"].Value = "Yes";
                        Logger.LogInformation($"\"{row.Cells["Filename"].Value}\" set as backported.");
                    }
                    ShowInformation("PKG set as backported.", false);
                }
            }

            if (clickedMenuItem == setRemarktoolStripMenuItem1)
            {
                if (PKGGridView.SelectedRows.Count > 0)
                {
                    foreach (DataGridViewRow row in PKGGridView.SelectedRows)
                    {
                        row.Cells["Backported"].Value = backportRemarkTextboxtoolStripTextBox1.Text;
                        Logger.LogInformation($"Added backport remark to \"{row.Cells["Filename"].Value}\" ({backportRemarkTextboxtoolStripTextBox1.Text}).");
                    }
                    ShowInformation("Backport remark added.", false);
                }
            }

            if (clickedMenuItem == setRemarktoolStripMenuItem2)
            {
                if (PKGGridView.SelectedRows.Count > 0)
                {
                    foreach (DataGridViewRow row in PKGGridView.SelectedRows)
                    {
                        row.Cells["Backported"].Value = backportRemarkTextboxtoolStripTextBox2.Text;
                        Logger.LogInformation($"Added backport remark to \"{row.Cells["Filename"].Value}\" ({backportRemarkTextboxtoolStripTextBox2.Text}).");
                    }
                    ShowInformation("Backport remark added.", false);
                }
            }

            if (clickedMenuItem == removeBackportedtoolStripMenuItem1 || clickedMenuItem == removeBackportedToolStripMenuItem2)
            {
                if (PKGGridView.SelectedRows.Count > 0)
                {
                    foreach (DataGridViewRow row in PKGGridView.SelectedRows)
                    {
                        row.Cells["Backported"].Value = "No";
                        Logger.LogInformation($"Removed backport label from \"{row.Cells["Filename"].Value}\".");
                    }
                    ShowInformation("Backport remark removed.", false);
                }
            }

            Backport.SaveData(PKGGridView);
            PopulateGroupedView(); // reflect backport labels in the grouped view
        }

        private void tbLog_TextChanged(object sender, EventArgs e)
        {
            //tbLog.SelectionStart = tbLog.Text.Length;
            //tbLog.ScrollToCaret();
        }


        // Helper method to find or create a node with a specific text
        private TreeNode FindOrCreateNode(TreeNodeCollection nodes, string text)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Text == text)
                {
                    return node;
                }
            }

            // If the node doesn't exist, create it
            TreeNode newNode = new TreeNode(text);
            nodes.Add(newNode);
            return newNode;
        }

        // Helper method to find or create a category node (Game, Patch, Addon, App)
        private TreeNode FindOrCreateCategoryNode(TreeNode parentNode, string categoryName)
        {
            foreach (TreeNode categoryNode in parentNode.Nodes)
            {
                if (categoryNode.Text == categoryName)
                {
                    return categoryNode;
                }
            }

            // If the category node doesn't exist, create it
            TreeNode newCategoryNode = new TreeNode(categoryName);
            parentNode.Nodes.Add(newCategoryNode);
            return newCategoryNode;
        }

        private void OpenAppDataDirectory_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem clickedMenuItem))
                return;

            if (clickedMenuItem == openAppDataDirectoryToolStripMenuItem2)
            {
                OpenTempDirectory();
            }
        }
    }
}
