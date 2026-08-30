# Echo installer

Builds a real Windows `.exe` installer for the Echo desktop app.

## Prerequisites

- Windows
- .NET 8 SDK (`dotnet --version` ≥ 8.0)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — install via `winget install JRSoftware.InnoSetup -e`

## Build

From the repo root:

```cmd
installer\build-installer.cmd
```

This:
1. `dotnet publish` the app self-contained for `win-x64` into `installer/staging/`.
2. Runs `ISCC.exe` to compile `installer/echo-setup.iss` into `installer/output/Echo-Setup-<version>.exe`.

## Output

`installer/output/Echo-Setup-1.0.0.exe` — single-file installer. Double-click to install to `C:\Program Files\Echo`. Creates Start Menu + optional Desktop shortcut. Registers uninstall entry in Add/Remove Programs.

## What it installs

- App binaries in `{pf}\Echo\`
- Per-user data dirs at `%LOCALAPPDATA%\Echo\{logs,models,recordings}\` (matches what the app expects)
- HKCU\Software\Echo registry keys (InstallPath, Version)
- Optional: PATH addition (off by default)
- Optional: launch on finish

User data in `%LOCALAPPDATA%\Echo\` is preserved on uninstall.

## Web distribution

Copy the built `Echo-Setup-*.exe` to `website/frontend/public/downloads/Echo-Setup.exe` so the marketing site's **Download for Windows** button works in production.
