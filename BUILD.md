# BTCNutServer Plugin Build Guide

This document explains how to build and package the BTCNutServer (Cashu) plugin for BTCPay Server.

## Quick Reference

**Build Command:**
```powershell
.\build-plugin.ps1
```

**Output Location:**
```
dist/btcnutserver-test/[VERSION]/btcnutserver-test.btcpay
```

**Plugin Identifier:** `btcnutserver-test` (matches AssemblyName in .csproj)

## Prerequisites

- **.NET 8.0 SDK** or later
- **Git** (for submodule management)
- **PowerShell** (for Windows) or **Bash** (for Linux/macOS)

### macOS Specific Requirements

On macOS, you need both:
1. **.NET 8.0 SDK** (for building) - typically installed at `/usr/local/share/dotnet`
2. **.NET 8.0 Runtime** (for running PluginPacker) - typically installed at `~/.dotnet`

**Important:** macOS may have .NET 10.0 SDK installed, but you need .NET 8.0 runtime for PluginPacker. Install it using:
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --runtime dotnet --version 8.0.0
```

Verify installations:
```bash
# Check SDK (for building)
/usr/local/share/dotnet/dotnet --version

# Check runtime (for running apps)
~/.dotnet/dotnet --version
```

## Quick Build

The easiest way to build the plugin is using the provided build script:

### Windows (PowerShell)
```powershell
.\build-plugin.ps1
```

### macOS (Bash)

**IMPORTANT:** On macOS, you must set environment variables to use the correct .NET installations:

```bash
# Set .NET environment for building (SDK)
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="/usr/local/share/dotnet:$PATH"

# Build the plugin
./build-plugin.sh
```

**Note:** The build script will automatically handle the runtime switching for PluginPacker.

### Linux (Bash)
```bash
./build-plugin.sh
```

## Build Output

The build script will create a `.btcpay` file in a consistent location:

**Output Location:** `dist/btcnutserver-test/[VERSION]/btcnutserver-test.btcpay`

For example:
- `dist/btcnutserver-test/0.0.1.0/btcnutserver-test.btcpay`

**Expected Size:** ~11-12 MB (production build with all dependencies included)

## Build Script Options

The build script accepts the following parameters:

```powershell
# Use Debug configuration (default is Release)
.\build-plugin.ps1 -Configuration Debug

# Specify custom output directory (default is "dist")
.\build-plugin.ps1 -OutputDir "my-output"
```

## Build Process

The build script performs the following steps:

1. **Initialize Submodules** - Ensures BTCPay Server submodules are available
2. **Restore Dependencies** - Downloads all NuGet packages
3. **Build PluginPacker** - Builds the tool used to package the plugin
4. **Build Plugin** - Compiles the plugin project
5. **Publish Plugin** - Creates a production build with all dependencies (CRITICAL for production)
6. **Package Plugin** - Creates the `.btcpay` file using PluginPacker

**Important:** The script uses `dotnet publish` to create a production build with all dependencies included. This ensures the plugin package is complete and ready for deployment.

## Manual Build Steps

If you prefer to build manually, follow these steps:

### macOS Manual Build (Step-by-Step)

**Step 0: Set Up .NET Environment**

```bash
# For building (use SDK at /usr/local/share/dotnet)
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="/usr/local/share/dotnet:$PATH"
```

### 1. Initialize Submodules

**Windows:**
```powershell
git submodule update --init --recursive
```

**macOS/Linux:**
```bash
git submodule update --init --recursive
```

### 2. Restore Dependencies

**Windows:**
```powershell
dotnet restore BTCNutServer.sln
```

**macOS/Linux:**
```bash
# Ensure DOTNET_ROOT is set (see Step 0)
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="/usr/local/share/dotnet:$PATH"
dotnet restore BTCNutServer.sln
```

### 3. Build PluginPacker

**Windows:**
```powershell
dotnet build submodules\btcpayserver\BTCPayServer.PluginPacker\BTCPayServer.PluginPacker.csproj --configuration Release
```

**macOS/Linux:**
```bash
# Use SDK for building
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="/usr/local/share/dotnet:$PATH"
dotnet build submodules/btcpayserver/BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj --configuration Release
```

### 4. Build the Plugin

**Windows:**
```powershell
dotnet build Plugin\BTCPayServer.Plugins.Cashu\BTCPayServer.Plugins.Cashu.csproj --configuration Release
```

**macOS/Linux:**
```bash
# Use SDK for building
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="/usr/local/share/dotnet:$PATH"
dotnet build Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj --configuration Release
```

### 5. Publish the Plugin (PRODUCTION BUILD)

This step is **critical** - it creates a production build with all dependencies included. The publish directory will contain:
- Plugin DLL and all dependencies
- Embedded resources (views, assets, etc.)
- Runtime configuration files

**Windows:**
```powershell
dotnet publish Plugin\BTCPayServer.Plugins.Cashu\BTCPayServer.Plugins.Cashu.csproj --configuration Release --output "Plugin\BTCPayServer.Plugins.Cashu\bin\Release\net8.0\publish"
```

**macOS/Linux:**
```bash
# Use SDK for publishing
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="/usr/local/share/dotnet:$PATH"
rm -rf /tmp/publish
mkdir -p /tmp/publish
dotnet publish Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj --configuration Release -o /tmp/publish --no-build
```

### 6. Clean Up Publish Directory (Optional)

Remove unnecessary files to reduce plugin size:

**Windows:**
```powershell
$publishDir = "Plugin\BTCPayServer.Plugins.Cashu\bin\Release\net8.0\publish"
Remove-Item "$publishDir\*.pdb" -Force -ErrorAction SilentlyContinue
Remove-Item "$publishDir\*.deps.json" -Force -ErrorAction SilentlyContinue
Remove-Item "$publishDir\refs" -Recurse -Force -ErrorAction SilentlyContinue
```

**macOS/Linux:**
```bash
rm -rf /tmp/publish/refs /tmp/publish/*.pdb /tmp/publish/*.deps.json /tmp/publish/*.staticwebassets.endpoints.json 2>/dev/null
```

### 7. Package the Plugin

**Windows:**
```powershell
dotnet run --project submodules\btcpayserver\BTCPayServer.PluginPacker\BTCPayServer.PluginPacker.csproj --configuration Release --no-build -- `
    "Plugin\BTCPayServer.Plugins.Cashu\bin\Release\net8.0\publish" `
    "btcnutserver-test" `
    "dist"
```

**macOS/Linux:**
```bash
# CRITICAL: Switch to .NET 8.0 runtime for running PluginPacker
export DOTNET_ROOT=~/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
dotnet exec submodules/btcpayserver/BTCPayServer.PluginPacker/bin/Release/net8.0/BTCPayServer.PluginPacker.dll /tmp/publish btcnutserver-test dist
```

**Note:** 
- The plugin identifier (`btcnutserver-test`) must match the `AssemblyName` in the `.csproj` file.
- On macOS, you **must** switch to `~/.dotnet` (runtime) for Step 7, as PluginPacker requires .NET 8.0 runtime, not just the SDK.

## Build Configuration

### Plugin Information

- **Plugin Identifier:** `btcnutserver-test` (defined as `AssemblyName` in `.csproj`)
- **Plugin Name:** `BTCNutServer` (display name)
- **Plugin DLL:** `btcnutserver-test.dll`
- **Current Version:** `0.0.1` (defined in `.csproj` file, becomes `0.0.1.0` in package)
- **Target Framework:** `.NET 8.0`

### Build Output Structure

```
dist/
└── btcnutserver-test/
    └── 0.0.1.0/
        ├── btcnutserver-test.btcpay      (Plugin package, ~11-12 MB)
        ├── btcnutserver-test.btcpay.json (Plugin metadata)
        ├── SHA256SUMS                     (Checksums)
        └── SHA256SUMS.asc                 (GPG-signed checksums)
```

## Production Build vs Development Build

### Production Build (Recommended)
- Uses `dotnet publish` to include all dependencies
- Package size: ~11-12 MB
- All required DLLs and dependencies included
- Ready for deployment to production servers

### Development Build
- Uses `dotnet build` only
- Package size: ~1-2 MB
- Dependencies expected to be available on server
- May fail if dependencies are missing

**Always use the production build script for server deployment.**

## Consistent Build Output

The build script ensures:

1. **Consistent Plugin Identifier:** Always uses `btcnutserver-test` (matches AssemblyName)
2. **Consistent Location:** Always outputs to `dist/btcnutserver-test/[VERSION]/`
3. **Consistent Naming:** The `.btcpay` file is always named `btcnutserver-test.btcpay`
4. **Production Ready:** Uses `dotnet publish` to include all dependencies
5. **Clean Builds:** Previous builds in the output directory are automatically cleaned

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
   
   **Windows:**
   ```powershell
   dotnet --version
   ```
   
   **macOS:**
   ```bash
   /usr/local/share/dotnet/dotnet --version
   ```
   
   Should be 8.0 or later.

2. **Check .NET Runtime (macOS only):**
   ```bash
   ~/.dotnet/dotnet --version
   ```
   Should show 8.0.0 runtime. If not installed:
   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --runtime dotnet --version 8.0.0
   ```

3. **Clean and rebuild:**
   
   **Windows:**
   ```powershell
   dotnet clean BTCNutServer.sln
   dotnet restore BTCNutServer.sln
   .\build-plugin.ps1
   ```
   
   **macOS:**
   ```bash
   export DOTNET_ROOT=/usr/local/share/dotnet
   export PATH="/usr/local/share/dotnet:$PATH"
   dotnet clean BTCNutServer.sln
   dotnet restore BTCNutServer.sln
   ./build-plugin.sh
   ```

4. **Verify plugin DLL exists:**
   
   **Windows:**
   ```powershell
   Test-Path "Plugin\BTCPayServer.Plugins.Cashu\bin\Release\net8.0\publish\btcnutserver-test.dll"
   ```
   
   **macOS/Linux:**
   ```bash
   test -f /tmp/publish/btcnutserver-test.dll && echo "DLL exists" || echo "DLL not found"
   ```

### macOS-Specific Issues

**Problem: "No .NET SDKs were found" or "Framework 'Microsoft.NETCore.App', version '8.0.0' not found"**

**Solution:**
1. Install .NET 8.0 SDK if missing:
   ```bash
   # Download and install .NET 8.0 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
   # Or use Homebrew: brew install --cask dotnet-sdk
   ```

2. Install .NET 8.0 Runtime (required for PluginPacker):
   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --runtime dotnet --version 8.0.0
   ```

3. Verify both are installed:
   ```bash
   # SDK (for building)
   /usr/local/share/dotnet/dotnet --list-sdks
   # Should show 8.0.x
   
   # Runtime (for running)
   ~/.dotnet/dotnet --list-runtimes
   # Should show Microsoft.NETCore.App 8.0.0
   ```

4. Set environment variables correctly:
   ```bash
   # For building (Steps 1-6)
   export DOTNET_ROOT=/usr/local/share/dotnet
   export PATH="/usr/local/share/dotnet:$PATH"
   
   # For packaging (Step 7)
   export DOTNET_ROOT=~/.dotnet
   export PATH="$DOTNET_ROOT:$PATH"
   ```

**Problem: PluginPacker fails with "Framework 'Microsoft.NETCore.App', version '8.0.0' not found"**

**Solution:** You're using the SDK path instead of the runtime path. Switch to runtime:
```bash
export DOTNET_ROOT=~/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
# Then run PluginPacker again
```

### Plugin Size Issues

If the plugin package is smaller than expected (~1-2 MB instead of ~11-12 MB):

- **Problem:** Build script is using `dotnet build` instead of `dotnet publish`
- **Solution:** Ensure the build script includes the publish step (Step 5 in the script)
- **Check:** Verify the publish directory exists and contains all dependencies

### PluginPacker Errors

If PluginPacker fails:

1. Ensure the plugin DLL was built and published successfully
2. Check that the plugin identifier matches exactly: `btcnutserver-test`
3. Verify the publish output directory contains all required dependencies
4. The plugin identifier must match the `AssemblyName` in the `.csproj` file

## Installing the Plugin

Once built, you can install the plugin in BTCPay Server:

1. **Via UI:**
   - Go to Server Settings > Plugins
   - Click "Upload Plugin"
   - Select the `.btcpay` file from `dist/btcnutserver-test/[VERSION]/`

2. **Via Command Line:**
   ```bash
   btcpay-update.sh /path/to/btcnutserver-test.btcpay
   ```

## Version Management

To update the plugin version:

1. Edit `Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj`
2. Update the `<Version>` property:
   ```xml
   <Version>0.0.2</Version>
   ```
3. Rebuild the plugin - the new version will be in a new subdirectory

## CI/CD Integration

The build script is designed to work in CI/CD environments:

### GitHub Actions (Windows)
```yaml
- name: Build Plugin
  run: |
    git submodule update --init --recursive
    pwsh -File build-plugin.ps1
  shell: pwsh
```

### GitHub Actions (macOS)
```yaml
- name: Setup .NET 8.0
  uses: actions/setup-dotnet@v3
  with:
    dotnet-version: '8.0.x'

- name: Install .NET 8.0 Runtime
  run: |
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --runtime dotnet --version 8.0.0

- name: Build Plugin
  run: |
    git submodule update --init --recursive
    export DOTNET_ROOT=/usr/local/share/dotnet
    export PATH="/usr/local/share/dotnet:$PATH"
    ./build-plugin.sh
  shell: bash
```

### Local macOS Build Script

For consistent builds on macOS, you can create a wrapper script:

```bash
#!/bin/bash
set -e

# Set SDK for building
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="/usr/local/share/dotnet:$PATH"

# Build
./build-plugin.sh

# Note: build-plugin.sh should handle runtime switching for PluginPacker
```

## Notes

- The build script automatically cleans previous builds in the output directory
- The plugin is always built in Release configuration by default for production use
- **Production builds use `dotnet publish` to include all dependencies** - this is essential for deployment
- Debug builds can be created but are not recommended for distribution
- The `.btcpay` file is a ZIP archive containing the plugin DLL and all dependencies
- Expected production package size: ~2-12 MB (varies based on dependencies)
- **macOS users:** Always ensure you have both .NET 8.0 SDK (for building) and .NET 8.0 Runtime (for PluginPacker)
- **macOS users:** The build process requires switching between SDK (`/usr/local/share/dotnet`) and Runtime (`~/.dotnet`) paths

## Support

For issues or questions:
- Check the [main README](README.md)
- Review the [Feature Implementation Plan](FEATURE_IMPLEMENTATION_PLAN.md)
- Open an issue on the repository
