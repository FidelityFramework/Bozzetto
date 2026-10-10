/// One shared, explicitly requested counter-acquisition window. No acquisition
/// exists at startup or after the final subscriber leaves. Native notifications
/// and autonomous stability calibration are not claimed by this bounded slice.
module Bozzetto.Server.ResourceMonitor

open System
open System.Diagnostics
open System.Threading
open Bozzetto.Web.Shared.Protocol

type private Message =
  | Observe of int64 * int * (HostResources -> unit) * (unit -> unit)
  | Remove of int64 * (unit -> unit)
  | Tick of int64
  | Close of (unit -> unit)

type private Demand = { Until: int64; Send: HostResources -> unit }

let empty status = {
  SampledAtMs = 0L; Sequence = 0L; Status = status; IntervalMs = 0.; SampleCostMs = 0.
  Metrics = [||]; Processes = [||]; ProcessCount = 0; UnreadableCount = 0; OmittedCount = 0
  Note = "No measurements yet. Enable a bounded observation window; no background scanning is running."
}

/// Effects at the edge: the injected acquisition is synchronous and bounded by
/// its source implementation; pure calculations and graph transitions own no I/O.
type Monitor(acquire: unit -> LinuxResources.Raw, interval: TimeSpan) =
  let gate = obj ()
  let mutable latest = empty "stopped"
  let delays = new CancellationTokenSource()
  let mutable closing = false
  let closed = System.Threading.Tasks.TaskCompletionSource<unit>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously)
  let send callback value = try callback value with _ -> ()
  let publish value = lock gate (fun () -> latest <- value)
  let agent = MailboxProcessor.Start(fun inbox ->
    let schedule generation =
      Async.Start(async {
        do! Async.Sleep(int interval.TotalMilliseconds)
        inbox.Post(Tick generation)
      }, delays.Token)
    let rec loop (demands: Map<int64,Demand>) generation previous graph sequence = async {
      let! message = inbox.Receive()
      let finish status demand = send demand.Send { latest with Status = status }
      match message with
      | Close reply ->
        demands |> Map.iter (fun _ d -> finish "stopped" d)
        publish { latest with Status = "stopped" }
        delays.Cancel()
        reply ()
      | Remove(id,reply) ->
        Map.tryFind id demands |> Option.iter (finish "stopped")
        let remaining = Map.remove id demands
        if remaining.IsEmpty then publish { latest with Status = "stopped" }
        reply ()
        return! loop remaining (if remaining.IsEmpty then generation + 1L else generation) previous graph sequence
      | Observe(id,seconds,callback,reply) ->
        if seconds < 1 || seconds > 120 then
          invalidArg "seconds" "Observation windows must last 1–120 seconds."
        let wasEmpty = demands.IsEmpty
        let nextGeneration = if wasEmpty then generation + 1L else generation
        // One epoch is bounded even if clients repeatedly renew their demand.
        let until = Stopwatch.GetTimestamp() + int64 (float seconds * float Stopwatch.Frequency)
        let added = Map.add id { Until = until; Send = callback } demands
        if not wasEmpty then send callback latest
        else inbox.Post(Tick nextGeneration)
        reply ()
        return! loop added nextGeneration (if wasEmpty then None else previous) (if wasEmpty then ResourceGraph.create() else graph) sequence
      | Tick run when run = generation && not demands.IsEmpty ->
        let now = Stopwatch.GetTimestamp()
        let active, expired = demands |> Map.partition (fun _ d -> d.Until > now)
        expired |> Map.iter (fun _ d -> finish "complete" d)
        if active.IsEmpty then
          publish { latest with Status = "complete" }
          return! loop active (generation + 1L) None (ResourceGraph.create()) sequence
        else
          let started = Stopwatch.GetTimestamp()
          try
            let raw = acquire ()
            let value = LinuxResources.project previous raw
            let nextGraph, occupied = ResourceGraph.update value.Metrics graph
            let value = { value with Sequence = sequence + 1L; Metrics = Array.append [|occupied|] value.Metrics
                                     SampleCostMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds }
            publish value
            active |> Map.iter (fun _ d -> send d.Send value)
            schedule generation
            // Recycle the pure graph after 60 acquisitions, retaining no old
            // tombstones. Payloads are snapshots, not compiler authority.
            return! loop active generation (Some raw) (if sequence % 60L = 59L then ResourceGraph.create() else nextGraph) (sequence + 1L)
          with error ->
            let failed = { latest with Status = "error"; Note = "Resource acquisition failed: " + error.Message }
            publish failed
            active |> Map.iter (fun _ d -> send d.Send failed)
            return! loop Map.empty (generation + 1L) None (ResourceGraph.create()) sequence
      | Tick _ -> return! loop demands generation previous graph sequence
    }
    loop Map.empty 0L None (ResourceGraph.create()) 0L)

  let control message = Async.FromContinuations(fun (done',_,_) ->
    lock gate (fun () ->
      if closing then done' ()
      else agent.Post(message done')))

  member _.Current = lock gate (fun () -> latest)
  member _.Observe(id, seconds, send) =
    if seconds < 1 || seconds > 120 then invalidArg "seconds" "Observation windows must last 1–120 seconds."
    control (fun reply -> Observe(id,seconds,send,reply))
  member _.Remove id = control (fun reply -> Remove(id,reply))
  member _.Close() = async {
    lock gate (fun () ->
      if not closing then
        closing <- true
        agent.Post(Close(fun () -> closed.TrySetResult() |> ignore)))
    do! closed.Task |> Async.AwaitTask
  }

let create () = new Monitor(LinuxResources.acquire, TimeSpan.FromSeconds 2.)
