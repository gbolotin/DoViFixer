# Add and scan movies

## Add movies

Do any of these:

- Drag MKV files or folders from File Explorer into the window.
- Click **Add files** or **Add folder** on the Media page.
- Use **File › Add files…** or **File › Add folder…**.
- Right-click MKV files or a folder in File Explorer and select **Open in DoViFixer**. On Windows 11, select **Show more options** first.

A folder adds every MKV file in it, subfolders included. **Settings › Conversion › Subfolder search depth** sets how deep it looks (0 = only the folder itself, up to 100 levels).

## Convert from File Explorer

Right-click MKV files or a folder and select **Convert with DoViFixer**. DoViFixer adds the files to the list and converts them, as the **Convert** button does:

1. If DoViFixer is not open, it starts. Otherwise its window comes to the front.
2. The app scans the files, then converts only the files that are safe to convert with your settings. A file it skips shows why in its details.
3. If another operation is running, the files wait until it finishes.

When you select many files, DoViFixer gathers them into one batch.

The File Explorer commands point to the folder DoViFixer runs from. If you move the app, start it once from the new folder. To remove the commands, turn off **Settings › General › File Explorer**.

DoViFixer remembers the movies you add. They are back in the list the next time you start the app.

## Scan

When you add a movie, the app scans it. You can turn that off in **Settings › Performance › Automatically scan added files**. To scan again, click **Rescan** or use **File › Rescan all files**.

<img class="screenshot" src="../../assets/screenshots/media.png" alt="The Media page with two scanned movies and the details of the selected movie">

A scan reads short clips spread over the movie (10 clips of 1 second by default) and shows:

- The Dolby Vision profile, such as Profile 7 or Profile 8.1.
- The type of enhancement layer: MEL, Simple FEL or Complex FEL.
- What conversion would do with the file.

The colored dot and the **Risk** row in the details panel tell you how safe the file is to convert. If the scan is not sure, the **Next step** button suggests what to do, for example a full inspection.

## Inspect and Deep Inspect

| Action | What it reads | When to use it |
| --- | --- | --- |
| **Scan** | Short clips of the metadata | First look at many movies |
| **Inspect** (++f4++) | The metadata of every frame | Before you decide on a file |
| **Deep Inspect** | Every decoded frame of the picture | When a FEL result looks borderline |

Deep Inspect decodes the whole movie, so it takes much longer and needs temporary disk space. It measures real picture brightness and compares it with the Dolby Vision metadata, so it does not depend on the brightness values written in the file.

## Select movies

The list selects like File Explorer: click, ++ctrl++ + click, ++shift++ + click and ++ctrl+a++. Actions on the toolbar and in the **File** menu work on the selected movies. ++delete++ removes them from the list; the files on disk stay.

Prefer check boxes? Turn on **View › Show check boxes**. Then the checked movies are the ones that actions process.

## Export results

**File › Export results…** saves the results in the list as a CSV, JSON or text file.

Next: [Convert](convert.md).
