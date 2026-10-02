param([int]$TimeoutSeconds = 30)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskLogs = Join-Path $taskRoot 'artifacts\checks'
New-Item -ItemType Directory -Force -Path $taskLogs | Out-Null
# UI validation against the last built dependencies. Does not rebuild parallel native/library work.
foreach ($taskProject in @('src/Echo.Desktop/Echo.Desktop.csproj', 'tools/Echo.Preview/Echo.Preview.csproj')) {
    $taskLabel = if ($taskProject.StartsWith('src/')) { 'settings-desktop' } else { 'settings-tool' }
    $taskOutput = Join-Path $taskLogs "$taskLabel.out.log"
    $taskError = Join-Path $taskLogs "$taskLabel.err.log"
    $taskProcess = Start-Process dotnet -ArgumentList @('build', $taskProject, '--no-restore', '--nologo', '-v', 'minimal', '-p:BuildProjectReferences=false', '-p:NuGetAudit=false') -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput $taskOutput -RedirectStandardError $taskError
    if (-not $taskProcess.WaitForExit($TimeoutSeconds * 1000)) {
        & taskkill /PID $taskProcess.Id /T /F | Out-Null
        throw "Settings build exceeded $TimeoutSeconds seconds; only this build was stopped."
    }
    Get-Content $taskOutput
    Get-Content $taskError
    if ($taskProcess.ExitCode -ne 0) { exit $taskProcess.ExitCode }
}
