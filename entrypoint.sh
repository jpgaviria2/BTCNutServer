#!/bin/bash
# Entrypoint script for Plugin Builder
# Extracts AssemblyName from plugin-metadata.json, .csproj, or falls back to project name
# Calls PluginPacker with the correct assembly name

set -e

# Function to extract AssemblyName from plugin-metadata.json (most reliable)
extract_from_metadata() {
    local metadata_file="$1"
    if [ ! -f "$metadata_file" ]; then
        return 1
    fi
    
    # Try using jq if available (most reliable)
    if command -v jq >/dev/null 2>&1; then
        local assembly_name=$(jq -r '.assemblyName // empty' "$metadata_file" 2>/dev/null)
        if [ -n "$assembly_name" ] && [ "$assembly_name" != "null" ]; then
            echo "$assembly_name"
            return 0
        fi
    fi
    
    # Fall back to grep/sed (no jq available)
    local assembly_name=$(grep -o '"assemblyName"[[:space:]]*:[[:space:]]*"[^"]*"' "$metadata_file" 2>/dev/null | sed 's/.*"assemblyName"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/' | head -n1)
    if [ -n "$assembly_name" ]; then
        echo "$assembly_name"
        return 0
    fi
    
    return 1
}

# Function to extract AssemblyName from .csproj file (fallback)
extract_from_csproj() {
    local csproj_file="$1"
    if [ ! -f "$csproj_file" ]; then
        return 1
    fi
    
    # Try to extract AssemblyName using sed (no Perl regex dependency)
    # Look for <AssemblyName>value</AssemblyName> pattern
    local assembly_name=$(sed -n 's/.*<AssemblyName>\([^<]*\)<\/AssemblyName>.*/\1/p' "$csproj_file" | head -n1 | xargs)
    
    if [ -n "$assembly_name" ]; then
        echo "$assembly_name"
        return 0
    fi
    
    return 1
}

# Function to get assembly name with fallback chain
get_assembly_name() {
    local metadata_file="${1:-plugin-metadata.json}"
    local csproj_file="$2"
    
    local assembly_name
    
    # Method 1: Try plugin-metadata.json first (most reliable)
    if assembly_name=$(extract_from_metadata "$metadata_file"); then
        echo "Extracted assembly name from plugin-metadata.json: $assembly_name" >&2
        echo "$assembly_name"
        return 0
    fi
    
    # Method 2: Try .csproj file
    if [ -n "$csproj_file" ] && assembly_name=$(extract_from_csproj "$csproj_file"); then
        echo "Extracted assembly name from .csproj: $assembly_name" >&2
        echo "$assembly_name"
        return 0
    fi
    
    # Method 3: Fall back to project name (last resort)
    if [ -n "$csproj_file" ]; then
        assembly_name=$(basename "$csproj_file" .csproj)
        echo "Warning: AssemblyName not found, using project name: $assembly_name" >&2
        echo "$assembly_name"
        return 0
    fi
    
    echo "Error: Could not determine assembly name" >&2
    return 1
}

# Default paths (can be overridden by environment variables)
PLUGIN_DIR="${PLUGIN_DIR:-Plugin/BTCPayServer.Plugins.Cashu}"
PROJECT_FILE="${PLUGIN_PROJECT_FILE:-$PLUGIN_DIR/BTCPayServer.Plugins.Cashu.csproj}"
METADATA_FILE="${PLUGIN_METADATA_FILE:-plugin-metadata.json}"
PUBLISH_DIR="${PLUGIN_PUBLISH_DIR:-/tmp/publish}"
PLUGIN_PACKER="${PLUGIN_PACKER:-/build-tools/PluginPacker/BTCPayServer.PluginPacker}"
OUTPUT_DIR="${PLUGIN_OUTPUT_DIR:-/tmp/publish-package}"

# Extract the assembly name using fallback chain
ASSEMBLY_NAME=$(get_assembly_name "$METADATA_FILE" "$PROJECT_FILE")

if [ -z "$ASSEMBLY_NAME" ]; then
    echo "Error: Failed to extract assembly name" >&2
    exit 1
fi

# Call PluginPacker with the extracted assembly name
echo "Calling PluginPacker with assembly name: $ASSEMBLY_NAME" >&2
"$PLUGIN_PACKER" "$PUBLISH_DIR" "$ASSEMBLY_NAME" "$OUTPUT_DIR"

