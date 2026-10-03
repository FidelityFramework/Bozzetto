#!/usr/bin/env bash
# drive-cohort.sh — Bozzetto-specific driver for the COHORT landing-gate demo
# recording.
#
# Meant to run as the --command of record-terminal.sh (asciinema), or as a
# record-orchestrator.sh "terminal" job: the recording is the printed MCP
# transcript. (It used to run under record-x11.sh with the daemon's web
# dashboard open in Chromium as the on-screen surface; that dashboard was
# removed, so there is nothing on an X display to capture any more.) It:
#   1. Builds a throwaway temp git repo carrying a real, tiny, PREBUILT
#      Expecto fixture project via the CohortOrchestrator's `setup-fixture`
#      subcommand — see scripts/demos/cohort-orchestrator/Program.fs and
#      Bozzetto.Tests/CohortLandingGateIntegrationTests.fs's header for why the
#      fixture must be prebuilt and SDK-pinned before the daemon ever sees it.
#   2. Starts an ISOLATED Bozzetto daemon (own --mcp-port, own BOZZETTO_DATA_DIR,
#      --owner-pid/--ttl/--no-resume, NEVER the real ~/.bozzetto) rooted at
#      that fixture repo.
#   3. Runs the CohortOrchestrator's `run-beats` subcommand, which drives two
#      real MCP client connections ("alice", "bob") through the five demo
#      beats (join -> disjoint claims + a real conflict -> set_integration_ref
#      + live testing -> a GOOD landing lands -> a BREAKING landing is
#      automatically BLOCKED by a real, discovered, failing test), printing
#      each beat's MCP response to stdout so the transcript proves the flow.
#   4. Tears down everything it started (orchestrator, daemon, and any worker
#      the daemon spawned) — never by process name, only by the pid this
#      script itself recorded.
#
# This script owns cleanup of only what it starts.

set -euo pipefail

SCRIPT_NAME="$(basename "$0")"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

MCP_PORT=""
DATA_DIR=""
BOZZETTO_BIN=""
ORCHESTRATOR_DLL=""
LOG_DIR=""
PAUSE_SECONDS=3
GATE_TIMEOUT_SECONDS=40

usage() {
  cat <<EOF
Usage: $SCRIPT_NAME --mcp-port N --data-dir DIR [options]

Required:
  --mcp-port N          Port for this job's isolated Bozzetto daemon (its
                         control listener takes N+1). Must not collide with
                         any other running daemon (default main daemon uses
                         47749/47750 — never use those here).
  --data-dir DIR         Directory for this job's BOZZETTO_DATA_DIR AND the
                         throwaway fixture git repo. Must be fresh — the real
                         ~/.bozzetto is never touched.

Options:
  --duration SECONDS         Accepted for record-orchestrator.sh video-job
                              compatibility and ignored: the run takes as long
                              as the five beats take.
  --bozzetto-bin PATH           Path to the built Bozzetto executable (default:
                              <repo>/Bozzetto/bin/Release/net10.0/Bozzetto — build
                              it first: dotnet build Bozzetto/Bozzetto.fsproj -c Release)
  --orchestrator-dll PATH       Path to the built CohortOrchestrator.dll
                              (default: <repo>/scripts/demos/cohort-orchestrator/bin/Release/net10.0/CohortOrchestrator.dll —
                              build it first: dotnet build scripts/demos/cohort-orchestrator/CohortOrchestrator.fsproj -c Release)
  --log-dir DIR                  Where to write daemon/orchestrator logs
                              (default: <data-dir>/logs)
  --pause-seconds N                Camera pause between beats (default: 3)
  --gate-timeout-seconds N          How long the orchestrator polls to prove
                              the breaking landing NEVER fast-forwards the
                              integration branch (default: 40)
  -h, --help                        Show this help and exit
EOF
}

log() { echo "[$SCRIPT_NAME] $*" >&2; }
die() { echo "[$SCRIPT_NAME] ERROR: $*" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mcp-port) MCP_PORT="$2"; shift 2 ;;
    --data-dir) DATA_DIR="$2"; shift 2 ;;
    --duration) shift 2 ;;
    --bozzetto-bin) BOZZETTO_BIN="$2"; shift 2 ;;
    --orchestrator-dll) ORCHESTRATOR_DLL="$2"; shift 2 ;;
    --log-dir) LOG_DIR="$2"; shift 2 ;;
    --pause-seconds) PAUSE_SECONDS="$2"; shift 2 ;;
    --gate-timeout-seconds) GATE_TIMEOUT_SECONDS="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown argument: $1 (see --help)" ;;
  esac
done

[[ -n "$MCP_PORT" ]] || { usage; die "--mcp-port is required"; }
[[ -n "$DATA_DIR" ]] || { usage; die "--data-dir is required"; }

for tool in curl pgrep git dotnet; do
  command -v "$tool" >/dev/null 2>&1 || die "'$tool' not found in PATH"
done

[[ -n "$BOZZETTO_BIN" ]] || BOZZETTO_BIN="$REPO_ROOT/Bozzetto/bin/Release/net10.0/Bozzetto"
[[ -x "$BOZZETTO_BIN" ]] || die "bozzetto binary not found or not executable: $BOZZETTO_BIN (build it first: dotnet build Bozzetto/Bozzetto.fsproj -c Release)"

[[ -n "$ORCHESTRATOR_DLL" ]] || ORCHESTRATOR_DLL="$SCRIPT_DIR/cohort-orchestrator/bin/Release/net10.0/CohortOrchestrator.dll"
[[ -f "$ORCHESTRATOR_DLL" ]] || die "orchestrator dll not found: $ORCHESTRATOR_DLL (build it first: dotnet build scripts/demos/cohort-orchestrator/CohortOrchestrator.fsproj -c Release)"

[[ -n "$LOG_DIR" ]] || LOG_DIR="$DATA_DIR/logs"
mkdir -p "$DATA_DIR" "$LOG_DIR"
FIXTURE_REPO="$DATA_DIR/fixture-repo"

log "mcp-port=$MCP_PORT data-dir=$DATA_DIR"
log "bozzetto-bin=$BOZZETTO_BIN orchestrator-dll=$ORCHESTRATOR_DLL"
log "fixture-repo=$FIXTURE_REPO pause=${PAUSE_SECONDS}s gate-timeout=${GATE_TIMEOUT_SECONDS}s"

DAEMON_PID=""
ORCH_PID=""

# Kill a pid and all of its descendants: TERM first. Only ever called with a
# pid THIS script started — never a name-based match.
kill_tree() {
  local pid="$1"
  [[ -n "$pid" ]] || return 0
  local children
  children="$(pgrep -P "$pid" 2>/dev/null || true)"
  local c
  for c in $children; do
    kill_tree "$c"
  done
  kill -TERM "$pid" 2>/dev/null || true
}

cleanup() {
  local exit_code=$?
  log "cleanup: orchestrator pid=${ORCH_PID:-none} daemon pid=${DAEMON_PID:-none}"
  [[ -n "$ORCH_PID" ]] && kill_tree "$ORCH_PID"
  [[ -n "$DAEMON_PID" ]] && kill_tree "$DAEMON_PID"
  sleep 0.5
  local pid
  for pid in "$ORCH_PID" "$DAEMON_PID"; do
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      log "pid $pid still alive after TERM, sending KILL"
      kill -9 "$pid" 2>/dev/null || true
    fi
  done
  log "cleanup done"
  exit "$exit_code"
}
trap cleanup EXIT INT TERM

log "setting up the fixture git repo (real, tiny, prebuilt Expecto project)"
dotnet "$ORCHESTRATOR_DLL" setup-fixture --dir "$FIXTURE_REPO" >"$LOG_DIR/fixture-setup.log" 2>&1 \
  || die "fixture setup failed, see $LOG_DIR/fixture-setup.log"
log "fixture ready at $FIXTURE_REPO"

log "starting isolated Bozzetto daemon rooted at the fixture repo"
# The daemon's OWN process working directory is what set_integration_ref
# treats as "the main repo" (there is no --working-directory flag) — a
# subshell cd's into the fixture repo and execs Bozzetto there, exactly
# CohortLandingGateIntegrationTests.fs's startIsolatedDaemon
# (psi.WorkingDirectory <- workingDir).
(
  cd "$FIXTURE_REPO"
  exec env BOZZETTO_DATA_DIR="$DATA_DIR" "$BOZZETTO_BIN" \
    --mcp-port "$MCP_PORT" \
    --owner-pid "$$" \
    --ttl 10m \
    --no-resume
) >"$LOG_DIR/daemon.log" 2>&1 &
DAEMON_PID=$!
log "daemon pid=$DAEMON_PID"

log "waiting for daemon health on port $MCP_PORT"
ready=0
for _ in $(seq 1 120); do
  if ! kill -0 "$DAEMON_PID" 2>/dev/null; then
    die "daemon exited before becoming healthy, see $LOG_DIR/daemon.log"
  fi
  if curl -sf -m 2 -o /dev/null "http://localhost:$MCP_PORT/health"; then
    ready=1
    break
  fi
  sleep 1
done
[[ "$ready" -eq 1 ]] || die "daemon did not become healthy within 120s, see $LOG_DIR/daemon.log"
log "daemon healthy"

log "running the cohort orchestrator (two real MCP connections, five beats)"
dotnet "$ORCHESTRATOR_DLL" run-beats \
  --mcp-port "$MCP_PORT" \
  --main-repo "$FIXTURE_REPO" \
  --pause-seconds "$PAUSE_SECONDS" \
  --gate-timeout-seconds "$GATE_TIMEOUT_SECONDS" \
  2>&1 | tee "$LOG_DIR/orchestrator.log" &
ORCH_PID=$!
log "orchestrator pid=$ORCH_PID"

wait "$ORCH_PID"
ORCH_EXIT=$?
ORCH_PID=""
log "orchestrator exited with code $ORCH_EXIT (see $LOG_DIR/orchestrator.log) — cleanup trap tears everything down"

exit "$ORCH_EXIT"
