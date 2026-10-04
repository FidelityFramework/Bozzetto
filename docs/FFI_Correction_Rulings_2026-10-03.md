# FFI correction: owner rulings and working principles

October 3, 2026. This record holds the owner's rulings and working principles for the FFI
boundary correction, so that the implementing agent and the auditor work from the same text.
It does not replace the governing contract,
[`clef-lang-spec/spec/ffi-boundary.md`](../../clef-lang-spec/spec/ffi-boundary.md). It records how
the owner has directed that contract to be applied, where it needs amendment, and the principles
that govern the work. Quotations of the owner are lightly edited for typos only. Rulings marked
"auditor, forwarded by the owner" were written by an auditing agent and adopted by the owner.

The [implementor guide](FFI_Correction_Implementor_Guide_2026-10-03.md) is the entry point for the work these rulings govern.

## Governing principle

> Every native callable must retain an authoritative boundary contract. Baker establishes
> compatibility with that contract, PSG preserves the evidence, Alex witnesses the settled
> operation, and the target pathway realizes its native representation.

## Native callable rulings (auditor, forwarded by the owner)

**R1. Compiler-owned `FnPtr` families: qualified yes.** Baker may choose an entry contract for a
family of compiler-owned native entries from the selected target's declared capabilities. It must
still settle the complete parameter and result representations, conversions and code lifetime.
"Use the target's standard convention" alone leaves widths, signedness and result adaptation
unresolved.
- One compatible contract across the family. Every origin and every indirect invocation agree; a
  call with small constants must not independently narrow its ABI while the entry keeps wider
  parameters.
- Adapters are established by Baker. Alex receives the settled construction and builds none.
- "Complete target knowledge" and "no source-level storage" are initial admission limits, diagnosed
  as unsupported. They are not permanent language laws. A foreign callable can eventually have an
  established contract without its implementation being statically known. Backend register spills
  are not source-level escape.
- Ordinary Clef functions keep the flat-closure machinery. `FnPtr` remains an explicitly admitted
  native entry.
- This needs an explicit spec amendment (ffi §3.4 and related text): Baker may establish a
  compiler-owned entry contract, with all obligations preserved.

**R2. `FnPtr.ofExtern binding` replaces bare-string `FnPtr.fromSymbol`.**
- `FnPtr.ofExtern LibC.abs` resolves the actual extern binding and obtains its descriptor, library
  identity, symbol and boundary obligations. Its logical function type is inferred from the binding.
- Taking the entry itself demands settlement of the import. It must not depend on an unrelated
  direct call having admitted that import.
- It identifies the declared foreign entry, including any necessary declared adapter, and never an
  application-facing Clef wrapper.
- A string lookup was rejected: it would still change `fromSymbol`'s specified meaning, and string
  selection needs scope, duplicate-symbol and library-identity rules that declaration identity
  already provides. The spec's §3.2, §5.3, §6 examples and §7 summary change accordingly.

**R3. Contracts carry more than the ABI.** Two values can have the same `FnPtr<'F>` type, and even
the same machine signature, while carrying different resource obligations: one borrows a handle,
another consumes and releases it. Taking an extern's address must not discard that distinction.
Aliases, conditional branches, records and indirect invocation preserve the applicable contract and
obligations; an unresolved combination is rejected or explicitly adapted. A callback's code entry
does not establish its environment's lifetime. A `CHandle` does not establish the bounds needed for
`Ptr<'T, 'Region, 'Access>` (ffi §5.6).

**R4. `NTUfnptr` is a portable function value.** The NTU table (`ntu-types.md`, near line 229)
still maps `NTUfnptr` to `index`. The FFI and backend-lowering chapters require a portable function
value until target realization. The table must agree with that architecture.

**R5. No invented ownership at any constructor.** Farscape's `Borrowed` default and BAREWire's
`Function.cdecl` default both invent ownership. Missing resource ownership stays unresolved.
Scalar-only operations carry no resource obligation to invent. (Spec basis: ffi §5.6, "Missing
ownership information SHALL NOT be reported as proof that a resource is borrowed.")

**R6. No comment-narrowing of nullable callback slots.** Unannotated and `_Null_unspecified`
function-pointer slots are `Option<FnPtr>` (ffi §5.2). While that optional-entry conversion is
unsupported, the binding is diagnosed before output is written (ffi §5.2, the "unsupported
contract" sentence). A comment cannot establish nonnull evidence. Explicit evidence narrows: a
header nonnull annotation, or a binding declaration with identified provenance recorded as an
external execution hypothesis (ffi §5.1, §5.4). See also the legacy-C principle below.

Final ruling text, verbatim:

> Proceed with Baker-owned settlement of compiler-owned native entries, preserving complete
> contracts and proof dependencies through PSG. Use `FnPtr.ofExtern` to reference described foreign
> declarations, and retire bare-string `fromSymbol`. Keep unsupported storage, absence conversions,
> and resource lifetimes as explicit admission failures until implemented. No tranche may
> manufacture missing evidence or move adapter construction into Alex.

## Review directions adopted for Step 1 (auditor, forwarded by the owner)

- **Contract changes and the shared daemon.** The daemon checks the worker's exact PSG schema,
  format, fingerprint and protocol, so a new worker alone cannot cross a contract change. Build and
  validate a matching daemon-worker snapshot from source, never with the stale installed `boz`.
  Validate it in Bozzetto's owned, isolated daemon harness
  (`Bozzetto.Composer.Tests/LiveProviderTests.fs`) before switching the shared dashboard at the
  integration gate. Report isolated integration acceptance and shared-dashboard acceptance
  separately. The shared switch needs the owner's explicit go-ahead.
- **Two record forms for records holding `FnPtr`.** An interior logical record can carry
  separately represented callable components, preserving their contracts through field selection,
  aliases and branches. A native C listener table needs its declared layout, target-realized
  function-address fields and retention obligations. Baker establishes the projection between
  them. Exempting every `FnPtr`-holding record from byte placement does not solve native tables;
  the callable-component record path is implementation work.
- **Step 1 obligations from the first successful native call:** code lifetime and explicit foreign
  execution hypotheses; complete parameter and result mappings, including justified `unit`
  erasure; invalidation when descriptors or target declarations change; source-through-native tests
  alongside contract-row tests; evidence that lowering preserves the settled calling contract.
- **ABI preservation is a target-pathway check.** Alex's `CoverageValidation` checks traversal
  coverage and cannot establish ABI preservation. The check belongs in the target pathway and
  compares the lowered operation against the target realization of Baker's contract, not portable
  and native signature text.
- **`Option<FnPtr>` needs a real boundary design.** Keep ordinary `Option<FnPtr>` semantics inside
  Clef and specify the admitted boundary conversion that realizes absence as native null. The
  current ffi §4.2 wording conflates native-word comparison with the function-value rules. Until the
  conversion exists, refuse unsupported uses. Neither integer casts nor a silently substituted
  nonnull entry resolves it.
- **Descriptor vocabulary changes** are scheduled together with the affected Fidelity.Platform
  fixtures and generated consumers. An additive resource descriptor may ease migration, but must
  retain exact declaration identity and must not inherit an invented `Borrowed` default.
- **Code lifetime follows the actual loading contract.** "Shared library" alone does not imply
  refusal; process-lifetime libraries and unloadable plugins have different obligations.
- **Test frameworks.** The FFI repositories use xUnit (Calque and Incremental use NUnit; BAREWire
  has its own harness). Bozzetto uses Expecto and FsCheck, and its unfiltered acceptance requires a
  `TRUST … verdict=Trusted` line (`Bozzetto/AGENTS.md`, Testing).

## Owner working principles

**Clean baseline first.**
> While I want the focus here to be FFI, I really feel like all of those clef failures need to be
> addressed along with the others. This is important enough that we really do (in this case) need
> to start from a clean baseline.

**Smooth seams; no component is fixed.**
> I'm not married to Bozzetto's implementation - it all changes to suit the needs of the
> framework. The point of building these components together is coordinated alignment and "smooth
> seams" between them.

Where a component's current mechanism creates friction at a seam, change that component, with its
change set, tests and acceptance, rather than designing a workaround around it. Smoothing never
means collapsing a seam: each component keeps its responsibility, and safety intents (for example,
refusing a mismatched contract loudly) stay enforced.

**Bozzetto sits outside the compiler pipeline.**
> I'm still surprised that a daemon is given this much weight. The point of having Bozzetto outside the pipeline is
> for this latitude to exist.

Compiler and FFI work is accepted on component gates only: the unfiltered suites, the Composer runners and Farscape
conformance. Adapting Bozzetto to a new compiler contract is an independent track. Switching the shared daemon is an
operational step the owner triggers, reported separately, and it never blocks a compiler phase.

**The spec is primary, and the relationship is multi-way.**
> The thing that I think is most correct is the spec - but it's a multi-way relationship! The
> "realities on the ground" may override a theoretical decision that "seems good on paper". Dealing
> with 50-plus-year-old C decisions is one of those places where we do our best to maintain
> principles and acknowledge that there may be limits to what we would want to dictate to legacy
> applications/code bases.

When a rule meets legacy C reality, first look for the principled accommodation: declared evidence
with provenance (binding declarations from documented contracts, GObject-Introspection transfer
annotations), recorded as an external execution hypothesis; a Baker adapter; or an explicitly
documented limit. If the rule itself is impractical, propose a spec amendment with the API evidence.
Never manufacture evidence in either direction. Expected pressure points: unannotated callback
slots across libc and GLib; callbacks without `userdata` or destroy hooks (`qsort`, `atexit`, signal
handlers, `pthread_create`); pointer-identity comparisons (`CHandle` equality); intrusive structures
(`wl_listener` with `container_of`); varargs (`printf`).

**Evidence is graded, not binary.**
> Every element of the information landscape you're on is on a gradient somewhere between 0.0 and
> 1.0.

State how each load-bearing claim is known: executed in an unfiltered run; read in code at a pinned
commit; stated by the spec; observed live (time-bound); reported by a single agent; docs or site
prose; small-model output. Spend verification where impact times uncertainty is highest. A repeated
claim does not become a fact by repetition. Owner rulings carry the highest authority on intent and
remain revisable by evidence.

## Compiler terminology (owner ruling, October 3, 2026)

The owner names Composer a **differential compiler** and its scoped or segmented
rebuilding **differential recompilation**. Use these terms in active design and
requirement prose to distinguish compiler work from the Clef `Incremental<'T>`
and `Observable` language surfaces. Changed dependency accounts identify the
affected compiler work; retained results still require current proof and
publication authority.

This is a terminology ruling. It does not rename existing APIs, projects such
as `Fidelity.FSharp.Incremental`, or historical records, and it does not change
the responsibilities of Baker, PSG, Alex or the target pathway.

## Standing repository rules that bear on this work

- Work lands on `main` in every repository; no side branches or worktrees. The owner reviews
  changes; commit messages are one line, `type(scope): what`.
- Acceptance is the unfiltered suite of each touched repository, run under Bozzetto leases
  (`acquire_full_build_lease`, `acquire_test_suite_lease`, `release_work_lease`). A filtered run is
  inner-loop only. Never add a gate reachable only by a name filter.
- Never stop or restart the shared Bozzetto daemon without the owner's go-ahead, and never through
  the installed `boz` release, which predates clef `a6614b5` and would silently drop its guards.
- Interior Clef has no raw pointer type; `CHandle<'T>` grants no dereference, offset or integer
  conversion. Clef admits no .NET `task`. SageFS is out of scope.
- Fidelity.FSharp.Incremental carries demand, invalidation and eligibility only, never proof,
  publication or launch authority.

## Owner decisions on the Step 1 plan (October 3, 2026)

The owner put leanings to another agent, received the recommendation below, and agreed with it. The owner added
two details: D6(a) would leave ordinary closures and callable union payloads pending beyond Step 1, because Step 1
implements only `FnPtr` record fields; and D1's protected-file approval assumes a filesystem boundary this
environment does not have, since agent sessions here have unrestricted filesystem access.

| Decision | Ruling |
|---|---|
| D6, callable aggregates | **(b)** Move the shared foundation earlier and repair the affected positive cases. |
| D1, deployment | **(a)** Bootstrap once on today's compiler, then replace workers without restarting the daemon while the wire contract stays compatible. |
| D1, approval | **(iii)** The owner's explicit go-ahead, tied to the reviewed manifest, with an audit record of the selection and the permitted rollback. |
| D2, shared switches | Each switch of the shared daemon needs the owner's go-ahead. Standing rule. |
| D3, old desktop repositories | **(a)** Freeze Fidelity.Gtk3, Fidelity.GObject and Fidelity.WebKit as legacy and regenerate maintained bindings into `Fidelity.Platform`. |
| D4, equality | Refuse `FnPtr` equality for now. Settle `CHandle` equality together with its resource contract (Step 3). |
| D5, integer promotion | Defer the C ABI integer-promotion field, and keep refusing unsupported native carriers explicitly. |

**D6(b) in practice.** Implement one callable-aggregate protocol with the code, environment and lifetime evidence it
needs, covering `FnPtr` record fields, ordinary function fields and callable union payloads. Keep the distinction
between ordinary flat closures and native entries. Make the affected original positive tests pass rather than
converting them into refusals. Build native ABI settlement on that foundation. This enlarges the clean-baseline
repair and moves the Fidelity.PSG schema change earlier. Option (c), a second mechanism, is excluded. Had the owner
chosen (a), the honest status would have been "baseline with tracked unsupported capabilities", with the positive
acceptance obligations retained.

**D1 in practice.** The daemon drops its unnecessary dependency on the PSG contract and keeps exact checks on the
wire contract it actually interprets. Worker-loaded PSG identity and distribution bytes stay checked against the
selected manifest. A content hash identifies a distribution. It does not establish who approved it, so no
file-based approval is presented as user-only in this environment. A dashboard approval control may come later for
convenience, but a local endpoint alone does not establish human-only authorization either. "No daemon restart"
still ends compiler sessions during a worker replacement: preserve the shared project list and reopen fresh sessions
so every participant returns to the same work.

**Guiding distinction (the recommendation's closing line, adopted):** defer unsupported functionality honestly,
but move shared foundations earlier when they are required to deliver the working language surface the owner wants.

## Open owner decisions

Listed in the [implementor guide](FFI_Correction_Implementor_Guide_2026-10-03.md#open-owner-decisions) until the tracker lands.
