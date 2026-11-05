param([string]$Executable = 'publish/SpeedLink.exe', [string]$Output = 'release')
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
Copy-Item -LiteralPath $Executable -Destination (Join-Path $folder 'SpeedLink.exe')
$archiveName = "SpeedLink-v$version-source.zip"
$archive = Join-Path $folder $archiveName
git -C $repository archive --format=zip --prefix=SpeedLink/ -o $archive HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
& (Join-Path $PSScriptRoot 'Verify-SourceArchive.ps1') -Archive $archive -Repository $repository -Commit $commit
$hashes = @{}
foreach ($name in @('SpeedLink.exe', $archiveName)) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest = [ordered]@{ schemaVersion=1; version=$version; tag="v$version"; sourceCommit=$commit; sdk=$sdk; runtime=$runtime; architecture='win-x86'; files=$hashes; ecuProgrammingEnabled=$true; programmingFamily='Gen1'; realEcuAcceptance='limited-bench' }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'release.json') -Encoding utf8NoBOM
$hashes['release.json'] = (Get-FileHash -LiteralPath (Join-Path $folder 'release.json') -Algorithm SHA256).Hash.ToLowerInvariant()
@($hashes.Keys | Sort-Object | ForEach-Object { "$($hashes[$_])  $_" }) | Set-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.txt') -Encoding utf8NoBOM
Write-Output "Packaged exact source commit $commit for v$version."
