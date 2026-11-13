param(
    [Parameter(Mandatory = $true)][string]$NsisPath,
    [string]$Version = '0.2.4',
    [switch]$SkipBuild,
    [switch]$BuildTestInstaller
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$app = Join-Path $repo 'build\app'
$dist = Join-Path $repo 'dist'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch.' }
$compiler = (Resolve-Path -LiteralPath $NsisPath).Path
if (-not $SkipBuild) { & (Join-Path $repo 'src\build.ps1') -Destination $app }
foreach ($file in @('StreamPrivacy.exe', 'PrivacyHook64.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $app $file))) { throw "Built application file missing: $file" }
}
foreach ($file in @('LICENSE', 'README.md', 'README.it.md', 'PRIVACY.md', 'THIRD_PARTY_NOTICES.txt', 'assets\StreamPrivacy.ico', 'assets\StreamPrivacy.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo $file))) { throw "Repository file missing: $file" }
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# A unique workspace avoids deleting a previous package or unrelated local files.
$stage = Join-Path $repo ('build\package-' + [Guid]::NewGuid().ToString('N'))
$portable = Join-Path $stage 'StreamPrivacy'
New-Item -ItemType Directory -Force -Path $portable,(Join-Path $portable 'assets'),(Join-Path $portable 'Data') | Out-Null
foreach ($file in @('StreamPrivacy.exe', 'PrivacyHook64.dll')) { Copy-Item -LiteralPath (Join-Path $app $file) -Destination $portable }
foreach ($file in @('LICENSE', 'README.md', 'README.it.md', 'PRIVACY.md', 'THIRD_PARTY_NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $portable }
foreach ($file in @('StreamPrivacy.ico', 'StreamPrivacy.png')) { Copy-Item -LiteralPath (Join-Path $repo ('assets\' + $file)) -Destination (Join-Path $portable 'assets') }
[IO.File]::WriteAllText((Join-Path $portable 'portable.flag'), "Portable profile: Data\settings.xml`r`n", [Text.UTF8Encoding]::new($false))
$zip = Join-Path $dist "StreamPrivacy-$Version-win-x64-portable.zip"
Compress-Archive -LiteralPath $portable -DestinationPath $zip -Force
$setup = Join-Path $dist "StreamPrivacy-$Version-win-x64-setup.exe"
& $compiler /V3 "/DAPP_VERSION=$Version" "/DAPP_DIR=$app" "/DREPO_DIR=$repo" "/DOUTPUT_FILE=$setup" (Join-Path $repo 'installer\StreamPrivacy.nsi')
if ($LASTEXITCODE -ne 0) { throw 'NSIS installer compilation failed.' }

if ($BuildTestInstaller) {
    # Not a release asset: no registry registration or system shortcuts.
    $testSetup = Join-Path $repo 'build\StreamPrivacy-installer-test.exe'
    & $compiler /V3 /DTEST_BUILD "/DAPP_VERSION=$Version" "/DAPP_DIR=$app" "/DREPO_DIR=$repo" "/DOUTPUT_FILE=$testSetup" (Join-Path $repo 'installer\StreamPrivacy.nsi')
    if ($LASTEXITCODE -ne 0) { throw 'NSIS isolated-test installer compilation failed.' }
}
$lines = foreach ($path in @($zip, $setup)) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, [IO.Path]::GetFileName($path)
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $lines, [Text.UTF8Encoding]::new($false))
Write-Output "Release artifacts: $dist"
