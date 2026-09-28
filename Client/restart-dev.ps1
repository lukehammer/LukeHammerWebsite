# Stop Client on 5000, rebuild WASM output, start without rebuilding again.
$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

Push-Location $projectRoot
try {
    dotnet build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet run --launch-profile BlazorApp.Client --no-build
}
finally {
    Pop-Location
}
