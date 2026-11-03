param([string]$Output = 'TestResults/ReadProgressUi')
$ErrorActionPreference = 'Stop'
$folder = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Read-progress UI harness build failed.' }
$executable = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
foreach ($generation in @('Gen1','Gen2','Gen2Unknown','Gen1Chassis','Gen2Chassis')) {
foreach ($scenario in @('success','cancel','timeout','short','mismatch','preflight')) {
    if ($generation -in @('Gen2Unknown','Gen1Chassis','Gen2Chassis') -and $scenario -ne 'success') { continue }
    $case = Join-Path $folder ($generation + '-' + $scenario)
    New-Item -ItemType Directory -Path $case | Out-Null
    $report = Join-Path $case 'report.json'
    $arguments = @('--read-progress-offline','--scenario',$scenario,'--output',('"'+$case+'"'),'--report',('"'+$report+'"'))
    if ($generation.StartsWith('Gen2')) { $arguments += '--gen2' }
    if ($generation -eq 'Gen2Unknown') { $arguments += '--unknown-cal' }
    if ($generation.EndsWith('Chassis')) { $arguments += '--chassis-id' }
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(55000)) { $process.Kill(); throw "Read-progress UI timed out: $scenario" }
    if ($process.ExitCode -ne 0) { if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw }; throw "Read-progress UI failed: $scenario" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$result.passed -or $result.hardwareAccessed -or !$result.actualReadButton -or !$result.progressReset -or !$result.controlsLocked -or !$result.unverifiedTransferCapped -or !$result.lateCallbacksIgnored -or !$result.minimumWindowVerified) { throw "Unexpected read-progress UI result: $scenario" }
    $expectedLabel = if ($generation.EndsWith('Chassis')) { 'Chassis ID' } else { 'VIN' }
    if (!$result.identityDisplayVerified -or $result.vehicleIdentifierLabel -ne $expectedLabel) { throw "Incorrect vehicle identifier display: $generation" }
    if ($scenario -in @('timeout','short') -and $result.memoryRequests -ne 15) { throw 'Failed read was retried.' }
    $expectedReads = if ($generation.StartsWith('Gen2')) { 2052 } else { 530 }
    if ($scenario -in @('success','mismatch') -and $result.memoryRequests -ne $expectedReads) { throw 'Unexpected two-pass request count.' }
    if ($scenario -eq 'preflight' -and $result.memoryRequests -ne 0) { throw 'Preflight failure started a read.' }
    Write-Output "Read-progress UI passed: $generation $scenario"
}
}
Write-Output "Read-progress UI results: $folder"
