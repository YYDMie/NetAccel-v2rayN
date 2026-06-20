param(
    [string]$Configuration = "Release",
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tools\NetAccel.OwnershipProbe\NetAccel.OwnershipProbe.csproj"
$probe = Join-Path $repoRoot "tools\NetAccel.OwnershipProbe\bin\$Configuration\net10.0\NetAccel.OwnershipProbe.dll"
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$root = Join-Path $env:TEMP ("netaccel-mode-ownership-" + [guid]::NewGuid().ToString("N"))
$snapshot = Join-Path $root "managed-connection-owner.json"
$mutexName = "NetAccel.Plan16.ModeOwnership." + [guid]::NewGuid().ToString("N")
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Join-ProcessArguments {
    param([string[]]$Values)

    return ($Values | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join " "
}

function Invoke-Probe {
    param([string[]]$ProbeArgs)

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $dotnet
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Arguments = Join-ProcessArguments -Values (@($probe) + $ProbeArgs)

    $process = [System.Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $result = @{
        ExitCode = $process.ExitCode
        Output = (($stdout, $stderr) | Where-Object { $_ } | ForEach-Object { $_.Trim() }) -join [Environment]::NewLine
    }
    $process.Dispose()
    return $result
}

function Start-Holder {
    param(
        [string]$Owner,
        [string]$ReadyPath,
        [string]$ReleasePath
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $dotnet
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Arguments = Join-ProcessArguments -Values @(
        $probe,
        "hold",
        $Owner,
        $snapshot,
        $mutexName,
        $ReadyPath,
        $ReleasePath,
        $TimeoutSeconds.ToString())

    $process = [System.Diagnostics.Process]::Start($start)
    $processes.Add($process)
    return $process
}

function Wait-Ready {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ReadyPath
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (!(Test-Path -LiteralPath $ReadyPath)) {
        if ($Process.HasExited) {
            $stderr = $Process.StandardError.ReadToEnd()
            $stdout = $Process.StandardOutput.ReadToEnd()
            throw "Holder exited before ready: exit=$($Process.ExitCode) stdout=$stdout stderr=$stderr"
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "Holder readiness timed out."
        }
        Start-Sleep -Milliseconds 50
    }
}

function Stop-HolderGracefully {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ReleasePath
    )

    New-Item -ItemType File -Path $ReleasePath -Force | Out-Null
    if (!$Process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "Holder release timed out."
    }
    if ($Process.ExitCode -ne 0) {
        $stderr = $Process.StandardError.ReadToEnd()
        $stdout = $Process.StandardOutput.ReadToEnd()
        throw "Holder release failed: exit=$($Process.ExitCode) stdout=$stdout stderr=$stderr"
    }
}

function Assert-Blocked {
    param([string]$Owner)

    $result = Invoke-Probe -ProbeArgs @("try", $Owner, $snapshot, $mutexName)
    if ($result.ExitCode -ne 2 -or $result.Output -notmatch "BLOCKED") {
        throw "$Owner was not blocked by the competing owner: exit=$($result.ExitCode) output=$($result.Output)"
    }
}

try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    & $dotnet build $project -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $probe)) {
        throw "Ownership probe build failed."
    }

    $switch = Invoke-Probe -ProbeArgs @("switch", $snapshot, $mutexName, "10")
    if ($switch.ExitCode -ne 0 -or $switch.Output -notmatch "SWITCHED iterations=10") {
        throw "Same-process Managed/Classic switching failed: exit=$($switch.ExitCode) output=$($switch.Output)"
    }

    $managedReady = Join-Path $root "managed.ready"
    $managedRelease = Join-Path $root "managed.release"
    $managed = Start-Holder -Owner "Managed" -ReadyPath $managedReady -ReleasePath $managedRelease
    Wait-Ready -Process $managed -ReadyPath $managedReady
    Assert-Blocked -Owner "Classic"
    Stop-HolderGracefully -Process $managed -ReleasePath $managedRelease

    $classicReady = Join-Path $root "classic.ready"
    $classicRelease = Join-Path $root "classic.release"
    $classic = Start-Holder -Owner "Classic" -ReadyPath $classicReady -ReleasePath $classicRelease
    Wait-Ready -Process $classic -ReadyPath $classicReady
    Assert-Blocked -Owner "Managed"
    Stop-HolderGracefully -Process $classic -ReleasePath $classicRelease

    $crashReady = Join-Path $root "crash.ready"
    $crashRelease = Join-Path $root "crash.release"
    $crashed = Start-Holder -Owner "Managed" -ReadyPath $crashReady -ReleasePath $crashRelease
    Wait-Ready -Process $crashed -ReadyPath $crashReady
    $crashed.Kill()
    $crashed.WaitForExit()

    $recovery = Invoke-Probe -ProbeArgs @("try", "Classic", $snapshot, $mutexName)
    if ($recovery.ExitCode -ne 0 -or $recovery.Output -notmatch "ACQUIRED owner=Classic") {
        throw "Crash recovery failed: exit=$($recovery.ExitCode) output=$($recovery.Output)"
    }
    if (Test-Path -LiteralPath $snapshot) {
        throw "Ownership snapshot remained after final release."
    }

    Write-Output "MODE_OWNERSHIP_INTEGRATION=PASS"
    Write-Output "SAME_PROCESS_SWITCHES=10"
    Write-Output "CROSS_PROCESS_BLOCKS=2"
    Write-Output "CRASH_RECOVERIES=1"
} finally {
    foreach ($process in $processes) {
        if (!$process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
        }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
