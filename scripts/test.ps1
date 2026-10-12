param([switch]$UnitOnly, [string]$BrowserExecutable)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-common.ps1')
$root = Split-Path -Parent $PSScriptRoot
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('CodexProxyTestBuild-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
try {
    $output = Join-Path $testDir 'RegressionTests.exe'
    $sources = (@($ManagerSourceFiles) + @($ManagerTestFiles)) |
        ForEach-Object { Join-Path $root $_ }
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 /main:CodexProxyManager.RegressionTests /out:$output `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Management.dll `
        /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll $sources
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
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
