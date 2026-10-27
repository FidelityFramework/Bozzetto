#!/usr/bin/env bash
set -euo pipefail

pipeline=${1:-}
[[ -z "$pipeline" || "$pipeline" == --pipeline ]] || { printf 'Unknown truth-suite argument.\n' >&2; exit 2; }
if [[ "$pipeline" == --pipeline && -n ${ALEX_ANALYZER_ONLY:-} ]]; then
  printf 'Pipeline truth suite cannot be filtered.\n' >&2
  exit 2
fi

tool_dir=$(cd "$(dirname "$0")/.." && pwd)
repo_dir=$(git -C "$tool_dir" rev-parse --show-toplevel)
psg_dll=${ALEX_ANALYZER_PSG_DLL:-$tool_dir/tests/ContractFixture/bin/Debug/net10.0/Fidelity.PSG.dll}
impostor_dll=$tool_dir/tests/ImpostorFixture/bin/Debug/net10.0/Unrelated.Contract.dll
alex_dll=$tool_dir/tests/AlexFixture/bin/Debug/net10.0/Alex.dll
stale_dll=$tool_dir/tests/StaleContract/bin/Debug/net10.0/Fidelity.PSG.dll
analyzers=${ALEX_ANALYZER_PATH:-$tool_dir/bin/Debug/net10.0}
[[ -f "$psg_dll" ]] || { printf 'Missing PSG contract assembly: %s\n' "$psg_dll" >&2; exit 1; }
[[ -f "$impostor_dll" ]] || { printf 'Missing impostor assembly: %s\n' "$impostor_dll" >&2; exit 1; }
[[ -f "$alex_dll" ]] || { printf 'Missing Alex fixture assembly: %s\n' "$alex_dll" >&2; exit 1; }
[[ -f "$stale_dll" ]] || { printf 'Missing stale contract assembly: %s\n' "$stale_dll" >&2; exit 1; }
if [[ -z ${ALEX_ANALYZER_ONLY:-} ]]; then
  sha256sum --check --status "$repo_dir/artifacts/alex-analyzers-tests/inproject-control/truth.sha256" || { printf 'In-project control receipt is missing or stale.\n' >&2; exit 1; }
fi
mkdir -p "$repo_dir/artifacts/alex-analyzers-tests"
scratch=$(mktemp -d "$repo_dir/artifacts/alex-analyzers-tests/run.XXXXXXXX")
printf '#r "%s"\nlet forward (r: Fidelity.PSG.Revision) = r\n' "$psg_dll" > "$scratch/forward.fsx"
printf '#r "%s"\nlet change (r: Fidelity.PSG.Revision) = { r with Header = r.Header }\n' "$psg_dll" > "$scratch/revision.fsx"
printf '#r "%s"\nlet change (n: Fidelity.PSG.SemanticNode) = { n with Kind = n.Kind }\n' "$psg_dll" > "$scratch/semantic-node.fsx"
printf '#r "%s"\nlet next (step: Fidelity.PSG.OccurrenceBreadcrumb) = { step with Ordinal = step.Ordinal + 1 }\n' "$psg_dll" > "$scratch/breadcrumb.fsx"
printf '#r "%s"\n#r "%s"\nlet change (r: Fidelity.PSG.Revision) = { r with Header = r.Header }\n' "$psg_dll" "$impostor_dll" > "$scratch/impostor.fsx"
printf '#r "%s"\nlet value = 1\n' "$impostor_dll" > "$scratch/missing-contract.fsx"
printf '#r "%s"\nlet value = 1\n' "$stale_dll" > "$scratch/stale-contract.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (t: TypeIdentity) bits = ensure (match t with TypeIdentity.Application(c, []) -> c.Declaration.Name = "Mmio" + string bits | _ -> false) "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-direct.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (c: ConstructorIdentity) = let accepted = c.Declaration.Name = "Mmio8" in ensure accepted "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-local.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (c: ConstructorIdentity) = ensure true c.Declaration.Name\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-message.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nlet display (c: ConstructorIdentity) = c.Declaration.Name\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-display.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nlet ensure condition message = if condition then () else failwith message\nlet check (c: ConstructorIdentity) = ensure (c.Declaration.Name = "Mmio8") "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-lookalike.fsx"
printf '#r "%s"\n#r "%s"\nopen Alex.XParsec.PSGCombinators\nlet check bits = ensure (bits = 8 || bits = 16 || bits = 32) "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/published-width.fsx"
printf '#r "%s"\n#r "%s"\nopen Alex.XParsec.PSGCombinators\nlet check (operation: string) = ensure (operation.StartsWith "reg") "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/published-string.fsx"
printf '#r "%s"\n#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (c: ConstructorIdentity) = ensure (c.Declaration.Name = "Mmio8") "bad"\n' "$psg_dll" "$impostor_dll" "$alex_dll" > "$scratch/nominal-impostor.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (c: ConstructorIdentity) = ensure (c.Declaration.Name.StartsWith "Mmio") "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-method.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (c: ConstructorIdentity) = ensure (c.Declaration.Module = "M") "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-module.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (c: CaptureInfo) = ensure (c.Name = "Mmio8") "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/other-name-field.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (c: ConstructorIdentity) = let a = c.Declaration.Name = "Mmio8" in let b = a in ensure b "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-two-hop.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (c: ConstructorIdentity) table = let row = Map.tryFind c.Declaration.Name table in ensure row.IsSome "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-lookup.fsx"
printf '#r "%s"\n#r "%s"\nopen Fidelity.PSG\nopen Alex.XParsec.PSGCombinators\nlet check (c: ConstructorIdentity) table = ensure (Map.tryFind c.Declaration.Name table |> Option.isSome) "bad"\n' "$psg_dll" "$alex_dll" > "$scratch/nominal-lookup-direct.fsx"
printf 'let value: string = null\n' > "$scratch/null.fsx"
printf 'let check (s: string) = match s with null -> 0 | _ -> 1\n' > "$scratch/null-pattern.fsx"
printf 'open System\nlet value = 1\n' > "$scratch/system-open.fsx"
printf 'open type System.Math\nlet value = 1\n' > "$scratch/system-type-open.fsx"
printf 'let run () = task { return 1 }\n' > "$scratch/task.fsx"
printf 'let builder = task\nlet run () = builder { return 1 }\n' > "$scratch/task-alias.fsx"
printf 'let task = 1\nlet run () = task + 1\n' > "$scratch/task-lookalike.fsx"
printf 'type P = int -> int\nlet call (p: P) = p 1\n' > "$scratch/abbreviation.fsx"
printf 'type Builder = System.Text.StringBuilder\nlet create () = Builder()\n' > "$scratch/bcl-alias.fsx"
printf 'let value = box 1\n' > "$scratch/box.fsx"
printf 'let widen = box\nlet value = widen 1\n' > "$scratch/box-alias.fsx"
printf 'let value = Microsoft.FSharp.Core.Operators.box 1\n' > "$scratch/box-qualified.fsx"
printf 'let box x = x + 1\nlet value = box 1\n' > "$scratch/box-lookalike.fsx"
printf 'let value = ("x" :> obj)\n' > "$scratch/upcast.fsx"
printf 'let consume (value: obj) = ()\nlet result = consume 1\n' > "$scratch/implicit-argument.fsx"
printf 'type Row = { Value: obj }\nlet row = { Value = 1 }\n' > "$scratch/implicit-field.fsx"
printf 'let values: obj list = [1; 2]\n' > "$scratch/implicit-list.fsx"
printf 'let identity (x: obj) = x\n' > "$scratch/object-identity.fsx"
printf 'let read = System.IO.File.ReadAllText\n' > "$scratch/file-reference.fsx"
printf 'let make () = new System.IO.FileStream("x", System.IO.FileMode.Open)\n' > "$scratch/file-stream.fsx"
printf 'let make (stream: System.IO.Stream) = new System.IO.StreamReader(stream)\n' > "$scratch/stream-reader.fsx"
printf 'let traverse (xs: int list) = xs |> List.map (fun x -> Some (x + 1))\nlet text = "null is text"\n' > "$scratch/pure.fsx"
printf 'let render x = sprintf "%%d" x\nlet consume xs = for x in xs do ignore x\n' > "$scratch/generic-pure.fsx"
printf 'let consume (message: string) = message\nlet render (number: int) (reason: string) = consume $"Literal {number}: {reason}"\n' > "$scratch/interpolation-pure.fsx"
printf 'let consume (value: obj) = ()\nlet render (number: int) = consume $"Literal {number}"\n' > "$scratch/interpolation-obj.fsx"
printf 'let invalid =\n' > "$scratch/invalid.fsx"

run_case() {
  local name=$1 expected=$2 positions=${3:-} reason count rc=0 analyzer='Clef production baseline'
  if [[ -n ${ALEX_ANALYZER_ONLY:-} && "$name" != "$ALEX_ANALYZER_ONLY" ]]; then return 0; fi
  executed=$((executed + 1))
  cases+=("$name")
  case "$name" in forward|revision|semantic-node|breadcrumb|impostor|missing-contract|stale-contract|nominal-*|other-name-field|published-width|published-string) analyzer='Alex published boundary' ;; esac
  (cd "$tool_dir" && dotnet fsharp-analyzers --script "$scratch/$name.fsx" --analyzers-path "$analyzers" --include-analyzers "$analyzer" --fail-on-type-errors) > "$scratch/$name.log" 2>&1 || rc=$?
  case "$expected" in
    clean|known-miss) [[ $rc -eq 0 ]] && ! rg -q 'AX[AC][0-9]+' "$scratch/$name.log" ;;
    type-error) [[ $rc -ne 0 ]] && rg -Fq 'did not type check and --fail-on-type-errors was passed' "$scratch/$name.log" ;;
    *)
      case "$expected" in
        AXA001) reason='Alex must not construct or update published PSG records.' ;;
        AXA000) reason='The referenced Fidelity.PSG contract must define Revision, SemanticNode and NominalTypeIdentity.Name.' ;;
        AXA002) reason='Alex must not derive an admission predicate from a nominal declaration name.' ;;
        AXC001) reason='Clef production code must not use null.' ;;
        AXC002) reason='Clef production code must not depend on .NET library APIs:' ;;
        AXC003) reason='Clef production code must not use task computation expressions.' ;;
        AXC004) reason='Clef production code must not widen a value to obj.' ;;
        *) printf 'Unknown expected diagnostic %s\n' "$expected" >&2; return 1 ;;
      esac
      count=$(rg -c 'Error AX[AC][0-9]+' "$scratch/$name.log" || true)
      [[ $rc -ne 0 && $count -eq $(wc -w <<< "$positions") ]] || { printf 'FAIL %s diagnostic count (exit %s; log %s)\n' "$name" "$rc" "$scratch/$name.log" >&2; return 1; }
      for position in $positions; do
        rg -Fq "$scratch/$name.fsx($position): Error $expected : $reason" "$scratch/$name.log" || { printf 'FAIL %s diagnostic at %s (log %s)\n' "$name" "$position" "$scratch/$name.log" >&2; return 1; }
      done
      if [[ $expected == AXC002 ]]; then
        [[ $(rg -c 'Error AXC002 : .+ \(assembly .+\)\.' "$scratch/$name.log" || true) -eq $count ]] || { printf 'FAIL %s missing resolved identity\n' "$name" >&2; return 1; }
      fi
      ;;
  esac || { printf 'FAIL %s %s (exit %s; log %s)\n' "$expected" "$name" "$rc" "$scratch/$name.log" >&2; return 1; }
  if [[ $expected == known-miss ]]; then known_misses+=("$name"); printf 'KNOWN-MISS %s\n' "$name"; else printf 'PASS %s\n' "$name"; fi
}

executed=0
cases=()
known_misses=()
run_case forward clean
run_case revision AXA001 '2,40'
run_case semantic-node AXA001 '2,44'
run_case breadcrumb clean
run_case impostor clean
run_case missing-contract AXA000 '1,0'
run_case stale-contract AXA000 '1,0'
run_case nominal-direct AXA002 '5,35'
run_case nominal-local AXA002 '5,84'
run_case nominal-message clean
run_case nominal-display clean
run_case nominal-lookalike clean
run_case published-width clean
run_case published-string clean
run_case nominal-impostor clean
run_case nominal-method known-miss
run_case nominal-module known-miss
run_case other-name-field clean
run_case nominal-two-hop known-miss
run_case nominal-lookup clean
run_case nominal-lookup-direct clean
run_case pure clean
run_case generic-pure clean
run_case interpolation-pure clean
run_case interpolation-obj AXC004 '2,35'
run_case null AXC001 '1,20'
run_case null-pattern AXC001 '1,37'
run_case system-open AXC002 '1,5'
run_case system-type-open AXC002 '1,10'
run_case task AXC003 '1,13 1,27'
run_case task-alias AXC003 '2,13 2,30'
run_case task-lookalike clean
run_case abbreviation clean
run_case bcl-alias AXC002 '1,15 2,16'
run_case box AXC004 '1,12'
run_case box-alias AXC004 '1,12'
run_case box-qualified AXC004 '1,12'
run_case box-lookalike clean
run_case upcast AXC004 '1,13'
run_case implicit-argument AXC004 '2,21'
run_case implicit-field AXC004 '2,20'
run_case implicit-list AXC004 '1,24 1,27'
run_case object-identity clean
run_case file-reference AXC002 '1,21 1,11'
run_case file-stream AXC002 '1,18 1,54 1,44'
run_case stream-reader AXC002 '1,18 1,42'
run_case invalid type-error
[[ $executed -gt 0 ]] || { printf 'No truth case matched.\n' >&2; exit 1; }
if [[ -z ${ALEX_ANALYZER_ONLY:-} ]]; then
  [[ $executed -eq 47 ]] || { printf 'Expected 47 registered cases, ran %s.\n' "$executed" >&2; exit 1; }
  printf '{"executed":%s,"cases":[' "$executed" > "$scratch/truth.json"
  separator=''
  for case_name in "${cases[@]}"; do printf '%s"%s"' "$separator" "$case_name" >> "$scratch/truth.json"; separator=','; done
  printf '],"known_miss":[' >> "$scratch/truth.json"
  separator=''
  for case_name in "${known_misses[@]}"; do printf '%s"%s"' "$separator" "$case_name" >> "$scratch/truth.json"; separator=','; done
  printf '],"sdk":"%s","analyzer_cli":"0.41.0"}\n' "$(dotnet --version)" >> "$scratch/truth.json"
  sha256sum "$analyzers/Alex.Analyzers.dll" "$tool_dir/Analyzer.fs" "$tool_dir/tests/run.sh" "$tool_dir/.config/dotnet-tools.json" "$psg_dll" "$impostor_dll" "$alex_dll" "$stale_dll" "$tool_dir/tests/ContractFixture/Contract.fs" "$tool_dir/tests/ContractFixture/ContractFixture.fsproj" "$tool_dir/tests/ImpostorFixture/ImpostorFixture.fsproj" "$tool_dir/tests/AlexFixture/Ensure.fs" "$tool_dir/tests/AlexFixture/AlexFixture.fsproj" "$tool_dir/tests/StaleContract/Contract.fs" "$tool_dir/tests/StaleContract/StaleContract.fsproj" "$tool_dir/tests/InProject/InProject.fsproj" "$tool_dir/tests/InProject/Ensure.fs" "$tool_dir/tests/InProject/Cases.fs" "$tool_dir/tests/project-control.sh" "$repo_dir/artifacts/alex-analyzers-tests/inproject-control/truth.sha256" "$tool_dir/analyze-alex.sh" "$tool_dir/check-project.sh" "$tool_dir/ProjectInputs.fsx" "$tool_dir/VerifyProjectLog.fsx" "$tool_dir/Alex.Analyzers.fsproj" "$tool_dir/Directory.Packages.props" "$tool_dir/packages.lock.json" "$tool_dir/NuGet.Config" "$scratch/truth.json" > "$scratch/truth.sha256"
  cp "$scratch/truth.sha256" "${ALEX_ANALYZER_TRUTH_RECEIPT_OUT:-$repo_dir/artifacts/alex-analyzers-tests/latest-truth.sha256}"
  printf 'Truth receipt: %s\n' "$scratch/truth.sha256"
fi
printf 'Evidence: %s\n' "$scratch"
