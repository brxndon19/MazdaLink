param([Parameter(Mandatory)][string]$Results)
$ErrorActionPreference = 'Stop'
[xml]$run = Get-Content -LiteralPath $Results -Raw
$passed = @($run.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed' | ForEach-Object testName)
foreach ($name in @('MazdaLink.Tests.TargetTests.RawReadsDoNotRequireAWhitelistedIdentityOrKnownRomLayout(identity: "ZYJ2EA000ZYV5050")','MazdaLink.Tests.TargetTests.KnownLookingRomFromUnknownCalibrationCannotUseTheZy87ChecksumPolicy','MazdaLink.Tests.TargetTests.RefusedReadStopsWithoutKeyRetryOrProtocolProbe','MazdaLink.Tests.TargetTests.EveryPrepareCorrectsAllRangesVerifiesDiskAndPreservesInputs','MazdaLink.Tests.TargetTests.ReadsBothPassesAt512AndExportsExactBytesWithNoOverwrite','MazdaLink.Tests.TargetTests.TransportRequiresValidIdentityBeforeSecurityEvenForDirectCalls','MazdaLink.Tests.TargetTests.MenuAndCommandsCannotOpenHardwareForFlashingOrOverrides')) {
    if ($passed -notcontains $name) { throw "Missing mandatory MazdaLink integrity/scope case: $name" }
}
if ([int]$run.TestRun.ResultSummary.Counters.total -lt 434) { throw 'Focused scope/integrity matrix is incomplete.' }
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

foreach ($name in @('MazdaLink.Tests.NativeBenchTests.ProtectedInitializationCannotBeAcquiredByIdentificationOrReadOnlyRamCapabilities','MazdaLink.Tests.NativeBenchTests.ProtectedInitializationNativeSequenceRequiresEveryByteAndCannotRestart','MazdaLink.Tests.NativeBenchTests.ProtectedInitializationCannotSkipReadbackOrAdvancePastAPendingReply','MazdaLink.Tests.NativeBenchTests.ProtectedInitializationNativeMismatchPermanentlyBlocksExecution','MazdaLink.Tests.ProtectedInitializationTests.InitializationResultRemainsObservableIfMonitorReturnFails','MazdaLink.Tests.ProtectedInitializationTests.FixedInitializationProgramAndScriptRemainImmutable','MazdaLink.Tests.ProtectedInitializationTests.InitializationUploadsOnlyTheFixedImageAndVerifiesEveryByteBeforeExecution','MazdaLink.Tests.ProtectedInitializationTests.MissingBackupsAndExternalCodeOverridesCannotOpenTheInitializationBackend')) {
    if ($passed -notcontains $name) { throw "Missing mandatory protected initialization case: $name" }
}
foreach ($case in @(@('UploadEchoBurstIsDrainedWithoutClearingAnEcuUploadError',2),@('AnyInitializationImageByteMismatchPreventsExecution',3),@('InitializationCancellationBeforeExecuteNeverRunsTheControllerProgram',2),@('WrongInitializationMarkerOrMonitorReturnCannotPass',2),@('NonzeroInitializationStatusCannotQualifyAControllerTest',13),@('ZeroStatusCannotHideFailedInitializationResultBytes',3))) {
    $pattern = 'MazdaLink.Tests.ProtectedInitializationTests.' + $case[0] + '(*)'
    if (@($passed | Where-Object { $_ -like $pattern }).Count -ne $case[1]) { throw "Incomplete protected initialization matrix: $pattern" }
}

foreach ($name in @('MazdaLink.Tests.RecoveryBindingTests.PublicBindingFactoryRejectsAnUnknownBackupProfile','MazdaLink.Tests.RecoveryBindingTests.EveryBindingWireStepIsRestrictedToItsFixedPreservedRangeOrFinalIdentity','MazdaLink.Tests.RecoveryBindingTests.BindingSnapshotAndReturnedPacketsCannotBeMutatedByTheCaller','MazdaLink.Tests.RecoveryBindingTests.BindingReadsEveryPreservedByteAndHashesObservedRepliesWithoutUploadOrExecution','MazdaLink.Tests.RecoveryBindingTests.MatchedBootEvidenceSurvivesAFailedFinalMonitorReply','MazdaLink.Tests.RecoveryBindingTests.CancelledBindingStopsBeforeAnotherRequest','MazdaLink.Tests.RecoveryBindingTests.BindingEnforcesAWholeRunDeadlineWithoutRetrying','MazdaLink.Tests.RecoveryBindingTests.BindingEnforcesTheSingleReplyDeadlineWithoutRetrying','MazdaLink.Tests.RecoveryBindingTests.ExcessBindingEchoesCannotHideAnUnboundedReceiveLoop','MazdaLink.Tests.RecoveryBindingTests.BindingCannotOpenHardwareForMissingBackupsOrExternalOverrides','MazdaLink.Tests.NativeBenchTests.BindingCapabilityIsIsolatedFromEveryRamCapability','MazdaLink.Tests.NativeBenchTests.NativeBindingRequiresAllPreservedRepliesBeforeItsFinalIdentityAndCannotRestart','MazdaLink.Tests.NativeBenchTests.NativeBindingCannotSkipStepsOrAdvancePastAnUnansweredRequest','MazdaLink.Tests.NativeBenchTests.NativeBindingByteMismatchPermanentlyBlocksFurtherReads')) {
    if ($passed -notcontains $name) { throw "Missing mandatory read-only binding case: $name" }
}
if (@($passed | Where-Object { $_ -like 'MazdaLink.Tests.RecoveryBindingTests.AnyPreservedByteMismatchStopsBeforeBindingCanPass(*)' }).Count -ne 5) { throw 'Missing mandatory preserved-boot mismatch cases.' }

foreach ($name in @('ControlPlanBindsPreservedBootBeforeOnlyItsImmutableFixedImage','ControlRunnerVerifiesEveryByteAndRecordsResultBeforeMonitorReturn','CancelledControlCannotAdvanceToExecution','ControlResultRemainsObservableIfFinalMonitorFails','UncertainRestorationRetainsResultAndStopsBeforeAnyMonitorQuery','WrongControlResultMarkerCannotBecomeEvidence','MissingBackupsAndExternalOverridesCannotOpenControlHardware')) {
    if ($passed -notcontains ('MazdaLink.Tests.WriteEnableControlTests.'+$name)) { throw "Missing write-enable control case: $name" }
}
foreach ($name in @('WriteEnableControlCannotBeAcquiredByAnyExistingCapability','NativeControlRequiresFullBootBindingAndEveryImageByteBeforeOneExecution','NativeControlCannotSkipBindingImageRepliesOrInitialMonitor')) {
    if ($passed -notcontains ('MazdaLink.Tests.NativeBenchTests.'+$name)) { throw "Missing native control capability case: $name" }
}
foreach ($case in @(@('MazdaLink.Tests.WriteEnableControlTests.AnyControlByteMismatchPreventsExecution(*)',3),@('MazdaLink.Tests.WriteEnableControlTests.StatusOrIncompleteObservationsCannotQualifyControl(*)',14),@('MazdaLink.Tests.NativeBenchTests.NativeControlBindingOrImageMismatchPermanentlyBlocksExecution(*)',2))) {
    if (@($passed | Where-Object { $_ -like $case[0] }).Count -ne $case[1]) { throw "Incomplete control fault matrix: $($case[0])" }
}

foreach ($name in @('RehearsalPlanBindsPreservedBootBeforeOnlyItsImmutableFixedImage','RehearsalRunnerVerifiesEveryByteAndRecordsResultBeforeMonitorReturn','CancelledRehearsalCannotAdvanceToExecution','RehearsalResultRemainsObservableIfFinalMonitorFails','FailedRehearsalRetainsResultAndStopsBeforeAnyMonitorQuery','WrongRehearsalResultMarkerCannotBecomeEvidence','FailedStatusOrIncompleteServiceEvidenceCannotQualifyRehearsal','MissingBackupsAndExternalOverridesCannotOpenRehearsalHardware')) {
    if ($passed -notcontains ('MazdaLink.Tests.RecoveryRehearsalTests.'+$name)) { throw "Missing recovery rehearsal case: $name" }
}
foreach ($name in @('RehearsalCannotBeAcquiredByAnyExistingCapability','NativeRehearsalRequiresFullBootBindingAndEveryImageByteBeforeOneExecution','NativeRehearsalCannotSkipBindingImageRepliesOrInitialMonitor')) {
    if ($passed -notcontains ('MazdaLink.Tests.NativeBenchTests.'+$name)) { throw "Missing native recovery rehearsal case: $name" }
}
foreach ($case in @(@('MazdaLink.Tests.RecoveryRehearsalTests.AnyRehearsalByteMismatchPreventsExecution(*)',3),@('MazdaLink.Tests.NativeBenchTests.NativeRehearsalBindingOrImageMismatchPermanentlyBlocksExecution(*)',2))) {
    if (@($passed | Where-Object { $_ -like $case[0] }).Count -ne $case[1]) { throw "Incomplete recovery rehearsal matrix: $($case[0])" }
}
foreach ($name in @('PublicRestorePlannerRejectsUnknownOrUnverifiedTargetImages','OriginalRecipeCoversExactlyEb14InImmutableAlignedUnits','ErasedAndCompleteReadbackChecksCannotIgnorePreservedRegions','EveryAmbiguousUnitBoundaryRequiresFreshBindingAndWholeBlockErase','PartialEraseWithMissingApplicationCanBindAndReconstructOnlyTheSelectedBlock','WrongBootBindingStopsBeforeAnyErase','UnitReadbackFailureBlocksAllLaterProgrammingUntilAFreshErase','OfflinePlannerAndRehearsalOverridesNeverOpenHardware')) {
    if ($passed -notcontains ('MazdaLink.Tests.OriginalBlockRestoreTests.'+$name)) { throw "Missing offline restore contract case: $name" }
}

foreach ($name in @('CrcMatchesAnIndependentPublishedVector','TrialContainsOnlyFixedReadStageCommandsAndImmutableCode','OrderedFullReplyMustMatchCrcCookieSequenceAndIndex','FullFpfrAndFailedStagingRemainFailedEvenWithZeroLowByte','TrialRunnerAndIndependentGateVerifyTheWholeImageBeforeStaging','ChangedCurrentRomStopsBeforeRamStagingWithoutOverwritingBackup','GateCannotSkipReplyReadbackOrRestartAfterFailure','DurableIntentPrecedesEveryOperationAndTwoFullReadbacksComplete','JournalIsCreateNewBoundedFlushedAndHashChainedWithoutRomPayload','MissingBackupsExternalCodeAndRangesCannotOpenNativeTrialHardware','PreservedMismatchAndCancellationCannotIssueErase')) {
    if ($passed -notcontains ('MazdaLink.Tests.TransactionTests.'+$name)) { throw "Missing native transaction contract: $name" }
}
foreach ($name in @('NativeTransactionCapabilityCannotBeAcquiredByExistingChannels','NativeTrialRequiresMonitorFullBindingCodeStateAndEveryCommandReply','NativeTrialCannotSkipPendingRepliesOrFailedWrites')) {
    if ($passed -notcontains ('MazdaLink.Tests.NativeBenchTests.'+$name)) { throw "Missing native transaction capability gate: $name" }
}
foreach ($case in @(@('CorruptMissingDuplicatedOrReorderedFramesNeverPass',6),@('JournalFailureCannotAdvanceToTheNextDestructiveRequest',3),@('AmbiguousCompletionCannotResendAndNewSessionAlwaysReerases',4))) {
    if (@($passed | Where-Object { $_ -like ('MazdaLink.Tests.TransactionTests.'+$case[0]+'(*)') }).Count -ne $case[1]) { throw "Incomplete transaction fault matrix: $($case[0])" }
}

foreach ($name in @('FailedFullFpfrIsRetainedBeforeAnyProgrammingRequest','FixedPlanCannotAcceptModifiedDataAddressesOrRepeatedErase','OriginalWireSessionDurablyOrdersOneEraseAllUnitsAndTwoFullReads','RecoveryCannotIgnorePreservedMismatchOrAUnitReadbackFailure','MissingBusyCallbackFullFpfrOrWrongProgressCannotPass','GateCannotSkipVerificationOrAdvanceAnUnansweredStep','RestoreCliRefusesMissingEvidenceAndOverridesBeforeHardwareOpen','MalformedOrIncompletePrerequisiteCannotQualifyTheOriginal','FullQualificationJournalIsFlushedHashChainedAndContainsNoRomPayload')) {
    if ($passed -notcontains ('MazdaLink.Tests.RestoreQualificationTests.'+$name)) { throw "Missing original restore qualification gate: $name" }
}
foreach ($name in @('NativeOriginalRestoreGateRequiresAllReadbacksIntentAndOnePassProgramming','NativeRestoreCannotBeAcquiredSkippedOrPromotedToOtherCapabilities')) {
    if ($passed -notcontains ('MazdaLink.Tests.NativeBenchTests.'+$name)) { throw "Missing native original restore gate: $name" }
}
foreach ($case in @(@('UnknownCompletionStopsAndRecoveryAlwaysRebindsAndReerases',4),@('JournalFailureStopsBeforeAnyLaterDestructiveRequest',3),@('FirstRestoreRequiresExactCurrentImageBeforeErase',3))) {
    if (@($passed | Where-Object { $_ -like ('MazdaLink.Tests.RestoreQualificationTests.'+$case[0]+'(*)') }).Count -ne $case[1]) { throw "Incomplete original qualification fault matrix: $($case[0])" }
}

foreach ($name in @('FixedBoundaryContainsExactlyOneOriginalUnitAndAnErasedCalibrationId','PartialStopCannotBeReportedAsACompleteOriginalRestoration','ColdRecoveryFromTheExactPartialImageAlwaysErasesAndStartsAtUnitZero','AFailedSessionCannotAdvanceItsJournalThroughLateReplies','ValidBoundaryReceiptRequiresExactPartialCaptureAndAnEightRecordJournal','PartialEvidenceCannotQualifyOriginalRestorationOrAnActivePowerCut','OriginalRestorationPrerequisiteChecksBothCapturesAndTheCompleteHashChain','MissingEvidenceAndExternalOverridesCannotOpenControlledRecoveryHardware')) {
    if ($passed -notcontains ('MazdaLink.Tests.ControlledRecoveryTests.'+$name)) { throw "Missing controlled recovery gate: $name" }
}
if (@($passed | Where-Object { $_ -like 'MazdaLink.Tests.ControlledRecoveryTests.UnexpectedPartialOrPreservedBytesStopBeforeRecoveryErase(*)' }).Count -ne 5) { throw 'Controlled recovery partial/preserved mismatch matrix incomplete.' }
if ($passed -notcontains 'MazdaLink.Tests.NativeBenchTests.NativeControlledStopEndsAtOneVerifiedUnitWithoutACompleteRestorationRecord') { throw 'Missing native fixed partial-stop gate.' }

foreach ($name in @('IndependentBigEndianSumsVerifyAllNineteenRangesAndEveryCorrectionWord','EveryCorrectionIsRecalculatedAndUneditedExportIsByteExact','OfflineReportAndModifiedFlashOverridesCannotAcquireNativeCapabilities')) {
 if ($passed -notcontains ('MazdaLink.Tests.Zyj2IntegrityTests.'+$name)) { throw "Missing ZYJ2 integrity gate: $name" }
}
foreach ($case in @(@('CalibrationEditRequiresEb15CorrectionAndPlanNeverOpensHardware',3),@('ChangedCodeIdentityHeaderRangesOrUnqualifiedCalibrationCannotBePrepared',7),@('InvalidOriginalChecksumOrLayoutCannotAuthorizeAnExport',4))) {
 if (@($passed | Where-Object { $_ -like ('MazdaLink.Tests.Zyj2IntegrityTests.'+$case[0]+'(*)') }).Count -ne $case[1]) { throw 'ZYJ2 checksum/preparation matrix incomplete.' }
}

foreach ($name in @('Eb15ImageAndPlanAreFixedAndRestoreThePointerUnitLast','Eb15FullWireRunRequiresOneEraseAllUnitsAndTwoExactOriginalReads','Eb15UnitMismatchAndFullFpfrFailureBlockLaterProgramming','Eb15JournalBindsEveryRecordToBlock15AndRetainsLegacyFormat','Eb15CannotAcquireTheEb14ControlledStopOrOfflineDeviceContract','NativeChannelRejectsAJournalForTheOtherBlockBeforeDriverAccess','MissingEb15PrerequisitesAndExternalOverridesNeverOpenHardware')) {
 if ($passed -notcontains ('MazdaLink.Tests.Eb15QualificationTests.'+$name)) { throw "Missing EB15 qualification gate: $name" }
}
foreach ($case in @(@('FirstEb15TestRejectsEveryChangedCurrentRegionBeforeErase',5),@('UnknownEb15CompletionCannotRetryAndFreshRecoveryReerasesFromUnitZero',4),@('Eb15RecoveryNeverIgnoresPreservedEb14Differences',3),@('BlankVerificationIncludesChecksumAndPointerBeforeAnyProgramming',3),@('Eb15DurableJournalFailurePreventsLaterDestructiveRequests',3))) {
 if (@($passed | Where-Object { $_ -like ('MazdaLink.Tests.Eb15QualificationTests.'+$case[0]+'(*)') }).Count -ne $case[1]) { throw "Incomplete EB15 matrix: $($case[0])" }
}
foreach ($name in @('CompletedPairedRecoveryEvidenceRequiresEveryCaptureAndBothJournalChains','IncompleteWrongSourceFailedTransportAndFalseRecoveryClaimsCannotQualifyEb15')) {
 if ($passed -notcontains ('MazdaLink.Tests.Eb15PrerequisiteTests.'+$name)) { throw "Missing EB15 evidence gate: $name" }
}
if ($passed -notcontains 'MazdaLink.Tests.NativeBenchTests.NativeEb15RequiresItsOwnImageEveryReplyAndAnEntireOriginalRestoration') { throw 'Missing native EB15 capability gate.' }

if ($passed -notcontains 'MazdaLink.Tests.NativeBenchTests.NativeEb15RejectsAnEb14KernelByteBeforeAnyExecution') { throw 'Missing EB15 cross-image byte gate.' }

foreach ($name in @('FixedEb15BoundaryPreservesEb14AndErasesChecksumTableAndBootPointer','Eb15PartialStopHasItsOwnResultAndCannotClaimCompleteRestorationOrErasedCalibrationId','Eb15ColdRecoveryChecksExactPartialImageThenReerasesAndRestoresEveryOriginalUnit','Alpha18PrerequisiteRequiresBothOriginalCapturesAndTheBlock15Journal','ReceiptRequiresExactEb15CaptureAndEightBlock15RecordsBoundToTheCompletedRestoration','WrongBlockPointerOrCaptureCannotAuthorizeEb15BoundaryResume','MissingEvidenceAndCodeBlockOrUnitOverridesNeverOpenEitherBackend')) {
 if ($passed -notcontains ('MazdaLink.Tests.Eb15ControlledRecoveryTests.'+$name)) { throw "Missing controlled EB15 gate: $name" }
}
if (@($passed | Where-Object { $_ -like 'MazdaLink.Tests.Eb15ControlledRecoveryTests.AnyUnexpectedPartialOrPreservedByteStopsBeforeRecoveryErase(*)' }).Count -ne 8) { throw 'Incomplete EB15 partial/preserved mismatch matrix.' }
if ($passed -notcontains 'MazdaLink.Tests.NativeBenchTests.NativeEb15ControlledBoundaryStopsAtOneUnitWithNoCompleteRestorationRecord') { throw 'Missing native EB15 completed-boundary gate.' }

foreach ($name in @('BothFixedBlocksProduceExactModifiedImageAndPreserveThePointerUntilItsLastUnit','SubsequentValidTuneUsesAFrozenCurrentSnapshotAndRejectsAnInvalidCurrentChecksum','FrozenOtherBlockCannotChangeAfterBindingOrBetweenBlockWrites','OriginalOnlyNativeCapabilityRejectsModifiedPlansBeforeDriverAccess','PublicFactoryRequiresExaminedBackupAndNeverAcceptsAnotherCalibration','DedicatedCommandsRejectMissingFilesAndOverridesBeforeAnyHardwareOpen','CombinedJournalBindsBothImagesLiveSnapshotBlockOrderAndHashChain')) {
 if ($passed -notcontains ('MazdaLink.Tests.ModifiedFlashTests.'+$name)) { throw "Missing modified spare gate: $name" }
}
foreach ($case in @(@('UnknownCompletionNeverRetriesAndColdRecoveryReerasesBothWholeBlocks',6),@('RecoveryCannotIgnoreAnyPreservedProgramDifference',2),@('ModifiedJournalFailureStopsAllLaterDestructiveRequests',3))) {
 if (@($passed | Where-Object { $_ -like ('MazdaLink.Tests.ModifiedFlashTests.'+$case[0]+'(*)') }).Count -ne $case[1]) { throw "Incomplete modified spare matrix: $($case[0])" }
}
foreach ($name in @('NativeModifiedTransitionRetainsOneMonitorConnectionAndRequiresBothExactBlocks','NativeModifiedTransitionRejectsIncompleteFailedAndOriginalOnlyCapabilities')) {
 if ($passed -notcontains ('MazdaLink.Tests.NativeBenchTests.'+$name)) { throw "Missing native modified capability gate: $name" }
}

foreach ($name in @('LoadingEditedBinAutomaticallyPreparesEveryChecksumAndKeepsInputsIntact','ChangedPreparedFileCannotSilentlyReplaceTheReviewedTargetEvenWithValidChecksums','ChangedOriginalCaptureCannotRemainAnAuthorizedDesktopBackup','AFailedFileLoadClearsThePreviousWriteTarget','SelectingAnotherOrInvalidBackupClearsAllPreparedAuthorization','SaveBinIsAnExactRawCopyAndNeverOverwritesAnExistingFile','MissingBackupAndUnpreparedTargetAreRejectedWithoutHardware')) {
 if ($passed -notcontains ('MazdaLink.Tests.DesktopFilesTests.'+$name)) { throw "Missing desktop file/integrity gate: $name" }
}
