param(
    [string]$OutputPath = 'artifacts/sbom/netaccel-wpf.spdx.json'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projects = @(
    (Join-Path $repo 'NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj'),
    (Join-Path $repo 'v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj'),
    (Join-Path $repo 'v2rayN\v2rayN\v2rayN.csproj')
)
$projectReports = foreach ($project in $projects) {
    $raw = & dotnet list $project package --include-transitive --format json --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet list package failed for $project." }
    ($raw -join "`n") | ConvertFrom-Json
}

$packages = @{}
foreach ($report in $projectReports) {
    foreach ($project in $report.projects) {
        foreach ($framework in $project.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($null -eq $package) { continue }
                $key = "$($package.id)@$($package.resolvedVersion)"
                $packages[$key] = [ordered]@{
                    SPDXID = 'SPDXRef-Package-' + ($key -replace '[^A-Za-z0-9.-]', '-')
                    name = [string]$package.id
                    versionInfo = [string]$package.resolvedVersion
                    downloadLocation = 'NOASSERTION'
                    filesAnalyzed = $false
                    licenseConcluded = 'NOASSERTION'
                    licenseDeclared = 'NOASSERTION'
                    copyrightText = 'NOASSERTION'
                }
            }
        }
    }
}

$document = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = 'NetAccel-v2rayN-WPF'
    documentNamespace = 'https://netaccel.local/spdx/' + [Guid]::NewGuid().ToString('N')
    creationInfo = [ordered]@{
        created = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        creators = @('Tool: NetAccel scripts/plan16/generate-sbom.ps1')
    }
    packages = @($packages.Values | Sort-Object name, versionInfo)
}

$fullOutput = [IO.Path]::GetFullPath((Join-Path $repo $OutputPath))
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
$json = $document | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText($fullOutput, $json, [Text.UTF8Encoding]::new($false))
Write-Output $fullOutput
