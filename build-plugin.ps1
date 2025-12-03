# BTCNutServer Plugin Build Script
# This script builds and packages the Cashu plugin into a .btcpay file

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"

# Constants
$PluginProjectPath = "Plugin\BTCPayServer.Plugins.Cashu\BTCPayServer.Plugins.Cashu.csproj"
$PluginPackerPath = "submodules\btcpayserver\BTCPayServer.PluginPacker\BTCPayServer.PluginPacker.csproj"
# Plugin identifier must match the AssemblyName in .csproj file
$PluginIdentifier = "btcnutserver-test"
$PublishOutputPath = "Plugin\BTCPayServer.Plugins.Cashu\bin\$Configuration\net8.0\publish"
$FinalOutputPath = Join-Path $OutputDir $PluginIdentifier

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BTCNutServer Plugin Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Initialize submodules if needed
Write-Host "[1/6] Checking submodules..." -ForegroundColor Yellow
if (-not (Test-Path "submodules\btcpayserver\.git")) {
    Write-Host "  Initializing submodules..." -ForegroundColor Gray
    git submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: Failed to initialize submodules" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  Submodules already initialized" -ForegroundColor Green
}

# Step 2: Restore dependencies
Write-Host "[2/6] Restoring dependencies..." -ForegroundColor Yellow
dotnet restore BTCNutServer.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Failed to restore dependencies" -ForegroundColor Red
    exit 1
}
Write-Host "  Dependencies restored" -ForegroundColor Green

# Step 3: Build PluginPacker
Write-Host "[3/6] Building PluginPacker..." -ForegroundColor Yellow
dotnet build $PluginPackerPath --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Failed to build PluginPacker" -ForegroundColor Red
    exit 1
}
Write-Host "  PluginPacker built successfully" -ForegroundColor Green

# Step 4: Build the plugin
Write-Host "[4/6] Building plugin ($Configuration)..." -ForegroundColor Yellow
dotnet build $PluginProjectPath --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Failed to build plugin" -ForegroundColor Red
    exit 1
}
Write-Host "  Plugin built successfully" -ForegroundColor Green

# Step 5: Publish the plugin (includes all dependencies for production)
Write-Host "[5/6] Publishing plugin ($Configuration)..." -ForegroundColor Yellow
if (Test-Path $PublishOutputPath) {
    Remove-Item -Path $PublishOutputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishOutputPath -Force | Out-Null

dotnet publish $PluginProjectPath --configuration $Configuration --output $PublishOutputPath --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Failed to publish plugin" -ForegroundColor Red
    exit 1
}
Write-Host "  Plugin published successfully" -ForegroundColor Green

# Verify DLL exists with correct assembly name
$DllPath = Join-Path $PublishOutputPath "$PluginIdentifier.dll"
if (-not (Test-Path $DllPath)) {
    Write-Host "  ERROR: Plugin DLL not found at $DllPath" -ForegroundColor Red
    Write-Host "  Expected DLL name: $PluginIdentifier.dll" -ForegroundColor Yellow
    Write-Host "  Files in publish directory:" -ForegroundColor Yellow
    Get-ChildItem -Path $PublishOutputPath -Filter "*.dll" | ForEach-Object { Write-Host "    - $($_.Name)" -ForegroundColor Gray }
    exit 1
}

# Clean up unnecessary files to reduce plugin size
Write-Host "  Cleaning up publish directory..." -ForegroundColor Gray
Get-ChildItem -Path $PublishOutputPath -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $PublishOutputPath -Filter "*.deps.json" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $PublishOutputPath -Filter "*.staticwebassets.endpoints.json" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path (Join-Path $PublishOutputPath "refs")) {
    Remove-Item -Path (Join-Path $PublishOutputPath "refs") -Recurse -Force
}

# Step 6: Package the plugin
Write-Host "[6/6] Packaging plugin..." -ForegroundColor Yellow

# Clean previous build output
if (Test-Path $FinalOutputPath) {
    Write-Host "  Cleaning previous build output..." -ForegroundColor Gray
    Remove-Item -Path $FinalOutputPath -Recurse -Force
}

# Create output directory
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Run PluginPacker using the plugin identifier (must match AssemblyName)
Write-Host "  Running PluginPacker..." -ForegroundColor Gray
dotnet run --project $PluginPackerPath --configuration $Configuration --no-build -- `
    $PublishOutputPath `
    $PluginIdentifier `
    $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Failed to package plugin" -ForegroundColor Red
    exit 1
}

# Find the created .btcpay file
$BtcpayFile = Get-ChildItem -Path $FinalOutputPath -Recurse -Filter "*.btcpay" | Select-Object -First 1

if ($BtcpayFile) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Build completed successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Plugin package:" -ForegroundColor Cyan
    Write-Host "  Location: $($BtcpayFile.FullName)" -ForegroundColor White
    Write-Host "  Size: $([math]::Round($BtcpayFile.Length / 1MB, 2)) MB" -ForegroundColor White
    Write-Host "  Plugin Identifier: $PluginIdentifier" -ForegroundColor White
    Write-Host ""
    Write-Host "You can now upload this file to your BTCPay Server instance." -ForegroundColor Yellow
} else {
    Write-Host "  WARNING: .btcpay file not found, but build completed" -ForegroundColor Yellow
}
