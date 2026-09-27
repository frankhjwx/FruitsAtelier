param([ValidateSet('event-10','event-50','poll-10','poll-50')][string]$Profile = 'event-10')
$ErrorActionPreference = 'Stop'
try {
    $exe = Join-Path $PSScriptRoot 'FruitsAtelier.App.exe'
    if (!(Test-Path -LiteralPath $exe)) { throw 'Extract the entire ZIP before running this script.' }
    if (Get-Process -Name 'FruitsAtelier.App' -ErrorAction SilentlyContinue) {
        throw 'Close all FruitsAtelier windows before starting a diagnostic run.'
    }
    $runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Profile + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
    $capture = Join-Path (Split-Path $PSScriptRoot -Parent) ('audio-captures\' + $runName)
    New-Item -ItemType Directory -Path $capture -Force | Out-Null
    $env:FRUITSATELIER_AUDIO_DIAGNOSTICS = '1'
    $env:FRUITSATELIER_AUDIO_PROFILE = $Profile
    $env:FRUITSATELIER_AUDIO_LOG_DIRECTORY = $capture
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build-info.json') -Destination $capture
    "Profile: $Profile`r`nStarted: $([DateTimeOffset]::Now.ToString('O'))" | Set-Content -LiteralPath (Join-Path $capture 'run.txt')
    Write-Host "Profile: $Profile"
    Write-Host 'Open the affected map. Play at 100%, pause/resume five times, and try seeking.'
    Write-Host 'Then try your usual playback speed. Close the editor when finished.'
    Write-Host "Logs: $capture"
    $process = Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot -PassThru
    $process.WaitForExit()
    "Exit code: $($process.ExitCode)" | Add-Content -LiteralPath (Join-Path $capture 'run.txt')
    $logs = @(Get-ChildItem -LiteralPath $capture -Filter 'audio-*.jsonl')
    if (!$logs.Count) { Write-Host 'ERROR: No audio logs were created. Please send the capture ZIP anyway.' -ForegroundColor Red }
    else { Write-Host "Created $($logs.Count) audio log files." }
    $archive = $capture + '.zip'
    Compress-Archive -LiteralPath $capture -DestinationPath $archive
    Write-Host "Please send: $archive"
} catch { Write-Host $_ -ForegroundColor Red }
Read-Host 'Press Enter to close'
