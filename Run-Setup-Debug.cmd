@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_CLI_HOME=%~dp0artifacts\dotnet-home"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"
dotnet build src\FruitsAtelier.App\FruitsAtelier.App.csproj -c Release --nologo --verbosity minimal
if errorlevel 1 (
  echo Build failed. Please inspect the output above.
  pause
  exit /b 1
)
start "" "%~dp0src\FruitsAtelier.App\bin\Release\net8.0-windows\FruitsAtelier.App.exe" --first-run-setup
