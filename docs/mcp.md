# MCP server

`DoViFixer.Mcp` is a [Model Context Protocol](https://modelcontextprotocol.io) server, so an AI assistant such as Claude Desktop, Claude Code, Codex or Hermes Agent can scan, inspect, convert, back up and restore media on this PC when you ask it to. It runs the same Application and Infrastructure services as the console and WPF apps, over standard input and output, built on the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## Install and connect an assistant

The user guide [AI assistants](https://gbolotin.github.io/DoViFixer/ai-assistants/) (source: [`docs-site/ai-assistants.md`](../docs-site/ai-assistants.md)) covers each assistant step by step: install with setup, manual configuration, how to check it works and how to remove it.

| Assistant | Configuration | Setup adds it |
| --- | --- | --- |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` (Store version: `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude`), `mcpServers` | Yes |
| Claude Code | `claude mcp add --scope user dovifixer -- <exe>` (`%USERPROFILE%\.claude.json`) | Yes |
| Codex app, CLI and IDE extension | `codex mcp add dovifixer -- <exe>` (`%CODEX_HOME%` or `%USERPROFILE%\.codex\config.toml`, `[mcp_servers.dovifixer]`) | Yes |
| Hermes Agent on Windows | `%HERMES_HOME%` or `%USERPROFILE%\.hermes\config.yaml`, `mcp_servers` | Yes |
| Hermes Agent in WSL | `~/.hermes/config.yaml` with `command: "/mnt/c/Program Files/DoViFixer/Mcp/DoViFixer.Mcp.exe"` | No |
| ChatGPT | Not supported: ChatGPT connects only to remote MCP servers over HTTPS. Use Codex. | — |

Setup installs the server to `C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe`. `DoViFixer.Mcp.exe --register <client>...` and `--unregister [<client>...]` (clients: `claude-desktop`, `claude-code`, `codex`, `hermes`) add or remove only the `dovifixer` entry; setup and the uninstaller use them. They write nothing to standard output and exit with 0 or 1.

Agents in a container or on another machine cannot use the server: it works with this PC's Windows paths and native tools.

### Build from source

Publish the server to a folder of your choice (CI also attaches `DoViFixer.Mcp-<version>.zip` to every run and release; it needs the .NET 10 Desktop Runtime):

```powershell
dotnet publish src/DoViFixer.Mcp -c Release -p:UseLocalWpfFoundation=false -o C:\Tools\DoViFixer.Mcp
```

Do not register `dotnet run` as the server command: build output on standard output corrupts the protocol stream.

### Configuration

The server reads the same Generic Host configuration as the console app, so environment variables such as `DoViFixer__DataDirectory` work. Pass them through the client's `env` setting, for example `"env": { "DoViFixer__DataDirectory": "D:\\DoViFixerTest" }` in Claude Desktop or `[mcp_servers.dovifixer.env]` in Codex.

## What you can ask

- "Scan E:\Movies two folders deep and tell me which films can be converted."
- "Inspect E:\Movies\Movie.mkv in depth."
- "Plan converting the MEL films in E:\Movies to Profile 8.1 with an enhancement archive, then convert them once I agree."
- "Restore Profile 7 for E:\Movies\Movie - DV P8.1.mkv."
- "Are all the tools DoViFixer needs installed?"

## Tools

| Tool | What it does | Changes files |
| --- | --- | --- |
| `scan_media` | Find media in a file or folder and classify Profile 7 conversion candidates | No |
| `inspect_media` | Analyze one file; `deep` measures every base-layer frame | No |
| `plan_conversion` | Prepare exact Profile 8.1 or HDR10 conversion plans | No |
| `start_conversion` | Run approved conversion plans | Yes |
| `plan_backup` / `start_backup` | Prepare, then create, a `.dovi` enhancement archive | Yes, on start |
| `plan_restore` / `start_restore` | Prepare, then run, a Profile 7 restore | Yes, on start |
| `list_backups` | List `.dovi` archives and `.bak.dovi_convert` backups | No |
| `delete_backups` | Permanently delete the files a `list_backups` plan showed | Yes |
| `check_dependencies` | Check the native tools | No |
| `plan_dependency_install` / `start_dependency_install` | Prepare, then run, installation of missing tools | Yes, on start |
| `get_settings`, `set_tool_path`, `set_temporary_directory` | Read or change saved settings | Setters only |
| `list_presets` | List built-in and saved presets, their values and the one the settings match | No |
| `plan_preset` / `apply_preset` | List every setting a preset changes, marking risky ones, then apply it | Settings only, on apply |
| `check_for_update` | Compare with the latest GitHub release | No |
| `get_job`, `list_jobs`, `cancel_job` | Follow or cancel background jobs | No |

## How it keeps you in control

- **Plan, then approve.** Every operation that writes, replaces or deletes files is two tools. The `plan_*` tool returns exact plans with ids: inputs, outputs, verdict, decision and any warning. The `start_*` tool runs only plan ids the assistant got from that planning call, each plan once. The server tells the assistant to show you the plans and wait for your approval, and clients such as Claude Desktop also ask before running a tool. FEL files are planned only when you ask for `includeSimpleFel` or `forceComplexFel`, and those plans warn that enhancement-layer picture data will be lost.
- **Jobs.** Scans, inspections, plans, conversions, backups, restores and installations run as background jobs, one at a time, so a long conversion outlives the tool call that started it. A tool waits up to 20 seconds and returns the result if the job finished; otherwise it returns the job, which the assistant follows with `get_job`. `cancel_job` stops a job and keeps the original media. Closing the client cancels running jobs and removes their temporary files.
- **Logs.** The server writes `mcp-diagnostic-`, `mcp-history-` and `mcp-audit-YYYYMMDD.jsonl` to the usual `Logs` folder; audit records mark approvals as `ApprovedByAssistantUser`. Nothing is written to standard output except protocol messages.
