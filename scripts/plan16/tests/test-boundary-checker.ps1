#Requires -Version 5.1
<#
.SYNOPSIS
    Self-tests for the Plan 16 boundary checker script.

.DESCRIPTION
    Creates temporary git repos with fixtures to test both allowed and forbidden
    change patterns. Runs the boundary checker against each fixture and verifies
    the expected outcome (PASS or FAIL).

    Test matrix:
     1. docs addition → PASS (default allowlist)
     2. scripts addition → PASS (default allowlist)
     3. global.json addition → PASS (default allowlist)
     4. NetAccel.Managed addition → PASS (default allowlist)
     5. Unknown ServiceLib source modification → FAIL (not in allowlist)
     6. Explicit -AllowedPathPrefixes permits one ServiceLib file → PASS
     7. Avalonia modification with -AllowedPathPrefixes → still FAIL (permanent deny)
     8. Delete classic parser → FAIL (permanent deny)
     9. Delete CoreManager → FAIL (permanent deny)
    10. Unknown root directory file → FAIL (not in allowlist)
    11. No changes → PASS
    12. global.json.evil rejected despite default global.json → FAIL (exact match regression)
    13. ConfigHandler.cs.backup rejected with .cs-only allowlist → FAIL (exact match regression)
    14. Unstaged deletion of critical file, explicitly allowlisted → FAIL (permanent deny)
    15. Staged deletion of critical file (AppManager.cs), explicitly allowlisted → FAIL (permanent deny)
    16. Unstaged deletion of protected root file → FAIL (permanent deny)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/plan16/tests/test-boundary-checker.ps1
#>

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$checkerScript = Resolve-Path (Join-Path $scriptDir "..\check-change-boundaries.ps1")
$tempRoot = Join-Path $env:TEMP "plan16-boundary-test-$(Get-Date -Format 'yyyyMMddHHmmss')"

$script:testCount = 0
$script:passCount = 0
$script:failCount = 0

function Invoke-BoundaryTest {
    param(
        [string]$Name,
        [scriptblock]$BaseSetup,       # Creates files for the initial (base) commit
        [scriptblock]$ChangeSetup,     # Creates/modifies/deletes files after base commit
        [int]$ExpectedExitCode,
        [string[]]$ExtraArgs = @(),    # Extra args to pass to the checker
        [switch]$SkipCommit,           # Do not commit changes (for staged/unstaged tests)
        [switch]$SkipStage             # Do not stage changes (leave unstaged; implies SkipCommit)
    )
    $script:testCount++
    $testDir = Join-Path $tempRoot $Name

    Write-Host ""
    Write-Host "--- Test $script:testCount`: $Name ---"

    try {
        # Create temp repo
        New-Item -ItemType Directory -Path $testDir -Force | Out-Null
        Push-Location $testDir

        & git init 2>&1 | Out-Null
        & git config user.email "test@test.com" 2>&1 | Out-Null
        & git config user.name "Test" 2>&1 | Out-Null

        # Phase 1: Create base state
        "initial" | Out-File -FilePath "base.txt" -Encoding utf8
        if ($BaseSetup) { & $BaseSetup }
        & git add . 2>&1 | Out-Null
        & git commit -m "base commit" 2>&1 | Out-Null

        $baseRef = & git rev-parse HEAD 2>&1

        # Phase 2: Apply changes
        if ($ChangeSetup) { & $ChangeSetup }

        if ($SkipStage) {
            # Leave all changes unstaged (do not git add)
            $SkipCommit = $true
        } elseif ($SkipCommit) {
            # Stage changes but do not commit
            & git add . 2>&1 | Out-Null
        } else {
            # Default: stage and commit
            & git add . 2>&1 | Out-Null
            $hasChanges = & git status --porcelain 2>&1
            if ($hasChanges) {
                & git commit -m "test changes" 2>&1 | Out-Null
            }
        }

        # Phase 3: Run checker
        $checkerArgs = @("-ExecutionPolicy", "Bypass", "-File", $checkerScript, "-BaseRef", $baseRef, "-RepositoryRoot", $testDir) + $ExtraArgs
        $output = & powershell @checkerArgs 2>&1
        $actualExitCode = $LASTEXITCODE

        # Phase 4: Verify
        if ($actualExitCode -eq $ExpectedExitCode) {
            $script:passCount++
            Write-Host "PASS: Expected exit code $ExpectedExitCode, got $actualExitCode"
        } else {
            $script:failCount++
            Write-Host "FAIL: Expected exit code $ExpectedExitCode, got $actualExitCode"
            Write-Host "Output:"
            $output | ForEach-Object { Write-Host "  $_" }
        }
    } catch {
        $script:failCount++
        Write-Host "FAIL: Exception - $_"
    } finally {
        Pop-Location
    }
}

Write-Host "=== Plan 16 Boundary Checker Self-Tests ==="
Write-Host "Temp directory: $tempRoot"

# ============================================================
# Test 1: Allowed - docs addition (default allowlist)
# ============================================================
Invoke-BoundaryTest -Name "allowed-docs-addition" -ExpectedExitCode 0 `
    -BaseSetup {} `
    -ChangeSetup {
        New-Item -ItemType Directory -Path "docs" -Force | Out-Null
        "# Test doc" | Out-File -FilePath "docs/test.md" -Encoding utf8
    }

# ============================================================
# Test 2: Allowed - scripts addition (default allowlist)
# ============================================================
Invoke-BoundaryTest -Name "allowed-scripts-addition" -ExpectedExitCode 0 `
    -BaseSetup {} `
    -ChangeSetup {
        New-Item -ItemType Directory -Path "scripts/plan16" -Force | Out-Null
        "# Test script" | Out-File -FilePath "scripts/plan16/test.ps1" -Encoding utf8
    }

# ============================================================
# Test 3: Allowed - global.json (default allowlist)
# ============================================================
Invoke-BoundaryTest -Name "allowed-global-json" -ExpectedExitCode 0 `
    -BaseSetup {} `
    -ChangeSetup {
        '{ "sdk": { "version": "10.0.301" } }' | Out-File -FilePath "global.json" -Encoding utf8
    }

# ============================================================
# Test 4: Allowed - NetAccel.Managed project (default allowlist)
# ============================================================
Invoke-BoundaryTest -Name "allowed-managed-project" -ExpectedExitCode 0 `
    -BaseSetup {} `
    -ChangeSetup {
        New-Item -ItemType Directory -Path "NetAccel.Managed" -Force | Out-Null
        "<Project></Project>" | Out-File -FilePath "NetAccel.Managed/NetAccel.Managed.csproj" -Encoding utf8
    }

# ============================================================
# Test 5: Rejected - unknown ServiceLib source modification
# ============================================================
Invoke-BoundaryTest -Name "rejected-unknown-serviceLib" -ExpectedExitCode 1 `
    -BaseSetup {
        New-Item -ItemType Directory -Path "v2rayN/ServiceLib/Handler" -Force | Out-Null
        "original" | Out-File -FilePath "v2rayN/ServiceLib/Handler/ConfigHandler.cs" -Encoding utf8
    } `
    -ChangeSetup {
        "modified" | Out-File -FilePath "v2rayN/ServiceLib/Handler/ConfigHandler.cs" -Encoding utf8
    }

# ============================================================
# Test 6: Allowed - explicit allowlist permits one ServiceLib file
# ============================================================
Invoke-BoundaryTest -Name "allowed-explicit-allowlist-serviceLib" -ExpectedExitCode 0 `
    -BaseSetup {
        New-Item -ItemType Directory -Path "v2rayN/ServiceLib/Handler" -Force | Out-Null
        "original" | Out-File -FilePath "v2rayN/ServiceLib/Handler/ConfigHandler.cs" -Encoding utf8
    } `
    -ChangeSetup {
        "modified" | Out-File -FilePath "v2rayN/ServiceLib/Handler/ConfigHandler.cs" -Encoding utf8
    } `
    -ExtraArgs @("-AllowedPathPrefixes", "v2rayN/ServiceLib/Handler/ConfigHandler.cs")

# ============================================================
# Test 7: Rejected - Avalonia even with allowlist (permanent deny)
# ============================================================
Invoke-BoundaryTest -Name "rejected-avalonia-with-allowlist" -ExpectedExitCode 1 `
    -BaseSetup {
        New-Item -ItemType Directory -Path "v2rayN/v2rayN.Desktop" -Force | Out-Null
        "original" | Out-File -FilePath "v2rayN/v2rayN.Desktop/App.axaml.cs" -Encoding utf8
    } `
    -ChangeSetup {
        "modified" | Out-File -FilePath "v2rayN/v2rayN.Desktop/App.axaml.cs" -Encoding utf8
    } `
    -ExtraArgs @("-AllowedPathPrefixes", "v2rayN/v2rayN.Desktop/")

# ============================================================
# Test 8: Rejected - delete classic parser (permanent deny)
# ============================================================
Invoke-BoundaryTest -Name "rejected-delete-parser" -ExpectedExitCode 1 `
    -BaseSetup {
        New-Item -ItemType Directory -Path "v2rayN/ServiceLib/Handler/Fmt" -Force | Out-Null
        "original" | Out-File -FilePath "v2rayN/ServiceLib/Handler/Fmt/FmtHandler.cs" -Encoding utf8
    } `
    -ChangeSetup {
        Remove-Item "v2rayN/ServiceLib/Handler/Fmt/FmtHandler.cs"
    }

# ============================================================
# Test 9: Rejected - delete CoreManager (permanent deny)
# ============================================================
Invoke-BoundaryTest -Name "rejected-delete-coremanager" -ExpectedExitCode 1 `
    -BaseSetup {
        New-Item -ItemType Directory -Path "v2rayN/ServiceLib/Manager" -Force | Out-Null
        "original" | Out-File -FilePath "v2rayN/ServiceLib/Manager/CoreManager.cs" -Encoding utf8
    } `
    -ChangeSetup {
        Remove-Item "v2rayN/ServiceLib/Manager/CoreManager.cs"
    }

# ============================================================
# Test 10: Rejected - unknown root directory file
# ============================================================
Invoke-BoundaryTest -Name "rejected-unknown-root-file" -ExpectedExitCode 1 `
    -BaseSetup {} `
    -ChangeSetup {
        "some config" | Out-File -FilePath "random-config.json" -Encoding utf8
    }

# ============================================================
# Test 11: No changes → PASS
# ============================================================
Invoke-BoundaryTest -Name "no-changes" -ExpectedExitCode 0 `
    -BaseSetup {} `
    -ChangeSetup {}

# ============================================================
# Test 12: global.json.evil rejected despite default global.json
#   Defect 1 regression: exact file entries must not use StartsWith
# ============================================================
Invoke-BoundaryTest -Name "rejected-global-json-evil" -ExpectedExitCode 1 `
    -BaseSetup {} `
    -ChangeSetup {
        '{ "sdk": { "version": "evil" } }' | Out-File -FilePath "global.json.evil" -Encoding utf8
    }

# ============================================================
# Test 13: ConfigHandler.cs.backup rejected when only .cs is allowlisted
#   Defect 1 regression: exact file entry must not match longer names
# ============================================================
Invoke-BoundaryTest -Name "rejected-cs-backup-with-cs-allowlist" -ExpectedExitCode 1 `
    -BaseSetup {
        New-Item -ItemType Directory -Path "v2rayN/ServiceLib/Handler" -Force | Out-Null
        "original" | Out-File -FilePath "v2rayN/ServiceLib/Handler/ConfigHandler.cs" -Encoding utf8
    } `
    -ChangeSetup {
        "backup" | Out-File -FilePath "v2rayN/ServiceLib/Handler/ConfigHandler.cs.backup" -Encoding utf8
    } `
    -ExtraArgs @("-AllowedPathPrefixes", "v2rayN/ServiceLib/Handler/ConfigHandler.cs")

# ============================================================
# Test 14: Unstaged deletion of critical classic file → FAIL
#   Defect 2 regression: unstaged deletions must be included
#   Even explicitly allowlisted critical deletions must fail (permanent deny)
# ============================================================
Invoke-BoundaryTest -Name "rejected-unstaged-delete-critical-allowlisted" -ExpectedExitCode 1 `
    -BaseSetup {
        New-Item -ItemType Directory -Path "v2rayN/ServiceLib/Manager" -Force | Out-Null
        "original" | Out-File -FilePath "v2rayN/ServiceLib/Manager/CoreManager.cs" -Encoding utf8
    } `
    -ChangeSetup {
        Remove-Item "v2rayN/ServiceLib/Manager/CoreManager.cs"
    } `
    -ExtraArgs @("-AllowedPathPrefixes", "v2rayN/ServiceLib/Manager/CoreManager.cs") `
    -SkipStage

# ============================================================
# Test 15: Staged deletion of critical classic file → FAIL
#   Defect 2 regression: staged deletions must be included
#   Even explicitly allowlisted critical deletions must fail (permanent deny)
# ============================================================
Invoke-BoundaryTest -Name "rejected-staged-delete-critical-allowlisted" -ExpectedExitCode 1 `
    -BaseSetup {
        New-Item -ItemType Directory -Path "v2rayN/ServiceLib/Manager" -Force | Out-Null
        "original" | Out-File -FilePath "v2rayN/ServiceLib/Manager/AppManager.cs" -Encoding utf8
    } `
    -ChangeSetup {
        Remove-Item "v2rayN/ServiceLib/Manager/AppManager.cs"
    } `
    -ExtraArgs @("-AllowedPathPrefixes", "v2rayN/ServiceLib/Manager/AppManager.cs") `
    -SkipCommit

# ============================================================
# Test 16: Unstaged deletion of protected root file → FAIL
#   Defect 2 regression: unstaged deletions of protected root files
# ============================================================
Invoke-BoundaryTest -Name "rejected-unstaged-delete-protected-root" -ExpectedExitCode 1 `
    -BaseSetup {
        "# license" | Out-File -FilePath "LICENSE" -Encoding utf8
    } `
    -ChangeSetup {
        Remove-Item "LICENSE"
    } `
    -SkipStage

# ============================================================
# Summary
# ============================================================
Write-Host ""
Write-Host "=== Test Summary ==="
Write-Host "Total: $($script:testCount)"
Write-Host "Passed: $($script:passCount)"
Write-Host "Failed: $($script:failCount)"

# Cleanup
try {
    Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host ""
    Write-Host "Cleaned up temp directory."
} catch {
    Write-Host ""
    Write-Host "Warning: Could not clean up $tempRoot"
}

if ($script:failCount -gt 0) {
    Write-Host ""
    Write-Host "RESULT: SOME TESTS FAILED"
    exit 1
} else {
    Write-Host ""
    Write-Host "RESULT: ALL TESTS PASSED"
    exit 0
}
