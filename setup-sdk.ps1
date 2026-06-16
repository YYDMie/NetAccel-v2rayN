# setup-sdk.ps1 - Install the pinned SDK used by the repository launcher.
# Usage: .\setup-sdk.ps1

$ErrorActionPreference = "Stop"
$requiredVersion = "10.0.301"
$userDotnet = Join-Path $env:USERPROFILE ".dotnet"
$dotnetExe = Join-Path $userDotnet "dotnet.exe"

$sdkPath = Join-Path $userDotnet "sdk\$requiredVersion"
if (-not (Test-Path -LiteralPath $sdkPath)) {
    Write-Host "[INFO] SDK not found. Installing .NET $requiredVersion to $userDotnet ..."
    $installScript = Join-Path $env:TEMP "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript -UseBasicParsing
    & $installScript -Version $requiredVersion -InstallDir $userDotnet
}

$verifyVersion = & $dotnetExe --version
if ($verifyVersion -eq $requiredVersion) {
    Write-Host "[OK] Installed .NET SDK $verifyVersion at $dotnetExe" -ForegroundColor Green
    Write-Host "[INFO] Use .\dotnet.cmd for repository build commands." -ForegroundColor Cyan
} else {
    Write-Host "[FAIL] Verification failed. Expected $requiredVersion, got $verifyVersion" -ForegroundColor Red
    exit 1
}
