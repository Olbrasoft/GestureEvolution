#!/bin/bash
set -e

# PushToTalk Deploy Script
# Builds, tests, auto-increments version, and deploys
# Usage: ./deploy.sh [--no-version-bump]
#   --no-version-bump: Skip version increment (used by webhook to avoid infinite loop)

PROJECT_PATH="/home/jirka/Olbrasoft/PushToTalk"
PROJECT_FILE="$PROJECT_PATH/src/PushToTalk.App/PushToTalk.App.csproj"
DEPLOY_TARGET="/home/jirka/push-to-talk"
DESKTOP_FILE="io.olbrasoft.PushToTalk.desktop"
ICON_NAME="io.olbrasoft.PushToTalk"

# Parse arguments
BUMP_VERSION=true
for arg in "$@"; do
    case $arg in
        --no-version-bump)
            BUMP_VERSION=false
            shift
            ;;
    esac
done

echo "╔══════════════════════════════════════════════════════════════╗"
echo "║               PushToTalk Deploy Script                        ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo

cd "$PROJECT_PATH"

# Step 1: Get current version (and optionally increment)
CURRENT_VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$PROJECT_FILE")

if [ "$BUMP_VERSION" = true ]; then
    IFS='.' read -ra VERSION_PARTS <<< "$CURRENT_VERSION"
    MAJOR=${VERSION_PARTS[0]}
    MINOR=${VERSION_PARTS[1]}
    PATCH=${VERSION_PARTS[2]}
    NEW_PATCH=$((PATCH + 1))
    NEW_VERSION="$MAJOR.$MINOR.$NEW_PATCH"

    echo "📋 Version: $CURRENT_VERSION → $NEW_VERSION"

    # Update version in project file
    sed -i "s/<Version>$CURRENT_VERSION<\/Version>/<Version>$NEW_VERSION<\/Version>/" "$PROJECT_FILE"
    echo "✅ Version updated in project file"
else
    NEW_VERSION="$CURRENT_VERSION"
    echo "📋 Version: $CURRENT_VERSION (no bump requested)"
fi
echo

# Step 2: Run tests
echo "🧪 Running tests..."
if ! dotnet test --verbosity quiet; then
    echo "❌ Tests failed! Aborting deployment."
    if [ "$BUMP_VERSION" = true ]; then
        echo "Reverting version..."
        sed -i "s/<Version>$NEW_VERSION<\/Version>/<Version>$CURRENT_VERSION<\/Version>/" "$PROJECT_FILE"
    fi
    exit 1
fi
echo "✅ All tests passed"
echo

# Step 4: Build and publish
echo "🔨 Building and publishing..."
mkdir -p "$DEPLOY_TARGET"
dotnet publish src/PushToTalk.App/PushToTalk.App.csproj \
  -c Release \
  -o "$DEPLOY_TARGET" \
  --no-self-contained

echo "✅ Published to $DEPLOY_TARGET"
echo

# Step 5: Install desktop file for GNOME launcher
echo "🖥️  Installing desktop entry..."
DESKTOP_DIR="$HOME/.local/share/applications"
mkdir -p "$DESKTOP_DIR"

# Create desktop file with correct Exec path
cat > "$DESKTOP_DIR/$DESKTOP_FILE" << EOF
[Desktop Entry]
Name=Push To Talk
GenericName=Dictation Tool
Comment=Voice transcription using Whisper AI (v$NEW_VERSION)
Exec=$DEPLOY_TARGET/push-to-talk
Icon=$ICON_NAME
Terminal=false
Type=Application
Categories=AudioVideo;Audio;Utility;Accessibility;
Keywords=voice;whisper;dictation;transcription;ptt;microphone;
StartupNotify=false
X-GNOME-UsesNotifications=false
EOF

echo "✅ Desktop entry installed"

# Step 6: Install icons
echo "🎨 Installing icons..."
ICON_DIR="$HOME/.local/share/icons/hicolor"
mkdir -p "$ICON_DIR/scalable/apps"
cp "$PROJECT_PATH/data/icons/hicolor/scalable/apps/$ICON_NAME.svg" "$ICON_DIR/scalable/apps/" 2>/dev/null || true

# Install PNG icons for various sizes
for SIZE in 16 22 24 32 48 64 128 256; do
    mkdir -p "$ICON_DIR/${SIZE}x${SIZE}/apps"
    if [ -f "$PROJECT_PATH/data/icons/hicolor/${SIZE}x${SIZE}/apps/$ICON_NAME.png" ]; then
        cp "$PROJECT_PATH/data/icons/hicolor/${SIZE}x${SIZE}/apps/$ICON_NAME.png" "$ICON_DIR/${SIZE}x${SIZE}/apps/"
    fi
done

# Update icon cache
gtk-update-icon-cache -f -t "$ICON_DIR" 2>/dev/null || true
echo "✅ Icons installed"

# Step 7: Commit version bump (only if version was bumped)
if [ "$BUMP_VERSION" = true ]; then
    echo "📝 Committing version bump..."
    cd "$PROJECT_PATH"
    git add "$PROJECT_FILE"
    git commit -m "chore: bump version to $NEW_VERSION" --no-verify 2>/dev/null || echo "ℹ️  No changes to commit"
    git push origin main 2>/dev/null || echo "ℹ️  Could not push (maybe offline)"
    echo
else
    echo "ℹ️  Skipping version commit (--no-version-bump)"
    echo
fi

# Step 8: Display status
echo
echo "╔══════════════════════════════════════════════════════════════╗"
echo "║               ✅ Deployment completed!                        ║"
echo "╠══════════════════════════════════════════════════════════════╣"
echo "║  Version: $NEW_VERSION                                           ║"
echo "║  Location: $DEPLOY_TARGET                          ║"
echo "║                                                              ║"
echo "║  Launch: Press Super key and search 'Push To Talk'          ║"
echo "║  Or run: $DEPLOY_TARGET/push-to-talk               ║"
echo "╚══════════════════════════════════════════════════════════════╝"
