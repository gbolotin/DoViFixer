# Media workspace revision 3

Generated with the built-in image generation tool. Five revised raster mockups; no application implementation.

## Agreed interactions

- Cancel on an active row cancels only that file, waits for its cleanup/recovery to finish, reports Cancelled, and continues with the next eligible file. Cancel batch remains separate. This requires application workflow changes; current batch cancellation stops subsequent work.
- Skip on a pending row unchecks it and changes status to Skipped, equivalent to deselection for the pending operation. Running and completed items cannot be skipped. Pending-row changes are synchronized with the worker to prevent starting a skipped file.
- Focus and checkbox selection are separate: focusing any row updates the Selected file panel, even while a different file runs. Include conversion outcome, output path, verification and retention results there. Previous operation results should be labeled as such.
- Classification colors match Console ScanRenderer: MEL green, Simple FEL blue, Complex FEL and analysis failure red, Unknown yellow. Always include text labels; pending evidence is neutral.
- Inspect keeps the shared table and updates the right details panel; there is no dedicated inspection screen.
- Clicking Convert opens a bottom options/review panel. Keep right panel for file details. Closing that panel does not cancel a running file. Approve and start is the only execution approval action.
- No routine retained-content section or temporary-storage control in conversion options. Destination defaults to Same folder as source; Other folder enables browsing.

## Source and prompts

Source images are in ../option-2-workflow-v2/ with matching filenames.

Edit this DoViFixer dark charcoal WPF studio mockup. Preserve existing style, teal accent, crisp readable Segoe UI-like typography, 1536x1024 landscape. Top navigation Media / Backup & Restore / Settings. ALL Media states use IDENTICAL layout: compact toolbar Add files, Add folder, Scan, Inspect dropdown, Convert; wide shared FILE TABLE occupying left two-thirds, Selected file details panel on RIGHT one-third; contextual operation or conversion-options panel at BOTTOM. Never use a separate inspection page. Table columns checkbox, File, Profile / Type, Status, Action. Color classification markers exactly: MEL green, Simple FEL BLUE (not teal), Complex FEL RED, Unknown YELLOW, failed analysis red. Always include text labels. File examples Mountain.mkv Profile 7 MEL, Ocean.mkv Profile 7 Simple FEL, City.mkv Profile 7 Complex FEL. No arbitrary new features. 

## 03-scanning

Media workspace during mixed batch activity, incorporating per-file controls. Mountain.mkv status Completed, Ocean.mkv status Analyzing and compact row action 'Cancel', City.mkv status Pending with compact row action 'Skip'. Mountain is highlighted/focused (Ocean remains active independent of focus). All initially checked. Right 'Selected file' Mountain.mkv contains file metadata and separate 'Last conversion result' section: 'Completed', 'Output: D:\Media\Mountain - DV P8.1.mkv', 'Verification: Passed', 'Original: Retained'. This result is from a prior conversion, separate from current scanning. Bottom panel current scan Ocean.mkv 6 of 10 samples, 60%, 'Cancel current file' button and separate muted 'Cancel batch' button. Small label 'Cancel current file continues with the next file'. Clicking Skip unchecks that row and changes status to Skipped. No completed verdict for Ocean while scanning: show Profile 7, Analysis pending with neutral marker rather than Simple FEL. No explanatory paragraph about button behavior except short cancellation label.

## 04-scan-results

Media table scan results with GREEN dot MEL for Mountain, BLUE dot Simple FEL for Ocean, RED dot Complex FEL for City; labels clearly readable. Mountain and Ocean checked, City unchecked status Skipped (consistent with checkbox). No Skip action on already skipped row. Right Selected file Ocean.mkv has Profile 7, Simple FEL BLUE marker, sampled evidence, resolution and duration. Footer 2 selected / 1 skipped. Toolbar enables independent Scan, Inspect dropdown, Convert. Maintain exact common table-and-right-panel layout.

## 05-inspection

Replace special inspection layout completely with same wide shared table as Scan and Convert. Table Mountain checked green MEL; Ocean checked blue Simple FEL, highlighted/focused; City unchecked red Complex FEL status Skipped. Right narrow 'Selected file' panel for Ocean.mkv shows full inspection results in compact text rows: 'Full RPU: Complete', 'Frame coverage: Complete', 'Classification: Simple FEL' blue marker, 'Deep inspection: Complete', 'HDR10 signaling: Valid', 'Expansion: 18 nits', 'Threshold: 50 nits'. Amber short note 'Brightness evidence is not a playback guarantee'. No giant central inspection cards, no separate inspection page, no stepper. Toolbar includes Add files, Add folder, Scan, Inspect dropdown, Convert. Bottom thin status strip 'Inspection complete'. Only the right details panel changes for the selected file.

## 06-review-plan

Conversion review opened by clicking Convert, within SAME Media workspace. UPPER area common file TABLE LEFT and Selected file metadata panel RIGHT. Table checkboxes only for file selection: Mountain green MEL checked, Ocean blue Simple FEL checked, City red Complex FEL unchecked Skipped. Table includes planned output paths under filenames D:\Media\Mountain - DV P8.1.mkv and D:\Media\Ocean - DV P8.1.mkv. RIGHT panel ONLY selected Ocean file metadata; NO conversion options in right panel. All conversion controls go in FULL-WIDTH BOTTOM PANEL titled 'Conversion options', occupying lower 40% of window. Organize bottom panel in 3 horizontal columns: Target radio Profile 8.1 selected / HDR10; Destination radio Same folder as source selected / Other folder unselected with disabled path and Browse; File handling radio Keep original selected / Replace original after verification unselected, checkbox Create EL archive. Under those controls compact amber warning 'Ocean.mkv: Enhancement-layer picture data will be lost.' At very bottom 2 files selected, Cancel button (closes options only), ONE primary 'Approve and start'. No temporary folder field, no retained-content list, no approval checkboxes, no separate conversion-review page. Keep table, toolbar and right panel visible above bottom panel.

## 07-conversion-progress

Active conversion using common table/selected-file/bottom-progress layout. Mountain green MEL status Completed; Ocean blue Simple FEL status Verifying with inline 'Cancel' button in Action column; City red Complex FEL Pending with inline 'Skip' button and checkbox checked. Mountain row highlighted so RIGHT Selected file panel shows Mountain.mkv metadata and 'Conversion result' Completed, output D:\Media\Mountain - DV P8.1.mkv, target Profile 8.1, verification Passed, original Retained. Active Ocean remains separate from focused Mountain. Bottom Current conversion Ocean.mkv 72% verification, 18 of 25 checks, Prepare/Convert/Remux complete, Verify active, Publish pending. Bottom button 'Cancel batch' secondary. Short label near progress 'Row Cancel stops only that file; batch continues.' Command toolbar disabled during active batch. Skip action is available only on pending City; it unchecks City and changes status to Skipped. No retained track list and no extra approval controls.

## Final visual corrections

Scan results: removed an unwanted generated conversion summary panel and extra Convert button. Conversion review: removed generated per-row Convert actions; fixed sample duration/size. Conversion progress: active/completed checkboxes remain checked but disabled.

