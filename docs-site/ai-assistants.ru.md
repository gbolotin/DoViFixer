# ИИ-ассистенты

MCP-сервер DoViFixer позволяет ИИ-ассистенту сканировать, проверять, конвертировать, сохранять в резервную копию и восстанавливать ваши фильмы по вашей просьбе. MCP (Model Context Protocol) — стандартный способ, которым ассистенты пользуются инструментами на вашем компьютере.

Ассистент работает с тем же движком, настройками и путями к инструментам, что и приложение. Всё, что меняет файлы, сначала оформляется планом. Ассистент обязан показать вам план и дождаться вашего согласия, прежде чем выполнять его.

| Ассистент | Работает с DoViFixer | Программа установки подключает сама |
| --- | --- | --- |
| [Claude Desktop](#claude-desktop) | Да | Да |
| [Claude Code](#claude-code) | Да | Да |
| [Codex](#codex) (приложение, CLI и расширение для IDE) | Да | Да |
| [Hermes Agent](#hermes-agent) | Да | В Windows да, в WSL нет |
| [ChatGPT](#chatgpt) | Нет, используйте Codex | — |

## Установка MCP-сервера

1. Запустите `DoViFixer-Setup-X.Y.Z.exe` из [последнего выпуска](https://github.com/gbolotin/DoViFixer/releases/latest).
2. На странице **Select Components** оставьте отмеченным **MCP server for AI assistants**.
3. На странице **Select Additional Tasks** отметьте ассистентов, к которым нужно подключить DoViFixer. Найденные на компьютере уже отмечены.
4. Завершите установку и перезапустите отмеченных ассистентов.

Программа установки ставит сервер в `C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe`. В настройках каждого ассистента она меняет только запись `dovifixer` и сохраняет всё остальное. При удалении DoViFixer эти записи тоже удаляются.

!!! tip "Без программы установки"
    Скачайте `DoViFixer.Mcp-X.Y.Z.zip` из того же выпуска и распакуйте его, например в `C:\Tools\DoViFixer.Mcp`. Затем выполните ручную настройку для своего ассистента ниже, указав этот путь вместо `C:\Program Files\DoViFixer\Mcp`.

Подключить или отключить сервер можно и позже, из терминала:

```powershell
& 'C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe' --register claude-desktop claude-code codex hermes
& 'C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe' --unregister
```

`--register` принимает одно или несколько имён: `claude-desktop`, `claude-code`, `codex`, `hermes`. `--unregister` без имён отключает сервер у всех.

## Claude Desktop

**Ручная настройка:**

1. В Claude Desktop откройте **Settings › Developer › Edit Config**. Откроется `claude_desktop_config.json`.
2. Добавьте `dovifixer` в `mcpServers`:

    ```json
    {
      "mcpServers": {
        "dovifixer": {
          "command": "C:\\Program Files\\DoViFixer\\Mcp\\DoViFixer.Mcp.exe"
        }
      }
    }
    ```

3. Закройте Claude Desktop через значок в области уведомлений и запустите снова.

**Проверка:** в новом чате откройте меню инструментов под полем ввода. В нём есть **dovifixer** с его инструментами.

**Отключение:** удалите запись `dovifixer` и перезапустите Claude Desktop.

!!! note "Где лежит файл"
    Обычно Claude Desktop хранит файл в `%APPDATA%\Claude`. Версия из Microsoft Store может хранить его в `%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Roaming\Claude`. **Edit Config** всегда открывает нужный файл.

## Claude Code

**Ручная настройка:** выполните в PowerShell или терминале:

```powershell
claude mcp add --scope user dovifixer -- "C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe"
```

`--scope user` делает DoViFixer доступным в любой папке, а не только в текущем проекте.

**Проверка:** выполните `claude mcp list` или введите `/mcp` в сеансе Claude Code. **dovifixer** показан как подключённый.

**Отключение:** `claude mcp remove --scope user dovifixer`

## Codex

Приложение Codex, Codex CLI и расширение Codex для IDE используют общие настройки, поэтому одной настройки хватает для всех.

**Ручная настройка:** выполните в PowerShell или терминале:

```powershell
codex mcp add dovifixer -- "C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe"
```

Или добавьте в `%USERPROFILE%\.codex\config.toml` (либо в `config.toml` в папке из переменной `CODEX_HOME`):

```toml
[mcp_servers.dovifixer]
command = "C:\\Program Files\\DoViFixer\\Mcp\\DoViFixer.Mcp.exe"
```

Затем начните новый сеанс Codex.

**Проверка:** выполните `codex mcp list` или введите `/mcp` в сеансе Codex CLI. В списке есть **dovifixer**.

**Отключение:** `codex mcp remove dovifixer`

## Hermes Agent

Hermes Agent хранит MCP-серверы в `config.yaml`, в разделе `mcp_servers`. Где лежит этот файл, зависит от того, как вы запускаете Hermes.

### Hermes в Windows

Программа установки подключает DoViFixer сама. Чтобы сделать это вручную, добавьте в `%USERPROFILE%\.hermes\config.yaml` (либо в `config.yaml` в папке из переменной `HERMES_HOME`):

```yaml
mcp_servers:
  dovifixer:
    command: "C:\\Program Files\\DoViFixer\\Mcp\\DoViFixer.Mcp.exe"
    args: []
```

Если в файле уже есть `mcp_servers:`, добавьте под него только часть `dovifixer:`.

### Hermes в WSL

Обычно Hermes работает в WSL (подсистеме Windows для Linux). Программа установки не меняет файлы внутри WSL, поэтому подключите DoViFixer вручную:

1. В терминале WSL откройте `~/.hermes/config.yaml`.
2. Добавьте сервер с путём в формате Linux. WSL запускает программы Windows из `/mnt/c/...`:

    ```yaml
    mcp_servers:
      dovifixer:
        command: "/mnt/c/Program Files/DoViFixer/Mcp/DoViFixer.Mcp.exe"
        args: []
    ```

3. Начните новый сеанс Hermes.

DoViFixer работает как программа Windows и понимает пути Windows. Спрашивайте про `E:\Movies`, а не про `/mnt/e/Movies`.

!!! note
    Запуск программ Windows из WSL требует взаимодействия WSL с Windows (interop); по умолчанию оно включено. Если `"/mnt/c/Program Files/DoViFixer/Mcp/DoViFixer.Mcp.exe" --help` выдаёт «command not found» или «cannot execute binary file», interop выключен в `/etc/wsl.conf`.

**Проверка:** спросите Hermes «Какими инструментами DoViFixer ты можешь пользоваться?». Он перечислит инструменты, например `scan_media` и `plan_conversion`.

**Отключение:** удалите запись `dovifixer` из `config.yaml`.

## ChatGPT

ChatGPT не может работать с DoViFixer. ChatGPT работает в облаке OpenAI. Он подключается только к MCP-серверам, доступным через интернет по HTTPS. Сервер DoViFixer работает на вашем компьютере и общается с ассистентом напрямую, поэтому ChatGPT до него не дотянется.

Используйте **[Codex](#codex)**. Это ассистент OpenAI, который работает на вашем компьютере; войти в него можно с учётной записью ChatGPT.

!!! warning
    Не открывайте сервер DoViFixer в интернет через туннель, чтобы подключить ChatGPT. Его инструменты могут конвертировать, заменять и удалять файлы на вашем компьютере.

## Как пользоваться

Просите своими словами. Например:

- «Просканируй E:\Movies на две папки вглубь и скажи, какие фильмы можно конвертировать».
- «Подробно проверь E:\Movies\Movie.mkv».
- «Составь план конвертации MEL-фильмов из E:\Movies в Profile 8.1 с резервной копией, а когда я соглашусь — конвертируй».
- «Восстанови Profile 7 для E:\Movies\Movie - DV P8.1.mkv».
- «Все ли инструменты, нужные DoViFixer, установлены?»

Как ассистент работает с DoViFixer:

1. **Сначала план.** Для конвертации, резервной копии, восстановления, удаления или установки инструментов он получает точный план: входные и выходные файлы, вердикт и предупреждения.
2. **Вы соглашаетесь.** Ассистент показывает план и выполняет его только после вашего согласия. Большинство ассистентов к тому же спрашивают перед каждым вызовом инструмента.
3. **Долгая работа идёт в фоне.** Сканирование и конвертация продолжаются как задания, и ассистент следит за ними. Задание можно отменить в любой момент; исходный фильм останется как был.

FEL-фильмы попадают в план, только если вы попросите, потому что при конвертации теряются данные изображения слоя улучшения. См. [Перед конвертацией](before-you-convert.md).

Сервер пишет журналы в `%LOCALAPPDATA%\DoViFixer\Logs` (`mcp-*.jsonl`).

## Решение проблем

| Проблема | Что сделать |
| --- | --- |
| Ассистент не видит DoViFixer | Полностью перезапустите ассистента. Проверьте, что путь в `command` существует. |
| Сервер запускается и сразу закрывается | Установите [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64). |
| Инструменты сообщают, что нет FFmpeg, MKVToolNix, MediaInfo или dovi_tool | Попросите ассистента установить их или один раз запустите приложение. См. [Установка](install.md). |
| Hermes в WSL не находит файл | Укажите путь Windows, например `E:\Movies\Movie.mkv`. |

Технические подробности, например полный список инструментов, — на странице [MCP server](https://github.com/gbolotin/DoViFixer/blob/main/docs/mcp.md) на GitHub (на английском).
