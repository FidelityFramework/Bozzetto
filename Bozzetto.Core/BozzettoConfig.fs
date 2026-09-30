/// Centralised runtime configuration for Bozzetto.
/// All environment variable reads happen here — no other module calls
/// Environment.GetEnvironmentVariable directly (except OTEL SDK integration points).
///
/// Rule: [<Literal>] for compile-time constants (usable in patterns/attributes/sprintf).
///       Plain let for runtime values (env-var derived, evaluated once at module init).
module Bozzetto.BozzettoConfig

open System

// ---------------------------------------------------------------------------
// Private helpers — parse env vars with logged fallback, never throw.
// Using eprintfn instead of a logger: config is read before LoggerFactory exists.
// ---------------------------------------------------------------------------

let private envInt (name: string) (defaultValue: int) =
  match Environment.GetEnvironmentVariable(name) with
  | null | "" -> defaultValue
  | s ->
    match Int32.TryParse(s) with
    | true, v -> v
    | false, _ ->
      eprintfn
        "[BozzettoConfig] WARNING: env var %s=%s is not a valid integer; using default %d"
        name s defaultValue
      defaultValue

let private envBool (name: string) (defaultValue: bool) =
  match Environment.GetEnvironmentVariable(name) with
  | null | "" -> defaultValue
  | s ->
    match s.Trim().ToLowerInvariant() with
    | "1" | "true" | "yes" -> true
    | "0" | "false" | "no" -> false
    | _ ->
      eprintfn
        "[BozzettoConfig] WARNING: env var %s=%s is not a valid bool; using default %b"
        name s defaultValue
      defaultValue

let private envString (name: string) (defaultValue: string) =
  match Environment.GetEnvironmentVariable(name) with
  | null | "" -> defaultValue
  | s -> s

// ---------------------------------------------------------------------------
// Compile-time constants — use [<Literal>] so they're usable in patterns.
// ---------------------------------------------------------------------------

/// Default MCP server port. Used in help text, match arms, and as base for DashboardPort.
[<Literal>]
let DefaultMcpPort = 47749

/// Default Dashboard port (MCP + 1). Named here so the "+1" relationship is documented once.
[<Literal>]
let DefaultDashboardPort = 47750

// ---------------------------------------------------------------------------
// Runtime-derived configuration — evaluated once at module initialisation.
// ---------------------------------------------------------------------------

/// Superseded by Timeouts.warmupInactivityLimit / Timeouts.warmupAbsoluteMax,
/// which awaitWorkerPort now actually reads: a flat "no port within N ms"
/// bound couldn't tell a stuck worker from a large repo genuinely still
/// discovering projects (fcs-trial-a/b/c, 2026-09-22). Kept — not read by
/// awaitWorkerPort any more — because BOZZETTO_WORKER_STARTUP_TIMEOUT_MS may
/// still be set in someone's environment; do not repurpose this name.
let WorkerStartupTimeoutMs : int =
  envInt "BOZZETTO_WORKER_STARTUP_TIMEOUT_MS" 120_000

/// Admission ceiling for the SessionManager mailbox's queue
/// (`MailboxProcessor<SessionCommand>.CurrentQueueLength`), mirroring
/// ElmLoop's own 256-message high-watermark alarm (`ElmLoop.fs:104-131`) —
/// the mailbox that didn't have an equivalent bound. Caller-facing ops
/// (create/stop/restart session) check this BEFORE posting and refuse with
/// `BozzettoError.SupervisorBusy` instead of queueing indefinitely behind
/// whatever is already backed up. Sized generously so it never engages
/// under real usage — only under genuine overload.
let SessionManagerQueueCapacity : int =
  envInt "BOZZETTO_SESSION_MANAGER_QUEUE_CAPACITY" 256

/// The loopback interface the daemon's HTTP listeners (MCP, dashboard) bind.
/// There is deliberately no non-loopback case: Bozzetto evaluates arbitrary F#
/// for whoever reaches those ports and has no authentication, so a LAN bind
/// would be remote code execution. See LoopbackHost.parse.
[<RequireQualifiedAccess>]
type LoopbackHost =
  /// "localhost" — Kestrel listens on both 127.0.0.1 and [::1].
  | Localhost
  | Ipv4
  | Ipv6

[<RequireQualifiedAccess>]
module LoopbackHost =

  /// The host as it appears in a URL.
  let urlHost (host: LoopbackHost) : string =
    match host with
    | LoopbackHost.Localhost -> "localhost"
    | LoopbackHost.Ipv4 -> "127.0.0.1"
    | LoopbackHost.Ipv6 -> "[::1]"

  /// The Kestrel listen URL for a port on this host.
  let listenUrl (host: LoopbackHost) (port: int) : string =
    sprintf "http://%s:%d" (urlHost host) port

  /// Parse a BOZZETTO_BIND_HOST value. Empty means the default (localhost);
  /// anything that is not a loopback form is an error that says why and what
  /// to do instead.
  let parse (raw: string) : Result<LoopbackHost, string> =
    match raw.Trim().ToLowerInvariant() with
    | "" | "localhost" -> Ok LoopbackHost.Localhost
    | "127.0.0.1" -> Ok LoopbackHost.Ipv4
    | "::1" | "[::1]" -> Ok LoopbackHost.Ipv6
    | _ ->
      Error (
        sprintf
          "BOZZETTO_BIND_HOST=%s is not supported. Bozzetto runs F# for anyone who can reach its HTTP ports and has no authentication, so it only listens on loopback. Unset BOZZETTO_BIND_HOST, or set it to localhost, 127.0.0.1 or ::1. To use Bozzetto inside a container, forward ports 47749 and 47750 to the container's loopback instead of binding all interfaces: VS Code Dev Containers \"forwardPorts\", `docker run --network host` (Linux), or `ssh -L 47749:localhost:47749 -L 47750:localhost:47750`."
          (raw.Trim()))

/// Bind host for HTTP servers (MCP and Dashboard).
/// BOZZETTO_BIND_HOST is read here only — all other modules use this value.
/// An Error here stops the daemon at startup (Program.runDaemon) and fails
/// `boz check`.
let BindHost : Result<LoopbackHost, string> =
  LoopbackHost.parse (envString "BOZZETTO_BIND_HOST" "")

/// Whether this process is running under external supervision (auto-restart on crash).
let IsSupervised : bool =
  envBool "BOZZETTO_SUPERVISED" false

/// Worker restart count injected by the supervisor. 0 = first launch.
/// Guards against restart storms — see RestartPolicy.
let RestartCount : int =
  envInt "BOZZETTO_RESTART_COUNT" 0

/// Whether hot reload (assembly patching) is enabled for this worker session.
let HotReloadEnabled : bool =
  envBool "BOZZETTO_HOT_RELOAD" false

/// Whether browser dev-reload (live page refresh) is enabled.
let DevReloadEnabled : bool =
  envBool "BOZZETTO_DEVRELOAD" false

/// OTLP exporter endpoint. Empty string means OTEL is not configured.
let OtelEndpoint : string =
  envString "OTEL_EXPORTER_OTLP_ENDPOINT" ""

/// OTLP protocol (grpc or http/protobuf).
let OtelProtocol : string =
  envString "OTEL_EXPORTER_OTLP_PROTOCOL" "grpc"

/// OTEL service name for this process.
let OtelServiceName : string =
  envString "OTEL_SERVICE_NAME" "bozzetto"

/// Whether OpenTelemetry is configured (endpoint is non-empty).
let OtelConfigured : bool =
  OtelEndpoint <> ""

/// MCP port parsed from the env var only (CLI arg takes precedence — see Program.parseMcpPort).
/// Most code should use the resolved port from Program.parseMcpPort, not this value directly.
let McpPortFromEnv : int =
  envInt "BOZZETTO_MCP_PORT" DefaultMcpPort
