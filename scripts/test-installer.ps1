param([Parameter(Mandatory = $true)][string]$TestSetupPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$setup = (Resolve-Path -LiteralPath $TestSetupPath).Path
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($setup).Comments -ne 'Isolated installer test build') {
    throw 'Refusing to test a normal installer. Compile with TEST_BUILD: it uses no registry registration or system shortcuts.'
}
$installPath = Join-Path $repo ('build\installer-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $installPath | Out-Null
$lines = [Collections.Generic.List[string]]::new()
$demo = $null
try {
    $install = Start-Process -FilePath $setup -ArgumentList @('/S', "/D=$installPath") -WindowStyle Hidden -Wait -PassThru
    if ($install.ExitCode -ne 0) { throw "Silent test installation failed: $($install.ExitCode)" }
    foreach ($file in @('StreamPrivacy.exe','PrivacyHook64.dll','LICENSE','README.md','README.it.md','PRIVACY.md','THIRD_PARTY_NOTICES.txt','Uninstall.exe','StreamPrivacy.install.ini','TestShortcuts\Stream Privacy.lnk')) {
        if (-not (Test-Path -LiteralPath (Join-Path $installPath $file))) { throw "Installed file missing: $file" }
    }
    $lines.Add('PASS: offline test installer deployed its owned files and local test shortcut.')
    $appHash = (Get-FileHash -LiteralPath (Join-Path $installPath 'StreamPrivacy.exe')).Hash
    $demo = Start-Process -FilePath (Join-Path $installPath 'StreamPrivacy.exe') -ArgumentList '--demo' -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 400
    if ($demo.HasExited) { throw 'Harmless file-lock test process exited unexpectedly.' }
    $lockedInstall = Start-Process -FilePath $setup -ArgumentList @('/S', "/D=$installPath") -WindowStyle Hidden -Wait -PassThru
    if ($lockedInstall.ExitCode -ne 1618) { throw "Running-app installation was not rejected: $($lockedInstall.ExitCode)" }
    if ((Get-FileHash -LiteralPath (Join-Path $installPath 'StreamPrivacy.exe')).Hash -ne $appHash) { throw 'Locked application was modified.' }
    $lines.Add('PASS: update detects a running app and aborts without terminating it or changing the executable.')
    # _?= keeps this refusal check in the original process, preserving its exit code.
    $lockedUninstall = Start-Process -FilePath (Join-Path $installPath 'Uninstall.exe') -ArgumentList @('/S', "_?=$installPath") -WindowStyle Hidden -Wait -PassThru
    if ($lockedUninstall.ExitCode -ne 1618) { throw "Running-app uninstallation was not rejected: $($lockedUninstall.ExitCode)" }
    $lines.Add('PASS: uninstallation also refuses to remove a running app.')
    $demo.CloseMainWindow() | Out-Null
    if (-not $demo.WaitForExit(2000)) { $demo.Kill(); $demo.WaitForExit() }
    $demo.Dispose(); $demo = $null
    New-Item -ItemType Directory -Force -Path (Join-Path $installPath 'Data') | Out-Null
    [IO.File]::WriteAllText((Join-Path $installPath 'Data\settings.xml'), 'User setting preservation sentinel')
    [IO.File]::WriteAllText((Join-Path $installPath 'keep-me.txt'), 'Unrelated user file preservation sentinel')
    $uninstall = Start-Process -FilePath (Join-Path $installPath 'Uninstall.exe') -ArgumentList '/S' -WindowStyle Hidden -Wait -PassThru
    if ($uninstall.ExitCode -ne 0) { throw "Test uninstallation failed: $($uninstall.ExitCode)" }
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((Test-Path -LiteralPath (Join-Path $installPath 'StreamPrivacy.exe')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
    foreach ($file in @('StreamPrivacy.exe','PrivacyHook64.dll','README.md','THIRD_PARTY_NOTICES.txt','TestShortcuts\Stream Privacy.lnk','StreamPrivacy.install.ini')) {
        if (Test-Path -LiteralPath (Join-Path $installPath $file)) { throw "Owned file survived uninstallation: $file" }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $installPath 'Data\settings.xml')) -or -not (Test-Path -LiteralPath (Join-Path $installPath 'keep-me.txt'))) { throw 'Uninstaller removed user data or unrelated files.' }
    $lines.Add('PASS: uninstaller removes owned files and preserves user settings and unrelated files.')
    $lines.Add('TEST_BUILD only: no real app, startup registration, uninstall registry entry or system shortcut was changed.')
    $lines | ForEach-Object { Write-Output $_ }
} catch {
    $lines.Add('FAIL: ' + $_.Exception.Message)
    throw
} finally {
    if ($null -ne $demo) { try { $demo.CloseMainWindow() | Out-Null; if (-not $demo.WaitForExit(2000)) { $demo.Kill() } } finally { $demo.Dispose() } }
    $report = Join-Path $repo 'build\installer-test-report.txt'
    [IO.File]::WriteAllLines($report, $lines, [Text.UTF8Encoding]::new($false))
}
