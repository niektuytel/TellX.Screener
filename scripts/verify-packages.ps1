[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packagesRoot = Join-Path $projectRoot 'publish\packages'
$expected = @('WorkflowTracker-win-x64.zip', 'WorkflowTracker-win-x86.zip',
    'WorkflowTracker-win-arm64.zip', 'WorkflowTracker-source.zip')
$lines = @(Get-Content -LiteralPath (Join-Path $packagesRoot 'SHA256SUMS.txt'))
if ($lines.Count -ne $expected.Count) { throw 'Vier releasechecksums verwacht.' }
$seen = @{}
foreach ($line in $lines) {
    if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9.-]+)$') { throw 'Ongeldige checksumregel.' }
    $hash = $Matches[1]
    $name = $Matches[2]
    if ($name -notin $expected -or $seen.ContainsKey($name)) { throw 'Onbekend of dubbel releasebestand.' }
    $seen[$name] = $true
    if ((Get-FileHash -LiteralPath (Join-Path $packagesRoot $name) -Algorithm SHA256).Hash -ine $hash) {
        throw "Checksum klopt niet: $name"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($name in $expected[0..2]) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $packagesRoot $name))
    try {
        if ($archive.Entries.Count -ne 1 -or $archive.Entries[0].FullName -cne 'WorkflowTracker.exe') {
            throw "Precies een WorkflowTracker.exe verwacht: $name"
        }
    }
    finally { $archive.Dispose() }
}
$source = [IO.Compression.ZipFile]::OpenRead((Join-Path $packagesRoot 'WorkflowTracker-source.zip'))
try {
    foreach ($required in @('WorkflowTracker.csproj', '.github/workflows/build.yml',
        '.agents/skills/screener-workflow-tracker/SKILL.md')) {
        if ($null -eq $source.GetEntry($required)) { throw "Bronbestand ontbreekt: $required" }
    }
}
finally { $source.Dispose() }
Write-Output 'PASS: releasechecksums, executable-ZIPs, workflow en skill gecontroleerd.'
