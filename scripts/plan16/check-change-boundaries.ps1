#Requires -Version 5.1
<#
.SYNOPSIS
    Plan 16 change boundary checker for NetAccel-v2rayN.

.DESCRIPTION
    Detects whether changes in the working tree violate Plan 16 boundaries:
    1. Avalonia project modifications are always denied.
    2. Deleted classic entry points or parsers are always denied.
    3. Source code or root-directory changes outside the allowlist are denied.
    4. Protected root file deletion is denied.
#>

param(
    [string]$BaseRef = "1869a95700e17369f071ed23c8c485c2c3e83a1d",
    [string[]]$AllowedPathPrefixes = @(),
    [string]$RepositoryRoot = "",
    [switch]$FallbackToWorkingTree
)

$ErrorActionPreference = "Continue"

if ($RepositoryRoot -ne "") {
    $repoRoot = Resolve-Path $RepositoryRoot
} else {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
}

$script:errors = 0
$script:warnings = 0

function Write-CheckResult {
    param(
        [string]$Level,
        [string]$Message
    )

    $prefix = switch ($Level) {
        "ERROR" { $script:errors++; "[ERROR]" }
        "WARN"  { $script:warnings++; "[WARN] " }
        "OK"    { "[OK]   " }
        "INFO"  { "[INFO] " }
        default { "[$Level]" }
    }
    Write-Host "$prefix $Message"
}

function Invoke-GitLines {
    param(
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & git @Arguments 2>$null
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed with exit code $exitCode"
    }

    return @(
        $output |
            ForEach-Object { [string]$_ } |
            Where-Object { $_ -and $_.Trim() -ne "" }
    )
}

function Normalize-RepoPath {
    param([string]$Path)

    return $Path.Replace('\', '/').TrimStart('/')
}

function Normalize-PathList {
    param([object[]]$Paths)

    return @(
        $Paths |
            ForEach-Object {
                if ($_ -is [string]) {
                    Normalize-RepoPath $_
                }
            } |
            Where-Object { $_ -and $_.Trim() -ne "" } |
            Sort-Object -Unique
    )
}

$defaultAllowedPrefixes = @(
    ".plan16/",
    "docs/",
    "scripts/",
    "global.json",
    "NetAccel.Managed/",
    "NetAccel.Managed.Tests/"
)

$effectiveAllow = @()
$seen = @{}
foreach ($p in ($defaultAllowedPrefixes + $AllowedPathPrefixes)) {
    $norm = Normalize-RepoPath $p
    if (-not $seen.ContainsKey($norm.ToLowerInvariant())) {
        $seen[$norm.ToLowerInvariant()] = $true
        $effectiveAllow += $norm
    }
}

$avaloniaPattern = "v2rayN\.Desktop[\\/]"
$classicCriticalFiles = @(
    "v2rayN/ServiceLib/Handler/Fmt/FmtHandler.cs",
    "v2rayN/ServiceLib/Handler/Fmt/VLESSFmt.cs",
    "v2rayN/ServiceLib/Handler/Fmt/VmessFmt.cs",
    "v2rayN/ServiceLib/Handler/Fmt/Hysteria2Fmt.cs",
    "v2rayN/ServiceLib/Handler/Fmt/TrojanFmt.cs",
    "v2rayN/ServiceLib/Handler/Fmt/ShadowsocksFmt.cs",
    "v2rayN/ServiceLib/Handler/Fmt/BaseFmt.cs",
    "v2rayN/ServiceLib/Handler/ConfigHandler.cs",
    "v2rayN/ServiceLib/Handler/SubscriptionHandler.cs",
    "v2rayN/ServiceLib/Handler/CoreConfigHandler.cs",
    "v2rayN/ServiceLib/Handler/ConnectionHandler.cs",
    "v2rayN/ServiceLib/Handler/SysProxy/SysProxyHandler.cs",
    "v2rayN/ServiceLib/Handler/Builder/CoreConfigContextBuilder.cs",
    "v2rayN/ServiceLib/Handler/Builder/NodeValidator.cs",
    "v2rayN/ServiceLib/Manager/CoreManager.cs",
    "v2rayN/ServiceLib/Manager/CoreInfoManager.cs",
    "v2rayN/ServiceLib/Manager/AppManager.cs",
    "v2rayN/ServiceLib/Manager/StatisticsManager.cs",
    "v2rayN/ServiceLib/Manager/TaskManager.cs",
    "v2rayN/ServiceLib/Services/SpeedtestService.cs",
    "v2rayN/ServiceLib/Services/UpdateService.cs",
    "v2rayN/ServiceLib/Services/CoreConfig/V2ray/CoreConfigV2rayService.cs",
    "v2rayN/ServiceLib/Services/CoreConfig/Singbox/CoreConfigSingboxService.cs",
    "v2rayN/ServiceLib/ViewModels/MainWindowViewModel.cs",
    "v2rayN/ServiceLib/ViewModels/SubSettingViewModel.cs",
    "v2rayN/ServiceLib/ViewModels/RoutingSettingViewModel.cs",
    "v2rayN/ServiceLib/ViewModels/DNSSettingViewModel.cs",
    "v2rayN/ServiceLib/ViewModels/CheckUpdateViewModel.cs",
    "v2rayN/ServiceLib/ViewModels/BackupAndRestoreViewModel.cs",
    "v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs",
    "v2rayN/Views/MainWindow.xaml",
    "v2rayN/Views/MainWindow.xaml.cs",
    "v2rayN/Views/SubSettingWindow.xaml",
    "v2rayN/Views/RoutingSettingWindow.xaml",
    "v2rayN/Views/DNSSettingWindow.xaml",
    "v2rayN/Views/ProfilesView.xaml",
    "v2rayN/Views/StatusBarView.xaml"
)
$rootProtectedFiles = @("global.json", "LICENSE")

Write-Host "=== Plan 16 Change Boundary Checker ==="
Write-Host "Base ref: $BaseRef"
Write-Host "Repo root: $repoRoot"
Write-Host "Effective allowlist: $($effectiveAllow -join ', ')"
Write-Host ""

$baseRefResolved = $false
$useWorkingTreeOnly = $false

try {
    Push-Location $repoRoot
    & git rev-parse --verify "$BaseRef" 1>$null 2>$null
    $baseRefResolved = $LASTEXITCODE -eq 0
} finally {
    Pop-Location
}

if (-not $baseRefResolved) {
    if ($FallbackToWorkingTree) {
        Write-CheckResult "WARN" "BaseRef '$BaseRef' not resolvable. Falling back to working-tree-only mode."
        $useWorkingTreeOnly = $true
    } else {
        Write-CheckResult "ERROR" "BaseRef '$BaseRef' cannot be resolved in the local repository. Use -FallbackToWorkingTree to check uncommitted changes only."
        Write-Host ""
        Write-Host "=== Summary ==="
        Write-Host "Errors: 1"
        Write-Host "Warnings: 0"
        Write-Host ""
        Write-Host "RESULT: FAIL - BaseRef not resolvable."
        exit 2
    }
}

$trackedFiles = @()
$untrackedFiles = @()
$deletedRaw = @()

try {
    Push-Location $repoRoot

    if ($useWorkingTreeOnly) {
        $trackedFiles += Invoke-GitLines -Arguments @("diff", "--name-only", "HEAD") -AllowFailure
    } else {
        $trackedFiles += Invoke-GitLines -Arguments @("diff", "--name-only", "$BaseRef", "HEAD") -AllowFailure
        $deletedRaw += Invoke-GitLines -Arguments @("diff", "--name-only", "--diff-filter=D", "$BaseRef", "HEAD") -AllowFailure
    }

    $trackedFiles += Invoke-GitLines -Arguments @("diff", "--name-only", "--cached") -AllowFailure
    $trackedFiles += Invoke-GitLines -Arguments @("diff", "--name-only") -AllowFailure
    $untrackedFiles += Invoke-GitLines -Arguments @("ls-files", "--others", "--exclude-standard") -AllowFailure

    $deletedRaw += Invoke-GitLines -Arguments @("diff", "--name-only", "--diff-filter=D", "--cached") -AllowFailure
    $deletedRaw += Invoke-GitLines -Arguments @("diff", "--name-only", "--diff-filter=D") -AllowFailure
} finally {
    Pop-Location
}

$deletedFiles = Normalize-PathList $deletedRaw
$allFiles = Normalize-PathList (@($trackedFiles) + @($untrackedFiles))

if ($allFiles.Count -eq 0 -and $deletedFiles.Count -eq 0) {
    Write-CheckResult "OK" "No changed files detected. Boundary check passes."
    Write-Host ""
    Write-Host "=== Summary ==="
    Write-Host "Errors: 0"
    Write-Host "Warnings: 0"
    Write-Host ""
    Write-Host "RESULT: PASS - No changes."
    exit 0
}

Write-CheckResult "INFO" "Analyzing $($allFiles.Count) changed/new files, $($deletedFiles.Count) deletion(s)..."
Write-Host ""

Write-Host "--- Rule 1: Avalonia Project Protection (permanent deny) ---"
$avaloniaFiles = @($allFiles | Where-Object { $_ -match $avaloniaPattern })
if ($avaloniaFiles.Count -gt 0) {
    foreach ($f in $avaloniaFiles) {
        Write-CheckResult "ERROR" "Avalonia project modified (permanent deny, cannot be allowlisted): $f"
    }
} else {
    Write-CheckResult "OK" "No Avalonia project modifications detected."
}
Write-Host ""

Write-Host "--- Rule 2: Classic Entry Points Protection (permanent deny) ---"
$deletedClassic = @()
foreach ($deleted in $deletedFiles) {
    if ($classicCriticalFiles -contains $deleted) {
        $deletedClassic += $deleted
    }
}

if ($deletedClassic.Count -gt 0) {
    foreach ($f in $deletedClassic) {
        Write-CheckResult "ERROR" "Critical classic file deleted (permanent deny, cannot be allowlisted): $f"
    }
} else {
    Write-CheckResult "OK" "No critical classic files deleted."
}
Write-Host ""

Write-Host "--- Rule 3: Allowlist Enforcement ---"

function Test-IsAllowed {
    param([string]$FilePath)

    $norm = Normalize-RepoPath $FilePath
    foreach ($entry in $effectiveAllow) {
        if ($entry.EndsWith('/')) {
            if ($norm.StartsWith($entry, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        } elseif ($norm.Equals($entry, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

$violations = @()
foreach ($f in $allFiles) {
    if (-not (Test-IsAllowed $f)) {
        $violations += $f
    }
}

if ($violations.Count -gt 0) {
    foreach ($f in $violations) {
        Write-CheckResult "ERROR" "Change outside allowlist: $f"
    }
    Write-Host ""
    Write-Host "  Hint: Use -AllowedPathPrefixes to explicitly permit additional paths."
    Write-Host '  Example: -AllowedPathPrefixes "v2rayN/ServiceLib/Handler/ConfigHandler.cs"'
} else {
    Write-CheckResult "OK" "All changes are within the effective allowlist."
}
Write-Host ""

Write-Host "--- Rule 4: Root File Protection ---"
foreach ($pf in $rootProtectedFiles) {
    if ($deletedFiles -contains $pf) {
        Write-CheckResult "ERROR" "Protected root file deleted: $pf"
    } else {
        Write-CheckResult "OK" "Protected root file intact: $pf"
    }
}
Write-Host ""

Write-Host "=== Summary ==="
Write-Host "Total files analyzed: $($allFiles.Count)"
Write-Host "Effective allowlist entries: $($effectiveAllow.Count)"
Write-Host "Errors: $($script:errors)"
Write-Host "Warnings: $($script:warnings)"
Write-Host ""
Write-Host "ValidatedPrefixes: $($effectiveAllow -join '|')"

if ($script:errors -gt 0) {
    Write-Host "RESULT: FAIL - $($script:errors) boundary violation(s) detected."
    exit 1
} elseif ($script:warnings -gt 0) {
    Write-Host "RESULT: PASS with warnings."
    exit 0
} else {
    Write-Host "RESULT: PASS - All boundary checks passed."
    exit 0
}
