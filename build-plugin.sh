#!/bin/bash
set -e

# Function to extract AssemblyName from .csproj file
# Falls back to project name if AssemblyName is not found
extract_assembly_name() {
    local csproj_file="$1"
    if [ ! -f "$csproj_file" ]; then
        echo "Error: .csproj file not found: $csproj_file" >&2
        exit 1
    fi
    
    # Try to extract AssemblyName from the .csproj file
    local assembly_name=$(grep -oP '<AssemblyName>([^<]+)</AssemblyName>' "$csproj_file" | sed 's/<AssemblyName>\(.*\)<\/AssemblyName>/\1/' | head -n1 | xargs)
    
    # If AssemblyName not found, fall back to project name (filename without extension)
    if [ -z "$assembly_name" ]; then
        assembly_name=$(basename "$csproj_file" .csproj)
        echo "Warning: AssemblyName not found in .csproj, using project name: $assembly_name" >&2
    fi
    
    echo "$assembly_name"
}

# Path to the plugin project
PROJECT_FILE="Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj"

# Extract the assembly name from the .csproj file
PLUGIN_NAME=$(extract_assembly_name "$PROJECT_FILE")
echo "Detected assembly name: $PLUGIN_NAME"

# Build the plugin
dotnet build "$PROJECT_FILE" -c Release

# Publish the plugin to a temporary directory
PUBLISH_DIR="/tmp/publish"
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

# Publish the plugin - the DLL will be named according to AssemblyName
dotnet publish "$PROJECT_FILE" -c Release -o "$PUBLISH_DIR" --no-build

# Verify the DLL exists with the expected name
DLL_PATH="$PUBLISH_DIR/$PLUGIN_NAME.dll"
if [ ! -f "$DLL_PATH" ]; then
    echo "Error: Expected DLL not found at $DLL_PATH" >&2
    echo "Files in publish directory:" >&2
    ls -la "$PUBLISH_DIR" >&2
    exit 1
fi

# Package the plugin using PluginPacker
OUTPUT_DIR="/tmp/publish-package"

dotnet run --project submodules/btcpayserver/BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj -- "$PUBLISH_DIR" "$PLUGIN_NAME" "$OUTPUT_DIR"

echo "Plugin packaged successfully!"
echo "Output: $OUTPUT_DIR/$PLUGIN_NAME/*/$PLUGIN_NAME.btcpay"

