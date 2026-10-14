$ManagerSourceFiles = @(
    'AppInfo.cs','GettingStartedForm.cs','ControllerProbe.cs','NodeCatalog.cs','OpenSourceFoundation.cs',
    'ProfileManager.cs','ProcessProxyLauncher.cs','RunningApplication.cs','ApplicationConfiguration.cs','PrivacyProtection.cs',
    'ApplicationManagerForm.cs','UiButton.cs','DashboardForm.cs','PrivacyLog.cs','PrivacyCenterForm.cs','BrowserPrivacyProbe.cs',
    'Diagnostics.cs','DiagnosticsForm.cs','NodeControlService.cs','DashboardPoller.cs','ProxySessionService.cs','RecoveryWatchdog.cs','CodexProxyManager.cs'
)
$ManagerSourceFiles = @($ManagerSourceFiles | ForEach-Object { 'src/CodexProxyManager/' + $_ })
$ManagerTestFiles = @(
    'NodeCatalogTests.cs','OnboardingTests.cs','ControllerProbeTests.cs','DiagnosticsTests.cs','PrivacyTests.cs','PrivacyUiTests.cs','RunningApplicationTests.cs','ReviewTests.cs','ServiceTests.cs','RegressionTests.cs'
) | ForEach-Object { 'tests/CodexProxyManager.Tests/' + $_ }
$ManagerBuildFiles = @(
    'build-common.ps1','build.ps1','test.ps1','package.ps1','test-package.ps1',
    'release-safety.ps1','audit-release.ps1','test-release-safety.ps1','migrate-layout.ps1','test-layout.ps1'
) | ForEach-Object { 'scripts/' + $_ }
$ManagerDocs = @('QUICKSTART.md','USER_GUIDE.md','DEVELOPMENT.md','PRIVACY.md','ARCHITECTURE.md',
    'RELEASE_CHECKLIST.md','RELEASE_NOTES.md','ACCEPTANCE.md','THIRD_PARTY_NOTICES.md','FILE_LAYOUT.md','REVIEW.md','COEXISTENCE_TEST.md') |
    ForEach-Object { 'docs/' + $_ }
$ManagerTools = @('tools/Restore Codex Network.cmd','tools/restore_codex_network.ps1')
$ManagerReleaseSourceFiles = @($ManagerSourceFiles) + @($ManagerTestFiles) + @($ManagerBuildFiles) +
    @($ManagerDocs) + @($ManagerTools) + @('LICENSE','README.md','.gitignore','examples/config.example.yaml','resources/package-README.md')
function Get-PackageFileMap {
    $map = [ordered]@{'README.md'='resources/package-README.md'; 'LICENSE'='LICENSE'}
    foreach ($name in (@($ManagerDocs) + @($ManagerTools))) { $map[$name] = $name }
    foreach ($name in $ManagerReleaseSourceFiles) { $map['source/' + $name] = $name }
    return $map
}
function Get-ManagerVersion {
    $source = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'src/CodexProxyManager/AppInfo.cs') -Raw
    $match = [regex]::Match($source, 'const string Version = "(\d+\.\d+\.\d+)";')
    if (-not $match.Success) { throw 'AppInfo.cs must contain a numeric three-part version.' }
    return $match.Groups[1].Value
}
