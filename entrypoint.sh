#!/bin/bash
# Entrypoint script for Plugin Builder
# Extracts AssemblyName from .csproj and calls PluginPacker with correct name

set -e

# Function to extract AssemblyName from .csproj file
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

# Default paths (can be overridden by environment variables)
PLUGIN_DIR="${PLUGIN_DIR:-Plugin/BTCPayServer.Plugins.Cashu}"
PROJECT_FILE="${PLUGIN_PROJECT_FILE:-$PLUGIN_DIR/BTCPayServer.Plugins.Cashu.csproj}"
PUBLISH_DIR="${PLUGIN_PUBLISH_DIR:-/tmp/publish}"
PLUGIN_PACKER="${PLUGIN_PACKER:-/build-tools/PluginPacker/BTCPayServer.PluginPacker}"
OUTPUT_DIR="${PLUGIN_OUTPUT_DIR:-/tmp/publish-package}"

# Extract the assembly name from the .csproj file
ASSEMBLY_NAME=$(extract_assembly_name "$PROJECT_FILE")
echo "Extracted assembly name from .csproj: $ASSEMBLY_NAME"

# Call PluginPacker with the extracted assembly name
echo "Calling PluginPacker with assembly name: $ASSEMBLY_NAME"
"$PLUGIN_PACKER" "$PUBLISH_DIR" "$ASSEMBLY_NAME" "$OUTPUT_DIR"

