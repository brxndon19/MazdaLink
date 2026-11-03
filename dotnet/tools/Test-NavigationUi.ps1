param([string]$Output = 'TestResults/NavigationUi')
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Navigation harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$reports = @()
foreach ($calibration in @('L33XEH000','L35HEH000','L38KEH000','L39SED000','L39SEE000','L39SEF000','L3M6EG000','L3M7EF000','L3P4EM000','L3CGEE000')) {
    $folder = Join-Path $runRoot $calibration
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $report = Join-Path $folder 'report.json'
    $process = Start-Process -FilePath $harness -ArgumentList @('--navigation-offline','--calibration',$calibration,'--output',('"' + $folder + '"'),'--report',('"' + $report + '"')) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(45000)) { $process.Kill(); throw "Navigation UI timed out: $calibration" }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw "Navigation UI failed: $calibration; see $report" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$result.passed -or $result.hardwareAccessed -or !$result.synthetic -or !$result.workflow.editorCommands -or !$result.workflow.navigationPreservesEdits -or !$result.workflow.minimumLayout -or !$result.workflow.menuChecks.permanentMenus -or !$result.workflow.menuChecks.labeledNavigation -or !$result.workflow.menuChecks.navigationRemembered) { throw "Unexpected navigation result: $calibration" }
    $reports += $result
}
@{ passed = $true; hardwareAccessed = $false; reports = $reports } | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Output ("Ten application-navigation workflows passed: " + $runRoot)
