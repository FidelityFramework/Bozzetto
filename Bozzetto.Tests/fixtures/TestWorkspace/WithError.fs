module TestWorkspace.WithError

// This module contains code that Bozzetto can evaluate to test error handling.
// The functions themselves are valid, but we'll eval broken expressions against them.

let validFunction x = x + 1

// The test will eval "let broken : int = \"not an int\";;" via Bozzetto
// to verify error diagnostics appear.
