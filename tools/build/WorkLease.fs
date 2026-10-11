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

/// Run `work` under a `kind` lease held by `holder`. A deferred ask keeps its place in line
/// and is retried as the same ask every 15 seconds for at most ten minutes, then withdrawn.
/// The heartbeat renews at a third of the remaining lifetime, never sooner than 30 seconds.
/// `work` receives a token the heartbeat cancels when the daemon reports the lease gone.
let run (kind: string) (holder: string) (work: CancellationToken -> Async<Result<unit, string>>) =
  async {
    let patience = TimeSpan.FromMinutes 10.
    let asked = DateTimeOffset.UtcNow
    let rec admit () =
      async {
        match! acquire kind holder with
        | Granted(id, expires) -> return Ok(id, expires)
        | Deferred reply when DateTimeOffset.UtcNow - asked < patience ->
          eprintfn "work-lease: %s deferred; waiting in line: %s" kind reply
          do! Async.Sleep 15000
          return! admit ()
        | Deferred reply ->
          do! withdraw kind holder
          return Error $"work-lease: {kind} still deferred after {patience.TotalMinutes} minutes; ask withdrawn: {reply}"
        | Refused reason -> return Error $"work-lease: {kind} refused: {reason}"
      }
    match! admit () with
    | Error reason -> return Error reason
    | Ok(leaseId, expires) ->
      use gone = new CancellationTokenSource()
      use stopBeat = new CancellationTokenSource()
      let pause (until: DateTimeOffset) =
        max 30000 (int ((until - DateTimeOffset.UtcNow).TotalMilliseconds / 3.))
      let rec beat (until: DateTimeOffset) =
        async {
          do! Async.Sleep(pause until)
          match! renew leaseId holder with
          | Renewed next -> return! beat next
          | Gone ->
            eprintfn "work-lease: lease %s is gone; stopping its work" leaseId
            gone.Cancel()
          | Unanswered reason ->
            eprintfn "work-lease: renewal unanswered (%s); retrying" reason
            return! beat until
        }
      Async.Start(beat expires, stopBeat.Token)
      use! _released = Async.OnCancel(fun () -> stopBeat.Cancel(); release leaseId |> Async.RunSynchronously |> ignore)
      let! outcome = Async.Catch(work gone.Token)
      stopBeat.Cancel()
      let! released = release leaseId
      match released with
      | Error reason -> eprintfn "work-lease: release failed for owned lease %s: %s" leaseId reason
      | Ok() -> ()
      return
        match outcome, released with
        | Choice2Of2 e, _ -> Error $"work-lease: {kind} work failed: {e.Message}"
        | Choice1Of2 _, _ when gone.IsCancellationRequested -> Error $"work-lease: lease {leaseId} was reported gone while its work ran"
        | Choice1Of2 result, Ok() -> result
        | Choice1Of2(Ok()), Error reason -> Error $"work-lease: {reason}"
        | Choice1Of2(Error e), Error _ -> Error e
  }
