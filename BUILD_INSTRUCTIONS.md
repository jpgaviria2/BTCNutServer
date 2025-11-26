# Build Instructions for BTCNutServer Plugin

## Overview

This plugin is designed to be built and distributed via the [BTCPay Server Plugin Builder](https://plugin-builder.btcpayserver.org/) or built locally for development and testing.

## Important: Assembly Name

**The plugin assembly name is `btcnutserver-test`**, not `BTCPayServer.Plugins.Cashu`.

This is critical for CI/CD environments:
- The DLL is named `btcnutserver-test.dll` (from the `AssemblyName` property in the .csproj)
- PluginPacker must be called with `btcnutserver-test` as the plugin name argument
- The plugin identifier in code is also `btcnutserver-test`

## Local Development Build

### Prerequisites

1. .NET 8.0 SDK installed
2. BTCPay Server source code cloned (as a submodule)

### Quick Build

Use the provided build scripts:

**Bash (Linux/macOS):**
```bash
./build-plugin.sh
```

**PowerShell (Windows):**
```powershell
.\build-plugin.ps1
```

**CI/CD Script:**
```bash
./scripts/build-plugin.sh
```

The scripts automatically:
1. Extract the `AssemblyName` from the .csproj file
2. Build the plugin
3. Publish to a temporary directory
4. Package using PluginPacker with the correct assembly name

### Manual Build Steps

If you need to build manually:

1. **Build the plugin:**
   ```bash
   dotnet build Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj -c Release
   ```

2. **Publish the plugin:**
   ```bash
   dotnet publish Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj -c Release -o /tmp/publish
   ```

3. **Package using PluginPacker:**
   ```bash
   # IMPORTANT: Use the assembly name, not the project name!
   dotnet run --project submodules/btcpayserver/BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj -- \
     "/tmp/publish" \
     "btcnutserver-test" \
     "/tmp/publish-package"
   ```

## CI/CD Configuration

### Plugin Builder

When registering with Plugin Builder:
- **Repository URL**: Your GitHub repository URL
- **Plugin Directory**: `Plugin/BTCPayServer.Plugins.Cashu`
- **Git Ref**: `main` (or specific version tag)

**Important for Plugin Builder:** The plugin uses a custom `AssemblyName` (`btcnutserver-test`) that differs from the project name. 

**When configuring Plugin Builder:**
- If Plugin Builder asks for an **assembly name** or **plugin slug**, use: **`btcnutserver-test`**
- The repository includes:
  - `entrypoint.sh` - Script that extracts AssemblyName from .csproj (if Plugin Builder supports custom entrypoints)
  - `plugin-metadata.json` - Contains the assembly name and plugin identifier for reference

**The assembly name must match the plugin identifier:**
- Assembly name in .csproj: `btcnutserver-test`
- Plugin identifier in code: `btcnutserver-test`
- DLL name: `btcnutserver-test.dll`
- Plugin slug for Plugin Builder: `btcnutserver-test`

### Custom CI/CD

If you're using a custom CI/CD environment, ensure:

1. **Set the correct ASSEMBLY_NAME environment variable:**
   ```bash
   export ASSEMBLY_NAME="btcnutserver-test"
   ```

2. **Or use the provided build script:**
   ```bash
   ./scripts/build-plugin.sh
   ```

3. **The build script automatically extracts AssemblyName**, so you can also use:
   ```bash
   PLUGIN_NAME=$(grep -oP '<AssemblyName>([^<]+)</AssemblyName>' Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj | sed 's/<AssemblyName>\(.*\)<\/AssemblyName>/\1/' | head -n1 | xargs)
   ```

### Common CI/CD Error

**Error:** `/tmp/publish/BTCPayServer.Plugins.Cashu.dll could not be found`

**Cause:** PluginPacker was called with the project name instead of the assembly name.

**Solution:** Use `btcnutserver-test` (the AssemblyName) when calling PluginPacker, not `BTCPayServer.Plugins.Cashu` (the project name).

## Build Output

The build process:
1. Compiles the plugin code
2. Embeds all `.cshtml` view files and resources as embedded resources
3. References BTCPay Server DLLs (but doesn't bundle them - `Private="False"`)
4. Creates a `.btcpay` package file

**Output location:** `dist/btcnutserver-test/{version}/btcnutserver-test.btcpay`

## Installation

Once you have the `.btcpay` file:

1. **Copy to Plugins Directory:**
   - Docker: `/btcpay/plugins/btcnutserver-test.btcpay`
   - Manual: `{BTCPayServerDirectory}/Plugins/btcnutserver-test.btcpay`

2. **Restart BTCPay Server**

3. **Enable Plugin:**
   - Go to Server Settings > Plugins
   - Find "BTCNutServer" and enable it

## Troubleshooting

### Build Errors: DLL Not Found

If you get errors about missing BTCPay Server DLLs:
- Ensure BTCPay Server submodule is initialized: `git submodule update --init --recursive`
- Check that `Directory.Build.targets` is present (handles submodule initialization)
- Verify DLLs exist in `submodules/btcpayserver/BTCPayServer/bin/Release/net8.0/`

### PluginPacker Errors: Wrong DLL Name

If PluginPacker can't find the DLL:
- Verify the DLL is named `btcnutserver-test.dll` (check `AssemblyName` in .csproj)
- Ensure PluginPacker is called with `btcnutserver-test`, not `BTCPayServer.Plugins.Cashu`
- Use the provided build scripts which automatically extract the correct name

### Plugin Not Loading

If the plugin doesn't appear in BTCPay Server:
- Check file extension is `.btcpay` (not `.dll`)
- Verify file is in the correct plugins directory
- Check BTCPay Server logs for errors
- Ensure BTCPay Server version is compatible (>=2.1.0)

## Project Structure

```
BTCNutServer/
├── Plugin/
│   └── BTCPayServer.Plugins.Cashu/
│       ├── BTCPayServer.Plugins.Cashu.csproj  (AssemblyName: btcnutserver-test)
│       └── CashuPlugin.cs                     (Identifier: "btcnutserver-test")
├── scripts/
│   └── build-plugin.sh                       (CI/CD build script)
├── build-plugin.sh                            (Local build script)
├── build-plugin.ps1                           (Windows build script)
├── Directory.Build.props                       (MSBuild properties)
└── Directory.Build.targets                    (Submodule handling)
```

## Notes

- The plugin does NOT bundle BTCPay Server DLLs
- DLLs are provided by BTCPay Server at runtime
- This ensures compatibility across different BTCPay Server installations
- The `.btcpay` file contains only the plugin code and embedded resources
- The `AssemblyName` property in the .csproj determines the DLL name and must match the plugin identifier

