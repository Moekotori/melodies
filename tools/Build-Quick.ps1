param([int]$TimeoutSeconds = 60)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskLogs = Join-Path $taskRoot 'artifacts\checks'
New-Item -ItemType Directory -Force -Path $taskLogs | Out-Null
$taskProcess = Start-Process dotnet -ArgumentList @('build', 'tools/Echo.Preview/Echo.Preview.csproj', '--nologo', '-v', 'minimal', '-p:NuGetAudit=false') -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskLogs 'build.out.log') -RedirectStandardError (Join-Path $taskLogs 'build.err.log')
if (-not $taskProcess.WaitForExit($TimeoutSeconds * 1000)) {
    # taskkill /T is restricted to the process tree created by this script.
    & taskkill /PID $taskProcess.Id /T /F | Out-Null
    Write-Error "Build exceeded $TimeoutSeconds seconds; only this build was stopped."
}
Get-Content (Join-Path $taskLogs 'build.out.log')
Get-Content (Join-Path $taskLogs 'build.err.log')
exit $taskProcess.ExitCode
