# Add and scan movies

## Add movies

Do any of these:

- Drag MKV files or folders from File Explorer into the window.
- Click **Add files** or **Add folder** on the Media page.
- Use **File › Add files…** or **File › Add folder…**.

A folder adds every MKV file in it, subfolders included. **Settings › Conversion › Subfolder search depth** sets how deep it looks (0 = only the folder itself, up to 100 levels).

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
