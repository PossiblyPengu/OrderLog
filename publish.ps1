$env:DOTNET_ROOT = "D:\CODE\important files\dotnet-sdk-8.0.404-win-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

Write-Host "Publishing OrderLog..." -ForegroundColor Cyan
dotnet publish src\OrderLog.csproj -c Release -r win-x64 --self-contained -o publish
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "`nPublished to: $PSScriptRoot\publish\" -ForegroundColor Green
Read-Host "Press Enter to exit"
