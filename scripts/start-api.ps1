# Starts the Azure Functions API for local development.
$ErrorActionPreference = "Stop"

$apiDir = Join-Path $PSScriptRoot ".." "Api" | Resolve-Path
$settingsFile = Join-Path $apiDir "local.settings.json"
$exampleFile = Join-Path $apiDir "local.settings.example.json"

if (-not (Test-Path $settingsFile)) {
    Copy-Item $exampleFile $settingsFile
    Write-Host "Created Api/local.settings.json from example."
}

if (-not (Get-Command func -ErrorAction SilentlyContinue)) {
    Write-Host "Azure Functions Core Tools not found."
    Write-Host "Install with: npm install -g azure-functions-core-tools@4 --unsafe-perm true"
    exit 1
}

Write-Host "Starting API at http://localhost:7071"
Write-Host "Camping survey: GET/POST http://localhost:7071/api/camping/potluck"
Set-Location $apiDir
func start --port 7071
