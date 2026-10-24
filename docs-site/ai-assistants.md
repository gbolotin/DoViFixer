# AI assistants

DoViFixer's MCP server lets an AI assistant scan, inspect, convert, back up and restore your movies when you ask it to. MCP (Model Context Protocol) is the standard way for assistants to use tools on your PC.

The assistant uses the same engine, settings and tool paths as the desktop app. Anything that changes files is planned first. The assistant must show you the plan and wait for your approval before it runs it.

| Assistant | Works with DoViFixer | Setup adds it for you |
| --- | --- | --- |
| [Claude Desktop](#claude-desktop) | Yes | Yes |
| [Claude Code](#claude-code) | Yes | Yes |
| [Codex](#codex) (app, CLI and IDE extension) | Yes | Yes |
| [Hermes Agent](#hermes-agent) | Yes | On Windows yes, in WSL no |
| [ChatGPT](#chatgpt) | No, use Codex instead | — |

## Install the MCP server

1. Run `DoViFixer-Setup-X.Y.Z.exe` from the [latest release](https://github.com/gbolotin/DoViFixer/releases/latest).
2. On **Select Components**, keep **MCP server for AI assistants** ticked.
3. On **Select Additional Tasks**, tick the assistants to add DoViFixer to. Setup already ticks the ones it finds on your PC.
4. Finish setup, then restart each assistant you ticked.

Setup installs the server to `C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe`. It changes only the `dovifixer` entry in each assistant's configuration and keeps everything else. When you uninstall DoViFixer, setup removes these entries again.

!!! tip "Without setup"
    Download `DoViFixer.Mcp-X.Y.Z.zip` from the same release and unzip it, for example to `C:\Tools\DoViFixer.Mcp`. Then follow the manual steps for your assistant below, with that path instead of `C:\Program Files\DoViFixer\Mcp`.

You can also add or remove the server later from a terminal:

```powershell
& 'C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe' --register claude-desktop claude-code codex hermes
& 'C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe' --unregister
```

`--register` takes one or more of `claude-desktop`, `claude-code`, `codex` and `hermes`. `--unregister` without names removes the server from all of them.

## Claude Desktop

**Manual setup:**

1. In Claude Desktop, open **Settings › Developer › Edit Config**. This opens `claude_desktop_config.json`.
2. Add `dovifixer` under `mcpServers`:

    ```json
    {
      "mcpServers": {
        "dovifixer": {
          "command": "C:\\Program Files\\DoViFixer\\Mcp\\DoViFixer.Mcp.exe"
        }
      }
    }
    ```

3. Quit Claude Desktop from the system tray, then start it again.

**Check:** in a new chat, open the tools menu below the message box. **dovifixer** is listed with its tools.

**Remove:** delete the `dovifixer` entry and restart Claude Desktop.

!!! note "Where the file is"
    Claude Desktop normally keeps the file in `%APPDATA%\Claude`. The Microsoft Store version can keep it in `%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Roaming\Claude`. **Edit Config** always opens the right one.

## Claude Code

**Manual setup:** run this in PowerShell or a terminal:

```powershell
claude mcp add --scope user dovifixer -- "C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe"
```

`--scope user` makes DoViFixer available in every folder, not only the current project.

**Check:** run `claude mcp list`, or type `/mcp` in a Claude Code session. **dovifixer** shows as connected.

**Remove:** `claude mcp remove --scope user dovifixer`

## Codex

The Codex app, the Codex CLI and the Codex IDE extension share one configuration, so one setup covers all of them.

**Manual setup:** run this in PowerShell or a terminal:

```powershell
codex mcp add dovifixer -- "C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe"
```

Or add this to `%USERPROFILE%\.codex\config.toml` (or `config.toml` in the folder that `CODEX_HOME` names):

```toml
[mcp_servers.dovifixer]
command = "C:\\Program Files\\DoViFixer\\Mcp\\DoViFixer.Mcp.exe"
```

Then start a new Codex session.

**Check:** run `codex mcp list`, or type `/mcp` in a Codex CLI session. **dovifixer** is listed.

**Remove:** `codex mcp remove dovifixer`

## Hermes Agent

Hermes Agent keeps its MCP servers in `config.yaml`, under `mcp_servers`. Where that file is depends on how you run Hermes.

### Hermes on Windows

Setup adds DoViFixer for you. To do it by hand, add this to `%USERPROFILE%\.hermes\config.yaml` (or `config.yaml` in the folder that `HERMES_HOME` names):

```yaml
mcp_servers:
  dovifixer:
    command: "C:\\Program Files\\DoViFixer\\Mcp\\DoViFixer.Mcp.exe"
    args: []
```

If the file already has `mcp_servers:`, add only the `dovifixer:` part under it.

### Hermes in WSL

Hermes usually runs in WSL (Windows Subsystem for Linux). Setup does not change files inside WSL, so add DoViFixer by hand:

1. In your WSL terminal, open `~/.hermes/config.yaml`.
2. Add the server with its Linux path. WSL starts Windows programs from `/mnt/c/...`:

    ```yaml
    mcp_servers:
      dovifixer:
        command: "/mnt/c/Program Files/DoViFixer/Mcp/DoViFixer.Mcp.exe"
        args: []
    ```

3. Start a new Hermes session.

DoViFixer runs as a Windows program, so it works with Windows paths. Ask about `E:\Movies`, not `/mnt/e/Movies`.

!!! note
    Starting Windows programs from WSL needs WSL interop, which is on by default. If `"/mnt/c/Program Files/DoViFixer/Mcp/DoViFixer.Mcp.exe" --help` gives "command not found" or "cannot execute binary file", interop is off in `/etc/wsl.conf`.

**Check:** ask Hermes "Which DoViFixer tools can you use?". It lists tools such as `scan_media` and `plan_conversion`.

**Remove:** delete the `dovifixer` entry from `config.yaml`.

## ChatGPT

ChatGPT cannot use DoViFixer. ChatGPT runs in OpenAI's cloud. It connects only to MCP servers that it can reach over the internet through HTTPS. DoViFixer's server runs on your PC and talks to the assistant directly, so ChatGPT cannot reach it.

Use **[Codex](#codex)** instead. Codex is OpenAI's assistant that runs on your PC, and you can sign in to it with your ChatGPT account.

!!! warning
    Do not expose DoViFixer's server to the internet with a tunnel to make it reach ChatGPT. Its tools can convert, replace and delete files on your PC.

## Use it

Ask in your own words. For example:

- "Scan E:\Movies two folders deep and tell me which films can be converted."
- "Inspect E:\Movies\Movie.mkv in depth."
- "Plan converting the MEL films in E:\Movies to Profile 8.1 with a backup, then convert them once I agree."
- "Restore Profile 7 for E:\Movies\Movie - DV P8.1.mkv."
- "Are all the tools DoViFixer needs installed?"

How the assistant works with DoViFixer:

1. **It plans first.** For a conversion, backup, restore, deletion or tool installation, it gets an exact plan: inputs, outputs, verdict and warnings.
2. **You approve.** It shows you the plan and runs it only after you agree. Most assistants also ask before each tool call.
3. **Long work runs in the background.** Scans and conversions continue as jobs, and the assistant checks on them. Ask it to cancel a job at any time; the original movie stays as it was.

FEL movies are planned only when you ask for them, because converting them loses enhancement-layer picture data. See [Before you convert](before-you-convert.md).

The server writes its logs to `%LOCALAPPDATA%\DoViFixer\Logs` (`mcp-*.jsonl`).

## Troubleshooting

| Problem | What to do |
| --- | --- |
| The assistant does not list DoViFixer | Restart the assistant completely. Check that the `command` path exists. |
| The server starts and stops at once | Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64). |
| Tools report missing FFmpeg, MKVToolNix, MediaInfo or dovi_tool | Ask the assistant to install them, or start the desktop app once. See [Install](install.md#3-let-the-app-set-up-its-tools). |
| Hermes in WSL cannot find a file | Use the Windows path, such as `E:\Movies\Movie.mkv`. |

For technical details, such as the full tool list, see [MCP server](https://github.com/gbolotin/DoViFixer/blob/main/docs/mcp.md) on GitHub.
