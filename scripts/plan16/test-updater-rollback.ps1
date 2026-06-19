$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$updater = Join-Path $repo 'NetAccel.Updater\bin\Release\net10.0-windows\NetAccel.Updater.dll'
if (-not (Test-Path -LiteralPath $updater)) { throw 'Build NetAccel.Updater in Release first.' }

$root = Join-Path ([IO.Path]::GetTempPath()) ('netaccel-updater-test-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'target'
$backup = Join-Path $root 'backup'
$marker = Join-Path $root 'health.marker'
try {
    New-Item -ItemType Directory -Force $source,$target | Out-Null
    Copy-Item -LiteralPath "$env:SystemRoot\System32\where.exe" -Destination (Join-Path $source 'NetAccel.exe')
    Copy-Item -LiteralPath "$env:SystemRoot\System32\where.exe" -Destination (Join-Path $target 'NetAccel.exe')
    [IO.File]::WriteAllText((Join-Path $source 'version.txt'), 'new')
    [IO.File]::WriteAllText((Join-Path $source 'new-file.txt'), 'new')
    [IO.File]::WriteAllText((Join-Path $target 'version.txt'), 'old')

    $plan = [ordered]@{
        ParentProcessId = 2147483647
        SourceDirectory = $source
        TargetDirectory = $target
        BackupDirectory = $backup
        ExecutableName = 'NetAccel.exe'
        HealthMarkerPath = $marker
        HealthTimeoutSeconds = 5
    }
    $planPath = Join-Path $root 'plan.json'
    [IO.File]::WriteAllText($planPath, ($plan | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

    & dotnet $updater --plan $planPath
    if ($LASTEXITCODE -ne 1) { throw "Expected updater rollback exit 1, got $LASTEXITCODE." }
    if ((Get-Content -Raw (Join-Path $target 'version.txt')) -ne 'old') { throw 'Old file was not restored.' }
    if (Test-Path (Join-Path $target 'new-file.txt')) { throw 'New file was not removed during rollback.' }
    Write-Output 'UPDATER_ROLLBACK_PASS'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
