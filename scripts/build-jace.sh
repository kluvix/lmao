#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
source scripts/env.sh

if ! command -v dotnet >/dev/null 2>&1; then
  printf 'Install the .NET 8 SDK or run scripts/setup.sh first.\n' >&2
  exit 1
fi
sdk_version="$(dotnet --version)"
if [[ ! "$sdk_version" =~ ^([0-9]+)\. ]] || (( BASH_REMATCH[1] < 8 )); then
  printf 'The build needs the .NET 8 SDK or newer; selected SDK: %s\n' "$sdk_version" >&2
  exit 1
fi

reference_dir="${PIGLESS_TOOL_ROOT:-/workspace/tools}/references"
unity_dir="${PIGLESS_UNITY_REFERENCE_DIR:-$reference_dir}"
for name in BepInEx.dll 0Harmony.dll; do
  if [[ ! -f "$reference_dir/$name" ]]; then
    printf 'Missing BepInEx 5 build reference: %s/%s\n' "$reference_dir" "$name" >&2
    printf 'Run scripts/setup.sh or select clean references with PIGLESS_TOOL_ROOT.\n' >&2
    exit 1
  fi
done
for name in UnityEngine.dll UnityEngine.CoreModule.dll UnityEngine.AudioModule.dll UnityEngine.IMGUIModule.dll UnityEngine.TextRenderingModule.dll; do
  if [[ ! -f "$unity_dir/$name" ]]; then
    printf 'Missing Unity build reference: %s/%s\n' "$unity_dir" "$name" >&2
    printf 'Set PIGLESS_UNITY_REFERENCE_DIR to matching game Unity assemblies.\n' >&2
    exit 1
  fi
done

dotnet build src/HeyJace/HeyJace.csproj --configuration Release --nologo \
  -p:ToolReferenceDir="$reference_dir" -p:UnityReferenceDir="$unity_dir"
dotnet run --project tests/HeyJaceChecks --configuration Release
dotnet run --project tests/HeyJacePipeChecks --configuration Release
dotnet run --project tests/HeyJaceGameActionsChecks --configuration Release
dotnet run --project src/HeyJace.Speech/Checks/HeyJace.SpeechChecks.csproj --configuration Release
dotnet publish src/HeyJace.Speech/HeyJace.Speech.csproj --configuration Release \
  --runtime win-x64 --self-contained true -p:PublishSingleFile=true \
  --output "$repo_dir/artifacts/heyjace-speech" --nologo

# Build a fresh package so old files cannot leak into it. Copy only our three
# runtime binaries and README; build references never enter the package.
output="$repo_dir/artifacts/HeyJace"
mkdir -p "$repo_dir/artifacts"
stage="$(mktemp -d "$repo_dir/artifacts/.HeyJace-stage.XXXXXX")"
trap 'rm -rf -- "$stage"' EXIT
plugin_dir="$stage/BepInEx/plugins/HeyJace"
mkdir -p "$plugin_dir/Speech"
cp src/HeyJace/bin/Release/netstandard2.1/HeyJace.dll "$plugin_dir/HeyJace.dll"
cp src/HeyJace.Core/bin/Release/netstandard2.1/HeyJace.Core.dll "$plugin_dir/HeyJace.Core.dll"
cp artifacts/heyjace-speech/HeyJace.Speech.exe "$plugin_dir/Speech/HeyJace.Speech.exe"
cp docs/HEY_JACE.md "$stage/README.md"
rm -rf -- "$output"
mv -- "$stage" "$output"
printf 'Build, checks, and Windows publish passed. Package staged at %s\n' "$output"
printf 'Copy the whole BepInEx/plugins/HeyJace folder into the game.\n'
