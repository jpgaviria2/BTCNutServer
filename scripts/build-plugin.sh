#!/bin/bash
# Build script for CI/CD environments (like Plugin Builder)
# Automatically extracts AssemblyName from .csproj and builds the plugin package

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

# Configuration - can be overridden by environment variables
PROJECT_FILE="${PLUGIN_PROJECT_FILE:-Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj}"
PUBLISH_DIR="${PLUGIN_PUBLISH_DIR:-/tmp/publish}"
OUTPUT_DIR="${PLUGIN_OUTPUT_DIR:-/tmp/publish-package}"
CONFIGURATION="${BUILD_CONFIGURATION:-Release}"

# Extract the assembly name from the .csproj file
# This is critical - PluginPacker requires the DLL name to match this
PLUGIN_NAME=$(extract_assembly_name "$PROJECT_FILE")
echo "=== Plugin Build Configuration ==="
echo "Project file: $PROJECT_FILE"
echo "Detected assembly name: $PLUGIN_NAME"
echo "Publish directory: $PUBLISH_DIR"
echo "Output directory: $OUTPUT_DIR"
echo "Configuration: $CONFIGURATION"
echo ""

# Build the plugin
echo "=== Building Plugin ==="
dotnet build "$PROJECT_FILE" -c "$CONFIGURATION"

# Publish the plugin to the publish directory
echo ""
echo "=== Publishing Plugin ==="
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$PROJECT_FILE" -c "$CONFIGURATION" -o "$PUBLISH_DIR" --no-build

# Verify the DLL exists with the expected name
DLL_PATH="$PUBLISH_DIR/$PLUGIN_NAME.dll"
if [ ! -f "$DLL_PATH" ]; then
    echo "Error: Expected DLL not found at $DLL_PATH" >&2
    echo "Files in publish directory:" >&2
    ls -la "$PUBLISH_DIR" >&2
    exit 1
fi

echo "Verified DLL exists: $DLL_PATH"

# Package the plugin using PluginPacker
echo ""
echo "=== Packaging Plugin ==="
PLUGIN_PACKER_PROJECT="${PLUGIN_PACKER_PROJECT:-submodules/btcpayserver/BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj}"

if [ ! -f "$PLUGIN_PACKER_PROJECT" ]; then
    echo "Error: PluginPacker project not found at $PLUGIN_PACKER_PROJECT" >&2
    exit 1
fi

dotnet run --project "$PLUGIN_PACKER_PROJECT" -- "$PUBLISH_DIR" "$PLUGIN_NAME" "$OUTPUT_DIR"

echo ""
echo "=== Build Complete ==="
echo "Plugin packaged successfully!"
echo "Output location: $OUTPUT_DIR/$PLUGIN_NAME/*/$PLUGIN_NAME.btcpay"

