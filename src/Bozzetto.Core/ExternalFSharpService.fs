/// The product boundary for F# execution formerly hosted inside Bozzetto.
module Bozzetto.ExternalFSharpService

let message =
  "Bozzetto no longer hosts F# sessions or evaluates F# configuration; embedded production FSI hosting is retired and no separate F# REPL service is part of its workflow. Use Composer for Clef projects: open an explicit .fidproj with composer_open_project or the /composer page on port 47749, reserve with composer_reserve_edit before editing, build with composer_build, and execute only through composer_run_current; Clefx interactive execution is not implemented yet. Validate F# changes to Bozzetto itself with dotnet build and the unfiltered test suite, started only after acquire_full_build_lease / acquire_test_suite_lease and ended with release_work_lease."

let refuse<'T> () : Result<'T, Bozzetto.BozzettoError> =
  Result.Error (Bozzetto.BozzettoError.SessionCreationFailed message)
