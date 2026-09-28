# Run unit tests on every save (run from this folder — do not use "dotnet watch test --project" from repo root).
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet watch test -- --logger "console;verbosity=normal"
