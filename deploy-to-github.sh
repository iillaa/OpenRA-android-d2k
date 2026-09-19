#!/bin/bash
# Run this in your Termux session to create a GitHub repo and push the branch:
#   cd /data/data/com.termux/files/home/chat/dune
#   chmod +x /data/data/com.termux/files/home/chat/dune/deploy-to-github.sh
#   ./deploy-to-github.sh
#
# It will:
#   1. Ask you to authenticate with GitHub (gh auth login --web, approve on phone)
#   2. Create a new repo: "OpenRA-android-d2k" under your GitHub account
#   3. Push the android-native-port branch + set upstream
#   4. Trigger the CI workflow

set -euo pipefail

REPO_NAME="OpenRA-android-d2k"

echo "=== Step 1: Authenticate with GitHub ==="
gh auth login --web --hostname github.com --protocol https

echo ""
echo "=== Step 2: Create new repo '$REPO_NAME' ==="
gh repo create "$REPO_NAME" \
  --public \
  --description "OpenRA Dune 2000 — Android native port (from android-native-port branch)" \
  --homepage "https://github.com/tarek369/OpenRA/tree/android-native-port" \
  --source=. \
  --push

echo ""
echo "=== Done! ==="
echo "Repo created and branch pushed. GitHub Actions should start automatically."
echo "Watch CI at: https://github.com/\$USER/$REPO_NAME/actions"
