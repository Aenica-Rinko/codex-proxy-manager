param(
    [string]$OutputName,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $PSScriptRoot 'build-common.ps1')
if (-not $OutputName) { $OutputName = 'CodexProxyManager.exe' }
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or $OutputName -notmatch '^[\p{L}\p{N}_. -]+\.exe$') { throw 'OutputName must be a safe EXE filename, not a path or alternate stream.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'app' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
if ($OutputDirectory.TrimEnd('\') -eq $projectRoot.TrimEnd('\')) {
    throw 'Build into app or another output directory, not the source root; source documentation must not be overwritten.'
}
foreach ($directory in @('.git','.github','src','tests','scripts','docs','examples','resources','tools')) {
    $protected = Join-Path $projectRoot $directory
    if ($OutputDirectory -eq $protected -or $OutputDirectory.StartsWith($protected + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Build output must be separate from source directories.'
    }
}
$outputAncestor = $OutputDirectory
while ($outputAncestor) {
    if ((Get-Item -LiteralPath $outputAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Build output must not pass through linked directories.'
    }
    $outputAncestor = Split-Path -Parent $outputAncestor
}
$sources = $ManagerSourceFiles | ForEach-Object { Join-Path $projectRoot $_ }
$output = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path $OutputName
if ((Test-Path -LiteralPath $output) -and ((Get-Item -LiteralPath $output -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Build output must not replace a linked executable.'
}
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
# Ship help beside the app; never copy private runtime or user data.
$support = [ordered]@{'README.md'='resources/package-README.md'; 'LICENSE'='LICENSE'}
foreach ($name in (@($ManagerDocs) + @($ManagerTools))) { $support[$name] = $name }
foreach ($entry in $support.GetEnumerator()) {
    $destination = Join-Path $OutputDirectory $entry.Key
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot $entry.Value) -Destination $destination
}
