# One long-lived dev session: rebuilds when C#/Razor change; CSS is picked up from wwwroot on refresh.
$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

Start-Sleep -Seconds 2

Push-Location $projectRoot
try {
    dotnet watch run --launch-profile BlazorApp.Client
}
finally {
    Pop-Location
}
