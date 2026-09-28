# Copies wwwroot/icons/hammer-sports.png to favicon.ico (PNG bytes; fine for modern browsers).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$wwwroot = Join-Path $root "wwwroot"
$sourcePath = Join-Path $wwwroot "icons\hammer-sports.png"

if (-not (Test-Path $sourcePath)) {
    Write-Error "Missing $sourcePath — add the Hammer Sports artwork there first."
}

Copy-Item $sourcePath (Join-Path $wwwroot "favicon.ico") -Force
Write-Host "Wrote $(Join-Path $wwwroot 'favicon.ico') from hammer-sports.png"
