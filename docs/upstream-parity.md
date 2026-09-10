# Upstream parity and implementation status

Console phases 1–3 are implemented; WPF phase 4 is intentionally deferred. This is an initial Windows implementation with the validation boundaries listed below, not a claim of complete Python CLI compatibility.

Behavioral baseline: cryptochrome/dovi_convert **8.2.0**, local commit `7b682ebb4505b8c4a25a018881b640f74a4b383e`. The [upstream source](https://github.com/cryptochrome/dovi_convert/blob/main/dovi_convert.py) and [conversion documentation](https://docs.doviconvert.com/essentials/conversion) were rechecked during implementation on 2026-09-08. No upstream Python implementation was copied into the C# projects.

| Capability | Implemented behavior | Differences / validation |
| --- | --- | --- |
| Solution / DI | Four production projects, four test projects; .NET 10; Generic Host; shared registration extensions | No WPF/Prism assemblies until phase 4. Domain has no package dependencies. Container resolution/lifetimes/disposal are tested without native work. |
| Dependencies | Configured paths, managed locations, PATH and installed locations; bounded version/capability probes | Requires supported CLI identities and minimum major versions; GUI MediaInfo rejected before launch when PE subsystem identifies it. Explicit bad paths do not silently fall back. |
| Installation | Exact plan, separate consent, per-package outcomes, independent re-detection and immediate persisted path refresh | x64 portable ZIP catalog. WinGet source/digest must match; otherwise verified direct download. Unsupported architectures return manual recovery guidance. Real installation is not part of the tests run. |
| scan | File/directory input, recursive depth, ten sample positions, candidate filtering, typed results and JSON | Requires every requested sample to succeed for a classification. Unknown/failed evidence is distinct from complex FEL. No fallback MaxCLL guess. |
| inspect | Complete disk extraction and streamed per-frame RPU JSON analysis | Full RPU count must match ffprobe video packet count. This does not decode base-layer luminance. |
| convert | Profile 8.1 and HDR10; full analysis before planning; `--include-simple`, `--force`, `--backup`, `--temp`, `--output`, `--yes`, `--plan` | Streaming is the default with disk fallback; `--safe` forces extraction. Multiple file/directory inputs are accepted. Unknown analysis is never forced. One video track and Matroska input required. |
| Batch | Prepares exact targets, detects duplicate/colliding outputs, processes sequentially, continues after per-file failure | Cancellation stops subsequent work. Failed/skipped inputs and partial outcomes are explicit. |
| Retention | Original MKV is renamed to `.bak.dovi_convert`; output reuses its filename | `--delete` removes that backup only after verified publication; EL archives are retained. Failed/cancelled operations restore the original filename before publication; if recovery is blocked, the backup path and recovery error are reported. |
| Verification / publication | Stages a non-media partial on destination volume; validates then moves without overwrite; bounded sharing-lock retries | Verifies packet counts, timestamps, profile/RPUs, base/nonvideo payload hashes, attachments and selected metadata. Default-duration/container serialization may normalize. Non-pixel display units are rejected. |
| backup | `.dovi` TAR with `el.hevc` and `manifest.json`; re-reads and verifies archive before publication | Adds a DoViFixer v1 manifest with normalized BL and EL SHA-256. The baseline upstream reader selects `el.hevc` by name and can ignore the additional manifest; automated cross-language reader invocation is not included. |
| restore | Validates manifest/payload, checks exact BL pairing, reconstructs EL, remuxes and verifies | Upstream `el.hevc`-only TAR is accepted with source pairing reported as unverified. The adjacent archive is discovered automatically; `--source` overrides it. Container byte identity is not expected. |
| cleanup | Exact `.dovi` and `.bak.dovi_convert` selection; preview by default; revalidation before handle-bound deletion | Permanent deletion requires `--delete-backups` plus exact interactive approval or `--yes`. Does not delete original `.mkv` files or arbitrary temporary files. |
| Settings | Shared JSON settings, cross-process writer lock, atomic replacement; immediate validated tool-path refresh | `--temp` and `--output` do not persist. No database or background queue. |
| update-check | Explicit upstream release metadata retrieval | No own release URL exists, so output names upstream distinctly. No self-update/download or background update check. |

## Dependency sources

The package catalog is pinned deliberately, not automatically upgraded to the latest releases. Existing compatible versions are reused. All supported routes are per-user portable ZIPs without elevation; if that exact route cannot be validated, installation fails or a separately reviewed direct-download plan is offered before execution.

| Group | Version | Official source / integrity evidence |
| --- | --- | --- |
| Gyan.FFmpeg | 8.0.1 | [WinGet manifest](https://github.com/microsoft/winget-pkgs/blob/master/manifests/g/Gyan/FFmpeg/8.0.1/Gyan.FFmpeg.installer.yaml), [release ZIP](https://github.com/GyanD/codexffmpeg/releases/download/8.0.1/ffmpeg-8.0.1-full_build.zip) |
| MoritzBunkus.MKVToolNix | 95.0.0 | [WinGet manifest](https://github.com/microsoft/winget-pkgs/blob/master/manifests/m/MoritzBunkus/MKVToolNix/95.0.0/MoritzBunkus.MKVToolNix.installer.yaml), [portable ZIP](https://mkvtoolnix.download/windows/releases/95.0/mkvtoolnix-64-bit-95.0.zip) |
| MediaArea.MediaInfo (CLI) | 26.05 | [WinGet manifest](https://github.com/microsoft/winget-pkgs/blob/master/manifests/m/MediaArea/MediaInfo/26.05/MediaArea.MediaInfo.installer.yaml), [CLI ZIP](https://mediaarea.net/download/binary/mediainfo/26.05/MediaInfo_CLI_26.05_Windows_x64.zip) |
| quietvoid.dovi_tool | 2.3.3 | [Official release metadata](https://api.github.com/repos/quietvoid/dovi_tool/releases/tags/2.3.3), [release](https://github.com/quietvoid/dovi_tool/releases/tag/2.3.3) |

SHA-256 values from those sources are recorded in `Infrastructure/Dependencies/InstallationCatalog.cs`. A checksum mismatch stops extraction. ZIP paths are confined to the newly created installation directory. Existing version directories are not overwritten. Replacing invalid configured paths needs explicit repair consent; installation success alone never establishes executable readiness.

## Validation boundary

Fast tests cover pure rules, fake dependency/install coordination, parsing, planning/operation failure handling, archive corruption and traversal, publication races, file identity, settings concurrency, CLI parsing, and DI ownership. Native tests use dovi_tool 2.3.3, MKVToolNix 100.0 and an existing ffprobe executable with tiny public HEVC/RPU data; MediaInfo JSON is a controlled fixture.

The native tests convert MEL Profile 7 to 8.1 and HDR10, restore from a generated archive, and run the production verification path. They verify normalized BL and EL hashes, complete output RPU counts/profiles, Unicode paths, per-track timestamps, optional audio/subtitles, attachments, chapters and tags. They do not establish compatibility with every commercial FEL title or exercise real installers. Those checks require explicitly selected local media and an approved installation environment.

WPF dependency setup, views/ViewModels, Prism/DryIoc integration and related tests remain phase 4, as requested.
