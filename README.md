# DoViFixer

[GPL-3.0 license](LICENSE)

Native Windows .NET 10 console and WPF applications for inspecting Dolby Vision MKVs, converting Profile 7 to Profile 8.1 or HDR10, and backing up/restoring enhancement layers. Both applications use the same Domain, Application and Infrastructure services, as does an [MCP server](docs/mcp.md) that lets an AI assistant such as Claude Desktop run these operations for you.

DoViFixer is a native Windows .NET implementation inspired by [dovi_convert](https://github.com/cryptochrome/dovi_convert), adapting its Dolby Vision workflows for a shared console and WPF architecture.

## Build and run

Requires the .NET 10 SDK; `global.json` pins 10.0.400. NuGet versions are centralized in `Directory.Packages.props`.

```powershell
dotnet restore DoViFixer.sln
dotnet build DoViFixer.sln --no-restore
dotnet run --project src/DoViFixer.App --no-build
dotnet run --project src/DoViFixer.Console --no-build -- --help
dotnet run --project src/DoViFixer.Console --no-build -- dependencies check
```

Help and argument validation do not start the host, probe executables, install tools, or create settings/workspaces.

The WPF app uses the [WpfFoundation](https://github.com/gbolotin/WpfFoundation) package, and `DoViFixer.sln` always builds against the pinned version. When the WpfFoundation repository is checked out next to this one (`..\WpfFoundation`), `DoViFixer.Local.sln` opens both together and builds against the library's source project instead. Release builds also pass the switch explicitly so they never pick up local library code:

```powershell
dotnet build DoViFixer.sln -c Release -p:UseLocalWpfFoundation=false
```

## Versioning

Every DoViFixer assembly gets one [SemVer](https://semver.org) version from `VersionPrefix` in `Directory.Build.props`. Local builds are `<VersionPrefix>-dev`, pull request builds are `<VersionPrefix>-ci.<run number>`, and each push to main builds `<VersionPrefix>` itself. The SDK appends the commit hash, so the version also says which commit a build came from. `DoViFixer.Console --version`, the help header, Settings › About, and every log entry (`ApplicationVersion`) show it.

Every push to main is a release: CI tags the commit `v<VersionPrefix>` and creates a GitHub release named after the tag, with `DoViFixer.App-X.Y.Z.zip`, `DoViFixer.Console-X.Y.Z.zip` and `DoViFixer.Mcp-X.Y.Z.zip` attached (framework-dependent; they need the .NET 10 Desktop Runtime). So bump `VersionPrefix` in every pull request; CI fails on main when that version is already tagged. A version with a pre-release suffix, such as `0.2.0-beta.1`, is marked as a pre-release. Pushing a `vX.Y.Z` tag by hand still builds and releases `X.Y.Z`. Every CI run also attaches the zips to the run as an `apps` artifact.

## WPF desktop application

Phase 4 follows the Studio v3 Media mockups and v2 Backup & Restore / Settings designs. Launch `src/DoViFixer.App/bin/Debug/net10.0-windows10.0.19041.0/DoViFixer.App.exe` after building. See [WPF implementation and verification](docs/wpf.md).

- **Media:** add MKV files or folders (subfolders included, up to 100 levels), then independently Scan, Inspect (standard/deep), or Convert. Focus controls the right details panel; checkboxes control inclusion. Convert automatically performs full analysis and opens the bottom options/review panel.
- **Approval:** changing selection, target, destination, or file handling invalidates the plan. Use **Review changes**, then **Approve and start** for the displayed paths and warnings. FEL picture loss and replacement of originals are included in that approval. Native-tool installation has its own separate plan and approval.
- **Cancellation:** row Cancel waits for that file's recovery before continuing. Skip unchecks a pending file. Cancel batch stops subsequent items. Closing the application cancels and awaits active work before disposing its resources.
- **Backup & Restore:** review archive and restoration plans, or review exact cleanup targets with a typed deletion confirmation.
- **Settings:** Conversion includes **Use disk-based conversion (Safe mode)** (disk extraction from the first attempt) and **Convert to HDR10** (removes Dolby Vision metadata). The saved target controls the main Convert button and individual file conversion; there is no per-operation target menu. With Safe mode off, streaming retains automatic disk fallback for eligible failures. **Subfolder search depth** controls newly added folders from 0 (selected folder only) to 100 nested levels; the default remains 100. Settings also provides shared temporary storage, output and retention defaults, dependency installation and validated tool paths. Safety has four verification levels (default: frame count with mismatch recount), a Recycle Bin option for deleted originals and approved backups, and a 20% extra free-space margin. Defaults never authorize execution. WPF logs are rolling `app-diagnostic-`, `app-history-` and `app-audit-` JSONL files under `%LOCALAPPDATA%\DoViFixer\Logs`.

### Persistent media library

The desktop app remembers added files in `%LOCALAPPDATA%\DoViFixer\library.db` (SQLite). Previously added files return to the Media list after restart. Scan and inspection evidence, conversion results and per-file history are stored there. Select a file and expand **Library history** to view its recorded events.

File identity uses a versioned SHA-256 fingerprint of the file length and three 8 MiB windows (start, middle and end). Files up to 24 MiB are read entirely. Unchanged paths reuse their recorded fingerprint while size and modification time match. This is collection identification, not a full-file integrity check. Renames in the last known folder are recovered on library load and before cleanup; a move to another folder is recognized when that location is added. Identical copies share one record at the last observed path.

**Remove**, **Remove missing files**, and **Clear all** also forget the affected library records, including saved analysis and history. Removed files stay out of the list after restart. Media and preview files are kept. Clear all affects only files in the current list.

**Automatically scan added files** keeps its existing behavior. Loading remembered files does not start a new scan. **Reuse saved results for Scan and Convert** controls reuse during Scan and Convert; Inspect and Deep Inspect always analyze again and save successful results; changing analysis settings prevents incompatible results from being reused. Full inspection evidence takes priority over a sampled scan.

Settings → Performance → **Clean library** removes only records whose files can be confirmed missing. Existing media, existing-file analysis, and frame previews remain unchanged. Inaccessible files are retained. Current missing rows keep their existing presentation until removed from the list or the app is restarted.

Valid analysis from the old JSON cache is imported on the first library load when its source still exists and matches the stored identity. The original JSON files remain intact. This database uses DoViFixer's own schema; it does not read or modify DoviConvert's database. The configured `DoViFixer:DataDirectory` relocates the database along with the other application data.

## Dependencies

The app validates FFmpeg, ffprobe, mkvmerge, mkvextract, mkvpropedit, MediaInfo **CLI**, and dovi_tool. Existing compatible installations are reused. Invalid configured paths are reported instead of silently replaced. MediaInfo GUI does not satisfy the CLI requirement.

```powershell
dotnet run --project src/DoViFixer.Console -- dependencies install
dotnet run --project src/DoViFixer.Console -- dependencies install DoviTool --yes
dotnet run --project src/DoViFixer.Console -- settings tool DoviTool 'C:\Tools\dovi_tool.exe'
```

Installation presents exact versions, executable groups, source URLs, hashes, scope and destination before consent. Plans include missing, unusable and incompatible tools, showing existing paths that DoViFixer will replace in its configuration. Existing executables are retained, and replacement paths are saved only after successful validation. `dependencies install --yes` supplies installation-specific consent. Interactive media commands offer setup when required tools are unmet; unattended commands need `--install-dependencies` explicitly. Conversion `--yes` alone never approves installation. `--repair` remains accepted for compatibility but is no longer required.

The maintained x64 catalog uses per-user portable ZIPs, prefers a matching WinGet route when its published URL/digest can be confirmed, and otherwise downloads a ZIP checked against a pinned SHA-256. No package manager or compiler is installed. Successful installs are independently re-detected, persisted, and used immediately through absolute paths. See [dependency source details](docs/upstream-parity.md#dependency-sources).

## Media commands

Successful `scan` and `inspect` results persist in `%LOCALAPPDATA%\DoViFixer\library.db` (or under the configured `DoViFixer:DataDirectory`). Library hits skip native analysis and temporary extraction. Results are checked against content identity, analysis options, method, and format/algorithm version. Changed or incompatible results are analyzed again. Source read leases and conversion verification still apply.

Full RPU inspection results can serve later scans and conversion planning. A sampled scan can supply saved probe metadata for conversion, but conversion still completes full RPU analysis once. Deep inspection can serve later scans when its options match; full RPU and deep inspection requests retain their own evidence. Inspect and Deep Inspect always run fresh analysis and update the library. Disable **Reuse saved results for Scan and Convert** to also force fresh analysis during Scan and conversion preparation. Unchanged-path identity checks do not detect edits that preserve both size and modification time; quick content fingerprints also do not cover unsampled bytes in large files.

[Deep Inspection](docs/deep-inspection.md) (`inspect --deep`) compares every decoded HDR10 base-layer frame with its RPU L1 brightness metadata. It reports possible FEL brightness expansion without relying on static MaxCLL; full decoding is slower than regular inspection.

`scan --inspect-simple` finishes scanning, then deep-inspects every Simple FEL candidate automatically. It uses the same decoded-frame analysis as `inspect --deep`, updates final verdicts, and retains the initial analysis in JSON. Inspection failures are reported while remaining candidates continue. This optional second pass can take a long time and requires scratch disk space; it performs no conversion. Ordinary scans remain unchanged.

Use **Export selected results** or **Export visible results** on the WPF Media page to save the results already in the list. Visible means the active file filter; selected means checked files within that filter. Both include failed and unscanned rows. The console's optional `--report <path.csv|path.json|path.txt>` saves results from the scan or inspection it just ran. Report files use UTF-8; JSON includes a schema version, CSV quotes cells and guards against spreadsheet formulas, and text labels unavailable measurements. A report does not rescan WPF rows or change conversion rules. Existing destinations are rejected. The report contains source paths and may contain tool errors or other personal information.

```powershell
# Read-only metadata/RPU analysis (temporary extraction is cleaned up).
dotnet run --project src/DoViFixer.Console -- scan 'E:\Movies' -r 3
dotnet run --project src/DoViFixer.Console -- scan 'E:\Movies' -r 3 --inspect-simple
dotnet run --project src/DoViFixer.Console -- inspect 'E:\Movies\Movie.mkv' --json
dotnet run --project src/DoViFixer.Console -- inspect 'E:\Movies\Movie.mkv' --deep --json
dotnet run --project src/DoViFixer.Console -- scan 'E:\Movies' --report 'E:\Reports\scan.csv'
dotnet run --project src/DoViFixer.Console -- inspect 'E:\Movies\Movie.mkv' --deep --report 'E:\Reports\inspection.json'

# Plan exact inputs/outputs first; output directories are created when needed.
dotnet run --project src/DoViFixer.Console -- convert 'E:\Movies' -r --plan
dotnet run --project src/DoViFixer.Console -- convert 'E:\Movies\Movie.mkv' --backup --output 'E:\Converted'
dotnet run --project src/DoViFixer.Console -- convert 'E:\Movies\Movie.mkv' --hdr10 --yes

dotnet run --project src/DoViFixer.Console -- backup 'E:\Movies\Movie.mkv'
dotnet run --project src/DoViFixer.Console -- restore 'E:\Movies\Movie.mkv'
# Or: restore 'E:\Movies\Movie.mkv' --source 'F:\Backups\Movie.dovi'

# List backups; deletion is a separate explicit choice.
dotnet run --project src/DoViFixer.Console -- cleanup 'E:\Movies' -r
dotnet run --project src/DoViFixer.Console -- cleanup 'E:\Movies\Movie.dovi' --delete-backups
```

Conversion accepts multiple files/directories and keeps each original at its exact existing path, unchanged. Converted files are published beside their originals as `<stem> - DV P8.1.mkv` (or `<stem> - HDR10.mkv` with `--hdr10`). `--output` selects another output directory while retaining the suffix. No movie subfolders are created and existing outputs are never overwritten. This preserves seeding paths and allows Plex to group matching movies in the same library; suffixes are not displayed as Plex version labels. `--backup` additionally creates an enhancement-layer `.dovi` archive. Explicit `--delete` retains the replacement workflow: rename the original to `<filename>.bak.dovi_convert`, publish using its original filename, and delete only the original backup after verification and publication. Failed/cancelled replacement restores the original path; blocked recovery reports the retained backup. Default conversion never renames or deletes the original. Backup and restore outputs use `.dovi` and `.restored.mkv`. Cleanup remains a separate command with exact approval; unattended cleanup requires `--delete-backups --yes`.

Scan samples evenly spaced clips (10 by default); inspect and conversion planning analyze the full RPU stream. MEL is eligible. Interactive conversion prepares eligible MEL, Simple FEL, and Complex FEL plans and displays output and backup details before asking for approval. Each category receives one execution confirmation covering all its planned files; there is no additional batch confirmation. Answering no skips that category while approved categories continue. FEL confirmations warn that enhancement-layer picture data will be lost. With `--yes`, `--plan`, or redirected input, the existing flag-based selection applies: Simple FEL requires `--include-simple`; detected complex FEL requires `--force`. Unknown/failed analysis remains blocked even with `--force`. Settings → Analysis controls the brightness margin (50 nits by default) and assumed peak brightness (1,000 nits by default). Metadata modes prefer valid MaxCLL of at least 100 nits; missing, invalid, or lower MaxCLL uses the assumed peak and labels the result as assumed. `inspect --deep` uses measured frame luminance and the configured margin instead. Settings changes invalidate cached analysis for future operations; existing displayed results require a new scan or inspection. L1-versus-MaxCLL classification is a metadata heuristic, not a base-layer frame measurement or playback guarantee.

Conversion streams FFmpeg output directly to dovi_tool by default and retries with disk extraction on streaming or verification failure. `--safe` forces disk extraction. Ordinary full RPU inspection also streams its selected video track into dovi_tool. It uses disk extraction if streaming fails or frame counts differ, so metadata-free tail checks still inspect the raw video. Deep inspection, archive operations and output verification continue to use disk. [Inspection validation](docs/streamed-inspection-validation.md) records native parity and measurements. Streaming does not remove the scratch-space requirement. Workspaces are owned per operation. Estimates are deliberately conservative: full inspection/conversion/backup currently reserve eight input sizes plus 1 GiB, with an additional archive allowance for restoration. Destination capacity is checked independently. This trades additional free-space requirements for room for extraction, RPU JSON, payload verification and publication; actual disk usage is usually lower.

Before publication, verification checks the target profile and full RPU count/profile (or absence for HDR10), video dimensions/codec and packet counts, per-track timestamps, audio/subtitle payload hashes, normalized base-layer hashes, attachments, chapters, tags and supported video container metadata. Failed or unavailable checks prevent publication. After remuxing, mkvpropedit restores the source video track’s `DefaultDuration` without changing packet timestamps or payloads. Verification rejects a changed `DefaultDuration`; actual timestamps are compared within 1 ms and video duration within 50 ms. Non-pixel Matroska display units are currently rejected. Full MKV byte identity is not promised.

Batch failures are reported per file and do not stop subsequent files. Ctrl+C stops the batch, terminates owned native processes, and disposes temporary resources. A cancelled external installer can leave package-manager changes; run `dependencies check` before retrying. Successfully published enhancement archives remain available if a subsequent conversion fails.

Each native-tool invocation owns a Windows Job Object; both processes in a streaming pipeline share one job. Closing that job terminates its assigned processes and descendants, including when the application crashes or is forcibly closed. Separate invocations have separate jobs. Job creation or assignment failure stops the invocation rather than silently running without protection. This applies to processes launched through the shared runner; services or installers launched independently by another system process are outside its ownership.

Protection starts when the process is assigned to the job immediately after `.NET Process.Start`. A crash, process exit, or child launch in the brief start-to-assignment interval can escape this protection. Eliminating that interval requires a native launcher with atomic job assignment. An abnormal application exit also cannot run managed workspace cleanup, so temporary files may remain. See [Windows Job Objects](https://learn.microsoft.com/windows/win32/procthread/job-objects) for the operating-system behavior.

In the WPF app, Settings → Performance → Simultaneous conversions controls how many selected files convert at once (1–16). The recommended value uses physical CPU cores: one worker per four cores, limited to 1–8. Existing saved counts remain selected. Every selected file enters the batch queue, so the worker count does not limit batch size. Pause prevents queued files from starting while active conversions finish. Each active conversion has its own temporary workspace. Concurrent conversions reserve conservative scratch and output disk budgets before execution; a queued worker waits if another active conversion is using the available space.

The same Performance page groups concurrency, system impact, and scanning/library settings without reset buttons. Concurrent scan clips runs up to 10 clip probes concurrently inside one video; videos and full inspections remain sequential. Set the total clip count under Analysis. The recommended scan value uses three workers per four physical cores, limited to 1–10. The actual concurrency cannot exceed the selected clip count. If Windows cannot report physical cores, the page identifies the conservative fallback estimate. CPU recommendations are starting points: lower the counts for mechanical disks, limited temporary storage, or falling throughput.

Tool process priority applies to native tools started afterward; Below normal is the default. Video pipe buffer size is shown in KiB (4096 KiB = 4 MiB by default). Transformed pipelines buffer both reads and writes, so larger values consume more memory per active pipeline. Reuse saved results for Scan and Convert uses the library's compatible evidence for Scan and Convert. Inspect and Deep Inspect always analyze again; successful results are saved with either setting. All settings save automatically.

### Analysis tuning

Settings → Analysis separates quick-scan sampling from full metadata inspection.

| Setting | Default | Allowed values | Applies to |
| --- | --- | --- | --- |
| Clips per video | 10 clips | 1–100, whole numbers | Scan |
| Clip length | 1 second | 0.1–60 seconds | Scan |
| Required scan coverage | 100% (all clips) | 1–100%, whole numbers | Scan |
| Peak-brightness percentile | 100% (maximum) | 1–100%, fractional values allowed | Inspect and conversion planning |

Clips start at the midpoint of each equal-duration segment: ten clips start at 5%, 15%, …, 95%.
Clip length is the requested FFmpeg stream-copy duration; actual boundaries can differ.
Required coverage counts successful clips, not the percentage of video frames inspected.
The minimum count rounds up: 50% of seven clips requires four successful clips.
Missing clips remain visible in diagnostics and reports even when the selected requirement is met.
A lower requirement can miss brightness expansion. Scan evidence does not authorize conversion.

The inspection percentile selects the nearest-rank value from per-frame Dolby Vision L1 peak metadata.
100% uses the maximum. For example, with 1,000 measured frames, 99.9% excludes the single brightest value.
Small files may still select their maximum. This is a metadata percentile, not a pixel-brightness measurement.
Scan continues to use clip maxima. Deep Inspection compares every frame without percentile filtering.
Lower percentiles can hide real brightness expansion. Defaults preserve the previous behavior.

These settings save automatically, have individual Reset buttons, and use native validation outlines.
Units appear inside numeric inputs as non-editable suffixes. Empty inputs show default-value hints without changing their values.
Folder and tool-path inputs show placeholders. WpfFoundation's `TextBoxAssist` supplies both behaviors.
Changes invalidate cached analysis for future operations. Existing displayed results are unchanged until analyzed again.

## AI assistants (MCP)

`DoViFixer.Mcp` exposes scanning, inspection, conversion, backup, restore, cleanup, dependency and settings operations as Model Context Protocol tools. Anything that changes files is planned first and runs only after you approve the plan. See [MCP server](docs/mcp.md) to register it in Claude Desktop or Claude Code.

## Archives and settings

`.dovi` is an uncompressed TAR containing `el.hevc` and a versioned `manifest.json`. The manifest binds the archive to a SHA-256 of the normalized base layer and records the enhancement-layer length/hash. Reading rejects unknown entries, duplicate entries, traversal paths, links, corrupt payloads and unsupported versions. Restore accepts upstream `el.hevc`-only TARs directly; their source pairing is explicitly unverified. It discovers the adjacent same-stem `.dovi` archive, or uses `--source <archive.dovi>`. The former second positional archive and `--allow-legacy-archive` remain accepted for compatibility.

Settings live in `%LOCALAPPDATA%\DoViFixer\settings.json`. Writes are atomic and serialized across processes. `settings tool`, `settings reset-tool`, and `settings temp` persist changes. Command-line `--temp`/`--output` overrides are operation-local. `DoViFixer__DataDirectory` can select an isolated data location for testing. Registration itself does not create that location.

To run from any directory, run `.\DoViFixer.Console.exe settings add-to-path` from the application folder once. This adds that folder to your user PATH without administrator rights, preserves existing entries, and avoids duplicates. It also updates the running app's PATH. Reopen your terminal (and its host app if needed), then use `DoViFixer.Console --help` from any directory. Keep the executable and its supporting files in the registered folder. This command does not change the system PATH or install native dependencies.

`update-check` compares the running version with the latest published [DoViFixer release](https://github.com/gbolotin/DoViFixer/releases) and links to it when it is newer. Until a release is published it says so and exits successfully. It never downloads or installs anything.

Exit codes: `0` success, `1` failure/partial batch, `2` invalid arguments, `3` unmet dependencies, `4` declined/no selected work, `130` cancellation.

## Logging and operation history

Serilog writes local, structured JSON Lines logs to `%LOCALAPPDATA%\DoViFixer\Logs` (or `Logs` under `DoViFixer__DataDirectory`). Each line is one event in Serilog's compact JSON format (CLEF): `@t` timestamp, `@m` rendered message, `@l` level (left out for Information), `@x` full exception when present, and the structured properties as top-level fields. [Seq](https://datalust.co/seq) and [Compact Log Viewer](https://github.com/warrenbuckley/Compact-Log-Format-Viewer) open these files directly.

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
    Where-Object { $_.Outcome -in 'Failed', 'Partial' } |
    Select-Object '@t', '@m', OperationId, Outcome |
    Out-GridView
```

For a bug report, find the failed event's `SessionId`, then collect matching events from the diagnostic files along with the command, expected behavior, and actual result. Logs can include local paths, user/machine names, and native-tool arguments; review them before sharing. Logging makes no automatic uploads. Audit records are ordinary local files subject to retention, not a tamper-proof ledger or a substitute for media verification.

### Failure explanations and diagnostic ZIPs

Native-tool failures and final output-publication failures include a cause category and recovery guidance. Windows error codes take precedence over tool-output phrases; unfamiliar native errors stay unclassified tool failures. The original exception or bounded tool output remains available, together with the tool invocation identifier. Publication keeps its existing sharing-lock retries. After a final sharing or access failure, [Windows Restart Manager](https://learn.microsoft.com/windows/win32/api/restartmanager/nf-restartmanager-rmgetlist) identifies applications using the destination when possible. Inspection never closes applications. A missing holder list does not prove that the file is unlocked.

In **Settings**, select **Export diagnostics**, or run:

```powershell
DoViFixer.Console diagnostics export "C:\Support\diagnostics.zip"
```

The ZIP contains `snapshot.json`, `manifest.json`, and recent compact JSON logs from the WPF, Console, and MCP apps. The snapshot includes app/runtime versions, explicit saved preferences and tool paths, and versions already known to the current process. Export does not launch tools to discover versions. No environment-variable dump, media files, cache, or arbitrary configuration is included. Logs can contain media paths, user names, machine names, and tool arguments. Nothing is uploaded; review the ZIP before sharing it.

Collection keeps at most **three newest files per log kind** (diagnostic/history/audit across all apps), **2 MiB per log**, and **256 KiB for the settings/tool snapshot**. Log tails contain only complete newline-terminated records. The manifest lists missing, unreadable, trimmed, or changing entries. Active logs are read with sharing enabled and remain unchanged. The command returns `0` for a complete bundle, `1` for a saved bundle with omissions or an export failure (the message distinguishes these), and `130` for cancellation.

The ZIP is staged on the destination volume and published only after completion. Existing files are never overwritten, including a collision during export; choose a new filename. Cancellation removes the owned staging directory. `DoViFixer__Logging__Directory` selects the log directory consistently in all three applications.

## Tests

```powershell
dotnet test DoViFixer.sln --no-restore --filter 'TestCategory!=NativeIntegration'
```

Fast tests use small fixtures and fake external boundaries. They cover domain eligibility, missing evidence, dependency coordination, changed inputs, partial batches, cancellation/disposal, settings concurrency, TAR integrity, publication collisions and CLI composition/arguments.

Windows process-lifetime tests are labelled `ProcessIntegration`. They launch disposable test hosts, not media tools. They cover abrupt host termination, nested jobs, child/grandchild cleanup, concurrent invocation isolation, cancellation, timeout, startup failure, and assignment failure. The test assembly's executable entry point is only a process fixture; use `dotnet test` to run the tests.

```powershell
dotnet test tests/DoViFixer.Infrastructure.Tests -c Release --filter TestCategory=ProcessIntegration
```

Native integration is opt-in and uses tiny public dovi_tool fixtures plus generated audio/subtitles; it never scans a media library or installs software:

```powershell
$env:DOVIFIXER_TEST_MKVMERGE = 'C:\Tools\MKVToolNix\mkvmerge.exe'
$env:DOVIFIXER_TEST_MKVEXTRACT = 'C:\Tools\MKVToolNix\mkvextract.exe'
$env:DOVIFIXER_TEST_MKVPROPEDIT = 'C:\Tools\MKVToolNix\mkvpropedit.exe'
$env:DOVIFIXER_TEST_DOVITOOL = 'C:\Tools\dovi_tool.exe'
$env:DOVIFIXER_TEST_FFPROBE = 'C:\Tools\ffprobe.exe'
$env:DOVIFIXER_TEST_FFMPEG = 'C:\Tools\ffmpeg.exe'
dotnet test tests/DoViFixer.Infrastructure.Tests --no-restore --filter TestCategory=NativeIntegration
```

These tests execute native conversion, HDR10 removal, backup/restore, and the verification pipeline with controlled MediaInfo JSON. They cover Unicode/punctuation in paths, timestamps, HDR container fields, and fixtures with/without audio, subtitles, tags, chapters and attachments. Actual MediaInfo CLI execution, real installer execution, and large commercial FEL titles remain integration checks for an explicitly configured environment. No native dependencies were installed during implementation.

See [architecture](docs/architecture.md), [upstream parity and limitations](docs/upstream-parity.md), and [fixture provenance](tests/DoViFixer.Infrastructure.Tests/Fixtures/README.md).

**Delete library** removes all remembered file records, saved analyses and history, including legacy analysis cache entries. The current list is also cleared. Media files and frame previews remain unchanged. Adding files again can populate the library.
