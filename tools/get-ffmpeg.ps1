<#
.SYNOPSIS  Downloads the FFmpeg build Audio Dimension bundles, verifies its SHA-256 and unpacks it into third_party/ffmpeg/bin.
#>
$ErrorActionPreference = 'Stop'
$asset    = 'ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip'
$expected = '04cbd0714a7fad85f799ab049c0594fd7716c89e0c86c915d814c36c4d7924ef'
$url      = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/$asset"
$dest     = Join-Path $PSScriptRoot '..\third_party\ffmpeg'
$work     = Join-Path ([IO.Path]::GetTempPath()) ("adffmpeg-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $zip = Join-Path $work $asset
    Write-Host "Downloading $url ..."
    Invoke-WebRequest -Uri $url -OutFile $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $expected) { throw "Checksum mismatch: expected $expected but got $hash. The 'latest' build may have been updated; update BUILDINFO.md after reviewing it." }
    $extract = Join-Path $work 'x'
    Expand-Archive $zip -DestinationPath $extract
    $root = (Get-ChildItem $extract -Directory | Select-Object -First 1).FullName
    New-Item -ItemType Directory -Force (Join-Path $dest 'bin') | Out-Null
    Copy-Item (Join-Path $root 'bin\ffmpeg.exe'), (Join-Path $root 'bin\ffprobe.exe') (Join-Path $dest 'bin') -Force
    Copy-Item (Join-Path $root 'bin\*.dll') (Join-Path $dest 'bin') -Force
    Copy-Item (Join-Path $root 'LICENSE.txt') $dest -Force
    Write-Host "FFmpeg installed to $dest\bin"
} finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
