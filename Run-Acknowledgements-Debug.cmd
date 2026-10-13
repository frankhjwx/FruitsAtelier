@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_CLI_HOME=%~dp0artifacts\dotnet-home"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"

:ChooseBuildDirectory
set "FA_PREVIEW_DIR=%~dp0artifacts\acknowledgements-debug\%RANDOM%-%RANDOM%"
if exist "%FA_PREVIEW_DIR%" goto ChooseBuildDirectory
dotnet build src\FruitsAtelier.App\FruitsAtelier.App.csproj -c Release --nologo --verbosity minimal -p:RestoreLockedMode=true -p:OutputPath="%FA_PREVIEW_DIR%/"
if errorlevel 1 (
  echo Build failed. Please inspect the output above.
  pause
  exit /b 1
)
start "" "%FA_PREVIEW_DIR%\FruitsAtelier.App.exe" --acknowledgements-preview %*
