# orbis-pub-cmd.exe — Reverse Engineering Task For AI Engineer

## Goal

Reverse-engineer `orbis-pub-cmd.exe` (native Win32 PE) and create a **standalone C# library** that replaces it. Pure C#, pure managed code, no P/Invoke, no native interop. No dependency on any existing PKG library.

## Target Binary

`orbis-pub-cmd.exe` — PS4 Fake PKG Tools v3.87 (patched by CyB1K)

| Attribute | Value |
|---|---|
| Format | PE32 executable, native x86 (NOT .NET) |
| Platform | Windows console, 32-bit |
| Size | 3,274,784 bytes |
| Language | C/C++ |
| Unicode | No — ANSI file APIs only |
| Companion | orbis-pub-prx.dll (3,835,424 bytes) — crypto primitives |

## What Must Be Ported (Only 2 Commands)

### 1. `img_file_list` — List files inside a PKG

```
orbis-pub-cmd.exe img_file_list --passcode <32-char-hex> --oformat long+original_size <pkg_path>
```

**Input:** Path to .pkg file + 32-char hex passcode  
**Output (stdout):** Directory listing. `D` prefix = directory, `F <size>` prefix = file.

Example output:
```
D 0 Image0
D 0 Image0/sce_sys
D 0 Image0/sce_sys/trophy
F 106496 Image0/sce_sys/param.sfo
F 131072 Image0/sce_sys/trophy/TROPHY.TRP
D 0 Sc0
F 512 Sc0/npbind.dat
F 1536 Sc0/changeinfo.xml
```

### 2. `img_extract` — Extract files from a PKG

```
orbis-pub-cmd.exe img_extract --passcode <32-char-hex> <pkg_path>:<entry_path> <output_dir>
```

```
orbis-pub-cmd.exe img_extract --passcode <32-char-hex> <pkg_path> <output_dir>
```

**Input:** PKG path + passcode + optional entry path + output directory  
**Output:** Extracted file(s) written to disk  
**Without entry path:** Extracts everything

## Required C# API

```csharp
namespace OrbisPkgTool
{
    public class PkgFileEntry
    {
        public string Path { get; set; }          // e.g. "Image0/sce_sys/param.sfo"
        public string Name { get; set; }          // e.g. "param.sfo"
        public long Size { get; set; }            // original (decrypted) size in bytes
        public long PackedSize { get; set; }      // size inside PKG (may differ if compressed)
        public bool IsDirectory { get; set; }
        public bool IsEncrypted { get; set; }
        public int EntryId { get; set; }          // numeric entry ID from PKG header
        public long Offset { get; set; }          // byte offset in PKG file
    }

    public class PkgReader : IDisposable
    {
        public PkgReader(string pkgPath, string passcode);

        /// <summary>img_file_list equivalent — reads entry table, returns full tree.</summary>
        public List<PkgFileEntry> ListFiles();

        /// <summary>img_extract equivalent — decrypt single entry to disk.</summary>
        public void ExtractFile(string entryPath, string outputDirectory);

        /// <summary>img_extract equivalent — decrypt all entries to disk.</summary>
        public void ExtractAll(string outputDirectory,
            IProgress<(int current, int total, string currentFile)> progress = null);
    }
}
```

## What the Native Binary Does Internally

### 1. Read PKG header (first ~4KB)
- Magic bytes: `7F 43 4E 54` (`CNT` in reverse)
- PKG type (PS4 app, patch, addon, etc.)
- Content type flags
- Entry count, table offset, table size
- Passcode flag, digest, etc.

### 2. Derive decryption keys from passcode
- 32-char hex passcode → 16-byte binary key
- Key used to derive per-entry AES-128-CTR keys
- Some entries may be unencrypted even with passcode set
- Entry encryption flag in the meta table determines this

### 3. Read entry table
- Each entry has: name ID, file name, path, offset, size, packed size, encryption flag
- Directories and files are interleaved
- Build tree in memory

### 4. Decrypt entries on demand
- Read entry data from PKG at offset
- Decrypt with derived key if encryption flag is set
- Write to output path

## Key Unknowns That Must Be Reversed

1. **PKG header structure** — exact byte layout, field sizes, endianness
2. **Entry table format** — how entries are serialized, how names are stored
3. **Passcode key derivation** — 32-char hex → ??? → entry decryption key
4. **AES mode and parameters** — likely AES-128-CTR, need IV derivation
5. **Encryption flag semantics** — which bits mean what
6. **Fake SELF support** — orbis patch allows unsigned executables; what header modification?
7. **Chunk handling** — large PKGs may split entries across chunks

## Reverse Engineering Approach

### Static Analysis
- **Ghidra** (free): Disassemble `orbis-pub-cmd.exe` + `orbis-pub-prx.dll`. Find the `img_file_list` and `img_extract` command handlers. Trace from argument parsing to file I/O. Identify key derivation function.
- Focus on string references: `--passcode`, `img_file_list`, `img_extract`, `[Error]`, `Could not open`

### Dynamic Analysis
- **x64dbg**: Run `img_file_list` on a known small PKG with a known passcode. Break on `CreateFileW`/`ReadFile` to see what offsets are read. Trace the key derivation.
- **API Monitor**: Watch all file I/O and crypto API calls (if it uses Windows CryptoAPI/CNG)

### Validation
- Unit test against the real orbis-pub-cmd output for byte-identical results
- Test on: base game PKG, patch PKG, addon PKG, encrypted PKG, unencrypted PKG

## Acceptance Criteria

1. `PkgReader.ListFiles()` produces identical output to `orbis-pub-cmd.exe img_file_list` for:
   - Base game PKG (encrypted, retail)
   - Patch PKG
   - Addon/theme PKG (unencrypted)
   - Homebrew PKG (fake signed)
   - PKG with Unicode path (must not fail — ANSI was orbis's bug)

2. `PkgReader.ExtractFile()` produces byte-identical files to `orbis-pub-cmd.exe img_extract` for:
   - Single file extraction
   - Full PKG extraction
   - Extraction with and without passcode

3. Performance: file listing completes in under 2 seconds for a 50 GB PKG (entry table only, no data reads).

4. Runs on .NET 10, cross-platform (Windows/Linux/macOS). No native dependencies.
