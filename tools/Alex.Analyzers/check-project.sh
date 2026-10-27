#!/usr/bin/env bash
set -euo pipefail

tool_dir=$(cd "$(dirname "$0")" && pwd)
repo_dir=$(git -C "$tool_dir" rev-parse --show-toplevel)
mode=${1:?expected alex, alex-boundary or clef}
project=${2:-${ALEX_ANALYZER_PROJECT:?set ALEX_ANALYZER_PROJECT or supply an absolute F# project path}}
[[ "$project" = /* && -f "$project" ]] || { printf 'Project must be an existing absolute path.\n' >&2; exit 2; }
project=$(realpath "$project")
case "$mode" in
  alex|alex-boundary)
    alex_project=$(realpath "${ALEX_ANALYZER_PROJECT:-$repo_dir/../Alex/src/Alex/Alex.fsproj}")
    [[ "$project" == "$alex_project" ]] || { printf 'Alex rules require the configured Alex production project.\n' >&2; exit 2; }
    if [[ "$mode" == alex ]]; then analyzers=("Clef production baseline" "Alex published boundary"); else analyzers=("Alex published boundary"); fi
    ;;
  clef) analyzers=("Clef production baseline") ;;
  *) printf 'Expected alex, alex-boundary or clef mode.\n' >&2; exit 2 ;;
esac

[[ -f "$tool_dir/bin/Debug/net10.0/Alex.Analyzers.dll" ]] || { printf 'Analyzer assembly is not built.\n' >&2; exit 1; }
receipt=${ALEX_ANALYZER_TRUTH_RECEIPT:-$repo_dir/artifacts/alex-analyzers-tests/latest-truth.sha256}
[[ -f "$receipt" ]] && sha256sum --check --status "$receipt" || { printf 'Truth receipt is missing or does not bind current inputs.\n' >&2; exit 1; }
for required in "$tool_dir/bin/Debug/net10.0/Alex.Analyzers.dll" "$tool_dir/Analyzer.fs" "$tool_dir/tests/run.sh" "$tool_dir/.config/dotnet-tools.json" "$tool_dir/check-project.sh" "$tool_dir/ProjectInputs.fsx" "$tool_dir/VerifyProjectLog.fsx" "$tool_dir/Alex.Analyzers.fsproj" "$tool_dir/Directory.Packages.props" "$tool_dir/packages.lock.json" "$tool_dir/NuGet.Config" "$tool_dir/tests/ContractFixture/Contract.fs" "$tool_dir/tests/ContractFixture/ContractFixture.fsproj" "$tool_dir/tests/ContractFixture/bin/Debug/net10.0/Fidelity.PSG.dll" "$tool_dir/tests/ImpostorFixture/ImpostorFixture.fsproj" "$tool_dir/tests/ImpostorFixture/bin/Debug/net10.0/Unrelated.Contract.dll" "$tool_dir/tests/AlexFixture/Ensure.fs" "$tool_dir/tests/AlexFixture/AlexFixture.fsproj" "$tool_dir/tests/AlexFixture/bin/Debug/net10.0/Alex.dll"; do
  binding=$(sha256sum "$required")
  rg -Fqx "$binding" "$receipt" || { printf 'Truth receipt does not bind %s.\n' "$required" >&2; exit 1; }
done
for required in "$tool_dir/tests/StaleContract/Contract.fs" "$tool_dir/tests/StaleContract/StaleContract.fsproj" "$tool_dir/tests/StaleContract/bin/Debug/net10.0/Fidelity.PSG.dll" "$tool_dir/tests/InProject/InProject.fsproj" "$tool_dir/tests/InProject/Ensure.fs" "$tool_dir/tests/InProject/Cases.fs" "$tool_dir/tests/project-control.sh" "$tool_dir/analyze-alex.sh" "$repo_dir/artifacts/alex-analyzers-tests/inproject-control/truth.sha256"; do
  binding=$(sha256sum "$required")
  rg -Fqx "$binding" "$receipt" || { printf 'Truth receipt does not bind %s.\n' "$required" >&2; exit 1; }
done
for required in "$tool_dir/tests/AliasFixture/Aliases.fs" "$tool_dir/tests/AliasFixture/AliasFixture.fsproj" "$tool_dir/tests/AliasFixture/packages.lock.json" "$tool_dir/tests/AliasFixture/bin/Debug/net10.0/AliasFixture.dll"; do
  binding=$(sha256sum "$required")
  rg -Fqx "$binding" "$receipt" || { printf 'Truth receipt does not bind %s.\n' "$required" >&2; exit 1; }
done
mkdir -p "$repo_dir/artifacts/alex-analyzers-tests"
scratch=$(mktemp -d "$repo_dir/artifacts/alex-analyzers-tests/project.XXXXXXXX")
dotnet msbuild "$project" -getItem:Compile > "$scratch/items.json"
dotnet fsi "$tool_dir/ProjectInputs.fsx" "$scratch/items.json" "$scratch/inputs.txt"
mapfile -t sources < "$scratch/inputs.txt"
cd "$tool_dir"
set +e
dotnet fsharp-analyzers --project "$project" --analyzers-path "$tool_dir/bin/Debug/net10.0" \
  --include-analyzers "${analyzers[@]}" --include-files "${sources[@]}" --fail-on-type-errors --verbosity d --report "$scratch/report.sarif" \
  2>&1 | tee "$scratch/analysis.log"
status=${PIPESTATUS[0]}
set -e
[[ -f "$scratch/report.sarif" ]] || { printf 'Analyzer did not produce SARIF (exit %s).\n' "$status" >&2; exit 1; }
if [[ $status -ne 0 ]]; then printf 'Analyzer exited %s; see %s.\n' "$status" "$scratch/analysis.log" >&2; fi
dotnet fsi "$tool_dir/VerifyProjectLog.fsx" "$scratch/inputs.txt" "$scratch/analysis.log" "$mode"
printf 'Evidence: %s\n' "$scratch"
exit "$status"
