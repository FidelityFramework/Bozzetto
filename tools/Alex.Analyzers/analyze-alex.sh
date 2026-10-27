#!/usr/bin/env bash
set -euo pipefail

tool_dir=$(cd "$(dirname "$0")" && pwd)
repo_dir=$(git -C "$tool_dir" rev-parse --show-toplevel)
: "${ALEX_ANALYZER_PROJECT:?set ALEX_ANALYZER_PROJECT to the absolute Alex production project}"
[[ "$ALEX_ANALYZER_PROJECT" = /* && -f "$ALEX_ANALYZER_PROJECT" ]] || { printf 'Alex project must be an existing absolute path.\n' >&2; exit 2; }
export BOZZETTO_PROJECT="$ALEX_ANALYZER_PROJECT"
cd "$tool_dir"
lease() { dotnet fsi "$repo_dir/tools/build/lease.fsx" "$@"; }
lease full_build dotnet tool restore
lease full_build dotnet build Alex.Analyzers.fsproj
lease full_build dotnet build tests/ContractFixture/ContractFixture.fsproj
lease full_build dotnet build tests/ImpostorFixture/ImpostorFixture.fsproj
lease full_build dotnet build tests/AlexFixture/AlexFixture.fsproj
lease full_build dotnet build tests/StaleContract/StaleContract.fsproj
lease full_build bash tests/project-control.sh
lease test_suite_run bash tests/run.sh --pipeline
lease full_build bash check-project.sh alex-boundary
lease full_build bash check-project.sh alex
