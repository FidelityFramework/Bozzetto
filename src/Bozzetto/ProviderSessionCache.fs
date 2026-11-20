namespace Bozzetto.ComposerIntegration

open System
open System.IO
open System.Runtime.InteropServices
open Microsoft.Win32.SafeHandles
open Bozzetto
open Bozzetto.Providers
open Bozzetto.Composer.Protocol
open Fidelity.Data.JSON

// Bounded cleanup of provider-session state (#37). Bozzetto.Composer's Worker.fs keeps each
// session at <cacheRoot>/<worker host>/<session>/<worker epoch>/ with current.json inside.
// A prune is bound to that exact owner triple. The configured root is resolved to its physical
// path through every ancestor link. Linux opens each physical directory without following links
// and removes entries relative to those handles.
module ProviderSessionCache =
  [<Literal>]
  let ManifestFileName = "current.json"

  [<RequireQualifiedAccess>]
  type PruneOutcome =
    | Pruned
    | NoCacheRoot
    | NotFullyQualified
    | WrongFileName of name: string
    | WrongLayout of relative: string
    | OwnerMismatch of expected: string * actual: string
    | RootUnresolved of reason: string
    | LinkInPath of path: string
    | Missing
    | ManifestNotClosed
    | DeleteFailed of reason: string

  [<RequireQualifiedAccess>]
  type StartupSweep =
    | Pruned of count: int
    | SkippedLiveWorkers of pids: int list
    | SkippedUnverified of reason: string

  let defaultRoot () =
    let cacheBase =
      match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" with
      | value when not (String.IsNullOrWhiteSpace value) && Path.IsPathFullyQualified value -> value
      | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
    Path.Combine(cacheBase, "bozzetto", "provider-sessions")

  let private separators = [| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |]

  let private trimRoot (root: string) =
    let full = Path.GetFullPath root
    match Path.GetPathRoot full with
    | null -> full
    | volume when volume.Length = full.Length -> full
    | _ -> full.TrimEnd separators

  // LinkTarget is non-null for a symbolic link or junction, including a dangling one.
  let private isLink (path: string) : Result<bool, string> =
    try Ok(not (isNull (FileInfo(path).LinkTarget)))
    with
    | (:? IOException | :? UnauthorizedAccessException) as error -> Error error.Message

  let private linkTarget finalTarget (path: string) : Result<string option, string> =
    try
      match DirectoryInfo(path).ResolveLinkTarget finalTarget with
      | null -> Ok None
      | target -> Ok(Some(Path.GetFullPath target.FullName))
    with
    | (:? IOException | :? UnauthorizedAccessException) as error -> Error error.Message

  type private PhysicalPathError =
    | Unresolved of reason: string
    | BelowRoot of path: string

  // With a boundary, walk each link hop so a target cannot hide a component below it.
  let rec private resolvePhysical (boundary: string option) (depth: int) (full: string) : Result<string, PhysicalPathError> =
    if depth > 40 then Error(Unresolved "Too many levels of symbolic links.")
    else
      match Path.GetPathRoot full with
      | null
      | "" -> Error(Unresolved("Path has no root: " + full))
      | volume ->
        full.Substring(volume.Length).Split(separators, StringSplitOptions.RemoveEmptyEntries)
        |> Array.fold
          (fun resolved part ->
            resolved
            |> Result.bind (fun current ->
              let next = Path.Combine(current, part)
              if Option.contains current boundary then Error(BelowRoot next)
              else
                linkTarget (Option.isNone boundary) next
                |> Result.mapError Unresolved
                |> Result.bind (function
                  | None -> Ok next
                  | Some target -> resolvePhysical boundary (depth + 1) target)))
          (Ok volume)

  let private firstLink (paths: string list) : Result<string option, string> =
    paths
    |> List.fold
      (fun found path ->
        match found with
        | Ok None -> isLink path |> Result.map (fun linked -> if linked then Some path else None)
        | Ok(Some _)
        | Error _ -> found)
      (Ok None)

  let private validSegment (segment: string) =
    not (String.IsNullOrWhiteSpace segment) && segment <> "." && segment <> ".." && segment.IndexOfAny separators < 0

  let private trimEmptyParents (physicalRoot: string) (startDir: string) =
    let mutable current = Path.GetDirectoryName startDir
    while not (isNull current)
          && current.StartsWith(physicalRoot + string Path.DirectorySeparatorChar, StringComparison.Ordinal)
          && Directory.Exists current
          && isLink current = Ok false do
      if Directory.EnumerateFileSystemEntries current |> Seq.isEmpty then
        Directory.Delete(current, false)
        current <- Path.GetDirectoryName current
      else
        current <- null

  let private closedManifestText text =
    match Json.parse text with
    | Ok(JsonValue.Object fields) ->
      fields |> List.exists (function
        | "status", JsonValue.String status -> String.Equals(status, "closed", StringComparison.OrdinalIgnoreCase)
        | _ -> false)
    | _ -> false

  module private LinuxCache =
    [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
    extern int private openRoot(string path, int flags)

    [<DllImport("libc", EntryPoint = "openat", SetLastError = true)>]
    extern int private openAt(SafeFileHandle parent, string name, int flags)

    [<DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)>]
    extern int private unlinkAt(SafeFileHandle parent, string name, int flags)

    // Linux ARM and PowerPC use different O_DIRECTORY and O_NOFOLLOW bits.
    let private directory, noFollow =
      match RuntimeInformation.ProcessArchitecture with
      | Architecture.Arm
      | Architecture.Armv6
      | Architecture.Arm64
      | Architecture.Ppc64le -> 1 <<< 14, 1 <<< 15
      | _ -> 1 <<< 16, 1 <<< 17

    let private closeOnExec = 1 <<< 19
    let private nonBlocking = 1 <<< 11
    let private directoryFlags = directory ||| noFollow ||| closeOnExec
    let private removeDirectory = 0x200
    let private enoent = 2
    let private enotdir = 20
    let private eloop = 40

    exception private PruneRefused of PruneOutcome

    let private fail path (errno: int) =
      let detail = ComponentModel.Win32Exception(errno).Message
      raise (PruneRefused(PruneOutcome.DeleteFailed(sprintf "%s: %s" path detail)))

    let private handle fd = new SafeFileHandle(nativeint fd, ownsHandle = true)
    let private withFdPath (fd: SafeFileHandle) (action: string -> 'T) : 'T =
      let mutable added = false
      fd.DangerousAddRef(&added)
      try
        action (sprintf "/proc/self/fd/%d" (int (fd.DangerousGetHandle())))
      finally
        if added then fd.DangerousRelease()

    let private withChild parent name path flags action =
      let fd = openAt(parent, name, flags)
      if fd >= 0 then
        use child = handle fd
        action child
      else
        let errno = Marshal.GetLastPInvokeError()
        match errno with
        | n when n = enoent -> raise (PruneRefused PruneOutcome.Missing)
        | n when n = eloop || (n = enotdir && withFdPath parent (fun p -> isLink (Path.Combine(p, name)) = Ok true)) ->
          raise (PruneRefused(PruneOutcome.LinkInPath path))
        | n -> fail path n

    let private remove parent name flags =
      if unlinkAt(parent, name, flags) <> 0 then
        let errno = Marshal.GetLastPInvokeError()
        if errno <> enoent then fail name errno

    let rec private emptyDirectory directory =
      // /proc/self/fd only enumerates the open directory. Every traversal and
      // removal uses a single entry name relative to its owning handle.
      for path in withFdPath directory Directory.GetFileSystemEntries do
        let name = Path.GetFileName path
        let fd = openAt(directory, name, directoryFlags)
        if fd >= 0 then
          use child = handle fd
          emptyDirectory child
          remove directory name removeDirectory
        else
          let errno = Marshal.GetLastPInvokeError()
          if errno = enotdir || errno = eloop then remove directory name 0
          elif errno <> enoent then fail name errno

    let private checkManifest requireClosed epoch epochPath =
      // Non-blocking so a misplaced FIFO cannot stall cleanup.
      let flags = noFollow ||| closeOnExec ||| nonBlocking
      try
        withChild epoch ManifestFileName (Path.Combine(epochPath, ManifestFileName)) flags (fun manifest ->
          if requireClosed then
            use stream = new FileStream(manifest, FileAccess.Read)
            use reader = new StreamReader(stream)
            if not (closedManifestText (reader.ReadToEnd())) then
              raise (PruneRefused PruneOutcome.ManifestNotClosed))
      with PruneRefused PruneOutcome.Missing when not requireClosed -> ()

    let prune requireClosed (root: string) host session epoch =
      let rec walk parent (path: string) = function
        | part :: rest ->
          let next = Path.Combine(path, part)
          withChild parent part next directoryFlags (fun child -> walk child next rest)
        | [] ->
          let hostPath = Path.Combine(path, host)
          let sessionPath = Path.Combine(hostPath, session)
          let epochPath = Path.Combine(sessionPath, epoch)
          withChild parent host hostPath directoryFlags (fun hostFd ->
            withChild hostFd session sessionPath directoryFlags (fun sessionFd ->
              withChild sessionFd epoch epochPath directoryFlags (fun epochFd ->
                checkManifest requireClosed epochFd epochPath
                emptyDirectory epochFd
                remove sessionFd epoch removeDirectory)
              // Optional parent trimming cannot invalidate a completed epoch removal.
              unlinkAt(hostFd, session, removeDirectory) |> ignore)
            unlinkAt(parent, host, removeDirectory) |> ignore)
      try
        let fd = openRoot("/", directoryFlags)
        if fd < 0 then fail "/" (Marshal.GetLastPInvokeError())
        use volume = handle fd
        let parts = root.Split('/', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
        walk volume "/" parts
        PruneOutcome.Pruned
      with
      | PruneRefused outcome -> outcome
      | (:? IOException | :? UnauthorizedAccessException) as error -> PruneOutcome.DeleteFailed error.Message

  let private pruneResolved requireClosed physicalRoot host session epoch =
    if OperatingSystem.IsLinux() then LinuxCache.prune requireClosed physicalRoot host session epoch
    else
      let hostDir = Path.Combine(physicalRoot, host)
      let sessionDir = Path.Combine(hostDir, session)
      let target = Path.Combine(sessionDir, epoch)
      let manifest = Path.Combine(target, ManifestFileName)
      match firstLink [ physicalRoot; hostDir; sessionDir; target; manifest ] with
      | Error reason -> PruneOutcome.DeleteFailed reason
      | Ok(Some linked) -> PruneOutcome.LinkInPath linked
      | Ok None when not (Directory.Exists target) -> PruneOutcome.Missing
      | Ok None ->
        try
          if requireClosed && (not (File.Exists manifest) || not (closedManifestText (File.ReadAllText manifest))) then
            PruneOutcome.ManifestNotClosed
          else
            Directory.Delete(target, true)
            try trimEmptyParents physicalRoot target
            with
            | (:? IOException | :? UnauthorizedAccessException) as error ->
              Bozzetto.Utils.Log.warn "[provider-cache] could not trim parents of %s: %s" target error.Message
            PruneOutcome.Pruned
        with
        | (:? IOException | :? UnauthorizedAccessException) as error -> PruneOutcome.DeleteFailed error.Message

  let tryPruneManifestPath (cacheRoot: string option) (owner: WorkerAddress) (session: string) (manifestPath: string) : PruneOutcome =
    match cacheRoot with
    | None -> PruneOutcome.NoCacheRoot
    | Some root when String.IsNullOrWhiteSpace root || String.IsNullOrWhiteSpace manifestPath
                     || not (Path.IsPathFullyQualified root && Path.IsPathFullyQualified manifestPath) ->
      PruneOutcome.NotFullyQualified
    | Some root ->
      let rootFull = trimRoot root
      let manifestFull = Path.GetFullPath manifestPath
      let expected = [ owner.Host; session; owner.Epoch ]
      match Path.GetFileName manifestFull, Path.GetDirectoryName manifestFull with
      | name, _ when not (String.Equals(name, ManifestFileName, StringComparison.Ordinal)) -> PruneOutcome.WrongFileName name
      | _, null -> PruneOutcome.WrongLayout manifestFull
      | _, epochDir ->
        match resolvePhysical None 0 rootFull with
        | Error(Unresolved reason | BelowRoot reason) -> PruneOutcome.RootUnresolved reason
        | Ok physicalRoot ->
          // Resolve the candidate root up to physicalRoot. The prune walk checks the
          // owner segments (<host>/<session>/<epoch>) for links.
          let sessionDir = Path.GetDirectoryName epochDir
          let hostDir = if isNull sessionDir then null else Path.GetDirectoryName sessionDir
          let candidateRoot = if isNull hostDir then null else Path.GetDirectoryName hostDir
          let boundary = Some physicalRoot
          let resolvedCandidate =
            if isNull candidateRoot then Ok None
            else resolvePhysical boundary 0 (trimRoot candidateRoot) |> Result.map Some
          match resolvedCandidate with
          | Error(BelowRoot path) -> PruneOutcome.LinkInPath path
          | Ok(Some physicalCandidate) when physicalCandidate = physicalRoot ->
            let targetEpoch = Path.Combine(physicalRoot, Path.GetFileName hostDir, Path.GetFileName sessionDir, Path.GetFileName epochDir)
            let relative = Path.GetRelativePath(physicalRoot, targetEpoch)
            let segments = relative.Split(separators, StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
            if Path.IsPathRooted relative || segments.Length <> 3 || not (segments |> List.forall validSegment) then
              PruneOutcome.WrongLayout relative
            elif not (expected |> List.forall validSegment) || segments <> expected then
              PruneOutcome.OwnerMismatch(String.Join("/", expected), String.Join("/", segments))
            else
              pruneResolved false physicalRoot owner.Host session owner.Epoch
          | _ ->
            let epochFull = trimRoot epochDir
            let displayEpoch = resolvePhysical boundary 0 epochFull |> Result.defaultValue epochFull
            PruneOutcome.WrongLayout(Path.GetRelativePath(physicalRoot, displayEpoch))

  let pruneStaleCacheDirectories (cacheRoot: string) (liveEpoch: string option) : int =
    if String.IsNullOrWhiteSpace cacheRoot || not (Path.IsPathFullyQualified cacheRoot) || not (Directory.Exists cacheRoot) then 0
    else
      match resolvePhysical None 0 (trimRoot cacheRoot) with
      | Error(Unresolved reason | BelowRoot reason) ->
        Bozzetto.Utils.Log.warn "[provider-cache] cannot resolve %s: %s" cacheRoot reason
        0
      | Ok physicalRoot ->
        let safeDirs (dir: string) =
          match isLink dir with
          | Ok false ->
            try Directory.GetDirectories dir
            with
            | (:? IOException | :? UnauthorizedAccessException) as error ->
              Bozzetto.Utils.Log.warn "[provider-cache] cannot enumerate %s: %s" dir error.Message
              [||]
          | Ok true -> [||]
          | Error reason ->
            Bozzetto.Utils.Log.warn "[provider-cache] cannot inspect %s: %s" dir reason
            [||]
        [ for hostDir in safeDirs physicalRoot do
            for sessionDir in safeDirs hostDir do
              for epochDir in safeDirs sessionDir do
                let host = Path.GetFileName hostDir
                let session = Path.GetFileName sessionDir
                let epoch = Path.GetFileName epochDir
                if liveEpoch <> Some epoch && List.forall validSegment [ host; session; epoch ] then
                  match pruneResolved true physicalRoot host session epoch with
                  | PruneOutcome.Pruned -> yield ()
                  | PruneOutcome.DeleteFailed reason ->
                    Bozzetto.Utils.Log.warn "[provider-cache] cannot prune %s: %s" epochDir reason
                  | _ -> () ]
        |> List.length

  // Live composer-worker processes on this host, identified by the worker file in their argv
  // beside the --socket argument every worker launch carries (ComposerWorkerClient.start).
  let liveWorkerPids (workerFileNames: Set<string>) (processes: ProcessWatchdog.ProcRecord list) : int list =
    processes
    |> List.filter (fun record ->
      ProcessWatchdog.isLiveRecord record
      && List.contains "--socket" record.Argv
      && record.Argv |> List.exists (fun token -> Set.contains (Path.GetFileName token) workerFileNames))
    |> List.map (fun record -> record.Pid)

  // Startup has no epoch of its own yet, so it may only sweep when no composer-worker of any
  // daemon is alive on this host to own a closed-but-cleanupPending manifest. An unreadable
  // process table proves nothing and skips the sweep.
  let startupSweep (cacheRoot: string) (workerFileNames: Set<string>) (scan: Result<ProcessWatchdog.ProcRecord list, ProcessWatchdog.ProcScanError>) : StartupSweep =
    match scan with
    | Error error -> StartupSweep.SkippedUnverified(ProcessWatchdog.describeProcScanError error)
    | Ok processes ->
      match liveWorkerPids workerFileNames processes with
      | [] -> StartupSweep.Pruned(pruneStaleCacheDirectories cacheRoot None)
      | pids -> StartupSweep.SkippedLiveWorkers pids
