#!/usr/bin/env bash
# Recover into a NEW directory; never overwrite the edited sources or recovery baseline.
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
source scripts/env.sh
output_dir="${1:?Usage: scripts/recover.sh NEW_OUTPUT_DIRECTORY}"
[[ ! -e "$output_dir" ]] || { printf 'Output already exists; refusing to overwrite.\n' >&2; exit 1; }
mkdir -p "$output_dir/references"
cp "${PIGLESS_TOOL_ROOT:-/workspace/tools}/references/"*.dll "$output_dir/references/"
cp lib/game/*.dll "$output_dir/references/"
ilspycmd --disable-updatecheck -r "$output_dir/references" -p -o "$output_dir/PiglessCamera" originals/PiglessCamera-1.3.0.dll
ilspycmd --disable-updatecheck -r "$output_dir/references" -p -o "$output_dir/PiglessSheesh" originals/PiglessSheesh-2.2.0.dll
