module Bozzetto.DaemonStatusPayload

open System
open Bozzetto
open Bozzetto.McpJson

let anomalyJson (verdict: Bozzetto.Features.HealthAnomaly.Verdict) =
  match verdict with
  | Bozzetto.Features.HealthAnomaly.Verdict.Drifting evidence
  | Bozzetto.Features.HealthAnomaly.Verdict.Broken evidence ->
    Some(object [
      "signal", (Bozzetto.Features.HealthAnomaly.signalName evidence.Signal |> text)
      "state", (Bozzetto.Features.HealthAnomaly.verdictName verdict |> text)
      "message", (Bozzetto.Features.HealthAnomaly.describe verdict |> Option.defaultValue "" |> text)
      "observedValue", (evidence.ObservedValue |> real)
      "baselineMean", (evidence.BaselineMean |> real)
      "baselineStdDev", (evidence.BaselineStdDev |> real)
      "deviationInSigmas", (evidence.DeviationInSigmas |> real)
      "sustainedForSeconds", (evidence.SustainedFor.TotalSeconds |> real)
      "samplesSustained", (evidence.SamplesSustained |> integer) ])
  | Bozzetto.Features.HealthAnomaly.Verdict.OperationalFault fault ->
    Some(object [
      "signal", (Bozzetto.Features.HealthAnomaly.operationalFaultName fault.Kind |> text)
      "state", (Bozzetto.Features.HealthAnomaly.verdictName verdict |> text)
      "kind", ("operational" |> text)
      "message", (Bozzetto.Features.HealthAnomaly.describe verdict |> Option.defaultValue "" |> text)
      "count", (fault.Count |> integer)
      "observedAt", (fault.ObservedAt |> timestamp) ])
  | Bozzetto.Features.HealthAnomaly.Verdict.InsufficientHistory
  | Bozzetto.Features.HealthAnomaly.Verdict.Normal -> None

let serializeWithStatus
  (mcpPort: int)
  (health: Bozzetto.Features.HealthSnapshot option)
  (telemetry: Bozzetto.Server.DaemonTelemetry.Snapshot option)
  (leasedCommands: Bozzetto.Server.DaemonTelemetry.LeasedCommandLiveness list)
  (retrieval: Bozzetto.Web.Shared.Protocol.RetrievalStatus) : string =
  let machineMemory = Bozzetto.Features.MachineMemory.current ()
  let leases = Bozzetto.Features.LeaseWatch.snapshot ()
  let anomalyValues =
    health
    |> Option.map _.Anomalies
    |> Option.defaultValue []
    |> List.choose anomalyJson
  let sessionSummaries =
    health |> Option.map (fun h -> h.SessionSummaries) |> Option.defaultValue []
  let countStatus status =
    sessionSummaries |> List.filter (fun session -> session.Status = status) |> List.length
  let payload = {|
    state = "Ready"
    scope = "Daemon"
    daemonVersion = health |> Option.map (fun h -> h.Version) |> Option.defaultValue Bozzetto.Server.DaemonInfo.version
    coreVersion = Bozzetto.Features.FrictionTelemetryTypes.BozzettoVersion.current ()
    daemonPid = health |> Option.map (fun h -> h.DaemonPid) |> Option.defaultValue Environment.ProcessId
    mcpPort = health |> Option.map (fun h -> h.DaemonPort) |> Option.defaultValue mcpPort
    uptimeSeconds =
      health |> Option.map (fun h -> h.Uptime.TotalSeconds) |> Option.defaultValue 0.0
    overall = health |> Option.map (fun h -> Bozzetto.Features.DaemonHealth.healthLabel (Bozzetto.Features.DaemonHealth.overallStatus h)) |> Option.defaultValue "Unknown"
    memoryPressure =
      health |> Option.map (fun h -> Bozzetto.MemoryPressure.describe h.MemoryPressure) |> Option.defaultValue (Bozzetto.MemoryPressure.describe Bozzetto.MemoryPressure.Normal)
    memoryPressureNote =
      let availableFrac =
        if machineMemory.TotalBytes > 0L then float machineMemory.AvailableBytes / float machineMemory.TotalBytes else 0.0
      let thresholds = Bozzetto.MemorySupervisor.pressureThresholds Bozzetto.MemorySupervisor.defaultThresholds
      health |> Option.map (fun h -> Bozzetto.MemoryPressure.explainAt thresholds availableFrac h.MemoryPressure) |> Option.defaultValue ""
    machineMemory =
      {| totalBytes = machineMemory.TotalBytes
         availableBytes = machineMemory.AvailableBytes |}
    daemonResidentBytes = health |> Option.map (fun h -> int64 h.MemoryMB * 1_048_576L) |> Option.defaultValue 0L
    aggregateResidentBytes = telemetry |> Option.map (fun t -> t.AggregateResidentBytes) |> Option.defaultValue 0L
    aggregateCpuPercent = telemetry |> Option.map (fun t -> t.AggregateCpuPercent) |> Option.defaultValue 0.0
    telemetrySampledAt = telemetry |> Option.map (fun t -> t.SampledAt) |> Option.defaultValue DateTimeOffset.UtcNow
    processes = telemetry |> Option.map (fun t -> t.Processes) |> Option.defaultValue []
    sessions = {|
      provider = "fsharp"
      total = sessionSummaries |> List.length
      ready = countStatus Bozzetto.Features.SessionHealthStatus.Ready
      evaluating = countStatus Bozzetto.Features.SessionHealthStatus.Evaluating
      warmingUp = countStatus Bozzetto.Features.SessionHealthStatus.WarmingUp
      faulted = countStatus Bozzetto.Features.SessionHealthStatus.Faulted
      stopped = countStatus Bozzetto.Features.SessionHealthStatus.Stopped
    |}
    leases = {| activeCount = leases.ActiveCount
                queueDepth = leases.QueueDepth
                active = leases.Active
                queue = leases.Queue |}
    available = Bozzetto.Affordances.availableTools Bozzetto.SessionState.Uninitialized
  |}
  (object [
    "state", (payload.state |> text)
    "scope", (payload.scope |> text)
    "daemonVersion", (payload.daemonVersion |> text)
    "coreVersion", (payload.coreVersion |> text)
    "daemonPid", (payload.daemonPid |> integer)
    "mcpPort", (payload.mcpPort |> integer)
    "uptimeSeconds", (payload.uptimeSeconds |> real)
    "overall", (payload.overall |> text)
    "memoryPressure", (payload.memoryPressure |> text)
    "memoryPressureNote", (payload.memoryPressureNote |> text)
    "machineMemory", object [
      "totalBytes", (payload.machineMemory.totalBytes |> signed)
      "availableBytes", (payload.machineMemory.availableBytes |> signed) ]
    "daemonResidentBytes", (payload.daemonResidentBytes |> signed)
    "aggregateResidentBytes", (payload.aggregateResidentBytes |> signed)
    "aggregateCpuPercent", (payload.aggregateCpuPercent |> real)
    "telemetrySampledAt", (payload.telemetrySampledAt |> timestamp)
    "processes", (payload.processes |> array (fun value -> object [
      "ProcessId", (value.ProcessId |> integer)
      "Role", (value.Role |> text)
      "ResidentBytes", (value.ResidentBytes |> signed)
      "CpuTime", (value.CpuTime |> fun value -> text (value.ToString("c")))
      "CpuPercent", (value.CpuPercent |> real) ]))
    "sessions", object [
      "provider", (payload.sessions.provider |> text)
      "total", (payload.sessions.total |> integer)
      "ready", (payload.sessions.ready |> integer)
      "evaluating", (payload.sessions.evaluating |> integer)
      "warmingUp", (payload.sessions.warmingUp |> integer)
      "faulted", (payload.sessions.faulted |> integer)
      "stopped", (payload.sessions.stopped |> integer) ]
    "leases", object [
      "activeCount", (payload.leases.activeCount |> integer)
      "queueDepth", (payload.leases.queueDepth |> integer)
      "active", (payload.leases.active |> array (fun value -> object [
        "Holder", (value.Holder |> text)
        "Kind", (value.Kind |> text)
        "ExpiresAt", (value.ExpiresAt |> timestamp) ]))
      "queue", (payload.leases.queue |> array (fun value -> object [
        "Holder", (value.Holder |> text)
        "Kind", (value.Kind |> text)
        "RequestedAt", (value.RequestedAt |> timestamp) ])) ]
    "leasedCommands", (leasedCommands |> array (fun cmd -> object [
      "runId", (cmd.RunId |> text)
      "leaseId", (cmd.LeaseId |> text)
      "processId", (cmd.ProcessId |> integer)
      "outcome", (cmd.Outcome |> text)
      "startedAtMs", (cmd.StartedAtMs |> signed)
      "lastLivenessVerifiedAtMs", (cmd.LastLivenessVerifiedAtMs |> optional signed)
      "leaseLive", (cmd.LeaseLive |> boolean)
      "leaseExpiresAt", (cmd.LeaseExpiresAt |> optional timestamp) ]))
    "anomalies", (anomalyValues |> array id)
    "retrieval", Bozzetto.Server.RetrievalObservation.daemonStatusJson retrieval
    "available", (payload.available |> strings) ] |> render)

let serializeWithLiveness
  (mcpPort: int)
  (health: Bozzetto.Features.HealthSnapshot option)
  (telemetry: Bozzetto.Server.DaemonTelemetry.Snapshot option)
  (leasedCommands: Bozzetto.Server.DaemonTelemetry.LeasedCommandLiveness list) : string =
  serializeWithStatus mcpPort health telemetry leasedCommands (Bozzetto.Server.RetrievalObservation.currentForStatus None)

let serialize
  (mcpPort: int)
  (health: Bozzetto.Features.HealthSnapshot option)
  (telemetry: Bozzetto.Server.DaemonTelemetry.Snapshot option) : string =
  serializeWithLiveness mcpPort health telemetry (Bozzetto.Server.DaemonTelemetry.currentLeasedCommandLiveness ())
