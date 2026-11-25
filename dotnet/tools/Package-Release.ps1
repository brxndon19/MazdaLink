param([string]$Executable = 'publish-mazdalink/MazdaLink.exe', [string]$Output = 'release')
$ErrorActionPreference = 'Stop'
$version = ([xml](Get-Content (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid release version.' }
$sdk = (Get-Content (Join-Path $PSScriptRoot '../global.json') -Raw | ConvertFrom-Json).sdk.version
$runtime = ([xml](Get-Content (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw)).Project.PropertyGroup.RuntimeFrameworkVersion
$repository = git rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the repository root.' }
$commit = git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'Cannot identify release source.' }
git -C $repository diff --quiet HEAD --
if ($LASTEXITCODE -ne 0) { throw 'Tracked source differs from the release commit.' }
$folder = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $folder) { throw 'Release destination must be new.' }
New-Item -ItemType Directory -Path $folder | Out-Null
Copy-Item -LiteralPath $Executable -Destination (Join-Path $folder 'MazdaLink.exe')
$archiveName = "MazdaLink-v$version-source.zip"
$archive = Join-Path $folder $archiveName
git -C $repository archive --format=zip --prefix=MazdaLink/ -o $archive HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
& (Join-Path $PSScriptRoot 'Verify-SourceArchive.ps1') -Archive $archive -Repository $repository -Commit $commit -Prefix 'MazdaLink/'
$hashes = @{}
foreach ($name in @('MazdaLink.exe', $archiveName)) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest = [ordered]@{ schemaVersion=10; version=$version; tag="v$version"; sourceCommit=$commit; sdk=$sdk; runtime=$runtime; architecture='win-x86'; files=$hashes; ecuProgrammingEnabled=$false; programmingFamily='None'; readIdentityPolicy='reported-calibration'; checksumSoftwareId='ZY87EC000ZYV5050'; benchRecoveryCheck='ZYJ2EA000ZYV5050-identification-only'; realEcuAcceptance='unqualified'; ramSelfTest='ZYJ2EA000ZYV5050-fixed-volatile-only'; ramSelfTestHardwareAcceptance='unqualified'; ramSelfTestImageSha256='34bd2a95b30d2a34f8ccc6ccc5e3486cac2d7ecc5f8c31720e7a0a2594c1cad0'; protectedFlashInitialization='ZYJ2EA000ZYV5050-fixed-protected-only'; protectedInitializationHardwareAcceptance='unqualified'; protectedInitializationImageSha256='85ccb46064aa483d64f53b5666017b57b2624ea015fcae0299ef5f336587925b'; recoveryBindingCheck='ZYJ2EA000ZYV5050-preserved-boot-read-only'; recoveryBindingHardwareAcceptance='unqualified'; recoveryBindingPreservedBytes=28928; writeEnableControl='ZYJ2EA000ZYV5050-fixed-gpio-only'; writeEnableControlHardwareAcceptance='unqualified'; writeEnableControlImageSha256='5d6a904f85c225e12f29d171fcc1c1a88fc1bd27ac55ee5a35083b73909326c9'; recoveryRehearsal='ZYJ2EA000ZYV5050-fixed-initialization-ram-service'; recoveryRehearsalHardwareAcceptance='unqualified'; recoveryRehearsalImageSha256='3d6d56b24d9e2629418beda03b40cc436d168cd47bf33754c759a7c6a00f1623'; originalRestorePlan='ZYJ2EA000ZYV5050-original-EB14-offline-only'; nativeTransactionTrial='ZYJ2EA000ZYV5050-fixed-read-ram-stage-only'; nativeTransactionHardwareAcceptance='unqualified'; nativeTransactionImageSha256='3ef235e86c0d603f697615e3810580fc9028a2fcb6d83cdba03dfcf89d965ee8'; nativeTransactionImageBytes=2028; nativeFlashCandidate='offline-model-only'; originalRestoreTransaction='unchanged-original-durable-intent-offline-only' }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'release.json') -Encoding utf8NoBOM
$hashes['release.json'] = (Get-FileHash -LiteralPath (Join-Path $folder 'release.json') -Algorithm SHA256).Hash.ToLowerInvariant()
@($hashes.Keys | Sort-Object | ForEach-Object { "$($hashes[$_])  $_" }) | Set-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.txt') -Encoding utf8NoBOM
Write-Output "Packaged exact source commit $commit for v$version."
