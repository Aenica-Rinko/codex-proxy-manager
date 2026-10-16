param([switch]$UnitOnly, [string]$BrowserExecutable, [switch]$KeepArtifacts)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-common.ps1')
$root = Split-Path -Parent $PSScriptRoot
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('CodexProxyTestBuild-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
try {
    $output = Join-Path $testDir 'RegressionTests.exe'
    $yamlDependency = Get-VerifiedYamlDependency
    Copy-Item -LiteralPath $yamlDependency -Destination (Join-Path $testDir 'YamlDotNet.dll')
    $sources = (@($ManagerSourceFiles) + @($ManagerTestFiles)) |
        ForEach-Object { Join-Path $root $_ }
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 /main:CodexProxyManager.RegressionTests /out:$output `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Management.dll `
        /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:$yamlDependency $sources
    if ($LASTEXITCODE -ne 0) { throw 'Regression test build failed' }
    $applicationRoot = Join-Path $root 'app'
    if ($BrowserExecutable) { & $output --privacy-browser-selftest $BrowserExecutable }
    elseif ($UnitOnly) { & $output $applicationRoot --unit-only }
    else { & $output $applicationRoot }
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($testDir)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($KeepArtifacts) { Write-Output ('Retained test build: ' + $resolved) }
    elseif ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved).StartsWith('CodexProxyTestBuild-')) {
        # A fixture watchdog can briefly hold this EXE after the test parent exits.
        # Retry only our exact temporary build; never kill unrelated processes or hide failure.
        for ($cleanupAttempt = 0; $cleanupAttempt -lt 12; $cleanupAttempt++) {
            try { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop; break }
            catch {
                if ($cleanupAttempt -eq 11) { throw }
                Start-Sleep -Milliseconds 250
            }
        }
    }
}
