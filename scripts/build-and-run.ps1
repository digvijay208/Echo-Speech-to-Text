taskkill /F /IM Echo.exe
Set-Location "C:\Users\WAGHAMODE\OneDrive\Desktop\murmer"
& "C:\Program Files\dotnet\dotnet.exe" build "Echo.csproj" -c "Debug" --nologo
& ".\bin\Debug\net8.0-windows\Echo.exe"
