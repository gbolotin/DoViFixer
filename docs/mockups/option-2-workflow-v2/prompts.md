# Studio mockups v2

Generated with the built-in image generation tool. Supersedes the earlier workflow concepts for the decisions listed here. Each source image is in ../option-2-workflow/ with the matching filename. Images contain illustrative data; no software behavior was implemented.

## Accepted design changes

- Navigation: Media, Backup & Restore, Settings.
- Media has a shared file list and independent Scan, Inspect (standard/deep), and Convert commands. No required manual scan or inspect before Convert; conversion planning performs necessary full-RPU analysis automatically.
- File selection occurs only in the table; remove duplicate category approval checkboxes.
- A single Approve and start action approves the displayed conversion plan and its warnings.
- Remove the routine retained-content section entirely; keep meaningful warnings and actual verification results.
- Temporary storage is configured in Settings only.
- Output destination: Same folder as source (default), or Other folder with path picker.
- Conversion target choices: Profile 8.1 and HDR10.

## Implementation notes

These raster concepts are not pixel-perfect interaction specifications. Keep toolbar position and iconography consistent in implementation; disable commands without valid inputs and during conflicting operations. Selection must not mutate an executing plan. Any plan edits require revalidation before approval. Generated example paths may differ between independent screen states; actual paths must always come from the prepared plan. Settings defaults do not constitute execution approval.

## Common edit prompt

Edit the attached existing DoViFixer screenshot mockup. Preserve dark charcoal studio style, teal accent, typography, window dimensions, sharp readability, and Windows title bar. GLOBAL CHANGE: top navigation has EXACTLY THREE items 'Media', 'Backup & Restore', 'Settings'. Remove old top-level Scan, Inspect, Convert tabs completely. Scan, Inspect, Convert are now toolbar COMMAND BUTTONS inside the Media screen only. All media operations independent; no mandatory Scan -> Inspect -> Convert screen sequence. No implementation jargon. Keep single full-window image 1536x1024. 

## 01-add-files

Media idle state. Main title 'Media'. Empty shared file list with Add files and Add folder. Toolbar commands Scan, Inspect with dropdown arrow, Convert (disabled without selection). Right panel only 'Select files to view details'. Remove sequential instructions about scanning before conversion. No stepper. All commands are independent.

## 02-dependency-setup

Keep dependency setup content and installation review. Only change the top navigation as specified.

## 03-scanning

Media scanning state. Shared file table. Toolbar Scan, Inspect dropdown, Convert disabled while busy. Ocean Profile 7, classification Pending, Mountain Profile 7 MEL complete, City Pending. Right panel scan progress 6 of 10 samples; remove duplicated conflicting profile-detected field. Bottom current-file sample progress and Cancel scan. No sequential workflow stepper.

## 04-scan-results

Media workspace with completed scan results. Shared table Mountain Profile 7 MEL checked, Ocean Profile 7 Simple FEL checked, City Profile 7 Complex FEL unchecked. Toolbar Add files, Add folder, then Scan, Inspect dropdown, Convert prominent. No 'Inspect selected' mandatory next-step footer. Footer '2 selected'. Right evidence panel sampled metadata. Remove 'Full inspection required before planning' warning and 'Deep inspection recommended' message. Users may invoke Convert directly; internal analysis is automatic. No sequential instructions.

## 05-inspection

Media workspace with inspection results. Maintain shared file table left with checkboxes only there: Mountain and Ocean selected, City unselected; Ocean focused. Toolbar Scan, Inspect dropdown, Convert. Show Inspect dropdown open with 'Standard inspection' and 'Deep inspection'. Right shows Ocean full RPU analysis and deep inspection results. Keep accurate brightness limitation note. Remove 'Prepare conversion plan' and 'Run deep inspection again' footer buttons; independent toolbar commands replace them. This is a details state of Media, not a separate Inspect page.

## 06-review-plan

Redesign conversion REVIEW state of Media workspace. Top Media active. Title 'Review conversion plan'. File-selection checkboxes ONLY in table: Mountain and Ocean checked, City unchecked excluded. Table shows input filename, source type, target, FULL resolved output paths D:\Media\Mountain - DV P8.1.mkv and D:\Media\Ocean - DV P8.1.mkv (wrap on two lines if needed). Right panel 'Conversion options': Target choice as two radio buttons 'Profile 8.1' selected, 'HDR10' unselected. Destination two radio buttons 'Same folder as source' selected default, 'Other folder' unselected; disabled empty path field and Browse button underneath Other folder. File handling 'Keep original' selected, 'Replace original after verification' unselected. Optional Create EL archive checkbox is a command option, not file-selection. REMOVE temporary storage field completely. REMOVE entire 'What will be retained' section and ALL routine explanatory track/content info, including static checkmarks. REMOVE bottom category approval checkboxes and MEL/Simple FEL approval cards completely. Instead one compact amber warning 'Ocean.mkv: Enhancement-layer picture data will be lost.' Footer '2 files selected', Back, ONE primary 'Approve and start'. Do not introduce other consent checkboxes. No tracks-retained list anywhere.

## 07-conversion-progress

Media operation running state. Shared file table and toolbar Scan, Inspect dropdown, Convert visibly disabled; remove Add/Remove/Clear commands during operation. Mountain Completed, Ocean Verifying, City Excluded unchecked. Right selected-file panel only source, target, output path, Original: Keep; REMOVE routine retained tracks/content text. Preserve bottom progress Verify 72%, 18 of 25 checks, Publish pending, Cancel batch. No approval button.

## 08-results

Media operation results state. Top shared toolbar Scan, Inspect dropdown, Convert. Preserve completed outputs, summary 2 succeeded 0 failed 1 excluded, detailed actual verification results (these are results, not routine retained-content explanation). Remove routine Enhancement layer explanation. Output profile only 'Profile 8.1', never append FEL. Footer Open output folder, View operation log; remove New scan big button since toolbar commands are independent.

## 09-backup

Keep backup review content. Only change top navigation as specified. No extra global Scan/Inspect/Convert toolbar here.

## 10-restore

Keep restoration review content. Only change top navigation as specified. No extra global Scan/Inspect/Convert toolbar here.

## 11-cleanup

Keep cleanup review content and exact typed confirmation. Only change top navigation as specified. No extra global Scan/Inspect/Convert toolbar here.

## 12-settings

Settings page. Keep temporary storage folder configuration ONLY here, with Browse. Default output destination two radio buttons 'Same folder as source' selected and 'Other folder' unselected, disabled path field with Browse for other. Keep tool management, logging, original retention defaults and About. Top navigation changes as specified. No routine retained-media-content list.
