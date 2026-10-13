# Background watchdog: if Blazor WASM on port 5000 is not healthy, restart it.
param(
    [int] $IntervalSeconds = 30,
    [int] $Port = 5000,
    [string] $HealthUrl = 'http://localhost:5000/',
    [int] $RestartCooldownSeconds = 90,
    [switch] $Quiet
)

$ErrorActionPreference = 'Continue'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$lastRestart = [DateTime]::MinValue

function Write-MonitorLog {
    param([string] $Message)
    if (-not $Quiet) {
        Write-Host "[wasm-watch $(Get-Date -Format 'HH:mm:ss')] $Message"
    }
}

function Test-WasmHealthy {
    $listening = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    if ($listening.Count -eq 0) {
        return $false
    }

    try {
        $response = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 10 -Method Get
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    }
    catch {
        return $false
    }
}

function Stop-WasmDevProcesses {
    Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -match 'LukeHammerWebsite\\Client' -and
            ($_.CommandLine -match 'watch' -or $_.CommandLine -match 'dotnet\.exe" run')
        } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
            break
        }
        Start-Sleep -Milliseconds 400
    }
}

function Start-WasmDevServer {
    Push-Location $projectRoot
    try {
        & dotnet build -c Debug --nologo -v q
        if ($LASTEXITCODE -ne 0) {
            Write-MonitorLog 'dotnet build failed; not starting server.'
            return $false
        }

        $startInfo = @{
            FilePath               = 'dotnet'
            ArgumentList           = @('run', '--no-build', '--launch-profile', 'BlazorApp.Client')
            WorkingDirectory       = $projectRoot
            WindowStyle            = 'Minimized'
            PassThru               = $true
        }
        $null = Start-Process @startInfo
        return $true
    }
    finally {
        Pop-Location
    }
}

function Restart-WasmDev {
    Write-MonitorLog "WASM on port $Port looks down - restarting..."
    Stop-WasmDevProcesses
    $started = Start-WasmDevServer
    if ($started) {
        Write-MonitorLog 'Restart launched (dotnet run in a new window).'
    }
    return $started
}

Write-MonitorLog "Watching $HealthUrl every ${IntervalSeconds}s (Ctrl+C to stop this monitor only)."

while ($true) {
    if (Test-WasmHealthy) {
        Write-MonitorLog 'OK'
    }
    else {
        $sinceRestart = ((Get-Date) - $lastRestart).TotalSeconds
        if ($sinceRestart -ge $RestartCooldownSeconds) {
            if (Restart-WasmDev) {
                $lastRestart = Get-Date
            }
        }
        else {
            $wait = [math]::Ceiling($RestartCooldownSeconds - $sinceRestart)
            Write-MonitorLog "Still unhealthy; waiting ${wait}s before another restart attempt."
        }
    }

    Start-Sleep -Seconds $IntervalSeconds
}
