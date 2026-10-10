/// Pure monitoring derivations on the shared incremental core. These evaluators
/// do no I/O; Finished/Drained settle synchronously. Each bounded acquisition
/// window has its own epoch, so library tombstones do not grow indefinitely.
module Bozzetto.Server.ResourceGraph

open Fidelity.FSharp.Incremental
open Bozzetto.Web.Shared.Protocol

type State = private {
  Core: Core.State
  Values: Map<ValueToken, float option>
  Inputs: Map<InputId, float option>
  Tokens: Map<InputId, ValueToken>
  Serial: uint64
  Result: float option
  Evaluations: int
}

let rec private apply action state =
  let core, effects =
    Core.step { Epoch = EpochId 1UL; Action = action } state.Core
    |> Result.defaultWith (fun e -> invalidOp (sprintf "Resource graph: %A" e))
  let next = { state with Core = core }
  (next,effects) ||> List.fold (fun current effect ->
    match effect.Action with
    | EffectAction.Start request ->
      let values = request.Reads |> List.map (fun r ->
        match r.Value with
        | ReadValue.Input(_,_,token) -> Map.find token current.Values
        | _ -> None)
      let value =
        match values with
        | [Some used; Some total] when total > 0. -> Some(100. * used / total)
        | _ -> None
      let token = ValueToken (current.Serial + 1UL)
      let evaluated = { current with Serial = current.Serial + 1UL; Result = value; Evaluations = current.Evaluations + 1 }
      evaluated
      |> apply (Action.Finished(request.Attempt, Completion.Succeeded token))
      |> apply (Action.Drained request.Attempt)
    | EffectAction.Cancel attempt ->
      current |> apply (Action.Finished(attempt, Completion.Cancelled)) |> apply (Action.Drained attempt)
    | _ -> current)

let create () =
  { Core = Core.init (EpochId 1UL); Values = Map.empty; Inputs = Map.empty; Tokens = Map.empty
    Serial = 0UL; Result = None; Evaluations = 0 }
  |> apply (Action.ReserveScope(ScopeId 1UL, RevisionId 1UL))
  |> apply (Action.ReplaceScope(ScopeId 1UL, RevisionId 1UL, [
    ScopeEntry.Define {
      Work = WorkId 1UL
      Stamp = DefinitionStamp 1UL
      Reads = [ { Slot = ReadSlotId 1UL; Source = ReadSource.Input(InputId 1UL) }
                { Slot = ReadSlotId 2UL; Source = ReadSource.Input(InputId 2UL) } ] } ]))
  |> apply (Action.Demand(DemandId 1UL, WorkId 1UL))

let update (metrics: ResourceMetric array) state =
  let get name = metrics |> Array.tryFind (fun m -> m.Name = name) |> Option.bind _.Value
  let inputs = [ InputId 1UL, get "RAM used (total − available)"; InputId 2UL, get "RAM total (OS managed)" ]
  let changed = inputs |> List.filter (fun (id,value) -> Map.tryFind id state.Inputs <> Some value)
  let prepared, writes =
    (({ state with Values = Map.empty }, []), changed) ||> List.fold (fun (s,writes) (id,value) ->
      let serial = s.Serial + 1UL
      let token = ValueToken serial
      { s with Serial = serial; Inputs = Map.add id value s.Inputs; Tokens = Map.add id token s.Tokens; Values = Map.add token value s.Values },
      { Input = id; Stamp = InputStamp serial; Value = token } :: writes)
  // Unchanged source tokens remain needed by the definition's other read.
  let prepared = { prepared with Values = Map.fold (fun acc k v -> Map.add k v acc) state.Values prepared.Values }
  let next = if writes.IsEmpty then state else apply (Action.SetInputs(List.rev writes)) prepared
  // Retain only the two current input payloads, not every sample ever seen.
  // Core retains token identity internally; payload retention is owner-defined.
  let retained = next.Tokens |> Map.toSeq |> Seq.map snd |> Set.ofSeq
  let newest = next.Values |> Map.filter (fun token _ -> Set.contains token retained)
  { next with Values = newest },
  { ResourceMetric.Name = "RAM occupied"; Unit = "percent"; Value = next.Result }

let evaluations state = state.Evaluations

/// Agent work uses the same foundation; recycle its settled epoch every 256 writes.
/// The cohort remains the only writer; these are immutable payload tokens and
/// an explicitly demanded identity projection, not another journal/actor.
module AgentPublication =
  open Bozzetto
  let create () : AgentWork.Publication =
    let published = ref { AgentWork.empty with Incarnation = System.Guid.NewGuid().ToString("N") }
    let changed = Event<unit>()
    let mutable graph = Core.init (EpochId 1UL)
    let mutable writes = 0UL
    let publish (value: AgentWork.State) =
      let serial = writes % 256UL + 1UL
      let epoch = EpochId (writes / 256UL + 1UL)
      let rec settle action core =
        let next, effects =
          Core.step { Epoch = epoch; Action = action } core
          |> Result.defaultWith (fun error -> invalidOp (sprintf "Agent publication: %A" error))
        (next, effects) ||> List.fold (fun current effect ->
          match effect.Action with
          | EffectAction.Start request ->
            current
            |> settle (Action.Finished(request.Attempt, Completion.Succeeded(ValueToken (serial + 256UL))))
            |> settle (Action.Drained request.Attempt)
          | EffectAction.Offer _ ->
            System.Threading.Interlocked.Exchange(published, value) |> ignore
            current
          | _ -> current)
      let input = Action.SetInputs [{ Input = InputId 1UL; Stamp = InputStamp serial; Value = ValueToken serial }]
      graph <-
        if serial = 1UL then
          Core.init epoch
          |> settle input
          |> settle (Action.ReserveScope(ScopeId 1UL, RevisionId 1UL))
          |> settle (Action.ReplaceScope(ScopeId 1UL, RevisionId 1UL, [ScopeEntry.Define {
            Work = WorkId 1UL; Stamp = DefinitionStamp 1UL
            Reads = [{ Slot = ReadSlotId 1UL; Source = ReadSource.Input(InputId 1UL) }] }]))
          |> settle (Action.Demand(DemandId 1UL, WorkId 1UL))
        else graph |> settle input
      writes <- writes + 1UL
      if published.Value <> value then invalidOp "Agent publication did not offer the demanded snapshot."
      changed.Trigger()
    { Publish = publish
      Read = fun () -> System.Threading.Volatile.Read(&published.contents)
      Changed = changed.Publish }
