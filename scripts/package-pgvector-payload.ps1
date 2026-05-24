param(
    [Parameter(Mandatory = $true)]
    [string]$Rid,

    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [string]$PgMajor = "18",
    [string]$PgvectorVersion = "0.8.2"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[pgvector] $Message"
}

function Get-CommandPath {
    param([string[]]$Names)

    foreach ($name in $Names) {
        $command = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $command) {
            return $command.Source
        }
    }

    return $null
}

function Find-FirstFile {
    param(
        [string]$Root,
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $match = Get-ChildItem -Path $Root -Recurse -File -Filter $name | Select-Object -First 1
        if ($null -ne $match) {
            return $match.FullName
        }
    }

    return $null
}

function Copy-Payload {
    param(
        [string]$Root,
        [string]$DestinationRoot
    )

    $libraryPath = Find-FirstFile -Root $Root -Names @("vector.dll", "vector.so", "vector.dylib")
    if ([string]::IsNullOrWhiteSpace($libraryPath)) {
        throw "Could not locate built pgvector library under $Root"
    }

    $controlPath = Find-FirstFile -Root $Root -Names @("vector.control")
    if ([string]::IsNullOrWhiteSpace($controlPath)) {
        throw "Could not locate vector.control under $Root"
    }

    $sqlFiles = Get-ChildItem -Path $Root -Recurse -File | Where-Object { $_.Name -like 'vector--*.sql' }
    if (-not $sqlFiles) {
        throw "Could not locate pgvector SQL migration files under $Root"
    }

    $libDest = Join-Path $DestinationRoot "lib"
    $shareExtensionDest = Join-Path (Join-Path $DestinationRoot "share") "extension"
    $includeVectorDest = Join-Path (Join-Path (Join-Path (Join-Path $DestinationRoot "include") "server") "extension") "vector"

    New-Item -ItemType Directory -Path $libDest -Force | Out-Null
    New-Item -ItemType Directory -Path $shareExtensionDest -Force | Out-Null

    Copy-Item -Path $libraryPath -Destination (Join-Path $libDest ([IO.Path]::GetFileName($libraryPath))) -Force
    Copy-Item -Path $controlPath -Destination (Join-Path $shareExtensionDest "vector.control") -Force

    foreach ($sqlFile in $sqlFiles) {
        Copy-Item -Path $sqlFile.FullName -Destination (Join-Path $shareExtensionDest $sqlFile.Name) -Force
    }

    $headerSourceDir = Get-ChildItem -Path $Root -Recurse -Directory |
        Where-Object { $_.FullName -match '[\\/]extension[\\/]vector$' } |
        Select-Object -First 1

    $headerFiles = @()
    if ($null -ne $headerSourceDir) {
        $headerFiles = Get-ChildItem -Path $headerSourceDir.FullName -File | Where-Object { $_.Name -in @("halfvec.h", "sparsevec.h", "vector.h") }
    }

    if ($headerFiles) {
        New-Item -ItemType Directory -Path $includeVectorDest -Force | Out-Null
        foreach ($headerFile in $headerFiles) {
            Copy-Item -Path $headerFile.FullName -Destination (Join-Path $includeVectorDest $headerFile.Name) -Force
        }
    }
}

$resolvedSourceRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SourceRoot)
$resolvedOutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$archiveName = "pgvector-$PgvectorVersion-pg$PgMajor-$Rid.tar.gz"
$archivePath = Join-Path $resolvedOutputDir $archiveName
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("cove-pgvector-package-" + [Guid]::NewGuid().ToString("N"))
$payloadRoot = Join-Path $tempRoot "payload"

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

try {
    Copy-Payload -Root $resolvedSourceRoot -DestinationRoot $payloadRoot

    if (Test-Path $archivePath) {
        Remove-Item -Path $archivePath -Force
    }

    $tar = Get-CommandPath -Names @("tar.exe", "tar")
    if ([string]::IsNullOrWhiteSpace($tar)) {
        throw "tar is required to create pgvector payload archives."
    }

    & $tar -czf $archivePath -C $payloadRoot .
    if ($LASTEXITCODE -ne 0) {
        throw "tar failed to create $archivePath with exit code $LASTEXITCODE"
    }

    $sha256 = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$archivePath.sha256" -Value "$sha256  $archiveName" -Encoding ASCII
    Write-Info "Created $archivePath"
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}