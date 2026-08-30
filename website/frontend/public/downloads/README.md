# Place `Echo-Setup.exe` here

The marketing site's **Download for Windows** button serves `/downloads/Echo-Setup.exe`. After building the installer (see `installer/README.md`), copy it here:

```cmd
copy installer\output\Echo-Setup-*.exe website\frontend\public\downloads\Echo-Setup.exe
```

For development with the Express backend, place the same file at `website/server/public/downloads/Echo-Setup.exe` (or symlink) and add a static-serve route in `server/index.js`.
