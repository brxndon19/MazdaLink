param([string]$Output = 'TestResults/MapWindowsUi')
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Map windows harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$reports = @()
foreach ($calibration in @('L33XEH000','L35HEH000','L38KEH000','L39SED000','L39SEE000','L39SEF000','L3M6EG000','L3M7EF000','L3P4EM000','L3CGEE000')) {
    $folder = Join-Path $runRoot $calibration
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $report = Join-Path $folder 'report.json'
    $stderr = Join-Path $folder 'stderr.log'
    $process = Start-Process -FilePath $harness -ArgumentList @('--map-windows-offline','--calibration',$calibration,'--output',('"' + $folder + '"'),'--report',('"' + $report + '"')) -WindowStyle Hidden -RedirectStandardError $stderr -PassThru
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Map window UI timed out: $calibration" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        Write-Output ("Map window harness exit code: " + $process.ExitCode)
        if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Tail 80 | Write-Output }
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw | Write-Output }
        throw "Map window UI failed: $calibration; see $report"
    }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$result.passed -or !$result.workflow.passed -or !$result.synthetic -or $result.hardwareAccessed -or $result.workflow.cases.Count -lt 57) { throw "Unexpected map windows result: $calibration" }
    foreach ($required in @('Five scalar panes fit together at 1280x850', 'Five scalar panes fit together at 1024x768', 'Value cards wrap at roughly half the old pane width at 1024x768', 'Value cards reflow to available space when the browser is shown', 'Apply and Cancel appear only while a value is being edited', 'Reopening the calibration restores compact card sizes and maps', 'Compact scalar retains full help and units through Map info', 'Compact invalid input still blocks export without changing bytes', 'Compact state undo restores exact original bytes')) {
        if ($result.workflow.cases -notcontains $required) { throw "Missing compact pane regression: $calibration; $required" }
    }
    foreach ($required in @('Bottom map widens across empty space without moving either upper map', 'Manual resizing allows overlap without moving the covered map', 'Choosing a covered map brings it to the front without changing its bounds', 'Moving one pane allows overlap and leaves all other panes in place', 'The visible corner resize handle receives pointer input', 'Opening another map preserves independently sized panes', 'Docking back restores only that pane''s bounds', 'Smaller workspaces scroll without moving independently sized maps', 'Reopening a ROM restores each pane''s independent position and size', 'Reopening a ROM restores overlapping map stacking order', 'Escape cancels a resize even when keyboard focus belongs to another map', 'Resizing preserves invalid pending input and still blocks ROM export', 'Mixed scalar and table panes resize into empty space without shifting other rows')) {
        if ($result.workflow.cases -notcontains $required) { throw "Missing independent resize regression: $calibration; $required" }
    }
    foreach ($required in @(
        'Blank map header space receives drag input while pinned',
        'The pin button still receives pointer input',
        'Browsing replaces the reusable map pane',
        'Replacing the preview keeps pane sizes and pinned positions',
        'Invalid scalar input blocks preview replacement without losing the draft',
        'Valid input is applied before preview replacement',
        'Tabs share applied edits and undo history',
        'Invalid pending input keeps the current workspace selected',
        'Invalid grid input also blocks a workspace switch',
        'Returning to a tab restores its pins, selection and detached window bounds',
        'Reopening the ROM restores workspace names, selected tab and preview',
        'Deleting a tab removes only its layout and retains applied edits',
        'Old fuel feedback layouts reopen the same ROM addresses under the new names'
    )) {
        if ($result.workflow.cases -notcontains $required) { throw "Missing workspace tab regression: $calibration; $required" }
    }
    $reports += $result
}
@{ passed = $true; hardwareAccessed = $false; reports = $reports } | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Output ('Ten map window workflows passed: ' + $runRoot)
