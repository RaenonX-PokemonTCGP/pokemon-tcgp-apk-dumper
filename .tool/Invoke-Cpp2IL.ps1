[CmdletBinding()]
param(
    [string] $BinaryPath,
    [string] $MetadataPath,
    [string] $OutputPath,
    [string] $ConfigPath,
    [string] $UnityVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($BinaryPath)) {
    $BinaryPath = Join-Path $repositoryRoot "libil2cpp.so"
}
if ([string]::IsNullOrWhiteSpace($MetadataPath)) {
    $MetadataPath = Join-Path $repositoryRoot "global-metadata.dat"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot ".cppDump"
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $repositoryRoot "current-takasho.json"
}

$cpp2IlPath = Join-Path $scriptRoot "Cpp2IL-2022.1.0-pre-release.21.exe"
foreach ($path in @($cpp2IlPath, $BinaryPath, $MetadataPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Cpp2IL input does not exist: $path"
    }
}

if ([string]::IsNullOrWhiteSpace($UnityVersion)) {
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
        throw "Takasho configuration does not exist: $ConfigPath"
    }
    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $UnityVersion = [string] $config.unityVersion
}
if ([string]::IsNullOrWhiteSpace($UnityVersion)) {
    throw "Unity version is missing."
}

function Get-MetadataMagic([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $header = New-Object byte[] 4
        if ($stream.Read($header, 0, $header.Length) -ne $header.Length) {
            throw "IL2CPP metadata is shorter than its four-byte header."
        }
        return [BitConverter]::ToUInt32($header, 0)
    } finally {
        $stream.Dispose()
    }
}

$metadataToUse = $MetadataPath
$decryptedMetadataPath = $null
try {
    if ((Get-MetadataMagic -Path $MetadataPath) -ne [uint32] 4205910959) {
        $decryptorRoot = Join-Path $scriptRoot "MetadataDecryptor"
        $decryptorSources = @(
            Get-ChildItem -LiteralPath $decryptorRoot -Filter "*.cs" |
                ForEach-Object FullName
        )
        if ($decryptorSources.Count -eq 0) {
            throw "Metadata decryptor source files are missing."
        }
        if ($null -eq ("PokemonTcgPocket.Metadata.MetadataDecryptor" -as [type])) {
            Add-Type -Path $decryptorSources
        }
        $decryptedMetadataPath = Join-Path ([IO.Path]::GetTempPath()) (
            "ptcgp-global-metadata.$([guid]::NewGuid().ToString('N')).dat"
        )
        $decryptedSize = (
            [PokemonTcgPocket.Metadata.MetadataDecryptor]::Decrypt(
                $BinaryPath,
                $MetadataPath,
                $decryptedMetadataPath
            )
        )
        Write-Host "Decrypted IL2CPP metadata: $decryptedSize bytes"
        $metadataToUse = $decryptedMetadataPath
    }

    & $cpp2IlPath `
        --force-binary-path $BinaryPath `
        --force-metadata-path $metadataToUse `
        --force-unity-version $UnityVersion `
        --output-as diffable-cs `
        --output-to $OutputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Cpp2IL failed with exit code $LASTEXITCODE."
    }

    $diffableCsPath = Join-Path $OutputPath "DiffableCs"
    $firstOutput = Get-ChildItem -LiteralPath $diffableCsPath -Recurse -File |
        Select-Object -First 1
    if ($null -eq $firstOutput) {
        throw "Cpp2IL completed without producing Diffable C# files."
    }
} finally {
    if (
        $null -ne $decryptedMetadataPath -and
        (Test-Path -LiteralPath $decryptedMetadataPath)
    ) {
        Remove-Item -LiteralPath $decryptedMetadataPath -Force
    }
}
