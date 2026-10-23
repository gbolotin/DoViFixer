# Troubleshooting

## Keyboard shortcuts

| Keys | Action |
| --- | --- |
| ++f1++ | Open this documentation |
| ++f4++ | Inspect the selected movies |
| ++f5++ | Convert the selected movies |
| ++ctrl+comma++ | Open Settings |
| ++ctrl+a++ | Select all movies |
| ++delete++ | Remove the selected movies from the list (the files stay on disk) |

## Common questions

??? question "Convert is grayed out"
    Every selected movie would only be skipped. Typical reasons: the movie is not Profile 7, or it is FEL and the FEL options in **Settings › Conversion** are off. The row of each movie shows why.

??? question "A tool is missing or not valid"
    Use **Tools › Check dependencies**. The app offers to install missing tools. To use your own copy, set its path in **Settings › Native tools**. The MediaInfo GUI app does not work; DoViFixer needs MediaInfo **CLI**.

??? question "Not enough disk space"
    Conversion reserves temporary space first. Choose a bigger drive as **Settings › Conversion › Temporary storage folder**, or convert fewer movies at once in **Settings › Performance**.

??? question "The output file is in use"
    Another app, often a media server, is reading the folder. DoViFixer retries, and if it still fails it names the apps that hold the file when Windows can tell. Close them and convert again.

??? question "Where is my converted movie?"
    Next to the original, as `<name> - DV P8.1.mkv`, unless you set another **Output folder**. Click the file location in the details panel to open the folder.

## Activity

The **Activity** button at the left of the status bar lists what happened in this session: each scanned, converted or restored movie, in plain words. A red count shows new problems. Each entry can open its file in File Explorer or copy its text.

## Logs and diagnostics

- **Help › Open logs** opens the log folder (`%LOCALAPPDATA%\DoViFixer\Logs`).
- **Help › Export diagnostics…** saves a ZIP with recent logs and your settings, for a bug report. Nothing is uploaded. Review the ZIP before you share it: it can contain file paths and your user name.

## Report a problem

Open an [issue on GitHub](https://github.com/gbolotin/DoViFixer/issues). Describe what you did, what you expected and what happened, and attach the diagnostics ZIP if you can.
