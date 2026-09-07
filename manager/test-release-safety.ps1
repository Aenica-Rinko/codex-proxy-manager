# Copyright (c) 2026 Rinko
# SPDX-License-Identifier: GPL-3.0-only
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-safety.ps1')
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('CodexProxySafetyTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
try {
    $fixture = Join-Path $scratch 'fixture.txt'
    [IO.File]::WriteAllText($fixture, "Safe text`nhttp://127.0.0.1:1080`nhttps://example.com/docs")
    Assert-ReleaseSafety -Root $scratch -RelativePaths @('fixture.txt')
    # Synthetic fixtures are assembled so this test's own source has no apparent credential.
    $samples = @(
        @('private-key', ('-----BEGIN ' + 'OPENSSH PRIVATE KEY-----')),
        @('github-token', ('gh' + 'p_' + ('A' * 36))),
        @('api-key', ('s' + 'k-proj-' + ('B' * 48))),
        @('proxy-share-link', ('vl' + 'ess://' + ('c' * 32) + '@example.com:443')),
        @('url-userinfo', ('https://' + 'fixture-user:fixture-password@' + 'example.com')),
        @('url-secret-query', ('https://example.com/sub?' + 'token=' + ('D' * 24)))
    )
    foreach ($sample in $samples) {
        [IO.File]::WriteAllText($fixture, ("safe first line`n" + $sample[1]))
        $findings = @(Get-ReleaseSafetyFindings -Root $scratch -RelativePaths @('fixture.txt'))
        Check (@($findings | Where-Object { $_.Rule -eq $sample[0] -and $_.Line -eq 2 }).Count -gt 0) 'Missed credential fixture or wrong line'
        $errorText = ''
        try { Assert-ReleaseSafety -Root $scratch -RelativePaths @('fixture.txt') } catch { $errorText = $_.Exception.Message }
        Check ($errorText.Contains('input #1') -and -not $errorText.Contains($sample[1]) -and -not $errorText.Contains($scratch)) 'Unsafe or missing failure report'
    }
    [IO.File]::WriteAllText($fixture, 'safe')
    foreach ($path in @('..\outside.txt', 'C:\outside.txt', 'fixture.txt:stream')) {
        $finding = @(Get-ReleaseSafetyFindings -Root $scratch -RelativePaths @($path))
        Check ($finding.Count -eq 1 -and $finding[0].Rule -eq 'invalid-path') 'Unsafe path accepted'
    }
    [IO.File]::WriteAllText((Join-Path $scratch 'settings.json'), '{}')
    $finding = @(Get-ReleaseSafetyFindings -Root $scratch -RelativePaths @('settings.json'))
    Check ($finding[0].Rule -eq 'private-or-runtime-input') 'Private filename accepted'
    $finding = @(Get-ReleaseSafetyFindings -Root $scratch -RelativePaths @('missing.md'))
    Check ($finding[0].Rule -eq 'unreadable-input') 'Missing input accepted'
    [IO.File]::WriteAllBytes($fixture, [byte[]]@(65,0,66))
    $finding = @(Get-ReleaseSafetyFindings -Root $scratch -RelativePaths @('fixture.txt'))
    Check ($finding[0].Rule -eq 'binary-content') 'Binary input accepted'
    # Directory junctions do not need administrator or Developer Mode privileges.
    $link = Join-Path $scratch 'linked'
    $target = Join-Path $scratch 'target'
    New-Item -ItemType Directory -Path $target | Out-Null
    [IO.File]::WriteAllText((Join-Path $target 'safe.txt'), 'safe')
    New-Item -ItemType Junction -Path $link -Target $target | Out-Null
    try {
        $finding = @(Get-ReleaseSafetyFindings -Root $scratch -RelativePaths @('linked/safe.txt'))
        Check ($finding[0].Rule -eq 'linked-input') 'Linked parent accepted'
    } finally { [IO.Directory]::Delete($link) }
    # Verify Git enumeration: Unicode paths survive -z, ignored local data stays
    # outside the candidate set, but force-tracked private data must fail closed.
    $gitFixture = Join-Path $scratch 'git-fixture'
    New-Item -ItemType Directory -Path $gitFixture | Out-Null
    & git init --quiet $gitFixture
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize audit fixture' }
    [IO.File]::WriteAllText((Join-Path $gitFixture '.gitignore'), "settings.json`n")
    [IO.File]::WriteAllText((Join-Path $gitFixture 'settings.json'), '{}')
    $unicodeFile = ([string][char]0x4E2D) + [char]0x6587 + ' safe.md'
    [IO.File]::WriteAllText((Join-Path $gitFixture $unicodeFile), 'safe')
    & (Join-Path $PSScriptRoot 'audit-release.ps1') -Root $gitFixture | Out-Null
    & git -C $gitFixture add --force -- settings.json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot stage audit fixture' }
    $message = ''
    try { & (Join-Path $PSScriptRoot 'audit-release.ps1') -Root $gitFixture | Out-Null }
    catch { $message = $_.Exception.Message }
    Check ($message.Contains('private-or-runtime-input')) 'Ignored but tracked private file bypassed audit'
    Write-Output 'PASS release safety: six credential families, safe examples, redacted failures, traversal/ADS, private names, missing/binary files, linked parents, Unicode Git paths and tracked ignored files'
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved).StartsWith('CodexProxySafetyTest-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
