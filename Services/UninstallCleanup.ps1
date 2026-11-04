# Ingebouwde, vaste cleanupcode. De webhook kan uitsluitend de actie activeren.
# Paden komen van de app via JSON-data, nooit uit de webhookrespons.
$ErrorActionPreference = 'Stop'
$cleanupCommitted = $false
$readySignal = $null
$proceedSignal = $null
$successSignal = $null

function Get-FullPath([string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value) -or -not [IO.Path]::IsPathRooted($Value)) {
        throw 'Een absoluut pad is vereist.'
    }
    return [IO.Path]::GetFullPath($Value).TrimEnd('\')
}

function Assert-NoReparseAncestors([string] $Value) {
    $checkedPath = Get-FullPath $Value
    while (-not [string]::IsNullOrWhiteSpace($checkedPath)) {
        if (Test-Path -LiteralPath $checkedPath) {
            $attributes = [IO.File]::GetAttributes($checkedPath)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Opruimen via een symlink of junction wordt geweigerd.'
            }
        }
        $parentPath = [IO.Path]::GetDirectoryName($checkedPath)
        if ($parentPath -eq $checkedPath) { break }
        $checkedPath = $parentPath
    }
}

function Remove-OwnedFile([string] $Value, [string] $ExpectedRoot) {
    $filePath = Get-FullPath $Value
    $rootPath = Get-FullPath $ExpectedRoot
    if (-not $filePath.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Bestand valt buiten de bedoelde appmap.'
    }
    Assert-NoReparseAncestors $filePath
    if (Test-Path -LiteralPath $filePath -PathType Leaf) {
        Remove-Item -LiteralPath $filePath -Force -ErrorAction Stop
    }
}

try {
    $plan = $env:WORKFLOWTRACKER_UNINSTALL_PLAN | ConvertFrom-Json
    if ($null -eq $plan) { throw 'Verwijderplan ontbreekt.' }
    $targetExePath = Get-FullPath $plan.TargetExe
    $dataRootPath = Get-FullPath $plan.DataRoot
    $expectedDataRoot = Get-FullPath ([IO.Path]::Combine($env:LOCALAPPDATA, 'WorkflowTracker'))
    if (-not [string]::Equals($dataRootPath, $expectedDataRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Onverwachte map voor appgegevens.'
    }
    if ([IO.Path]::GetExtension($targetExePath) -ine '.exe' -or $plan.ExeSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'Ongeldige executable-identiteit.'
    }
    Assert-NoReparseAncestors $targetExePath
    Assert-NoReparseAncestors $dataRootPath
    $targetProcess = [Diagnostics.Process]::GetProcessById([int] $plan.TargetPid)
    if (-not [string]::Equals((Get-FullPath $targetProcess.MainModule.FileName), $targetExePath, [StringComparison]::OrdinalIgnoreCase) -or
        $targetProcess.StartTime.ToUniversalTime().Ticks -ne [long] $plan.TargetStartTicks) {
        throw 'Procesidentiteit komt niet overeen.'
    }
    if ((Get-FileHash -LiteralPath $targetExePath -Algorithm SHA256).Hash -ine $plan.ExeSha256) {
        throw 'Executable-inhoud komt niet overeen.'
    }
    foreach ($eventName in @($plan.ReadyEvent, $plan.ProceedEvent, $plan.ResultEvent)) {
        if ($eventName -notmatch '^Local\\WorkflowTracker\.Uninstall\.[a-f0-9]{32}\.(Ready|Proceed|Success)$') {
            throw 'Ongeldig synchronisatie-event.'
        }
    }
    $readySignal = [Threading.EventWaitHandle]::OpenExisting($plan.ReadyEvent)
    $proceedSignal = [Threading.EventWaitHandle]::OpenExisting($plan.ProceedEvent)
    $successSignal = [Threading.EventWaitHandle]::OpenExisting($plan.ResultEvent)
    [void] $readySignal.Set()
    if (-not $proceedSignal.WaitOne(30000)) { exit 2 }
    $cleanupCommitted = $true
    if (-not $targetProcess.WaitForExit(60000)) { throw 'De app is niet op tijd afgesloten.' }

    # Hercontrole na afsluiten: verwijder geen inmiddels vervangen bestand.
    Assert-NoReparseAncestors $targetExePath
    if (Test-Path -LiteralPath $targetExePath -PathType Leaf) {
        if ((Get-FileHash -LiteralPath $targetExePath -Algorithm SHA256).Hash -ine $plan.ExeSha256) {
            throw 'De executable is inmiddels vervangen; verwijderen is gestopt.'
        }
        $removed = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            try {
                Remove-OwnedFile $targetExePath ([IO.Path]::GetDirectoryName($targetExePath))
                $removed = $true
                break
            } catch [IO.IOException] { Start-Sleep -Milliseconds 500 }
        }
        if (-not $removed) { throw 'De executable kon niet worden verwijderd.' }
    }

    # Geen recursieve verwijdering: uitsluitend vaste eigen bestandsnamen en logpatroon.
    Remove-OwnedFile ([IO.Path]::Combine($dataRootPath, 'settings.dat')) $dataRootPath
    Remove-OwnedFile ([IO.Path]::Combine($dataRootPath, 'settings.dat.tmp')) $dataRootPath
    $logRootPath = [IO.Path]::Combine($dataRootPath, 'logs')
    Assert-NoReparseAncestors $logRootPath
    if (Test-Path -LiteralPath $logRootPath -PathType Container) {
        foreach ($logFile in Get-ChildItem -LiteralPath $logRootPath -Filter 'tracker-*.log' -File -Force) {
            Remove-OwnedFile $logFile.FullName $logRootPath
        }
        if (@(Get-ChildItem -LiteralPath $logRootPath -Force).Count -eq 0) {
            Remove-Item -LiteralPath $logRootPath -Force -ErrorAction Stop
        }
    }
    if ((Test-Path -LiteralPath $dataRootPath -PathType Container) -and
        @(Get-ChildItem -LiteralPath $dataRootPath -Force).Count -eq 0) {
        Remove-Item -LiteralPath $dataRootPath -Force -ErrorAction Stop
    }
    [void] $successSignal.Set()
    exit 0
} catch {
    if (-not $cleanupCommitted) {
        [Console]::Error.WriteLine('Cleanup preflight failed: ' + $_.Exception.Message)
    }
    # Na afsluiten kan de app zelf geen foutmelding meer tonen.
    if ($cleanupCommitted) {
        $failureLog = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'WorkflowTracker-uninstall-error.log')
        try { [IO.File]::WriteAllText($failureLog, [string] $_) } catch { }
        try {
            Add-Type -AssemblyName PresentationFramework
            [void] [Windows.MessageBox]::Show(
                ('Automatisch verwijderen is niet volledig gelukt. De opname is gestopt. ' +
                 'Verwijder de resterende WorkflowTracker-bestanden handmatig. Details: ' + $failureLog),
                'Workflow Tracker')
        } catch { }
    }
    exit 1
} finally {
    if ($null -ne $readySignal) { $readySignal.Dispose() }
    if ($null -ne $proceedSignal) { $proceedSignal.Dispose() }
    if ($null -ne $successSignal) { $successSignal.Dispose() }
}
