module Bozzetto.DaemonStatusPayload

open System
open Bozzetto
open Bozzetto.McpJson

/// Observe and encode daemon resource state with exact byte counts.
let serialize
  (mcpPort: int)
  (health: Bozzetto.Features.HealthSnapshot option)
  (telemetry: Bozzetto.Server.DaemonTelemetry.Snapshot option) : string =
  let machineMemory = Bozzetto.Features.MachineMemory.current ()
  let leases = Bozzetto.Features.LeaseWatch.snapshot ()
  let anomalyRows =
    health
    |> Option.bind (fun h -> Some h.Anomalies)
    |> Option.defaultValue []
    |> List.choose (fun verdict ->
      Bozzetto.Features.HealthAnomaly.evidenceOf verdict
      |> Option.map (fun evidence ->
        {| signal = Bozzetto.Features.HealthAnomaly.signalName evidence.Signal
           state = Bozzetto.Features.HealthAnomaly.verdictName verdict
           message = Bozzetto.Features.HealthAnomaly.describe verdict |> Option.defaultValue ""
           observedValue = evidence.ObservedValue
           baselineMean = evidence.BaselineMean
           baselineStdDev = evidence.BaselineStdDev
           deviationInSigmas = evidence.DeviationInSigmas
           sustainedForSeconds = evidence.SustainedFor.TotalSeconds
           samplesSustained = evidence.SamplesSustained |}))
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
      health |> Option.map (fun h -> Bozzetto.MemoryPressure.explain h.MemoryPressure) |> Option.defaultValue ""
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
    anomalies = anomalyRows
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
    "anomalies", (payload.anomalies |> array (fun value -> object [
      "signal", (value.signal |> text)
      "state", (value.state |> text)
      "message", (value.message |> text)
      "observedValue", (value.observedValue |> real)
      "baselineMean", (value.baselineMean |> real)
      "baselineStdDev", (value.baselineStdDev |> real)
      "deviationInSigmas", (value.deviationInSigmas |> real)
      "sustainedForSeconds", (value.sustainedForSeconds |> real)
      "samplesSustained", (value.samplesSustained |> integer) ]))
    "available", (payload.available |> strings) ] |> render)
