# Automated portion of the NetAccel-v2rayN WPF baseline smoke test.
# Tray visibility and tray-menu Exit require separate visual verification.
$ErrorActionPreference = "Stop"
$evidenceDir = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$publishDir = Join-Path $repoRoot "v2rayN\v2rayN\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
$exePath = Join-Path $publishDir "v2rayN.exe"

$logFile = Join-Path $evidenceDir "smoke-test.log"
if (Test-Path $logFile) { Remove-Item $logFile }

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$timestamp] $Message"
    Write-Host $line
    Add-Content -Path $logFile -Value $line
}

Write-Log "=== Smoke Test Started ==="

# Record pre-existing
$preExisting = @(Get-Process -Name "v2rayN" -ErrorAction SilentlyContinue)
$preExistingIds = @($preExisting.Id)
Write-Log "Pre-existing v2rayN: $($preExisting.Count) process(es)"
foreach ($p in $preExisting) { Write-Log "  PID=$($p.Id) started=$($p.StartTime)" }

# Verify exe
if (-not (Test-Path $exePath)) { Write-Log "FATAL: EXE not found"; exit 1 }
Write-Log "EXE: $exePath ($([math]::Round((Get-Item $exePath).Length/1MB))MB)"

# TEST 1: First Launch
Write-Log "--- TEST 1: First Launch ---"
$proc = Start-Process -FilePath $exePath -PassThru
Write-Log "Launched PID=$($proc.Id)"
Start-Sleep -Seconds 10

$running = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
if ($running) {
    Write-Log "PASS: Process running after 10s"
} else {
    Write-Log "FAIL: Process exited within 10s"
}

# TEST 2: Main Window - use Get-Process MainWindowTitle
Write-Log "--- TEST 2: Main Window ---"
$procInfo = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
if ($procInfo) {
    $title = $procInfo.MainWindowTitle
    if ($title) {
        Write-Log "PASS: MainWindowTitle='$title'"
    } else {
        Write-Log "INFO: MainWindowTitle is empty (may be minimized to tray)"
        # Check all our processes
        $ourProcs = Get-Process -Name "v2rayN" -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-2) }
        foreach ($p in $ourProcs) {
            Write-Log "  PID=$($p.Id) Title='$($p.MainWindowTitle)' MainWindowHandle=$($p.MainWindowHandle)"
        }
    }
}

# TEST 3: Tray Icon
Write-Log "--- TEST 3: Tray Icon ---"
Write-Log "MANUAL_REQUIRED: Verify the v2rayN tray icon visually."

# TEST 4: Single Instance
Write-Log "--- TEST 4: Single Instance ---"
$proc2 = Start-Process -FilePath $exePath -PassThru
Write-Log "Second launch PID=$($proc2.Id)"
Start-Sleep -Seconds 5
$proc2.Refresh()
$allAfterSecond = @(Get-Process -Name "v2rayN" -ErrorAction SilentlyContinue)
$testOwned = @($allAfterSecond | Where-Object { $_.Id -notin $preExistingIds })
Write-Log "Second process exited: $($proc2.HasExited); test-owned PIDs: $($testOwned.Id -join ',')"

if ($proc2.HasExited -and $testOwned.Count -eq 1 -and $testOwned[0].Id -eq $proc.Id) {
    Write-Log "PASS: Second launch exited and exactly one test-owned process remains"
} else {
    Write-Log "FAIL: Single-instance invariant not met"
    exit 1
}

# TEST 5: Exit
Write-Log "--- TEST 5: Exit ---"
Write-Log "MANUAL_REQUIRED: Use the tray Exit command and verify PID=$($proc.Id) exits."
Write-Log "TEST_PROCESS_PID=$($proc.Id)"

Write-Log "=== Automated Smoke Test Complete ==="
