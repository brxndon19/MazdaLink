param([Parameter(Mandatory)][string]$Results)
$ErrorActionPreference = 'Stop'
[xml]$run = Get-Content -LiteralPath $Results -Raw
$passed = @($run.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed' | ForEach-Object testName)
foreach ($name in @('MazdaLink.Tests.TargetTests.RawReadsDoNotRequireAWhitelistedIdentityOrKnownRomLayout(identity: "ZYJ2EA000ZYV5050")','MazdaLink.Tests.TargetTests.KnownLookingRomFromUnknownCalibrationCannotUseTheZy87ChecksumPolicy','MazdaLink.Tests.TargetTests.RefusedReadStopsWithoutKeyRetryOrProtocolProbe','MazdaLink.Tests.TargetTests.EveryPrepareCorrectsAllRangesVerifiesDiskAndPreservesInputs','MazdaLink.Tests.TargetTests.ReadsBothPassesAt512AndExportsExactBytesWithNoOverwrite','MazdaLink.Tests.TargetTests.TransportRequiresValidIdentityBeforeSecurityEvenForDirectCalls','MazdaLink.Tests.TargetTests.MenuAndCommandsCannotOpenHardwareForFlashingOrOverrides')) {
    if ($passed -notcontains $name) { throw "Missing mandatory MazdaLink integrity/scope case: $name" }
}
if ([int]$run.TestRun.ResultSummary.Counters.total -lt 178) { throw 'Focused scope/integrity matrix is incomplete.' }
foreach ($name in @('MazdaLink.Tests.BenchRecoveryTests.EntryStopsBeforeOneIdentificationAndNeverUploadsOrExecutes','MazdaLink.Tests.BenchRecoveryTests.CancellationStopsPeriodicTrafficBeforeAnyIdentificationRequest','MazdaLink.Tests.BenchRecoveryTests.WireGateAllowsOnlyItsTwoFixedNonProgrammingPayloads','MazdaLink.Tests.BenchRecoveryTests.SpoofingHeaderAndCalibrationDoesNotSatisfyTheBootCodeFingerprints','MazdaLink.Tests.NativeBenchTests.NativePacketsUseRawExtendedCanAndExactlyTwoFixedPayloads','MazdaLink.Tests.NativeBenchTests.FailedNativeWriteCannotBeRetried')) {
    if ($passed -notcontains $name) { throw "Missing mandatory bench-scope case: $name" }
}
if ([int]$run.TestRun.ResultSummary.Counters.failed -ne 0 -or [int]$run.TestRun.ResultSummary.Counters.notExecuted -ne 0) { throw 'Focused test suite has failures or skipped tests.' }
foreach ($name in @('MazdaLink.Tests.BenchTimingTests.RoundedWindowsPollingDoesNotStretchTwentySecondsToTwentyFive','MazdaLink.Tests.BenchTimingTests.SlowPollingAndStartLatencyConsumeTheSameDeadline','MazdaLink.Tests.NativeBenchTests.ReceiveStatisticsDistinguishEntryEchoesEmptyReadsAndIdentityWithoutPayloadLogging','MazdaLink.Tests.NativeBenchTests.EntryTrafficIsGroupedIntoBoundedTimeBins','MazdaLink.Tests.BenchRecoveryTests.EntryReceiveFailureStopsThePulseBeforeIdentification')) {
    if ($passed -notcontains $name) { throw "Missing mandatory bench-diagnostics case: $name" }
}
if ($passed -notcontains 'MazdaLink.Tests.NativeBenchTests.AdapterDiagnosticsConfigureOnlyLocalEchoAndMeasureSupplyOutsideEntry' -or
    @($passed | Where-Object { $_ -like 'MazdaLink.Tests.NativeBenchTests.LoopbackSetAndGetAreDistinctAndMissingReadbackCannotClaimEnabled(*)' }).Count -ne 6) {
    throw 'Missing mandatory J2534 SET/GET loopback configuration and readback cases.'
}
foreach ($name in @('MazdaLink.Tests.RamSelfTestTests.ReservedProgrammingPinFlagsCannotBecomeARecordedResult','MazdaLink.Tests.RamSelfTestTests.ScriptUploadsOnlyItsFixedRamImageAndReadsBackEveryByteBeforeExecute','MazdaLink.Tests.RamSelfTestTests.CancellationAfterReadbackCannotExecute','MazdaLink.Tests.RamSelfTestTests.UploadErrorIsNotLostBeforeReadbackAndCannotReachExecution','MazdaLink.Tests.RamSelfTestTests.ExternalCodeAddressOverridesAndMissingBackupsCannotOpenEitherBackend','MazdaLink.Tests.RamSelfTestTests.ColdPowerOffWaitCompletesBeforeStartAndCancellationStopsSetup','MazdaLink.Tests.RamSelfTestTests.NormalRestartWaitsSixtyThenTenSecondsWithExplicitPowerConfirmations','MazdaLink.Tests.RamSelfTestTests.WrongNormalRestartPowerConfirmationCannotStartDiagnosticsWait','MazdaLink.Tests.NativeBenchTests.IdentificationChannelCannotAcquireTheSeparateRamCapability','MazdaLink.Tests.NativeBenchTests.RamStepsCannotSkipReadbackOrAdvancePastAnUnansweredAddress','MazdaLink.Tests.NativeBenchTests.NativeRamCapabilityRequiresAllOneHundredTwentyByteRepliesBeforeExecutionAndCannotRestart','MazdaLink.Tests.NativeBenchTests.NativeRamByteMismatchPermanentlyBlocksLaterSteps')) {
    if ($passed -notcontains $name) { throw "Missing mandatory fixed-RAM capability case: $name" }
}
if (@($passed | Where-Object { $_ -like 'MazdaLink.Tests.RamSelfTestTests.FixedSh2ProgramOnlyWritesItsOwnRamResultAndReturnsToMonitor(*)' }).Count -ne 64 -or
    @($passed | Where-Object { $_ -like 'MazdaLink.Tests.RamSelfTestTests.AnyByteMismatchPreventsExecution(*)' }).Count -ne 3 -or
    @($passed | Where-Object { $_ -like 'MazdaLink.Tests.RamSelfTestTests.CancelledNormalRestartCannotCompleteStartup(*)' }).Count -ne 2) {
    throw 'Missing mandatory independent SH-2 execution or per-byte mismatch cases.'
}
Write-Output 'Raw-read integrity, no-flash gates, isolated monitor and fixed volatile RAM contracts passed. Hardware acceptance remains unqualified.'
