param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$userDataRoot = Join-Path $env:LOCALAPPDATA 'CodexProxyManager'
$userSettingsFile = Join-Path $userDataRoot 'settings.json'
$userStateFile = Join-Path $userDataRoot 'manager-state.json'
$managerStateFile = Join-Path $projectRoot 'manager-state.json'
$legacyStateFile = Join-Path $projectRoot 'proxy-state.json'
$managedProxy = '127.0.0.1:17890'
$managedPort = 17890
$internetSettings = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
$message = ''

if (Test-Path -LiteralPath $userSettingsFile) {
    try {
        $settings = Get-Content -LiteralPath $userSettingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([int]$settings.MixedPort -ge 1024 -and [int]$settings.MixedPort -le 65535) {
            $managedPort = [int]$settings.MixedPort
            $managedProxy = '127.0.0.1:{0}' -f $managedPort
        }
    }
    catch { }
}

function Refresh-InternetSettings {
    if (-not ('CodexProxyRestore.WinInet' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace CodexProxyRestore {
    public static class WinInet {
        [DllImport("wininet.dll", SetLastError = true)]
        public static extern bool InternetSetOption(
            IntPtr hInternet,
            int dwOption,
            IntPtr lpBuffer,
            int dwBufferLength);
    }
}
'@
    }
    [CodexProxyRestore.WinInet]::InternetSetOption([IntPtr]::Zero, 39, [IntPtr]::Zero, 0) | Out-Null
    [CodexProxyRestore.WinInet]::InternetSetOption([IntPtr]::Zero, 37, [IntPtr]::Zero, 0) | Out-Null
}

try {
    # A live manager owns its session; recovery must not race its watchdog/lifecycle.
    $managerPaths = @((Join-Path $projectRoot 'CodexProxyManager.exe'), (Join-Path $projectRoot 'app\CodexProxyManager.exe'))
    $liveManagers = @(Get-CimInstance Win32_Process -Filter "Name='CodexProxyManager.exe'" -ErrorAction Stop |
        Where-Object { -not $_.ExecutablePath -or $_.ExecutablePath -in $managerPaths })
    if ($CheckOnly) {
        [pscustomobject]@{ LiveManagerDetected = ($liveManagers.Count -gt 0); StateExists = (Test-Path -LiteralPath $userStateFile); Changed = $false }
        return
    }
    if ($liveManagers.Count) { throw 'Exit the manager through its tray menu before recovery.' }
    $current = Get-ItemProperty -LiteralPath $internetSettings
    $stateFile = @($userStateFile, $managerStateFile, $legacyStateFile) |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    $saved = $null
    if ($stateFile) {
        $stateItem = Get-Item -LiteralPath $stateFile -ErrorAction Stop
        if ($stateItem.PSIsContainer -or $stateItem.Length -gt 65536) { throw 'Invalid state file.' }
        $cursor = $stateItem.FullName
        while ($cursor) {
            if ((Get-Item -LiteralPath $cursor -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked state path.' }
            $cursor = Split-Path -Parent $cursor
        }
        $saved = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    }
    if ($stateFile -and $saved.NetworkMode -eq 'process') {
        $message = 'Process-only state found. Windows proxy settings were left unchanged; session state retained for verified manager cleanup.'
    }
    elseif ($stateFile -and [int]$current.ProxyEnable -eq 1 -and [string]$current.ProxyServer -eq $managedProxy) {
        Set-ItemProperty -LiteralPath $internetSettings -Name ProxyEnable -Type DWord -Value ([int]$saved.ProxyEnable)
        if ([bool]$saved.HasProxyServer) {
            Set-ItemProperty -LiteralPath $internetSettings -Name ProxyServer -Value ([string]$saved.ProxyServer)
        }
        else {
            Remove-ItemProperty -LiteralPath $internetSettings -Name ProxyServer -ErrorAction SilentlyContinue
        }
        if ([bool]$saved.HasAutoConfigURL) {
            Set-ItemProperty -LiteralPath $internetSettings -Name AutoConfigURL -Value ([string]$saved.AutoConfigURL)
        }
        else {
            Remove-ItemProperty -LiteralPath $internetSettings -Name AutoConfigURL -ErrorAction SilentlyContinue
        }
        $message = 'The saved Windows proxy setting was restored. State retained for verified manager cleanup.'
    }
    elseif ([int]$current.ProxyEnable -eq 1 -and [string]$current.ProxyServer -eq $managedProxy) {
        Set-ItemProperty -LiteralPath $internetSettings -Name ProxyEnable -Type DWord -Value 0
        $message = 'No saved state existed, so the leftover Codex proxy was disabled.'
    }
    else {
        $message = 'No Codex proxy state needed restoration.'
    }

    if ($message -notlike 'Process-only*') { Refresh-InternetSettings }

    # A source checkout uses app/runtime; a portable package uses runtime.
    $projectCorePaths = @((Join-Path $projectRoot 'runtime\mihomo.exe'),
        (Join-Path $projectRoot 'app\runtime\mihomo.exe'))
    $projectCores = Get-CimInstance Win32_Process -Filter "Name='mihomo.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $saved -and $_.ExecutablePath -and $_.ExecutablePath -in $projectCorePaths -and
            $_.ExecutablePath -eq $saved.CoreExecutablePath -and $_.ProcessId -eq [int]$saved.CorePid
        }
    foreach ($process in $projectCores) {
        $candidate = $null
        try {
            $candidate = Get-Process -Id $process.ProcessId -ErrorAction Stop
            # Recheck identity instead of trusting a possibly reused CIM PID.
            if ($candidate.Path -eq $process.ExecutablePath -and
                $candidate.StartTime.ToUniversalTime().Ticks -eq [long]$saved.CoreStartUtcTicks -and
                [Math]::Abs(($candidate.StartTime.ToUniversalTime() - $process.CreationDate.ToUniversalTime()).Ticks) -lt 10) {
                Stop-Process -InputObject $candidate -ErrorAction Stop
                $message += ' The project Mihomo process was stopped.'
            }
        }
        catch { }
        finally { if ($null -ne $candidate) { $candidate.Dispose() } }
    }

    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        $message,
        'Restore Codex Network',
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Information
    ) | Out-Null
}
catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        'Recovery could not complete. No raw configuration or private error details are shown. Check local permissions and manager state.',
        'Restore Codex Network',
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Error
    ) | Out-Null
}
