# Function to extract AssemblyName from .csproj file
function Get-AssemblyName {
    param(
        [string]$CsprojPath
    )
    
    if (-not (Test-Path $CsprojPath)) {
        Write-Error "Error: .csproj file not found: $CsprojPath"
        exit 1
    }
    
    # Load the XML file
    [xml]$csproj = Get-Content $CsprojPath
    
    # Try to find AssemblyName in any PropertyGroup
    $assemblyName = $null
    foreach ($propertyGroup in $csproj.Project.PropertyGroup) {
        if ($propertyGroup.AssemblyName) {
            $assemblyName = $propertyGroup.AssemblyName
            break
        }
    }
    
    # If AssemblyName not found, fall back to project name (filename without extension)
    if (-not $assemblyName) {
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($CsprojPath)
        Write-Warning "AssemblyName not found in .csproj, using project name: $assemblyName"
    }
    
    return $assemblyName
}

# Path to the plugin project
$PROJECT_FILE = "Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj"

# Extract the assembly name from the .csproj file
$PLUGIN_NAME = Get-AssemblyName -CsprojPath $PROJECT_FILE
Write-Host "Detected assembly name: $PLUGIN_NAME"

# Build the plugin
dotnet build $PROJECT_FILE -c Release

# Publish the plugin to a temporary directory
$PUBLISH_DIR = "/tmp/publish"
if (Test-Path $PUBLISH_DIR) {
    Remove-Item -Recurse -Force $PUBLISH_DIR
}
New-Item -ItemType Directory -Path $PUBLISH_DIR -Force | Out-Null

# Publish the plugin - the DLL will be named according to AssemblyName
dotnet publish $PROJECT_FILE -c Release -o $PUBLISH_DIR --no-build

# Verify the DLL exists with the expected name
$DLL_PATH = Join-Path $PUBLISH_DIR "$PLUGIN_NAME.dll"
if (-not (Test-Path $DLL_PATH)) {
    Write-Error "Error: Expected DLL not found at $DLL_PATH"
    Write-Host "Files in publish directory:"
    Get-ChildItem $PUBLISH_DIR
    exit 1
}

# Package the plugin using PluginPacker
$OUTPUT_DIR = "/tmp/publish-package"

dotnet run --project submodules/btcpayserver/BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj -- $PUBLISH_DIR $PLUGIN_NAME $OUTPUT_DIR

Write-Host "Plugin packaged successfully!"
Write-Host "Output: $OUTPUT_DIR/$PLUGIN_NAME/*/$PLUGIN_NAME.btcpay"

