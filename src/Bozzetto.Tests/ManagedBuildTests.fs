module Bozzetto.Tests.ManagedBuildTests

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks
open Bozzetto
open Bozzetto.Build
open Fidelity.FSharp.Incremental
open Fidelity.FSharp.Incremental.Hosting
open Expecto
open Expecto.Flip

let private bound path : ManagedBuild.FileIdentity =
  { Path = path; Sha256 = SHA256.HashData(File.ReadAllBytes path) |> Convert.ToHexString |> _.ToLowerInvariant() }

let private root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))
let private dotnet =
  let runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
  Path.Combine(DirectoryInfo(runtime).Parent.Parent.Parent.FullName, if OperatingSystem.IsWindows() then "dotnet.exe" else "dotnet")

let private fixture body =
  // Generated build inputs/artifacts remain in the primary checkout's ignored
  // package scratch; this is not a clone, secondary checkout or source owner.
  let folder = Path.Combine(root, "nupkg", "managed-build-controls", Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory folder |> ignore
  let project = Path.Combine(folder, "control.fsproj")
  File.WriteAllText(project, "<Project DefaultTargets=\"Build\"><Target Name=\"Build\">" + body folder + "</Target></Project>")
  let request : ManagedBuild.Request = {
    Epoch = EpochId 1UL; Checkout = root; Project = project; Dotnet = bound dotnet
    Configuration = "Release"; NoRestore = true; Inputs = [ bound project ]; Artifacts = []
    EvidenceDirectory = Path.Combine(folder, "evidence"); Holder = "managed-build-control"
    Timeout = TimeSpan.FromSeconds 15.
  }
  folder, request

let private client onRelease : WorkLease.Client = {
  Acquire = fun kind holder -> async {
    (kind, holder) |> Expect.equal "The managed owner uses the existing full-build lease." ("full_build", "managed-build-control")
    return WorkLease.Granted("managed-control-lease", DateTimeOffset.UtcNow.AddMinutes 5.) }
  Renew = fun _ _ -> async.Return(WorkLease.Renewed(DateTimeOffset.UtcNow.AddMinutes 5.))
  Release = fun _ -> async { onRelease (); return Ok () }
  Withdraw = fun _ _ -> async.Return()
  Delay = fun milliseconds token -> Task.Delay(milliseconds, token) |> Async.AwaitTask
  Now = fun () -> DateTimeOffset.UtcNow
}

let private expectOk = function Ok value -> value | Error error -> failtestf "Unexpected refusal: %A" error
let private start request api =
  let handle = ManagedBuild.createWithLease api request |> expectOk
  ManagedBuild.start handle |> expectOk
  handle
let private wait handle =
  ManagedBuild.wait handle |> Async.StartAsTask |> fun pending -> pending.WaitAsync(TimeSpan.FromSeconds 20.)
let private receipt (result: Result<ManagedBuild.Receipt option * ResultHandle option, Failure>) =
  match result with
  | Ok(Some value, eligible) -> value, eligible
  | other -> failtestf "Expected exact terminal receipt, got %A" other
let private close handle = ManagedBuild.close handle |> Async.StartAsTask
let private cleanup handle =
  { new IDisposable with
      member _.Dispose() = close handle |> fun pending -> pending.GetAwaiter().GetResult() |> ignore }
let private exited (identity: ManagedBuild.ProcessIdentity) =
  try
    use ownedProcess = Process.GetProcessById identity.Pid
    ownedProcess.HasExited || ownedProcess.StartTime.ToUniversalTime() <> identity.StartedUtc
  with :? ArgumentException -> true

let private fileArrival (path: string) =
  let signal = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
  let watcher = new FileSystemWatcher(Path.GetDirectoryName path, Path.GetFileName path)
  watcher.Created.Add(fun _ -> signal.TrySetResult() |> ignore)
  watcher.EnableRaisingEvents <- true
  if File.Exists path then signal.TrySetResult() |> ignore
  watcher, signal.Task.WaitAsync(TimeSpan.FromSeconds 10.)

let private managedTest name (body: unit -> Task<unit>) =
  testCaseAsync name (async { do! body() |> Async.AwaitTask })

[<Tests>]
let tests =
  testList "Managed build lifetime" [
    managedTest "cold construction has no admission and success binds joined logs and artifact" (fun () -> task {
      let folder, request = fixture (fun folder ->
        $"<Message Text=\"managed-control-output\" Importance=\"high\"/><WriteLinesToFile File=\"{folder}/result.txt\" Lines=\"owned artifact\" Overwrite=\"true\"/>")
      let request = { request with Artifacts = [ Path.Combine(folder, "result.txt") ] }
      let mutable acquired = 0
      let mutable released = false
      let original = client (fun () -> released <- true)
      let api = { original with Acquire = fun kind holder -> async { acquired <- acquired + 1; return! original.Acquire kind holder } }
      let handle = ManagedBuild.createWithLease api request |> expectOk
      use _cleanup = cleanup handle
      acquired |> Expect.equal "Construction is cold." 0
      Directory.Exists request.EvidenceDirectory |> Expect.isFalse "Construction has no output side effects."
      ManagedBuild.start handle |> expectOk
      let! result = wait handle
      let value, eligible = receipt result
      value.Outcome |> Expect.equal "The process succeeds." ManagedBuild.Succeeded
      value.LeaseId |> Expect.equal "Exact admission is retained." (Some "managed-control-lease")
      value.Arguments |> Expect.contains "Build servers are explicitly disabled." "--disable-build-servers"
      value.RootAndStreamsJoined |> Expect.isTrue "Root and redirected streams physically joined."
      value.DetachedDescendantsContained |> Expect.isFalse "No unearned OS containment claim."
      value.Process.Value |> exited |> Expect.isTrue "The exact root identity is no longer running."
      value.Stdout.Value |> Expect.equal "stdout is byte-bound." (bound value.Stdout.Value.Path)
      File.ReadAllText(value.Stdout.Value.Path) |> Expect.stringContains "Output belongs to this attempt." "managed-control-output"
      value.Artifacts |> Expect.equal "Declared output bytes are retained." [ bound request.Artifacts.Head ]
      eligible.IsSome |> Expect.isTrue "Joined current success has a Foundation result."
      released |> Expect.isTrue "The lease is released after completion."
      let! _ = close handle
      return ()
    })

    managedTest "refused admission records reason and never launches a child" (fun () -> task {
      let _, request = fixture (fun _ -> "<Message Text=\"must-not-run\"/>")
      let api = { client (fun () -> failtest "No unowned lease may be released.") with
                    Acquire = fun _ _ -> async.Return(WorkLease.Refused "capacity exhausted") }
      let handle = start request api
      use _cleanup = cleanup handle
      let! result = wait handle
      let value, eligible = receipt result
      value.Outcome |> Expect.equal "Specific refusal survives." (ManagedBuild.Failed "work-lease: full_build refused: capacity exhausted")
      value.Process |> Expect.isNone "Refusal creates no process."
      Directory.Exists request.EvidenceDirectory |> Expect.isFalse "Refusal creates no log directory."
      eligible |> Expect.isNone "Refusal is never reusable success."
      let! _ = close handle
      return ()
    })

    managedTest "nonzero exit keeps the actual diagnostic and cannot offer success" (fun () -> task {
      let _, request = fixture (fun _ -> "<Error Text=\"managed-control-deliberate-failure\"/>")
      let handle = start request (client ignore)
      use _cleanup = cleanup handle
      let! result = wait handle
      let value, eligible = receipt result
      value.ExitCode |> Expect.equal "Exact nonzero exit is retained." (Some 1)
      value.Outcome |> Expect.equal "Failure is not hidden by lease success." (ManagedBuild.Failed "managed-build: dotnet exited 1")
      File.ReadAllText(value.Stdout.Value.Path) |> Expect.stringContains "Failure log is retained." "managed-control-deliberate-failure"
      value.Process.Value |> exited |> Expect.isTrue "Failed root was joined."
      eligible |> Expect.isNone "No failure offer."
      let! _ = close handle
      return ()
    })

    managedTest "wrong source digest refuses before acquiring or creating logs" (fun () -> task {
      let _, request = fixture (fun _ -> "")
      let request = { request with Inputs = [ { request.Inputs.Head with Sha256 = String.replicate 64 "0" } ] }
      let api = { client ignore with Acquire = fun _ _ -> failtest "Changed closure must not acquire." }
      let handle = start request api
      use _cleanup = cleanup handle
      let! result = wait handle
      let value, eligible = receipt result
      value.Outcome |> Expect.equal "Exact input mismatch reason." (ManagedBuild.Failed "managed-build: declared input closure mismatch")
      value.Process |> Expect.isNone "No process after mismatched source identity."
      eligible |> Expect.isNone "Changed inputs cannot offer success."
      let! _ = close handle
      return ()
    })

    managedTest "process deadline requests cleanup and joins the managed root" (fun () -> task {
      if not (OperatingSystem.IsLinux()) then skiptest "The controlled Exec sleep fixture requires Linux."
      let _, request = fixture (fun _ -> "<Exec Command=\"sleep 30\"/>")
      let request = { request with Timeout = TimeSpan.FromSeconds 2. }
      let mutable released = false
      let handle = start request (client (fun () -> released <- true))
      use _cleanup = cleanup handle
      let! result = wait handle
      let value, eligible = receipt result
      value.Outcome |> Expect.equal "A deadline is a timeout failure, not Foundation cancellation." (ManagedBuild.Failed "managed-build: deadline expired")
      value.TimedOut |> Expect.isTrue "The receipt identifies its deadline."
      value.CancellationRequested |> Expect.isTrue "The deadline requested physical cleanup."
      value.Process.Value |> exited |> Expect.isTrue "Canceled root is physically gone."
      value.RootAndStreamsJoined |> Expect.isTrue "Both pipes reached completion after process cleanup."
      released |> Expect.isTrue "Lease release follows cleanup."
      eligible |> Expect.isNone "Timed-out build cannot become eligible."
      let! _ = close handle
      return ()
    })

    managedTest "a changed declared input during execution withdraws successful exit eligibility" (fun () -> task {
      if not (OperatingSystem.IsLinux()) then skiptest "The controlled Exec fixture requires Linux."
      let folder, request = fixture (fun folder -> $"<Exec Command=\"echo ready &gt; {folder}/ready; sleep 1\"/>")
      let source = Path.Combine(folder, "declared-source.txt")
      File.WriteAllText(source, "before")
      let request = { request with Inputs = request.Inputs @ [ bound source ] }
      let watcher, arrived = fileArrival (Path.Combine(folder, "ready"))
      use watcher = watcher
      let handle = start request (client ignore)
      use _cleanup = cleanup handle
      do! arrived
      File.WriteAllText(source, "after")
      let! result = wait handle
      let value, eligible = receipt result
      value.ExitCode |> Expect.equal "A successful command is not source-currentness evidence." (Some 0)
      value.Outcome |> Expect.equal "The changed declared premise is refused." (ManagedBuild.Failed "managed-build: declared input closure changed")
      value.InputsUnchanged |> Expect.isFalse "Receipt records the actual input change."
      eligible |> Expect.isNone "Changed input cannot become an eligible result."
      let! _ = close handle
      return ()
    })

    managedTest "observer cancellation does not cancel owned work but explicit release does" (fun () -> task {
      if not (OperatingSystem.IsLinux()) then skiptest "The controlled Exec sleep fixture requires Linux."
      let folder, request = fixture (fun folder -> $"<Exec Command=\"echo $$ &gt; {folder}/child.pid; echo ready &gt; {folder}/ready; sleep 30\"/>")
      let watcher, arrived = fileArrival (Path.Combine(folder, "ready"))
      use watcher = watcher
      let mutable released = false
      let handle = start request (client (fun () -> released <- true))
      use _cleanup = cleanup handle
      do! arrived
      use knownChild = Process.GetProcessById(Int32.Parse(File.ReadAllText(Path.Combine(folder, "child.pid"))))
      let knownIdentity : ManagedBuild.ProcessIdentity = { Pid = knownChild.Id; StartedUtc = knownChild.StartTime.ToUniversalTime() }
      use observation = new CancellationTokenSource()
      let detached = Async.StartAsTask(ManagedBuild.wait handle, cancellationToken = observation.Token)
      observation.Cancel()
      try let! _ = detached in failtest "Observer cancellation must detach its waiter."
      with :? OperationCanceledException -> ()
      released |> Expect.isFalse "A canceled observer does not release work demand."
      let operation = ManagedBuild.cancel handle |> expectOk
      let! _ = AsyncMailbox.observe operation |> Async.StartAsTask
      let! result = wait handle
      let value, eligible = receipt result
      value.Outcome |> Expect.equal "Foundation cancellation retains its terminal classification after lease cleanup." ManagedBuild.Cancelled
      value.TimedOut |> Expect.isFalse "Explicit demand withdrawal is not a deadline failure."
      value.Process.Value |> exited |> Expect.isTrue "Explicit cancellation joins the actual root."
      value.RootAndStreamsJoined |> Expect.isTrue "Explicit cancellation joins streams."
      knownIdentity |> exited |> Expect.isTrue "The controlled known Exec child is no longer running."
      released |> Expect.isTrue "Explicit cleanup eventually releases admission."
      eligible |> Expect.isNone "Withdrawn demand cannot offer success."
      let! _ = close handle
      return ()
    })
  ] |> testSequenced
