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
if ($help -notmatch 'ZY87EC000ZYV5050' -or $help -match 'SpeedLink' -or $menu -notmatch 'Flashing is unavailable' -or $flash -notmatch 'No adapter was opened' -or $status -notmatch 'Other-vehicle workflows: absent') { throw 'Unexpected standalone product scope.' }
$runtime = ([xml](Get-Content 'Directory.Build.props' -Raw)).Project.PropertyGroup.RuntimeFrameworkVersion
$version = ([xml](Get-Content 'Directory.Build.props' -Raw)).Project.PropertyGroup.Version
if ($status -notmatch 'Process: 32-bit' -or !$status.Contains("Runtime: $runtime") -or !$status.Contains("MazdaLink $version")) { throw 'Standalone runtime, architecture or version differs from the pins.' }
@{passed=$true;product='MazdaLink';targetSoftwareId='ZY87EC000ZYV5050';programmingEnabled=$false;hardwareAccessed=$false;menu=$true;overrideRefused=$true;flashRefusedBeforeDriverOpen=$true} | ConvertTo-Json | Set-Content (Join-Path $root 'report.json') -Encoding utf8NoBOM
