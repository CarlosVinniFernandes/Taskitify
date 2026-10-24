[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9.-]{3,50}$')]
    [string]$IdentityName = 'Taskitify.Local',
    [string]$Publisher = 'CN=Taskitify.Local',
    [string]$PublisherDisplayName = 'Carlos Fernandes',
    [string]$DisplayName = 'Taskitify',
    [version]$Version = '1.0.0.0',
    [string]$MakeAppxPath,
    [switch]$ForStore
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ($Version.Revision -ne 0 -or $Version.Major -lt 1 -or
    $Version.Major -gt 65535 -or $Version.Minor -gt 65535 -or $Version.Build -lt 0 -or $Version.Build -gt 65535) {
    throw 'Use a four-part version such as 1.0.0.0. The Store requires the last part to be zero.'
}
if ($ForStore -and ($IdentityName -eq 'Taskitify.Local' -or $Publisher -eq 'CN=Taskitify.Local')) {
    throw 'ForStore requires the real IdentityName and Publisher from Partner Center.'
}
if ([string]::IsNullOrWhiteSpace($Publisher) -or [string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    throw 'Publisher and PublisherDisplayName must not be empty.'
}
if (!$MakeAppxPath) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (!$sdk) { throw 'Install the Windows 10/11 SDK with MakeAppx, or pass -MakeAppxPath.' }
    $MakeAppxPath = Join-Path $sdk.FullName 'x64\makeappx.exe'
}

# Each build gets an isolated staging folder. Never include old output files in a package.
$stage = Join-Path $repoRoot ('publish-msix\' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'payload'
$output = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $payload,$output -Force | Out-Null
& dotnet publish (Join-Path $repoRoot 'src\Taskitify\Taskitify.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
$manifest.Package.Identity.SetAttribute('Name', $IdentityName)
$manifest.Package.Identity.SetAttribute('Publisher', $Publisher)
$manifest.Package.Identity.SetAttribute('Version', $Version.ToString(4))
$manifest.Package.Properties.DisplayName = $DisplayName
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
$ns.AddNamespace('desktop', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10')
$manifest.SelectSingleNode('//uap:VisualElements', $ns).SetAttribute('DisplayName', $DisplayName)
$manifest.SelectSingleNode('//desktop:StartupTask', $ns).SetAttribute('DisplayName', $DisplayName)
$manifest.Save((Join-Path $payload 'AppxManifest.xml'))

# Derive standard package icons from the existing multi-resolution app icon.
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $payload 'Assets'
New-Item -ItemType Directory -Path $assets | Out-Null
# The icon's frames are PNG, which System.Drawing.Icon decodes as noise: read the largest frame's PNG directly.
$iconBytes = [IO.File]::ReadAllBytes((Join-Path $repoRoot 'src\Taskitify\Resources\Taskitify.ico'))
$largest = 0
for ($i = 0; $i -lt [BitConverter]::ToUInt16($iconBytes, 4); $i++) {
    $entry = 6 + 16 * $i
    $length = [BitConverter]::ToUInt32($iconBytes, $entry + 8)
    if ($length -gt $largest) { $largest = $length; $offset = [BitConverter]::ToUInt32($iconBytes, $entry + 12) }
}
$icon = New-Object IO.MemoryStream($iconBytes, $offset, $largest)
$source = [System.Drawing.Image]::FromStream($icon)
try {
    foreach ($asset in @(@('StoreLogo',50), @('Square44x44Logo',44), @('Square150x150Logo',150))) {
        $size = [int]$asset[1]
        $bitmap = New-Object System.Drawing.Bitmap($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, 0, 0, $size, $size)
            $bitmap.Save((Join-Path $assets ($asset[0] + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose(); $icon.Dispose() }

$suffix = if ($ForStore) { 'store' } else { 'local-unsigned' }
$package = Join-Path $output ("Taskitify_{0}_x64_{1}.msix" -f $Version.ToString(4), $suffix)
# Validation is intentionally enabled (no /nv).
$buildLog = Join-Path $stage 'makeappx.log'
& $MakeAppxPath pack /d $payload /p $package /o *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $buildLog -Tail 30
    throw "MSIX creation or manifest validation failed. See $buildLog"
}
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
Set-Content -LiteralPath ($package + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($package)) -Encoding ASCII
Write-Host "MSIX: $package"
Write-Host "SHA256: $hash"
Write-Host "Validation log: $buildLog"
if (!$ForStore) { Write-Warning 'Unsigned local package. Supply Partner Center identity with -ForStore before Store submission; local installation requires signing and a trusted certificate.' }
