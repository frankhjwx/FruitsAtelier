[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(alpha|beta|rc)\.[1-9][0-9]*)?$')][string]$Version,
    [string]$OutputDirectory,
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$UserManual
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
if (!$Version) { $Version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version }
if (!$IsWindows) { throw 'Publish-Windows.ps1 requires PowerShell 7 on Windows.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/releases' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$numericVersion = $Version.Split('-')[0]
foreach ($part in $numericVersion.Split('.')) { if ([int]$part -gt 65534) { throw 'Version components must be at most 65534.' } }
$package = "FruitsAtelier-$Version-win-x64"
$staging = Join-Path $repo "artifacts/publish/$package-$([Guid]::NewGuid().ToString('N'))"
$payload = Join-Path $staging $package
New-Item -ItemType Directory -Path $payload, $OutputDirectory -Force | Out-Null
Push-Location $repo
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source commit.' }
    $dirty = [bool](& git status --porcelain --untracked-files=normal)
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'The pinned .NET SDK is required; see global.json.' }
    & dotnet publish src/FruitsAtelier.App/FruitsAtelier.App.csproj -c Release -r win-x64 --self-contained true `
        -o $payload -p:PublishProfile=WindowsRelease -p:NuGetLockFilePath=packages.win-x64.lock.json -p:RestoreLockedMode=true `
        "-p:Version=$Version" "-p:FileVersion=$numericVersion.0" "-p:AssemblyVersion=$numericVersion.0" "-p:SourceRevisionId=$commit"
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    $manifest = [ordered]@{ version = $Version; commit = $commit; dirty = $dirty; runtimeIdentifier = 'win-x64'; sdk = $sdk }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payload 'build-info.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $repo 'docs/WINDOWS-PACKAGE.txt') -Destination (Join-Path $payload 'START-HERE.txt')
    Copy-Item -LiteralPath (Join-Path $repo 'docs/USER_MANUAL.md') -Destination (Join-Path $payload 'USER-MANUAL.md')
    if ($UserManual) { Copy-Item -LiteralPath $UserManual -Destination (Join-Path $payload 'FruitsAtelier-User-Manual.pdf') }
    foreach ($required in @('FruitsAtelier.App.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'THIRD_PARTY_NOTICES.md')) {
        if (!(Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Missing package file: $required" }
    }
    if (Test-Path -LiteralPath (Join-Path $payload 'assets/skins/default.osk')) { throw 'Local private skin must not be distributed.' }
    $archive = Join-Path $OutputDirectory "$package.zip"
    Compress-Archive -LiteralPath $payload -DestinationPath $archive -CompressionLevel Optimal -Force
    $digest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$digest  $package.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
    Write-Host "Package: $archive"
    Write-Host "SHA256:  $digest"
}
finally { Pop-Location }
