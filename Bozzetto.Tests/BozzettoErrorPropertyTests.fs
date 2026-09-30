module Bozzetto.Tests.BozzettoErrorPropertyTests

open System
open Expecto
open Expecto.Flip
open FsCheck
open FsCheck.FSharp
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Reflection
open Bozzetto
open Bozzetto.Tests.SharedGenerators

let private pick gen = (Gen.sample 1 gen).[0]

// ── Generators ──

let private genSessionState =
  Gen.elements [
    SessionState.Uninitialized
    SessionState.WarmingUp
    SessionState.Ready
    SessionState.Evaluating
    SessionState.Faulted
  ]

let private genNonEmptyString =
  Gen.elements ['a'..'z']
  |> Gen.listOfLength 8
  |> Gen.map (fun cs -> String(cs |> List.toArray))

let private genStringList =
  genNonEmptyString
  |> Gen.listOfLength 3

let private genBozzettoError =
  Gen.oneof [
    gen {
      let! tool = genNonEmptyString
      let! state = genSessionState
      let! tools = genStringList
      return BozzettoError.ToolNotAvailable(tool, state, tools)
    }
    genNonEmptyString |> Gen.map BozzettoError.SessionNotFound
    Gen.constant BozzettoError.NoActiveSessions
    genStringList |> Gen.map BozzettoError.AmbiguousSessions
    genNonEmptyString |> Gen.map BozzettoError.SessionCreationFailed
    genStringList |> Gen.map BozzettoError.NeedsRebuild
    gen {
      let! id = genNonEmptyString
      let! dir = genNonEmptyString
      return BozzettoError.DuplicateSession(id, dir)
    }
    gen {
      let! path = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.UnsafeSessionPath(path, reason)
    }
    gen {
      let! project = genNonEmptyString
      let! tfms = genStringList
      return BozzettoError.ProjectFrameworkNotHostable(
        project,
        tfms,
        ProjectCompatibility.UnsupportedTfmReason.NetFramework)
    }
    gen {
      let! id = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.SessionStopFailed(id, reason)
    }
    gen {
      let! id = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.SessionSwitchFailed(id, reason)
    }
    genNonEmptyString |> Gen.map BozzettoError.SessionNotRoutable
    gen {
      let! id = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.WorkerCommunicationFailed(id, reason)
    }
    genNonEmptyString |> Gen.map BozzettoError.WorkerSpawnFailed
    gen {
      let! id = genNonEmptyString
      let! op = genNonEmptyString
      let! sec = Gen.choose (1, 300) |> Gen.map float
      return BozzettoError.WorkerTimeout(id, op, sec)
    }
    gen {
      let! id = genNonEmptyString
      let! endpoint = genNonEmptyString
      let! status = Gen.choose (400, 599)
      return BozzettoError.WorkerHttpError(id, endpoint, status)
    }
    Gen.constant BozzettoError.PipeClosed
    genNonEmptyString |> Gen.map BozzettoError.EvalFailed
    genNonEmptyString |> Gen.map BozzettoError.ResetFailed
    genNonEmptyString |> Gen.map BozzettoError.HardResetFailed
    gen {
      let! exitCode = Gen.choose (1, 255)
      let! message = genNonEmptyString
      return BozzettoError.BuildFailed(exitCode, [ BuildDiagnostic.ofLine message ])
    }
    genNonEmptyString |> Gen.map BozzettoError.ScriptLoadFailed
    genNonEmptyString |> Gen.map BozzettoError.CheckFailed
    gen {
      let! id = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.CompletionFailed(id, reason)
    }
    genNonEmptyString |> Gen.map BozzettoError.CancelFailed
    Gen.constant BozzettoError.EvalSupersededByReset
    gen {
      let! name = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.WarmupOpenFailed(name, reason)
    }
    gen {
      let! id = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.WarmupContextFailed(id, reason)
    }
    gen {
      let! path = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.HotReloadFailed(path, reason)
    }
    gen {
      let! id = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.HotReloadStateError(id, reason)
    }
    gen {
      let! project = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.AppRunFailed(project, reason)
    }
    gen {
      let! count = Gen.choose (1, 20)
      let! minutes = Gen.choose (1, 60) |> Gen.map float
      return BozzettoError.RestartLimitExceeded(count, minutes)
    }
    genNonEmptyString |> Gen.map BozzettoError.DaemonStartFailed
    Gen.constant BozzettoError.DaemonNotRunning
    Gen.choose (1, 65535) |> Gen.map BozzettoError.PortInUse
    genNonEmptyString |> Gen.map BozzettoError.SseConnectionError
    gen {
      let! ctx = genNonEmptyString
      let! reason = genNonEmptyString
      return BozzettoError.JsonParseError(ctx, reason)
    }
    gen {
      let! reason = genNonEmptyString
      let! suggestion = genNonEmptyString
      return BozzettoError.CohortActionFailed(reason, suggestion)
    }
    Gen.constant (BozzettoError.Unexpected(Exception "test"))
    gen {
      let! pending = Gen.choose (0, 1000)
      let! capacity = Gen.choose (1, 1000)
      return BozzettoError.SupervisorBusy(pending, capacity)
    }
    genNonEmptyString |> Gen.map BozzettoError.MemoryPressureRefused
  ]

// ── Reflection helpers ──

let private errorDuType = typeof<BozzettoError>

let private allDuCaseInfos =
  FSharpType.GetUnionCases(errorDuType)

/// Build a concrete instance for each DU case using default field values.
let private allDuCaseInstances =
  allDuCaseInfos
  |> Array.map (fun case ->
    let fields =
      case.GetFields()
      |> Array.map (fun f ->
        match f.PropertyType with
        | t when t = typeof<string> -> box "test"
        | t when t = typeof<int> -> box 42
        | t when t = typeof<float> -> box 1.0
        | t when t = typeof<exn> -> box (Exception "test")
        | t when t = typeof<SessionState> -> box SessionState.Ready
        | t when t = typeof<string list> -> box [ "a"; "b" ]
        | t when t = typeof<BuildDiagnostic list> -> box [ BuildDiagnostic.ofLine "test error" ]
        | t when t = typeof<ProjectCompatibility.UnsupportedTfmReason> -> box ProjectCompatibility.UnsupportedTfmReason.NetFramework
        | t -> failwithf "Unhandled field type %s in case %s" t.Name case.Name)
    FSharpValue.MakeUnion(case, fields) :?> BozzettoError)

// ── Tests ──

[<Tests>]
let bozzettoErrorPropertyTests =
  testList "BozzettoError property tests" [

    // 1. describe is total — every DU case returns a non-empty string
    testCase "describe is total over all DU cases" <| fun _ ->
      allDuCaseInstances
      |> Array.iter (fun err ->
        let desc = BozzettoError.describe err
        desc
        |> String.IsNullOrWhiteSpace
        |> Expect.isFalse (sprintf "describe should return non-empty for %A" err))

    // 2. describe never throws on random inputs
    testPropertyWithConfig propConfig "describe never throws" <|
      fun () ->
        let err = pick genBozzettoError
        try
          BozzettoError.describe err |> ignore
          true
        with _ ->
          false

    // 3. toLogLevel is total — every case returns a valid LogLevel
    testCase "toLogLevel is total over all DU cases" <| fun _ ->
      let validLevels =
        [ LogLevel.Critical; LogLevel.Error
          LogLevel.Warning; LogLevel.Information ]
        |> Set.ofList
      allDuCaseInstances
      |> Array.iter (fun err ->
        let level = BozzettoError.toLogLevel err
        validLevels
        |> Set.contains level
        |> Expect.isTrue (sprintf "toLogLevel should return valid level for %A, got %A" err level))

    // 4. toHttpStatus returns codes in 400..599
    testPropertyWithConfig propConfig "toHttpStatus returns valid HTTP error codes (400-599)" <|
      fun () ->
        let err = pick genBozzettoError
        let status = BozzettoError.toHttpStatus err
        status >= 400 && status <= 599

    testCase "toHttpStatus is total over all DU cases" <| fun _ ->
      allDuCaseInstances
      |> Array.iter (fun err ->
        let status = BozzettoError.toHttpStatus err
        status
        |> fun s -> s >= 400 && s <= 599
        |> Expect.isTrue (sprintf "toHttpStatus should be 400-599 for %A, got %d" err status))

    // 5. Client and infrastructure classifications map onto HTTP 4xx.
    testPropertyWithConfig propConfig "isClientError implies toHttpStatus is 4xx" <|
      fun () ->
        let err = pick genBozzettoError
        let status = BozzettoError.toHttpStatus err
        match BozzettoError.isClientError err with
        | true -> status >= 400 && status <= 499
        | false -> true

    testPropertyWithConfig propConfig "4xx status implies isClientError or isInfraError" <|
      fun () ->
        let err = pick genBozzettoError
        let status = BozzettoError.toHttpStatus err
        match status >= 400 && status <= 499 with
        | true -> BozzettoError.isClientError err || BozzettoError.isInfraError err
        | false -> true

    // 6. isServerError ↔ toHttpStatus 500 consistency
    testPropertyWithConfig propConfig "isServerError is true iff toHttpStatus is 500" <|
      fun () ->
        let err = pick genBozzettoError
        let status = BozzettoError.toHttpStatus err
        BozzettoError.isServerError err = (status = 500)

    // 7. isGatewayError ↔ toHttpStatus 502/504 consistency
    testPropertyWithConfig propConfig "isGatewayError is true iff toHttpStatus is 502 or 504" <|
      fun () ->
        let err = pick genBozzettoError
        let status = BozzettoError.toHttpStatus err
        BozzettoError.isGatewayError err = (status = 502 || status = 504)

    // 8. Classification predicates are mutually exclusive and exhaustive
    testPropertyWithConfig propConfig "classification predicates are mutually exclusive" <|
      fun () ->
        let err = pick genBozzettoError
        let flags = [
          BozzettoError.isClientError err
          BozzettoError.isServerError err
          BozzettoError.isGatewayError err
          BozzettoError.isInfraError err
          BozzettoError.isOverloadError err
        ]
        let trueCount = flags |> List.filter id |> List.length
        trueCount = 1

    testCase "every DU case belongs to exactly one classification" <| fun _ ->
      allDuCaseInstances
      |> Array.iter (fun err ->
        let flags = [
          "isClientError", BozzettoError.isClientError err
          "isServerError", BozzettoError.isServerError err
          "isGatewayError", BozzettoError.isGatewayError err
          "isInfraError", BozzettoError.isInfraError err
          "isOverloadError", BozzettoError.isOverloadError err
        ]
        let trueOnes = flags |> List.filter snd |> List.map fst
        trueOnes
        |> List.length
        |> Expect.equal
          (sprintf "expected exactly 1 classification for %A, got [%s]"
            err (trueOnes |> String.concat ", "))
          1)

    // 9. DU completeness guard — detect new cases
    testCase "BozzettoError DU has exactly 41 cases" <| fun _ ->
      allDuCaseInfos
      |> Array.length
      |> Expect.equal
        "BozzettoError case count changed — update generators and property tests"
        41

    // 10. Unexpected wraps exception message
    testPropertyWithConfig propConfig "Unexpected description contains exception message" <|
      fun () ->
        let msg = pick genNonEmptyString
        let err = BozzettoError.Unexpected(Exception msg)
        let desc = BozzettoError.describe err
        desc.Contains(msg)

    // ── 409 ownership: client precondition unless it is a system conflict ──
    testPropertyWithConfig propConfig "isInfraError owns 409 except client preconditions" <|
      fun () ->
        let err = pick genBozzettoError
        let status = BozzettoError.toHttpStatus err
        BozzettoError.isInfraError err = (status = 409 && not (BozzettoError.isClientError err))

    // ── isOverloadError ↔ toHttpStatus 503 consistency ──
    testPropertyWithConfig propConfig "isOverloadError is true iff toHttpStatus is 503" <|
      fun () ->
        let err = pick genBozzettoError
        let status = BozzettoError.toHttpStatus err
        BozzettoError.isOverloadError err = (status = 503)
  ]
