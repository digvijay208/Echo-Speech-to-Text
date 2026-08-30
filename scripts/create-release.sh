#!/usr/bin/env bash
# Create GitHub release v1.0.0 and upload installer.
# Usage: GITHUB_TOKEN=github_pat_... ./scripts/create-release.sh
#
# Reads installer from installer/output/Echo-Setup-1.0.0.exe.
# Reads release notes from scripts/release-notes.md.
# Sets token in Authorization header only — never written to disk or git config.

set -euo pipefail

REPO="digvijay208/Echo-Speech-to-Text"
TAG="v1.0.0"
NAME="Echo TC-80 v1.0.0"
INSTALLER="installer/output/Echo-Setup-1.0.0.exe"
NOTES_FILE="scripts/release-notes.md"
ASSET_NAME="Echo-Setup-1.0.0.exe"

if [[ -z "${GITHUB_TOKEN:-}" ]]; then
  echo "ERROR: set GITHUB_TOKEN env var (github_pat_...)" >&2
  exit 1
fi

if [[ ! -f "$INSTALLER" ]]; then
  echo "ERROR: installer not found at $INSTALLER" >&2
  exit 1
fi

if [[ ! -f "$NOTES_FILE" ]]; then
  echo "ERROR: release notes not found at $NOTES_FILE" >&2
  exit 1
fi

# 1. Create release (draft, then we patch; or direct release — direct is simpler)
echo "=== Creating release $TAG ==="
RELEASE_JSON=$(mktemp)
HTTP=$(curl -sS -o "$RELEASE_JSON" -w "%{http_code}" \
  -X POST "https://api.github.com/repos/$REPO/releases" \
  -H "Authorization: Bearer $GITHUB_TOKEN" \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2022-11-28" \
  -d "$(jq -n \
    --arg tag "$TAG" \
    --arg name "$NAME" \
    --arg notes "$(cat "$NOTES_FILE")" \
    '{tag_name: $tag, name: $name, body: $notes, draft: false, prerelease: false, generate_release_notes: false}')")

if [[ "$HTTP" != "201" ]]; then
  echo "ERROR: create release failed (HTTP $HTTP)" >&2
  cat "$RELEASE_JSON"
  rm -f "$RELEASE_JSON"
  exit 1
fi

UPLOAD_URL=$(jq -r '.upload_url' "$RELEASE_JSON" | sed 's/{?name,label}//')
RELEASE_URL=$(jq -r '.html_url' "$RELEASE_JSON")
echo "Created: $RELEASE_URL"
rm -f "$RELEASE_JSON"

# 2. Upload installer
echo "=== Uploading $ASSET_NAME ==="
ASSET_JSON=$(mktemp)
HTTP=$(curl -sS -o "$ASSET_JSON" -w "%{http_code}" \
  -X POST "${UPLOAD_URL}?name=$ASSET_NAME" \
  -H "Authorization: Bearer $GITHUB_TOKEN" \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2022-11-28" \
  -H "Content-Type: application/octet-stream" \
  --data-binary "@$INSTALLER")

if [[ "$HTTP" != "201" ]]; then
  echo "ERROR: upload failed (HTTP $HTTP)" >&2
  cat "$ASSET_JSON"
  rm -f "$ASSET_JSON"
  exit 1
fi

ASSET_URL=$(jq -r '.browser_download_url' "$ASSET_JSON")
ASSET_SIZE=$(jq -r '.size' "$ASSET_JSON")
rm -f "$ASSET_JSON"
echo "Uploaded: $ASSET_URL ($ASSET_SIZE bytes)"
echo
echo "=== Done ==="
echo "Release: $RELEASE_URL"
echo "Direct:  $ASSET_URL"
