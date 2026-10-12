# Copyright (c) 2026 Rinko
# SPDX-License-Identifier: GPL-3.0-only
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [switch]$ArchiveLegacy,
    [switch]$NoShortcut
)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $rootPath 'src/CodexProxyManager/AppInfo.cs')) -or
    -not (Test-Path -LiteralPath (Join-Path $rootPath 'scripts/build.ps1'))) { throw 'Expected a manager source checkout.' }
$prefix = $rootPath + '\'
function CheckedPath([string]$Relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $rootPath $Relative))
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Migration target outside checkout.' }
    $cursor = $path
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked migration paths are not supported.' }
        $cursor = Split-Path -Parent $cursor
    }
    return $path
}
function CheckTree([string]$Path) {
    $queue = New-Object 'Collections.Generic.Queue[string]'
    $queue.Enqueue($Path)
    while ($queue.Count) {
        $item = Get-Item -LiteralPath $queue.Dequeue() -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked legacy files cannot be archived.' }
        if ($item.PSIsContainer) { Get-ChildItem -LiteralPath $item.FullName -Force | ForEach-Object { $queue.Enqueue($_.FullName) } }
    }
}
function AssertStopped {
    # Missing process paths are treated conservatively, not as evidence of exit.
    $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.Name -like 'CodexProxyManager*.exe' -or $_.Name -eq 'mihomo.exe'
    })
    foreach ($process in $processes) {
        if (-not $process.ExecutablePath -or $process.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Exit the manager from its tray menu and stop its core before archiving; no process was terminated.'
        }
    }
}
$appExe = CheckedPath 'app/CodexProxyManager.exe'
if (-not (Test-Path -LiteralPath $appExe)) { throw 'Build the application before migration.' }
if ($ArchiveLegacy) { AssertStopped }
# Copy only immutable runtime inputs; never merge settings, caches or logs.
$runtimeFiles = @('mihomo.exe','data/GeoSite.dat','data/geoip.metadb')
foreach ($name in $runtimeFiles) {
    $source = CheckedPath ('runtime/' + $name)
    $destination = CheckedPath ('app/runtime/' + $name)
    if ((Test-Path -LiteralPath $source) -and (Test-Path -LiteralPath $destination) -and
        (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
        throw 'Existing app runtime differs; refusing to overwrite it. Resolve the version choice manually.'
    }
}
$copied = 0
foreach ($name in $runtimeFiles) {
    $source = CheckedPath ('runtime/' + $name)
    $destination = CheckedPath ('app/runtime/' + $name)
    if ((Test-Path -LiteralPath $source) -and -not (Test-Path -LiteralPath $destination)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
        if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw 'Runtime copy hash mismatch.' }
        $copied++
    }
}
$shortcut = CheckedPath 'Codex Proxy Manager.lnk'
if (-not $NoShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    if ((Test-Path -LiteralPath $shortcut) -and $link.TargetPath -ne $appExe) { throw 'Existing shortcut points elsewhere; refusing to overwrite.' }
    $link.TargetPath = $appExe
    $link.WorkingDirectory = Split-Path -Parent $appExe
    $link.Arguments = ''
    $link.Description = 'Codex Proxy Manager - manual application launch'
    $link.Save()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}
$archived = 0
if ($ArchiveLegacy) {
    $legacy = @('runtime','dist','manager')
    $legacy += @(Get-ChildItem -LiteralPath $rootPath -File | Where-Object { $_.Name -match '^CodexProxyManager\.v\d+\.\d+\.\d+\.exe$' } | ForEach-Object { $_.Name })
    # Character codes keep this script compatible with Windows PowerShell 5.1.
    $oldLink = 'Codex ' + [char]0x4E13 + [char]0x7528 + [char]0x4EE3 + [char]0x7406 + '.lnk'
    $legacy += $oldLink
    $targets = @($legacy | ForEach-Object { CheckedPath $_ } | Where-Object { Test-Path -LiteralPath $_ })
    foreach ($path in $targets) { CheckTree $path }
    if ($targets.Count) {
        $archiveRelative = 'artifacts/legacy-layout/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
        $archive = CheckedPath $archiveRelative
        # All destinations are checked before moving; the archive is recoverable.
        foreach ($path in $targets) {
            $destination = CheckedPath ($archiveRelative + '/' + [IO.Path]::GetFileName($path))
            if (Test-Path -LiteralPath $destination) { throw 'Archive destination already exists.' }
        }
        New-Item -ItemType Directory -Path $archive | Out-Null
        AssertStopped
        foreach ($path in $targets) {
            Move-Item -LiteralPath $path -Destination (Join-Path $archive ([IO.Path]::GetFileName($path)))
            $archived++
        }
    }
}
[pscustomobject]@{RuntimeFilesCopied=$copied;LegacyEntriesArchived=$archived;ShortcutPrepared=(-not $NoShortcut);UserDataChanged=$false}
