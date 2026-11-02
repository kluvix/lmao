#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
tool_dir="${PIGLESS_TOOL_ROOT:-/workspace/tools}"
mkdir -p "$tool_dir/references"
if [[ ! -x "$tool_dir/dotnet/dotnet" ]]; then
  curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh -o "$tool_dir/dotnet-install.sh"
  bash "$tool_dir/dotnet-install.sh" --version 8.0.419 --install-dir "$tool_dir/dotnet" --no-path
fi
source "$repo_dir/scripts/env.sh"
if [[ ! -x "$tool_dir/bin/ilspycmd" ]]; then
  dotnet tool install ilspycmd --version 9.1.0.7988 --tool-path "$tool_dir/bin"
fi
if [[ ! -f "$tool_dir/references/UnityEngine.CoreModule.dll" ]]; then
  curl --fail --silent --show-error --location https://api.nuget.org/v3-flatcontainer/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg -o "$tool_dir/references/unity.nupkg"
  python - "$tool_dir/references" <<'PY'
import pathlib, sys, zipfile
root = pathlib.Path(sys.argv[1])
with zipfile.ZipFile(root / 'unity.nupkg') as archive:
    for name in archive.namelist():
        if name.endswith('.dll'):
            (root / name.rsplit('/', 1)[-1]).write_bytes(archive.read(name))
PY
fi
if [[ ! -f "$tool_dir/references/BepInEx.dll" ]]; then
  curl --fail --silent --show-error --location https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip -o "$tool_dir/references/bepinex.zip"
  python - "$tool_dir/references" <<'PY'
import pathlib, sys, zipfile
root = pathlib.Path(sys.argv[1])
with zipfile.ZipFile(root / 'bepinex.zip') as archive:
    (root / 'BepInEx.dll').write_bytes(archive.read('BepInEx/core/BepInEx.dll'))
PY
fi
# netstandard reference assemblies allow ILSpy to reconstruct managed code correctly.
cp -n "$DOTNET_ROOT/packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1/"*.dll "$tool_dir/references/"
printf 'SDK: %s\n' "$(dotnet --version)"
ilspycmd --version
