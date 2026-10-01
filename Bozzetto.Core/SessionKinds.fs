/// Execution modes retained by the F# component-test engine.
module Bozzetto.SessionKinds

/// Isolated is a rejected compatibility request: production F# execution
/// belongs to separate SageFS. InProcess remains for component tests.
type FsiSessionKind =
  | Isolated
  | InProcess
