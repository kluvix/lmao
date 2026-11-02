# Source this file from any shell before building or decompiling.
export DOTNET_ROOT="${PIGLESS_TOOL_ROOT:-/workspace/tools}/dotnet"
export DOTNET_CLI_HOME="${PIGLESS_TOOL_ROOT:-/workspace/tools}/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$DOTNET_ROOT:${PIGLESS_TOOL_ROOT:-/workspace/tools}/bin:$PATH"
