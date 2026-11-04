param([string]$Output = 'TestResults/LoggerRendering')
$ErrorActionPreference = 'Stop'
$folder = Join-Path ([IO.Path]::GetFullPath($Output)) ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'SpeedLink.UiHarness/SpeedLink.UiHarness.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Logger rendering harness build failed.' }
$executable = Join-Path $PSScriptRoot 'SpeedLink.UiHarness/bin/Release/net10.0-windows/win-x86/SpeedLink.UiHarness.exe'
$report = Join-Path $folder 'report.json'
$arguments = @('--logger-render-offline','--require-stable','--output',('"'+$folder+'"'),'--report',('"'+$report+'"'))
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'Offline logger rendering timed out.' }
if ($process.ExitCode -ne 0) { if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw }; throw 'Offline logger rendering failed.' }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (!$result.passed -or $result.hardwareAccessed -or !$result.stableChartViews -or !$result.unchangedBindingsQuiet -or !$result.gapsAndFlatValuesVerified -or !$result.selectionChangesBounded -or $result.charts -ne 6 -or $result.channels -ne 33) { throw 'Unexpected logger rendering result.' }
Write-Output "Offline logger rendering passed: $report"
