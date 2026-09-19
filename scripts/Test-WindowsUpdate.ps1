[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$FeedDirectory,
    [Parameter(Mandatory)][string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path $repo "artifacts/update-check/用户 Upgrade $([Guid]::NewGuid().ToString('N'))"
$install = Join-Path $root 'application'
Expand-Archive -LiteralPath $Archive -DestinationPath $install
$exe = Join-Path $install 'FruitsAtelier.exe'
$original = Get-Content -LiteralPath (Join-Path $install 'current/build-info.json') -Raw | ConvertFrom-Json
$sentinel = Join-Path $install 'preserved-user-file.txt'
'preserve me' | Set-Content -LiteralPath $sentinel
$feed = (Resolve-Path -LiteralPath $FeedDirectory).Path
$corrupt = Join-Path $root 'corrupt-feed'
New-Item -ItemType Directory -Path $corrupt | Out-Null
Get-ChildItem -LiteralPath $feed -File | Where-Object { $_.Extension -in '.json', '.nupkg' } | Copy-Item -Destination $corrupt
foreach ($package in Get-ChildItem -LiteralPath $corrupt -Filter '*.nupkg') {
    $stream = [IO.File]::OpenWrite($package.FullName)
    try { $stream.SetLength(32) } finally { $stream.Dispose() }
}
function Invoke-UpdateCheck([string]$source, [string]$report) {
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WorkingDirectory = $root
    $info.ArgumentList.Add('--update-package-check')
    $info.ArgumentList.Add($source)
    $info.ArgumentList.Add($report)
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Update test timed out.' }
        $deadline = [DateTime]::UtcNow.AddSeconds(90)
        while (!(Test-Path -LiteralPath $report) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
        if (!(Test-Path -LiteralPath $report)) { throw "Missing update report; exit code $($process.ExitCode)." }
        return Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    }
    finally { $process.Dispose() }
}
$bad = Invoke-UpdateCheck $corrupt (Join-Path $root 'corrupt.json')
if ($bad.success -or $bad.error -notmatch 'Checksum|size|hash') { throw "Corrupt package was not rejected by verification: $($bad.error)" }
$unchanged = Get-Content -LiteralPath (Join-Path $install 'current/build-info.json') -Raw | ConvertFrom-Json
if ($unchanged.version -ne $original.version) { throw 'Corrupt update changed the installed version.' }
Write-Host 'PASS: corrupt download leaves the installed application intact.'
$result = Invoke-UpdateCheck $feed (Join-Path $root 'upgraded.json')
if (!$result.success -or $result.version.Split('+')[0] -ne $ExpectedVersion) { throw "Update did not launch the expected version: $($result | ConvertTo-Json -Depth 4)" }
if ((Get-Content -LiteralPath $sentinel -Raw).Trim() -ne 'preserve me') { throw 'Update changed data outside current/.' }
Write-Host "PASS: portable update $($original.version) -> $ExpectedVersion, restart, bundled runtime and preserved user file."
Write-Host "Reports: $root"
