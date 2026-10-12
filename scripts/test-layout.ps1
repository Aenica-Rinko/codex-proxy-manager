# Copyright (c) 2026 Rinko
# SPDX-License-Identifier: GPL-3.0-only
$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('CodexProxyLayoutTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$probe = $null
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
try {
    foreach ($relative in @('src/CodexProxyManager/AppInfo.cs','scripts/build.ps1','app/CodexProxyManager.exe',
        'runtime/mihomo.exe','runtime/data/GeoSite.dat','runtime/data/geoip.metadb',
        'runtime/data/cache.db','dist/old.zip','CodexProxyManager.v0.10.2.exe')) {
        $path = Join-Path $scratch $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, 'isolated fixture: ' + $relative)
    }
    & (Join-Path $PSScriptRoot 'migrate-layout.ps1') -Root $scratch -NoShortcut | Out-Null
    foreach ($relative in @('mihomo.exe','data/GeoSite.dat','data/geoip.metadb')) {
        Check ((Get-FileHash -LiteralPath (Join-Path $scratch ('runtime/'+$relative))).Hash -eq
            (Get-FileHash -LiteralPath (Join-Path $scratch ('app/runtime/'+$relative))).Hash) 'Runtime copy differs'
    }
    Check (-not (Test-Path -LiteralPath (Join-Path $scratch 'app/runtime/data/cache.db'))) 'Cache copied into application'
    $result = & (Join-Path $PSScriptRoot 'migrate-layout.ps1') -Root $scratch -NoShortcut -ArchiveLegacy
    Check ($result.LegacyEntriesArchived -eq 3 -and -not $result.UserDataChanged) 'Unexpected migration result'
    Check (-not (Test-Path -LiteralPath (Join-Path $scratch 'runtime'))) 'Legacy runtime not archived'
    $archives = @(Get-ChildItem -LiteralPath (Join-Path $scratch 'artifacts/legacy-layout') -Directory)
    Check ($archives.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $archives[0].FullName 'runtime/data/cache.db'))) 'Legacy backup incomplete'
    $result = & (Join-Path $PSScriptRoot 'migrate-layout.ps1') -Root $scratch -NoShortcut -ArchiveLegacy
    Check ($result.LegacyEntriesArchived -eq 0 -and $result.RuntimeFilesCopied -eq 0) 'Migration is not idempotent'
    New-Item -ItemType Directory -Path (Join-Path $scratch 'runtime') | Out-Null
    [IO.File]::WriteAllText((Join-Path $scratch 'runtime/mihomo.exe'),'different runtime')
    $before = (Get-FileHash -LiteralPath (Join-Path $scratch 'app/runtime/mihomo.exe')).Hash
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'migrate-layout.ps1') -Root $scratch -NoShortcut | Out-Null } catch { $rejected=$true }
    Check ($rejected -and $before -eq (Get-FileHash -LiteralPath (Join-Path $scratch 'app/runtime/mihomo.exe')).Hash) 'Conflicting runtime overwritten'
    # Use a linked legacy dist to verify the preflight never follows junctions.
    Copy-Item -LiteralPath (Join-Path $scratch 'app/runtime/mihomo.exe') -Destination (Join-Path $scratch 'runtime/mihomo.exe')
    $link = Join-Path $scratch 'dist'
    New-Item -ItemType Junction -Path $link -Target (Join-Path $scratch 'app') | Out-Null
    try {
        $rejected=$false
        try { & (Join-Path $PSScriptRoot 'migrate-layout.ps1') -Root $scratch -NoShortcut -ArchiveLegacy | Out-Null } catch { $rejected=$true }
        Check ($rejected -and (Test-Path -LiteralPath (Join-Path $scratch 'runtime/mihomo.exe'))) 'Linked tree accepted or partial archive occurred'
    } finally { [IO.Directory]::Delete($link) }
    # Isolated, network-free stand-in verifies active-process refusal.
    $probeSource = Join-Path $scratch 'probe.cs'
    [IO.File]::WriteAllText($probeSource,'class Probe { static void Main() { System.Threading.Thread.Sleep(60000); } }')
    $probePath = Join-Path $scratch 'CodexProxyManager.v9.9.9.exe'
    & "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /out:$probePath $probeSource
    if ($LASTEXITCODE -ne 0) { throw 'Process fixture compilation failed' }
    $probe = Start-Process -FilePath $probePath -WindowStyle Hidden -PassThru
    $rejected=$false
    try { & (Join-Path $PSScriptRoot 'migrate-layout.ps1') -Root $scratch -NoShortcut -ArchiveLegacy | Out-Null } catch { $rejected=$true }
    Check ($rejected -and -not $probe.HasExited -and (Test-Path -LiteralPath $probePath)) 'Active manager not protected'
    $probe.Kill(); $probe.WaitForExit(); $probe.Dispose(); $probe=$null
    Write-Output 'PASS layout migration: runtime hashes, no cache merge, recoverable archive, idempotence, conflict/link rejection, active process preserved'
}
finally {
    if ($probe) { if (-not $probe.HasExited) { $probe.Kill(); $probe.WaitForExit() }; $probe.Dispose() }
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved).StartsWith('CodexProxyLayoutTest-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
