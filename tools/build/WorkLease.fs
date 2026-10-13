/// The pipeline's client of the daemon's work-lease API (POST /api/lease/request, renew,
/// release and withdraw). Builds and suites over Clef and Composer code run under a lease.
///
/// One stable holder per agent and project, so a retry after a deferral is the same queued
/// ask. A granted lease renews on a heartbeat while its work runs; the daemon's word that the
/// lease is gone cancels the work's token; the lease is released when the work ends.
///
/// Loaded by ci-pipeline.fsx (`#load`). Agents lease through the MCP tools instead.
module Bozzetto.Build.WorkLease

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

type Admission =
  | Granted of leaseId: string * expiresAt: DateTimeOffset
  | Deferred of reply: string
  | Refused of reason: string

type Renewal =
  | Renewed of expiresAt: DateTimeOffset
  | Gone
  | Unanswered of reason: string

let private endpoint =
  match Environment.GetEnvironmentVariable "BOZZETTO_MCP_PORT" with
  | null | "" -> "http://127.0.0.1:47749/api/lease"
  | port -> $"http://127.0.0.1:{port}/api/lease"

let private client = new HttpClient(Timeout = TimeSpan.FromSeconds 5.)

/// The stable holder for this agent and project. Labels are caller-reported context.
let holder (project: string) =
  let orDefault fallback name =
    match Environment.GetEnvironmentVariable name with
    | null | "" -> fallback
    | value -> value
  $"""{orDefault "ci-pipeline" "BOZZETTO_AGENT"} · {orDefault project "BOZZETTO_PROJECT"}"""

let private post (route: string) (fields: (string * string) list) =
  async {
    try
      use content = new StringContent(JsonSerializer.Serialize(dict fields), Encoding.UTF8, "application/json")
      use! response = client.PostAsync($"{endpoint}/{route}", content) |> Async.AwaitTask
      let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
      use document = JsonDocument.Parse body
      return Ok(int response.StatusCode, document.RootElement.Clone())
    with e ->
      return Error $"{route}: {e.Message}"
  }

let private text (reply: JsonElement) (name: string) =
  match reply.TryGetProperty name with
  | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
  | _ -> None

let private instant reply name =
  text reply name
  |> Option.bind (fun value ->
    match DateTimeOffset.TryParse value with
    | true, at -> Some at
    | false, _ -> None)

let acquire kind holder =
  async {
    match! post "request" [ "kind", kind; "holder", holder ] with
    | Error reason -> return Refused reason
    | Ok(_, reply) ->
      match text reply "decision", text reply "leaseId", instant reply "expiresAt" with
      | Some "granted", Some id, Some expires when id <> "" -> return Granted(id, expires)
      | Some "wait", _, _ -> return Deferred(reply.GetRawText())
      | _ -> return Refused(reply.GetRawText())
  }

let renew leaseId holder =
  async {
    match! post "renew" [ "leaseId", leaseId; "holder", holder ] with
    | Error reason -> return Unanswered reason
    | Ok(200, reply) ->
      match text reply "outcome", instant reply "expiresAt" with
      | Some "renewed", Some expires -> return Renewed expires
      | Some "already_gone", _ -> return Gone
      | _ -> return Unanswered(reply.GetRawText())
    | Ok(status, _) -> return Unanswered $"renew: HTTP {status}"
  }

let release leaseId =
  async {
    match! post "release" [ ("leaseId", leaseId) ] with
    | Ok(200, _) -> return Ok()
    | Ok(status, reply) -> return Error $"release: HTTP {status} {reply.GetRawText()}"
    | Error reason -> return Error reason
  }

let withdraw kind holder =
  post "withdraw" [ "kind", kind; "holder", holder ] |> Async.Ignore

/// Binary observation transport for the runner; the shared codec owns its payload.
let observeCommand mediaType phase bytes =
  async {
    try
      use content = new ByteArrayContent(bytes: byte array)
      content.Headers.ContentType <- Headers.MediaTypeHeaderValue(mediaType)

      use! response =
        client.PostAsync($"{endpoint}/command/{phase}", content) |> Async.AwaitTask

      let! reply = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
      return Ok(int response.StatusCode, reply)
    with error ->
      return Error $"command/{phase}: {error.Message}"
  }

/// Injectable effects of the existing lease client; admission remains daemon-owned.
type Client = {
  Acquire: string -> string -> Async<Admission>
  Renew: string -> string -> Async<Renewal>
  Release: string -> Async<Result<unit, string>>
  Withdraw: string -> string -> Async<unit>
  Delay: int -> CancellationToken -> Async<unit>
  Now: unit -> DateTimeOffset
}

let private api = {
  Acquire = acquire
  Renew = renew
  Release = release
  Withdraw = withdraw
  Delay = fun milliseconds token -> Task.Delay(milliseconds, token) |> Async.AwaitTask
  Now = fun () -> DateTimeOffset.UtcNow
}

// A canceled child is an outcome to join, never cancellation of the cleanup owner.
let private settle work =
  Async.FromContinuations(fun (complete, _, _) ->
    Async.StartWithContinuations(work, (Ok >> complete), (Error >> complete),
      (fun error -> complete (Error error)), cancellationToken = CancellationToken.None))

/// The callback owns its physical work and must return only after joining its cleanup.
/// Cancellation requests that cleanup; it cannot release the lease ahead of the callback,
/// cancellation callbacks, or heartbeat. The caller's ambient async cancellation follows
/// the same ordering as the explicit token.
let runWith (client: Client) (kind: string) (holder: string) (cancellation: CancellationToken)
            (work: string -> CancellationToken -> Async<Result<'T, string>>) =
  async {
    let! ambient = Async.CancellationToken
    return! Async.FromContinuations(fun (complete, failed, _) ->
      let owned = async {
        use stopping = new CancellationTokenSource()
        let cancelGate = obj ()
        let mutable cancellationTask: Task option = None
        let requestCancellation () =
          lock cancelGate (fun () ->
            if cancellationTask.IsNone then cancellationTask <- Some(stopping.CancelAsync()))
        use explicitRegistration = cancellation.Register requestCancellation
        use ambientRegistration = ambient.Register requestCancellation
        let joinCancellation () = async {
          // Dispose joins forwarding callbacks before observing their CancelAsync task.
          explicitRegistration.Dispose()
          ambientRegistration.Dispose()
          match lock cancelGate (fun () -> cancellationTask) with
          | Some running -> return! running |> Async.AwaitTask |> settle
          | None -> return Ok()
        }
        let canceled = $"work-lease: {kind} canceled; owned work and cleanup joined"
        let! outcome = settle (async {
          let patience = TimeSpan.FromMinutes 10.
          let asked = client.Now()
          let rec admit () =
            async {
              if stopping.IsCancellationRequested then
                do! client.Withdraw kind holder
                return Error canceled
              else
                match! client.Acquire kind holder with
                | Granted(id, expires) -> return Ok(id, expires)
                | Deferred _ when stopping.IsCancellationRequested ->
                  do! client.Withdraw kind holder
                  return Error canceled
                | Deferred reply when client.Now() - asked < patience ->
                  eprintfn "work-lease: %s deferred; waiting in line: %s" kind reply
                  let! delayed = client.Delay 15000 stopping.Token |> settle
                  match delayed with
                  | Ok() -> return! admit ()
                  | Error _ when stopping.IsCancellationRequested ->
                    do! client.Withdraw kind holder
                    return Error canceled
                  | Error error ->
                    do! client.Withdraw kind holder
                    return Error $"work-lease: admission delay failed: {error.Message}"
                | Deferred reply ->
                  do! client.Withdraw kind holder
                  return Error $"work-lease: {kind} still deferred after {patience.TotalMinutes} minutes; ask withdrawn: {reply}"
                | Refused reason -> return Error $"work-lease: {kind} refused: {reason}"
            }
          match! admit () with
          | Error reason -> return Error reason
          | Ok(leaseId, expires) ->
            use stopBeat = new CancellationTokenSource()
            let mutable gone = false
            let pause (until: DateTimeOffset) =
              max 30000 (int ((until - client.Now()).TotalMilliseconds / 3.))
            let rec beat (until: DateTimeOffset) =
              async {
                let! delayed = client.Delay (pause until) stopBeat.Token |> settle
                match delayed with
                | Error _ when stopBeat.IsCancellationRequested -> ()
                | Error error -> return raise error
                | Ok() when stopBeat.IsCancellationRequested -> ()
                | Ok() ->
                  match! client.Renew leaseId holder with
                  | Renewed next -> return! beat next
                  | Gone ->
                    eprintfn "work-lease: lease %s is gone; stopping its work" leaseId
                    gone <- true
                    requestCancellation ()
                  | Unanswered reason ->
                    eprintfn "work-lease: renewal unanswered (%s); retrying" reason
                    return! beat until
              }
            let heartbeat = Async.StartAsTask(async {
              let! result = settle (beat expires)
              if Result.isError result then requestCancellation ()
              return result
            }, cancellationToken = CancellationToken.None)
            let! outcome =
              if stopping.IsCancellationRequested then async.Return(Ok(Error canceled))
              else async.Delay(fun () -> work leaseId stopping.Token) |> settle
            // Renewal covers callback cleanup as well as the returned work: a
            // retained lease must not expire while an owned callback is held.
            let! firstCallbacks = joinCancellation ()
            let! stoppedHeartbeat = stopBeat.CancelAsync() |> Async.AwaitTask |> settle
            let! heartbeatResult = heartbeat |> Async.AwaitTask
            // A renewal already in flight may report Gone/failure after the
            // first join observed no cancellation task. Join that request too.
            let! lastCallbacks = joinCancellation ()
            let canceledCallbacks =
              match firstCallbacks, lastCallbacks with
              | Error error, _ | _, Error error -> Error error
              | Ok(), Ok() -> Ok()
            let! released = client.Release leaseId
            match released with
            | Error reason -> eprintfn "work-lease: release failed for owned lease %s: %s" leaseId reason
            | Ok() -> ()
            return
              match outcome, heartbeatResult, stoppedHeartbeat, canceledCallbacks, released with
              | _, _, _, _, _ when gone -> Error $"work-lease: lease {leaseId} was reported gone while its work ran"
              | _, Error error, _, _, _ -> Error $"work-lease: heartbeat failed: {error.Message}"
              | _, _, Error error, _, _
              | _, _, _, Error error, _ -> Error $"work-lease: cancellation callback failed: {error.Message}"
              | _, _, _, _, _ when stopping.IsCancellationRequested -> Error canceled
              | Error error, _, _, _, _ -> Error $"work-lease: {kind} work failed: {error.Message}"
              | Ok result, _, _, _, Ok() -> result
              | Ok(Ok _), _, _, _, Error reason -> Error $"work-lease: {reason}"
              | Ok(Error error), _, _, _, Error _ -> Error error
        })
        let! callbacks = joinCancellation ()
        return
          match outcome, callbacks with
          | Error error, _ -> Error $"work-lease: {kind} failed: {error.Message}"
          | _, Error error -> Error $"work-lease: cancellation callback failed: {error.Message}"
          | Ok result, Ok() -> result
      }
      Async.StartWithContinuations(owned, complete, failed, (fun error -> failed error),
        cancellationToken = CancellationToken.None))
  }

/// Retry a deferred stable ask every 15 seconds for at most ten minutes. Renew a
/// granted lease at a third of its remaining lifetime, never sooner than 30 seconds.
let runWithCancellation kind holder cancellation work = runWith api kind holder cancellation work

/// Compatibility wrapper for existing pipeline and command-runner callers.
let run kind holder (work: CancellationToken -> Async<Result<unit, string>>) =
  runWithCancellation kind holder CancellationToken.None (fun _ token -> work token)
