param(
    [Parameter(Mandatory = $true)][string]$ArtifactPath,
    [Parameter(Mandatory = $true)][string]$ArtifactUrl,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [ValidateSet('x86_64', 'aarch64')][string]$Arch = 'x86_64',
    [ValidateSet('stable', 'beta', 'dev')][string]$Channel = 'stable',
    [string]$KeyId = $env:NETACCEL_RELEASE_SIGNING_KEY_ID,
    [string]$MinVersion = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$env:NETACCEL_RELEASE_SIGNING_KEY_ID = $KeyId
$arguments = @(
    'run', '--project', (Join-Path $repo 'tools\NetAccel.ReleaseSigner\NetAccel.ReleaseSigner.csproj'), '--',
    '--artifact', ([IO.Path]::GetFullPath($ArtifactPath)),
    '--url', $ArtifactUrl,
    '--version', $Version,
    '--output', ([IO.Path]::GetFullPath($OutputPath)),
    '--arch', $Arch,
    '--channel', $Channel
)
if (-not [string]::IsNullOrWhiteSpace($MinVersion)) {
    $arguments += @('--min-version', $MinVersion)
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Release manifest signing failed.' }
