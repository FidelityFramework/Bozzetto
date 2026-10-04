# FFI increment handoff template

One page per increment. Copy this file, fill it in, and delete the guidance
lines. Rationale belongs in code comments or the spec, not here. Grades: **E**
executed, **R** source read, **S** spec, **I** inference.

## Rules for both sides

1. **The implementer runs the adversarial checks before handoff:** the source
   shape matrix, and one mutant per new rule. The auditor does not discover
   these.
2. **The auditor verifies binding, not results.** Reproduce gates only on a
   binding mismatch, a ledger anomaly, or a spot check the owner asks for.
3. **Owner decisions are settled before implementation starts.** A decision
   found mid-increment stops that item, not the whole increment.
4. **One reviewer, one checklist** (below). First-pass reading goes to the LAN
   workers.
5. **The return lists findings only.** Do not restate the handoff.
6. **Retrieval first, grep last.** Locate code through the retrieval service
   before reading files. Retrieval costs more wall clock but far fewer tokens,
   and token budget is the binding constraint.
   - **Sequence:** start with `schema`, and require
     `freshness.fresh = true` with `snapshot_id = current_snapshot_id`. Then
     `find`, `pgq` and `sources`, each pinned to that `snapshot_id`. Cite repo,
     revision, path and the returned lines.
   - **Keep the index current:** it covers pushed heads only. Push checkpoints
     before asking questions about them.
   - **Direct reads are allowed only for:**
     - repositories outside the index (Bozzetto, BAREWire, Calque,
       Fidelity.Data);
     - uncommitted changes, read through `git diff`;
     - an index reported stale;
     - a bounded confirmation of a line retrieval already located.

     Record which case applied.
   - **Never grep a repository wholesale.**
   - Helpers and request shapes are in
     `~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/tools/README.md`.
7. **Growth is justified by the rule ledger.** New code should extend the
   owning pass. A new file, a new owner, or logic duplicated across producer and
   reader (two authorities for one fact) needs a one-line reason in the handoff.

---

## Handoff: `<increment name>`, `<date>`

**Scope:** one sentence naming the plan step and items (for example "Phase B,
B1 and N2-N4").

**Heads** (pushed to main, clean):

| Repository | Head |
| --- | --- |
| `<repo>` | `<full sha>` |

**Gates** (unfiltered, leased, released; paste each ledger line verbatim):

```text
<suite> total=N passed=N failed=N skipped=0 | vs trusted: missing=0 added=N new-failures=0 resolved=N
```

**Binding:** `<evidence dir>/manifest.sha256` covers every changed path at the
heads above. Artifacts were built from the committed heads.

**Rule ledger.** One row per new or changed check, refusal or invariant. Every
row needs all four columns.

| Rule (owner file:line) | Positive source control | Negative source control | Mutant killed (test name) |
| --- | --- | --- | --- |
| `<file:line>`, `<what it enforces>` | `<test>` | `<test>, asserts <code + reason>` | `<mutation> -> <test>` |

**Shape matrix** (check, publish, then `Integrity.check`, for every aggregate
form the increment touches):

| Form | Result |
| --- | --- |
| record / union / tuple / copy-update / nested / recursive / conditional / array | clean / refused `<code>` (intended) / **unexpected** |

Any "unexpected" row blocks handoff.

**Growth** (`git diff --shortstat <base>..<head>` per repository):

| Repository | Files / + / - | New files, each with owning pass and reason |
| --- | --- | --- |
| `<repo>` | `<n> / +<n> / -<n>` | none, or `<file>`: `<owner>`, `<reason>` |

**Context sources:** retrieval snapshot `<snapshot_id>`, plus each direct read
with its allowed case (rule 6).

**Residuals** (refused or unsupported, each with code and tracking item):

- `<form>`: `<code>`, tracked as `<item>`

**Owner decisions needed:** none, or each as a yes/no question with the
governing clause.

---

## Auditor return: `<increment name>`, `<date>`

**Binding check (about a minute; E):**

- Heads equal origin.
- Each manifest verifies.
- Ledger lines match their TRX files.
- Every rule-ledger row names an existing test.

**Checklist** (one reviewer, diff only):

- [ ] Producer and reader agree on every new rule (no reader stricter than
      valid source).
- [ ] Each new rule has positive and negative source controls and a killed
      mutant.
- [ ] Withdrawal is handled as carefully as admission.
- [ ] Each owner maintains the facts it changes; there is no replay, inferred
      fact or fallback.
- [ ] The shape matrix covers every form the diff touches.
- [ ] Growth is justified: there is no new authority for a fact another owner
      already holds.
- [ ] Review context came from retrieval at a fresh snapshot, with direct reads
      only under rule 6.

**Verdict:** accept / accept with follow-up / changes required.

**Findings** (most severe first; findings only):

| # | Severity | Grade | File:line | Defect | Required fix and exit |
| --- | --- | --- | --- | --- | --- |
| 1 | blocker / significant / minor | E/R/S/I | `<file:line>` | `<one sentence>` | `<fix>; exit: <test>` |

**Spot reproduction** (only if run, with the reason): `<suite>: identical / differs`.
