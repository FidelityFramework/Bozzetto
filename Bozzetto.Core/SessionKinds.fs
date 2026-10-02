/// Execution modes retained by the F# component-test engine.
module Bozzetto.SessionKinds

/// Isolated is a rejected compatibility request: production F# execution
/// is retired with embedded FSI hosting. InProcess remains for component tests.
type FsiSessionKind =
  | Isolated
  | InProcess
