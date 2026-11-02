#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
source scripts/env.sh
managed_dir="${PIGLESS_GAME_MANAGED_DIR:-$repo_dir/lib/game}"
reference_dir="${PIGLESS_TOOL_ROOT:-/workspace/tools}/references"
for name in Assembly-CSharp SteamVR SteamVR_Actions; do
  if [[ ! -f "$managed_dir/$name.dll" ]]; then
    printf 'Missing game reference: %s/%s.dll\n' "$managed_dir" "$name" >&2
    exit 1
  fi
done
for project in PiglessCamera PiglessSheesh; do
  dotnet build "src/$project/$project.csproj" --configuration Release --nologo \
    -p:GameManagedDir="$managed_dir" -p:ToolReferenceDir="$reference_dir" \
    -p:UnityReferenceDir="${PIGLESS_UNITY_REFERENCE_DIR:-$reference_dir}"
done
PIGLESS_GAME_MANAGED_DIR="$managed_dir" dotnet run --project tests/CoreChecks --configuration Release
mkdir -p artifacts/BepInEx/plugins/Pigless/assets
cp src/PiglessCamera/bin/Release/netstandard2.1/PiglessCamera.dll artifacts/BepInEx/plugins/Pigless/
cp src/PiglessSheesh/bin/Release/netstandard2.1/PiglessSheesh.dll artifacts/BepInEx/plugins/Pigless/
if [[ -f assets/jacesus.png ]]; then
  cp assets/jacesus.png artifacts/BepInEx/plugins/Pigless/assets/
else
  printf 'Pillow image missing: add assets/jacesus.png before deploying JACESUS.\n' >&2
fi
