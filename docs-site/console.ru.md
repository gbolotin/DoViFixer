# Командная строка

Проще всего пользоваться DoViFixer через настольное приложение. Тот же движок работает из терминала и через [ИИ-ассистента](ai-assistants.md). У всех трёх общие настройки и пути к инструментам.

Программа установки ставит версию для командной строки, если оставить отмеченным **Command-line version**, и добавляет её в `PATH`, если отметить эту задачу. Без программы установки скачайте `DoViFixer.Console-X.Y.Z.zip` из [последнего выпуска](https://github.com/gbolotin/DoViFixer/releases/latest) и распакуйте его. Чтобы запускать его из любой папки, один раз выполните в папке приложения:

```powershell
.\DoViFixer.Console.exe settings add-to-path
```

Частые команды:

```powershell
DoViFixer.Console scan 'E:\Movies' -r 3                    # просканировать папку на 3 уровня вглубь
DoViFixer.Console inspect 'E:\Movies\Movie.mkv' --deep     # глубокая проверка одного фильма
DoViFixer.Console convert 'E:\Movies' -r --plan            # показать, что произойдёт, ничего не меняя
DoViFixer.Console convert 'E:\Movies\Movie.mkv' --backup   # конвертировать и сохранить резервную копию .dovi
DoViFixer.Console restore 'E:\Movies\Movie - DV P8.1.mkv'  # восстановить Profile 7 из резервной копии
DoViFixer.Console dependencies check                       # проверить нативные инструменты
DoViFixer.Console --help
```

Как и приложение, консоль показывает, что собирается сделать, и спрашивает, прежде чем менять файлы. Для Simple FEL нужен ключ `--include-simple`, для complex FEL — `--force`.

[Полный справочник](https://github.com/gbolotin/DoViFixer#media-commands) в README (на английском) перечисляет все команды и параметры.
