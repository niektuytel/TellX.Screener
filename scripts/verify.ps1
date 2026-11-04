[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet build (Join-Path $projectRoot 'WorkflowTracker.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build mislukt.' }
& dotnet run --project (Join-Path $projectRoot 'Tests\WorkflowTracker.Tests.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests mislukt.' }
Write-Output 'Build en tests geslaagd. Er zijn geen echte screenshots of netwerkrequests verstuurd.'
