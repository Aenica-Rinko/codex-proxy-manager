$ManagerSourceFiles = @(
    'AppInfo.cs','GettingStartedForm.cs','ControllerProbe.cs','NodeCatalog.cs','OpenSourceFoundation.cs',
    'ProfileManager.cs','ProcessProxyLauncher.cs','ApplicationConfiguration.cs',
    'ApplicationManagerForm.cs','Diagnostics.cs','DiagnosticsForm.cs','CodexProxyManager.cs'
)
$ManagerReleaseSourceFiles = $ManagerSourceFiles + @(
    'NodeCatalogTests.cs','OnboardingTests.cs','ControllerProbeTests.cs','DiagnosticsTests.cs','RegressionTests.cs',
    'build-common.ps1','build.ps1','test.ps1','package.ps1','test-package.ps1',
    'release-safety.ps1','audit-release.ps1','test-release-safety.ps1'
)
function Get-ManagerVersion {
    $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppInfo.cs') -Raw
    $match = [regex]::Match($source, 'const string Version = "(\d+\.\d+\.\d+)";')
    if (-not $match.Success) { throw 'AppInfo.cs must contain a numeric three-part version.' }
    return $match.Groups[1].Value
}
