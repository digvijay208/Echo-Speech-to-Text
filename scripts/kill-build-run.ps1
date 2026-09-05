Get-Process | Where-Object { $_.ProcessName -in @("Echo", "dotnet", "MSBuild", "VBCSCompiler") } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$proj = "C:\Users\WAGHAMODE\OneDrive\Desktop\murmer"
Remove-Item -Recurse -Force "$proj\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$proj\obj" -ErrorAction SilentlyContinue
Set-Location $proj
& "C:\Program Files\dotnet\dotnet.exe" build "Echo.csproj" -c "Debug" --nologo
if ($LASTEXITCODE -eq 0) { & ".\bin\Debug\net8.0-windows\Echo.exe" }
