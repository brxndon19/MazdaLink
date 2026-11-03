param([string]$Output = 'TestResults/LayoutUi')
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Layout harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$reports = @()
foreach ($calibration in @('L33XEH000','L35HEH000','L38KEH000','L39SED000','L39SEE000','L39SEF000','L3M6EG000','L3M7EF000','L3P4EM000')) {
    $folder = Join-Path $runRoot $calibration
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $report = Join-Path $folder 'report.json'
    $process = Start-Process -FilePath $harness -ArgumentList @('--layout-offline','--calibration',$calibration,'--output',('"' + $folder + '"'),'--report',('"' + $report + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw "Layout UI timed out: $calibration" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw | Write-Output }
        throw "Layout UI failed: $calibration; see $report"
    }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($result.workflow.acceptance.previewResizeBothDirections -ne $true -or $result.workflow.acceptance.previewWidthPersisted -ne $true -or $result.workflow.acceptance.fitMapResetsPreview -ne $true) { throw "Preview resizing failed: $calibration" }
    if ($result.passed -ne $true -or $result.hardwareAccessed -ne $false -or $result.workflow.freshWorkspaceRestored -ne $true -or $result.workflow.acceptance.mafBulkEditsAndClipboard -ne $true -or $result.workflow.acceptance.axisLabelsAligned -ne $true -or $result.workflow.acceptance.axisTitlesFrozen -ne $true -or $result.workflow.acceptance.selectedHeadersHighlighted -ne $true -or $result.workflow.acceptance.surfaceFollowsDrag -ne $true -or $result.workflow.acceptance.surfaceAxesFollowCamera -ne $true) { throw "Unexpected layout result: $calibration" }
    $reports += $result
}
@{ passed = $true; hardwareAccessed = $false; reports = $reports } | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Output ("Eight layout workflows passed: " + $runRoot)
