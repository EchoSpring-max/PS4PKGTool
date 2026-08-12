# Asset Framework — Pre-Implementation Technical Review

Status: **GO for Phase 0** (interfaces freeze before any engine work).
Scope: view/render/export game assets from PS4 PKGs in-app (File Browser viewer pane), engine-agnostic, all in-process.

## Goal

Let modders view and export ANY known game asset type regardless of engine (Unity, Unreal, PS4-native, middleware), via an **extensible framework with tiered coverage** — not an unbounded "everything" promise.

## Architecture shape

```
PS4PKGTool (app — hosts Asset workspace in the File Browser viewer pane)
└── PS4PKGTool.Assets (own repo, own tests, referenced like DarkUI)
    ├── Abstractions   (IAssetSource, IAssetDetector, IAssetHandler, IAssetExporter, models)
    ├── Detection      (multi-stage, confidence-based registry)
    ├── IO             (virtual sources: PKG entry, file, container member, nested slices)
    ├── Export         (PNG/OBJ/glTF/WAV)
    └── Adapters       (Unity: IUnityAssetBackend · Unreal: CUE4Parse · Texture/DDS/GNF · Audio · PSARC)
```

Library is separate repo ≠ separate app. It compiles into the same exe; the app renders its output in the existing preview pane.

## Core interfaces

```csharp
public interface IAssetSource
{
    string Name { get; }
    long Length { get; }
    Stream OpenRead();                       // full stream
    Stream OpenRead(long offset, long length); // bounded slice
    string? SourceDescription { get; }       // "PKG entry", "PAK member", "PSARC member"...
}

[Flags] public enum AssetCapabilities
{
    None, Inspect, Browse, Preview, Decode,
    ExportRaw, ExportConverted
}
```

Resource ownership is explicit per handler (borrow stream / lazy stream / decoded buffer / temp file). **No hidden whole-archive loads into RAM.**

Capability model applied globally (a format is never a single binary capability):

| Asset | Capabilities |
|---|---|
| Texture | Inspect + Preview + Decode + ExportRaw + ExportConverted |
| PSARC | Inspect + Browse + ExportRaw |
| GNF unsupported pixel format | Inspect + ExportRaw |
| Unknown WEM codec | Inspect + ExportRaw |
| Unknown Unreal object | Inspect + ExportRaw |

## Detection

Multi-stage, extensible, confidence-based (NOT a magic-byte switch):

```
extension + path/context + magic/signature + container probe + engine probe = AssetDetectionResult{Format, Engine, Confidence, Evidence}
```
Example evidence: `Content/Paks/*.pak` → Unreal; `sharedassets*.assets` / `globalgamemanagers` → Unity.

## Error model

```
AssetParseException
UnsupportedAssetException
MissingDependencyException      // e.g. Oodle DLL absent
EncryptedAssetException
CorruptAssetException
```
Handlers are untrusted parsing code. Corrupt files yield structured messages ("Parsing failed: unsupported serialized version 27. Raw export remains available.") and never propagate raw library exceptions to WinForms.

## Container nesting

Any IAssetSource may expose child IAssetSource list. Arbitrary depth (PKG → PSARC → Unity bundle → Texture2D). Configurable depth limit (default 8) guards recursion/cycles. Stream slices avoid re-extracting full files per level.

## Dependency matrix (verified)

| Library | Purpose | License | Maint. | Managed/Native | Required/Optional | Redistributable by us? | Fallback if missing |
|---|---|---|---|---|---|---|---|
| AssetStudio core (original or aelurum fork — spike decides) | Unity backend | MIT | archived orig / active forks | managed | required (Unity) | yes | AssetRipper backend later |
| CUE4Parse | Unreal backend | Apache-2.0 | active (FModel) | managed | required (Unreal) | yes (Apache notice) | n/a |
| Oodle DLL | Unreal compression | proprietary (Epic) | n/a | native | **optional, user-supplied** | **unresolved — do not ship** | non-Oodle content works; Oodle content shows requirement + raw export |
| LibAtrac9 (shadps4 fork) | ATRAC9 | MIT | active | C/C# split (C# port exists) | required (PS4 audio) | yes | raw export |
| GNF (port from shadPS4/freegnm) | PS4 textures | port — licenses to verify | n/a | managed | required (PS4 textures) | yes if clean port | metadata + raw |
| PSARC (own implementation) | PS4 container | ours (GPL-3.0) | n/a | managed | required (PSARC) | yes | n/a |
| BC decode (AssetStudio/detex) | texture BC formats | MIT | active | managed | required | yes | GNF metadata-only |
| SkiaSharp | (CUE4Parse-Conversion dep) | MIT | active | native assets | **avoid** — route around | — | managed decoders |
| OpenTK | 3D viewport (Phase 7) | MIT | active | managed | optional | yes | static render |
| SharpGLTF | glTF/GLB export | MIT | active | managed | required (Phase 3+) | yes | OBJ-only |
| NAudio / StbImageSharp | audio / image decode | MIT | active | managed | required (Phase 1) | yes | n/a |
| KTX/KTX2 | PS5-era textures | — | — | — | **DEFERRED** | — | — |

## Oodle: unresolved by design

- CUE4Parse can load it (OodleHelper + OodleDotNet) — technical yes.
- Users can legitimately obtain it (UE game installs, research mirrors) — legal for the user.
- **Our GPL-3.0 project redistributing the proprietary DLL: no authoritative license found. Marked UNRESOLVED — we do not ship it, never auto-download it.**
- Design: user places `oodle-data-shared.dll` in AppData (same manual pattern as orbis tools). Oodle is required only for Oodle-compressed archives, not for CUE4Parse generally.

## Unity backend strategy

`UnityAssetHandler → IUnityAssetBackend → AssetStudioBackend | future AssetRipperBackend`.

**Decision: AssetStudio first, AssetRipper later.** MIT, managed, stream-capable, stable/archived (no churn), covers Unity 3.4-2022.1 (the PS4 corpus). AssetRipper is GPL-3.0, very active, covers newer Unity — kept as a replaceable backend behind the seam, not chosen on license alone.

Fork spike: original (archived) vs aelurum fork — evaluate stream input, single-asset extraction without whole-bundle load, mesh/texture export correctness, API stability, dependency size. Not by commit recency.

## KTX/KTX2 decision

**DEFERRED.** PS4 ships DDS/GNF, not KTX2 (PS5-era). If ever needed: header parsing is trivial managed code; Basis Universal/ASTC/Zstd transcoding is a separate later decision. Container parsing ≠ payload decoding.

## GNF decomposition

```
GNF parser (header/metadata, TEX2D)          → managed, feasible
PS4 swizzle/deswizzle                        → port algorithms from shadPS4 (verify license)
GPU format decode (BC1/3/5, BC6/7, RGBA8/16) → reuse managed BC decoder
```
Unsupported pixel formats → Inspect + ExportRaw. Estimate ~1-2 weeks once licenses verified.

## PSARC feasibility

**Feasible — own managed read-only implementation.** Structure documented (magic `PSAR`, TOC, zlib entries). Scope: Open / List / Open entry as Stream / Extract. No creation/repacking. Fits IAssetSource child streams. Estimate ~1 week.

## Wwise strategy

```
RIFF/WEM parser → metadata → codec identification → supported → decode
                                                  → unsupported → raw export
```
`WEM recognized ≠ WEM playable` — enforced by the capability model. No maintained pure-C# Wwise decoder; wwiser is Python.

## Phase roadmap (solo-safe ordering)

```
Phase 0   Asset framework: interfaces, registry, detection, IAssetSource, capabilities, error model
Phase 1   Generic formats: PNG/JPEG/BMP, DDS, WAV, OGG, text — into the existing preview pane
Phase 2A  Easy PS4: ATRAC9 + GNF metadata/basic (if feasible)
Phase 2B  Container infrastructure: nested IAssetSource, virtual dirs, caching, stream slices
Phase 3   Unity (AssetStudio backend)
Phase 4   Unreal (CUE4Parse, Oodle-optional)
Phase 5   Harder PS4: full GNF, PSARC, additional codecs
Phase 6   Asset workspace (grid UI: name/type/engine/source/size/preview/export)
Phase 7   3D viewport (OpenTK: orbit/pan/zoom, solid/wireframe, texture toggle)
Phase 8   Middleware: Wwise, CRIWARE (HCA/ACB/AWB), FMOD
```
Unity/Unreal are NOT blocked by PSARC/GNF research.

## Phase 0 acceptance tests (fake/test handlers)

| Test | Requirement |
|---|---|
| A | physical file → detector → handler → metadata |
| B | parent container → child IAssetSource → detector → child handler |
| C | nested: container A → container B → asset |
| D | simulated multi-GB source: prove no full-size allocation |
| E | cancellation |
| F | malformed input never propagates a fatal UI exception |
| G | unknown format still permits raw export |

Only after A-G pass do engine integrations begin.

## Effort (honest, production)

| Scope | Estimate |
|---|---|
| Phases 0-2 prototype (usable value) | ~4-6 weeks |
| Full pipeline through Phase 8 | ~4-6 months solo |

## Open verification items (do NOT block Phase 0)

- Oodle redistribution rights (authoritative evidence, if any)
- GNF port licenses (shadPS4 / freegnm)
- AssetStudio fork pick (original vs aelurum spike)
- PSARC structure confirmation against a real sample
- KTX2 re-evaluation if PS5 corpus ever matters

## Distribution / packaging rules

- Managed dependencies: preferred, embedded in single-file publish
- Native in-process DLL: only when justified (Oodle remains user-supplied external)
- External helper executables: avoided for the asset subsystem
- THIRD-PARTY-NOTICES.md required at release (MIT/Apache/GPL notices)
