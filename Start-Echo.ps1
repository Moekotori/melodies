$ErrorActionPreference = 'Stop'
$taskExe = Join-Path $PSScriptRoot 'src\Echo.Desktop\bin\Debug\net10.0\Echo.Desktop.exe'
if (-not (Test-Path -LiteralPath $taskExe)) {
    & (Join-Path $PSScriptRoot 'tools\Build-Quick.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Start-Process -FilePath $taskExe -WorkingDirectory $PSScriptRoot
