param(
    [Parameter(Mandatory = $true)]
    [string]$Rid,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [string]$PgMajor = "18",
    [string]$PgvectorVersion = "0.8.2",
    [string]$PayloadRepository = "yourcove/cove",
    [string]$PayloadTag = "",
    [string]$PayloadManifestUrl = "",
    [string]$PayloadManifestPath = ""
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

function Download-File {
    param(
        [string]$Url,
        [string]$Destination
    )

    Write-Info "Downloading $Url"
    $curl = Get-CommandPath -Names @("curl.exe", "curl")
    if (-not [string]::IsNullOrWhiteSpace($curl)) {
        & $curl -L --fail --silent --show-error -o $Destination $Url
        if ($LASTEXITCODE -ne 0) {
            throw "curl failed to download $Url with exit code $LASTEXITCODE"
        }
        return
    }

    Invoke-WebRequest -Uri $Url -OutFile $Destination
}

function Expand-TarArchive {
    param(
        [string]$Archive,
        [string]$Destination
    )

    $tar = Get-CommandPath -Names @("tar.exe", "tar")
    if ([string]::IsNullOrWhiteSpace($tar)) {
        throw "tar is required to extract bundled pgvector payloads."
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    & $tar -xzf $Archive -C $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "tar failed to extract $Archive with exit code $LASTEXITCODE"
    }
}

function Get-ManifestUrl {
    if (-not [string]::IsNullOrWhiteSpace($PayloadManifestUrl)) {
        return $PayloadManifestUrl
    }

    $tag = $PayloadTag
    if ([string]::IsNullOrWhiteSpace($tag)) {
        $tag = "pgvector-payload-v$PgvectorVersion-pg$PgMajor"
    }

    return "https://github.com/$PayloadRepository/releases/download/$tag/pgvector-payload-manifest.json"
}

function Read-PayloadManifest {
    param([string]$TempRoot)

    if (-not [string]::IsNullOrWhiteSpace($PayloadManifestPath)) {
        $resolvedPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PayloadManifestPath)
        Write-Info "Reading payload manifest $resolvedPath"
        return Get-Content -Path $resolvedPath -Raw | ConvertFrom-Json
    }

    $manifestPath = Join-Path $TempRoot "pgvector-payload-manifest.json"
    Download-File -Url (Get-ManifestUrl) -Destination $manifestPath
    return Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
}

function Get-ManifestAsset {
    param($Manifest)

    if ($Manifest.postgresMajor -ne $PgMajor) {
        throw "The pgvector payload manifest targets PostgreSQL '$($Manifest.postgresMajor)', but this build requested PostgreSQL '$PgMajor'."
    }

    if ($Manifest.pgvectorVersion -ne $PgvectorVersion) {
        throw "The pgvector payload manifest targets pgvector '$($Manifest.pgvectorVersion)', but this build requested pgvector '$PgvectorVersion'."
    }

    $assetProperty = $Manifest.assets.PSObject.Properties[$Rid]
    if ($null -eq $assetProperty) {
        throw "The pgvector payload manifest does not contain an asset for runtime identifier '$Rid'."
    }

    $asset = $assetProperty.Value
    foreach ($propertyName in @("fileName", "url", "sha256")) {
        if ([string]::IsNullOrWhiteSpace($asset.$propertyName)) {
            throw "The pgvector payload manifest asset for '$Rid' is missing '$propertyName'."
        }
    }

    return $asset
}

function Get-Sha256Hex {
    param([string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hashBytes = $sha256.ComputeHash($stream)
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    return ([System.BitConverter]::ToString($hashBytes) -replace '-', '').ToLowerInvariant()
}

function Assert-FileHash {
    param(
        [string]$Path,
        [string]$ExpectedSha256
    )

    $actual = Get-Sha256Hex -Path $Path
    $expected = $ExpectedSha256.Trim().ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "SHA-256 mismatch for $Path. Expected $expected, got $actual."
    }
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

function Test-PayloadRoot {
    param([string]$Root)

    $libraryPath = Find-FirstFile -Root $Root -Names @("vector.dll", "vector.so", "vector.dylib")
    if ([string]::IsNullOrWhiteSpace($libraryPath)) {
        throw "The bundled pgvector payload does not contain vector.dll, vector.so, or vector.dylib."
    }

    $controlPath = Find-FirstFile -Root $Root -Names @("vector.control")
    if ([string]::IsNullOrWhiteSpace($controlPath)) {
        throw "The bundled pgvector payload does not contain vector.control."
    }

    $sqlFiles = Get-ChildItem -Path $Root -Recurse -File | Where-Object { $_.Name -like 'vector--*.sql' }
    if (-not $sqlFiles) {
        throw "The bundled pgvector payload does not contain vector--*.sql migration files."
    }
}

$cleanOutputDir = $OutputDir.Trim('"')
$resolvedOutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($cleanOutputDir)
$destinationRoot = Join-Path $resolvedOutputDir "runtimes/$Rid/native/postgresql/pg$PgMajor/pgvector"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("cove-pgvector-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    Write-Info "Staging bundled pgvector $PgvectorVersion payload for $Rid into $destinationRoot"
    $manifest = Read-PayloadManifest -TempRoot $tempRoot
    $asset = Get-ManifestAsset -Manifest $manifest

    $archivePath = Join-Path $tempRoot $asset.fileName
    Download-File -Url $asset.url -Destination $archivePath
    Assert-FileHash -Path $archivePath -ExpectedSha256 $asset.sha256

    if (Test-Path $destinationRoot) {
        Remove-Item -Path $destinationRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
    Expand-TarArchive -Archive $archivePath -Destination $destinationRoot
    Test-PayloadRoot -Root $destinationRoot
    Write-Info "Bundled pgvector payload ready at $destinationRoot"
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}