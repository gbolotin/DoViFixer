# DoViFixer

[GPL-3.0 license](LICENSE)

Native Windows .NET 10 console workflows for inspecting Dolby Vision MKVs, converting Profile 7 to Profile 8.1 or HDR10, and backing up/restoring enhancement layers. The shared Domain, Application and Infrastructure projects are ready for a later WPF presentation layer.

DoViFixer is a native Windows .NET implementation inspired by [dovi_convert](https://github.com/cryptochrome/dovi_convert), adapting its Dolby Vision workflows for a shared console and future WPF architecture.

## Build and run

Requires the .NET 10 SDK; `global.json` pins 10.0.400. NuGet versions are centralized in `Directory.Packages.props`.

```powershell
dotnet restore DoViFixer.sln
dotnet build DoViFixer.sln --no-restore
dotnet run --project src/DoViFixer.Console --no-build -- --help
dotnet run --project src/DoViFixer.Console --no-build -- dependencies check
```

Help and argument validation do not start the host, probe executables, install tools, or create settings/workspaces.

## Dependencies

The app validates FFmpeg, ffprobe, mkvmerge, mkvextract, MediaInfo **CLI**, and dovi_tool. Existing compatible installations are reused. Invalid configured paths are reported instead of silently replaced. MediaInfo GUI does not satisfy the CLI requirement.

```powershell
dotnet run --project src/DoViFixer.Console -- dependencies install
dotnet run --project src/DoViFixer.Console -- dependencies install DoviTool --yes
dotnet run --project src/DoViFixer.Console -- settings tool DoviTool 'C:\Tools\dovi_tool.exe'
```

Installation presents exact versions, executable groups, source URLs, hashes, scope and destination before consent. Plans include missing, unusable and incompatible tools, showing existing paths that DoViFixer will replace in its configuration. Existing executables are retained, and replacement paths are saved only after successful validation. `dependencies install --yes` supplies installation-specific consent. Interactive media commands offer setup when required tools are unmet; unattended commands need `--install-dependencies` explicitly. Conversion `--yes` alone never approves installation. `--repair` remains accepted for compatibility but is no longer required.

The maintained x64 catalog uses per-user portable ZIPs, prefers a matching WinGet route when its published URL/digest can be confirmed, and otherwise downloads a ZIP checked against a pinned SHA-256. No package manager or compiler is installed. Successful installs are independently re-detected, persisted, and used immediately through absolute paths. See [dependency source details](docs/upstream-parity.md#dependency-sources).

## Media commands

```powershell
# Read-only metadata/RPU analysis (temporary extraction is cleaned up).
dotnet run --project src/DoViFixer.Console -- scan 'E:\Movies' -r 3
dotnet run --project src/DoViFixer.Console -- inspect 'E:\Movies\Movie.mkv' --json

# Plan exact inputs/outputs first; use existing output/temp directories.
dotnet run --project src/DoViFixer.Console -- convert 'E:\Movies' -r --plan
dotnet run --project src/DoViFixer.Console -- convert 'E:\Movies\Movie.mkv' --backup --output 'E:\Converted'
dotnet run --project src/DoViFixer.Console -- convert 'E:\Movies\Movie.mkv' --hdr10 --yes

dotnet run --project src/DoViFixer.Console -- backup 'E:\Movies\Movie.mkv'
dotnet run --project src/DoViFixer.Console -- restore 'E:\Movies\Movie.dv81.mkv' 'E:\Movies\Movie.dovi'

# List backups; deletion is a separate explicit choice.
dotnet run --project src/DoViFixer.Console -- cleanup 'E:\Movies' -r
dotnet run --project src/DoViFixer.Console -- cleanup 'E:\Movies\Movie.dovi' --delete-backups
```

Outputs use `.dv81.mkv`, `.hdr10.mkv`, `.dovi`, or `.restored.mkv` suffixes. Originals stay at their original paths. Existing outputs are never overwritten. Conversion does not delete media; cleanup accepts only exact planned `.dovi` or `.bak.dovi_convert` backup files, revalidates them, and requires an `APPROVE <code>` response. Unattended permanent cleanup requires both `--delete-backups --yes`.

Scan samples ten positions; inspect and conversion planning analyze the full RPU stream. MEL is eligible. Interactive conversion asks once per batch about Simple FEL when `--include-simple` is absent, and separately about Complex FEL when `--force` is absent. Answering no skips that FEL type and continues planning the other files, including MEL. The final conversion-plan confirmation still applies. With `--yes`, `--plan`, or redirected input, the existing flag-based selection applies: Simple FEL requires `--include-simple`; detected complex FEL requires `--force`. Unknown/failed analysis remains blocked even with `--force`. Missing MaxCLL stays unknown. L1-versus-MaxCLL classification is a metadata heuristic, not a base-layer frame measurement or playback guarantee.

All processing currently uses disk extraction; `--safe` is accepted as an explicit synonym for this default. Workspaces are owned per operation. Estimates are deliberately conservative: full inspection/conversion/backup currently reserve eight input sizes plus 1 GiB, with an additional archive allowance for restoration. Destination capacity is checked independently. This trades additional free-space requirements for room for extraction, RPU JSON, payload verification and publication; actual disk usage is usually lower.

Before publication, verification checks the target profile and full RPU count/profile (or absence for HDR10), video dimensions/codec and packet counts, per-track timestamps, audio/subtitle payload hashes, normalized base-layer hashes, attachments, chapters, tags and supported video container metadata. Failed or unavailable checks prevent publication. MKVToolNix may normalize `DefaultDuration`; actual timestamps are compared within 1 ms and video duration within 50 ms. Non-pixel Matroska display units are currently rejected. Full MKV byte identity is not promised.

Batch failures are reported per file and do not stop subsequent files. Ctrl+C stops the batch, terminates owned native processes, and disposes temporary resources. A cancelled external installer can leave package-manager changes; run `dependencies check` before retrying. Successfully published enhancement archives remain available if a subsequent conversion fails.

## Archives and settings

`.dovi` is an uncompressed TAR containing `el.hevc` and a versioned `manifest.json`. The manifest binds the archive to a SHA-256 of the normalized base layer and records the enhancement-layer length/hash. Reading rejects unknown entries, duplicate entries, traversal paths, links, corrupt payloads and unsupported versions. Legacy upstream `el.hevc`-only TARs require `--allow-legacy-archive`; their source pairing is explicitly unverified.

Settings live in `%LOCALAPPDATA%\DoViFixer\settings.json`. Writes are atomic and serialized across processes. `settings tool`, `settings reset-tool`, and `settings temp` persist changes. Command-line `--temp`/`--output` overrides are operation-local. `DoViFixer__DataDirectory` can select an isolated data location for testing. Registration itself does not create that location.

To run from any directory, run `.\DoViFixer.Console.exe settings add-to-path` from the application folder once. This adds that folder to your user PATH without administrator rights, preserves existing entries, and avoids duplicates. It also updates the running app's PATH. Reopen your terminal (and its host app if needed), then use `DoViFixer.Console --help` from any directory. Keep the executable and its supporting files in the registered folder. This command does not change the system PATH or install native dependencies.

`update-check` reports the latest **upstream dovi_convert** release against the reviewed 8.2.0 baseline. There is no configured DoViFixer release feed or automatic self-update.

Exit codes: `0` success, `1` failure/partial batch, `2` invalid arguments, `3` unmet dependencies, `4` declined/no selected work, `130` cancellation.

## Logging and operation history

Serilog writes local, structured JSON Lines logs to `%LOCALAPPDATA%\DoViFixer\Logs` (or `Logs` under `DoViFixer__DataDirectory`). Each line is a complete JSON event with its timestamp, level, rendered message, structured properties, and full exception when present.

| File | Contents | Default retention |
| --- | --- | --- |
| `history-YYYYMMDD.jsonl` | Command and media-operation starts, completion/failure/cancellation, elapsed time, and per-file conversion results | 90 files |
| `audit-YYYYMMDD.jsonl` | Presented plans, approval/decline and approval method, published outputs, backup deletion, package installation, and persisted settings changes with previous/new values | 90 files |
| `diagnostic-YYYYMMDD.jsonl` | Debug and higher events, including history/audit, stack traces, analysis decisions, tool paths/versions/arguments/exit codes/timing, bounded stdout/stderr tails, workspace lifecycle, and publication retries | 14 files |

Files roll daily and at 10 MiB; size rolls add a sequence suffix. Retention is a **file count**, not a number of days. History and audit remain enabled when diagnostic verbosity is reduced. Warnings/errors go to stderr; progress and command results remain under the console renderer, including valid `--json` stdout. File writes are unbuffered at the Serilog sink, and host disposal closes the logger. Concurrent processes use shared file access.

Every event carries `SessionId`, `ProcessId`, application version, Windows user name, and machine name. Commands add `CommandId`; workflows add `OperationId`, operation name, and input. Conversion batches add `BatchId`, and native invocations add `ToolInvocationId`. Plan IDs tie approvals to exact prepared inputs/outputs and execution. An inspection nested inside planning gets its own operation ID while retaining the command ID. An operation with only a `Started` record may have been interrupted or lost its terminal record; it does not prove completion.

Configuration uses the Generic Host configuration sources (`appsettings.json` or environment variables):

| Configuration key under `DoViFixer:Logging` | Default |
| --- | --- |
| `Directory` | `Logs` under the data directory |
| `DiagnosticLevel` | `Debug` (`Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`) |
| `FileSizeLimitBytes` | `10485760` |
| `DiagnosticRetainedFiles` | `14` |
| `HistoryRetainedFiles` | `90` |
| `AuditRetainedFiles` | `90` |

For example, `DoViFixer__Logging__DiagnosticLevel=Information` reduces diagnostic detail on the next launch. Values must be valid levels or positive integers. An unusable log directory fails startup before command execution; later Serilog write failures are reported to stderr. Help and invalid command-line arguments do not initialize logging or create files.

To inspect failed operations in PowerShell:

```powershell
$logs = Join-Path $env:LOCALAPPDATA 'DoViFixer\Logs'
Get-Content (Join-Path $logs 'history-*.jsonl') |
    ConvertFrom-Json |
    Where-Object { $_.Properties.Outcome -in 'Failed', 'Partial' } |
    Select-Object Timestamp, RenderedMessage, Properties
```

For a bug report, find the failed event's `SessionId`, then collect matching events from the diagnostic files along with the command, expected behavior, and actual result. Logs can include local paths, user/machine names, and native-tool arguments; review them before sharing. Logging makes no automatic uploads. Audit records are ordinary local files subject to retention, not a tamper-proof ledger or a substitute for media verification.

## Tests

```powershell
dotnet test DoViFixer.sln --no-restore --filter 'TestCategory!=NativeIntegration'
```

Fast tests use small fixtures and fake external boundaries. They cover domain eligibility, missing evidence, dependency coordination, changed inputs, partial batches, cancellation/disposal, settings concurrency, TAR integrity, publication collisions and CLI composition/arguments.

Native integration is opt-in and uses tiny public dovi_tool fixtures plus generated audio/subtitles; it never scans a media library or installs software:

```powershell
$env:DOVIFIXER_TEST_MKVMERGE = 'C:\Tools\MKVToolNix\mkvmerge.exe'
$env:DOVIFIXER_TEST_MKVEXTRACT = 'C:\Tools\MKVToolNix\mkvextract.exe'
$env:DOVIFIXER_TEST_DOVITOOL = 'C:\Tools\dovi_tool.exe'
$env:DOVIFIXER_TEST_FFPROBE = 'C:\Tools\ffprobe.exe'
dotnet test tests/DoViFixer.Infrastructure.Tests --no-restore --filter TestCategory=NativeIntegration
```

These tests execute native conversion, HDR10 removal, backup/restore, and the verification pipeline with controlled MediaInfo JSON. They cover Unicode/punctuation in paths, timestamps, HDR container fields, and fixtures with/without audio, subtitles, tags, chapters and attachments. Actual MediaInfo CLI execution, real installer execution, and large commercial FEL titles remain integration checks for an explicitly configured environment. No native dependencies were installed during implementation.

See [architecture](docs/architecture.md), [upstream parity and limitations](docs/upstream-parity.md), and [fixture provenance](tests/DoViFixer.Infrastructure.Tests/Fixtures/README.md).
