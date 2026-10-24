# Backup and restore

The enhancement layer is a small part of a Profile 7 movie. DoViFixer can keep it in a `.dovi` backup file next to the converted movie. With that backup, you can rebuild the original Profile 7 movie at any time and delete the large original now.

## Make a backup when you convert

Turn on **Settings › Conversion › Back up enhancement layer**. Every conversion then also saves:

```
Movie - DV P8.1.mkv   ← the converted movie
Movie.dovi            ← the enhancement layer backup
```

The `.dovi` file is tied to the converted movie's base layer by a SHA-256 hash, so it cannot be restored onto a different movie by mistake.

## Restore to Profile 7

When a converted movie has its `.dovi` backup in the same folder, its row shows **Restore to DV7**.

1. Click **Restore to DV7** on the row, or select several movies and use **File › Restore to DV7…**.
2. Review the files the app will create.
3. Click **Approve and start**.

The restored movie is saved as a new file; the converted movie and the backup stay.

## Clean up backups

**Tools › Clean up backups…** finds `.dovi` backups and `.bak.dovi_convert` originals in a folder. You review the list, and the app deletes exactly those files. By default they go to the Recycle Bin.

## Other cases

Backups in another folder, older backups without a manifest, and restoring to another folder work from the command line: see `backup` and `restore` in [Command line](../console.md).
