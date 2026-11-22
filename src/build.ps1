param([string]$Destination = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Installa Visual Studio Build Tools con gli strumenti C++ per Windows.' }
$toolset = Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10"
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h') } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Windows SDK non trovato.' }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$Destination = (Resolve-Path -LiteralPath $Destination).Path
$oldInclude = $env:INCLUDE
$oldLib = $env:LIB
try {
    $env:INCLUDE = @((Join-Path $toolset.FullName 'include'), (Join-Path $sdk.FullName 'ucrt'), (Join-Path $sdk.FullName 'shared'), (Join-Path $sdk.FullName 'um')) -join ';'
    $env:LIB = @((Join-Path $toolset.FullName 'lib\x64'), (Join-Path $sdkRoot "Lib\$($sdk.Name)\ucrt\x64"), (Join-Path $sdkRoot "Lib\$($sdk.Name)\um\x64")) -join ';'
    $compiler = Join-Path $toolset.FullName 'bin\Hostx64\x64\cl.exe'
    & $compiler /nologo /LD /MT /O2 /W4 /WX /std:c++17 /DUNICODE /D_UNICODE (Join-Path $PSScriptRoot 'native\PrivacyHook.cpp') "/Fo$(Join-Path $Destination 'PrivacyHook64.obj')" /link user32.lib "/OUT:$(Join-Path $Destination 'PrivacyHook64.dll')" "/IMPLIB:$(Join-Path $Destination 'PrivacyHook64.lib')" /DYNAMICBASE /NXCOMPAT
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione del modulo non riuscita.' }
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $managedSources = @('AssemblyInfo.cs', 'StreamPrivacy.cs', 'RecoverySafety.cs', 'Settings.cs', 'Theme.cs', 'UiLanguage.cs', 'MainForm.cs', 'CaptureTestForm.cs', 'TrayRestoreTests.cs', 'ReleaseReadinessTests.cs', 'ForcedExitTests.cs', 'Program.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ "/out:$(Join-Path $Destination 'StreamPrivacy.exe')" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" "/win32icon:$(Join-Path $PSScriptRoot 'assets\StreamPrivacy.ico')" "/resource:$(Join-Path $PSScriptRoot 'assets\StreamPrivacy.ico'),StreamPrivacy.Icon" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $managedSources
    if ($LASTEXITCODE -ne 0) { throw "Compilazione dell'interfaccia non riuscita." }
    Write-Output "Creato: $Destination\StreamPrivacy.exe"
} finally {
    $env:INCLUDE = $oldInclude
    $env:LIB = $oldLib
}
