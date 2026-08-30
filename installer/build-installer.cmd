@echo off
REM Echo installer build script.
REM Run from repo root:  installer\build-installer.cmd
REM Produces: installer\output\Echo-Setup-<version>.exe

setlocal
set ROOT=%~dp0..
set CONFIG=Release
set RUNTIME=win-x64
set STAGING=%~dp0staging
set OUTPUT=%~dp0output

echo === Cleaning staging ===
if exist "%STAGING%" rmdir /s /q "%STAGING%"
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"

echo === Publishing self-contained app ===
dotnet publish "%ROOT%\Echo.csproj" -c %CONFIG% -r %RUNTIME% --self-contained true ^
  -p:PublishSingleFile=false -p:DebugType=embedded ^
  -o "%STAGING%"
if errorlevel 1 exit /b 1

echo === Compiling installer ===
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "%~dp0echo-setup.iss"
) else if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" (
  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" "%~dp0echo-setup.iss"
) else (
  echo ERROR: ISCC.exe not found. Install Inno Setup 6.
  exit /b 1
)
if errorlevel 1 exit /b 1

echo.
echo === Done ===
echo Installer: %OUTPUT%\Echo-Setup-*.exe
endlocal
