param([Parameter(Mandatory=$true)][string]$Output, [Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$folder = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$run = Join-Path $folder ('run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$report = Join-Path $folder 'report.json'
$process = Start-Process -FilePath (Resolve-Path -LiteralPath $Executable).Path -ArgumentList @(
    '--gen1-flash-smoke', '--output', ('"' + $run + '"'), '--smoke-report', ('"' + $report + '"')
) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Gen 1 flashing desktop test timed out.' }
if ($process.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw }
    throw 'Gen 1 flashing desktop test failed.'
}
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (!$result.passed -or $result.hardwareAccessed -ne $false -or !$result.explicitSetupConfirmation -or
    !$result.changedInputDisarms -or !$result.rejectedInputRecovers -or !$result.offlineTransferVerified -or
    !$result.offlineStockRecoveryVerified -or !$result.mongooseRecoverySelectionVerified -or !$result.progressAndLockingVerified -or
    !$result.movedTargetAndAutomaticBackup -or !$result.recoveryStateGate -or !$result.skipReadbackVerified) {
    throw 'Unexpected Gen 1 flashing desktop evidence.'
}
Write-Output 'The standalone Gen 1 flashing and recovery desktop workflows passed with a synthetic ECU.'
