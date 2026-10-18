[CmdletBinding()]
param(
    [version]$Version = '1.0.0.0',
    [string]$MakeAppxPath
)

$ErrorActionPreference = 'Stop'
$identity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'StoreIdentity.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$buildParameters = @{
    ForStore = $true
    IdentityName = $identity.IdentityName
    Publisher = $identity.Publisher
    PublisherDisplayName = $identity.PublisherDisplayName
    DisplayName = $identity.DisplayName
    Version = $Version
}
if ($MakeAppxPath) { $buildParameters.MakeAppxPath = $MakeAppxPath }
& (Join-Path $PSScriptRoot 'Build-Msix.ps1') @buildParameters
