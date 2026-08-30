## Echo TC-80 v1.0.0

First public release. On-device voice dictation for Windows using Whisper.

### What's in the box
- `Echo-Setup-1.0.0.exe` — self-contained Windows installer (~50 MB, bundles .NET 8 runtime + Whisper)
- SHA-256: `5b8e7cee82a2a28fd2c2ccb02de43e720611f6007d7a48a1c465160255c1aa9a`

### Features
- 🎙️ **Push-to-talk** — hold Right Ctrl to record, release to inject
- ⌨️ **Works in every app** — global text injection into any focused field
- 📖 **Personal dictionary** — keyword bias + two-pass correction with glued-word matching
- 📼 **Tape history** — every dictation logged locally with corrections + re-inject
- 🎯 **Bring your own model** — drop in any Whisper ggml model (tiny → large-v3)
- 📊 **Analog VU meter** — live audio level with peak hold
- 🔒 **Zero telemetry** — fully offline, no network calls

### Requirements
- Windows 10 (build 19041+) or Windows 11
- x64 CPU
- Microphone
- A Whisper model file (not bundled — see README)

### Install
Double-click `Echo-Setup-1.0.0.exe`, follow the wizard. The app launches
automatically at the end. First run will prompt you to select a Whisper
model from `%LOCALAPPDATA%\Echo\models\`.

### Feedback
Issues and feature requests: https://github.com/digvijay208/Echo-Speech-to-Text/issues
