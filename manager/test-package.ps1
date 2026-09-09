$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-common.ps1')
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('CodexProxyPackageTest-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $scratch 'fixture'
New-Item -ItemType Directory -Path (Join-Path $fixture 'manager') -Force | Out-Null
try {
    $docs = @('LICENSE','README.md','QUICKSTART.md','USER_GUIDE.md','DEVELOPMENT.md','PRIVACY.md','ARCHITECTURE.md',
        'RELEASE_CHECKLIST.md','RELEASE_NOTES.md','ACCEPTANCE.md','THIRD_PARTY_NOTICES.md',
        'config.example.yaml','Restore Codex Network.cmd','restore_codex_network.ps1')
    foreach ($name in $docs) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $fixture $name) }
    foreach ($name in $ManagerReleaseSourceFiles) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $fixture ('manager\' + $name))
    }
    # Canary files must never appear in the archive, even when inside the source tree.
    foreach ($name in @('settings.json','node-preferences.json','manager.log','CodexProxyManager.old.exe',
        'profiles\private.yaml','runtime\mihomo.exe','runtime\data\GeoSite.dat','manager\unlisted.cs','unlisted.txt')) {
        $path = Join-Path $fixture $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, 'PRIVATE_CANARY_NOT_FOR_RELEASE')
    }
    & (Join-Path $fixture 'manager\package.ps1') | Out-Null
    $archivePath = Join-Path $fixture ('dist\CodexProxyManager-v' + (Get-ManagerVersion) + '-manager-only-preview.zip')
    $hashLine = [IO.File]::ReadAllText($archivePath + '.sha256').Split(' ')[0]
    if ($hashLine -ne (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'ZIP hash mismatch' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $expected = $docs + @($ManagerReleaseSourceFiles | ForEach-Object { 'manager/' + $_ }) +
            ('CodexProxyManager.v' + (Get-ManagerVersion) + '.exe') + 'manifest.json'
        $entryNames = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        if (@(Compare-Object ($expected | Sort-Object) ($entryNames | Sort-Object)).Count -ne 0) { throw 'Unexpected archive contents' }
        $reader = New-Object IO.StreamReader($archive.GetEntry('manifest.json').Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($manifest.version -ne (Get-ManagerVersion) -or $manifest.includesRuntime -ne $false -or $manifest.files.Count -ne ($expected.Count - 1)) { throw 'Invalid release metadata' }
        if ($manifest.license -ne 'GPL-3.0-only' -or $manifest.author -ne 'Rinko' -or
            $manifest.includesCorrespondingSource -ne $true -or $manifest.sourceDirectory -ne 'manager') { throw 'Invalid license/source metadata' }
        $reader = New-Object IO.StreamReader($archive.GetEntry('LICENSE').Open())
        try { $licenseText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($licenseText -ne [IO.File]::ReadAllText((Join-Path $root 'LICENSE')) -or
            $licenseText.Length -lt 35000 -or $licenseText -notmatch 'END OF TERMS AND CONDITIONS') { throw 'Missing or incomplete GPL license' }
        foreach ($record in $manifest.files) {
            $entry = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $record.path })[0]
            if (-not $entry) { throw 'Manifest refers to missing entry' }
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($actual -ne $record.sha256) { throw 'Manifest hash mismatch' }
        }
    }
    finally { $archive.Dispose() }
    # Recipients must be able to compile using only the delivered source snapshot.
    # Use character codes so Windows PowerShell 5.1 works even with BOM-less source.
    $unicodeDirectory = ([string][char]0x4E2D) + [char]0x6587 + ' recipient'
    $extracted = Join-Path $scratch $unicodeDirectory
    [IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extracted)
    $rebuilt = Join-Path $scratch 'rebuilt'
    New-Item -ItemType Directory -Path $rebuilt | Out-Null
    & (Join-Path $extracted 'manager\build.ps1') -OutputDirectory $rebuilt | Out-Null
    $exeName = 'CodexProxyManager.v' + (Get-ManagerVersion) + '.exe'
    foreach ($directory in @($extracted, $rebuilt)) {
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $directory $exeName))
        if ($info.LegalCopyright -ne 'Copyright (c) 2026 Rinko' -or $info.FileVersion -ne ((Get-ManagerVersion) + '.0')) { throw 'Missing author or mismatched rebuilt version' }
    }
    $before = (Get-FileHash -LiteralPath $archivePath).Hash
    $rejected = $false
    try { & (Join-Path $fixture 'manager\package.ps1') | Out-Null } catch { $rejected = $true }
    if (-not $rejected -or (Get-FileHash -LiteralPath $archivePath).Hash -ne $before) { throw 'Existing package overwritten' }
    # A credential accidentally placed in an allowed document must block output.
    $syntheticKey = 'gh' + 'p_' + ('Z' * 36)
    [IO.File]::AppendAllText((Join-Path $fixture 'README.md'), ("`n" + $syntheticKey))
    $blockedOutput = Join-Path $scratch 'blocked-output'
    $message = ''
    try { & (Join-Path $fixture 'manager\package.ps1') -OutputDirectory $blockedOutput | Out-Null }
    catch { $message = $_.Exception.Message }
    if (-not $message.Contains('Release safety check failed') -or $message.Contains($syntheticKey)) { throw 'Credential did not block packaging safely' }
    if (@(Get-ChildItem -LiteralPath $blockedOutput -File).Count -ne 0) { throw 'Rejected package left a release artifact' }
    Write-Output 'PASS packaging: GPL license, Rinko attribution, Chinese-path source rebuild, exact allowlist, private/runtime canary exclusion, metadata, file/ZIP hashes, overwrite refusal and redacted credential gate'
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved).StartsWith('CodexProxyPackageTest-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
