param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ReleaseKeyId
)

$ErrorActionPreference = "Stop"

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
if ($ReleaseKeyId -notmatch '^[A-Za-z0-9._-]{1,64}$') {
    throw "Release key id contains unsupported characters."
}

$requiredFiles = @(
    (Join-Path $publishRoot "NetAccel.exe"),
    (Join-Path $publishRoot "updater\NetAccel.Updater.exe"),
    (Join-Path $publishRoot "release-trust\$ReleaseKeyId.pem")
)

foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required release file is missing: $requiredFile"
    }
    if ((Get-Item -LiteralPath $requiredFile).Length -le 0) {
        throw "Required release file is empty: $requiredFile"
    }
}

$forbiddenFiles = Get-ChildItem -LiteralPath $publishRoot -Recurse -File |
    Where-Object {
        $_.Extension -in @(".pfx", ".p12", ".key") -or
        $_.Name -match '(?i)(private|signing).*\.(pem|jwk|json)$'
    }
if ($forbiddenFiles) {
    $names = ($forbiddenFiles.FullName -join ", ")
    throw "Private signing material must not be packaged: $names"
}

Write-Host "RELEASE_PACKAGE_PASS"
