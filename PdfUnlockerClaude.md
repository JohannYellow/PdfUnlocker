# PdfUnlocker — Build Specification

This document is the implementation brief for **PdfUnlocker**, a small Windows
console utility written in C# / .NET. Read it in full before writing code, then
implement the program exactly to this specification. Where this document marks
something as **VERIFY**, confirm the detail against the actually-installed
library/SDK version rather than assuming — APIs change between versions.

---

## 1. Purpose

PdfUnlocker removes the *open password* (user password) from a PDF file by
trying a stored list of candidate passwords until one succeeds, then writing a
new, unencrypted copy of the PDF alongside the original.

The user already knows these passwords — this is a convenience/automation tool
for their own protected documents, **not** a password cracker. It only ever
tries passwords from a list the user maintains; it does not guess, brute-force,
or attack unknown passwords.

The program is intended to be launched from the Windows GUI via the **Open With**
or **Send To** context menus, with the target PDF passed as a single command-line
argument.

---

## 2. Tech stack and constraints

- **Language:** C# (latest language version available with the chosen SDK).
- **Runtime:** .NET 8.0 (LTS) by default. A newer LTS (e.g. .NET 10) is
  acceptable; keep the `TargetFramework` easy to change in one place.
- **PDF library:** PDFsharp, NuGet package id **`PdfSharp`** (the empira 6.x
  package). **Require version 6.2.0 or newer** — 6.2.0 added support for reading
  PDFs secured with the newer revision‑5 encryption (AES‑256), which older
  producers now emit. **VERIFY** the latest 6.2.x is referenced.
- **Distribution:** a **single self-contained .exe**, no installer, no external
  runtime dependency. It will live at `C:\Programs\PdfUnlocker\PdfUnlocker.exe`.
- **No GUI.** Console application. See §6 for whether the console window is shown.
- **No third-party dependencies** beyond PDFsharp and the .NET BCL.

---

## 3. Repository / project layout

```
PdfUnlocker/
├─ PdfUnlocker.sln
├─ src/
│  └─ PdfUnlocker/
│     ├─ PdfUnlocker.csproj
│     ├─ Program.cs            # entry point + orchestration
│     ├─ Unlocker.cs           # PDF open/try-passwords/save logic
│     ├─ ConfigLoader.cs       # reads PdfUnlocker.config
│     └─ RunLogger.cs          # create-on-start / delete-on-success log
├─ PdfUnlocker.config.sample   # example password list (committed)
├─ .gitignore                  # ignore bin/, obj/, and real PdfUnlocker.config
└─ README.md                   # short human-facing usage notes
```

Keep `Program.cs` thin: parse args, wire up the logger, call into `Unlocker`,
map results to exit codes. Put the real work in the other classes so it is
testable.

**Do not commit a real `PdfUnlocker.config`** (it contains real passwords). Add
it to `.gitignore` and ship only `PdfUnlocker.config.sample`.

---

## 4. Runtime behaviour (functional spec)

### 4.1 Command-line contract

- Exactly one argument is expected: the full path to the PDF to unlock
  (`args[0]`).
- If zero arguments are supplied → this is an error (see §5). There is no input
  PDF, so the log goes in the **executable's** folder (see §4.5 fallback).
- Ignore any arguments beyond the first.
- The path may contain spaces; rely on the runtime's argument parsing, do not
  re-split `args`.

### 4.2 Locating the config file

`PdfUnlocker.config` lives **in the same folder as `PdfUnlocker.exe`**, *not* in
the current working directory.

> **Critical gotcha:** when launched via *Open With* / *Send To*, the process
> working directory is typically the PDF's folder or a system folder — never
> assume it is the exe folder. Resolve the exe folder explicitly:
>
> ```csharp
> string exeDir = AppContext.BaseDirectory;
> string configPath = Path.Combine(exeDir, "PdfUnlocker.config");
> ```
>
> (`AppContext.BaseDirectory` is correct for single-file publish; do **not** use
> `Assembly.Location`, which is empty in single-file builds. **VERIFY**.)

### 4.3 Config file format (`PdfUnlocker.config`)

- Plain UTF‑8 text, **one candidate password per line**.
- Lines that are empty/whitespace-only are skipped.
- Lines whose first non-whitespace character is `#` are treated as comments and
  skipped.
- For all other lines, strip only the trailing line-ending (`\r` / `\n`). **Do
  not `Trim()` the password** — leading/trailing spaces can be significant parts
  of a real password. Document this clearly in `PdfUnlocker.config.sample`.
- Passwords are tried **in file order**, top to bottom.
- If the config file is missing or contains no usable passwords, that is an
  error (see §5) — except note that an *unencrypted* input still succeeds (see
  §4.4 step 1).

### 4.4 Unlock algorithm

1. **Unencrypted check first.** Try to open the input with no password in import
   mode. If it opens, the PDF was never encrypted — skip straight to step 4 and
   just write the copy.
2. **Try each candidate.** For each password from the config (in order), attempt
   to open the document with that password. On the *wrong-password* exception,
   catch it and continue to the next candidate. On success, stop.
3. **No match.** If every candidate fails, this is an error (see §5):
   "no configured password unlocked the file."
4. **Write the unlocked copy.** Produce a new PDF with no encryption (see §6 for
   the exact PDFsharp pattern) and save it to the output path from §4.4a.
   Overwrite silently if it already exists.

#### 4.4a Output file naming

Append `_unlock` to the base name, keep the original directory and extension:

```
C:\docs\Report.pdf   ->   C:\docs\Report_unlock.pdf
C:\docs\a.b.pdf      ->   C:\docs\a.b_unlock.pdf
```

```csharp
string dir  = Path.GetDirectoryName(inputPath)!;
string stem = Path.GetFileNameWithoutExtension(inputPath);
string ext  = Path.GetExtension(inputPath);            // ".pdf"
string outputPath = Path.Combine(dir, $"{stem}_unlock{ext}");
```

If the output file already exists, **overwrite it** (no prompt, no counter).

### 4.5 Logging behaviour (`PdfUnlocker.log`)

The log records errors and exists **only when a run fails**.

- **Location:** the same folder as the **input PDF** (the file that "caused" the
  run). **Fallback:** if there is no usable input path (e.g. no argument, or the
  directory can't be resolved), use the **executable's** folder.
- **Name:** `PdfUnlocker.log`.
- **Create-then-conditionally-delete pattern:**
  1. At startup, after resolving the log folder, create/open the log for writing
     (overwrite any stale log from a previous run) and write a timestamped start
     line.
  2. Track an `errorOccurred` flag, initially `false`.
  3. Wrap the whole run. On any error, set `errorOccurred = true` and append the
     details (timestamp, message, exception type, stack trace, and the input
     path) to the log.
  4. In a `finally` block: flush and close the log. **If `errorOccurred` is
     false, delete the log file.** If it is true, leave it on disk.
- **Robustness:** if writing/creating/deleting the log itself throws (e.g. the
  folder is read-only), do not let that crash a run that otherwise succeeded.
  Swallow logging failures (optionally fall back to writing to the exe folder or
  to `stderr`), and prefer reporting the *original* error.

Net effect: a clean successful run leaves no files except the `_unlock` PDF; a
failed run leaves a `PdfUnlocker.log` next to the offending PDF explaining why.

### 4.6 Exit codes

Return a meaningful process exit code (useful if it is ever scripted):

| Code | Meaning                                             |
|------|-----------------------------------------------------|
| 0    | Success (file unlocked, or input was unencrypted).  |
| 1    | Usage error (missing/invalid argument).             |
| 2    | Input file not found / not readable.                |
| 3    | Config missing or empty.                            |
| 4    | No configured password unlocked the file.           |
| 5    | Failed to write the output file.                    |
| 10   | Unexpected/unclassified error.                      |

---

## 5. Error handling

Treat the following as handled errors — each writes to the log (§4.5) and returns
the matching exit code (§4.6):

- No argument supplied, or the argument is empty/whitespace. (code 1)
- The input path does not exist, is not a file, or cannot be read. (code 2)
- The input does not appear to be a PDF / PDFsharp cannot parse it as one
  (distinct from "wrong password"). (code 10, or 2 if it’s an access issue)
- `PdfUnlocker.config` is missing, unreadable, or yields zero usable passwords —
  **but** only if the input was actually encrypted (an unencrypted input still
  succeeds without any config). (code 3)
- Every candidate password failed. (code 4)
- The output file cannot be written (path invalid, disk full, locked by another
  process). (code 5)
- Any other unexpected exception. (code 10)

Distinguish clearly between **"wrong password" (expected, loop continues)** and
**"file is corrupt / not a PDF / I/O failure" (real error, stop and log)**. Do
not let a corrupt-file exception be silently swallowed as if it were just a bad
password.

---

## 6. PDFsharp implementation notes

Namespaces: `PdfSharp.Pdf`, `PdfSharp.Pdf.IO`.

### 6.1 Trying a password

Open the document with a candidate password. The recommended, most reliable way
to guarantee an unencrypted output regardless of whether the file also has a
separate *owner* password is to open in **Import** mode and copy the pages into a
brand-new document:

```csharp
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

// Returns true and sets `source` if this password opened the file.
bool TryOpen(string inputPath, string password, out PdfDocument? source)
{
    source = null;
    try
    {
        source = PdfReader.Open(inputPath, password, PdfDocumentOpenMode.Import);
        return true;
    }
    catch (PdfReaderException)   // <-- VERIFY exact type for wrong password
    {
        return false;           // wrong password: caller tries the next one
    }
}
```

> **VERIFY — wrong-password exception type.** Confirm which exception PDFsharp
> 6.2.x throws when the password is incorrect. Historically it is
> `PdfSharp.Pdf.IO.PdfReaderException` with a message indicating an invalid
> password. PDFsharp also supports a `PdfPasswordProvider` callback overload of
> `PdfReader.Open`; either approach is acceptable as long as a wrong password is
> cleanly distinguished from a genuinely broken/corrupt file. **Only** catch the
> wrong-password case in the retry loop; let parsing/I/O exceptions propagate to
> the error handler in §5. Write a tiny throwaway test against a known
> password-protected PDF to confirm the type before finalizing the catch clause.

For the **unencrypted-input** check in §4.4 step 1, call the no-password overload
`PdfReader.Open(inputPath, PdfDocumentOpenMode.Import)`; if it succeeds the file
was not encrypted.

### 6.2 Writing the unlocked copy

Copy pages into a new document and save without ever touching `SecuritySettings`
— a document with no security settings is saved unencrypted:

```csharp
void SaveUnlocked(PdfDocument source, string outputPath)
{
    using var output = new PdfDocument();
    foreach (var page in source.Pages)
        output.AddPage(page);

    // Do NOT set output.SecuritySettings.UserPassword / OwnerPassword.
    // No security set => the saved file is unencrypted.
    output.Save(outputPath);   // overwrites if it exists
}
```

> **Known limitation to note in the README:** copying pages into a new document
> reliably removes encryption and preserves page content, but may not carry over
> every document-level feature (some bookmarks/outlines, certain annotations, or
> AcroForm fields). If preserving everything matters more than maximum
> robustness, the alternative is to open the *same* document in
> `PdfDocumentOpenMode.Modify` and re-save it without security — but that path
> can be blocked when the file carries a separate owner password you don't have.
> Default to the import-and-copy approach above; mention the trade-off in the
> README.

### 6.3 Console window

Because the tool is launched from the GUI and reports via the log file, it should
**not** pause for input (`Console.ReadKey`) and should not require a visible
console.

- **Recommended:** set `<OutputType>WinExe</OutputType>` in the `.csproj` so no
  console window flashes on screen during a GUI launch. All diagnostics go to
  `PdfUnlocker.log`.
- If you prefer a normal console app (e.g. for easier debugging from a terminal),
  keep `Exe`, but still do not block on `ReadKey` — rely on the log and exit
  codes. Make this a one-line change so it’s easy to flip.

---

## 7. Build and publish

Single-file, self-contained, x64:

```powershell
dotnet publish src/PdfUnlocker/PdfUnlocker.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

The resulting `PdfUnlocker.exe` is the only file that needs to be deployed.
**VERIFY** it runs on a machine without the .NET runtime installed (that is the
point of `--self-contained`).

Recommended `.csproj` highlights (confirm against the chosen SDK):

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>      <!-- or Exe; see §6.3 -->
  <TargetFramework>net8.0-windows</TargetFramework>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

> **VERIFY** that PDFsharp 6.2.x works under self-contained single-file publish
> with no GDI/`System.Drawing` requirement for this read/copy/save workload.
> (The 6.x core handles PDF I/O without needing GDI; confirm there are no
> missing-native-library warnings at runtime.)

---

## 8. Installation (document this in README.md)

1. Create `C:\Programs\PdfUnlocker\` and copy `PdfUnlocker.exe` into it.
2. Create `C:\Programs\PdfUnlocker\PdfUnlocker.config` (copy the `.sample`,
   then fill in real passwords, one per line).
3. **Open With:** right-click any PDF → *Open with* → *Choose another app* →
   *More apps* → *Look for another app on this PC* → browse to
   `C:\Programs\PdfUnlocker\PdfUnlocker.exe`. Optionally tick *Always use this
   app* if you want it as a permanent choice.
4. **Send To (often nicer for a utility):** press `Win+R`, run `shell:sendto`,
   and place a **shortcut** to `PdfUnlocker.exe` in that folder. The tool then
   appears under right-click → *Send to → PdfUnlocker*.

---

## 9. Testing checklist

Implement/verify against these cases:

- [ ] PDF encrypted with a password that **is** in the config → produces
      `<name>_unlock.pdf`, no leftover log, exit 0.
- [ ] PDF whose correct password is the **last** entry in the config → still
      succeeds (loop ordering correct).
- [ ] PDF with **no** matching password in config → no output PDF,
      `PdfUnlocker.log` written next to the input, exit 4.
- [ ] **Unencrypted** PDF → copied to `_unlock.pdf` without needing config,
      exit 0.
- [ ] Output file already exists → overwritten without error.
- [ ] Input path **with spaces** → handled correctly.
- [ ] **Missing argument** → log written to exe folder, exit 1.
- [ ] **Nonexistent** input path → exit 2, log written (exe folder, since input
      folder is unknown/invalid).
- [ ] **Missing/empty** `PdfUnlocker.config` with an *encrypted* input → exit 3.
- [ ] **Corrupt / non-PDF** file → treated as a real error (not "wrong
      password"), logged, exit 10.
- [ ] AES‑256 (revision‑5) encrypted PDF opens correctly (confirms the 6.2.0+
      requirement is met).
- [ ] Successful run leaves **no** `PdfUnlocker.log` behind.
- [ ] Launched via *Send To* and *Open With* from Explorer (no console flash if
      `WinExe`), output appears beside the source PDF.
- [ ] Runs on a machine **without** the .NET runtime installed.

---

## 10. Acceptance criteria

The build is complete when:

1. A single self-contained `PdfUnlocker.exe` builds and runs per §7.
2. It fulfils §4 exactly: argument contract, exe-folder config lookup,
   in-order password trial, `_unlock` output naming with overwrite, and the
   create-then-delete-on-success log behaviour located next to the input file
   (exe folder as fallback).
3. Wrong passwords are cleanly distinguished from corrupt/non-PDF files (§5/§6).
4. Exit codes match §4.6.
5. Every box in §9 passes.
6. `README.md` documents installation (§8), the import-copy limitation (§6.2),
   and how to switch `TargetFramework` and console/WinExe mode.

---

## 11. Notes for the implementer

- Keep all the magic strings (`"PdfUnlocker.config"`, `"PdfUnlocker.log"`,
  `"_unlock"`) as named constants in one place.
- Prefer small, separately testable methods (config parsing, output-name
  construction, password trial) so the test checklist in §9 can be exercised
  without a GUI.
- Comment the two non-obvious decisions explicitly in code: (a) why
  `AppContext.BaseDirectory` rather than `Assembly.Location`, and (b) why
  passwords are not `Trim()`-med.
- Anywhere this doc says **VERIFY**, check it against the installed PDFsharp /
  .NET SDK version and adjust — do not hard-code an assumption that a quick test
  could confirm or refute.
