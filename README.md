# Echo — Speech to Text

> On-device voice dictation for Windows. Hold a key, talk, watch the words land anywhere you can type. Zero cloud. Zero telemetry.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078d4)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](#requirements)
[![License](https://img.shields.io/badge/license-MIT-green)](#license)

---

## What is Echo?

**Echo TC-80** is a 1980s-styled push-to-talk dictation deck for Windows. It uses
[Whisper.net](https://github.com/sandrohanea/whisper.net) to transcribe your speech
on-device and injects the result into whatever text field is focused — chat
windows, documents, code editors, terminals.

Everything runs locally. No audio ever leaves your machine.

---

## Features

- **Push-to-talk.** Hold `Right Ctrl` to record. Release to inject. No UI
  ceremony, no mode switching.
- **Works everywhere.** Injects into any focused text field across any app —
  Chrome, VS Code, Slack, Notepad, terminals, games with text chat.
- **Personal dictionary.** Two-pass correction pipeline with keywords (bias
  the model) and correction rules (post-process). Glued-word matching catches
  terms that come out joined together.
- **Tape history.** Every dictation logged with timestamps, durations, raw
  text, and which corrections were applied. Re-inject any past entry.
- **Bring your own model.** Drop any Whisper ggml model into
  `%LOCALAPPDATA%\Echo\models\` — tiny / base / small / medium / large-v3
  supported.
- **Live VU meter.** Ballistic analog VU with peak hold. Because why not.
- **Global tray icon.** Show / hide / settings / exit from the system tray.

---

## Download

Grab the latest installer:

> **→ [Echo-Setup-1.0.0.exe](https://github.com/digvijay208/Echo-Speech-to-Text/releases/latest)**
> (~50 MB · SHA-256: `5b8e7cee82a2a28fd2c2ccb02de43e720611f6007d7a48a1c465160255c1aa9a`)

Or download directly from the repo: [`installer/output/Echo-Setup-1.0.0.exe`](installer/output/Echo-Setup-1.0.0.exe).

Double-click `Echo-Setup-1.0.0.exe` to install. Creates a Start Menu shortcut
and (optionally) a desktop shortcut. Registers an uninstall entry in
**Settings → Apps**.

---

## Requirements

- Windows 10 (build 19041 or later) or Windows 11
- x64 CPU
- ~500 MB free disk (installer + model)
- Microphone (any input device)
- Whisper model file (the installer does not bundle a model — download one,
  see below)

The installer is self-contained and bundles the .NET 8 runtime, so no
separate runtime install is needed.

### Models

Echo expects Whisper ggml model files. Download from
[ggerganov/whisper.cpp](https://huggingface.co/ggggerganov/whisper.cpp/tree/main)
or any compatible source:

| Model | Size  | Speed   | Accuracy |
|-------|-------|---------|----------|
| tiny  | ~75 MB | fastest | lowest   |
| base  | ~140 MB | fast   | decent   |
| small | ~460 MB | medium | good     |
| medium| ~1.5 GB | slow   | great    |
| large-v3 | ~3 GB | slowest | best |

Place the `.bin` file in `%LOCALAPPDATA%\Echo\models\`. Echo will auto-pick
the first one it finds on launch. Use **Settings → Model** to switch.

---

## Building from source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (only for the installer)
  - `winget install JRSoftware.InnoSetup -e`
- [Node.js 18+](https://nodejs.org/) (only for the marketing site)

### Desktop app

```cmd
dotnet build Echo.csproj -c Release
dotnet run --project Echo.csproj -c Release
```

The dev build goes to `bin/Release/net8.0-windows/`. Launch `Echo.exe`.

### Installer

```cmd
installer\build-installer.cmd
```

Produces `installer/output/Echo-Setup-<version>.exe`. The script runs
`dotnet publish` (self-contained, win-x64) then compiles the Inno Setup
script.

### Marketing site

```cmd
cd website\frontend
npm install
npm run dev      # http://localhost:5173

cd ..\server
npm install
npm run dev      # http://localhost:3001
```

The Vite dev server proxies `/api/*` to the Express backend on 3001.

### Production build

```cmd
cd website\frontend
npm run build    # outputs to dist/

cd ..\server
node index.js    # serves dist/ + API on 3001
```

Drop the installer into `website/server/public/downloads/Echo-Setup.exe` for
the **Download for Windows** button to work.

---

## Project layout

```
.
├── Echo.csproj              WPF / .NET 8 desktop app
├── App.xaml(.cs)            entry point + tray + global hook
├── Controls/                AnalogVuMeter, custom widgets
├── Models/                  HistoryEntry, DictionaryEntry (JSON-persisted)
├── Native/                  Win32 P/Invoke (keyboard hook, window helpers)
├── Services/                audio capture, transcription, history, etc.
├── Views/                   MainWindow, SettingsWindow, HudOverlayWindow
├── installer/
│   ├── echo-setup.iss       Inno Setup script
│   ├── build-installer.cmd  one-shot build
│   └── output/              built installer (committed)
└── website/
    ├── frontend/            Vite + React + TS marketing site
    └── server/              Express backend (API + static + downloads)
```

---

## API (server)

| Method | Path                | Description                                  |
|--------|---------------------|----------------------------------------------|
| GET    | `/api/health`       | Liveness + uptime                            |
| GET    | `/api/app`          | App metadata (version, features, hotkey)     |
| GET    | `/api/waitlist`     | Current waitlist count                       |
| POST   | `/api/waitlist`     | Join waitlist (`{ email }`, rate-limited)    |
| GET    | `/downloads/*`      | Static installer assets (`attachment`)       |

The waitlist persists to `website/server/data/waitlist.json` (atomic write).
Rate-limited to 5 requests / minute / IP.

---

## Privacy

Echo is local-only:

- No network calls of any kind (verify with Wireshark if you want)
- No analytics, no crash reports, no telemetry
- No update checks (you choose when to install a new version)
- Models, recordings, logs, history all live in `%LOCALAPPDATA%\Echo\`
- Uninstaller preserves user data; delete that folder to wipe everything

---

## Roadmap

- [ ] macOS / Linux builds
- [ ] Custom hotkey + per-app hotkey profiles
- [ ] Streaming transcription (start injecting before recording ends)
- [ ] Cloud sync as opt-in only, E2E encrypted
- [ ] Plugin system for app-specific cleanup rules

---

## Contributing

Issues, PRs, and model recommendations welcome. Please test installer changes
on a clean Windows VM before submitting.

---

## License

MIT. See [`LICENSE`](LICENSE) (add your name + year when you add the file).

---

## Credits

- [Whisper.net](https://github.com/sandrohanea/whisper.net) — .NET bindings
- [whisper.cpp](https://github.com/ggerganov/whisper.cpp) — inference engine
- [NAudio](https://github.com/naudio/NAudio) — Windows audio capture
- [Inno Setup](https://jrsoftware.org/isinfo.php) — installer compiler
