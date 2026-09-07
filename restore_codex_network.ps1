$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
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
    $current = Get-ItemProperty -LiteralPath $internetSettings
    $stateFile = @($userStateFile, $managerStateFile, $legacyStateFile) |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if ($stateFile -and ((Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json).NetworkMode -eq 'process')) {
        Remove-Item -LiteralPath $stateFile -Force
        $message = 'Process-only session cleared. Windows proxy settings were left unchanged.'
    }
    elseif ($stateFile -and [int]$current.ProxyEnable -eq 1 -and [string]$current.ProxyServer -eq $managedProxy) {
        $saved = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
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
        Remove-Item -LiteralPath $stateFile -Force
        $message = 'The saved Windows proxy setting was restored.'
    }
    elseif ([int]$current.ProxyEnable -eq 1 -and [string]$current.ProxyServer -eq $managedProxy) {
        Set-ItemProperty -LiteralPath $internetSettings -Name ProxyEnable -Type DWord -Value 0
        $message = 'No saved state existed, so the leftover Codex proxy was disabled.'
    }
    else {
        $message = 'No Codex proxy state needed restoration.'
    }

    if ($message -notlike 'Process-only*') { Refresh-InternetSettings }

    $projectCorePath = Join-Path $projectRoot 'runtime\mihomo.exe'
    $projectCores = Get-CimInstance Win32_Process -Filter "Name='mihomo.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine.IndexOf($projectCorePath, [StringComparison]::OrdinalIgnoreCase) -ge 0
        }
    foreach ($process in $projectCores) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        $message += ' The project Mihomo process was stopped.'
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
        $_.Exception.Message,
        'Restore Codex Network',
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Error
    ) | Out-Null
}
