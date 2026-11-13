param([string]$Executable=(Join-Path $PSScriptRoot '..\build\app\StreamPrivacy.exe'),[string]$ReportDirectory=(Join-Path $PSScriptRoot '..\build\test-reports'))
$ErrorActionPreference='Stop'
$Executable=(Resolve-Path -LiteralPath $Executable).Path
New-Item -ItemType Directory -Force -Path $ReportDirectory | Out-Null
$ReportDirectory=(Resolve-Path -LiteralPath $ReportDirectory).Path
foreach ($name in @('self-test','integration-test','regression-test','capture-demo-test','tray-restore-test','release-readiness-test')) {
    $report=Join-Path $ReportDirectory ($name+'.txt')
    $run=Start-Process -FilePath $Executable -ArgumentList @("--$name",('"'+$report+'"')) -WindowStyle Hidden -Wait -PassThru
    if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }
    if ($run.ExitCode -ne 0) { throw "$name failed (exit $($run.ExitCode)). See $report" }
}
Write-Output 'All local development checks passed. Actual recorder output still needs a real capture test.'
