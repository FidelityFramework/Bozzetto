namespace Bozzetto

/// Pure, bounded client-reported work facts. Membership/transport authentication
/// belongs to the cohort and MCP boundaries; no process supervision or journal.
module AgentWork =
  type Usage = {
    Input: int64
    Output: int64
    CacheRead: int64
    CacheWrite: int64
    Total: int64
    EstimateUsd: float option
  }
  type Report = {
    Run: string
    Session: string
    Sequence: int64
    Name: string
    Model: string
    Endpoint: string
    Project: string
    Focus: string
    Operation: string
    Status: string
    Usage: Usage option
  }
  type Run = {
    Member: MemberTable.MemberId
    Role: Cohort.JoinableRole
    Report: Report
    UpdatedAt: System.DateTime
    Activity: string list
    OmittedActivity: int64
  }
  type State = { Incarnation: string; Sequence: int64; Runs: Map<string, Run> }
  let empty = { Incarnation = ""; Sequence = 0L; Runs = Map.empty }
  type Refusal =
    | NotPresent
    | InvalidField of string
    | OversizedField of string
    | InvalidUsage
    | RegressingUsage
    | WrongMember
    | StaleSequence
    | ConflictingDuplicate
    | TerminalRun
    | Capacity
    | ReportingBusy
    | PublicationFailed
    | ByteCapacity
  let code = function
    | NotPresent -> "member_not_present"
    | InvalidField f -> "invalid_field:" + f
    | OversizedField f -> "oversized_field:" + f
    | InvalidUsage -> "invalid_usage"
    | RegressingUsage -> "regressing_usage"
    | WrongMember -> "wrong_member"
    | StaleSequence -> "stale_sequence"
    | ConflictingDuplicate -> "conflicting_duplicate"
    | TerminalRun -> "terminal_run"
    | Capacity -> "agent_work_capacity"
    | ReportingBusy -> "reporting_busy"
    | PublicationFailed -> "publication_failed"
    | ByteCapacity -> "agent_work_byte_capacity"
  let private terminal status = status <> "running"
  let private fields (r: Report) =
    [ "run", r.Run, 128; "session", r.Session, 128; "name", r.Name, 128
      "model", r.Model, 128; "endpoint", r.Endpoint, 256; "project", r.Project, 512
      "focus", r.Focus, 256; "operation", r.Operation, 128; "status", r.Status, 32 ]
  let private invalidUsage (u: Usage) =
    let counters = [u.Input; u.Output; u.CacheRead; u.CacheWrite; u.Total]
    (counters |> List.exists (fun n -> n < 0L || n > 9007199254740991L))
    || u.Total <> u.Input + u.Output + u.CacheRead + u.CacheWrite
    || (u.EstimateUsd |> Option.exists (fun n -> not (System.Double.IsFinite n) || n < 0.))
  let private regresses old next =
    match old, next with
    | Some _, None -> true
    | Some a, Some b ->
      b.Input < a.Input || b.Output < a.Output || b.CacheRead < a.CacheRead
      || b.CacheWrite < a.CacheWrite || b.Total < a.Total
      || (match a.EstimateUsd, b.EstimateUsd with Some _, None -> true | Some x, Some y -> y < x | _ -> false)
    | _ -> false
  let report now who role (r: Report) state =
    let fieldError = fields r |> List.tryPick (fun (name, value, limit) ->
      if System.String.IsNullOrWhiteSpace value || (value |> Seq.exists System.Char.IsControl) then Some(InvalidField name)
      elif System.Text.Encoding.UTF8.GetByteCount value > limit then Some(OversizedField name)
      else None)
    let existing = Map.tryFind r.Run state.Runs
    let refusal =
      match existing with
      | Some run when run.Member <> who -> Some WrongMember
      | _ ->
        match fieldError with
        | Some error -> Some error
        | None when r.Endpoint.IndexOfAny([|'@'; '?'; '#'; '='|]) >= 0 -> Some(InvalidField "endpoint")
        | None when r.Sequence < 1L || r.Sequence > 9007199254740991L -> Some(InvalidField "sequence")
        | None when not (List.contains r.Status ["running"; "completed"; "failed"; "aborted"; "disconnected"; "unknown"]) -> Some(InvalidField "status")
        | None when r.Usage |> Option.exists invalidUsage -> Some InvalidUsage
        | None ->
          match existing with
          | Some run when r.Sequence < run.Report.Sequence -> Some StaleSequence
          | Some run when r.Sequence = run.Report.Sequence && r <> run.Report -> Some ConflictingDuplicate
          | Some run when r.Sequence = run.Report.Sequence -> None
          | Some run when terminal run.Report.Status -> Some TerminalRun
          | Some run when r.Session <> run.Report.Session -> Some(InvalidField "session")
          | Some run when r.Name <> run.Report.Name -> Some(InvalidField "name")
          | Some run when regresses run.Report.Usage r.Usage -> Some RegressingUsage
          | None when state.Runs.Count >= 256 || (state.Runs |> Map.toSeq |> Seq.map (fun (_,v) -> v.Member) |> Set.ofSeq |> Set.add who |> Set.count) > 64 -> Some Capacity
          | _ -> None
    match refusal with
    | Some error -> Error error
    | None ->
      match existing with
      | Some run when r = run.Report -> Ok state
      | _ ->
        let activity, omitted =
          match existing with
          | None -> [r.Operation], 0L
          | Some run -> r.Operation :: List.truncate 15 run.Activity, run.OmittedActivity + (if run.Activity.Length >= 16 then 1L else 0L)
        let run = { Member = who; Role = role; Report = r; UpdatedAt = now; Activity = activity; OmittedActivity = omitted }
        let runs = Map.add r.Run run state.Runs
        let retainedBytes = runs |> Map.toSeq |> Seq.sumBy (fun (_, item) ->
          let labels = fields item.Report |> List.sumBy (fun (_, value, _) -> System.Text.Encoding.UTF8.GetByteCount value)
          let activities = item.Activity |> List.sumBy System.Text.Encoding.UTF8.GetByteCount
          2048 + 2 * (labels + activities))
        if retainedBytes > 2 * 1024 * 1024 then Error ByteCapacity
        else Ok { state with Sequence = state.Sequence + 1L; Runs = runs }
  let disconnect now who state =
    let runs = state.Runs |> Map.map (fun _ run ->
      if run.Member = who && run.Report.Status = "running" then
        { run with Report = { run.Report with Status = "disconnected" }; UpdatedAt = now }
      else run)
    if runs = state.Runs then state else { state with Sequence = state.Sequence + 1L; Runs = runs }

  /// Injected read-model publication. Production settles through the shared
  /// incremental core; tests may supply a held publisher. No second writer.
  type Publication = {
    Publish: State -> unit
    Read: unit -> State
    Changed: System.IObservable<unit>
  }
  let inMemoryPublication () =
    let value = ref { empty with Incarnation = System.Guid.NewGuid().ToString("N") }
    let changed = Event<unit>()
    { Publish = fun state ->
        System.Threading.Interlocked.Exchange(value, state) |> ignore
        changed.Trigger()
      Read = fun () -> System.Threading.Volatile.Read(&value.contents)
      Changed = changed.Publish }

  /// Same compact projection for MCP and bridge. No tool payloads or transcripts.
  let jsonValue (state: State) =
    let str = Fidelity.Data.JSON.JsonValue.String
    let wide (n: int64) = str (n.ToString(System.Globalization.CultureInfo.InvariantCulture))
    let array xs = Fidelity.Data.JSON.JsonValue.Array xs
    let obj xs = Fidelity.Data.JSON.JsonValue.Object xs
    let nullValue = Fidelity.Data.JSON.JsonValue.Null
    obj [
      "incarnation", str state.Incarnation; "sequence", wide state.Sequence
      "executionHost", str System.Environment.MachineName
      "capacity", Fidelity.Data.JSON.JsonValue.ofInt64 256L
      "runs", array [
        for KeyValue(key, run) in state.Runs ->
          let r = run.Report
          obj [
            "id", str key; "member", str (MemberTable.MemberId.display run.Member)
            "role", str (string run.Role); "name", str r.Name; "session", str r.Session
            "reportSequence", wide r.Sequence; "model", str r.Model; "endpoint", str r.Endpoint
            "project", str r.Project; "focus", str r.Focus; "operation", str r.Operation; "status", str r.Status
            "updatedAtMs", wide (System.DateTimeOffset(run.UpdatedAt).ToUnixTimeMilliseconds())
            "omittedActivity", wide run.OmittedActivity
            "activity", array (run.Activity |> List.map str)
            "usage",
              (match r.Usage with
               | None -> nullValue
               | Some u ->
                 obj [
                   "input", wide u.Input; "output", wide u.Output; "cacheRead", wide u.CacheRead
                   "cacheWrite", wide u.CacheWrite; "total", wide u.Total
                   "estimateUsd", (u.EstimateUsd |> Option.map Fidelity.Data.JSON.JsonValue.Number |> Option.defaultValue nullValue)
                 ])
          ]
      ]
    ]
  let json state = jsonValue state |> Fidelity.Data.JSON.Json.serialize
