param([string]$Output = 'TestResults/WorkbenchUi')
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([System.IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Workbench harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$recording = Join-Path $PSScriptRoot '../tests/fixtures/Synthetic-Speed6-log.csv'
$reports = @()
foreach ($calibration in @('L33XEH000','L35HEH000','L38KEH000','L39SED000','L39SEE000','L39SEF000','L3M6EG000','L3M7EF000','L3P4EM000','L3CGEE000')) {
    $folder = Join-Path $runRoot $calibration
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $report = Join-Path $folder 'report.json'
    $arguments = @('--workbench-offline','--calibration',$calibration,'--output',('"' + $folder + '"'),'--report',('"' + $report + '"'),'--recording',('"' + $recording + '"'))
    $process = Start-Process -FilePath $harness -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw "Workbench UI timed out: $calibration" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw "Workbench UI failed: $calibration; see $report" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$result.workflow.savedHiddenBrowserRecovered -or !$result.workflow.savedFloatingBrowserRecovered -or
        !$result.workflow.editor.mapBrowserAccessibleOnOpen -or !$result.workflow.editor.mapsButtonRestoresBrowser -or
        !$result.workflow.editor.emptyMapPrompt -or !$result.workflow.editor.lastMapClosedRecovery -or !$result.workflow.editor.minimizedBrowserRestored) { throw "Map browser access failed: $calibration" }
    if ($result.workflow.editor.scalarEditor -ne $true -or $result.workflow.editor.compactMaps -ne $true) { throw "Compact map editing failed: $calibration" }
    if ($result.passed -ne $true -or $result.synthetic -ne $true -or $result.hardwareAccessed -ne $false -or $result.workflow.navigationPreservesEdits -ne $true -or $result.workflow.minimumLayout -ne $true -or $result.workflow.editor.mapInformationShowsUnits -ne $true -or $result.workflow.editor.mapInfoDirectAccess -ne $true -or $result.workflow.editor.mapInfoF1 -ne $true -or $result.workflow.editor.axisFrameJoined -ne $true -or $result.workflow.editor.themedScrollbars -ne $true -or $result.workflow.editor.scrollbarInputAndEdges -ne $true) { throw "Unexpected workbench result: $calibration" }
    $reports += $result
}
@{ passed = $true; hardwareAccessed = $false; reports = $reports } | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Output ("Ten embedded workbench workflows passed: " + $runRoot)
