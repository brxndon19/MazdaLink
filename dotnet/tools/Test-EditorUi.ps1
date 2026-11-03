param([string]$Output = 'TestResults/EditorUi')
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path ([System.IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Editor UI harness build failed.' }
$harness = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$reports = @()
foreach ($calibration in @('L33XEH000','L35HEH000','L38KEH000','L39SED000','L39SEE000','L39SEF000','L3M6EG000','L3M7EF000','L3P4EM000','L3CGEE000')) {
    $folder = Join-Path $runRoot $calibration
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $report = Join-Path $folder 'report.json'
    $arguments = @('--editor-offline','--calibration',$calibration,'--output',('"' + $folder + '"'),'--report',('"' + $report + '"'))
    $process = Start-Process -FilePath $harness -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Editor UI timed out: $calibration" }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw "Editor UI failed: $calibration; see $report" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($result.passed -ne $true -or $result.synthetic -ne $true -or $result.hardwareAccessed -ne $false -or $result.workflow.directCellCommit -ne $true) { throw "Unexpected editor UI result: $calibration" }
    $reports += $result
}
@{ passed = $true; hardwareAccessed = $false; reports = $reports } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
Write-Output ("Ten synthetic editor UI workflows passed: " + $runRoot)
