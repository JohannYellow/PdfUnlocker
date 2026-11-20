# PdfUnlocker

Removes the *open password* (user password) from a PDF by trying a list of passwords
**you already know**, then writing a new, unencrypted copy next to the original.

This is a convenience tool for your own protected documents — it only ever tries
passwords from a list you maintain. It does not guess, brute-force, or attack unknown
passwords.

## Installation

1. Download **`PdfUnlockerSetup.exe`** from the
   [latest release](https://github.com/JohannYellow/PdfUnlocker/releases/latest). No .NET
   runtime is needed. The exe is not code-signed, so Windows SmartScreen may warn on first
   run: choose *More info → Run anyway*.
2. Double-click `PdfUnlockerSetup.exe` and click **OK**. Setup:
   - copies the program to `%APPDATA%\PdfUnlocker\PdfUnlocker.exe` (your own user folder,
     no admin rights needed);
   - creates `%APPDATA%\PdfUnlocker\PdfUnlocker.config` if it doesn't exist yet (an
     existing config is never overwritten);
   - adds **PDF Unlocker** to your right-click *Send to* menu;
   - opens the config in Notepad.
3. Add your passwords to the config, one per line, and save it.
4. Right-click a PDF → **Send to → PDF Unlocker**. On Windows 11, choose *Show more
   options* first to see *Send to*.

`PdfUnlockerSetup.exe` can be deleted after setup. To upgrade, run the new version's setup;
your passwords are kept.

**Optional, Open With:** right-click a PDF → *Open with* → *Choose another app* → *More
apps* → *Look for another app on this PC* → browse to `%APPDATA%\PdfUnlocker\PdfUnlocker.exe`.

**Uninstall:** delete the `%APPDATA%\PdfUnlocker` folder and the *PDF Unlocker* shortcut in
your Send To folder (press `Win+R` and run `shell:sendto` to open it).

## How it works

Started with no arguments (double-clicked), the exe runs the setup described above. Given a
PDF path as its argument, PdfUnlocker:

1. Tries to open the PDF with no password. If it opens, it was never encrypted — it just
   writes the copy.
2. Otherwise it reads `PdfUnlocker.config` (next to the exe) and tries each password in
   order until one opens the file.
3. On success it writes `<name>_unlock.pdf` in the same folder, unencrypted (overwriting
   any existing file of that name).

A run that fails leaves a `PdfUnlocker.log` next to the PDF (or next to the exe if there
is no usable input) explaining why. A successful run leaves no log — only the
`_unlock.pdf`.

### Exit codes

| Code | Meaning                                            |
|------|----------------------------------------------------|
| 0    | Success (file unlocked, or input was unencrypted). |
| 1    | Usage error (blank/invalid argument).              |
| 2    | Input file not found / not readable.               |
| 3    | Config missing or empty (for an encrypted input).  |
| 4    | No configured password unlocked the file.          |
| 5    | Failed to write the output file.                   |
| 10   | Unexpected/unclassified error.                     |

## Configuration

`PdfUnlocker.config` lives in the **same folder as `PdfUnlocker.exe`** (not the current
working directory), which after setup is `%APPDATA%\PdfUnlocker`. Setup creates it from
the template [`PdfUnlocker.config.sample`](PdfUnlocker.config.sample), which is embedded in
the exe. Add your passwords one per line.

- Blank lines and lines starting with `#` are ignored.
- Passwords are tried top to bottom.
- **Passwords are not trimmed** — leading and trailing spaces are kept, because they can
  be a real part of a password.

The real `PdfUnlocker.config` is git-ignored so your passwords are never committed.

## Building

Requires the .NET SDK (project currently targets **.NET 10**).

```powershell
# Run the tests
dotnet test

# Single-file, self-contained, x64 exe (the only file you deploy):
dotnet publish src/PdfUnlocker/PdfUnlocker.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

The published exe is self-contained: it runs on a machine with **no .NET runtime
installed**. The project file already sets those options, so `dotnet publish
src/PdfUnlocker/PdfUnlocker.csproj -c Release` is enough. Publishing also writes
`PdfUnlockerSetup.exe` to the same `publish` folder: an identical copy under the setup name,
which is the file to attach to a GitHub release. A plain `dotnet build` doesn't create it,
because build output isn't self-contained.

### Changing the target framework

`TargetFramework` is set in one place, `src/PdfUnlocker/PdfUnlocker.csproj`. Change
`net10.0-windows` there (e.g. to `net8.0-windows`) to target a different runtime.

### Console window vs. no window

The project is built as a `WinExe` so no console window flashes when the tool is launched
from Explorer — all output goes to `PdfUnlocker.log`. To get a normal console app (easier
to debug from a terminal), change `<OutputType>WinExe</OutputType>` to
`<OutputType>Exe</OutputType>` in the `.csproj`. Behaviour is otherwise identical; the
tool never pauses for input.

## Known limitation

The unlocked copy is produced by copying the pages into a brand-new PDF, which reliably
strips encryption and preserves page content. It may **not** carry over every
document-level feature — some bookmarks/outlines, certain annotations, or AcroForm
fields. This approach is used because it also works when the file carries a separate
*owner* password you don't have. If you need to preserve everything and the file has no
owner-password restriction, a re-save-in-place approach would retain more, but that path
can be blocked by an owner password.

## License

PdfUnlocker is released under the [MIT License](LICENSE). The released exe bundles
PDFsharp and the .NET runtime, which are also MIT-licensed; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
