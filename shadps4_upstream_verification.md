# shadPS4 upstream verification (P0)

Verified 2026-08-14 against the official upstream repos and the user's real installs.
Re-verify tag/asset names at implementation time — nightlies change daily.

## 1. Feeds (three separate sources)

| Feed | Repository | Windows asset pattern | Notes |
|---|---|---|---|
| Core stable | `shadps4-emu/shadPS4` releases | `shadps4-win64-sdl-<ver>.zip` | v.0.14.0 (Feb) → v.0.15.0 (Mar) → v.0.16.0 (Jun) → v.0.17.0 (2026-07-30). `prerelease == false`. |
| Core nightly (Windows) | `shadps4-emu/shadps4-binaries-Windows` releases | `shadps4-win64-sdl-YYYY-MM-DD-<sha7>.zip` | Tag `Pre-release-shadPS4-YYYY-MM-DD-<sha40>`; **the short sha in tag/name is the real emulator commit** (matches the main repo's nightly of the same commit). Multiple builds per day. `prerelease == true`. |
| QtLauncher | `shadps4-emu/shadps4-qtlauncher` releases | `shadPS4QtLauncher-win64-qt-YYYY-MM-DD-<sha7>.zip` | Latest (2026-08-08) is a **prerelease**. Launcher-only zip — contains NO core. Older stable launcher v224 stated "for use with up to shadPS4 0.15.0". |

GitHub API exposes a **`digest: sha256:<hex>` on every asset** → official hashes exist; download verification by hash is legitimate (no invented hashes needed).

## 2. Archive layouts (confirmed from the user's real installs of these exact assets)

- Core zip: extracts `shadPS4.exe` + runtime folders (`AssetBundles`, `CSVFiles`, `Cutscenes`, `qtplugins`, ...) **at the zip root** — the user's `Desktop\SHADPS4\` folder is a raw extraction of `shadps4-win64-sdl-0.17.0.zip`.
- QtLauncher zip: extracts `shadPS4QtLauncher.exe` + `qtplugins` + `Data` **at the zip root** — the user's `Desktop\SHADPS4\Latest ShadPS4\` is a raw extraction of `shadPS4QtLauncher-win64-qt-2026-08-08-….zip`.

## 3. Core CLI and runtime behavior (src/main.cpp, main branch)

- Positional arg and `-g/--game` are equivalent: `shadPS4.exe <CUSA-id-or-path>` boots directly. No arg → "Please provide a game path or ID." + exit 1.
- A non-path arg is searched in `EmulatorSettings.GetGameInstallDirs()` via `FindGameByID(installDir, id, maxDepth=5)`; not found → "Game ID or file path not found: …" + exit 1.
- **The core does NOT change its working directory** (no `current_path`/`chdir` in main.cpp). Paths are absolute/config-derived. The app's `WorkingDirectory = exe dir` launch setting is not required by the core; keep it anyway (matches launcher spawn, costs nothing).
- Config/user-dir resolution lives in `common/path_util.h` (portable `user\` beside the exe, else `%APPDATA%\shadPS4`), consistent with earlier verification.

## 4. QtLauncher role and state (shadps4-qtlauncher, main branch)

- **Version Manager**: keeps a `versions.json` registry in `LauncherDir` and manages its own core downloads (diagnostic-only for us — the launcher can swap/delete those cores; never auto-adopt).
- **Cheats**: launcher writes per-game JSON (`<serial>_<version>.json`) into `CheatsDir` = the shared shadPS4 user directory. Persisted for the core to read at boot. Runtime application is additionally IPC-based in the launcher.
- **Patches**: launcher writes repo-organized XML (+ `files.json` index) into `PatchesDir` = shared user directory; toggling sets `isEnabled` in the XML.
- **Core applies patches itself at boot** (`src/common/memory_patcher.cpp`, main branch): `OnGameLoaded()` loads the XMLs from `PatchesDir` automatically, gated by `IsAutoPatchesLoadEnabled()`. → Patches configured in QtLauncher persist and are applied on a **direct core launch**, provided the core build has auto-patch-load enabled. (Launcher-side IPC patch application is a parallel/WIP path; the persisted-XML mechanism is the primary one.)
- **Graphics/audio/controller settings**: QtLauncher's settings dialogs edit the shared emulator config (`emulator_settings`/`user_settings`) which the core reads at boot → persist for direct launch. No CLI-argument injection for these.

## 5. Prerequisites and failure modes

- Windows builds require the **Microsoft Visual C++ 2022 runtime** (upstream quickstart). Missing runtime surfaces as a process start/load failure — worth a hint in wizard failure messaging.
- The user's real incident: launcher 2026-08-08 + parent-folder core v0.17.0 (07-30). v0.17.0 is the current STABLE; the launcher nightly is 9 days newer — version boundaries are real but no universal mapping is published for nightlies.

## 6. Consequences for the implementation

1. `CoreNightly` feed = `shadps4-binaries-Windows`; `CoreStable` = main repo; `QtLauncher` = launcher repo. Build ID = the **emulator commit sha** from the binaries tag.
2. Download verification uses GitHub's `digest` (sha256) — verified hashes, not invented ones.
3. "Recommended" core = latest CoreStable (v0.17.0 today). "Recommended" launcher = latest QtLauncher release, labeled "Upstream release type: Pre-release". Compatibility = `Unknown` (no authoritative pairing published).
4. Completion screen can truthfully say: emulator settings, cheats and patches configured in QtLauncher persist for direct core launch (shared user dir; patches applied by the core at boot when auto-load is enabled — verified main-branch; behavior may vary per build).
5. Launcher-managed cores: diagnostics only; external-core approval stores a path reference, never a copy.
6. Keep the `WorkingDirectory = core dir` launch setting (not required by main.cpp, harmless, matches launcher spawn).
