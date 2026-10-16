#!/usr/bin/env bash
set -euo pipefail

: "${STEAM_USERNAME:?STEAM_USERNAME secret is required}"
: "${STEAM_CONFIG_VDF:?STEAM_CONFIG_VDF secret is required}"
: "${CONTENT_DIR:?CONTENT_DIR is required}"
: "${PREVIEW_FILE:?PREVIEW_FILE is required}"
: "${STEAMCMD:?STEAMCMD is required}"
: "${WORKSHOP_ITEM_ID:?WORKSHOP_ITEM_ID is required}"
: "${WORKSHOP_VISIBILITY:?WORKSHOP_VISIBILITY is required}"

[[ "$WORKSHOP_ITEM_ID" =~ ^[0-9]+$ ]] || { echo "WORKSHOP_ITEM_ID must be numeric"; exit 1; }
[[ "$WORKSHOP_VISIBILITY" == "0" || "$WORKSHOP_VISIBILITY" == "2" || "$WORKSHOP_VISIBILITY" == "3" ]] || {
  echo "WORKSHOP_VISIBILITY must be public (0), private (2) or unlisted (3)"; exit 1;
}

for artifact in NinjaSlayer.dll NinjaSlayer.json NinjaSlayer.pck ninjaslayer-variants.manifest SHA256SUMS; do
  test -f "$CONTENT_DIR/$artifact" || { echo "Missing release artifact: $artifact"; exit 1; }
done
test -d "$CONTENT_DIR/lib" || { echo "Missing release artifact directory: lib"; exit 1; }
test -f "$PREVIEW_FILE" || { echo "Missing Workshop preview image"; exit 1; }
test "$(stat -c%s "$PREVIEW_FILE")" -le 1048576 || { echo "Workshop preview exceeds 1 MiB"; exit 1; }
test -x "$STEAMCMD" || { echo "SteamCMD is unavailable"; exit 1; }

note_file="${RELEASE_NOTE_FILE:-$(dirname "${BASH_SOURCE[0]}")/../../Workshop/change-note.md}"
test -f "$note_file" || { echo "Workshop release note is missing"; exit 1; }
CHANGE_NOTE=$(cat "$note_file")
CHANGE_NOTE=${CHANGE_NOTE//$'\r'/}
[[ -n "${CHANGE_NOTE//[[:space:]]/}" ]] || { echo "Workshop release note must not be empty"; exit 1; }

steam_home="${STEAM_HOME:-$HOME/Steam}"
mkdir -p "$steam_home/config"
printf '%s' "$STEAM_CONFIG_VDF" > "$steam_home/config/config.vdf"
chmod 600 "$steam_home/config/config.vdf"

manifest="$(mktemp --suffix=.vdf)"
# Omit title/description: SteamCMD content uploads must preserve the language fields
# maintained by tools/workshop-metadata. Encode the frozen UTF-8 note as VDF.
escaped_note=${CHANGE_NOTE//\\/\\\\}
escaped_note=${escaped_note//\"/\\\"}
escaped_note=${escaped_note//$'\n'/\\n}
{
  printf '"workshopitem"\n{\n'
  printf '    "appid" "2868840"\n'
  printf '    "publishedfileid" "%s"\n' "$WORKSHOP_ITEM_ID"
  printf '    "contentfolder" "%s"\n' "$CONTENT_DIR"
  printf '    "previewfile" "%s"\n' "$PREVIEW_FILE"
  printf '    "visibility" "%s"\n' "$WORKSHOP_VISIBILITY"
  printf '    "changenote" "%s"\n' "$escaped_note"
  printf '}\n'
} > "$manifest"

"$STEAMCMD" +login "$STEAM_USERNAME" +quit
"$STEAMCMD" +login "$STEAM_USERNAME" +workshop_build_item "$manifest" +quit
