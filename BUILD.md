# BTCNutServer Plugin Build Guide

This document explains how to build and package the BTCNutServer (Cashu) plugin for BTCPay Server.

## Quick Reference

**Build Command:**
```powershell
.\build-plugin.ps1
```

**Output Location:**
```
dist/BTCPayServer.Plugins.Cashu/[VERSION]/BTCPayServer.Plugins.Cashu.btcpay
```

**Plugin Name:** `BTCPayServer.Plugins.Cashu` (always consistent)

## Prerequisites

- **.NET 8.0 SDK** or later
- **Git** (for submodule management)
- **PowerShell** (for Windows) or **Bash** (for Linux/macOS)

## Quick Build

The easiest way to build the plugin is using the provided build script:

### Windows (PowerShell)
```powershell
.\build-plugin.ps1
```

### Linux/macOS (Bash)
```bash
# Note: A bash version of the script can be created if needed
# For now, you can follow the manual steps below
```

## Build Output

The build script will create a `.btcpay` file in a consistent location:

**Output Location:** `dist/BTCPayServer.Plugins.Cashu/[VERSION]/BTCPayServer.Plugins.Cashu.btcpay`

For example:
- `dist/BTCPayServer.Plugins.Cashu/0.0.1.0/BTCPayServer.Plugins.Cashu.btcpay`

## Build Script Options

The build script accepts the following parameters:

```powershell
# Use Debug configuration (default is Release)
.\build-plugin.ps1 -Configuration Debug

# Specify custom output directory (default is "dist")
.\build-plugin.ps1 -OutputDir "my-output"
```

## Manual Build Steps

If you prefer to build manually, follow these steps:

### 1. Initialize Submodules

```powershell
git submodule update --init --recursive
```

### 2. Restore Dependencies

```powershell
dotnet restore BTCNutServer.sln
```

### 3. Build PluginPacker

```powershell
dotnet build submodules\btcpayserver\BTCPayServer.PluginPacker\BTCPayServer.PluginPacker.csproj --configuration Release
```

### 4. Build the Plugin

```powershell
dotnet build Plugin\BTCPayServer.Plugins.Cashu\BTCPayServer.Plugins.Cashu.csproj --configuration Release
```

### 5. Package the Plugin

```powershell
dotnet run --project submodules\btcpayserver\BTCPayServer.PluginPacker\BTCPayServer.PluginPacker.csproj --configuration Release -- `
    "Plugin\BTCPayServer.Plugins.Cashu\bin\Release\net8.0" `
    "BTCPayServer.Plugins.Cashu" `
    "dist"
```

## Build Configuration

### Plugin Information

- **Plugin Name:** `BTCPayServer.Plugins.Cashu`
- **Plugin DLL:** `BTCPayServer.Plugins.Cashu.dll`
- **Current Version:** `0.0.1.0` (defined in `.csproj` file)
- **Target Framework:** `.NET 8.0`

### Build Output Structure

```
dist/
└── BTCPayServer.Plugins.Cashu/
    └── 0.0.1.0/
        ├── BTCPayServer.Plugins.Cashu.btcpay      (Plugin package)
        ├── BTCPayServer.Plugins.Cashu.btcpay.json (Plugin metadata)
        ├── SHA256SUMS                             (Checksums)
        └── SHA256SUMS.asc                         (GPG-signed checksums)
```

## Consistent Build Output

The build script ensures:

1. **Consistent Plugin Name:** Always uses `BTCPayServer.Plugins.Cashu`
2. **Consistent Location:** Always outputs to `dist/BTCPayServer.Plugins.Cashu/[VERSION]/`
3. **Consistent Naming:** The `.btcpay` file is always named `BTCPayServer.Plugins.Cashu.btcpay`
4. **Clean Builds:** Previous builds in the output directory are automatically cleaned

## Troubleshooting

### Submodule Issues

If you encounter submodule-related errors:

```powershell
# Remove and reinitialize submodules
git submodule deinit --all -f
git submodule update --init --recursive
```

### Build Failures

1. **Check .NET SDK version:**
   ```powershell
   dotnet --version
   ```
   Should be 8.0 or later.

2. **Clean and rebuild:**
   ```powershell
   dotnet clean BTCNutServer.sln
   dotnet restore BTCNutServer.sln
   .\build-plugin.ps1
   ```

3. **Verify plugin DLL exists:**
   ```powershell
   Test-Path "Plugin\BTCPayServer.Plugins.Cashu\bin\Release\net8.0\BTCPayServer.Plugins.Cashu.dll"
   ```

### PluginPacker Errors

If PluginPacker fails:

1. Ensure the plugin DLL was built successfully
2. Check that the plugin name matches exactly: `BTCPayServer.Plugins.Cashu`
3. Verify the build output directory contains all required dependencies

## Installing the Plugin

Once built, you can install the plugin in BTCPay Server:

1. **Via UI:**
   - Go to Server Settings > Plugins
   - Click "Upload Plugin"
   - Select the `.btcpay` file from `dist/BTCPayServer.Plugins.Cashu/[VERSION]/`

2. **Via Command Line:**
   ```bash
   btcpay-update.sh /path/to/BTCPayServer.Plugins.Cashu.btcpay
   ```

## Version Management

To update the plugin version:

1. Edit `Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj`
2. Update the `<Version>` property:
   ```xml
   <Version>0.0.2.0</Version>
   ```
3. Rebuild the plugin - the new version will be in a new subdirectory

## CI/CD Integration

The build script is designed to work in CI/CD environments:

```yaml
# Example GitHub Actions step
- name: Build Plugin
  run: |
    git submodule update --init --recursive
    pwsh -File build-plugin.ps1
  shell: pwsh
```

## Notes

- The build script automatically cleans previous builds in the output directory
- The plugin is always built in Release configuration by default for production use
- Debug builds can be created but are not recommended for distribution
- The `.btcpay` file is a ZIP archive containing the plugin DLL and all dependencies

## Support

For issues or questions:
- Check the [main README](README.md)
- Review the [Feature Implementation Plan](FEATURE_IMPLEMENTATION_PLAN.md)
- Open an issue on the repository

