# PS4 PKG Tool — Complete Changes Since v1.7.1

Date: 2026-08-18 · Branch: `feature/asset-framework` (235 commits, unreleased)
All changes below are present in the Debug build; no release has been published yet.

---

## 1. Asset Framework (in-app game asset viewing)

### File preview
- Generic format handlers wired into the app's preview pane: PNG, JPEG, BMP, GIF, DDS, WAV, OGG, text
- Texture contact sheet preview + resS companion extraction
- PNG texture export (exporter + "Export Textures" button)
- **Fixed: black text previews** — root cause was self-describing patch-pak blobs with embedded 53-byte record prefixes; text entries are now detected and stripped correctly

### Container/format parsers (all self-written, validated against real PS4 games)
- Unity: bundle container (UnityFS / UnityRaw / UnityWeb) + member browsing; serialized-file parser with Texture2D layout (validated on Overcooked 2 PS4)
- Unreal: PAK container parser (validated on CODE VEIN); PAK v8 + compressed entries + two-section index; Unreal package parser + UTexture2D metadata; mip bulk probing for streaming payloads
- PS4-native: GNF texture and ATRAC9 audio metadata
- Asset workspace UI: browse container children in the File Browser viewer, real texture names, robust Back-to-list navigation
- Asset framework core: interfaces, format detection, IO, error handling, acceptance tests

---

## 2. shadPS4 Compatibility Display

- "ShadPS4 (Windows)" column in the main grid showing official compatibility statuses (Playable / In-Game / Menus / Boots / Nothing)
- User-selectable compatibility OS in Settings (Windows / Linux / macOS)
- Compatibility database download reworked: primary source is now the upstream project's aggregated JSON release asset (one CDN download instead of many rate-limited API calls); falls back to scraping the source issues when the asset is unavailable; cache shape unchanged
- "ShadPS4" added as a group-by option in the grouped view
- Compatibility filter (type + status + search compose into one row filter)
- Semantic status sorting (Playable > In-Game > Menus > Boots > Nothing)
- Fixed the startup column-name mismatch that broke manifest loads

---

## 3. shadPS4 Integration

### Detection & settings
- Full environment detection: core vs QtLauncher classification, portable `user\` vs `%APPDATA%\shadPS4` config, legacy config.toml fallback, warnings, path normalization, exe validation
- Reworked to the verified current model: single selected executable + shared config reading (JSON config.json, System.Text.Json DTOs)
- Fixed launcher-in-subfolder layouts (core detected one level up, with version-mismatch warning)
- Active Core / Active Launcher model: `managed:<buildId>` or `adopted:<path>`, with one-time migration from legacy settings
- Install-directory setting, auto-filled from shadPS4's own config (never hardcoded), user-overridable
- Optional orbis-pub-cmd temp directory setting (`--tmp_path`, its internal scratch space) — user-selectable in settings, validated, empty means the normal %TEMP% default

### Installing games to shadPS4 libraries
- Transactional install: staging inside the library → validation → atomic rename; existing installs protected
- Correct dump layout: Image0 contents at the folder root, Sc0 metadata merged into `sce_sys\`, folder named by Title ID (CUSAxxxxx)
- Patch (update) install: merges into the existing base folder (patch files overwrite, base-only files remain); patch-before-base also works
- Warnings and confirmations: patch without base game detected, game with no known compatibility status, merge/replace confirmation with versions, always-ask install confirmation
- Skips the folder picker when the install directory is set
- Extraction path routing: an unsafe PKG path is temporarily relocated through the crash-recoverable ASCII-safe temp-rename; an unsafe output path is routed through a same-volume ASCII workspace and moved into the requested staging folder after extraction

### Launching games
- **Active-core model**: launch uses ONLY the explicitly configured core; a core discovered in a parent folder is never auto-selected (fixes the old-core crash incident) — external cores require explicit approval
- Launch works for games in shadPS4 libraries (by Title ID) AND games in the tool's own install directory (boots by explicit `-g <eboot>` path)
- "shadPS4 is already running" guard (no second instance)
- Launch confirmation shows the core version ("shadPS4 launched [v0.17.0]")
- Core runs from its own folder (working-directory fix)
- Removed the extract-and-boot fallback for uninstalled games (single warning + install hint)
- Open QtLauncher (explicit only — the launcher is never part of Play)

### First-run setup + version management
- Setup wizard: Install Recommended (latest stable core + optional QtLauncher) / Choose Core Version (real upstream metadata: channel, date, commit, asset) / Use Existing Installation
- Step 2: managed builds folder (chosen once) + install directory (default `<root>\Data`, browsable, or cleared) + QtLauncher option
- Managed versioned builds under `%LOCALAPPDATA%\PS4PKGTool\shadPS4\builds\` (`core-<commit>\`, `launcher-<commit>\`) with per-build `ps4pkgtool-manifest.json` (default root is now `<exe dir>\AppData\shadPS4`; `%LOCALAPPDATA%` was the legacy default — recorded roots keep working)
- Rollback by switching active build — old builds never deleted
- Version switching + rollback live in the shadPS4 Manager (Builds tab): Make Active / Open Folder / Choose Core Version / Install shadPS4
- Manual Check for Updates (Core + QtLauncher independently; never auto-installs or auto-activates)
- Full setup reset (removes only PS4PKGTool-owned state; shadPS4's config/saves/games untouched)
- Three separate official upstream feeds (shadPS4 stable, shadps4-binaries-Windows nightlies, QtLauncher) with JSON cache and GitHub's official sha256 digests

### shadPS4 Manager (tabbed hub) + surface reorganization
- New **shadPS4 Manager** window — one tabbed hub for operating shadPS4, opened from Tools > shadPS4 Manager, the PKG context menu and Settings
- Manager redesigned into a developer/test-control-center layout with five tabs: **Overview · Games · Builds · Saves · Settings**
  - **Overview** — current core/launcher cards (with Open Folder), quick actions (Install/Update, Open Game Library, Open shadPS4 Folder), counts, latest test reports per game (double-click opens the results viewer), full environment details with Copy, header bar with environment state and a "..." menu (config folder, managed builds, game library, copy info)
  - **Games** — installed games as a Details list with icons (Title / Title ID / Version / Last Test) and a selected-game pane: compatibility status with color, last test, results count, best profile, size, Recent Results, Launch, Add Feedback, Create Report, Results, Open Folder and a "..." menu (Add Report, Copy Game Information, Uninstall)
  - **Builds** — managed core + launcher builds on the SidebarPage (flattened, no wrapper panel): every build row carries an explicit **Release / Nightly** type label, the active card shows "● ACTIVE · Release|Nightly"; Activate / Open Folder / Remove (active build protected), Install Build..., Browse Builds..., Maintenance menu (Reset Managed Setup..., Open Managed Builds Folder)
  - **Saves** — savegame management for the shadPS4 user directory (see below), with a selected-save panel (title, user, path, "game not installed" note) and backup rows showing size + slot count
  - **Settings** — shadPS4 paths (adopt core/launcher with Browse, install directory saved immediately, Open Config Folder), Check for Updates, live detection report with warnings
  - Status strip: [shadPS4 Manager] | progress | status (idle label hidden)
  - shadPS4 configuration moved from Program Settings into the manager's Settings tab; Program Settings keeps only the compatibility section ("shadPS4 Compat") plus an "Open shadPS4 Manager..." button
- Surface split: **Manager = operate · Settings = configure · PKG context menu = per-game**
  - PKG right-click menu gets a flat "shadPS4" section header with Launch / Install to shadPS4 Library / Open shadPS4 Manager (renamed from "Install Game to shadPS4 Library")
  - Old Manage Builds dialog removed; all three entry points open the Manager
- Wording: external-component approval now asks "Adopt This Core?" / "Adopt This Launcher?"; "Install shadPS4 Setup..." simplified to "Install shadPS4..."

### Saves (browse / backup / restore)
- Saves tab in the shadPS4 Manager: three columns (saves / slots / backups) over the verified layout `<userDir>\home\<userID>\savedata\<TitleID>\<slot>\`; all emulated users with saves are listed, orphan saves (game not installed) included, titles resolved from the slot's own `sce_sys\param.sfo` (falls back to the Title ID)
- **Backup**: timestamped snapshot copies into `<managedRoot>\saves\<userId>\<TitleID>\<snapshotId>\` with a `manifest.json` (schema, snapshot id, title id, user id, UTC time, informational source path, consistency). Only folders with a valid manifest count as restore sources; ordering uses the manifest time, never folder names. Backups taken while the emulator runs are marked "(live)" in the list and warned about up front
- **Restore**: journaled and crash-recoverable — safety backup of the current save (hard precondition), staged copy beside the target, double emulator-running check (before and immediately before the swap), rename swap with synchronous rollback, and interrupted-restore recovery on the next Saves-tab visit (stale staging discarded, half-swaps completed or rolled back). Restore refuses while the emulator runs; free-space preflight on both volumes; reparse points are never traversed when copying
- Save browsing is fault-tolerant (one unreadable file or malformed SFO never hides a game)

### Crash capture (termination reporting)
- A background watcher keeps the launched core's process handle and classifies its exit when the emulator terminates: neutral categories (access violation, stack overflow, fail fast, breakpoint, DLL loader and entry-point errors, user termination) with the Windows STATUS_ code; exit 0 and Ctrl+C are never reported as crashes
- A dialog is shown for EVERY exit of a tool-launched core: "shadPS4 closed after ..." (Info, with target; unmapped exit codes also shown) or "shadPS4 terminated with error status ..." (Warning, with runtime, launch context and the last emulator log lines)
- WER evidence discovery, fully read-only: after an abnormal exit the WER report queue/archive is searched for a fresh AppCrash report (brief polling, werfault lags the exit) and the dialog offers to open the report folder; LocalDumps is only used when the machine already configured it; no report means nothing is invented
- Emulator log tail (last 200 lines, best-effort, read-only) attached to the report when a log file is found

### Safety & quality
- Strict read-only principle: PS4PKGTool never writes `%APPDATA%\shadPS4\config.json` or `.toml`
- No hardcoded machine paths (enforced by tests)
- Streaming downloads (.part files, cancellation cleanup), hardened ZIP extraction (zip-slip, traversal, rooted/UNC, symlinks, case-insensitive duplicate paths), free-space checks before download AND extract, AV-quarantine message
- Comprehensive logging for every shadPS4 operation — including orbis-pub-cmd stderr capture, so real extraction errors are visible in the Log tab and error dialogs

---

## 4. Settings UI Overhaul

- **Program Settings reorganized into 8 clean tabs grouped by concern** (Designer-only, no code-behind changes):
  - **General** — directories, downloads, startup, server settings
  - **Appearance** — theme + PKG color labeling only
  - **Columns** — grid column visibility checkboxes
  - **Library** — PS5 BC / PSVR / PS4 Pro checks
  - **shadPS4** — the whole shadPS4 section gets its own full page (checkbox, OS, compat data download, executable selection, detection info with warnings)
  - **Remote PKG Installer / PKG Rename / Trophies** — unchanged
- All page sizes unified (632x516); the form/tab height reverted to the original compact size — shadPS4 is no longer crammed into the Appearance tab
- Further shadPS4 tab rework: Active Core / QtLauncher rows with Browse-adopt, Manage Builds, Check for Updates, Open Config Folder, install-directory row

---

## 5. Filter Bar + Grouped List View

### Filter bar (main grid)
- Filter bar panel above the game list: Category, Region, PKG Type and ShadPS4 status as multi-select checkbox dropdowns (DarkCheckedComboBox), a "SysVer ≥" minimum-firmware box, and a game search box — every change applies in realtime (no Apply button)
- Active filters shown as removable chips (DarkChipsPanel) — clicking a chip's × clears just that filter; Clear resets everything
- Match counter ("matches x / y"); the grouped view carries its own filter indicator ("filtered: x / y") with a Clear of its own
- Filters compose into a single row filter over the grid table (title / type / category / region / status / version / search) and apply to both the Table and Grouped views
- Hidden helper columns (Region Name, System Version (Num)) back the region and minimum-firmware filters
- Responsive layout: one row of filters when maximized; wraps with row spacing at the 1150px minimum window width

### Grouped List View
- Table / Group tabs under the filter bar (subTabControl) switching between the grid and the grouped list
- Group by Title / Title ID / System Version / PKG Type / Category / ShadPS4 status, with group counts; empty groups bucket into "Unknown"

### Fixes
- **Fixed region data leaking into the ShadPS4 column** — positional row construction shifted when the column was added to the schema; all three load paths (manifest, directory scan, drag-drop) now assign columns by name, which also restored the Region and minimum-firmware filters

---

## 6. Game Feedback & Compatibility Reports

Casual test-result recording for shadPS4 testers, with optional one-click conversion into a shadPS4 compatibility issue — no bug-tracker machinery.

### Post-game dialog (rebuilt)
- The session dialog is now a quick tap-through: "How did it run?" with one-button status selection (Nothing / Boots / Menus / In-Game / Playable), a Notes box and **Save · Create Report · Skip**
- Save stays lightweight — one JSONL entry in the existing local test history, enriched automatically with session metadata (game, version, status, date, active core/launcher, shadPS4 build, OS, CPU/GPU, crash log tail)
- Create Report saves the result AND opens the report builder (no double-save, no replaying the session)

### Report builder
- One straightforward form (no wizard): game line, **shadPS4 version picker (official release versions only)**, status, description, error / relevant log, a 6-item opt-in checklist (release build, duplicate check, own dump, firmware modules, sync logging, default settings — nothing auto-ticked), session log association with Open, Preview, Copy Markdown with a "Report copied to clipboard." confirmation
- **Release-only enforcement**: the shadPS4 version is a dropdown over known official releases (stable feed cache + release-tagged managed core builds; nightlies and commit ids are never candidates); Preview / Copy Markdown stay disabled until a release version is picked
- Markdown generation is isolated from the UI (`CompatibilityReport`) so the template can be reworded later — shadPS4 issue structure (Checklist / Game Name / serial / version / emulator / status / OS / CPU / GPU / Error / Description) with GitHub placeholders for screenshots and the log file

### Entry points
- Games tab: **Add Feedback** and **Create Report** buttons per game; Reports renamed to **Results**; **Recent Results** mini-list (last three, newest first); manual Create Report pre-fills status/description from the latest saved result
- Installed games read their own `sce_sys\param.sfo` (minimal TITLE + APP_VER reader) for the version shown in the Games tab and to pre-fill the feedback form
- Results viewer: **Copy as Report** turns any saved result into a pre-filled report
- Overview "Latest Reports": double-clicking a row opens that game's results viewer
- Builds tab: every build row and the active-build state marker show **Release / Nightly**, so report-eligible builds are recognizable at a glance

---

## 7. General Improvements & Fixes

- **Fixed: main window tab area lifts off the status strip on maximize** — the responsive filter bar shrank to one row and moved the sub-tab control up without compensating its height; the tab control's bottom edge is now pinned to the panel edge at every window size
- **Games list columns are now designer-proof** — the column wiring moved from the Designer into the constructor; the VS visual designer repeatedly dropped `Columns.AddRange` on re-serialization, leaving a Details list with zero columns that rendered nothing (games looked "not loaded" but were present)
- All dialogs now use the app's dark message box (no native popups; no hyphen characters in dialog text) — status vocabulary keeps the project's existing "In-Game" spelling
- Fixed PKG directory-list pollution when moving many PKGs (226 entries collapsed back to 1 root)
- Fixed a crash when opening Manage Builds with an empty build list (DarkListBox empty-draw guard)
- Package move/rename improvements (title grouping, base+update/addon folders)

---

## 8. Mini PKG Viewer

A standalone read-only package inspector. Launching the app with one `.pkg` path on the command line opens it directly (ready for OS file-association); it reuses the main app's inspection, extraction and trophy pipelines.

### Layout
- Six tabs: **Overview** (package summary with the PARAM.SFO grid beside it), **Package** (Header / Build Info / Entries), **Trophy**, **Files** (file browser), **Artwork** (pic0 / pic1 previews)
- Menu bar mirroring the main app: **File** (Copy Title / Title ID / Content ID / Filename, Rename PKG with the same naming patterns, Exit), **Tools** (Save Artwork, Extract PKG, View Change Info, Download Official Update), **Help** (About, Buy me a coffee, Check for update)
- Status strip mirroring the main app: package path, always-visible progress bar, state text, and a **Stop Extract** button while extraction runs

### Single-package operations (same flows as the main app)
- Copy / Rename PKG / Delete: rename and delete operate through the crash-recoverable ASCII-safe temp-rename (`OrbisSafePkgOperation`)
- **Save Artwork** (icon + backgrounds as PNG), **Extract PKG** (full package via orbis-pub-cmd, same flow as the main app's Extract full PKG), **View Change Info**
- **Download Official Update**: reuses the main app's `OfficialUpdateForm` (Game / Patch PKGs only, same title-ID and category checks)

### File browser (mirror of the main app's)
- Tree + Details list with the same `tv_*` icon set, four columns (Name / Type / Path / Size), "..." up-navigation, whole-package search box, extract / copy context menu
- Passcode-protected packages: the passcode failure is detected from orbis-pub-cmd output and prompted for in a dark input dialog; the passcode is remembered for the session

### Extraction with cancellation
- **Stop Extract** mirrors the main app's stop button: a volatile stop flag plus killing the running orbis-pub-cmd; the status strip stays live while the rest of the viewer locks, and the temp-renamed package is always restored
- Progress bar is always visible and animates while work runs

### Trophy tab
- Decrypts and lists trophy data (icon, id, name, description, grade, hidden) through the shared trophy metadata pipeline
- **Auto-extracts the NP Communication ID on launch** (orbis-pub-cmd `Sc0/npbind.dat`, the same method as the main app's cache builder) into the shared `np-communication-ids.json` cache, so trophy decryption works the first time the tab is opened

### Robustness
- All grid columns resizable in both directions; designer-proof wiring (toolbar dropdowns, list columns and the SFO grid survive VS designer re-serialization)

---

## 9. Startup Argument Routing

- One `.pkg` path on the command line opens that package in the Mini PKG Viewer (file-association ready)
- Validated before launch: exactly one argument, `.pkg` extension, file exists, non-empty, accessible; invalid input shows a clear dark dialog instead of launching
- Wired in `Program.cs`; every branch covered by `StartupArgumentRouterTests`

---

## 10. Settings Durability

- Settings now persist in `%APPDATA%\PS4PKGTool\Settings.conf` (new `UserSettingsDirectory`) instead of next to the exe — a clean rebuild used to wipe the build-output AppData folder and silently reset every saved option; rebuilds, clean installs and moved folders can no longer lose them *(reversed: portability won — settings, feedback history and report snapshots now live in `<exe>\AppData\` with everything else, with a one-time reverse migration from `%APPDATA%\PS4PKGTool`; clean rebuilds/reset is the accepted tradeoff)*
- Game feedback history moved to the same durable location *(now also in `<exe>\AppData\`)*

---

## 11. Trophy Metadata Pipeline

- `TrophyMetadataResult` now classifies failures (`TrophyMetadataFailureKind`: missing metadata, unsupported payload, missing NP Communication ID, decryption failure, parse failure) so callers can tell "no trophy data" apart from "cannot decrypt"
- `NpbindExtractor` honors the configured orbis-pub-cmd temp path option

---

## 12. Testing

- 270 tests, all passing, solution builds with 0 errors
- New test coverage since the last changelog: package inspection services (metadata snapshot, entry lists, file listing with passcode handling), trophy inspection (ESFM decryption, failure classification), startup argument routing, shadPS4 save backup/restore (journaled swap, crash recovery), shadPS4 termination classification, game feedback recording, plus the previously listed shadPS4 detection/config parsing, active-core resolution and migration, launch resolution (by ID / by path / already running), install service (staging, merge, replace, space, cancel), setup pipeline (download, ZIP safety, disk-full, quarantine), feed parsing/cache/rate-limits, managed store (commit/list/delete/rollback/reset), settings round-trips, release-version collection (feed cache + installed builds, nightlies excluded), compatibility report Markdown template, backup metrics
- Upstream verification documented in `shadps4_upstream_verification.md`; implementation completion report in `shadps4_setup_completion_report.md`

---

## Not included (deliberately deferred)

- Auto-updates / auto-activation of shadPS4 builds
- Per-game core selection ("Launch With >")
- Cheat / patch / graphics / controller management (QtLauncher's role)
- Writing shadPS4's own configuration
