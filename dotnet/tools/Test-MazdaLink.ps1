param([string]$Output = 'TestResults/MazdaLinkStandalone')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $root -Force | Out-Null
dotnet publish src/MazdaLink -c Release --no-restore -o publish-mazdalink -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { throw 'MazdaLink publish failed.' }
$exe = [IO.Path]::GetFullPath('publish-mazdalink/MazdaLink.exe')
$files = @(Get-ChildItem publish-mazdalink -File)
if ($files.Count -ne 1 -or $files[0].Name -ne 'MazdaLink.exe') { throw 'Release must contain only the independent MazdaLink executable.' }
function Invoke-Check([string[]]$Arguments, [string]$InputText, [int]$Expected, [string]$Name) {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $process.StandardInput.Write($InputText); $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(30000)) { $process.Kill(); throw "Standalone $Name timed out." }
    $text = $stdout.GetAwaiter().GetResult(); $errors = $stderr.GetAwaiter().GetResult()
    $text | Set-Content (Join-Path $root "$Name.txt") -Encoding utf8NoBOM
    if ($process.ExitCode -ne $Expected -or $errors.Length -gt 0) { throw "Standalone $Name failed: $errors" }
    return $text
}
$help = Invoke-Check -Arguments @('help') -InputText '' -Expected 0 -Name 'help'
$licence = Invoke-Check -Arguments @('license') -InputText '' -Expected 0 -Name 'licence'
if ($licence -notmatch 'GNU GENERAL PUBLIC LICENSE' -or $licence -notmatch 'MIT License') { throw 'Embedded application/runtime licences are missing.' }
$status = Invoke-Check -Arguments @('status') -InputText '' -Expected 0 -Name 'status'
$menu = Invoke-Check -Arguments @() -InputText "6`n7`n0`n" -Expected 0 -Name 'menu'
$flash = Invoke-Check -Arguments @('flash','does-not-exist.rom') -InputText '' -Expected 1 -Name 'flash-refused'
$override = Invoke-Check -Arguments @('read','--gen2') -InputText '' -Expected 1 -Name 'override-refused'
$bench = Invoke-Check -Arguments @('bench-recovery-check','does-not-exist\original.rom') -InputText '' -Expected 1 -Name 'bench-refused-before-open'
$ram = Invoke-Check -Arguments @('ram-self-test','does-not-exist\original.rom') -InputText '' -Expected 1 -Name 'ram-refused-before-open'
$ramOverride = Invoke-Check -Arguments @('ram-self-test','does-not-exist\original.rom','external.bin') -InputText '' -Expected 1 -Name 'ram-override-refused'
$initialization = Invoke-Check -Arguments @('protected-flash-init','does-not-exist\original.rom') -InputText '' -Expected 1 -Name 'protected-init-refused-before-open'
$initializationOverride = Invoke-Check -Arguments @('protected-flash-init','does-not-exist\original.rom','external.bin') -InputText '' -Expected 1 -Name 'protected-init-override-refused'
if ($initialization -notmatch 'STOPPED' -or $initializationOverride -notmatch 'Unknown command' -or $help -notmatch 'protected-flash-init' -or $menu -notmatch 'protected flash-algorithm initialization' -or $help -notmatch 'INIT ZYJ2') { throw 'Protected initialization scope/backup gate is missing.' }
$binding = Invoke-Check -Arguments @('recovery-binding-check','does-not-exist\original.rom') -InputText '' -Expected 1 -Name 'binding-refused-before-open'
$bindingOverride = Invoke-Check -Arguments @('recovery-binding-check','does-not-exist\original.rom','0xC0000') -InputText '' -Expected 1 -Name 'binding-address-override-refused'
if ($binding -notmatch 'STOPPED' -or $bindingOverride -notmatch 'Unknown command' -or $help -notmatch 'recovery-binding-check' -or $help -notmatch 'BIND ZYJ2' -or $menu -notmatch 'read-only recovery boot binding') { throw 'Read-only recovery binding scope/backup gate is missing.' }
$control = Invoke-Check -Arguments @('write-enable-check','does-not-exist\original.rom') -InputText '' -Expected 1 -Name 'control-refused-before-open'
$controlOverride = Invoke-Check -Arguments @('write-enable-check','does-not-exist\original.rom','external.bin') -InputText '' -Expected 1 -Name 'control-override-refused'
if ($control -notmatch 'STOPPED' -or $controlOverride -notmatch 'Unknown command' -or $help -notmatch 'write-enable-check' -or $help -notmatch 'ENABLE ZYJ2' -or $menu -notmatch 'write-enable control and restoration') { throw 'Write-enable control scope/backup gate is missing.' }
$rehearsal = Invoke-Check -Arguments @('recovery-rehearsal','does-not-exist\original.rom') -InputText '' -Expected 1 -Name 'rehearsal-refused-before-open'
$rehearsalOverride = Invoke-Check -Arguments @('recovery-rehearsal','does-not-exist\original.rom','external.bin') -InputText '' -Expected 1 -Name 'rehearsal-override-refused'
$plan = Invoke-Check -Arguments @('original-restore-plan','does-not-exist\original.rom') -InputText '' -Expected 1 -Name 'original-plan-refused-before-open'
if ($rehearsal -notmatch 'STOPPED' -or $plan -notmatch 'STOPPED' -or $rehearsalOverride -notmatch 'Unknown command' -or $help -notmatch 'REHEARSE ZYJ2' -or $help -notmatch 'original-restore-plan' -or $menu -notmatch 'recovery kernel rehearsal') { throw 'Recovery rehearsal/offline plan scope gates are missing.' }
$transaction = Invoke-Check -Arguments @('native-transaction-check','does-not-exist\original.rom') -InputText '' -Expected 1 -Name 'transaction-refused-before-open'
$transactionOverride = Invoke-Check -Arguments @('native-transaction-check','does-not-exist\original.rom','external.bin') -InputText '' -Expected 1 -Name 'transaction-override-refused'
if ($transaction -notmatch 'STOPPED' -or $transactionOverride -notmatch 'Unknown command' -or $help -notmatch 'TRANSACTION ZYJ2' -or $menu -notmatch 'native full-ROM read and RAM staging') { throw 'Native read/staging scope gates are missing.' }
$restore = Invoke-Check -Arguments @('original-restore-check','does-not-exist\original.rom','missing\events.jsonl') -InputText '' -Expected 1 -Name 'original-restore-refused-before-open'
$recover = Invoke-Check -Arguments @('original-restore-recover','does-not-exist\original.rom','missing\events.jsonl') -InputText '' -Expected 1 -Name 'original-recover-refused-before-open'
$restoreOverride = Invoke-Check -Arguments @('original-restore-check','original.rom','events.jsonl','modified.rom') -InputText '' -Expected 1 -Name 'original-restore-override-refused'
if ($restore -notmatch 'STOPPED' -or $recover -notmatch 'STOPPED' -or $restoreOverride -notmatch 'Unknown command' -or $help -notmatch 'RESTORE ORIGINAL ZYJ2' -or $help -notmatch 'RECOVER ORIGINAL ZYJ2' -or $menu -notmatch 'ERASE / RESTORE ORIGINAL EB14') { throw 'Original-only qualification scope or prerequisite gates are missing.' }
$controlled = Invoke-Check -Arguments @('controlled-recovery-check','does-not-exist\original.rom','missing\alpha15') -InputText '' -Expected 1 -Name 'controlled-recovery-refused-before-open'
$controlledResume = Invoke-Check -Arguments @('controlled-recovery-resume','does-not-exist\original.rom','missing\alpha15','missing\boundary') -InputText '' -Expected 1 -Name 'controlled-resume-refused-before-open'
$controlledOverride = Invoke-Check -Arguments @('controlled-recovery-check','original.rom','run','unit-override') -InputText '' -Expected 1 -Name 'controlled-stop-override-refused'
if ($controlled -notmatch 'STOPPED' -or $controlledResume -notmatch 'STOPPED' -or $controlledOverride -notmatch 'Unknown command' -or $help -notmatch 'BOUNDARY RECOVERY ZYJ2' -or $help -notmatch 'RESUME BOUNDARY ZYJ2' -or $menu -notmatch 'controlled unit-zero boundary / cold recovery') { throw 'Controlled boundary prerequisites/scope gates are missing.' }
if ($ram -notmatch 'STOPPED'   -or $ramOverride -notmatch 'Unknown command' -or $help -notmatch 'ram-self-test' -or $menu -notmatch 'fixed volatile RAM self-test' -or $help -notmatch '60-second ECU-off wait BEFORE' -or $help -notmatch '10-second startup wait' -or $help -notmatch 'resident hardware class') { throw 'Fixed RAM scope or cold-start guidance is missing.' }
if ($bench -notmatch 'STOPPED' -or $help -notmatch 'bench-recovery-check' -or $menu -notmatch 'bench monitor identification check') { throw 'Bench command/backup gate is missing.' }
if ($help -notmatch 'Keep OBD 12 V and USB powered' -or $help -notmatch 'measured timing, without frame payloads') { throw 'Bench power/diagnostics guidance is missing.' }
if ($help -notmatch 'ZY87EC000ZYV5050' -or $help -match 'SpeedLink' -or $help -notmatch 'no fixed calibration whitelist' -or $menu -notmatch 'no fixed calibration whitelist' -or $menu -notmatch 'Flashing is unavailable' -or $flash -notmatch 'No adapter was opened' -or $status -notmatch 'Other-vehicle workflows: absent') { throw 'Unexpected standalone product scope.' }
$runtime = ([xml](Get-Content 'Directory.Build.props' -Raw)).Project.PropertyGroup.RuntimeFrameworkVersion
$version = ([xml](Get-Content 'Directory.Build.props' -Raw)).Project.PropertyGroup.Version
if ($status -notmatch 'Process: 32-bit' -or !$status.Contains("Runtime: $runtime") -or !$status.Contains("MazdaLink $version")) { throw 'Standalone runtime, architecture or version differs from the pins.' }
@{passed=$true;product='MazdaLink';readIdentityPolicy='reported-calibration';checksumSoftwareId='ZY87EC000ZYV5050';benchRecoveryCheck='ZYJ2EA000ZYV5050-identification-only';benchMissingBackupRefused=$true;ramMissingBackupRefused=$true;ramExternalCodeRefused=$true;protectedInitializationMissingBackupRefused=$true;protectedInitializationExternalCodeRefused=$true;protectedInitializationHardwareAcceptance='unqualified';ramSelfTest='ZYJ2EA000ZYV5050-fixed-volatile-only';ramHardwareAcceptance='unqualified';recoveryBindingMissingBackupRefused=$true;recoveryBindingExternalAddressRefused=$true;recoveryBindingHardwareAcceptance='unqualified';writeEnableControlMissingBackupRefused=$true;writeEnableControlOverridesRefused=$true;writeEnableControlHardwareAcceptance='unqualified';recoveryRehearsalMissingBackupRefused=$true;recoveryRehearsalOverridesRefused=$true;originalRestorePlanMissingBackupRefused=$true;recoveryRehearsalHardwareAcceptance='unqualified';nativeTransactionMissingBackupRefused=$true;nativeTransactionOverridesRefused=$true;nativeTransactionHardwareAcceptance='unqualified';originalRestoreMissingPrerequisitesRefused=$true;originalRestoreOverridesRefused=$true;originalRestoreHardwareAcceptance='unqualified';controlledRecoveryPrerequisitesRefused=$true;controlledRecoveryOverrideRefused=$true;activeFlashPowerCutImplemented=$false;modifiedFlashingEnabled=$false;programmingEnabled=$true;hardwareAccessed=$false;menu=$true;overrideRefused=$true;flashRefusedBeforeDriverOpen=$true} | ConvertTo-Json | Set-Content (Join-Path $root 'report.json') -Encoding utf8NoBOM

$modifiedPlan = Invoke-Check -Arguments @('modified-rom-plan','missing/original.rom','edited.rom') -InputText '' -Expected 1 -Name 'modified-plan-refused-before-open'
$modifiedOverride = Invoke-Check -Arguments @('modified-rom-plan','original.rom','edited.rom','--write-eb15') -InputText '' -Expected 1 -Name 'modified-plan-override-refused'
if ($modifiedPlan -notmatch 'STOPPED' -or $modifiedOverride -notmatch 'Unknown command' -or $help -notmatch 'ZYJ2EA000ZYV5050 only' -or $help -notmatch 'modified-rom-plan' -or $menu -notmatch 'modified-ROM block report') { throw 'Offline ZYJ2 preparation scope missing.' }

$eb15 = Invoke-Check -Arguments @('eb15-original-restore-check','missing/original.rom','missing/alpha16') -InputText '' -Expected 1 -Name 'eb15-original-refused-before-open'
$eb15Recovery = Invoke-Check -Arguments @('eb15-original-restore-recover','missing/original.rom','missing/alpha16') -InputText '' -Expected 1 -Name 'eb15-recovery-refused-before-open'
$eb15Override = Invoke-Check -Arguments @('eb15-original-restore-check','original.rom','run','modified.rom') -InputText '' -Expected 1 -Name 'eb15-modified-override-refused'
if ($eb15 -notmatch 'STOPPED' -or $eb15Recovery -notmatch 'STOPPED' -or $eb15Override -notmatch 'Unknown command' -or $help -notmatch 'RESTORE EB15 ORIGINAL ZYJ2' -or $help -notmatch 'RECOVER EB15 ORIGINAL ZYJ2' -or $menu -notmatch 'ERASE / RESTORE ORIGINAL EB15') { throw 'EB15 original-only prerequisite/scope gates missing.' }
