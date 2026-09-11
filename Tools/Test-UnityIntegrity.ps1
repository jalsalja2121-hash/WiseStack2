$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $issues = [System.Collections.Generic.List[string]]::new()
    $assetGuids = @{}
    $knownGuids = @{}
    $roots = @('Assets', 'Packages')
    if (Test-Path 'Library/PackageCache') { $roots += 'Library/PackageCache' }
    foreach ($line in (& rg --no-heading --color never '^guid: [a-f0-9]{32}' @roots -g '*.meta')) {
        if ($line -notmatch '^(.+\.meta):guid: ([a-f0-9]{32})') { continue }
        $path = $Matches[1]; $guid = $Matches[2]
        $knownGuids[$guid] = $true
        if ($path -notmatch '^Assets[/\\]') { continue }
        if ($assetGuids.ContainsKey($guid)) { $issues.Add("Duplicate GUID: $path and $($assetGuids[$guid])") }
        $assetGuids[$guid] = $path
        if (-not (Test-Path -LiteralPath $path.Substring(0, $path.Length - 5))) { $issues.Add("Orphan meta: $path") }
    }
    foreach ($path in (& rg --files Assets -g '!*.meta')) {
        if (-not (Test-Path -LiteralPath ($path + '.meta'))) { $issues.Add("Missing meta: $path") }
    }
    foreach ($line in (& rg --no-heading --color never 'guid: [a-f0-9]{32}' Assets ProjectSettings -g '*.unity' -g '*.prefab' -g '*.asset' -g '*.mat')) {
        foreach ($match in [regex]::Matches($line, 'guid: ([a-f0-9]{32})')) {
            $guid = $match.Groups[1].Value
            if ($guid -notmatch '^0000000000000000' -and -not $knownGuids.ContainsKey($guid)) {
                $path = $line.Substring(0, $line.IndexOf(':'))
                $issues.Add("Unresolved GUID: $path -> $guid")
            }
        }
    }
    foreach ($path in (& rg -l '^<<<<<<< |^=======\s*$|^>>>>>>> |^version https://git-lfs.github.com/spec/v1' Assets Packages ProjectSettings)) {
        $issues.Add("Conflict marker or unresolved LFS pointer: $path")
    }
    Get-Content Packages/manifest.json -Raw | ConvertFrom-Json | Out-Null
    Get-Content Packages/packages-lock.json -Raw | ConvertFrom-Json | Out-Null
    if ($issues.Count -gt 0) {
        $issues | Sort-Object -Unique | Write-Output
        throw "Unity integrity audit found $($issues.Count) issues."
    }
    Write-Output "PASS: $($assetGuids.Count) asset GUIDs; metadata, YAML GUID references, JSON, conflict markers and LFS pointers checked."
}
finally { Pop-Location }
