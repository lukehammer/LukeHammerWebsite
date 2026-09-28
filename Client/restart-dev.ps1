# Stop Client on 5000, rebuild, start (single run — avoids boot.json / .wasm mismatch).
$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match 'LukeHammerWebsite\\Client' -and $_.CommandLine -match 'watch' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline) {
    if (-not (Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue)) {
        break
    }
    Start-Sleep -Milliseconds 400
}

Push-Location $projectRoot
try {
    dotnet run --launch-profile BlazorApp.Client
}
finally {
    Pop-Location
}
