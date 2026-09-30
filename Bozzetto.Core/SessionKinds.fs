/// Where a session's FSI lives. Its own early file because both the daemon side (which decides how to launch a
/// worker) and the worker side (which decides how to create the FSI session) need it.
module Bozzetto.SessionKinds

/// In an isolated host process that shares no assembly with Bozzetto (how every session runs: no package or
/// runtime of Bozzetto's can ever conflict with the user's), or in this process (the reference implementation
/// the tests use — issue #141 by construction if a real session ever ran this way, so production never
/// constructs it; there is no environment-variable opt-out anymore).
type FsiSessionKind =
  | Isolated
  | InProcess
