# CSS lives under wwwroot and is served from the project folder — no WASM rebuild needed.
Write-Host "Hard refresh the browser (Ctrl+F5) to load css/app.css."
Write-Host ""
Write-Host "If WASM fails: you likely have TWO dev processes (dotnet run + dotnet watch) fighting for port 5000."
Write-Host "Run restart-dev.ps1 once — it stops watch + port 5000 and starts a single server."
