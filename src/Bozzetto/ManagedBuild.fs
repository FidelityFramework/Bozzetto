namespace Bozzetto

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Bozzetto.Build
open Fidelity.FSharp.Incremental
open Fidelity.FSharp.Incremental.Hosting

/// Local managed build composition. Foundation owns work eligibility, WorkLease
/// owns admission, and this evaluator owns its process and diagnostic streams.
/// No remote, compiler-proof or detached-descendant authority is supplied here.
[<RequireQualifiedAccess>]
module ManagedBuild =
  type FileIdentity = { Path: string; Sha256: string }

  type Request = {
    Epoch: EpochId
    Checkout: string
    Project: string
    Dotnet: FileIdentity
    Configuration: string
    NoRestore: bool
    Inputs: FileIdentity list
    Artifacts: string list
    EvidenceDirectory: string
    Holder: string
    Timeout: TimeSpan
  }

  type ProcessIdentity = { Pid: int; StartedUtc: DateTime }
  type Outcome = Succeeded | Cancelled | Failed of string
  type Receipt = {
    RequestSha256: string
    Request: Request
    Attempt: StartRequest
    LeaseId: string option
    Process: ProcessIdentity option
    Arguments: string list
    ExitCode: int option
    CancellationRequested: bool
    TimedOut: bool
    Outcome: Outcome
    Stdout: FileIdentity option
    Stderr: FileIdentity option
    Artifacts: FileIdentity list
    InputsUnchanged: bool
    RootAndStreamsJoined: bool
    // This implementation does not create an OS containment boundary.
    DetachedDescendantsContained: bool
  }

  type private LeaseRunner = {
    Run: string -> CancellationToken -> (string -> CancellationToken -> Async<Result<unit, string>>) -> Async<Result<unit, string>>
  }

  type Handle = private {
    Mailbox: AsyncMailbox.Handle
    Receipt: unit -> Receipt option
    StartGate: obj
    mutable Started: bool
  }

  let private hashBytes bytes = SHA256.HashData(bytes: byte array) |> Convert.ToHexString |> _.ToLowerInvariant()
  let private fileIdentity (path: string) =
    use stream = File.OpenRead path
    { Path = path; Sha256 = SHA256.HashData stream |> Convert.ToHexString |> _.ToLowerInvariant() }

  let private sameFiles (files: FileIdentity list) =
    files |> List.forall (fun expected ->
      File.Exists expected.Path && (fileIdentity expected.Path).Sha256 = expected.Sha256)

  let private arguments (request: Request) =
    [ "build"; request.Project; "--disable-build-servers"; "--configuration"; request.Configuration
      "-nodeReuse:false"; "-p:UseSharedCompilation=false" ]
    @ (if request.NoRestore then [ "--no-restore" ] else [])

  let private requestHash (request: Request) =
    // Length-prefix every field; this is an evidence identity, not a compiler key.
    let fields =
      [ string request.Epoch; request.Checkout; request.Project; request.Dotnet.Path; request.Dotnet.Sha256
        request.Configuration; string request.NoRestore; request.EvidenceDirectory; request.Holder
        string request.Timeout.Ticks ]
      @ (request.Inputs |> List.collect (fun file -> [ file.Path; file.Sha256 ]))
      @ [ "artifacts" ] @ request.Artifacts
    fields |> List.map (fun value -> string (Encoding.UTF8.GetByteCount value) + ":" + value)
    |> String.concat "" |> Encoding.UTF8.GetBytes |> hashBytes

  let private validate (request: Request) =
    let absolute (path: string) = Path.IsPathFullyQualified path && Path.GetFullPath path = path
    let inside (path: string) = path.StartsWith(request.Checkout.TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar, StringComparison.Ordinal)
    let validHash (value: string) = value.Length = 64 && (value |> Seq.forall Uri.IsHexDigit)
    let paths = request.Dotnet.Path :: request.Project :: request.EvidenceDirectory :: (request.Inputs |> List.map _.Path) @ request.Artifacts
    if paths |> List.exists (absolute >> not) then Error "managed-build: absolute canonical paths are required"
    elif not (absolute request.Checkout && Directory.Exists(Path.Combine(request.Checkout, ".git"))) then
      Error "managed-build: a primary Git checkout is required"
    elif not (inside request.Project) then Error "managed-build: project is outside the primary checkout"
    elif Path.GetFileName(request.Dotnet.Path) <> "dotnet" then Error "managed-build: an explicit dotnet host is required"
    elif request.Inputs |> List.exists (fun file -> not (validHash file.Sha256)) then Error "managed-build: invalid input digest"
    elif not (validHash request.Dotnet.Sha256) then Error "managed-build: invalid tool digest"
    elif request.Inputs |> List.exists (fun file -> file.Path = request.Project) |> not then Error "managed-build: project is absent from the declared input closure"
    elif request.Inputs |> List.map _.Path |> Set.ofList |> Set.count <> request.Inputs.Length then Error "managed-build: duplicate input path"
    elif request.Artifacts |> List.exists (fun path -> request.Inputs |> List.exists (fun file -> file.Path = path)) then Error "managed-build: an artifact cannot overwrite a declared input"
    elif String.IsNullOrWhiteSpace request.Holder || String.IsNullOrWhiteSpace request.Configuration then Error "managed-build: holder and configuration are required"
    elif request.Configuration |> Seq.exists (fun c -> not (Char.IsLetterOrDigit c || c = '_' || c = '-')) then Error "managed-build: invalid configuration"
    elif request.Timeout <= TimeSpan.Zero || request.Timeout.TotalMilliseconds > float Int32.MaxValue then Error "managed-build: a finite positive deadline is required"
    else Ok ()

  let private execute (request: Request) (initial: Receipt) (cancellation: CancellationToken) = task {
    use deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation)
    deadline.CancelAfter request.Timeout
    let gate = obj ()
    let mutable launched = false
    let mutable stopFailure = None
    use child = new Process()
    let start = ProcessStartInfo(request.Dotnet.Path)
    start.WorkingDirectory <- request.Checkout
    start.UseShellExecute <- false
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    arguments request |> List.iter start.ArgumentList.Add
    start.Environment["MSBUILDDISABLENODEREUSE"] <- "1"
    start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] <- "0"
    child.StartInfo <- start
    let stop () = lock gate (fun () ->
      if launched then
        try if not child.HasExited then child.Kill(entireProcessTree = true)
        with error -> stopFailure <- Some error.Message)
    use registration = deadline.Token.Register stop
    Directory.CreateDirectory request.EvidenceDirectory |> ignore
    let stdoutPath = Path.Combine(request.EvidenceDirectory, "stdout.log")
    let stderrPath = Path.Combine(request.EvidenceDirectory, "stderr.log")
    use stdout = new FileStream(stdoutPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)
    use stderr = new FileStream(stderrPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)
    let started = lock gate (fun () ->
      if deadline.IsCancellationRequested then false
      else
        launched <- child.Start()
        launched)
    if not started then
      let outcome =
        if cancellation.IsCancellationRequested then Cancelled
        elif deadline.IsCancellationRequested then Failed "managed-build: deadline expired"
        else Failed "managed-build: process did not start"
      return { initial with
                Outcome = outcome; RootAndStreamsJoined = true
                CancellationRequested = deadline.IsCancellationRequested
                TimedOut = deadline.IsCancellationRequested && not cancellation.IsCancellationRequested }
    else
      let mutable identity = None
      let mutable output: Task = Task.CompletedTask
      let mutable errors: Task = Task.CompletedTask
      let mutable executionFailure = None
      try
        identity <- Some { Pid = child.Id; StartedUtc = child.StartTime.ToUniversalTime() }
        output <- child.StandardOutput.BaseStream.CopyToAsync stdout
        errors <- child.StandardError.BaseStream.CopyToAsync stderr
        let streams = Task.WhenAll(output, errors)
        let exited = child.WaitForExitAsync(CancellationToken.None)
        let! first = Task.WhenAny(exited, streams)
        if first.IsFaulted then stop ()
        do! exited
        do! streams
      with error ->
        executionFailure <- Some error.Message
        stop ()
      // These joins are outside the failure/cancellation path. A retained pipe
      // keeps ownership pending; root exit is never whole-tree drain evidence.
      do! child.WaitForExitAsync(CancellationToken.None)
      try do! Task.WhenAll(output, errors)
      with error -> executionFailure <- Some error.Message
      try
        do! stdout.FlushAsync()
        do! stderr.FlushAsync()
      with error -> executionFailure <- Some error.Message
      registration.Dispose() // join an in-flight kill callback before lease release
      let mutable evidenceFailure = None
      let evidence path =
        try Some(fileIdentity path)
        with error -> evidenceFailure <- Some error.Message; None
      let standardOutput, standardError = evidence stdoutPath, evidence stderrPath
      let artifacts =
        if child.ExitCode = 0 && not deadline.IsCancellationRequested then request.Artifacts |> List.choose evidence else []
      let unchanged =
        try sameFiles (request.Dotnet :: request.Inputs)
        with error -> evidenceFailure <- Some error.Message; false
      let cleanupError = lock gate (fun () -> stopFailure) |> Option.orElse executionFailure
      let outcome =
        match cleanupError with
        | Some message -> Failed("managed-build: cleanup failed: " + message)
        | None when evidenceFailure.IsSome -> Failed("managed-build: evidence binding failed: " + evidenceFailure.Value)
        | None when cancellation.IsCancellationRequested -> Cancelled
        | None when deadline.IsCancellationRequested -> Failed "managed-build: deadline expired"
        | None when not unchanged -> Failed "managed-build: declared input closure changed"
        | None when child.ExitCode <> 0 -> Failed $"managed-build: dotnet exited {child.ExitCode}"
        | None -> Succeeded
      return { initial with
                Process = identity; ExitCode = Some child.ExitCode; Outcome = outcome
                CancellationRequested = deadline.IsCancellationRequested
                TimedOut = deadline.IsCancellationRequested && not cancellation.IsCancellationRequested
                Stdout = standardOutput; Stderr = standardError
                Artifacts = artifacts; InputsUnchanged = unchanged
                RootAndStreamsJoined = cleanupError.IsNone }
  }

  let private createUsing (lease: LeaseRunner) (request: Request) =
    validate request |> Result.bind (fun () ->
      let receiptGate = obj ()
      let mutable receipt = None
      let evaluator invocation cancellation = async {
        match invocation with
        | StepInvocation.Resume _ ->
          return StepOutcome.Complete(Completion.Failed { Code = "managed-build-resume"; Message = "Managed builds do not suspend." })
        | StepInvocation.Start attempt ->
          let initial = {
            RequestSha256 = requestHash request; Request = request; Attempt = attempt
            LeaseId = None; Process = None; Arguments = arguments request; ExitCode = None
            CancellationRequested = false; TimedOut = false
            Outcome = Failed "managed-build: not launched"; Stdout = None; Stderr = None; Artifacts = []
            InputsUnchanged = false; RootAndStreamsJoined = false; DetachedDescendantsContained = false }
          let mutable retained = initial
          let expectedRead = { Slot = ReadSlotId 1UL; Value = ReadValue.Input(InputId 1UL, InputStamp 1UL, ValueToken 1UL) }
          let! result = async {
            try
              if attempt.Reads <> [ expectedRead ] then return Error "managed-build: immutable request read mismatch"
              elif not (sameFiles (request.Dotnet :: request.Inputs)) then return Error "managed-build: declared input closure mismatch"
              else
                return! lease.Run request.Holder (ClrInterop.cancellationToken cancellation) (fun id token -> async {
                  retained <- { initial with LeaseId = Some id }
                  if not (sameFiles (request.Dotnet :: request.Inputs)) then return Error "managed-build: declared input closure changed before launch"
                  else
                    let! result = ClrInterop.fromUncancelledTask (fun () -> execute request retained token)
                    retained <- result
                    return Ok () })
            with error -> return Error("managed-build: " + error.Message)
          }
          let final =
            match result with
            | Error _ when retained.Outcome = Cancelled && WorkCancellation.isRequested cancellation -> retained
            | Error message -> { retained with Outcome = Failed message }
            | Ok () -> retained
          let final = { final with CancellationRequested = final.CancellationRequested || WorkCancellation.isRequested cancellation }
          lock receiptGate (fun () -> receipt <- Some final)
          return StepOutcome.Complete(
            match final.Outcome with
            // This single-shot mailbox has one immutable request. The local
            // token is not an artifact identity or authority for downstream reuse.
            | Succeeded -> Completion.Succeeded(ValueToken 2UL)
            | Cancelled -> Completion.Cancelled
            | Failed message -> Completion.Failed { Code = "managed-build"; Message = message })
      }
      AsyncMailbox.create { Epoch = request.Epoch; MaxConcurrency = 1; CommandCapacity = 8 } evaluator
      |> Result.mapError (fun error -> string error)
      |> Result.map (fun mailbox -> { Mailbox = mailbox; Receipt = (fun () -> lock receiptGate (fun () -> receipt)); StartGate = obj (); Started = false }))

  let create request =
    createUsing { Run = fun holder cancellation work -> WorkLease.runWithCancellation "full_build" holder cancellation work } request

  let internal createWithLease client request =
    createUsing { Run = fun holder cancellation work -> WorkLease.runWith client "full_build" holder cancellation work } request

  /// Cold construction above has no process/lease effects. Explicit start owns
  /// admission; cancelling an observer does not retract these queued commands.
  let start handle = lock handle.StartGate (fun () ->
    if handle.Started then Error "managed-build: already started"
    else
      match AsyncMailbox.start handle.Mailbox with
      | Error error -> Error(string error)
      | Ok () ->
        handle.Started <- true
        [ Action.ReserveScope(ScopeId 1UL, RevisionId 1UL)
          Action.ReplaceScope(ScopeId 1UL, RevisionId 1UL,
            [ ScopeEntry.Define { Work = WorkId 1UL; Stamp = DefinitionStamp 1UL
                                  Reads = [ { Slot = ReadSlotId 1UL; Source = ReadSource.Input(InputId 1UL) } ] } ])
          Action.SetInputs [ { Input = InputId 1UL; Stamp = InputStamp 1UL; Value = ValueToken 1UL } ]
          Action.Demand(DemandId 1UL, WorkId 1UL) ]
        |> List.fold (fun state action ->
          state |> Result.bind (fun () -> AsyncMailbox.admit action handle.Mailbox |> Result.map ignore |> Result.mapError string)) (Ok ()))

  let cancel handle = AsyncMailbox.admit (Action.Release(DemandId 1UL)) handle.Mailbox
  let invalidate handle = AsyncMailbox.admit (Action.RemoveInputs [ InputId 1UL ]) handle.Mailbox
  let close handle = AsyncMailbox.beginClose handle.Mailbox |> AsyncMailbox.awaitClose
  let snapshot handle = AsyncMailbox.snapshot handle.Mailbox

  /// The terminal receipt remains diagnostic evidence after withdrawal. Only a
  /// separately returned eligible handle identifies a current successful offer.
  let wait handle = async {
    let! joined = AsyncMailbox.waitForIdle handle.Mailbox
    return joined |> Result.map (fun () ->
      let result = AsyncMailbox.tryResult (WorkId 1UL) handle.Mailbox
      handle.Receipt (), result)
  }
