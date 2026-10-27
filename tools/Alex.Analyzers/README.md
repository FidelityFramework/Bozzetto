# Alex analyzer tooling

Bozzetto hosts these development-only F# analyzers outside Alex's production dependency boundary. For Alex work, declare the primary project and run the required combined gate:

```sh
ALEX_ANALYZER_PROJECT=/home/hhh/repos/Alex/src/Alex/Alex.fsproj \
  bash /home/hhh/repos/Bozzetto/tools/Alex.Analyzers/analyze-alex.sh
```

The command uses Bozzetto's renewing lease client for builds, truth controls and analysis. It runs the Alex boundary check first, then the required combined shared and Alex check. A boundary-only pass is a narrowed result; a combined failure remains a failure. The tool truth suite is an ordinary pipeline step, not a TRUST tier.

AXA001 detects construction of the published `Revision` and `SemanticNode` records. AXA002 detects the scoped nominal-name admission pattern in the `ensure` predicate, directly or through one local binding. The analyzer refuses when the referenced PSG contract lacks its target records or name field. Method-call predicates, module-name checks, two-hop locals, other guards and helper calls remain outside that rule; the truth receipt labels three such cases as known misses.

The shared baseline currently detects null, selected .NET APIs and imports, task builders, authored `box` references and selected implicit widening. It does not yet establish the full dependency boundary: unrelated foreign assemblies and cross-assembly abbreviations can escape detection. Already-`obj` declarations and several implicit widening shapes, including annotated bindings and returns, tuples, branches and multi-argument calls, remain open. Ordinary interpolated strings consumed as strings have a positive control; a direct interpolated-string-to-`obj` argument has a negative control. Absence of findings is not proof that these gaps are absent.

Run `bash tests/run.sh --pipeline` only after building the fixtures and running `bash tests/project-control.sh`; the wrapper and pipeline do this in the required order. Truth receipts bind the analyzer, project input and coverage verifiers, fixtures and tooling configuration. The current combined gate is red and must be adjudicated from its actual diagnostics, without treating their count as a count of confirmed defects.
