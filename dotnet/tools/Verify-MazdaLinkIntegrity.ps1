param([Parameter(Mandatory)][string]$Results)
$ErrorActionPreference = 'Stop'
[xml]$run = Get-Content -LiteralPath $Results -Raw
$passed = @($run.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed' | ForEach-Object testName)
foreach ($name in @('MazdaLink.Tests.TargetTests.EveryPrepareCorrectsAllRangesVerifiesDiskAndPreservesInputs','MazdaLink.Tests.TargetTests.ReadsBothPassesAt512AndExportsExactBytesWithNoOverwrite','MazdaLink.Tests.TargetTests.TransportRequiresExactIdentityBeforeSecurityEvenForDirectCalls','MazdaLink.Tests.TargetTests.MenuAndCommandsCannotOpenHardwareForFlashingOrOverrides')) {
    if ($passed -notcontains $name) { throw "Missing mandatory MazdaLink integrity/scope case: $name" }
}
if ([int]$run.TestRun.ResultSummary.Counters.failed -ne 0 -or [int]$run.TestRun.ResultSummary.Counters.notExecuted -ne 0) { throw 'Focused test suite has failures or skipped tests.' }
Write-Output 'Exact-target, all-19-range correction/readback, raw-export and no-programming gates passed.'
