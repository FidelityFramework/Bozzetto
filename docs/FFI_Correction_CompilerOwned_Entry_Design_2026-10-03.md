# Compiler-owned entries in interior aggregates

October 3, 2026. W3 of the [auditor return](FFI_Correction_Auditor_Return_2026-10-03.md#work-order)
repairs the positive `FnPtr.ofFunction` record cases under
[R1 and D6(b)](FFI_Correction_Rulings_2026-10-03.md). Baker is to settle a portable
receiving convention for compiler-owned entries and retain the code's residence
in its program image. The implementation follows the
[FFI §3.4 amendment](../../clef-lang-spec/spec/ffi-boundary.md#34-fnptroffunction)
and the existing callable-component rules in §3.6.

The first source case has a single module function with a scalar signature:

```fsharp
type Listener = { Entry: FnPtr<bool -> bool> }
let callback (enabled: bool) : bool = enabled
let listener = { Entry = FnPtr.ofFunction callback }
```

Its interior `Entry` component occupies zero bytes. The callable row retains
`NativeEntry` kind, an exact receiving contract and explicit environment absence.
The standalone boundary type retains its declared `PlatformWord` layout.
Foreign invocation, native listener-table projection and conversion to an address
remain separate commitments requiring their own admitted contracts.

## Source evidence

The following observations are R-graded source evidence at clef `8e8cc82`, the
pushed recovery checkpoint. Retrieval snapshot
`6ad6568fc41861572a5793f361ad82fc0a8ce45f37fd7f8a1a558ac9aeb0b3ef`
was current when read. These observations establish the starting implementation.
The changes described below require executed acceptance records.

| Source | Observed behavior |
| --- | --- |
| [`FunctionPointers.fs`](../../clef/src/Compiler/PSGSaturation/SemanticGraph/FunctionPointers.fs), lines 42 to 54 | `FnPtr.ofFunction` resolves a reference to a module binding with one capture-free lambda child. It records `Address(symbol, lambda)`. |
| [`CallableCarriers.fs`](../../clef/src/Compiler/PSGSaturation/SemanticGraph/CallableCarriers.fs) | The census constructs ordinary carriers from admitted `TFun` occurrences. |
| [`CallableContracts.fs`](../../clef/src/Compiler/Baker/Ingredients/CallableContracts.fs) and [`CallableContractRecipes.fs`](../../clef/src/Compiler/Baker/Recipes/CallableContractRecipes.fs) | Signature selection and receiving-contract production accept ordinary carriers. Physical representations come from the settled numeric domain. |
| [`CallableAggregateRecipes.fs`](../../clef/src/Compiler/Baker/Recipes/CallableAggregateRecipes.fs) | A retained native entry requires lifetime participants. The current check rejects an empty list. |
| [`Placement.fs`](../../clef/src/Compiler/PSGSaturation/SemanticGraph/Placement.fs) | Native-entry components remain pending even when an aggregate contract exists. |
| [`CallableAggregateAccounts.fs`](../../clef/src/Compiler/Baker/Ingredients/CallableAggregateAccounts.fs) | Dependency accounts preserve source kind, children, type and obligation anchors. They omit the node's `Parent` field. |

LAN-generated retrieval requests identified `FunctionPointers.fs` as the current
admission owner. The retained local evidence is under
`~/.cache/bozzetto/evidence/ffi-correction-2026-10-03/auditor-return/lan/`.
`W3-functionpointers-source.response.json` contains the retrieved source.
`lemonade-w3.response.json` is an A-graded design critique. Its useful questions
concern typed lifetime evidence and conversion coverage. Its assertion that the
current production lifetime lists contain numeric `Source` participants is
incorrect: the inspected producer initializes them to an empty list.

## Contract extension

Coordinate this addition with W2's contract-owned
`SourcePremises: Map<NodeId, BoundarySourcePremise>` and the planned schema 18
generation. The following proposed shapes extend `CallableContract` in the
public PSG and its Clef mirror:

```fsharp
type ProgramImageCodeLifetime = {
    Module: NodeId
    Binding: NodeId
    Implementation: NodeId
}

type CallableConversion =
    | Identity of ValueRepresentation

type CompilerOwnedCallableConvention = {
    Target: BoundaryPlatformPremise
    ParameterConversions: (int * CallableConversion) list
    ResultConversion: CallableConversion
    CodeLifetime: ProgramImageCodeLifetime
}

type CallableConvention =
    | Ordinary
    | CompilerOwnedPortable of CompilerOwnedCallableConvention

// Additional CallableContract fields:
// Convention: CallableConvention
// SourcePremises: Map<NodeId, BoundarySourcePremise>  // W2
```

`CompilerOwnedPortable` has a fixed meaning: the entry and its admitted interior
users share the source-settled portable calling convention. `NativeEntry` remains
a distinct callable kind. This convention grants no foreign ABI or address
conversion authority. A future foreign-entry convention requires its declared
ABI, conversions and loading obligations.

The recipe copies `Target` from the current numeric domain's
`BoundaryPlatformPremise`. It reads the actual physical lambda parameters and
body result from that same validated domain. Conversion ordinals must equal the
physical parameter ordinals in declaration order. Each identity conversion must
match the corresponding selected representation exactly. The result conversion
has the same requirement. Target or representation changes withdraw the earlier
convention before witnessing.

The source reader compares the complete target premise with the current
platform. Public PSG integrity checks the target dimensions available on the
revision: `Pointer` and `Register`. The published numeric projection contains no
second complete platform premise against which to compare the remaining fields.

The initial producer admits scalar signatures with explicit environment absence
and no resource-bearing parameters or results. It preserves every parameter.
An erased parameter, a nonidentity conversion or a resource-bearing signature
requires the relevant source-owned rule before admission. This batch creates no
ownership default or empty resource proof. Scalar signatures impose no foreign
resource obligation.

Contract selection groups by callable kind and implementation. Native and
ordinary occurrences of one implementation retain distinct contracts. Reuse
requires equal complete convention content and current source support.

## Program-image residence

Baker is to derive `ProgramImageCodeLifetime` from the current source graph:

1. The formation is an application of the resolved `FnPtr.ofFunction` intrinsic.
2. Its argument resolves through admitted annotations to an actual binding
   reference. The binding is immutable and has the implementation as its sole
   child.
3. The binding's parent is the exact `ModuleDef`, and that module's member list
   contains the binding. Both directions of this incidence are checked.
4. The implementation is the actual capture-free lambda. Its typed formals and
   result agree with the resolved `FnPtr` payload after the existing physical
   boundary checks.
5. The containing source module belongs to the current compilation. The code is
   resident for uses admitted within that program image. Export or retention
   beyond the image requires a separate loading contract.

The typed residence fact identifies the three declaration nodes. Its source
premises retain their exact structural observations, including the binding's
parent. Carrier lifetime participants use `CallableLifetime` with the receiving
contract as their group and a fixed order: module, binding, implementation.
The contract's participant account includes the same residence support. A node's
presence alone does not establish this relation.

An owning ingredient reobserves these premises before the aggregate account is
authored or renewed. Changing `Binding.Parent` while preserving its identity and
children must invalidate the earlier residence fact. W2's current source-premise
reader supplies the structural comparison. The native ingredient additionally
checks the typed module-binding-lambda relation.

The existing `Address` plan remains an operation plan. Its symbol spelling
establishes neither code residence nor native ABI compatibility. Ordinary closure
environment residence continues through its existing obligations.

## Producer and consumer placement

| Owner | Change |
| --- | --- |
| Baker ingredient | Factor the current module-entry admission into a current-graph reader shared with `FunctionPointers`. Read the target and scalar representations after numeric settlement. Reobserve program-image residence and exact source premises. |
| Carrier census | Author a `NativeEntry` carrier at the actual admitted formation and transport it through checked aliases. Retain the formation occurrence, implementation, explicit environment absence and contract-grouped lifetime participants. Share physical-lambda validation with the ordinary path. |
| Contract recipe | Produce the compiler-owned portable convention from the ingredient. Record identity conversions and the target premise. Install the contract and its source evidence through the existing fold. |
| Aggregate recipe and account | Require the current typed native convention and residence support. Include the new convention and source premises in the dependency account. Report missing lifetime as CCS8101 and mismatched receiving contracts as CCS8413. |
| Placement | Admit the same selector/environment data protocol for a validated native component. A singleton without an environment has zero bytes. Wider families require an authored selector covering exactly their alternatives. |
| PSG publication and integrity | Copy the selected rows. Check callable kind, conversion ordinals, exact representations, environment absence and typed residence incidence. Check lifetime participants against the corresponding contract's source premises. |
| Alex and Composer | Native aggregate reconstruction, function-address emission and indirect native invocation retain their existing refusals in this batch. The accepted milestone is source placement and valid public rows. Passive reconstruction and target ABI realization require their later consumer changes. |

The recipe has one forward fold after numeric settlement. Each later rewrite
maintains the source facts it changes. Account renewal compares those current
facts. This follows
[Baker Saturation Architecture §3](../../clef/docs/fidelity/Baker_Saturation_Architecture.md#3-fan-out-and-fold-in)
and adds no replay of earlier pipeline stages. Changed support invalidates the
dependent selection and proof receipts through the existing differential
recompilation account.

## Controls and acceptance

The two 32-bit and 64-bit native-record cases must run through target-aware
settlement with declared platform authority. This setup establishes the numeric
domain before contract production and placement. Keep the positive source program and its
interior invocation. The expectations change under FFI §3.6 item 1, “A
single-entry family needs no code storage,” and item 2's receiving-contract
requirement. The test checks fully placed zero-byte `Entry` data, `NativeEntry`
kind, exact contract identity, environment absence and typed lifetime support.
Keep the standalone `TypeLayout.PlatformWord` assertion.

The ordinary `Work` cases likewise use the settled pipeline and expect a
zero-byte closed singleton plus a placed `Tail`. The governing Closure
Representation §2.4 sentence is: “A slot without an environment and with one
alternative occupies no component storage.”

| Control | Required observation |
| --- | --- |
| Valid scalar native record on both target widths | Complete contract and residence evidence, zero component bytes, current published dependency account. |
| Missing native lifetime | Remove the lifetime support from an otherwise valid carrier. Aggregate commitment reports CCS8101. |
| Forged lifetime support | Substitute an unrelated live node while keeping the list nonempty. Source admission and public integrity reject it. |
| Changed binding parent or module membership | Change one side of the incidence while keeping declaration identities and callable output unchanged. The old account and witness authority are rejected. Inconsistent incidence remains refused after attempted renewal. A coherent relocation would require a separate control with both sides updated. |
| Changed target or conversion representation | Retain the old contract while changing the selected premise. Reuse is refused. |
| Ordinary/native contract substitution | Equal source types or physical representations do not admit the wrong kind or convention. |
| Explicit scalar-only contract | Resource ownership is neither invented nor required. A resource-bearing signature remains unadmitted by this producer. |
| Native emitter refusal | Existing unconditional function-address and indirect-call refusals remain. This control establishes the current admission limit. Convention discrimination at a foreign crossing requires the later native ABI implementation. |

Register the controls in the ordinary test suites. Run focused W3 checks first,
then follow the auditor's acceptance order after W1, W2 and W3 each meet their
focused exit conditions. Record executed counts and changed failure names in the
evidence file. Native execution acceptance requires its later Composer gate.

## W3 implementation placement

`CompilerOwnedEntries` is the current-graph ingredient shared by the carrier
census and `FunctionPointers`. Its declaration index is scoped to one immutable
graph. The reader checks the module's member list and the binding's parent,
then resolves the actual capture-free lambda. The existing address plan retains
its operation-only role.

The census authors provisional native carriers. After numeric settlement,
`CallableContractRecipes` supplies the portable convention and the exact
contract-grouped lifetime vector in the same fold. The existing scoped
`renewForSites` fold continues to renew only signature families affected by
aggregate representation changes. W2's source-premise reader checks current
structural evidence, and `CallableContracts` additionally checks program-image
incidence and the selected target.

Code residence uses a typed structural premise. Its controls withdraw the
retained aggregate account and witness authority. They create no SMT obligation.
The separate W2 numeric control exercises retirement of an actual numeric claim
after changed support with an unchanged result.
