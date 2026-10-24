# Command line

The desktop app is the easiest way to use DoViFixer. The same engine also runs from a terminal and from an [AI assistant](ai-assistants.md). All three share your settings and tool paths.

Setup installs the command-line version when you keep **Command-line version** ticked, and adds it to `PATH` if you tick that task. Without setup, download `DoViFixer.Console-X.Y.Z.zip` from the [latest release](https://github.com/gbolotin/DoViFixer/releases/latest) and unzip it. To run it from any folder, run this once in the app folder:

```powershell
.\DoViFixer.Console.exe settings add-to-path
```

Common commands:

```powershell
DoViFixer.Console scan 'E:\Movies' -r 3                    # scan a folder, 3 levels deep
DoViFixer.Console inspect 'E:\Movies\Movie.mkv' --deep     # deep inspection of one movie
DoViFixer.Console convert 'E:\Movies' -r --plan            # show what would happen, change nothing
DoViFixer.Console convert 'E:\Movies\Movie.mkv' --backup   # convert and keep a .dovi backup
DoViFixer.Console restore 'E:\Movies\Movie - DV P8.1.mkv'  # rebuild Profile 7 from the backup
DoViFixer.Console dependencies check                       # check the native tools
DoViFixer.Console --help
```

Like the app, the console shows what it will do and asks before it changes files. Simple FEL needs `--include-simple`, and complex FEL needs `--force`.

The [full reference](https://github.com/gbolotin/DoViFixer#media-commands) in the README lists every command and option.
