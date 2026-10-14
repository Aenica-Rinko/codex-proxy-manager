param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-common.ps1')
. (Join-Path $PSScriptRoot 'release-safety.ps1')
$root = Split-Path -Parent $PSScriptRoot
$version = Get-ManagerVersion
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/releases' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$destination = (Resolve-Path -LiteralPath $OutputDirectory).Path
$packageName = Get-ManagerPackageName
$archivePath = Join-Path $destination ($packageName + '.zip')
$hashPath = $archivePath + '.sha256'
if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $hashPath)) { throw 'Package already exists; choose a new output directory or move the previous package first.' }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('CodexProxyPackage-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $scratch $packageName
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    # Explicit allowlist only: never recursively copy the workspace or runtime/user directories.
    $map = Get-PackageFileMap
    $files = @($map.Keys)
    Assert-ReleaseSafety -Root $root -RelativePaths $ManagerReleaseSourceFiles
    foreach ($entry in $map.GetEnumerator()) {
        $item = Get-Item -LiteralPath (Join-Path $root $entry.Value)
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Release inputs must be regular files.' }
        $destinationFile = Join-Path $stage $entry.Key
        New-Item -ItemType Directory -Path (Split-Path -Parent $destinationFile) -Force | Out-Null
        Copy-Item -LiteralPath $item.FullName -Destination $destinationFile
    }
    # Build from the exact source snapshot shipped beside the executable.
    Assert-ReleaseSafety -Root $stage -RelativePaths $files
    & (Join-Path $stage 'source/scripts/build.ps1') -OutputDirectory $stage | Out-Null
    $exe = 'CodexProxyManager.exe'
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage $exe)).FileVersion
    if ($fileVersion -ne ($version + '.0')) { throw 'Built version does not match release version.' }
    $manifest = [ordered]@{ version = $version; channel = (Get-ManagerPackageChannel); license = 'GPL-3.0-only';
        author = 'Rinko'; includesRuntime = $false; includesCorrespondingSource = $true; sourceDirectory = 'source'; files = @() }
    foreach ($name in ($files + $exe | Sort-Object)) {
        $manifest.files += [ordered]@{ path = $name; sha256 = (Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    [IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryZip = Join-Path $scratch 'package.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip)
    $hash = (Get-FileHash -LiteralPath $temporaryZip -Algorithm SHA256).Hash.ToLowerInvariant()
    # Copy only after staging/build/hash succeed. Do not overwrite existing release files.
    [IO.File]::Copy($temporaryZip, $archivePath, $false)
    [IO.File]::WriteAllText($hashPath, ($hash + '  ' + [IO.Path]::GetFileName($archivePath) + [Environment]::NewLine))
    Get-Item -LiteralPath $archivePath,$hashPath | Select-Object FullName,Length
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved).StartsWith('CodexProxyPackage-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
