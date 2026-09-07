# Copyright (c) 2026 Rinko
# SPDX-License-Identifier: GPL-3.0-only
# Read-only, conservative checks; no matched content is returned or printed.
function Get-ReleaseSafetyFindings {
    param([Parameter(Mandatory=$true)][string]$Root,
          [Parameter(Mandatory=$true)][AllowEmptyCollection()][string[]]$RelativePaths)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $prefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    $rules = [ordered]@{
        'private-key' = '-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED )?PRIVATE KEY-----'
        'github-token' = '\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b'
        'api-key' = '\bsk-(?:(?:proj|svcacct)-)?[A-Za-z0-9_-]{32,}\b'
        'proxy-share-link' = '(?i)\b(?:ss|ssr|vmess|vless|trojan|hysteria2?|hy2|tuic)://[A-Za-z0-9_+/%=-]{12,}'
        'url-userinfo' = '(?i)\b(?:https?|socks5?)://[^\s/"<>]+:[^\s/"<>]+@'
        'url-secret-query' = '(?i)https?://[^\s"<>]*[?&](?:token|api[_-]?key|password|secret)=[A-Za-z0-9_%+/-]{12,}'
    }
    $fileIndex = 0
    foreach ($relative in $RelativePaths) {
        $fileIndex++
        $rule = $null
        $text = $null
        try {
            if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or
                $relative -match '[:\x00-\x1f]' -or $relative -match '(^|[\\/])\.\.([\\/]|$)') {
                $rule = 'invalid-path'
            } else {
                $path = [IO.Path]::GetFullPath((Join-Path $rootPath $relative))
                if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $rule = 'outside-root' }
                $cursor = $path
                while (-not $rule) {
                    $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
                    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $rule = 'linked-input'; break }
                    if ($cursor -eq $rootPath) { break }
                    $cursor = Split-Path -Parent $cursor
                }
                if (-not $rule) {
                    $normalized = $relative.Replace('\', '/')
                    $leaf = [IO.Path]::GetFileName($path)
                    $extension = [IO.Path]::GetExtension($path).ToLowerInvariant()
                    $file = Get-Item -LiteralPath $path -Force -ErrorAction Stop
                    if ($normalized -match '(?i)(^|/)(\.git|profiles|runtime|dist|logs|core-data)(/|$)' -or
                        $leaf -match '(?i)^(settings|node-preferences|manager-state|proxy-state)\.json$' -or
                        $leaf -match '(?i)^\.env(?:\.|$)') { $rule = 'private-or-runtime-input' }
                    elseif ($file.PSIsContainer -or $file.Length -gt 8MB) { $rule = 'unsupported-size-or-type' }
                    elseif ($extension -notin @('.cs','.ps1','.md','.txt','.yaml','.yml','.json','.cmd') -and
                        $leaf -notin @('LICENSE','.gitignore','.gitattributes','.editorconfig')) { $rule = 'unsupported-file-type' }
                    else {
                        $reader = New-Object IO.StreamReader($path, (New-Object Text.UTF8Encoding($false, $true)), $true)
                        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
                        if ($text.IndexOf([char]0) -ge 0) { $rule = 'binary-content' }
                    }
                }
            }
        } catch { $rule = 'unreadable-input' }
        if ($rule) {
            [pscustomobject]@{ FileIndex = $fileIndex; Line = 0; Rule = $rule }
            continue
        }
        $lineNumber = 0
        foreach ($line in ($text -split '\r?\n')) {
            $lineNumber++
            foreach ($name in $rules.Keys) {
                if ([regex]::IsMatch($line, $rules[$name])) {
                    [pscustomobject]@{ FileIndex = $fileIndex; Line = $lineNumber; Rule = $name }
                }
            }
        }
    }
}

function Assert-ReleaseSafety {
    param([Parameter(Mandatory=$true)][string]$Root,
          [Parameter(Mandatory=$true)][AllowEmptyCollection()][string[]]$RelativePaths)
    $findings = @(Get-ReleaseSafetyFindings -Root $Root -RelativePaths $RelativePaths)
    if ($findings.Count -gt 0) {
        # Even filenames can contain secrets: identify inputs by index, not by name.
        $summary = @($findings | Select-Object -First 20 | ForEach-Object {
            'input #{0}, line {1}, rule {2}' -f $_.FileIndex, $_.Line, $_.Rule
        }) -join '; '
        throw ('Release safety check failed ({0} findings): {1}. Matched content and filenames are withheld.' -f $findings.Count, $summary)
    }
}
