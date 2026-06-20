param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$cores = @(
    @{
        Name = "Xray"
        Version = "v26.3.27"
        Url = "https://github.com/XTLS/Xray-core/releases/download/v26.3.27/Xray-windows-64.zip"
        Sha256 = "d004c39288ce9ada487c6f398c7c545f7d749e44bdfdd59dbc9f865afba4e1ad"
        ArchiveExecutable = "xray.exe"
        Destination = "bin\xray\xray.exe"
    },
    @{
        Name = "sing-box"
        Version = "v1.13.12"
        Url = "https://github.com/SagerNet/sing-box/releases/download/v1.13.12/sing-box-1.13.12-windows-amd64.zip"
        Sha256 = "e93fc531134eb1beb4efa3c74990a24e48456098a31c03b60d5ddf17f223cf98"
        ArchiveExecutable = "sing-box.exe"
        Destination = "bin\sing_box\sing-box.exe"
    }
)

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("netaccel-cores-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workRoot | Out-Null

try {
    foreach ($core in $cores) {
        $archive = Join-Path $workRoot ($core.Name + ".zip")
        $extract = Join-Path $workRoot ($core.Name + "-extract")
        Invoke-WebRequest -UseBasicParsing -Uri $core.Url -OutFile $archive

        $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $core.Sha256) {
            throw "$($core.Name) $($core.Version) SHA-256 mismatch. Expected $($core.Sha256), got $actualHash."
        }

        Expand-Archive -LiteralPath $archive -DestinationPath $extract
        $matches = @(Get-ChildItem -LiteralPath $extract -Recurse -File |
            Where-Object { $_.Name -ieq $core.ArchiveExecutable })
        if ($matches.Count -ne 1) {
            throw "$($core.Name) archive must contain exactly one $($core.ArchiveExecutable)."
        }

        $destination = Join-Path $publishRoot $core.Destination
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $matches[0].FullName -Destination $destination -Force
        if ((Get-Item -LiteralPath $destination).Length -le 0) {
            throw "$($core.Name) executable is empty after extraction."
        }
    }
}
finally {
    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "PINNED_CORES_INSTALL_PASS"
