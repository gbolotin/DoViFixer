# Streamed full RPU inspection: PR4 validation

The ordinary `FullRpu` path now relays the selected Matroska video stream through FFmpeg, `HevcAnnexB`, and dovi_tool. It falls back to disk extraction once after a streaming failure or frame-count mismatch. Deep inspection remains on disk. The cache version stays at 1 because the parser, evidence fields, and interpretation did not change. The existing scratch-space preflight remains in place for fallback.

## Native parity and measurements

These runs called `MediaProbe` directly, so they bypassed the application analysis cache. The test used separate RPU exports for streamed and disk runs. Source files were on a local Windows drive, and temporary files used the default local workspace. OS file caching was not controlled. Tools: FFmpeg 8.0.1, MKVToolNix 95.0, dovi_tool 2.3.3.

| Source | Evidence | Stream elapsed | Disk elapsed | Stream scratch | Disk scratch |
| --- | --- | ---: | ---: | ---: | ---: |
| Local `title_t07.mkv`, run 1 | FEL, 1,028 frames; equal evidence | 0.81 s | 1.16 s | 4,193,471 B | 344,668,019 B |
| Same source, run 2 | Equal evidence | 2.79 s | 2.44 s | 4,193,471 B | 344,668,019 B |
| Same source, run 3 | Equal evidence | 0.80 s | 1.14 s | 4,193,471 B | 344,668,019 B |
| 259-frame original fixture | Equal evidence; layer Unknown | 0.24 s | 0.25 s | 1,100,738 B | 1,187,060 B |
| 259-frame MEL fixture | Equal evidence | 0.21 s | 0.27 s | 1,214,217 B | 1,310,134 B |

The scratch figures are file totals at the end of each stage, including the generated fixture source in the fixture run. They are not measured peak usage. The streaming path did not create `inspection.hevc`; the disk comparison did. Fallback frequency was zero in these valid native runs. Timing does not establish a stable speed improvement. The consistent benefit here is avoiding a full raw-video write on successful inspection.

Focused tests cover truncated relay input, injected pipe failure, invalid exported JSON, frame-count mismatch, cancellation, failed fallback, nonzero Matroska track ID, and the deep-inspection disk path. The native parity fixture includes a Unicode and punctuation path. Existing `RpuCoverageTests` continue to check metadata-free-tail position and rejection rules.

Native parity is still unmeasured for separate Simple FEL and Complex FEL sources, missing in-band parameter sets, malformed or metadata-free native sources, a verified metadata-free tail, and network-source/local-scratch I/O. The existing disk fallback and evidence checks remain active for those cases. The measurements above support default streaming for ordinary full-RPU inspection because it avoids the full raw track write while retaining those checks; they do not support a speed claim.
