param([Parameter(Mandatory)][string]$Results)
$ErrorActionPreference = 'Stop'
[xml]$run = Get-Content -LiteralPath $Results -Raw
$passed = @($run.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed' | ForEach-Object testName)
foreach ($name in @('MazdaLink.Tests.TargetTests.RawReadsDoNotRequireAWhitelistedIdentityOrKnownRomLayout(identity: "ZYJ2EA000ZYV5050")','MazdaLink.Tests.TargetTests.KnownLookingRomFromUnknownCalibrationCannotUseTheZy87ChecksumPolicy','MazdaLink.Tests.TargetTests.RefusedReadStopsWithoutKeyRetryOrProtocolProbe','MazdaLink.Tests.TargetTests.EveryPrepareCorrectsAllRangesVerifiesDiskAndPreservesInputs','MazdaLink.Tests.TargetTests.ReadsBothPassesAt512AndExportsExactBytesWithNoOverwrite','MazdaLink.Tests.TargetTests.TransportRequiresValidIdentityBeforeSecurityEvenForDirectCalls','MazdaLink.Tests.TargetTests.MenuAndCommandsCannotOpenHardwareForFlashingOrOverrides')) {
    if ($passed -notcontains $name) { throw "Missing mandatory MazdaLink integrity/scope case: $name" }
}
if ([int]$run.TestRun.ResultSummary.Counters.total -lt 74) { throw 'Focused scope/integrity matrix is incomplete.' }
foreach ($name in @('MazdaLink.Tests.BenchRecoveryTests.EntryStopsBeforeOneIdentificationAndNeverUploadsOrExecutes','MazdaLink.Tests.BenchRecoveryTests.CancellationStopsPeriodicTrafficBeforeAnyIdentificationRequest','MazdaLink.Tests.BenchRecoveryTests.WireGateAllowsOnlyItsTwoFixedNonProgrammingPayloads','MazdaLink.Tests.BenchRecoveryTests.SpoofingHeaderAndCalibrationDoesNotSatisfyTheBootCodeFingerprints','MazdaLink.Tests.NativeBenchTests.NativePacketsUseRawExtendedCanAndExactlyTwoFixedPayloads','MazdaLink.Tests.NativeBenchTests.FailedNativeWriteCannotBeRetried')) {
    if ($passed -notcontains $name) { throw "Missing mandatory bench-scope case: $name" }
}
if ([int]$run.TestRun.ResultSummary.Counters.failed -ne 0 -or [int]$run.TestRun.ResultSummary.Counters.notExecuted -ne 0) { throw 'Focused test suite has failures or skipped tests.' }
Write-Output 'Raw-read integrity, no-programming gates, bounded identification-only bench flow and native packet checks passed. Hardware acceptance remains unqualified.'
