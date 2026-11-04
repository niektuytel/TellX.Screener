[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string[]] $RuntimeIdentifiers = @('win-x64', 'win-x86', 'win-arm64'),
    [switch] $SkipCanonicalCopy
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'WorkflowTracker.csproj'
$publishRoot = Join-Path $projectRoot 'publish'
$stageRoot = Join-Path $projectRoot 'obj\release-packages'
$packagesRoot = Join-Path $publishRoot 'packages'
New-Item -ItemType Directory -Path $publishRoot, $stageRoot, $packagesRoot -Force | Out-Null
$canonicalExe = Join-Path $publishRoot 'WorkflowTracker.exe'
$updateCanonical = ($RuntimeIdentifiers -contains 'win-x64') -and -not $SkipCanonicalCopy
if ($updateCanonical) {
    $running = @(Get-Process -Name WorkflowTracker -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -ieq $canonicalExe })
    if ($running.Count -gt 0) {
        throw 'Sluit de app uit publish\WorkflowTracker.exe voordat je publiceert. Er is nog niets gebouwd of vervangen.'
    }
}
$artifacts = @()
& (Join-Path $PSScriptRoot 'package-source.ps1')

# RIDs worden na elkaar gebouwd: geen gedeelde project.assets.json-race.
foreach ($runtimeId in ($RuntimeIdentifiers | Select-Object -Unique)) {
    $stageDirectory = Join-Path $stageRoot $runtimeId
    & dotnet publish $projectFile -c Release -r $runtimeId --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false -o $stageDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publiceren mislukt voor $runtimeId." }
    $stagedExe = Join-Path $stageDirectory 'WorkflowTracker.exe'
    if (-not (Test-Path -LiteralPath $stagedExe -PathType Leaf)) { throw "Executable ontbreekt voor $runtimeId." }

    # Eén huidige package per architectuur; elke ZIP bevat uitsluitend dezelfde actuele appversie.
    $archivePath = Join-Path $packagesRoot ("WorkflowTracker-{0}.zip" -f $runtimeId)
    Compress-Archive -LiteralPath $stagedExe -DestinationPath $archivePath -Force
    $artifacts += $archivePath
}

if ($updateCanonical) {
    Copy-Item -LiteralPath (Join-Path $stageRoot 'win-x64\WorkflowTracker.exe') `
        -Destination $canonicalExe -Force
}

$artifacts += Join-Path $packagesRoot 'WorkflowTracker-source.zip'
$checksums = foreach ($artifact in ($artifacts | Sort-Object)) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($artifact)
}
[IO.File]::WriteAllText((Join-Path $packagesRoot 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))

if ($updateCanonical) { Write-Output 'Actuele x64-exe: publish\WorkflowTracker.exe' }
elseif ($RuntimeIdentifiers -contains 'win-x64') { Write-Output 'X64-exe verpakt; lokale publish\WorkflowTracker.exe niet vervangen.' }
Write-Output 'Architectuurpakketten: publish\packages\WorkflowTracker-win-*.zip'
Write-Output 'Bronpakket en SHA256SUMS.txt: publish\packages'
