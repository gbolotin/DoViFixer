"""Speak text with Kokoro TTS and save it as a WAV file.

Usage:
    python make-voiceover.py "Text to speak" out.wav [--voice am_michael] [--speed 1.1]
    python make-voiceover.py --file lines.txt out.wav
    python make-voiceover.py --list-voices

Needs the kokoro-onnx package and the model files in KOKORO_HOME
(default: %USERPROFILE%\\kokoro-tts\\models). See how-to-make-the-video.md.
"""

import argparse
import os
import sys
from pathlib import Path

import soundfile as sf
from kokoro_onnx import Kokoro

MODELS = Path(os.environ.get("KOKORO_HOME", Path.home() / "kokoro-tts" / "models"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("text", nargs="?", help="text to speak")
    parser.add_argument("output", nargs="?", help="WAV file to write")
    parser.add_argument("--file", help="read the text from this UTF-8 file instead")
    parser.add_argument("--voice", default="am_michael", help="voice name (default: am_michael)")
    parser.add_argument("--speed", type=float, default=1.1, help="speaking speed (default: 1.1)")
    parser.add_argument("--list-voices", action="store_true", help="print the available voices and exit")
    args = parser.parse_args()

    kokoro = Kokoro(str(MODELS / "kokoro-v1.0.onnx"), str(MODELS / "voices-v1.0.bin"))
    if args.list_voices:
        print("\n".join(sorted(kokoro.get_voices())))
        return 0

    # With --file, the only positional argument is the output path.
    if args.file:
        text = Path(args.file).read_text(encoding="utf-8")
        output = args.output or args.text
    else:
        text, output = args.text, args.output
    if not text or not output:
        parser.error("give the text (or --file) and an output path")

    samples, sample_rate = kokoro.create(text, voice=args.voice, speed=args.speed, lang="en-us")
    sf.write(output, samples, sample_rate)
    print(f"{output}: {len(samples) / sample_rate:.1f}s, voice {args.voice}, speed {args.speed}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
