param(
    [Parameter(Mandatory=$true)][string]$Ffmpeg,
    [Parameter(Mandatory=$true)][string]$TsMuxer,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new output directory for generated disc fixtures.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$fixtureRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$fixtureSource = Join-Path $fixtureRoot 'source.mkv'
$fixtureLong = Join-Path $fixtureRoot 'long.mkv'
& $Ffmpeg -nostdin -hide_banner -loglevel error -y -f lavfi -i 'color=c=blue:s=1280x720:r=24' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 4 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -profile:v high -level:v 4.1 -c:a ac3 -b:a 192k -threads 1 $fixtureSource
if ($LASTEXITCODE) { throw 'Short video generation failed' }
# Repeated timestamps exercise the MPEG-TS duration-estimation error corrected using MPLS.
& $Ffmpeg -nostdin -hide_banner -loglevel error -y -stream_loop 46 -i $fixtureSource -t 185 -c copy $fixtureLong
if ($LASTEXITCODE) { throw 'Long video generation failed' }
$fixtureMeta = Join-Path $fixtureRoot 'long.meta'
@('MUXOPT --blu-ray --custom-chapters=00:00:00.000;00:00:02.000', ('V_MPEG4/ISO/AVC, "' + $fixtureLong + '", track=1'), ('A_AC3, "' + $fixtureLong + '", track=2, lang=chi')) | Set-Content -LiteralPath $fixtureMeta -Encoding utf8
& $TsMuxer $fixtureMeta (Join-Path $fixtureRoot 'long.iso')
if ($LASTEXITCODE) { throw 'UDF image authoring failed' }
& $TsMuxer $fixtureMeta (Join-Path $fixtureRoot 'long-folder')
if ($LASTEXITCODE) { throw 'BDMV authoring failed' }
Write-Output $fixtureRoot
