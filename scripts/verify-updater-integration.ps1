param(
    [string]$UpdaterPath = ".\NetAccel.Updater\bin\Release\net10.0-windows\NetAccel.Updater.exe"
)

$ErrorActionPreference = "Stop"
$updater = (Resolve-Path -LiteralPath $UpdaterPath).Path
$root = Join-Path $env:TEMP ("netaccel-updater-integration-" + [guid]::NewGuid().ToString("N"))

function New-TestClient {
    param(
        [string]$Path,
        [bool]$Healthy
    )

    $className = "NetAccelUpdateTest" + [guid]::NewGuid().ToString("N")
    $healthCode = if ($Healthy) {
        'for (int i = 0; i + 1 < args.Length; i++) { if (args[i] == "--netaccel-update-health-marker") File.WriteAllText(args[i + 1], "healthy"); }'
    } else {
        ''
    }
    $source = @"
using System;
using System.IO;
using System.Threading;
public static class $className
{
    [STAThread]
    public static void Main(string[] args)
    {
        $healthCode
        Thread.Sleep(30000);
    }
}
"@
    Add-Type -TypeDefinition $source -OutputAssembly $Path -OutputType ConsoleApplication
}

function Invoke-Scenario {
    param(
        [string]$Name,
        [bool]$Healthy
    )

    $scenarioRoot = Join-Path $root $Name
    $sourceDir = Join-Path $scenarioRoot "source"
    $targetDir = Join-Path $scenarioRoot "target"
    $backupDir = Join-Path $scenarioRoot "backup"
    New-Item -ItemType Directory -Path $sourceDir, $targetDir, $backupDir -Force | Out-Null

    $sourceExe = Join-Path $sourceDir "NetAccel.exe"
    $targetExe = Join-Path $targetDir "NetAccel.exe"
    New-TestClient -Path $sourceExe -Healthy $Healthy
    Copy-Item -LiteralPath "$env:WINDIR\System32\where.exe" -Destination $targetExe

    $oldHash = (Get-FileHash -LiteralPath $targetExe -Algorithm SHA256).Hash
    $newHash = (Get-FileHash -LiteralPath $sourceExe -Algorithm SHA256).Hash
    $marker = Join-Path $scenarioRoot "healthy.marker"
    $planPath = Join-Path $scenarioRoot "update-plan.json"
    @{
        ParentProcessId = 2147483647
        SourceDirectory = $sourceDir
        TargetDirectory = $targetDir
        BackupDirectory = $backupDir
        ExecutableName = "NetAccel.exe"
        HealthMarkerPath = $marker
        HealthTimeoutSeconds = 5
    } | ConvertTo-Json | Set-Content -LiteralPath $planPath -Encoding UTF8

    $process = Start-Process `
        -FilePath $updater `
        -ArgumentList @("--plan", "`"$planPath`"") `
        -PassThru `
        -WindowStyle Hidden
    if (!$process.WaitForExit(20000)) {
        $process.Kill()
        throw "$Name updater timed out."
    }

    $installedHash = (Get-FileHash -LiteralPath $targetExe -Algorithm SHA256).Hash
    $children = Get-Process NetAccel -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "$targetDir*" }
    try {
        if ($Healthy) {
            if ($process.ExitCode -ne 0 -or $installedHash -ne $newHash -or !(Test-Path $marker)) {
                throw "Healthy update scenario failed: exit=$($process.ExitCode), installed=$($installedHash -eq $newHash), marker=$(Test-Path $marker)."
            }
        } else {
            if ($process.ExitCode -ne 1 -or $installedHash -ne $oldHash -or $children) {
                throw "Unhealthy update rollback scenario failed: exit=$($process.ExitCode), restored=$($installedHash -eq $oldHash), residual=$(@($children).Count)."
            }
        }
    } finally {
        $children | Stop-Process -Force -ErrorAction SilentlyContinue
        $process.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    Invoke-Scenario -Name "healthy" -Healthy $true
    Invoke-Scenario -Name "unhealthy" -Healthy $false
    Write-Output "UPDATER_INTEGRATION=PASS"
} finally {
    Get-Process NetAccel -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "$root*" } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    if (Test-Path $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
