#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
source scripts/env.sh

reference_dir="${PIGLESS_TOOL_ROOT:-/workspace/tools}/references"
unity_dir="${PIGLESS_UNITY_REFERENCE_DIR:-$reference_dir}"
if [[ ! -f "$reference_dir/BepInEx.dll" ]]; then
  printf 'Missing BepInEx 5 build reference: %s/BepInEx.dll\n' "$reference_dir" >&2
  printf 'Run scripts/setup.sh or use a clean reference directory.\n' >&2
  exit 1
fi
for name in UnityEngine.dll UnityEngine.CoreModule.dll UnityEngine.PhysicsModule.dll; do
  if [[ ! -f "$unity_dir/$name" ]]; then
    printf 'Missing Unity build reference: %s/%s\n' "$unity_dir" "$name" >&2
    exit 1
  fi
done

dotnet build src/PiglessBunny/PiglessBunny.csproj --configuration Release --nologo \
  -p:ToolReferenceDir="$reference_dir" -p:UnityReferenceDir="$unity_dir"
dotnet run --project tests/PiglessBunnyChecks --configuration Release

# Stage only the plugin after compilation and checks succeed. Reference DLLs
# remain development dependencies and are never copied into the game package.
output="$repo_dir/artifacts/PiglessBunny"
plugin_dir="$output/BepInEx/plugins/PiglessBunny"
mkdir -p "$plugin_dir"
cp src/PiglessBunny/bin/Release/netstandard2.1/PiglessBunny.dll "$plugin_dir/PiglessBunny.dll"
cp docs/PIGLESS_BUNNY.md "$output/README.md"
printf 'Build and checks passed. Package staged at %s\n' "$output"
