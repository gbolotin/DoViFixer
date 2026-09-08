# Fixture provenance

The three binary fixtures originate from [quietvoid/dovi_tool](https://github.com/quietvoid/dovi_tool/tree/d1abe0e27ff2c7ab3339614d06db9f8a058af6b2), tag 2.3.3, commit `d1abe0e27ff2c7ab3339614d06db9f8a058af6b2`. The repository's MIT license is included as `dovi_tool.LICENSE`. These are small upstream test vectors, not user movie files.

| File | Upstream path / generation | SHA-256 |
| --- | --- | --- |
| `regular_start_code_4_muxed_el.hevc` | `assets/hevc_tests/regular_start_code_4_muxed_el.hevc` | `968A31F36CEB785F43E097FA421F3E8B7E686F0C3973CC45BBB5BA48616DE56A` |
| `fel_orig.bin` | `assets/tests/fel_orig.bin` | `B2B27714B7279C4E24D1A795CB6F95D3AD06745DB96D360698EE0932184117A0` |
| `mel_orig.bin` | `assets/tests/mel_orig.bin` | `CABDA945D36CD4C3ECA9C2DF05CF387562E648E3250BE28632C9B2991F2EC60B` |
| `fel-rpu.json` | dovi_tool 2.3.3 `export -i fel_orig.bin -d all=fel-rpu.json` | `1C0DE8973006AFB1E6F3EF67D825A162BA9A449D316ECB22637BE2D9D954E946` |
| `mel-rpu.json` | dovi_tool 2.3.3 `export -i mel_orig.bin -d all=mel-rpu.json` | `5B8BB71A97E54B9F7DD45CEA906399EDD1568EAACCC3B58766E97AF2A061D9D8` |

`mkvmerge.json` is identification output from MKVToolNix 100.0 after remuxing the HEVC vector. The file path and container creation dates were sanitized. Tests supply a small hand-authored MediaInfo-schema fixture with the known video dimensions/timing; it is not represented as captured MediaInfo output.

The muxed HEVC upstream vector contains Profile 8 metadata. Native tests first use dovi_tool mode 1 to create a Profile 7 MEL test stream, then exercise the conversion/backup/restore path. Optional PCM audio, SRT subtitles, XML tags/chapters and text attachments are generated inside disposable workspaces. No binary tools or personal media are included here.
