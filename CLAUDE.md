# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current state

The project is implemented: solution `PdfUnlocker.slnx`, app in `src/PdfUnlocker/`, tests
in `tests/PdfUnlocker.Tests/`. It targets **.NET 10** (`net10.0-windows`, `win-x64`) and
uses **PDFsharp 6.2.4**. `PdfUnlockerClaude.md` remains the authoritative build spec; the
implementation follows it and its VERIFY items have been resolved empirically (see below).

### Verified PDFsharp 6.2.4 facts (were VERIFY in the spec)

- Wrong/absent password throws `PdfSharp.Pdf.IO.PdfReaderException` ("The specified
  password is invalid." / "A password is required…"). `Unlocker.TryOpen` catches only this
  in the retry loop.
- A corrupt/non-PDF file throws `System.InvalidOperationException` ("The file is not a
  valid PDF document.") — a *different* type, so it propagates and is not mistaken for a
  bad password (Program maps it to exit 10).
- AES-256 (revision 5) is created in tests via `doc.SecurityHandler.SetEncryptionToV5()`
  and `SecurityHandler.UserPassword = …`; the 6.2.x security API is on `SecurityHandler`,
  not the old `SecuritySettings.DocumentSecurityLevel`.
- `AppContext.BaseDirectory` resolves the exe folder correctly under single-file publish.
- A published single-file exe still runs after being renamed, which is how the release's
  `PdfUnlockerSetup.exe` is produced (a renamed copy of the published `PdfUnlocker.exe`).
- PDFsharp 6.2.4 pulls in `System.Security.Cryptography.Pkcs` 8.0.1, which has DoS
  advisories; the app `.csproj` pins a patched version directly.

### Setup mode (deviation from the original spec)

Started with **no arguments** (double-clicked), the exe runs `Setup.RunInteractive`
instead of failing with a usage error: an OK/Cancel message box, then it copies itself to
`%APPDATA%\PdfUnlocker\PdfUnlocker.exe`, creates `PdfUnlocker.config` from the embedded
`PdfUnlocker.config.sample` **only if missing** (never overwrite the user's passwords),
creates a "PDF Unlocker" `.lnk` in the Send To folder (`ShellLink.cs`, IShellLink COM
interop), and opens the config in Notepad. `Setup.Install` is the testable core; tests
must only ever point it at temp folders, never the real %APPDATA% or Send To. The sample
config must contain no active (uncommented) passwords. A blank argument still exits 1.

## Commands

```powershell
dotnet build                 # build solution
dotnet test                  # run all xUnit tests
dotnet test tests/PdfUnlocker.Tests/PdfUnlocker.Tests.csproj --filter "FullyQualifiedName~UnlockerTests.Aes256_RevisionFive_OpensCorrectly"   # single test

# Deployment artifact — single-file, self-contained exe (runs with no .NET installed):
dotnet publish src/PdfUnlocker/PdfUnlocker.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Tests reach the app's `internal` types via `InternalsVisibleTo` (in the app `.csproj`), so
the test project must stay named `PdfUnlocker.Tests` and share the app's TFM
(`net10.0-windows`).

## What this project is

PdfUnlocker is a small Windows console utility (C# / .NET) that removes the *open
password* from a PDF by trying a stored list of user-supplied candidate passwords, then
writing an unencrypted copy alongside the original. It is a convenience tool for
passwords the user already owns — not a password cracker — and only ever tries
passwords from a config file the user maintains. It's launched from Explorer via
**Open With** / **Send To**, with the PDF path as the single command-line argument.

## Intended project layout (per spec §3)

```
PdfUnlocker/
├─ PdfUnlocker.sln
├─ src/
│  └─ PdfUnlocker/
│     ├─ PdfUnlocker.csproj
│     ├─ Program.cs            # entry point + orchestration only
│     ├─ Unlocker.cs           # PDF open/try-passwords/save logic
│     ├─ ConfigLoader.cs       # reads PdfUnlocker.config
│     └─ RunLogger.cs          # create-on-start / delete-on-success log
├─ PdfUnlocker.config.sample   # example password list (committed)
└─ README.md
```

Keep `Program.cs` thin: parse args, wire up the logger, call into `Unlocker`, map
results to exit codes. Never commit a real `PdfUnlocker.config` (it holds real
passwords) — only `PdfUnlocker.config.sample`.

## Tech stack

- C# (latest language version), targeting .NET 8.0 by default (a newer LTS is fine;
  keep `TargetFramework` a one-line change).
- PDF library: NuGet package `PdfSharp` (empira 6.x), **version 6.2.0+ required** — that
  version added revision-5 (AES-256) decryption support.
- Ships as a single self-contained `PdfUnlocker.exe` (no installer, no external runtime
  dependency), installed by its own setup mode to `%APPDATA%\PdfUnlocker\PdfUnlocker.exe`.
- No third-party dependencies beyond PdfSharp and the BCL.

## Build / publish commands

Once the project exists:

```powershell
dotnet build src/PdfUnlocker/PdfUnlocker.csproj -c Release

# Single-file, self-contained, x64 — the actual deployment artifact:
dotnet publish src/PdfUnlocker/PdfUnlocker.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Verify the published exe runs on a machine without the .NET runtime installed — that's
the point of `--self-contained`.

## Architecture (core algorithm — spec §4)

1. **Config resolution:** `PdfUnlocker.config` lives next to the exe, found via
   `AppContext.BaseDirectory` — **not** `Assembly.Location`, which is empty in
   single-file publishes. Never rely on the process working directory (Explorer
   launches it from unpredictable directories).
2. **Config format:** one password per line, UTF-8. Blank/whitespace lines and lines
   starting with `#` are skipped. Only the line terminator is stripped — passwords are
   **never `Trim()`-med**, since leading/trailing spaces can be significant.
3. **Unlock algorithm:**
   - Try opening with no password first (`PdfDocumentOpenMode.Import`) — if that
     succeeds, the file was never encrypted; skip straight to writing the copy.
   - Otherwise try each config password in file order via
     `PdfReader.Open(inputPath, password, PdfDocumentOpenMode.Import)`, catching only
     the wrong-password exception (**VERIFY** exact type — historically
     `PdfSharp.Pdf.IO.PdfReaderException`) to continue the loop. Let corrupt-file/I-O
     exceptions propagate — do not treat a broken PDF as "just a wrong password".
   - On success, copy pages into a brand-new `PdfDocument` and save **without ever
     touching `SecuritySettings`** — a document with no security settings is written
     unencrypted. This page-copy approach may drop some document-level features
     (certain bookmarks/outlines/AcroForm fields); document that trade-off in the
     README rather than silently switching strategies.
4. **Output naming:** `<dir>/<stem>_unlock<ext>`, overwriting silently if it exists.
5. **Logging (`PdfUnlocker.log`):** create-then-conditionally-delete pattern. Create/
   overwrite the log at startup next to the *input* PDF (fallback: exe folder, used when
   there's no valid input path). Track an `errorOccurred` flag; append error details on
   failure; in a `finally` block, delete the log if no error occurred, otherwise leave
   it. Logging failures themselves must never crash an otherwise-successful run.
6. **Exit codes:** 0 success, 1 usage error, 2 input not found/readable, 3 config
   missing/empty (only when input was actually encrypted), 4 no password matched, 5
   output write failed, 10 unexpected error. See spec §4.6/§5 for exact mapping.
7. **Console window:** prefer `<OutputType>WinExe</OutputType>` so no console flashes
   when launched from Explorer; never block on `Console.ReadKey`. Keep this a one-line
   toggle since a normal console app is easier to debug from a terminal.

## Testing

There is no test project yet. The spec's testing checklist (§9) is the acceptance
criteria to implement/verify against — covers correct-password / wrong-password /
last-in-list / unencrypted-input / missing-argument / nonexistent-input / missing-config
/ corrupt-file / AES-256 / no-leftover-log-on-success / Send-To & Open-With launch /
running without the .NET runtime installed. Favor small, separately testable methods
(config parsing, output-name construction, password trial) so these cases can be
exercised without a GUI.
