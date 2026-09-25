# FixletBuilder

![FixletBuilder Banner](screenshots/banner.png)

A production-grade GUI & CLI tool for generating HCL BigFix `.bes` fixlet files with registry-aware detection, prefetch downloads, and action script generation.

**Made by K K Shal**

---

## Features

### Core
- **GUI Application** — WPF-based desktop app with real-time XML preview
- **CLI Interface** — Command-line tool for scripting and automation
- **CSV/Excel Import** — Batch generate fixlets from spreadsheets
- **Installed App Scanner** — Scan machine and auto-generate fixlets from installed software

### Action Script Generation
- **71 BigFix ActionScript commands** integrated with searchable reference
- **Prefetch downloads** with SHA1/SHA256 hash verification
- **Pre-install actions** — Kill processes, stop services before install
- **Registry operations** — regset, regset64, regdelete, regkeydelete
- **Flow control** — if/elseif/else, continue if, pause while, exit
- **File operations** — copy, move, delete, folder create/delete, createfile, appendfile
- **Execution** — wait, waithidden, run, dos, script, override with custom options
- **Client commands** — restart, shutdown, settings, action lock/unlock

### Detection & Relevance
- **5 selectable detection strategies** — MSI Product Code, EXE File Version, Registry Key + DisplayVersion, DisplayName Contains, File/Folder Exists
- **One-click Generate** — pick a method in the Relevance tab, click *Generate Relevance*
- **Combined pattern** — install/upgrade relevance answers "not installed OR installed below the required version"
- **Registry key/value detection** using `native registry` syntax (correct 32/64-bit view)
- **Version comparison** with `as string as version` inspectors
- **Fallback chain** — MSI code → file version → registry key → DisplayName search → folder guess
- **Automatic relevance generation** for install, upgrade, and uninstall fixlets (mirrored success criteria)

### Analyses (retrieved properties)
- **GUI Analysis tab** — property grid with add/remove/reorder/edit + live BES XML preview
- **Templates** — app-status, app-version, software-inventory, os-overview, disk-space, bigfix-components, custom
- **Property snippets** — OS, client version, free disk, registry DisplayVersion, regapp version
- **EvaluationPeriod** — ISO-8601 intervals (`PT15M`, `PT1H`, `P1D`) or friendly `1h`/`daily`
- **KeepStatistics** — enable dashboard/wizard statistical inspection per property
- **Validate + Save .bes** — schema-aware checks (unique Name/ID, non-empty relevance)
- **Publish to BigFix REST** — `POST api/analyses/{site}` with `--what-if` dry-run

### Metadata & Hashes
- **Auto-Fetch (winget)** — App name, version, URL, silent args, install path from the winget catalog
- **Detect on this PC** — reads the installed app from this machine's Uninstall registry: name, version, install path, uninstall string, registry key/value, MSI product code; then regenerates relevance with the selected detection method. Shows an *"App not installed"* popup when the app is not installed on this device
- **Hash computation** — Download installer and compute SHA1, SHA256, file size
- **Registry detection** — Auto-find registry key from installed apps
- **Version monitoring** — Track installed app versions and detect updates

### Security
- **URL validation** — Blocks private IPs, localhost, cloud metadata endpoints (SSRF prevention)
- **Input sanitization** — Proper escaping for winget CLI arguments
- **No telemetry** — No external data connections or tracking

---

## Requirements

- Windows 10/11 (x64)
- .NET 8.0 SDK
- winget (optional, for auto-fetch and version monitoring)

---

## Build & Run

```bash
# Build
dotnet build FixletBuilder.csproj

# Run (GUI)
dotnet run --project FixletBuilder.csproj

# Run (CLI)
dotnet run --project FixletBuilder.csproj -- [options]
```

---

## GUI Quick Start

1. Fill the **Application** panel (name, version, installer URL, install path, MSI product code, registry key).
2. Use one of the two toolbar fetch buttons:
   - **Auto-Fetch (winget)** — metadata from the winget catalog (name, version, URL, silent args, install path).
   - **Detect on this PC** — metadata from this machine's Uninstall registry (name, version, install path, uninstall string, registry key/value, MSI product code), then regenerates relevance with the selected method. Shows an *"App not installed"* popup if the app is not installed here.
3. Open the **Relevance** tab → pick a detection strategy (radio buttons) → **Generate Relevance**. Use *Not installed OR older* to append the generic form.
4. Fill the **Action Script** tab (Quick Insert panel, command reference on **F1**) → **Save** (Ctrl+S) writes the `.bes`.

---

## CLI Usage

```bash
# Create a single fixlet
FixletBuilder.exe create --type install --name "7-Zip" --version "24.09" --url "https://..."

# Choose the detection method (auto, msi, file, registry, displayname, path)
FixletBuilder.exe create -n "7-Zip" -v 25.00 --detection msi --msi-code "{23170F69-40C1-2702-2500-000001000000}"
FixletBuilder.exe create -n "7-Zip" -v 25.00 --detection file -p "C:\Program Files\7-Zip\7z.exe"
FixletBuilder.exe create -n "7-Zip" -v 25.00 --detection registry --registry-key "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\7-Zip"
FixletBuilder.exe create -n "Google Chrome" -v 140.0.0.0 --detection displayname

# Import from CSV
FixletBuilder.exe import --csv packages.csv --output ./fixlets

# Scan installed apps (--detection sets the method for every generated fixlet)
FixletBuilder.exe scan --output ./fixlets --detection registry
FixletBuilder.exe batch -i packages.csv --detection file

# Check for updates
FixletBuilder.exe check-updates --track

# Auto-fetch from winget
FixletBuilder.exe create --name "Chrome" --auto-fetch
```

### `create` / `scan` / `batch` options

| Option | Description |
|--------|-------------|
| `-t/--type` | `install` (default), `upgrade`, `uninstall` |
| `-n/--name`, `-v/--version`, `-u/--url` | App metadata |
| `-p/--path` | Install path (also the `file`/`path` detection target) |
| `--detection` | `auto` (default), `msi`, `file`, `registry`, `displayname`, `path` |
| `--msi-code` | `{GUID}` used by the `msi` method |
| `--registry-key`, `--registry-value-name`, `--registry-value` | Used by the `registry` method (value name defaults to `DisplayVersion`) |
| `--auto-fetch` | Fetch metadata from winget before creating |
| `-o/--output` | Output directory (`scan`, `batch`) |

---

## Analyses (`analysis`)

Create BigFix **Analyses** (collections of retrieved-property relevance expressions — no action script).

```bash
# List built-in templates
FixletBuilder.exe analysis templates

# Create an analysis from a template
FixletBuilder.exe analysis create --template os-overview -o ./analysis-output
FixletBuilder.exe analysis create --template app-status --name "Google Chrome" \
  --registry-key "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{GUID}" -o ./analysis-output
FixletBuilder.exe analysis create --template software-inventory --name "chrome" --period 4h

# Extra properties + targeting relevance + preview
FixletBuilder.exe analysis create --template custom -n "My App" \
  -r "true" -r "name of operating system as lowercase starts with \"win\"" \
  -p "My Prop=name of computer" --what-if

# Publish existing analysis .bes to BigFix REST API
FixletBuilder.exe analysis publish -i ./analysis-output/My\ App\ Analysis.bes --site "Enterprise Windows Patching"
FixletBuilder.exe analysis publish -i ./file.bes --what-if
```

| Option | Description |
|--------|-------------|
| `-t/--template` | `app-status`, `app-version`, `software-inventory`, `os-overview`, `disk-space`, `bigfix-components`, `custom` |
| `-n/--name` | Application / filter name used by templates |
| `-r/--relevance` | Targeting relevance (repeatable; default `true`) |
| `-p/--property` | Extra property `Name=relevance` (repeatable) |
| `--period` | Default EvaluationPeriod (`PT1H`, `1h`, `daily`, `every report`) |
| `--publish` | Publish after create (requires BigFix config) |
| `--what-if` | Print XML only |

**BES XML shape** (from [HCL BigFix Analyses schema](https://developer.bigfix.com/other/platform-api/bes-xml/analyses.html)):

```xml
<BES xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="BES.xsd">
  <Analysis>
    <Title>…</Title>
    <Description><![CDATA[…]]></Description>
    <Relevance>true</Relevance>
    <Property Name="OS name" ID="1" EvaluationPeriod="PT1H">name of operating system</Property>
  </Analysis>
</BES>
```

---

## Monthly Patch Automation (`patch`)

Full Patch Tuesday lifecycle: discovery → validation gate → BES generation → REST publish → groups → staged deploy → compliance.

```bash
# One-shot for Patch Tuesday (discover + validate + generate)
FixletBuilder.exe patch cycle --output ./patch-output

# Or step by step
FixletBuilder.exe patch discover --patch-tuesday --arch x64 -o ./patch-output
FixletBuilder.exe patch discover -k KB5071234 -o ./patch-output
FixletBuilder.exe patch validate -i ./patch-output/patches.json --download
FixletBuilder.exe patch generate -i ./patch-output/patches.json -o ./patch-output/bes
FixletBuilder.exe patch generate -i ./patch-output/patches.json -o ./patch-output/bes --task

# Human approval gate — then publish to BigFix REST API
FixletBuilder.exe patch publish -i ./patch-output/patches.json -o ./patch-output --site "Enterprise Windows Patching"
FixletBuilder.exe patch publish ... --what-if

# Automatic groups: Microsoft Patches/{cycle}/{Pilot|Wave 1|...|OS}
FixletBuilder.exe patch groups -i ./patch-output/patches.json -o ./patch-output --cycle 2026-09

# Staged deployment (after pilot soak)
FixletBuilder.exe patch deploy -i ./patch-output/patches.json -o ./patch-output --stage pilot
FixletBuilder.exe patch deploy ... --stage wave1
FixletBuilder.exe patch deploy ... --stage wave2
FixletBuilder.exe patch deploy ... --stage wave3

# Compliance report (action status + estate install estimate)
FixletBuilder.exe patch compliance -i ./patch-output/patches.json -o ./patch-output -r ./patch-output/compliance.json
```

| Command | Purpose |
|---------|---------|
| `patch cycle` | Discover (Patch Tuesday) + validate + generate `.bes` |
| `patch discover` | Microsoft Update Catalog metadata (KB, URL, SHA1/SHA256, size, CVE) |
| `patch validate` | Gate: KB/OS/arch/URL/hash/relevance/actionscript checks (exit 3 on fail) |
| `patch generate` | Product-specific relevance + prefetch ActionScript → Fixlet or Task |
| `patch publish` | POST BES XML to site, dedupe by KB, `--what-if` |
| `patch groups` | Create monthly automatic group hierarchy |
| `patch deploy` | Pilot → Wave 1/2/3 actions targeting cycle groups |
| `patch compliance` | Action status + installed-KB estimate per patch |

**OS relevance profiles:** Windows 11 24H2/23H2/22H2, Windows 10 22H2, Server 2025/2022/2019/2016 (build-range + arch + CBS KB-not-installed).

**State:** `patch-output/patches.json` and `patch-output/patch-cycle-state.json` (published IDs, action IDs, groups).

CLI runs in your current terminal (attaches to parent console — no extra black window). No GUI is launched when args are present.


---

## CSV Format

| name | version | url | silent_args | type | install_path | uninstall_string | sha1 | sha256 | filesize | registrykey | registryvaluename | registryvalue | detection | msi_product_code | processToKill | serviceToStop |
|------|---------|-----|-------------|------|--------------|------------------|------|--------|----------|-------------|-------------------|---------------|-----------|-----------------|---------------|---------------|
| 7-Zip | 24.09 | https://... | /S | install | C:\Program Files\7-Zip\7z.exe | | abc123 | def456 | 1748480 | HKLM\SOFTWARE\7-Zip | DisplayVersion | 24.09 | file | | | |

`detection` - `auto` (default), `msi`, `file`, `registry`, `displayname`, `path`. `msi_product_code` - `{GUID}` for the `msi` method. A per-row `detection` wins over the `batch --detection` fallback.

---

## Action Script Templates

### Install
```
prefetch {AppName} sha1:{Sha1} size:{FileSize} "{Url}" sha256:{Sha256}
waithidden powershell -Command "Stop-Process -Name '{Process}' -Force -ErrorAction SilentlyContinue"
waithidden sc stop "{Service}"
waithidden msiexec /i "{AppName}" /q
action requires restart
```

### Uninstall
```
waithidden {UninstallString}
action requires restart
```

### Upgrade
```
prefetch {AppName} sha1:{Sha1} size:{FileSize} "{Url}" sha256:{Sha256}
waithidden msiexec /i "{AppName}" /q
action requires restart
```

---

## Detection Methods

Relevance for a deployment Fixlet/Task answers exactly one question:

> **Is this application NOT installed, or is the installed version older than the required version?**

Pick a method with the radio buttons on the **Relevance** tab and click **Generate Relevance**
(the same selector is used by *Apply Template*, CSV import, and the CLI `--detection` option).

| # | Method | `--detection` | Generated relevance (install / upgrade) |
|---|--------|---------------|------------------------------------------|
| 1 | MSI Product Code | `msi` | `not exists products whose (product code of it as string as uppercase = "{GUID}" and version of it >= "25.00" as version) of windows installer` |
| 2 | EXE File Version | `file` | `not exists file "7z.exe" whose (version of it >= "25.00" as version) of folder "7-Zip" of program files folder` |
| 3 | Registry Key + DisplayVersion | `registry` | `not exists keys "7-Zip" whose (exists value "DisplayVersion" of it and value "DisplayVersion" of it as string as version >= "25.00" as version) of keys "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" of native registry` |
| 4 | DisplayName Contains | `displayname` | one expression per hive (HKLM 64-bit, HKLM WOW6432Node, HKCU): `not exists keys whose (exists value "DisplayName" of it and value "DisplayName" of it as string as lowercase contains "7-zip" and exists value "DisplayVersion" of it and value "DisplayVersion" of it as string as version >= "25.00" as version) of keys "..." of native registry` |
| 5 | File / Folder Exists | `path` | `not exists folder "7-Zip" of program files folder` |

**Auto** (default) resolves in that priority: MSI product code → executable file → registry key →
DisplayName search → folder guess. When no key is supplied, FixletBuilder looks one up on the
packaging machine (`...\Uninstall\<subkey>` matching the app name).

The **Detect on this PC** button pre-fills exactly what each method needs: MSI product code
(Uninstall key name), install path, registry key/value — then regenerate with any radio button.

The DisplayName search strips a trailing version/architecture token from the app name
(`7-Zip 26.01 (x64)` → `7-zip`) so machines with an older build still match, while the
`DisplayVersion` comparison decides whether they are outdated.

Uninstall fixlets invert the same checks (`exists ...`), and every method generates a matching
**success criteria** (`exists ...` for install/upgrade, `not exists ...` for uninstall).

**Not installed OR older** — the *Not installed OR older* button appends the generic form for the
current app/version as an extra relevance line:

```
not exists keys whose (
    exists value "DisplayName" of it and
    value "DisplayName" of it as string = "Google Chrome" and
    exists value "DisplayVersion" of it and
    value "DisplayVersion" of it as string as version >= "140.0.0.0" as version
) of keys "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" of native registry
```

| Installed version | Relevance result |
|-------------------|------------------|
| not installed | TRUE (apply) |
| 135.0 | TRUE (apply) |
| 139.9 | TRUE (apply) |
| 140.0 | FALSE (skip) |
| 141.0 | FALSE (skip) |

### Related fields

| Field | Used by |
|-------|---------|
| MSI product code | `msi` — fill `{GUID}` (auto-filled when scanning: the Uninstall key name is a product code) |
| Install path | `file` / `path` — `C:\Program Files\App\app.exe`, `C:\Program Files (x86)\...`, `%LOCALAPPDATA%\...` are mapped to `program files folder`, `program files (x86) folder`, `local appdata folder of current user` |
| Registry key / value name / value | `registry` — value defaults to `DisplayVersion`, compared with `as version` |
| App name / version | `displayname` and the version floor for every method |

Registry checks always use `native registry` for the proper 32/64-bit view:

```
exists key "HKLM\SOFTWARE\..." of native registry
exists value "DisplayVersion" whose (it as string as version >= "1.0" as version) of key "HKLM\SOFTWARE\..." of native registry
```

---

## Quick Insert Panel

20 one-click snippet buttons in the Action Script tab:

| Pre-Install | Post-Install | Flow Control | Registry |
|-------------|--------------|--------------|----------|
| Kill Process | Restart (delay) | Continue If | Set Registry |
| Stop Service | Shutdown (delay) | Pause While | Set Reg 64 |
| Delete File | Action Restart | | Delete Reg Value |
| Delete Folder | Client Restart | | Delete Reg Key |
| Copy File | Force Refresh | | |
| Move File | | | |
| Create Folder | | | |
| Create Config | | | |
| Append File | | | |

---

## Command Reference

Press **F1** or click the **Command Reference** tab to access the searchable reference for all 71 BigFix ActionScript commands with syntax, descriptions, examples, and platform support.

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+S | Save fixlet (or analysis when Analysis tab is active) |
| Ctrl+N | New fixlet |
| Ctrl+Shift+O | Import CSV/Excel |
| F1 | Open Command Reference |

---

## Project Structure

```
FixletBuilder/
├── MainWindow.xaml/.cs      — Main GUI window
├── ImportWindow.xaml/.cs    — CSV/Excel import dialog
├── ScanWindow.xaml/.cs      — Installed app scanner
├── App.xaml/.cs             — Application entry point
├── Core/
│   ├── ActionScriptCommands.cs — 71 command reference + snippets
│   ├── RelevanceBuilder.cs     — 5 detection strategies (MSI/file/registry/name/path)
│   ├── Interfaces.cs           — Scanner / winget / HTTP contracts
│   ├── FixletTemplates.cs      — Action script templates
│   ├── FixletModel.cs          — Fixlet data model
│   ├── FixletFactory.cs        — Build fixlets from CSV/manual input
│   ├── FixletWriter.cs         — Generate BES XML output
│   ├── AnalysisModel.cs        — Analysis + retrieved-property model
│   ├── AnalysisWriter.cs       — Analysis BES XML generation + validation
│   ├── AnalysisTemplates.cs    — Built-in analysis property templates
│   ├── DownloadService.cs      — Download files + compute hashes
│   ├── WingetIntegration.cs    — Winget CLI integration
│   ├── InstalledAppsScanner.cs — Scan installed applications
│   ├── AppVersionMonitor.cs    — Track app versions for updates
│   ├── BigFixConsoleClient.cs  — BigFix REST API client (fixlets + analyses)
│   ├── CliRunner.cs            — CLI argument parser
│   ├── AppSettings.cs          — JSON config persistence
│   ├── PatchFactory/           — Patch Tuesday discovery/validation/publish
│   └── ...
├── Resources/
│   └── app.ico                 — Application icon
└── appsettings.json            — Configuration file
```

---

## Configuration

Edit `appsettings.json`:

```json
{
  "BaseUrl": "https://bigfix.example.com:52311",
  "Username": "admin",
  "Password": "",
  "SkipCertificateValidation": false,
  "TimeoutSeconds": 30
}
```

---

## License

MIT License — Copyright (c) 2026 K K Shal
