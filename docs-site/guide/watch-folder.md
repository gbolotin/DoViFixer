# Watch folder

DoViFixer can watch a folder, such as your download folder, and convert new movies by itself while the app runs.

## Set it up

1. Open **Settings › Watch folder**.
2. Under **Folder to watch**, click **Browse** and choose the folder.
3. Optional: turn on **Watch subfolders too**.
4. Optional: change **Wait before acting** (default 30 seconds).

## What happens

1. A new MKV file arrives in the folder.
2. The app waits until the file stops growing for the wait time and no other app is writing it. A file that is still copying is never read half-written.
3. The app scans the file.
4. If your settings would convert it, the movie joins the Media list and converts. Other files stay out of the list, and **Activity** says why.

Automatic conversion uses your Conversion and Safety settings, exactly like the **Convert** button. It never shows a dialog: if the output already exists, it skips the file, and it reports missing tools instead of installing them. Movies that were already in the folder when watching started are not converted.

## Status

The status bar shows **Watching:** and the folder name, the number of files waiting, and **Auto-converting from** and the folder name while a conversion runs. Click it to open the Watch folder settings. **Activity** marks these movies as "Auto-converted".
