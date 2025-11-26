# Build the plugin
dotnet build Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj -c Release

# Publish the plugin to a temporary directory
$PUBLISH_DIR = "/tmp/publish"
if (Test-Path $PUBLISH_DIR) {
    Remove-Item -Recurse -Force $PUBLISH_DIR
}
New-Item -ItemType Directory -Path $PUBLISH_DIR -Force | Out-Null

# Publish the plugin - the DLL will be named btcnutserver-test.dll due to AssemblyName
dotnet publish Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj -c Release -o $PUBLISH_DIR --no-build

# Package the plugin using PluginPacker
# IMPORTANT: Use the assembly name (btcnutserver-test), not the project name
$PLUGIN_NAME = "btcnutserver-test"
$OUTPUT_DIR = "/tmp/publish-package"

dotnet run --project submodules/btcpayserver/BTCPayServer.PluginPacker/BTCPayServer.PluginPacker.csproj -- $PUBLISH_DIR $PLUGIN_NAME $OUTPUT_DIR

Write-Host "Plugin packaged successfully!"
Write-Host "Output: $OUTPUT_DIR/$PLUGIN_NAME/*/$PLUGIN_NAME.btcpay"

