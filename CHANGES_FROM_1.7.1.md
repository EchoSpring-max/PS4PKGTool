# PS4 PKG Tool — Complete Changes Since v1.7.1

Date: 2026-08-14 · Branch: `feature/asset-framework` (57 commits, unreleased)
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
- Program Settings reorganized into clean tabs (General / Appearance / Columns / Library / shadPS4 / Remote PKG Installer / PKG Rename / Trophies) — Designer-only, no code-behind changes
- Active Core / Active Launcher model: `managed:<buildId>` or `adopted:<path>`, with one-time migration from legacy settings
- Install-directory setting, auto-filled from shadPS4's own config (never hardcoded), user-overridable

### Installing games to shadPS4 libraries
- Transactional install: staging inside the library → validation → atomic rename; existing installs protected
- Correct dump layout: Image0 contents at the folder root, Sc0 metadata merged into `sce_sys\`, folder named by Title ID (CUSAxxxxx)
- Patch (update) install: merges into the existing base folder (patch files overwrite, base-only files remain); patch-before-base also works
- Warnings and confirmations: patch without base game detected, game with no known compatibility status, merge/replace confirmation with versions, always-ask install confirmation
- Skips the folder picker when the install directory is set

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
- Managed versioned builds under `%LOCALAPPDATA%\PS4PKGTool\shadPS4\builds\` (`core-<commit>\`, `launcher-<commit>\`) with per-build `ps4pkgtool-manifest.json`
- Rollback by switching active build — old builds never deleted
- Manage Builds dialog: Make Active / Open Folder / Choose Core Version / Install shadPS4
- Manual Check for Updates (Core + QtLauncher independently; never auto-installs or auto-activates)
- Full setup reset (removes only PS4PKGTool-owned state; shadPS4's config/saves/games untouched)
- Three separate official upstream feeds (shadPS4 stable, shadps4-binaries-Windows nightlies, QtLauncher) with JSON cache and GitHub's official sha256 digests

### Safety & quality
- Strict read-only principle: PS4PKGTool never writes `%APPDATA%\shadPS4\config.json` or `.toml`
- No hardcoded machine paths (enforced by tests)
- Streaming downloads (.part files, cancellation cleanup), hardened ZIP extraction (zip-slip, traversal, rooted/UNC, symlinks, case-insensitive duplicate paths), free-space checks before download AND extract, AV-quarantine message
- Comprehensive logging for every shadPS4 operation — including orbis-pub-cmd stderr capture, so real extraction errors are visible in the Log tab and error dialogs

---

## 4. General Improvements & Fixes

- All dialogs now use the app's dark message box (no native popups; no hyphen characters in dialog text)
- Fixed PKG directory-list pollution when moving many PKGs (226 entries collapsed back to 1 root)
- Fixed a crash when opening Manage Builds with an empty build list (DarkListBox empty-draw guard)
- Package move/rename improvements (title grouping, base+update/addon folders)

---

## 5. Testing

- 121 app tests + 58 asset tests, all passing, solution builds with 0 errors
- New test coverage: shadPS4 detection/config parsing, active-core resolution and migration, launch resolution (by ID / by path / already running), install service (staging, merge, replace, space, cancel), setup pipeline (download, ZIP safety, disk-full, quarantine), feed parsing/cache/rate-limits, managed store (commit/list/rollback/reset), settings round-trips
- Upstream verification documented in `shadps4_upstream_verification.md`; implementation completion report in `shadps4_setup_completion_report.md`

---

## Not included (deliberately deferred)

- Auto-updates / auto-activation of shadPS4 builds
- Per-game core selection ("Launch With >")
- Cheat / patch / graphics / controller management (QtLauncher's role)
- Writing shadPS4's own configuration
