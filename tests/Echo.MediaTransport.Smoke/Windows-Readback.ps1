$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime]
$propertiesType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties, Windows.Media.Control, ContentType=WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
function Wait-WinRt($operation, [Type]$resultType) {
    $task = $asTask.MakeGenericMethod($resultType).Invoke($null, @($operation))
    if (-not $task.Wait(3000)) { throw 'Windows media operation timed out' }
    return $task.Result
}
$manager = Wait-WinRt ($managerType::RequestAsync()) $managerType
$session = $null
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    $session = $manager.GetSessions() | Where-Object { $_.SourceAppUserModelId -eq 'com.echo.avalonia' } | Select-Object -First 1
    if ($session) { break }
    Start-Sleep -Milliseconds 50
}
if (-not $session) { throw 'Migrated SMTC session missing' }
$properties = Wait-WinRt ($session.TryGetMediaPropertiesAsync()) $propertiesType
if ($properties.Title -notlike 'ECHO SMTC smoke *' -or $properties.Artist -ne 'Echo test' -or $properties.AlbumTitle -ne 'Migration') { throw 'SMTC metadata differs from confirmed track' }
if (-not $properties.Thumbnail) { throw 'Local artwork was not published' }
$streamType = [Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Storage.Streams, ContentType=WindowsRuntime]
$thumbnail = Wait-WinRt ($properties.Thumbnail.OpenReadAsync()) $streamType
if (-not $thumbnail) { throw 'Local artwork stream could not be opened' }
# Windows PowerShell exposes this WinRT result as an opaque COM object.
# Successfully opening it verifies artwork access; the short probe owns the stream until exit.
if ($session.GetPlaybackInfo().PlaybackStatus.ToString() -ne 'Playing') { throw 'Expected playing status' }
if (-not (Wait-WinRt ($session.TryPauseAsync()) ([bool]))) { throw 'Windows pause rejected' }
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    if ($session.GetPlaybackInfo().PlaybackStatus.ToString() -eq 'Paused') { break }
    Start-Sleep -Milliseconds 50
}
if ($session.GetPlaybackInfo().PlaybackStatus.ToString() -ne 'Paused') { throw 'Confirmed pause did not reach Windows' }
if (-not (Wait-WinRt ($session.TryChangePlaybackPositionAsync([long]30000000)) ([bool]))) { throw 'Windows seek rejected' }
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    if ($session.GetTimelineProperties().Position.TotalSeconds -eq 3) { break }
    Start-Sleep -Milliseconds 50
}
if ($session.GetTimelineProperties().Position.TotalSeconds -ne 3) { throw 'Seek timeline did not reach Windows' }
if (-not (Wait-WinRt ($session.TrySkipNextAsync()) ([bool]))) { throw 'Windows next rejected' }
Write-Output 'PASS Windows session metadata/artwork/status/timeline readback and pause/seek/next commands'
