$ErrorActionPreference = 'Stop'

$env:DOTNET_ROOT = "D:\CODE\important files\dotnet-sdk-8.0.404-win-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

$publishRoot = Join-Path $PSScriptRoot 'publish'
$appFolder = Join-Path $publishRoot 'SSCommandCentre'
$symbolsFolder = Join-Path $publishRoot 'symbols'
$zipPath = Join-Path $publishRoot 'SSCommandCentre.zip'

if (Test-Path $publishRoot) {
    Write-Host "Cleaning existing publish directory..." -ForegroundColor Yellow
    Remove-Item $publishRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $appFolder | Out-Null

Write-Host "Publishing OrderLog..." -ForegroundColor Cyan
dotnet publish src\OrderLog.csproj -c Release -r win-x64 --self-contained -o $appFolder
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Ensure optional folders exist
New-Item -ItemType Directory -Path $symbolsFolder | Out-Null

# Move debug symbols out of the main app folder
Get-ChildItem -Path $appFolder -Filter '*.pdb' -File -Recurse | ForEach-Object {
    $target = Join-Path $symbolsFolder $_.Name
    Move-Item $_.FullName $target -Force
}

# Remove any empty culture/resource folders left behind by publish
Get-ChildItem -Path $appFolder -Directory | Where-Object {
    -not (Get-ChildItem -Path $_.FullName -Force | Select-Object -First 1)
} | Remove-Item -Force

# (Re)create compressed artifact for easier distribution
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
$maxZipAttempts = 5
$zipDelaySeconds = 2
$zipSucceeded = $false

for ($attempt = 1; $attempt -le $maxZipAttempts; $attempt++) {
    try {
        Compress-Archive -Path (Join-Path $appFolder '*') -DestinationPath $zipPath -Force
        $zipSucceeded = $true
        break
    }
    catch {
        if ($attempt -eq $maxZipAttempts) {
            throw
        }

        Write-Host "Zip attempt $attempt failed due to a locked file. Retrying in $zipDelaySeconds seconds..." -ForegroundColor Yellow
        Start-Sleep -Seconds $zipDelaySeconds
    }
}

if (-not $zipSucceeded) {
    throw "Failed to create zip archive after $maxZipAttempts attempts."
}

Write-Host "`nStructured publish output:" -ForegroundColor Green
Write-Host " - App:        $appFolder" -ForegroundColor Gray
Write-Host " - Symbols:    $symbolsFolder" -ForegroundColor Gray
Write-Host " - Zip bundle: $zipPath" -ForegroundColor Gray

Read-Host "Publish complete. Press Enter to exit"
