# Copyright (c) 2026 Rinko
# SPDX-License-Identifier: GPL-3.0-only
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-safety.ps1')
$resolved = (Resolve-Path -LiteralPath $Root).Path
# -z preserves spaces and Unicode paths. Tracked files remain in scope even if ignored.
$listing = @(& git -C $resolved ls-files -z --cached --others --exclude-standard 2>$null)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate Git release candidates; no audit was performed.' }
$raw = [string]::Join("`n", [string[]]$listing)
$paths = @($raw.Split([char]0) | Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
if ($paths.Count -eq 0) { throw 'No Git release candidates found; refusing an empty audit.' }
Assert-ReleaseSafety -Root $resolved -RelativePaths $paths
Write-Output ('PASS release candidates: {0} tracked/untracked non-ignored working-tree files; known credential patterns and unsafe input types checked.' -f $paths.Count)
Write-Output 'Scope: working-tree bytes only, not staged blobs or Git history. No guarantee against unknown, encoded or split secrets. Human review is still required.'
