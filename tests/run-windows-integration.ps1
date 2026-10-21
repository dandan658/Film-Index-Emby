param(
    [Parameter(Mandatory=$true)][string]$EmbySystemPath,
    [string]$DotNet = 'dotnet',
    [string]$FixtureParent = $env:TEMP
)
$ErrorActionPreference = 'Stop'
$taskBase = (Resolve-Path -LiteralPath $FixtureParent).Path
$taskRepo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskSystem = (Resolve-Path -LiteralPath $EmbySystemPath).Path
& $DotNet build (Join-Path $PSScriptRoot 'IntegrationHarness') -c Release --nologo -v quiet "-p:EmbyServerPath=$taskSystem"
if ($LASTEXITCODE) { throw 'Harness build failed' }
$taskRoot = Join-Path $taskBase ('film-index-integration-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $taskRoot,(Join-Path $taskRoot 'music'),(Join-Path $taskRoot 'movies'),(Join-Path $taskRoot 'state/config'),(Join-Path $taskRoot 'state/plugins') | Out-Null
Set-Content (Join-Path $taskRoot '.fixture-only') 'isolated generated media only'
$taskEncoder = Join-Path $taskSystem 'ffmpeg.exe'
& $taskEncoder -nostdin -hide_banner -loglevel error -y -f lavfi -i 'color=c=blue:s=720x480:r=10' -f lavfi -i 'anullsrc=r=44100:cl=stereo' -t 3 -c:v mpeg4 -c:a aac -threads 1 (Join-Path $taskRoot 'movies/fixture.mp4')
if ($LASTEXITCODE) { throw 'video fixture generation failed' }
New-Item -ItemType Directory -Force (Join-Path $taskRoot 'intro-movies') | Out-Null
& $taskEncoder -nostdin -hide_banner -loglevel error -y -f lavfi -i 'color=c=black:s=64x64:r=1' -t 120 -c:v mpeg4 -threads 1 (Join-Path $taskRoot 'intro-movies/intro.mp4')
if ($LASTEXITCODE) { throw 'intro fixture generation failed' }
& $taskEncoder -nostdin -hide_banner -loglevel error -y -f lavfi -i 'color=c=black:s=64x64:r=1' -f lavfi -i 'anoisesrc=c=pink:r=16000:d=160' -t 160 -c:v mpeg4 -c:a aac -threads 1 (Join-Path $taskRoot 'fingerprint-source.mp4')
if ($LASTEXITCODE) { throw 'fingerprint fixture generation failed' }
& $taskEncoder -nostdin -hide_banner -loglevel error -y -f lavfi -i 'anullsrc=r=44100:cl=stereo' -t 3 -c:a libmp3lame -threads 1 (Join-Path $taskRoot 'music/track.mp3')
if ($LASTEXITCODE) { throw 'music fixture generation failed' }
& $taskEncoder -nostdin -hide_banner -loglevel error -y -f lavfi -i 'anullsrc=r=44100:cl=stereo' -t 3 -c:a aac -threads 1 (Join-Path $taskRoot 'movies/fixture.en.mka')
if ($LASTEXITCODE) { throw 'audio fixture generation failed' }
& $taskEncoder -nostdin -hide_banner -loglevel error -y -i (Join-Path $taskRoot 'movies/fixture.mp4') -frames:v 1 -an -threads 1 (Join-Path $taskRoot 'cover.jpg')
if ($LASTEXITCODE) { throw 'cover fixture generation failed' }
"1`n00:00:00,000 --> 00:00:02,000`nIntegration fixture`n" | Set-Content (Join-Path $taskRoot 'movies/fixture.zh.srt')
'<ServerConfiguration><EnableAutoUpdate>false</EnableAutoUpdate><HttpServerPortNumber>18766</HttpServerPortNumber><HttpsPortNumber>18767</HttpsPortNumber><PublicPort>18766</PublicPort><IsPortAuthorized>true</IsPortAuthorized><AutoRunWebApp>false</AutoRunWebApp><EnableUPnP>false</EnableUPnP><EnableRemoteAccess>false</EnableRemoteAccess></ServerConfiguration>' | Set-Content (Join-Path $taskRoot 'state/config/system.xml')
Copy-Item (Join-Path $taskRepo 'StrmAssistant/bin/Release/net8.0/FilmIndex.Emby.dll') (Join-Path $taskRoot 'state/plugins')
Copy-Item (Join-Path $taskRepo 'tests/IntegrationHarness/bin/Release/net8.0/FilmIndex.IntegrationHarness.dll') (Join-Path $taskRoot 'state/plugins')
Set-Content (Join-Path $PSScriptRoot 'last-fixture-root.txt') $taskRoot
$taskServer = Start-Process -FilePath (Join-Path $taskSystem 'EmbyServer.exe') -ArgumentList @('-programdata',('"' + (Join-Path $taskRoot 'state') + '"'),'-noautorunwebapp','-service') -WindowStyle Hidden -PassThru -Environment @{FILM_INDEX_TEST_ROOT=$taskRoot}
try {
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(45)
    $taskReport = Join-Path $taskRoot 'report.json'
    while (!(Test-Path $taskReport) -and [DateTime]::UtcNow -lt $taskDeadline -and !$taskServer.HasExited) { Start-Sleep -Milliseconds 250; $taskServer.Refresh() }
    if (!(Test-Path $taskReport)) { throw 'Integration report was not produced' }
    $taskResult = Get-Content $taskReport -Raw | ConvertFrom-Json
    @{root=$taskRoot;success=$taskResult.success;checks=$taskResult.checks.Count;error=$taskResult.error} | ConvertTo-Json
    if (!$taskResult.success) { throw 'Integration checks failed; inspect the fixture report' }
} finally {
    $taskServer.Refresh()
    if (!$taskServer.HasExited) { Stop-Process -Id $taskServer.Id }
}
