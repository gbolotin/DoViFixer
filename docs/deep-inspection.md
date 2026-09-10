# Deep Inspection

Run `inspect <file.mkv> --deep`, optionally with `--json` and `--temp <directory>`.
Regular inspection still uses the full RPU / static MaxCLL heuristic; ordinary scan and conversion planning keep their existing behavior.

Use `scan <directory> -r --inspect-simple` to finish the sampled scan first, then automatically run the equivalent of `inspect --deep` on each Simple FEL candidate in scan order. This is opt-in and uses decoded-frame inspection, rather than regular `inspect`'s full-RPU metadata heuristic. MEL, Complex FEL, Unknown and failed scan results are not selected. An existing valid deep-inspection cache entry can be reused.

Each updated result includes its initial analysis and final evidence. Failures are reported while remaining candidates continue; cancellation stops the batch. `--json` emits one final result per file. `--candidates-only` retains auto-inspected candidates even if reclassified or failed, so changed verdicts remain visible. `--temp` selects scratch storage for both stages. No conversion is performed, and conversion planning retains its existing full-RPU policy.

Deep Inspection extracts the complete RPU and demuxes the HDR10 base layer. FFmpeg decodes every base-layer frame at its original resolution. Explicit PQ, BT.2020 non-constant-luminance and limited/full-range signaling are required. Missing signaling, filters, L1 metadata, decoding errors or mismatched frame counts produce a failed analysis rather than falling back to MaxCLL.

The filter converts PQ RGB to linear light, then computes BT.2020 luminance (0.2627 R + 0.6780 G + 0.0593 B). It measures each frame's maximum linear luminance using a full-range 16-bit plane normalized to 10,000 nits, with approximately 0.153-nit quantization. It performs no resizing or tone mapping. See the official [FFmpeg zscale documentation](https://ffmpeg.org/ffmpeg-filters.html#zscale) and [signalstats documentation](https://ffmpeg.org/ffmpeg-filters.html#signalstats).

Measurements are compared in presentation order with dovi_tool's exported RPU frame sequence. Every frame must have a measurement and L1 max_pq; frame indices must be contiguous and decoded-frame/RPU/video-packet counts must agree. JSON is streamed one RPU at a time and the temporary measurement file is read incrementally. Temporary files are removed on completion, failure or cancellation.

The report includes compared frames, frames exceeding a 50-nit difference, base-layer peak, maximum RPU-minus-base-layer difference and its zero-based frame index. FEL is classified as Complex when any compared frame exceeds that threshold; otherwise it is classified as Simple. This threshold is a heuristic, not a Dolby specification. Static MaxCLL is unnecessary in this mode.

L1 metadata can describe scene brightness, so a dark frame within a bright scene can trigger the indicator. This is evidence of possible expansion, not a reconstruction of FEL picture data or a guarantee that discarding FEL is safe. Native validation covers black, approximately 1,000-nit and 10,000-nit gray frames, saturated full-range RGB colors, and complete extraction/decoding/comparison of the repository's tiny Dolby Vision fixture. Commercial-title detection accuracy is not established.

Deep Inspection can take substantially longer than regular inspection and requires temporary space for extracted streams and RPU JSON. It uses existing dependencies; FFmpeg must include zscale, signalstats and metadata filters. It never installs missing software without the existing dependency-install consent flow.
