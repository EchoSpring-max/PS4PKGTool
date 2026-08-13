# PS4 PKG Tool — Complete Project Documentation for WinForms → WPF Migration

> **Version analyzed:** 1.6 (AssemblyInformationalVersion)
> **Date:** 2026-05-20
> **Purpose:** Full analysis for migration from Windows Forms to WPF

---

## 1. PROJECT OVERVIEW

**PS4 PKG Tool** is a Windows desktop application for managing, analyzing, and organizing PlayStation 4 PKG (Package) files. It provides a dark-themed GUI for browsing PKG collections, extracting resources (icons, images, trophies, background music), renaming/moving files, checking official updates, sending PKGs to a PS4 over the network, and more.

| Attribute | Value |
|-----------|-------|
| **Project type** | .NET SDK-style, Windows Forms (`WinExe`) |
| **Target framework** | `net10.0-windows7.0` (primary), also `net7.0-windows` |
| **Language** | C# (with some VB.NET interop remnants) |
| **UI framework** | Windows Forms + DarkUI theme library |
| **Number of source files** | 36 `.cs` files |
| **Lines of code** | ~12,000+ (Main.cs alone is ~5,300 lines) |
| **Forms** | 5 forms, 8 tab pages in main form |
| **Entry point** | `Program.cs` → `Main()` |

---

## 2. SOLUTION & PROJECT STRUCTURE

```
PS4PKGTool/
├── PS4PKGTool.sln                          # Solution file (VS 2022)
└── PS4PKGTool/                             # Project directory
    ├── PS4PKGTool.csproj                   # SDK-style project file
    ├── Program.cs                           # Entry point
    ├── App.config                           # .NET Framework-style config (legacy)
    ├── IllegalNameCheck.cs                  # File name validation utility
    ├── Properties/
    │   ├── AssemblyInfo.cs                  # Assembly metadata (GUID, version)
    │   ├── Resources.resx / .Designer.cs    # Embedded resources (flag icons)
    │   └── Settings.settings / .Designer.cs # .NET settings (unused, stub)
    ├── Forms/
    │   ├── Main/
    │   │   ├── Main.cs                      # Main form (~5300 lines, ALL business logic)
    │   │   ├── Main.Designer.cs             # Designer-generated UI (~3700 lines)
    │   │   └── Main.resx                    # Form resources
    │   ├── DLC/
    │   │   ├── DLC.cs                       # DLC/Addon viewer form
    │   │   ├── DLC.Designer.cs              # Designer-generated UI
    │   │   └── DLC.resx
    │   ├── PKG Change Info Viewer/
    │   │   ├── PKG Change Info Viewer.cs    # Patch changelog viewer
    │   │   ├── PKG Change Info Viewer.Designer.cs
    │   │   └── PKG Change Info Viewer.resx
    │   ├── PKG Directory Settings/
    │   │   ├── PKG Directory Settings.cs    # Directory picker (startup)
    │   │   ├── PKG Directory Settings.Designer.cs
    │   │   └── PKG Directory Settings.resx
    │   └── Program Settings/
    │       ├── Program Settings.cs          # Full settings/preferences
    │       ├── Program Settings.Designer.cs
    │       └── Program Settings.resx
    └── Utilities/
        ├── GlobalUsing.cs                   # Global usings
        ├── Logger.cs                        # File-based logging
        ├── ListViewDraw.cs                  # ListView header custom drawing
        ├── Wallpaper.cs                     # Desktop wallpaper setter
        ├── System.IO.cs                     # EndianIO, EndianReader, EndianWriter
        ├── Constants/
        │   ├── PKGState.cs                  # "Fake", "Official", "Addon_Unlocker"
        │   ├── PKGRegion.cs                 # "EU", "US", "JAPAN", "HONG KONG", "ASIA", "KOREA"
        │   ├── PKGCategory.cs               # "Game", "Patch", "Addon", "App", "Uknown"
        │   ├── PKGSelectionType.cs          # "ALL", "SELECTED"
        │   ├── ImageIconExtractionType.cs   # "ALL", "IMAGE", "ICON"
        │   └── NamingFormat.cs              # Rename placeholders: {TITLE}, {TITLE_ID}, etc.
        ├── Settings/
        │   ├── AppSettings.cs               # Settings POCO (30+ properties)
        │   └── SettingsManager.cs           # Read/write settings to flat .conf file
        ├── PS4PKGToolHelper/
        │   ├── Helper.cs                    # Master helper (~1900 lines — see below)
        │   ├── DialogHelper.cs              # FolderBrowserDialog/SaveFileDialog wrappers
        │   └── MessageBoxHelper.cs          # DarkMessageBox wrappers (Info/Error/Warn/YesNo)
        ├── Extension/
        │   ├── Extension.cs                 # String extension: GetUntilOrEmpty()
        │   └── TreeView.cs                  # TreeView extension: GetAllNodes()
        └── Trophy/
            ├── Trophy.cs                    # PS4_Trophy_xdpx namespace (legacy TRP loader)
            ├── TRPReader.cs                 # TRP file reader (decompiled)
            ├── TRPCreator.cs                # TRP file creator (decompiled)
            ├── Archiver.cs                  # TRP archive entry model
            ├── Utilities.cs                 # TRPViewer utilities (decompiled)
            └── Utils.cs                     # Binary/hex/compression helpers
```

---

## 3. THIRD-PARTY DEPENDENCIES

### NuGet Packages

| Package | Version | Purpose | WPF Compatible? |
|---------|---------|---------|-----------------|
| `ByteSize` | 2.1.1 | Human-readable file sizes | Yes |
| `ClosedXML` | 0.102.0 | Excel (.xlsx) export | Yes |
| `DotNetZip` | 1.13.8 | ZIP archive handling | Yes |
| `GitHubUpdate` | 1.2.0 | Auto-update checker | Yes |
| `Microsoft.Extensions.Logging` | 7.0.0 | Logging abstractions | Yes |
| `Microsoft.VisualBasic` | 10.3.0 | VB.NET compatibility | Yes |
| `semver` | 2.3.0 | Semantic version parsing | Yes |
| `Microsoft.CSharp` | 4.7.0 | Dynamic support | Yes |
| `Newtonsoft.Json` | 13.0.3 | JSON serialization | Yes |
| `Serilog.Sinks.File` | 5.0.0 | File logging (unused in code?) | Yes |

### Direct DLL References (WinForms-specific — critical for migration)

| DLL | Purpose | WPF Status |
|-----|---------|------------|
| **DarkUI** | Dark-themed WinForms controls | **MUST REPLACE** — WinForms-only |
| **VisualStudioTabControl.Core** | Custom tab control | **MUST REPLACE** — WinForms-only |
| PS4_Tools | Core PS4 PKG reading | OK — no UI dependency |
| LibOrbisPkg.Core | Low-level PKG reader | OK — no UI dependency |
| PARAM.SFO | PS4 param file parser | OK — no UI dependency |
| DiscUtils | Disk utilities | OK — no UI dependency |
| GameArchives | Game archive handling | OK — no UI dependency |

### External Tools Required at Runtime

These must be present in `AppData\`:
- `orbis-pub-cmd.exe` — Orbis publishing command-line tool (for PKG extraction & file listing)
- `curl.exe` — For HTTP requests to the PS4's Remote Package Installer API
- Node.js + `http-server` npm module — For serving PKG files to PS4

---

## 4. ARCHITECTURE & DESIGN PATTERNS

### 4.1 Application Lifecycle

```
Program.Main()
  ├─ Application.EnableVisualStyles()
  ├─ EnsureSettingsFileExists()
  │   ├─ Creates AppData directory if missing
  │   └─ Creates default Settings.conf if missing
  ├─ LoadSettings(SettingFilePath) → populates static appSettings_
  └─ ChooseStartupForm()
      ├─ If ShowDirectorySettingsAtStartup → new PKG_Directory_Settings()
      └─ Else → new Main()
      └─ Application.Run(form)
```

### 4.2 Master Helper Class (`Helper` in `Helper.cs`)

The entire business state lives in a single static class `Helper` with nested static classes:

```
Helper
├── static fields: FirstLaunch, FinalizePkgProcess
├── static paths: AppDataDirectory, OrbisPubCmd, Ps5BcJsonFile, PS4PKGToolLogFile
├── RoundBytes(long) → human-readable byte count
├── Backport (nested class) — backport tracking via JSON file
│   ├── BackportInfo (model)
│   ├── CheckPKGBackported(pkgFile)
│   └── SaveData(DarkDataGridView)
├── Bitmap (nested class) — image conversion helpers
│   ├── BytesToBitmap / BytesToImage / ImageToBytes / GetImage
│   └── IsAdmin()
├── Passcode (nested class) — PKG passcode storage
├── Update (nested class) — official update download state
├── Entry (nested class) — PKG entry decryption (AES, RSA, SHA256)
│   ├── PackageEntry struct
│   ├── Decrypt(byte[]) — RSA-2048 decryption
│   ├── DecryptAes()
│   ├── Sha256()
│   ├── EntryIdNameDictionary (static)
│   └── EncryptedEntryOffsetNameDictionary (static)
├── TreeView (nested class) — file explorer tree/list state
│   ├── TreeNodeInfo
│   └── currentNode, rootNodes, Nodename
├── PKG (nested class) — global PKG state container
│   ├── Counters: game, patch, addon, app, official, fake, unknown, unlockerAddon, pkgCount
│   ├── Lists: VerifiedPs4PkgList, EntryIdList, EntryNameList
│   ├── State: SelectedPKGFilename, CurrentPKGTitle, CurrentPKGType, NodeFullPath
│   ├── Magic byte validation: PkgHeader..PkgHeader4
│   ├── GetPkgHeaderBuffer() — reads 16-byte header
│   └── CompareBytes() — byte array comparison
├── BGM (nested class) — background music
│   ├── ExtractBgm() — extracts AT9 audio from all PKGs
│   └── PlayAt9(pkg) — plays AT9 via SoundPlayer
├── NodeJsHttpServer (nested class) — Node.js setup check
├── Tool (nested class) — OS/network utilities
│   ├── IsRootDrive / CreateDirectoryIfNotExists
│   ├── DownloadFileFromUrlAsync / OpenWebLink
│   ├── IsAppInstalled (registry check)
│   ├── KillNodeJS / CheckForPS4Connection / CheckForInternetConnection
│   └── CheckLocalServerConnection
├── PKGSENDER (nested class) — Remote Package Installer communication
│   ├── JSON (nested) — API response state containers
│   │   ├── STOPTASK, SENDPKG, UNINTSALLAPP, UNINTSALLPATCH
│   │   ├── UNINTSALLADDON, UNINTSALLTHEME, CHECKAPPEXISTS, MONITORTASK
│   │   └── All use static string properties for state
│   ├── MonitorPkgSenderTaskBackgroundWorker
│   ├── State: taskMonitorIsCancelling, isPreparing, pkgSendDone, pkgSendStopped
│   ├── CheckRequirement() → validates Node.js, curl, PS4 IP, etc.
│   ├── CheckIfPkgInstalled(psfo) → calls PS4 RPI API via curl
│   ├── StopTask() → calls PS4 RPI stop API
│   ├── UninstallAddonTheme / UninstallPatch / UninstallGame
│   ├── GetTaskProgress() → polls PS4 for transfer status
│   ├── RunServer(directory) → starts http-server
│   └── SendPKG(tempFilename) → sends PKG to PS4 via curl
└── Trophy (nested class) — trophy extraction state
    ├── TRPReader trophy, outPath
    ├── ImageToExtractList, TrophyFilenameToExtractList
    ├── idEntryList, nameEntryList
    ├── TrophyTempFolder
    └── ResizeImage()
```

### 4.3 Settings Architecture

```
SettingsManager (static)
├── appSettings_ : AppSettings (static instance)
├── SettingFilePath = "{exe}\AppData\Settings.conf"
├── SaveSettings(AppSettings, filePath) → writes key=value flat file
└── LoadSettings(filePath) → reads key=value flat file → returns AppSettings

AppSettings (POCO with 30+ properties)
├── PkgDirectories : List<string>
├── ScanRecursive : bool
├── PlayBgm : bool
├── ShowDirectorySettingsAtStartup : bool
├── AutoSortRow : bool
├── LocalServerIp, Ps4Ip, OfficialUpdateDownloadDirectory : string
├── NodeJsInstalled, HttpServerInstalled : bool
├── PkgColorLabel : bool
├── ForeColor/BackColor for Game, Patch, Addon, App : Color
├── RenameCustomName : string
├── Ps5BcJsonLastDownloadDate : DateTime
├── psvr_neo_ps5bc_check : bool
└── Column visibility booleans (11 columns)
```

### 4.4 Logging

- `Logger` class writes to `AppData\PS4PKGToolLog.txt`
- Thread-safe via `lock`
- Levels: Information → "INFO", Warning → "WARN", Error → "ERR"
- Format: `{DateTime:G} : [{level}] {message}`
- MessageBoxHelper optionally logs when showing dialogs

---

## 5. DETAILED FORM ANALYSIS

### 5.1 Main Form (`Main.cs` — ~5,300 lines)

**Base class:** `DarkUI.Forms.DarkForm`

**Key controls (from Designer.cs):**

| Control | Type | Purpose |
|---------|------|---------|
| `darkMenuStrip1` | DarkMenuStrip | Top menu bar (File, Tool, Help) |
| `contextMenuPKGGridView` | DarkContextMenu | Right-click menu on PKG grid (~25 items) |
| `darkStatusStrip1` | DarkStatusStrip | Bottom status bar with progress |
| `flatTabControl1` | VisualStudioTabControl | Main tab container (7 tabs) |
| `PKGGridView` | DarkDataGridView | Primary PKG list (15 columns) |
| `PKGListView` | ListView | Alternative PKG list view |
| `darkDataGridView2` | DarkDataGridView | PARAM.SFO data display |
| `pictureBox1` | PictureBox | PKG icon display |
| `pbPIC0` / `pbPIC1` | PictureBox | Background images (PIC0/PIC1) |
| `TrophyGridView` | DarkDataGridView | Trophy entries grid |
| `dgvEntryList` | DarkDataGridView | PKG file entries |
| `dgvHeader` | DarkDataGridView | PKG header info |
| `darkDataGridView4` | DarkDataGridView | PubTool info |
| `dgvUpdate` | DarkDataGridView | Official update parts |
| `PKGTreeView` | TreeView | PKG internal file tree |
| `listView1` | ListView | File listing in explorer tab |
| `tbSearchGame` | DarkTextBox | Search/filter PKG list |
| `tbPasscode` | DarkTextBox | PKG passcode input |
| `tbSearchTreeView` | DarkTextBox | Search in file tree |
| `btnExtractFullPKG` | DarkButton | Full PKG extraction |
| `btnViewPKGData` | DarkButton | Load PKG file listing |

**Tab pages structure:**
```
flatTabControl1
├── tabPage1  — PKG Grid + Detail Info
│   ├── Search box + PKGGridView (data grid)
│   ├── PKG icon (pictureBox1) + title (darkLabel1)
│   └── PARAM.SFO data (darkDataGridView2)
├── tabPage6  — Alternative ListView (PKGListView + TreeView)
├── tabPage2  — Trophy Viewer (TrophyGridView)
├── tabPage3  — Background Images
│   └── flatTabControlBgi
│       ├── tabPagePic0 → pbPIC0
│       └── tabPagePic1 → pbPIC1
├── tabPage4  — PKG Contents
│   ├── dgvEntryList (file entries)
│   ├── dgvHeader (header info)
│   └── darkDataGridView4 (PubTool info)
├── tabPage7  — PKG File Explorer
│   ├── Passcode textbox + buttons
│   ├── PKGTreeView + listView1 (split container)
│   └── Search in tree view
└── tabPage5  — Official Update
    ├── dgvUpdate (update parts list)
    └── Update metadata labels (version, size, type, etc.)
```

**Major method groups in Main.cs:**

| Region / Group | Methods | Lines (approx.) |
|---------------|---------|-----------------|
| Constructor & Init | `Main()`, `Form1_Load()`, `Form1_FormClosed()` | 60-210 |
| PKG Detail Loading | `LoadPKGDetails()`, `ShowPackageIcon()`, `UpdateParamInfoGrid()`, `LoadHeaderInfo()`, `LoadPKGEntries()`, `LoadBackgroundImages()`, `LoadTrophyInfo()`, `LoadPubToolInfo()`, `GetOfficialUpdate()` | 212-465 |
| PKG Scanning | `LoadPKGGridView()`, `LoadPKGListView()`, `PostPkgLoad()`, `FinalizePkgLoadingProcess()` | 841-2247 |
| PKG Filtering | `GridViewFilterPKG_Click()`, `darkButton3_Click()`, `TbSearchGame_TextChanged()` | 2277-4350 |
| PKG Rename | `RenamePkg_Click()`, `RenamePKG()`, `UpdatePKGFilename()` | 1254-1595, 4536-4596 |
| PKG Export | `ExportPKGToExcel_Click()`, `InitializedExportPKGToExcel()`, `GenerateDatatableFromSelectedPKG()` | 1381-1416, 4948-5042 |
| PKG Move | `MovePkg_Click()`, `MovePKG()`, `MovePKGByCategory/Type/Title/Region()` | 4619-4859 |
| PKG Delete | `DeletePkg()`, `DeletePKG_Click()` | 2322-2376 |
| PKG View | `ViewPKGInExplorer()`, `ViewPKGExplorer_Click()`, `ViewUpdateChangelog()` | 2310-2315, 4390-4477 |
| Copy ID | `CopyTitleID()`, `CopyContentID()`, `CopyID_Click()` | 1153-1171, 1418-1453 |
| Image/Icon Extract | `ImageIconExtractor()`, `InitializedImageIconExtractor()`, `ExtractImage()`, etc. | 979-1536 |
| Trophy Extract | `ExtractTrophyFile()`, `ExtractTrophyIcon()`, `LoadTrophyInfo()` | 582-875, 3292-3338 |
| Background Image | `SaveBackgroundImage()`, `SetImageAsDesktopBackground()`, `ContextMenuBackgroundImage_Click()` | 3340-3419 |
| Entry Extract | `ExtractDecryptedEntry()`, `ExtractAllEntry()`, `ExtractFullPKG()`, `ExtractSelectedPKGData()` | 3427-4055 |
| PKG TreeView | `PopulatePKGDataToTreeView()`, `PopulateListView()`, `PKGTreeView_AfterSelect()` | 3714-5201 |
| Official Update | `GetOfficialUpdate()`, `InitializedDownloadSelectedOfficialUupdate()`, `DownloadSelectedOfficialUpdate()`, `CancelDownloadingFile()` | 312-467, 4104-4301 |
| RPI (Send to PS4) | `InitializePKGSender()`, `SendPKG()`, `MonitorPKGSenderTask()`, `Rpi_Click()`, `CheckIfAppInstalledOnPS4()`, `UninstallBasePkgFromPs4()`, `UninstallPatchPkgFromPs4()`, `UninstallDlcPkgFromPs4()`, `UninstallThemePkgFromPs4()` | 2473-3266 |
| Backport | `Backport_Click()` | 5208-5266 |
| Settings | `OpenProgramSettings()`, `settingstoolStripMenuItem_Click()` | 3269-3290 |
| BGM | `PlayBGM()`, `SetBackgroundMusicVolume()` | 878-895, 2172-2186 |
| Duplicate Check | `CheckForDuplicatePKG_Click()`, `FindDuplicatePKG()` | 2382-2435 |
| UI Helpers | `DisableTabPages()`, `EnableTabPages()`, `DisableControls()`, `EnableControls()`, `DisableControls_PkgSender()`, `EnableControls_PkgSender()` | 4171-4254 |
| Color Labeling | `UpdatePKGColorLabel()`, `PKGListGridView_CellFormatting()`, `GetCellStyle()` | 4861-4924 |
| Get Selected | `GetSelectedPKGPath()`, `GetSelectedPKGDirectoryList()`, `SelectFirstRowPkg()` | 948-977, 4598-4617 |
| Check Orbis | `CheckOrbisPubCmdExists()` | 4479-4488 |
| Tree Search | `SearchFileInTreeView()`, `SearchFileInTreeView_Click()`, `expandAllToolStripMenuItem_Click()`, `collapseAllNodeToolStripMenuItem_Click()` | 4490-4534 |
| Misc Events | `listView1_SizeChanged`, `listView1_ColumnWidthChanging`, `listView1_MouseClick`, `listView1_ItemActivate`, etc. | 4328-4388 |

### 5.2 DLC Form (`DLC.cs`)

- **Purpose:** Displays DLC/Addon items for a game fetched from the PlayStation Store API
- **Base class:** `DarkUI.Forms.DarkForm`
- **Controls:** Single `DarkDataGridView` bound to `List<StoreItems>`
- **Entry point:** Called from Main when user clicks "View Addon" context menu item
- **Data source:** `PS4_Tools.PKG.Official.Get_All_Store_Items("CUSA07022")` (hardcoded title ID for testing?)

### 5.3 PKG Change Info Viewer (`PKG Change Info Viewer.cs`)

- **Purpose:** Displays patch changelog extracted from `Sc0/changeinfo/changeinfo.xml` inside a PKG
- **Base class:** `DarkUI.Forms.DarkForm`
- **Controls:** Single `DarkDataGridView` with two columns: "App Version" and "Change Info"
- **Data:** XML parsed via `XmlDocument`, each `<changeinfo>` node displayed as a row
- **Cell wrapping:** Enabled for multi-line change info text

### 5.4 PKG Directory Settings (`PKG Directory Settings.cs`)

- **Purpose:** First-launch / reconfiguration form for selecting PKG source directories
- **Base class:** `DarkUI.Forms.DarkForm`
- **Controls:**
  - `darkListBox1` — Lists selected PKG directories
  - `btnAddFolder` / `btnDeleteFolder` — Add/remove directories
  - `darkCheckBoxRecursive` — Toggle recursive scanning
  - `darkCheckBoxDontshowthisagain` — Toggle "show at startup"
  - `btnLoadPkg` — Save settings and launch Main form
- **Flow:** If `FirstLaunch == true`, shows this form first, then creates and shows Main form

### 5.5 Program Settings (`Program Settings.cs`)

- **Purpose:** Full application settings/preferences
- **Base class:** `DarkUI.Forms.DarkForm`
- **Controls (many):**
  - PKG directory list management
  - Column visibility checkboxes (11 columns)
  - Color picker buttons for PKG category coloring
  - Naming pattern builder with placeholder dropdown
  - Server IP combo box / PS4 IP textbox
  - Node.js/http-server installation status and buttons
  - PS5 BC JSON download button
  - BGM toggle, auto-sort toggle, "show at startup" toggle
  - Official update download directory picker
  - Save/Close button

---

## 6. DATA FLOW & KEY PROCESSES

### 6.1 PKG Scanning & Loading

```
1. User specifies directories → stored in appSettings_.PkgDirectories
2. On Main form load:
   └─ BackgroundWorker scans directories for *.PKG files
      └─ For each .PKG file:
         ├─ Read first 16 bytes (magic header check)
         ├─ Compare against 5 known PS4 PKG header patterns
         ├─ If valid → add to PKG.VerifiedPs4PkgList
         └─ If invalid → skip
3. For each verified PKG:
   └─ PS4_Tools.PKG.SceneRelated.Read_PKG(path) → Unprotected_PKG
      ├─ Extract: PS4_Title, Content_ID, PKG_Type, PKGState, Region
      ├─ Extract: Firmware_Version, APP_VER, VERSION from PARAM.SFO
      ├─ Extract: File size via System.IO.FileInfo
      ├─ Check PS5 BC JSON for PSVR/Neo/PS5BC info
      ├─ Check backport.json for backport status
      └─ Collect into DataTable → bind to PKGGridView
4. Count by category (game/patch/addon/app/unknown)
5. Count by state (official/fake/addon_unlocker)
6. Show filter dropdown with counts
7. Extract trophy files and BGM in background
```

### 6.2 PKG Detail Viewing (on selection change)

```
PKGGridView_SelectionChanged
└─ LoadPKGDetails()
   ├─ Read_PKG(SelectedPKGFilename)
   ├─ ShowPackageIcon() → pictureBox1
   ├─ PlayBGM() (if setting enabled)
   ├─ UpdateParamInfoGrid() → darkDataGridView2 (PARAM.SFO table)
   ├─ LoadBackgroundImages() → pbPIC0/pbPIC1
   ├─ LoadTrophyInfo() → TrophyGridView (extracts .TRP, loads entries)
   ├─ LoadHeaderInfo() → dgvHeader
   ├─ LoadPKGEntries() → dgvEntryList (via PkgReader)
   ├─ LoadPubToolInfo() → darkDataGridView4
   └─ GetOfficialUpdate() → dgvUpdate (async web call)
```

### 6.3 PKG Rename Process

```
RenamePkg_Click → determines format + selection type
└─ RenamePKG(format, pkgList) [BackgroundWorker]
   └─ For each PKG:
      ├─ Read_PKG(pkg)
      ├─ PS4_Tools.PKG.SceneRelated.GetNewPKGName() → new name
      ├─ File.Move(source, target)
      └─ UpdatePKGFilename() → updates grid cell
```

### 6.4 Remote PKG Installer (Send to PS4)

```
Rpi_Click → InitializePKGSender()
├─ PKGSENDER.CheckRequirement()
│   ├─ Node.js installed?
│   ├─ http-server module installed?
│   ├─ curl.exe present?
│   ├─ PS4 IP set?
│   ├─ Server IP set?
│   ├─ PS4 reachable via ping?
│   └─ Server IP valid via ping?
├─ CheckIfPkgInstalled() → curl to PS4 RPI API
├─ SendPKG() [BackgroundWorker]
│   ├─ Rename PKG to temp name
│   ├─ PKGSENDER.RunServer(directory) → starts http-server
│   ├─ PKGSENDER.SendPKG(tempFilename) → curl to PS4 install API
│   └─ MonitorPKGSenderTask() → polls progress, updates status bar
└─ On complete: rename back, kill server, show result
```

### 6.5 PKG Entry Decryption

```
ExtractDecryptedEntry()
├─ Read PKG binary via EndianIO
├─ Read entry table (offset 0x10 for count, 0x18 for table offset)
├─ Read string table
├─ For each entry:
│   ├─ Check is_encrypted flag
│   ├─ Build key seed from entry data + RSA-decrypted data
│   ├─ SHA256 → IV + AES key
│   ├─ AES-CBC decrypt
│   └─ Save to output directory
```

---

## 7. REUSABLE VS MUST-REPLACE COMPONENTS

### 7.1 Fully Reusable (no changes needed)

| Component | Files |
|-----------|-------|
| Constants | `PKGState.cs`, `PKGRegion.cs`, `PKGCategory.cs`, `PKGSelectionType.cs`, `ImageIconExtractionType.cs`, `NamingFormat.cs` |
| Settings model | `AppSettings.cs` |
| Settings I/O | `SettingsManager.cs` (just change MessageBoxHelper calls) |
| Logger | `Logger.cs` |
| Helpers (non-UI) | Helper.Entry, Helper.Backport, Helper.Passcode, Helper.Update, Helper.PKG (static state), Helper.NodeJsHttpServer, Helper.Tool |
| PKGSENDER | All curl/API communication logic |
| Trophy | `TRPReader.cs`, `TRPCreator.cs`, `Archiver.cs`, `Trophy.cs` |
| EndianIO | `System.IO.cs` (EndianReader, EndianWriter, EndianIO) |
| Extensions | `Extension.cs` (string extension), `TreeView.cs` (but TreeNode usage will change) |
| File validation | `IllegalNameCheck.cs` |
| Wallpaper | `Wallpaper.cs` (uses Win32 API — still works in WPF) |
| BGM logic | Helper.BGM (ExtractBgm, PlayAt9) — uses System.Media.SoundPlayer |

### 7.2 Must Replace (WinForms-specific)

| WinForms Component | WPF Replacement |
|-------------------|-----------------|
| `DarkUI.Forms.DarkForm` | `Window` (with custom dark style/template) |
| `DarkUI.Controls.DarkMenuStrip` | `Menu` with dark style |
| `DarkUI.Controls.DarkContextMenu` | `ContextMenu` with dark style |
| `DarkUI.Controls.DarkDataGridView` | `DataGrid` with `DataTable` binding |
| `DarkUI.Controls.DarkSectionPanel` | `GroupBox` or `Border` with header |
| `DarkUI.Controls.DarkLabel` | `TextBlock` or `Label` |
| `DarkUI.Controls.DarkTextBox` | `TextBox` with dark style |
| `DarkUI.Controls.DarkButton` | `Button` with dark style |
| `DarkUI.Controls.DarkStatusStrip` | `StatusBar` |
| `DarkUI.Controls.DarkCheckBox` | `CheckBox` with dark style |
| `DarkUI.Forms.DarkMessageBox` | `MessageBox` or custom WPF dialog |
| `VisualStudioTabControl.VisualStudioTabControl` | `TabControl` |
| `System.Windows.Forms.ListView` | `ListView` (WPF) |
| `System.Windows.Forms.TreeView` | `TreeView` (WPF) |
| `System.Windows.Forms.PictureBox` | `Image` control |
| `System.Windows.Forms.SplitContainer` | `GridSplitter` |
| `System.Windows.Forms.BackgroundWorker` | `BackgroundWorker` (still available in WPF) or `Task`/`async` |
| `System.Windows.Forms.Timer` | `DispatcherTimer` |
| `System.Windows.Forms.FolderBrowserDialog` | `System.Windows.Forms.FolderBrowserDialog` (still usable) or `Microsoft.Win32.OpenFolderDialog` |
| `System.Windows.Forms.SaveFileDialog` | `Microsoft.Win32.SaveFileDialog` |
| `System.Windows.Forms.NotifyIcon` | `System.Windows.Forms.NotifyIcon` (still usable) |
| `System.Windows.Forms.ColorDialog` | `System.Windows.Forms.ColorDialog` (still usable) |
| `System.Windows.Forms.Clipboard` | `System.Windows.Clipboard` |

### 7.3 Event Handler Patterns to Change

| WinForms Pattern | WPF Equivalent |
|-----------------|----------------|
| `this.Invoke((MethodInvoker)delegate { ... })` | `Dispatcher.Invoke(() => { ... })` |
| `Control.Enabled = false` | `IsEnabled = false` |
| `Control.Visible = false` | `Visibility = Collapsed` |
| `Form.Text = "..."` | `Window.Title = "..."` |
| `form.ShowDialog()` | `window.ShowDialog()` |
| `form.Hide()` / `form.Show()` | `window.Hide()` / `window.Show()` |
| `Application.Exit()` | `Application.Current.Shutdown()` |
| `e.CellStyle.BackColor` | DataGrid cell style binding |
| `DataGridView.Rows.Add(...)` | Add rows to `DataTable` bound to `DataGrid` |
| `DataGridView.SelectedRows` | `DataGrid.SelectedItems` |
| `DataGridView.Columns[i].Visible` | Column visibility binding |
| `listView1.Items.Add(...)` | `ListView.Items.Add(...)` (WPF ListView) |
| `PKGTreeView.Nodes.Add(...)` | `TreeViewItem` hierarchy |
| `ProgressBar.Increment(n)` | Binding to progress value |
| `PictureBox.Image = bitmap` | `ImageBrush` or `Image.Source` |
| `ControlPaint.DrawBorder(...)` | WPF Border/BorderBrush |

---

## 8. PROPOSED WPF ARCHITECTURE

### 8.1 Recommended Project Structure (New WPF Project)

```
PS4PKGTool.Wpf/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── Windows/
│   ├── DlcViewerWindow.xaml/.cs
│   ├── PkgChangeInfoWindow.xaml/.cs
│   ├── PkgDirectorySettingsWindow.xaml/.cs
│   └── ProgramSettingsWindow.xaml/.cs
├── Views/ (UserControls — one per tab)
│   ├── PkgListView.xaml/.cs          (tabPage1 replacement)
│   ├── PkgDetailView.xaml/.cs        (right panel of tabPage1)
│   ├── TrophyView.xaml/.cs           (tabPage2 replacement)
│   ├── BackgroundImageView.xaml/.cs  (tabPage3 replacement)
│   ├── PkgContentsView.xaml/.cs      (tabPage4 replacement)
│   ├── PkgExplorerView.xaml/.cs      (tabPage7 replacement)
│   └── OfficialUpdateView.xaml/.cs   (tabPage5 replacement)
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── PkgListViewModel.cs
│   ├── PkgDetailViewModel.cs
│   ├── TrophyViewModel.cs
│   ├── PkgExplorerViewModel.cs
│   ├── OfficialUpdateViewModel.cs
│   ├── DlcViewerViewModel.cs
│   ├── PkgDirectorySettingsViewModel.cs
│   └── ProgramSettingsViewModel.cs
├── Models/
│   ├── PkgFileInfo.cs               (observed PKG model)
│   ├── PkgEntryInfo.cs              (entry model)
│   └── TrophyEntryInfo.cs           (trophy entry model)
├── Services/
│   ├── PkgScanService.cs            (directory scanning, PKG reading)
│   ├── PkgExtractionService.cs      (entry extraction, orbis-pub-cmd)
│   ├── RpiService.cs               (PS4 communication) — from Helper.PKGSENDER
│   ├── TrophyService.cs            (trophy extraction)
│   ├── BgmService.cs               (BGM extraction/playback)
│   ├── UpdateService.cs            (official update check/download)
│   ├── ExcelExportService.cs       (ClosedXML export)
│   ├── SettingsService.cs          (from SettingsManager)
│   └── LogService.cs               (from Logger)
├── Themes/
│   └── DarkTheme.xaml              (ResourceDictionary with dark colors/styles)
├── Converters/
│   ├── BoolToVisibilityConverter.cs
│   ├── BytesToImageConverter.cs
│   ├── RegionToFlagConverter.cs
│   ├── ByteArrayToBitmapConverter.cs
│   └── FileSizeFormatConverter.cs
├── Behaviors/
│   └── (any attached behaviors needed)
└── Utilities/                      (all reusable code from original)
    ├── Constants/                   (unchanged)
    ├── Settings/                    (AppSettings.cs)
    ├── Trophy/                      (unchanged)
    ├── System.IO.cs                 (unchanged)
    └── Helpers/                     (non-UI portions of Helper.cs)
```

### 8.2 MVVM Pattern

The current codebase has **zero separation of concerns** — Main.cs mixes UI, business logic, and data access in one 5300-line file. WPF migration should use MVVM:

- **Models:** `PkgFileInfo`, `PkgEntryInfo`, `TrophyEntryInfo` — observable objects with `INotifyPropertyChanged`
- **ViewModels:** One per view/window, exposing `ICommand` properties for actions, `ObservableCollection<T>` for lists
- **Views:** XAML with data binding, data templates, control templates
- **Services:** Injected into ViewModels via constructor injection (manual DI or Microsoft.Extensions.DependencyInjection)
- **Commands:** Use `RelayCommand` or `DelegateCommand` instead of Click event handlers

### 8.3 Dark Theme Strategy

Since DarkUI is WinForms-only, the WPF dark theme must be rebuilt:

1. Create `DarkTheme.xaml` ResourceDictionary with:
   - Color palette: Background `#3C3F41`, Surface `#3C3F41`, Text `#DCDCDC`, Selection `#2D6EC9`
   - Styles for: Button, TextBox, ComboBox, CheckBox, DataGrid, ListView, TreeView, TabControl, Menu, ContextMenu, StatusBar, GroupBox, ProgressBar
   - Control templates for consistent look
2. Apply via `App.xaml` merged dictionaries
3. Use `DynamicResource` for runtime theme switching support (future-proofing)

### 8.4 DataGrid (PKG List) Implementation

The 15-column PKG grid is the most complex UI element. In WPF:

```xml
<DataGrid ItemsSource="{Binding PkgFiles}" 
          AutoGenerateColumns="False"
          SelectionMode="Extended"
          SelectionUnit="FullRow">
    <DataGrid.Columns>
        <DataGridTextColumn Header="Filename" Binding="{Binding Filename}" />
        <DataGridTextColumn Header="Title" Binding="{Binding Title}" />
        <DataGridTextColumn Header="Title ID" Binding="{Binding TitleId}" />
        <DataGridTextColumn Header="Content ID" Binding="{Binding ContentId}" />
        <DataGridTemplateColumn Header="Region">
            <!-- Image for flag -->
        </DataGridTemplateColumn>
        <!-- ... 10 more columns -->
    </DataGrid.Columns>
</DataGrid>
```

Column visibility is bound to settings booleans via `BooleanToVisibilityConverter`.

### 8.5 Tab Control Mapping

| Old (WinForms) | New (WPF) |
|---------------|-----------|
| `flatTabControl1` + 7 `TabPage` | `TabControl` with 7 `TabItem` |
| `flatTabControlBgi` inside tabPage3 | Nested `TabControl` inside tab item |
| Each tab page's controls | `UserControl` per tab, composed in XAML |

### 8.6 Async Pattern Upgrade

Replace `BackgroundWorker` with `async/await` + `Task`:

| Old | New |
|-----|-----|
| `BackgroundWorker.DoWork` | `await Task.Run(() => { ... })` |
| `BackgroundWorker.RunWorkerCompleted` | Code after `await` or `ContinueWith` |
| `BackgroundWorker.ReportProgress` | `IProgress<T>` |
| `this.Invoke((MethodInvoker)delegate {...})` | `Dispatcher.InvokeAsync(() => {...})` or proper data binding |
| `CheckForIllegalCrossThreadCalls = false` | **Remove entirely** — WPF enforces thread safety |

---

## 9. COMPLETE FEATURE CATALOG

### 9.1 PKG File Management

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 1 | Scan directories for PKG files | `LoadPKGGridView()` / `LoadPKGListView()` | Medium |
| 2 | Validate PKG via magic bytes | `Helper.PKG.GetPkgHeaderBuffer()` + 5 header patterns | Low |
| 3 | Read PKG metadata (title, IDs, version, etc.) | `PS4_Tools.PKG.SceneRelated.Read_PKG()` | Low (library call) |
| 4 | Display PKG in data grid (15 columns) | `PKGGridView` DataTable binding | Medium |
| 5 | Display PKG in list view (grouped by title ID) | `PKGListView` with groups | Medium |
| 6 | Sort PKG grid by column | `PKGGridView.Sort()` | Low |
| 7 | Filter PKG by category (Game/Patch/Addon/App) | `DataView.RowFilter` | Low |
| 8 | Search/filter by text | `DataView.RowFilter` with LIKE | Low |
| 9 | Switch between GridView and ListView | `toolStripMenuItem5_Click` / `toolStripMenuItem6_Click` | Low |
| 10 | Refresh PKG list | `RefreshPkgList()` | Low |
| 11 | Delete PKG files | `DeletePkg()` | Low |
| 12 | View PKG file in Windows Explorer | `ViewPKGInExplorer()` → `Process.Start("explorer", "/select," + path)` | Low |
| 13 | Show drive free space in status bar | `GetDrivesFreeSpace()` | Low |

### 9.2 PKG Detail Information

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 14 | Display PKG icon | `ShowPackageIcon()` → `pictureBox1.Image` | Low |
| 15 | Display PKG title | `darkLabel1.Text` / `UpdateFormTitle()` | Low |
| 16 | Display PARAM.SFO data | `UpdateParamInfoGrid()` → `darkDataGridView2` | Low |
| 17 | Display PKG header info | `LoadHeaderInfo()` → `dgvHeader` | Low |
| 18 | Display PKG entries (file listing) | `LoadPKGEntries()` → `dgvEntryList` | Medium |
| 19 | Display PubTool info | `LoadPubToolInfo()` → `darkDataGridView4` | Low |
| 20 | Show background images (PIC0/PIC1) | `LoadBackgroundImages()` → `pbPIC0`/`pbPIC1` | Low |
| 21 | Save background image to file | `SaveBackgroundImage()` | Low |
| 22 | Set background image as desktop wallpaper | `SetImageAsDesktopBackground()` → Win32 `SystemParametersInfo` | Low |
| 23 | Context menu on background images | `contextMenuBackgroundImage` | Low |

### 9.3 Trophy System

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 24 | Extract trophy TRP file from PKG | `ExtractTrophyFile()` | Medium |
| 25 | Load trophy entries from TRP | `LoadTrophyInfo()` → `TRPReader` | Medium |
| 26 | Display trophy entries (icon, name, size) | `TrophyGridView` with image column | Medium |
| 27 | Extract trophy icons to folder | `ExtractTrophyIcon()` | Low |
| 28 | Resize trophy images for display | `Helper.Trophy.ResizeImage()` | Low |

### 9.4 PKG File Explorer (tabPage7)

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 29 | List PKG internal files via orbis-pub-cmd | `PopulatePKGDataToTreeView()` | Medium |
| 30 | Display file tree (TreeView + ListView) | `PKGTreeView` + `listView1` with split container | High |
| 31 | Navigate tree directories in ListView | `HandleListViewActivation()` / `PopulateListView()` | High |
| 32 | Extract selected files/folders | `ExtractSelectedPKGData()` → orbis-pub-cmd | Medium |
| 33 | Extract full PKG | `ExtractFullPKG()` → orbis-pub-cmd | Medium |
| 34 | Search in file tree | `SearchFileInTreeView()` | Low |
| 35 | Expand/collapse all tree nodes | `PKGTreeView.ExpandAll()` / `CollapseAll()` | Low |
| 36 | Passcode input for encrypted PKGs | `tbPasscode` / `PKG.Passcode` | Low |
| 37 | Context menu on tree/list items | `contextMenuExtractNode` / `contextMenuExtractListView` | Low |

### 9.5 PKG Entry Extraction

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 38 | Extract all entries | `ExtractAllEntry()` → PkgReader + SubStream | Medium |
| 39 | Extract decrypted entries | `ExtractDecryptedEntry()` → AES/RSA decryption | High |
| 40 | Error handling for encrypted entries | `errorEncryptedEntries` list + warning dialog | Low |

### 9.6 Image & Icon Extraction

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 41 | Extract all (PIC0 + PIC1 + ICON) | `ImageIconExtractor("ALL", ...)` | Low |
| 42 | Extract images only (PIC0 + PIC1) | `ImageIconExtractor("IMAGE", ...)` | Low |
| 43 | Extract icon only | `ImageIconExtractor("ICON", ...)` | Low |
| 44 | Global vs selected extraction | `GetSelectedPKGDirectoryList(ALL/SELECTED)` | Low |
| 45 | Respective folder extraction dialog | Yes/No/Cancel dialog before extraction | Low |
| 46 | Post-extraction status messages | `ImageIconPostExtraction()` | Low |

### 9.7 PKG Rename (11 formats × 2 scopes)

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 47 | Rename all by TITLE | `RenamePKG("{TITLE}", ALL)` | Medium |
| 48 | Rename all by TITLE [TITLE_ID] | `RenamePKG("{TITLE} [{TITLE_ID}]", ALL)` | Medium |
| 49 | Rename all by TITLE [TITLE_ID] [APP_VERSION] | ... | Medium |
| 50 | Rename all by TITLE [CATEGORY] | ... | Medium |
| 51 | Rename all by TITLE_ID | ... | Medium |
| 52 | Rename all by TITLE_ID [TITLE] | ... | Medium |
| 53 | Rename all by [TITLE_ID] [CATEGORY] [APP_VERSION] TITLE | ... | Medium |
| 54 | Rename all by TITLE [CATEGORY] [VERSION] | ... | Medium |
| 55 | Rename all by CONTENT_ID | ... | Medium |
| 56 | Rename all by CONTENT_ID2 | ... | Medium |
| 57 | Rename all by custom format | Uses `appSettings_.RenameCustomName` | Medium |
| 58 | Rename selected (same 11 formats) | `RenamePKG(format, SELECTED)` | Medium |
| 59 | Actual file rename + grid update | `UpdatePKGFilename()` → `File.Move` + cell update | Low |

### 9.8 PKG Move/Organize

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 60 | Move by PKG title (create title folders) | `MovePKGByTitle()` | Medium |
| 61 | Move by category (Game/Patch/Addon folders) | `MovePKGByCategory()` | Low |
| 62 | Move by type (Fake/Official/Addon_Unlocker) | `MovePKGByType()` | Low |
| 63 | Move by region (EU/US/JAPAN/etc. folders) | `MovePKGByRegion()` | Low |
| 64 | Progress tracking per file | `toolStripStatusLabel2` + `toolStripProgressBar1` | Low |

### 9.9 Excel Export

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 65 | Export all PKG to Excel | `GenerateDatatableFromSelectedPKG(ALL)` + ClosedXML | Medium |
| 66 | Export selected PKG to Excel | `GenerateDatatableFromSelectedPKG(SELECTED)` + ClosedXML | Medium |
| 67 | Region icon → text conversion for export | `ConvertImageToRegion()` | Low |

### 9.10 Official Updates

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 68 | Check for official update | `PS4_Tools.PKG.Official.CheckForUpdate(TITLEID)` via API | Medium |
| 69 | Display update parts (URL, size, hash) | `dgvUpdate` DataTable | Low |
| 70 | Display update metadata (version, size, type, etc.) | Multiple `darkLabel` controls | Low |
| 71 | Copy update URLs to clipboard | `copyURLToolStripMenuItem_Click()` | Low |
| 72 | Download selected update part | `DownloadSelectedOfficialUpdate()` → WebClient async | Medium |
| 73 | Cancel download | `CancelDownloadingFile()` → `WebClient.CancelAsync()` | Low |
| 74 | Progress bar during download | `WebClient.DownloadProgressChanged` event | Low |
| 75 | Open folder after download | `Process.Start(directory)` | Low |

### 9.11 Remote PKG Installer (PS4 Communication)

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 76 | RPI requirement check | `PKGSENDER.CheckRequirement()` | Medium |
| 77 | Node.js + http-server validation | Registry check + file existence | Medium |
| 78 | Check if PKG installed on PS4 | `PKGSENDER.CheckIfPkgInstalled()` → curl API | Medium |
| 79 | Send PKG to PS4 | `SendPKG()` → rename → run server → curl → monitor | High |
| 80 | Monitor transfer progress | `MonitorPKGSenderTask()` → poll `get_task_progress` API | High |
| 81 | Stop current transfer | `PKGSENDER.StopTask()` → curl stop API | Medium |
| 82 | Uninstall base PKG from PS4 | `UninstallBasePkgFromPs4()` → check exists → uninstall | Medium |
| 83 | Uninstall patch PKG from PS4 | `UninstallPatchPkgFromPs4()` | Medium |
| 84 | Uninstall DLC from PS4 | `UninstallDlcPkgFromPs4()` | Medium |
| 85 | Uninstall theme from PS4 | `UninstallThemePkgFromPs4()` | Medium |
| 86 | RPI status indicator in menu | `toolStripMenuItem18.Text` dynamic update | Low |
| 87 | UI enable/disable during RPI operations | `DisableControls_PkgSender()` / `EnableControls_PkgSender()` | Low |
| 88 | Tab disable during RPI | `DisableTabPages()` / `EnableTabPages()` | Low |

### 9.12 Backport Tracking

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 89 | Mark PKG as backported | `Backport_Click()` → set cell to "Yes" | Low |
| 90 | Set backport remarks | `Backport_Click()` → set cell to custom text | Low |
| 91 | Remove backport label | `Backport_Click()` → set cell to "No" | Low |
| 92 | Persist backport data to JSON | `Helper.Backport.SaveData()` | Low |
| 93 | Load backport status on scan | `Helper.Backport.CheckPKGBackported()` | Low |

### 9.13 PS5 Backward Compatibility

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 94 | Download PS5 BC JSON from GitHub | `btnDownloadPS5BCJson_Click()` → `Tool.DownloadFileFromUrlAsync()` | Low |
| 95 | Show BC info in grid columns (PSVR/Neo/PS5BC) | `LoadPKGGridView()` → parse JSON per title ID | Medium |
| 96 | Toggle BC columns visibility | `cbPs5BcCheck_CheckedChanged()` → `Refresh = true` | Low |
| 97 | Show last download date | `labelPs5BcJsonDownloadDate` | Low |

### 9.14 Background Music

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 98 | Extract AT9 BGM from all PKGs | `Helper.BGM.ExtractBgm()` → PkgReader → .AT9 files | High |
| 99 | Play BGM when PKG selected | `PlayBGM()` → `Helper.BGM.PlayAt9()` → SoundPlayer | Medium |
| 100 | Toggle BGM playback | `appSettings_.PlayBgm` → `SetBackgroundMusicVolume()` | Low |
| 101 | System volume mute/unmute | `waveOutSetVolume()` Win32 API | Low |

### 9.15 Copy to Clipboard

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 102 | Copy TITLE_ID | `CopyTitleID()` → `Clipboard.SetText()` | Low |
| 103 | Copy CONTENT_ID | `CopyContentID()` → `Clipboard.SetText()` | Low |

### 9.16 UI Personalization

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 104 | Color-code PKG rows by category | `UpdatePKGColorLabel()` → per-row ForeColor/BackColor | Low |
| 105 | Custom colors for Game/Patch/Addon/App | `ProgramSetting` color pickers | Low |
| 106 | Show/hide grid columns | `UpdateDataGridViewColumnVisibility()` | Low |
| 107 | 11 toggleable columns | Settings checkboxes → 11 bool properties | Low |
| 108 | Custom naming format builder | `ProgramSetting` placeholder dropdown + textbox | Low |
| 109 | Live naming format preview | `tbCustomNamePattern_TextChanged()` → `darkLabelNamingPatternExample` | Low |

### 9.17 Application Features

| # | Feature | Implementation Location | Complexity |
|---|---------|------------------------|------------|
| 110 | Auto-update check (GitHub) | `GitHubUpdate.UpdateChecker` → `UpdateNotifyDialog` | Low |
| 111 | About dialog | `ShowInformation(version + copyright)` | Low |
| 112 | Donate link | `Tool.OpenWebLink("https://ko-fi.com/pearlxcore")` | Low |
| 113 | Duplicate PKG detection | `FindDuplicatePKG()` → compare columns 1-8 | Low |
| 114 | View PKG changelog (changeinfo.xml) | `ViewUpdateChangelog()` → orbis-pub-cmd extract → XML viewer | Medium |
| 115 | DLC/Addon store viewer | `DLC` form → `PS4_Tools.PKG.Official.Get_All_Store_Items()` | Medium |
| 116 | Open AppData in Explorer | `OpenTempDirectory()` | Low |
| 117 | First-launch directory config | `FirstLaunch` flag → `PKG_Directory_Settings` form | Low |
| 118 | Settings persistence | `SaveSettings()` on form close | Low |

---

## 10. MIGRATION RISK ASSESSMENT

### High Risk

| Area | Risk | Mitigation |
|------|------|------------|
| **DarkDataGridView → DataGrid** | DarkUI DataGridView has custom styling and behavior; WPF DataGrid is fundamentally different | Use DataGrid with custom column templates; plan for significant XAML work |
| **TreeView + ListView dual-pane explorer** | Complex custom navigation logic in `PopulateListView()`, `HandleListViewActivation()`, `listView1_ItemActivate()` | Can be simplified in WPF with proper data binding and hierarchical templates |
| **BackgroundWorker-heavy code** | ~30+ BackgroundWorker instances scattered throughout code | Replace with async/await + Task.Run; careful testing needed for cancellation logic |
| **Static state everywhere** | `Helper.PKG.*`, `Helper.PKGSENDER.*`, `Helper.Trophy.*` are all static — thread safety issues, testing impossible | Refactor into injectable services with interfaces; keep state scoped to ViewModels |
| **Cross-thread UI manipulation** | `CheckForIllegalCrossThreadCalls = false` is set globally, and many Invoke calls exist | WPF enforces UI thread — remove the flag, use Dispatcher properly |

### Medium Risk

| Area | Risk | Mitigation |
|------|------|------------|
| **DarkUI theme** | No DarkUI equivalent in WPF; dark theme must be built from scratch | Create comprehensive ResourceDictionary; start with MaterialDesignInXaml or similar as base |
| **VisualStudioTabControl** | Custom tab control behavior | WPF TabControl with restyled template is sufficient |
| **Form.ShowDialog() flow** | PKG Directory Settings → Main form navigation uses Show/Hide pattern | Use Window.ShowDialog() and Window.Show() consistently |
| **PictureBox image handling** | Image loading/conversion code tightly coupled to WinForms Bitmap/Image | WPF uses BitmapSource; wrapper needed for byte[] → ImageSource conversion |
| **Clipboard access** | Uses `System.Windows.Forms.Clipboard` | Switch to `System.Windows.Clipboard` |
| **System.Media.SoundPlayer** | Used for BGM playback | Still available in WPF, but consider NAudio or similar for better AT9 support |

### Low Risk

| Area | Risk | Mitigation |
|------|------|------------|
| **MessageBox/Dialogs** | DarkMessageBox → standard WPF MessageBox or custom | Custom styled WPF dialog or use MessageBox for simplicity |
| **FolderBrowserDialog** | WinForms dialog — still works in WPF | Can use Windows API Code Pack's CommonOpenFileDialog for better UX |
| **All non-UI code** | Business logic, PS4 libraries, Trophy readers — no changes needed | Just reference from new WPF project |
| **Settings system** | Flat-file key=value — no dependency on WinForms | Reusable as-is |

---

## 11. RECOMMENDED MIGRATION PLAN

### Phase 1: Foundation (Week 1-2)
1. Create new WPF project in same solution
2. Set up project references to all reusable libraries (PS4_Tools, etc.)
3. Copy all non-UI code files (Utilities/, Constants, Settings, Trophy, Helpers)
4. Build dark theme ResourceDictionary
5. Create MVVM infrastructure (RelayCommand, ViewModelBase, ServiceLocator/DI)
6. Port `AppSettings`, `SettingsManager`, `Logger`, `DialogHelper`, `MessageBoxHelper` as services

### Phase 2: Core Models & Services (Week 2-3)
1. Create `PkgFileInfo` observable model
2. Create `PkgScanService` (extract scanning logic from LoadPKGGridView)
3. Create `PkgDetailService` (extract detail loading from LoadPKGDetails)
4. Create `SettingsService` wrapping SettingsManager
5. Create `RpiService` from Helper.PKGSENDER
6. Create `TrophyService` from Trophy extraction logic
7. Create `BgmService` from BGM logic
8. Create `UpdateService` from official update logic
9. Create `ExcelExportService` from Excel export logic

### Phase 3: ViewModels (Week 3-4)
1. `MainViewModel` — orchestrates all tabs
2. `PkgListViewModel` — grid data, filtering, sorting, selection
3. `PkgDetailViewModel` — PARAM, header, entries, pub tool info
4. `TrophyViewModel` — trophy list
5. `PkgExplorerViewModel` — tree + list navigation (simplify the current logic)
6. `OfficialUpdateViewModel` — update check + download
7. Remaining ViewModels for settings, DLC, change info

### Phase 4: Views (Week 4-6)
1. `MainWindow.xaml` — shell with menu, status bar, tab control
2. `PkgListView.xaml` — DataGrid with all columns, context menu
3. `PkgDetailView.xaml` — icon + title + PARAM grid
4. `TrophyView.xaml` — trophy grid with images
5. `BackgroundImageView.xaml` — images with context menu
6. `PkgContentsView.xaml` — entry list + header + pub tool grids
7. `PkgExplorerView.xaml` — tree + list split view
8. `OfficialUpdateView.xaml` — update parts list + metadata
9. `DlcViewerWindow.xaml`, `PkgChangeInfoWindow.xaml`
10. `PkgDirectorySettingsWindow.xaml`, `ProgramSettingsWindow.xaml`

### Phase 5: Integration & Polish (Week 6-7)
1. Wire all menu/toolbar commands to ViewModel ICommands
2. Implement progress reporting via IProgress<T>
3. Implement context menus
4. Implement drag-drop for directory adding
5. Test all async operations with cancellation
6. Error handling review
7. Performance optimization (virtualization for large PKG lists)

### Phase 6: Replacements for DarkUI-specific behavior
1. Custom DarkMessageBox window
2. Dark-themed FolderBrowserDialog alternative (or use Ookii.Dialogs.Wpf)
3. Verify all data binding works correctly
4. Accessibility review

---

## 12. CRITICAL CODE SMELLS TO FIX DURING MIGRATION

1. **`CheckForIllegalCrossThreadCalls = false`** — Removes all thread safety. In WPF, use proper `Dispatcher.Invoke`/`BeginInvoke` or data binding (which auto-marshals).

2. **5300-line Main.cs** — Split into ~10 ViewModels + ~7 Views + ~9 Services.

3. **Static mutable state everywhere** — `Helper.PKG.SelectedPKGFilename`, `Helper.PKGSENDER.JSON.SENDPKG.status`, etc. are thread-unsafe and untestable. Use scoped service instances with proper state management.

4. **BackgroundWorker proliferation** — 30+ BackgroundWorkers with inline delegates. Replace with `async/await` + `CancellationToken` + `IProgress<T>`.

5. **String-based PKG type checking** — `ps4Pkg.PKG_Type.ToString() == PKGCategory.GAME` compares strings instead of using an enum. The library should expose an enum; if not, wrap it.

6. **Empty catch blocks** — Many `catch { }` or `catch (Exception ex) { }` blocks silently swallow errors. Add proper logging at minimum.

7. **Hardcoded title ID** — `PS4_Tools.PKG.Official.Get_All_Store_Items("CUSA07022")` in DLC flow uses a hardcoded title ID (should be the selected PKG's title ID).

8. **Unused code** — Commented-out code blocks, unused fields (`pkgFile` MemoryMappedFile, `send_pkg_json`, `filenameDLC`, etc.), dead methods (`LogToTextBox`, old `LoadTrophies`).

9. **Duplicate scanning logic** — `LoadPKGGridView()` and `LoadPKGListView()` duplicate the entire directory scanning and PKG verification logic (~200 lines duplicated).

10. **No separation of concerns** — UI thread manipulation, business logic, file I/O, and network calls all interleaved in event handlers.

11. **SettingsManager save bug** — `if (!File.Exists(filePath)) File.Create(filePath);` — `File.Create()` returns a `FileStream` that is never disposed, causing a file lock. Should be `File.Create(filePath).Dispose();` or just remove the line (StreamWriter creates the file).

12. **Resource leaks** — `NotifyIcon` created and disposed immediately in `ShowTaskbarNotification()`, but `BalloonTipClicked` event handler is never removed. `WebClient` instances in `Helper.Update` may not be disposed properly.

---

## 13. NUGET PACKAGES TO ADD FOR WPF

| Package | Purpose |
|---------|---------|
| `Microsoft.Xaml.Behaviors.Wpf` | Event-to-command binding, UI behaviors |
| `Microsoft.Extensions.DependencyInjection` | DI container (optional but recommended) |
| `Ookii.Dialogs.Wpf` | Better folder browser dialog |
| `MaterialDesignThemes` or `ModernWpf` | Optional: jumpstart dark theme instead of building from scratch |

---

## 14. SUMMARY STATISTICS

| Metric | Current (WinForms) | Target (WPF) |
|--------|-------------------|--------------|
| Forms/Windows | 5 | 5 + ~7 UserControls |
| Lines of code (Main form) | ~5,300 | Split across ~10 files, 200-400 lines each |
| BackgroundWorkers | ~30 instances | Replaced by async/await |
| Static state classes | 20+ nested classes in Helper | ~9 injectable services |
| UI thread violations | Flag disabled + manual Invoke | Proper Dispatcher + bindings |
| Theme/Controls | DarkUI (3rd party) | Custom WPF ResourceDictionary |
| Data binding | Manual cell population | XAML data binding |
| Reusable code | ~60% (non-UI portions) | All reused as-is |
| New code needed | — | ~3,000-5,000 lines of XAML + ~3,000 lines of C# (ViewModels/Services) |
