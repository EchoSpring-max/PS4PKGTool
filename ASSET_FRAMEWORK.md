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

## Implementation status (branch: feature/asset-framework, private)

| Phase | Scope | Status |
|---|---|---|
| 0 | Framework core (IAssetSource, capabilities, errors, detection, nesting) | DONE - 7/7 tests |
| 1 | Generic formats: PNG/JPEG/BMP/GIF, DDS/BC1-3, WAV, OGG, text + app preview integration | DONE - 7/7 tests |
| 2A | PS4-native metadata: GNF, ATRAC9 | DONE - 4/4 tests |
| 3a | Unity bundle container (UnityFS/UnityRaw/UnityWeb) + member browsing | DONE - 3/3 tests |
| 3b | Unity serialized file parser + hardcoded Texture2D layout, validated against real Overcooked 2 PS4 samples (LE body, stripped type trees, all textures streamed to .resS) | DONE - 4/4 tests |
| 3c | IUnityAssetBackend seam + AssetStudioBackend + UnitySerializedFileHandler (detect/inspect/browse/preview) | DONE - 7/7 tests (synthetic inline fixture + fake backend seam + real sample) |
| 3d | resS companion extraction + texture contact sheet preview (app-visible) | DONE - 4 new tests |
| 3e | PNG texture export: UnityTextureExporter (IAssetExporter, single child + whole-file bulk) + Export Textures button | DONE - 6 new tests |
| 4a | Unreal PAK container: self-written parser (verified against real CODE VEIN UE4 patch pak, v4, stripped header, mount point + byte-length index strings + FPakEntry records per CUE4Parse), IUnrealAssetBackend seam, browse/preview/decompress (None/Zlib/Gzip/LZ4; Oodle + AES = graceful). CUE4Parse stays a future swap. | DONE - 5 new tests |
| 5 | Hard PS4: full GNF decode, PSARC, ATRAC9 decode | pending |
| 6 | Asset workspace UI | pending |
| 7 | 3D viewport (OpenTK) | pending |
| 8 | Middleware: Wwise, CRIWARE, FMOD | pending |

Key learnings:
- Unity serialized data endianness follows the BUILD MACHINE (x86 = LE), not the
  target platform. Header fields are always BE; the marker byte at offset 16
  drives the body (0 = LE, matching AssetStudio).
- PS4 Unity builds ship stripped type trees - hardcoded class layouts are
  mandatory, generic type-tree walking does not work.
- PS4 Unity builds stream ALL texture image data out to .resS companions
  (verified: 199/199 textures in resources.assets, 0 inline).

## Phase 4b status (2026-08-13): PARTIALLY BLOCKED

The Unreal texture decode (UTexture2D from .uasset) is blocked on a
non-standard package format:

- CODE VEIN (the only accessible UE4 sample) writes BOTH .uasset and .uexp
  with a 53-byte preamble (8 zeros + fileSize + fileSize + int32 + 20-byte
  GUID + 5 zeros) before the package magic at offset 53.
- The summary is UNVERSIONED (legacy version -7, all version fields 0) with
  byte-length-prefixed strings everywhere (pak index, summary, name table)
  and 4-byte name hashes.
- The export-table record size and the summary's tail fields could not be
  pinned from static analysis; CUE4Parse does not support CODE VEIN (not in
  its game list - strong evidence the format is game-specific).
- Verified anchors: preamble, name table format ({int32 byteLen incl null,
  UTF-8 + null, uint32 hash}), 40-45 byte export records ending at EOF.

Delivered instead (verified):
- PAK index now handles two-section indexes (entries + UTF-16 directory
  section): the 14.4 GB CODE VEIN base pak parses 71,118 entries (4,196
  textures browsable/extractable via the pak reader).

Options to unblock:
1. Dedicated session to finish the CODE VEIN summary/export mapping
   (needs the uexp export-data anchors).
2. Extract a different game's base pak (6-22 GB) and test whether its
   packages use the STANDARD format (magic at 0) - most likely.
3. Defer 4b until the asset workspace (Phase 6) makes pak-entry browsing
   user-visible anyway.

## Phase 4b progress 2 (2026-08-13): PAK v8 + compressed entries + standard format confirmed

- PAK version 8 support (FNameBasedCompressionMethod): 1-byte method index in
  entries; trailer carries the method name ("Zlib") and can sit well before
  EOF - the trailer scan now searches backward with version/index validation.
- Compressed entries decode: self-describing records with block lists
  ({start,end} ranges; v5+ entry-relative, v4 absolute). Verified against
  Bee Simulator (Zlib entries round-trip).
- Bee Simulator base pak (6.4 GB): 67,486 entries, 4,246 textures browsable
  and extractable.
- STANDARD package format CONFIRMED on Bee Simulator (magic at offset 0) -
  the CODE VEIN 53-byte preamble is game-specific, not the norm.
- Standard summary mapping (verified against EOF alignment on real textures):
  magic@0, legacy -7 (unversioned), TotalHeaderSize, byte-length PackageName,
  PackageFlags, NameCount, ExportOffset/Count, ImportOffset. Name table =
  {int32 byteLen incl null, UTF-8 + null, uint32 hash}. Export records 40
  bytes (3 x 40 = EOF exact). Import table ends where the export table begins.

REMAINING for 4b: the UTexture2D object data in the .uexp uses UNVERSIONED
properties (FUnversionedHeader + class schema), then the cooked platform data
(FTexturePlatformData/FTexture2DMipMap/FByteBulkData layouts are fully
referenced). The unversioned property schema for UTexture2D is the last
unknown; the dims/format/mips live AFTER the properties, so the property
values can be skipped once the schema order is pinned.

## Phase 4b progress 3 (2026-08-13): Unreal package parser + texture metadata VALIDATED

The UnrealPackage reader now decodes REAL standard-format packages end to end
(Bee Simulator T_Default_Material_Grid_M, verified):

- Summary: magic at 0 (game-specific preambles handled by magic-scan), legacy
  -7 unversioned, TotalHeaderSize, byte-length PackageName, PackageFlags,
  NameCount, ExportOffset/Count, ImportOffset. A 1-byte realignment handles
  the cooked-flag extra byte observed in this format.
- Name table: {int32 byteLen incl null, UTF-8 + null, uint32 hash}; position
  located empirically (the summary's name offset is 0 in this format, and the
  declared name count covers more than the parsed section - 17 real entries
  here, not 193).
- Export table: 40-byte records {Class, Super, Template, Outer, Name(8),
  Flags, Size(4), ???(4), Offset(4)}; SerialOffset is relative to
  TotalHeaderSize and addresses the .uexp companion.
- Texture2D object data: unversioned properties skipped via the presence
  header, then the cooked platform data anchored on the "PF_" pixel-format
  FString: platform dims (512x512), format (PF_DXT5), mip count (10) - all
  validated against real bytes.

REMAINING for full texture decode: the mip BULK payloads are NOT in the
.uexp - the bulk flags (0x0501 = PayloadAtEndOfFile + PayloadInSeperateFile +
Force_NOT_InlinePayload) and offsets indicate the payloads live in the PAK
(streaming texture data). Retrieving them needs the pak-source plumbed into
the package reader, then BC decode. The bulk-header layout also needs
verification against the actual payload locations.

## Final status (2026-08-13): what "finish all" delivered, what is sample-blocked

DONE (verified):
- Phase 0-3e (framework, generic formats, PS4 metadata, Unity full pipeline)
- Phase 4a (Unreal PAK: three real paks verified)
- Phase 4b (Unreal package parse + texture metadata; pixel decode blocked on
  streaming bulk payload locations that resolve to no accessible data)
- Phase 6 (asset workspace UI: container children browse + per-child preview
  in the File Browser viewer; the app-visible payoff)

SAMPLE-BLOCKED (not code-blocked):
- Phase 5a PSARC: format documented; theme PKGs in the library keep their
  psarc content encrypted (orbis lists only Sc0 system files, no PSAR magic
  in the extracted bytes). Needs a decrypted theme psarc sample.
- Phase 5b GNF decode: metadata handler done; no real GNF file in the
  library (games ship BC-in-pak / Unity resS / UE formats). Needs a GNF
  sample to verify the deswizzle.
- Phase 7 (3D viewport), Phase 8 (Wwise/CRIWARE): deferred - large standalone
  efforts.

Validation corpus on disk: C:\Users\User\AppData\Local\Temp\p4t_spike_ue
(CODE VEIN base 14.4GB + update 628MB, Bee Simulator 6.4GB paks, extracted
samples, CUE4Parse/AssetStudio reference sources). Delete to reclaim ~22GB
when done with Unreal work.
