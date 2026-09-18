[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsWindows) { throw 'This check requires PowerShell 7 on Windows.' }
$repo = Split-Path $PSScriptRoot -Parent
$Archive = [IO.Path]::GetFullPath($Archive)
$expectedHash = ((Get-Content -LiteralPath "$Archive.sha256" -Raw).Trim() -split '\s+')[0]
if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Package checksum mismatch.' }
$destination = Join-Path $repo "artifacts/package-check/用户 Release $([Guid]::NewGuid().ToString('N'))"
Expand-Archive -LiteralPath $Archive -DestinationPath $destination
$folders = @(Get-ChildItem -LiteralPath $destination -Directory)
if ($folders.Count -ne 1) { throw 'Expected one application folder in the ZIP.' }
$exe = Join-Path $folders[0].FullName 'FruitsAtelier.App.exe'
$report = Join-Path $destination 'package-check.json'
$processInfo = [Diagnostics.ProcessStartInfo]::new($exe)
$processInfo.UseShellExecute = $false
$processInfo.WorkingDirectory = $destination
$processInfo.CreateNoWindow = $true
$processInfo.ArgumentList.Add('--package-check')
$processInfo.ArgumentList.Add($report)
$processInfo.Environment['DOTNET_ROOT'] = Join-Path $destination 'no-installed-runtime'
$processInfo.Environment['DOTNET_ROOT_X64'] = $processInfo.Environment['DOTNET_ROOT']
$processInfo.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$process = [Diagnostics.Process]::Start($processInfo)
try {
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Packaged executable timed out.' }
    if (!(Test-Path -LiteralPath $report)) { throw "No package report; exit code $($process.ExitCode)." }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or !$result.success) { throw "Package check failed: $($result.error)" }
    $manifest = Get-Content -LiteralPath (Join-Path $folders[0].FullName 'build-info.json') -Raw | ConvertFrom-Json
    if ($result.architecture -ne 'X64' -or $result.version -ne "$($manifest.version)+$($manifest.commit)") { throw 'Package version, source commit or architecture mismatch.' }
    Write-Host "PASS: $($result.checks -join ', ')"
    Write-Host "Report: $report"
}
finally { $process.Dispose() }
