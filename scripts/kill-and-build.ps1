taskkill /F /IM Echo.exe 2>&1 | Out-String
Set-Location "C:\Users\WAGHAMODE\OneDrive\Desktop\murmer"
& "C:\Program Files\dotnet\dotnet.exe" build Echo.csproj -c Debug --nologo 2>&1 | Out-String
& ".\bin\Debug\net8.0-windows\Echo.exe"
