#!/bin/bash
# Bozzetto VSCode Configuration Script
# Automatically configures Ionide to use Bozzetto

set -e

echo "🔍 Finding Bozzetto installation..."

# Find Bozzetto.Server.dll
Bozzetto_DLL=$(find ~/.dotnet/tools/.store -name "Bozzetto.Server.dll" 2>/dev/null | head -n 1)

if [ -z "$Bozzetto_DLL" ]; then
    echo "❌ Bozzetto not found. Please install it first:"
    echo "   dotnet tool install -g Bozzetto.Server"
    exit 1
fi

echo "✅ Found Bozzetto at: $Bozzetto_DLL"

# Find VSCode settings file
if [ "$(uname)" == "Darwin" ]; then
    SETTINGS_PATH="$HOME/Library/Application Support/Code/User/settings.json"
else
    SETTINGS_PATH="$HOME/.config/Code/User/settings.json"
fi

if [ ! -f "$SETTINGS_PATH" ]; then
    echo "❌ VSCode settings file not found at: $SETTINGS_PATH"
    echo "   Please ensure VSCode is installed"
    exit 1
fi

echo "📝 Updating VSCode settings..."

# Use jq to update settings (install if needed)
if ! command -v jq &> /dev/null; then
    echo "❌ jq is required but not installed."
    echo "   Install it with: sudo apt-get install jq  (or brew install jq on macOS)"
    exit 1
fi

# Update the setting
jq --arg path "$Bozzetto_DLL" '."FSharp.fsiSdkFilePath" = $path' "$SETTINGS_PATH" > "$SETTINGS_PATH.tmp"
mv "$SETTINGS_PATH.tmp" "$SETTINGS_PATH"

echo "✅ VSCode configured successfully!"
echo ""
echo "You can now use Bozzetto with Ionide:"
echo "  1. Press Ctrl+Shift+P → 'FSI: Start'"
echo "  2. Use Alt+Enter to send code to Bozzetto"
echo ""
echo "Setting: FSharp.fsiSdkFilePath = $Bozzetto_DLL"
