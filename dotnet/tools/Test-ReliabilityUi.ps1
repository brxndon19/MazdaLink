param([string]$Output = 'TestResults/ReliabilityUi')
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([System.IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Reliability harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$recording = Join-Path $PSScriptRoot '../tests/fixtures/Synthetic-Speed6-log.csv'
$vectors = Join-Path $PSScriptRoot '../../definitions/logging'
$reports = @()
foreach ($calibration in @('L33XEH000','L35HEH000','L38KEH000','L39SED000','L39SEE000','L39SEF000','L3M6EG000','L3M7EF000','L3P4EM000','L3CGEE000')) {
    $folder = Join-Path $runRoot $calibration
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $report = Join-Path $folder 'report.json'
    $arguments = @('--reliability-offline','--calibration',$calibration,'--output',('"' + $folder + '"'),'--report',('"' + $report + '"'),'--recording',('"' + $recording + '"'),'--vectors-root',('"' + $vectors + '"'))
    $process = Start-Process -FilePath $harness -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw "Reliability UI timed out: $calibration" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw | Write-Output }
        throw "Reliability UI failed: $calibration; see $report"
    }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$result.passed -or !$result.workflow.passed -or !$result.synthetic -or $result.hardwareAccessed -or $result.workflow.cases.Count -lt 20) { throw "Unexpected reliability result: $calibration" }
    foreach ($required in @('Empty editor: unsupported-program reports one recoverable error', 'Edited multi-map workspace: unsupported-program reports one recoverable error', 'Rejected unsupported-program preserves unsaved bytes and undo', 'Rejected unsupported-program preserves docked and detached maps, selections and zoom', 'Supported calibration-only modified ROM opens after unsupported rejection', 'Unexpected programming errors still reach fatal crash reporting')) {
        if ($result.workflow.cases -notcontains $required) { throw "Missing ROM rejection regression: $calibration; $required" }
    }
    $reports += $result
}
@{ passed = $true; hardwareAccessed = $false; reports = $reports } | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Output ('Ten reliability workflows passed: ' + $runRoot)
