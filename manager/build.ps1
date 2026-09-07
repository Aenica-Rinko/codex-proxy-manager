param(
    [string]$OutputName,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $PSScriptRoot 'build-common.ps1')
if (-not $OutputName) { $OutputName = 'CodexProxyManager.v' + (Get-ManagerVersion) + '.exe' }
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or $OutputName -notmatch '\.exe$') { throw 'OutputName must be an EXE filename, not a path.' }
if (-not $OutputDirectory) { $OutputDirectory = $projectRoot }
$sources = $ManagerSourceFiles | ForEach-Object { Join-Path $PSScriptRoot $_ }
$output = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path $OutputName
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 `
    /out:$output `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Management.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    $sources

if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) {
    throw 'Codex Proxy Manager build failed.'
}

Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
