# Option 2: Studio workflow mockups

Generated using the built-in image generation tool with ../option-2-studio-console.png as the visual reference. Sample data and installation states are illustrative. No media operations or WPF implementation were performed.

[Open gallery](index.html)

- [Add files](01-add-files.png)
- [Dependency setup](02-dependency-setup.png)
- [Scanning](03-scanning.png)
- [Scan results](04-scan-results.png)
- [Inspection](05-inspection.png)
- [Review conversion plan](06-review-plan.png)
- [Conversion progress](07-conversion-progress.png)
- [Results](08-results.png)
- [Backup](09-backup.png)
- [Restore](10-restore.png)
- [Cleanup](11-cleanup.png)
- [Settings](12-settings.png)

## Design notes

Actual application data, approval tokens, available storage, tool capabilities, and actions must drive the implementation. Thumbnails are proposed presentation elements. Retained-content checkmarks are read-only indicators, not track-selection options. Changing an approved plan invalidates its approval. Active batch controls must prevent modifying the executing plan. Unknown/failed analysis remains distinct from complex FEL. Backup/restore progress and errors should reuse the operation-progress presentation; this set shows their review screens.

## Shared prompt

Use case: ui-mockup. Create ONE high fidelity landscape 1536x1024 desktop UI mockup for DoViFixer. Use the attached image strictly as STYLE REFERENCE: identical dark charcoal Windows WPF studio aesthetic, teal action buttons, thin slate borders, white Segoe UI-like type, compact horizontal top navigation 'Scan', 'Inspect', 'Convert', 'Backup & Restore', 'Settings', DoViFixer top left and window controls. Change screen content according to the stage below. Single full window straight-on, no outer presentation border or caption, no code. Crisp large readable text, concise accurate labels, ample spacing. Avoid inventing controls or metadata beyond the specification. No duplicate buttons. Sample media throughout: Mountain.mkv = Profile 7 MEL, Ocean.mkv = Profile 7 Simple FEL, City.mkv = Profile 7 Complex FEL. Target Profile 8.1 keeps base layer and RPU metadata; removes enhancement layer. Never label MEL/FEL as Profile 8. Original filenames stay unchanged when Keep original is selected; output is 'Mountain - DV P8.1.mkv' or 'Ocean - DV P8.1.mkv'. No claims of guaranteed playback or lossless FEL. 

## 01-add-files

Scan active. Title 'Add media'. Empty spacious file workspace with centered outlined folder icon, 'Drop MKV files or folders here', Add files and Add folder buttons. Small subheading 'Choose files to scan for Dolby Vision'. Right panel 'Scan options': Include subfolders checkbox checked; folder field D:\Media; sample analysis explanatory text 'Scan samples first; inspect full metadata before conversion.' Footer 0 files, disabled 'Start scan'. Top right Tools ready.

## 02-dependency-setup

Settings active. Title 'Required tools'. Inline banner 'Setup needed before scanning'. Table columns Tool, Version, Status: FFmpeg 8.0.1 Ready; MKVToolNix 95.0.0 Ready; MediaInfo CLI 26.05 Ready; dovi_tool 2.3.3 Missing. Selected missing row and installation review card 'Install dovi_tool 2.3.3', 'Source: github.com/quietvoid/dovi_tool', 'Scope: Current user', 'Destination: %LOCALAPPDATA%\DoViFixer\tools', 'Elevation: Not required', 'Integrity: Verified release checksum'. Footer secondary 'Choose executable', primary 'Install selected tool'. Explain 'After installation, validate the tool and resume scanning.' Tools need setup top right. This is before installation, show no progress.

## 03-scanning

Scan active. Title 'Scanning media'. File table Mountain.mkv Profile 7 MEL Complete, Ocean.mkv Profile 7 Analyzing current, City.mkv Pending with profile em dash. Right 'Selected file' Ocean.mkv, 'Reading sample 6 of 10', no verdict yet. Bottom current scan progress card: 1 of 3 files complete, sample progress 6 of 10, active bar, 'Cancel scan'. No made-up remaining-time estimates. Tools ready.

## 04-scan-results

Scan active. Title 'Scan results'. Three file rows all Profile 7: Mountain.mkv MEL, Ocean.mkv Simple FEL, City.mkv Complex FEL. Mountain and Ocean checked; City unchecked with amber indicator. Right selected Ocean.mkv metadata: 3840 x 2160, 01:36:12, 46.1 GB, 'Evidence: Sampled RPU metadata', 'Full inspection required before planning'. Toolbar Add files, Add folder, Rescan. Bottom '2 selected', primary 'Inspect selected'. Smaller secondary 'Deep inspect'. No completed conversion.

## 05-inspection

Inspect active. Title 'Inspection results'. Ocean.mkv selected from compact left list Mountain.mkv MEL, Ocean.mkv Simple FEL, City.mkv Complex FEL. Main evidence panel 'Full RPU analysis complete', 'Frame coverage: Complete', 'Classification: Simple FEL', 'Dolby Vision: Profile 7'. Separate 'Deep inspection' card with 'HDR10 signaling: Valid', 'Decoded frame coverage: Complete', 'Maximum measured expansion: 18 nits', 'Threshold: 50 nits'; restrained amber information note 'Brightness evidence does not guarantee conversion safety or playback compatibility.' No decorative chart. Buttons 'Run deep inspection again' and primary 'Prepare conversion plan'. Tools ready.

## 06-review-plan

Convert active. Title 'Review conversion plan'. Wide main table TWO selected files Mountain.mkv MEL and Ocean.mkv Simple FEL, each shows Profile 7 -> Profile 8.1, output Mountain - DV P8.1.mkv and Ocean - DV P8.1.mkv. Compact excluded line City.mkv Complex FEL, Excluded. Right 'Output & retention': Target Profile 8.1 dropdown, Output folder D:\Media, Temporary storage D:\Temp\DoViFixer, Keep original selected radio, Replace original after verification unselected radio, Create EL archive unchecked. Small clear retained list Base layer, RPU metadata, Audio, Subtitles, Chapters. Bottom approval area MEL checkbox checked 'Approve 1 MEL file', Simple FEL checkbox checked 'Approve 1 Simple FEL file', amber 'Enhancement-layer picture data will be lost for FEL files.' Primary 'Start 2 approved conversions', secondary Back. Entire plan visible before start.

## 07-conversion-progress

Convert active. Title 'Conversion queue'. Three rows Mountain.mkv Completed, Ocean.mkv Verifying, City.mkv Excluded. Selected Ocean right panel Profile 7 -> Profile 8.1, Original: Keep, Output: Ocean - DV P8.1.mkv. Bottom wide Current job card Ocean.mkv, 'Verifying output', stage percentage 72%, 'Verification checks completed: 18 of 25', stage tracker Prepare completed, Convert completed, Remux completed, Verify active, Publish pending. Elapsed 00:08:14. 'Cancel batch' button and small note 'Cancellation stops remaining files.' Footer 1 completed, 1 running, 1 excluded. No Start or approval button while active.

## 08-results

Convert active. Title 'Conversion complete'. Summary 2 succeeded / 0 failed / 1 excluded. Two completed output rows 'Mountain - DV P8.1.mkv', 'Ocean - DV P8.1.mkv', Profile 8.1, Verified, Original retained. Third City.mkv Excluded, Complex FEL not approved. Right 'Verification details' Ocean output: Base-layer payload Passed; RPU profile and count Passed; Audio and subtitles Passed; Chapters and attachments Passed; Timestamps Passed; Publication Complete. Bottom selected output exact path D:\Media\Ocean - DV P8.1.mkv, 'Open output folder', 'View operation log', primary 'New scan'. No claim of byte-identical container. Tools ready.

## 09-backup

Backup & Restore active. Secondary tabs Backup, Restore, Cleanup with Backup selected. Title 'Create enhancement-layer backup'. Source field D:\Media\Ocean.mkv, Browse button. Source metadata Profile 7 Simple FEL. Archive field D:\Media\Ocean.dovi. Review card 'Archive contents': el.hevc, manifest.json; 'Verification: Payload hashes and archive read-back'; 'Original file: Retained unchanged'. Small note 'An EL archive is not a complete movie backup.' Footer status 'Ready for approval' and primary 'Create verified archive'. No imaginary video thumbnail required.

## 10-restore

Backup & Restore active. Secondary tabs Backup, Restore, Cleanup with Restore selected. Title 'Review restoration'. Three stacked path fields: Converted media D:\Media\Ocean - DV P8.1.mkv; EL archive D:\Media\Ocean.dovi; Output D:\Restored\Ocean.mkv. Right evidence card 'Archive manifest: Valid', 'Payload hashes: Verified', 'Base-layer pairing: Matched'. Center result summary 'Restore Profile 7 enhancement layer', 'Converted file and archive will be retained'. Steps Review active / Reconstruct / Verify / Publish future. Primary 'Start restoration', secondary Back. Do not promise byte-identical MKV.

## 11-cleanup

Backup & Restore active. Secondary tabs Backup, Restore, Cleanup with Cleanup selected. Title 'Review backup cleanup'. Table with exact targets: D:\Media\OldMovie.dovi 4.2 GB checked, D:\Media\OldMovie.bak.dovi_convert 52.3 GB checked. Summary '2 selected backups · 56.5 GB'. Restrained amber panel 'Permanently delete these selected backups', 'Deleted backups cannot be used for restoration.' Below 'Original MKV files are not selected.' Confirmation textbox label 'Type APPROVE CLEANUP to continue', input empty, disabled red 'Delete selected backups', secondary Cancel. Preview state only, no completed deletion. No arbitrary media selection.

## 12-settings

Settings active. Title 'Settings'. Left internal section list Storage, Tools, Logging, About; Storage selected. Main settings cards Default temporary folder D:\Temp\DoViFixer Browse, Default output location 'Beside source file', 'Original retention: Keep original' with short help 'Conversion plans show the final retention choice.' Lower 'Tools' compact rows FFmpeg Ready, MKVToolNix Ready, MediaInfo CLI Ready, dovi_tool Ready, secondary 'Manage tools'. Right 'Logging' panel 'Open logs folder', 'Operation history', 'Diagnostics'. Small About area 'Check upstream releases' button, label 'dovi_convert release information'. Bottom 'Cancel' and 'Save settings'. No background update toggle, no self-update, no pause/resume queue feature.

## Review edits

- Scanning: remove premature Simple FEL classification and label progress as current-file samples.
- Results: remove FEL from the Profile 8.1 output label.
- Progress: uncheck the excluded City.mkv row.

