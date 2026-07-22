#!/usr/bin/env bash
set -euo pipefail

: "${STEAM_USERNAME:?STEAM_USERNAME secret is required}"
: "${STEAM_CONFIG_VDF:?STEAM_CONFIG_VDF secret is required}"
: "${CONTENT_DIR:?CONTENT_DIR is required}"
: "${PREVIEW_FILE:?PREVIEW_FILE is required}"
: "${STEAMCMD:?STEAMCMD is required}"

for artifact in NinjaSlayer.dll NinjaSlayer.json NinjaSlayer.pck SHA256SUMS; do
  test -f "$CONTENT_DIR/$artifact" || { echo "Missing release artifact: $artifact"; exit 1; }
done
test -f "$PREVIEW_FILE" || { echo "Missing Workshop preview image"; exit 1; }
test "$(stat -c%s "$PREVIEW_FILE")" -le 1048576 || { echo "Workshop preview exceeds 1 MiB"; exit 1; }
test -x "$STEAMCMD" || { echo "SteamCMD is unavailable"; exit 1; }

steam_home="${STEAM_HOME:-$HOME/Steam}"
mkdir -p "$steam_home/config"
printf '%s' "$STEAM_CONFIG_VDF" > "$steam_home/config/config.vdf"
chmod 600 "$steam_home/config/config.vdf"

manifest="$(mktemp --suffix=.vdf)"
escaped_note=${CHANGE_NOTE//\"/\\\"}
{
  printf '"workshopitem"\n{\n'
  printf '    "appid" "2868840"\n'
  printf '    "publishedfileid" "3761570842"\n'
  printf '    "contentfolder" "%s"\n' "$CONTENT_DIR"
  printf '    "previewfile" "%s"\n' "$PREVIEW_FILE"
  printf '    "changenote" "%s"\n' "$escaped_note"
  printf '}\n'
} > "$manifest"

"$STEAMCMD" +login "$STEAM_USERNAME" +quit
"$STEAMCMD" +login "$STEAM_USERNAME" +workshop_build_item "$manifest" +quit
