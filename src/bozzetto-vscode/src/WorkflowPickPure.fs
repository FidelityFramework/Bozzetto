/// Pure decisions behind "Switch Workflow".
///
/// No Fable dependency; tested under `dotnet fsi`
/// (tests/WorkflowPickContractTests.fsx).
module Bozzetto.Vscode.WorkflowPickPure

/// One workflow the user can switch into.
type WorkflowChoice = {
  /// The value POSTed to the daemon. Must parse via `SessionWorkflow.tryOfString`.
  Wire: string
  /// `SessionWorkflow.label` for that case, verbatim — this is what the status
  /// bar will read afterwards, so it is what the picker must say now.
  Label: string
  /// The codicon id shown beside the label.
  Icon: string
  /// One line on what the mode actually costs and gives.
  Detail: string
}

/// Every case of `SessionWorkflow`. A case missing here is a mode the user
/// cannot reach from VS Code at all.
let choices: WorkflowChoice list = [
  { Wire = "Interactive"
    Label = "REPL"
    Icon = "notebook"
    Detail = "Interactive evaluation without test-on-save." }
  { Wire = "LiveTesting"
    Label = "Live Testing"
    Icon = "beaker"
    Detail = "Full REPL, and affected tests re-run as you type." }
]

/// One rendered quick-pick row.
type PickRow = {
  /// What the row reads, codicon token included — `label` is one of the two
  /// QuickPickItem fields where VS Code expands `$(...)`.
  Label: string
  /// The right-hand hint. Carries the "current" marker, so the picker can
  /// always answer "which one am I in?" without a second lookup.
  Description: string
  Detail: string
  Wire: string
  IsCurrent: bool
}

/// Build the rows for a session currently in `currentLabel` (a
/// `SessionWorkflow.label` value, e.g. what `/api/sessions` sends as
/// `workflowLabel`). An unrecognised current label simply marks nothing —
/// it never hides a choice.
let rows (currentLabel: string) : PickRow list =
  choices
  |> List.map (fun c ->
    let isCurrent = c.Label = currentLabel
    { Label = sprintf "$(%s) %s" c.Icon c.Label
      Description =
        match isCurrent with
        | true -> "current"
        | false -> ""
      Detail = c.Detail
      Wire = c.Wire
      IsCurrent = isCurrent })

/// Resolve a picked row's label back to the wire value. Total: an unknown
/// label yields `None` rather than defaulting into a workflow the user did
/// not choose.
let wireOfPickedLabel (pickedLabel: string) : string option =
  rows ""
  |> List.tryFind (fun r -> r.Label = pickedLabel)
  |> Option.map (fun r -> r.Wire)

/// The user-facing label for a wire value, for the confirmation message.
let labelOfWire (wire: string) : string option =
  choices |> List.tryFind (fun c -> c.Wire = wire) |> Option.map (fun c -> c.Label)
