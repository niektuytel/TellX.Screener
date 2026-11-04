[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packagesRoot = Join-Path $projectRoot 'publish\packages'
$manifestPath = Join-Path $PSScriptRoot 'source-files.txt'
$files = @{}

foreach ($pattern in (Get-Content -LiteralPath $manifestPath -Encoding UTF8)) {
    if ([string]::IsNullOrWhiteSpace($pattern) -or $pattern.StartsWith('#')) { continue }
    $matches = @(Get-ChildItem -Path (Join-Path $projectRoot $pattern) -File -Force -ErrorAction Stop)
    if ($matches.Count -eq 0) { throw "Geen bronbestanden voor patroon: $pattern" }
    foreach ($file in $matches) {
        if (-not $file.FullName.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Bronbestand ligt buiten de projectmap.'
        }
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Bronpakket accepteert geen symlinks of junctions.'
        }
        for ($item = $file.Directory; $item.FullName -ne $projectRoot; $item = $item.Parent) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Bronpakket accepteert geen symlinks of junctions.'
            }
        }
        $relativePath = $file.FullName.Substring($projectRoot.Length + 1).Replace('\', '/')
        $files[$relativePath] = $file.FullName
    }
}

# Shipped defaults must never contain the user's credentials.
$defaults = Get-Content -LiteralPath (Join-Path $projectRoot 'workflowtracker.defaults.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not [string]::IsNullOrEmpty($defaults.VisionApiKey) -or
    -not [string]::IsNullOrEmpty($defaults.WebhookBearerToken)) {
    throw 'Verwijder credentials uit de ingebouwde defaults voordat je publiceert.'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $packagesRoot -Force | Out-Null
$destination = Join-Path $packagesRoot 'WorkflowTracker-source.zip'
$temporary = Join-Path $packagesRoot ([Guid]::NewGuid().ToString('N') + '.zip.tmp')
$stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($relativePath in ($files.Keys | Sort-Object)) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $files[$relativePath],
                $relativePath, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $archive.Dispose() }
}
finally { $stream.Dispose() }
Move-Item -LiteralPath $temporary -Destination $destination -Force
Write-Output "Bronpakket: $destination ($($files.Count) bestanden)"
