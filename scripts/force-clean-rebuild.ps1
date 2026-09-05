# Kill any lingering Echo / dotnet / msbuild processes
Get-Process | Where-Object { $_.ProcessName -in @("Echo", "dotnet", "MSBuild", "VBCSCompiler") } | Stop-Process -Force -ErrorAction SilentlyContinue

# Wait for the OS to actually release the file handle
Start-Sleep -Seconds 2

# Clean build outputs so nothing stale lingers
$proj = "C:\Users\WAGHAMODE\OneDrive\Desktop\murmer"
Remove-Item -Recurse -Force "$proj\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$proj\obj" -ErrorAction SilentlyContinue

Set-Location $proj
& "C:\Program Files\dotnet\dotnet.exe" build "Echo.csproj" -c "Debug" --nologo

if ($LASTEXITCODE -eq 0) {
    & ".\bin\Debug\net8.0-windows\Echo.exe"
} else {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
