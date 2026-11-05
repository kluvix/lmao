#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
source scripts/env.sh

reference_dir="${PIGLESS_TOOL_ROOT:-/workspace/tools}/references"
unity_dir="${PIGLESS_UNITY_REFERENCE_DIR:-$reference_dir}"
for name in BepInEx.dll 0Harmony.dll; do
  if [[ ! -f "$reference_dir/$name" ]]; then
    printf 'Missing build reference: %s/%s\n' "$reference_dir" "$name" >&2
    printf 'Install the official BepInEx 5 references described in docs/STICKY_HOLDABLES.md.\n' >&2
    exit 1
  fi
done
for name in UnityEngine.dll UnityEngine.CoreModule.dll; do
  if [[ ! -f "$unity_dir/$name" ]]; then
    printf 'Missing Unity build reference: %s/%s\n' "$unity_dir" "$name" >&2
    exit 1
  fi
done

dotnet build src/StickyHoldables/StickyHoldables.csproj --configuration Release --nologo \
  -p:ToolReferenceDir="$reference_dir" -p:UnityReferenceDir="$unity_dir"
dotnet run --project tests/StickyHoldablesChecks --configuration Release

# Stage only our plugin after compilation and the checks succeed. Never copy
# reference assemblies, game files, or other plugins into the install package.
output="$repo_dir/artifacts/StickyHoldables"
plugin_dir="$output/BepInEx/plugins/StickyHoldables"
mkdir -p "$plugin_dir"
cp src/StickyHoldables/bin/Release/netstandard2.1/StickyHoldables.dll "$plugin_dir/StickyHoldables.dll"
cp docs/STICKY_HOLDABLES.md "$output/README.md"
cp docs/licenses/OldHoldables-MIT.txt "$output/OldHoldables-MIT.txt"
printf 'Build and checks passed. Package staged at %s\n' "$output"
