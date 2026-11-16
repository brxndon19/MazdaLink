param([Parameter(Mandatory)][string]$Archive, [Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Commit, [string]$Prefix = 'SpeedLink/')
$ErrorActionPreference = 'Stop'
$tracked = @(git -C $Repository -c core.quotepath=false ls-tree -r '--format=%(objectname) %(path)' $Commit)
if ($LASTEXITCODE -ne 0 -or !$tracked.Count) { throw 'Cannot enumerate the release source.' }
$expected = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
foreach ($line in $tracked) { $parts=$line -split ' ',2; $expected.Add($Prefix+$parts[1],$parts[0]) }
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Archive))
try {
    $files = @($zip.Entries | Where-Object { !$_.FullName.EndsWith('/') })
    if ($files.Count -ne $expected.Count) { throw 'Source archive does not contain every tracked file.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $files) {
        if (!$expected.ContainsKey($entry.FullName) -or !$seen.Add($entry.FullName)) { throw 'Source archive contains an unexpected or duplicate path.' }
        $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
        $stream = $entry.Open()
        try {
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes("blob $($entry.Length)`0"))
            $buffer = [byte[]]::new(65536)
            while (($count=$stream.Read($buffer,0,$buffer.Length)) -gt 0) { $hash.AppendData($buffer,0,$count) }
            $actual = [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
            if ($actual -ne $expected[$entry.FullName]) { throw 'Archived source differs from the source commit.' }
        } finally { $stream.Dispose(); $hash.Dispose() }
    }
    Write-Output "Complete source archive verified: $($files.Count) tracked files and their Git blob hashes."
} finally { $zip.Dispose() }
