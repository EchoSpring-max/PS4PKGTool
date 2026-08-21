# shadPS4 First-Run Setup + Managed Builds — Completion Report (v1)

Date: 2026-08-14 · Branch: `feature/asset-framework` · Upstream verification: `shadps4_upstream_verification.md`

## 1. Files changed

**New (Utilities/Shadps4/):**
- `Shadps4ActiveCore.cs` — active core/launcher model (`managed:<id>` / `adopted:<path>`), never derived from disk proximity
- `Shadps4ReleaseFeed.cs` — official GitHub API client for three separate feeds + JSON cache (1 h TTL) + rate-limit handling
- `Shadps4Clock.cs` — `IClock`/`SystemClock` test seam
- `Shadps4RecommendationService.cs` — upstream policy (recommended = latest stable core; launcher labeled honestly)
- `Shadps4ManagedBuilds.cs` — versioned build store + `ps4pkgtool-manifest.json`
- `Shadps4Downloader.cs` — streaming download to `.part`, size + sha256 (GitHub digest) verification
- `SafeZipExtractor.cs` — ZIP-slip-hardened extraction + uncompressed-size estimate
- `Shadps4SetupService.cs` — transactional install pipeline

**New (Forms/Shadps4 Setup/):** `Shadps4SetupWizard` (+Designer), `Shadps4BuildManager` (+Designer), `Shadps4VersionPicker` (+Designer).

**Modified:** `Shadps4EnvironmentResolver.cs` (external-core warning), `Shadps4Launcher.cs` (working dir + process-info seam), `Utilities/Settings/AppSettings.cs` + `SettingsManager.cs` (new keys + one-time migration), `Forms/Program Settings/*` (shadPS4 tab rework), `Forms/Main/Main.cs` + `Main.Designer.cs` (launch gating, Open QtLauncher, Install shadPS4 Setup menu).

## 2. Services/classes added
See section 1. Key contracts: `IShadps4FeedClient` (test seam), `Shadps4SetupService.InstallBuildAsync(release, store, progress, ct)`, `Shadps4ManagedBuilds.Commit/ListBuilds/FindExe/ResolveManagedExecutable`, `Shadps4ActiveCore.ResolveExecutable(setting, managedHook, out error)`.

## 3. Settings added
`shadps4_active_core=` (`"" | managed:<id> | adopted:<path>`), `shadps4_active_launcher=` (same), `shadps4_managed_root=` (legacy default `%LOCALAPPDATA%\PS4PKGTool\shadPS4`; current default is `<exe dir>\AppData\shadPS4` — recorded roots always win). One-time migration from legacy `shadps4_core_exe`/`shadps4_launcher_exe`/`shadps4_executable` (by filename) — new keys never overwritten; legacy keys never consulted after migration persists.

## 4. Official upstream repositories/APIs used
- `api.github.com/repos/shadps4-emu/shadPS4/releases` (core stable)
- `api.github.com/repos/shadps4-emu/shadps4-binaries-Windows/releases` (core nightlies; the tag sha is the real emulator commit, used as build id)
- `api.github.com/repos/shadps4-emu/shadps4-qtlauncher/releases` (launcher; launcher-only zips)
- Asset `digest` field (official sha256) for download verification. No third-party mirrors, no hardcoded asset URLs, no invented hashes.

## 5. Managed/adopted model
- **Managed** = downloaded by PS4PKGTool into `<managed root>\builds\{core|launcher}-<buildId>\`, never deleted/overwritten; installed alongside older builds.
- **Adopted** = user-chosen existing install stored as a path **reference only** (`adopted:<path>`); never modified, never updated in place. Adoption requires explicit user approval (`[Use This Core]`/`[Use This Launcher]`).

## 6/7. Storage model
`<managed root>\builds\core-<emulator commit>\` and `launcher-<launcher commit>\`, each with `ps4pkgtool-manifest.json` (managedBy, component, repository, release, commit, asset, installedAtUtc). Orphans (interrupted commits) tolerated. Emulator commit recorded separately from any release-repo automation sha.

## 8. Active-core logic
`Shadps4ActiveCore.ResolveExecutable` — the ONLY launch-path source: managed id → store lookup; adopted → path must exist (missing = error, **no silent fallback to any discovered core**). Launch handler gates: no active core → dark dialog (offers explicit external-core approval or Settings). Play never launches the QtLauncher (pinned by test).

## 9. Specific-version selection
`Shadps4VersionPicker` lists real upstream metadata (channel Stable/Nightly, date, commit, asset name) from the feeds — no invented version numbers. Install via `Shadps4SetupService`; activation is a separate explicit action.

## 10. Update detection
Manual `Check for Updates` (Settings tab): Core (installed / latest stable / latest nightly) and QtLauncher (installed / latest) shown independently; `[Open GitHub]` fallback. **Never** auto-downloads, auto-installs or auto-activates.

## 11. Rollback/version switching
`Shadps4BuildManager`: all managed builds listed with `[Make Active]`; old builds preserved on every new install (test: `Store_InstallingNewVersion_KeepsOldBuildForRollback`); rollback = switch back. Per-game `Launch With >` deferred.

## 12. Launcher/core mismatch prevention (the incident)
Parent/grandparent cores are **external candidates only** — shown with a warning and explicit `[Use This Core]` approval; never auto-selected for launch (test: `ActiveCore_ResolveAdopted_RequiresExistingFileNoFallback`, `Launch_UsesOnlyTheActiveCore_NeverTheLauncher`). Wizard-managed installs never search unrelated folders. Compatibility confidence enum exists (`Verified/UpstreamBounded/SameGeneration/Unknown/Incompatible`); v1 emits only `Unknown` — never inferred from dates/proximity.

## 13. Download/extraction safeguards
Streaming download (`.part`, byte progress, cancellation); size check + official sha256 digest; free-space checks before download AND before extract (uncompressed-entry sum + 512 MB margin); ZIP-slip rejection (traversal/rooted/UNC/link entries, canonical boundary-aware containment); staging on the same volume as the builds root; atomic `Directory.Move` commit; AV-quarantine message when the expected exe is missing (never advises disabling AV); settings touched only after Success; cancellation cleans `.part` + staging (tests for all of these).

## 14. Direct-launch behavior
`shadPS4.exe CUSAxxxxx` via `BuildProcessStartInfo` (safe `ArgumentList`, `WorkingDirectory = core dir` — not required by main.cpp, matches launcher spawn). `Open QtLauncher` = explicit launcher only, no game args, never part of Play.

## 15. QtLauncher ownership boundary
PS4PKGTool: PKG management, library install/extract, compatibility display, direct launch, managed installs, version switching, update detection. QtLauncher: graphics/controllers/audio/cheats/patches. Wizard completion states this explicitly. "Open shadPS4 Config Folder" provided instead of config editing.

## 16. Config read-only confirmation
PS4PKGTool never writes `%APPDATA%\shadPS4\config.json` (or `config.toml`). Detection reads it; the wizard writes only PS4PKGTool's own settings and its build manifests. Verified upstream: patches configured in QtLauncher persist in the shared user dir and the core applies them at boot (auto-load gated); cheats are per-game JSON in the shared user dir.

## 17. Tests/build results
111/111 app tests + 58/58 asset tests pass; solution builds with 0 errors. Coverage includes the spec §41 list minus pure-UI interactions (covered by service seams): feed parsing/cache/rate-limit, recommendation policy, store commit/list/resolve/orphans/rollback, downloader verify/cancel, zip traversal/corrupt/truncated/containment/symlink, setup end-to-end/space/cancel/quarantine, active-core parse/resolve/migration, launch gating, settings round-trips (incl. `=` in paths), no hardcoded machine paths.

## 18. Unresolved upstream/compatibility questions
- **Core/launcher pairing**: upstream publishes no authoritative mapping for current builds; wizard reports compatibility `Unknown` and never guarantees a pair. (QtLauncher v224's "for use with up to shadPS4 0.15.0" note shows boundaries exist but are unmaintained.)
- **Cheat/patch persistence across core builds**: verified on main branch (shared user dir; core applies patches at boot when auto-load enabled); behavior may vary per nightly — completion screen wording kept conservative.
- **Launcher Version Manager cores**: located via the launcher's `versions.json` (LauncherDir) — shown nowhere in v1; diagnostics-only if adopted manually.
- **VC++ 2022 runtime**: required by upstream; not installed/detected by PS4PKGTool (deferred "runtime prerequisite installation").

## Deferred from v1 (per plan)
Auto-updates, auto-activation, auto-deletion, per-game core selection, full rollback UI, compatibility inference beyond explicit boundaries, automatic launcher/core pairing, cheat/patch/graphics management, writing shadPS4 config, runtime prerequisite installation.

## Manual verification still recommended (interactive UI)
Fresh-settings run of the wizard (Recommended install → builds under the managed root → active core set → config.json hash unchanged → Bloodborne launches via the active core; switch builds and back; cancel mid-download leaves no `.part`/staging).
