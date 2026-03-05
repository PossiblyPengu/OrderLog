$env:DOTNET_ROOT = "D:\CODE\important files\dotnet-sdk-8.0.404-win-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

Write-Host "Building OrderLog..." -ForegroundColor Cyan
dotnet build src\OrderLog.csproj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "`nBuild succeeded." -ForegroundColor Green
Read-Host "Press Enter to exit"
