$env:DOTNET_ROOT = "D:\CODE\important files\dotnet-sdk-8.0.404-win-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

Write-Host "Building and running OrderLog..." -ForegroundColor Cyan
dotnet run --project src\OrderLog.csproj -c Debug
