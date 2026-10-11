module Bozzetto.Tests.BozzettoErrorClassificationTests

open Expecto
open Expecto.Flip
open FsCheck
open FsCheck.FSharp
open Microsoft.FSharp.Reflection
open Bozzetto

/// Generate representative instances of every BozzettoError case.
/// Uses FSharp.Reflection to ensure exhaustiveness — if a case is added,
/// this generator fails loudly rather than silently missing it.
let allErrorCases : BozzettoError list =
  let cases = FSharpType.GetUnionCases(typeof<BozzettoError>)
  cases
  |> Array.map (fun case ->
    let fields =
      case.GetFields()
      |> Array.map (fun f ->
        match f.PropertyType with
        | t when t = typeof<string> -> box "test"
        | t when t = typeof<int> -> box 42
        | t when t = typeof<float> -> box 1.0
        | t when t = typeof<exn> -> box (System.Exception "test")
        | t when t = typeof<string list> -> box (["a"; "b"] : string list)
        | t when t = typeof<SessionState> -> box SessionState.Ready
        | t when t = typeof<BuildDiagnostic list> -> box ([ BuildDiagnostic.ofLine "test error" ] : BuildDiagnostic list)
        | t when t = typeof<ProjectCompatibility.UnsupportedTfmReason> -> box ProjectCompatibility.UnsupportedTfmReason.NetFramework
        | _ -> box "unknown")
    FSharpValue.MakeUnion(case, fields) :?> BozzettoError)
  |> Array.toList

[<Tests>]
let bozzettoErrorClassificationTests =
  testList "BozzettoError classification" [

    test "allErrorCases covers every DU case" {
      let caseCount = FSharpType.GetUnionCases(typeof<BozzettoError>).Length
      allErrorCases
      |> List.length
      |> Expect.equal "should have one instance per case" caseCount
    }

    testList "isClientError" [
      test "client errors map to 4xx HTTP status" {
        allErrorCases
        |> List.filter BozzettoError.isClientError
        |> List.iter (fun e ->
          let status = BozzettoError.toHttpStatus e
          (status, 400)
          |> Expect.isGreaterThanOrEqual
            (sprintf "client error %A should have status >= 400" e)
          (status, 500)
          |> Expect.isLessThan
            (sprintf "client error %A should have status < 500" e))
      }
    ]

    testList "isServerError" [
      test "server errors map to 500 HTTP status" {
        allErrorCases
        |> List.filter BozzettoError.isServerError
        |> List.iter (fun e ->
          BozzettoError.toHttpStatus e
          |> Expect.equal
            (sprintf "server error %A should be 500" e) 500)
      }
    ]

    testList "isGatewayError" [
      test "gateway errors map to 502/504 HTTP status" {
        allErrorCases
        |> List.filter BozzettoError.isGatewayError
        |> List.iter (fun e ->
          let status = BozzettoError.toHttpStatus e
          [502; 504]
          |> List.contains status
          |> Expect.isTrue
            (sprintf "gateway error %A should be 502 or 504, got %d" e status))
      }
    ]

    testList "isInfraError" [
      test "infra errors map to 409 HTTP status" {
        allErrorCases
        |> List.filter BozzettoError.isInfraError
        |> List.iter (fun e ->
          BozzettoError.toHttpStatus e
          |> Expect.equal
            (sprintf "infra error %A should be 409" e) 409)
      }
    ]

    testList "isOverloadError" [
      test "overload errors map to 503 HTTP status" {
        allErrorCases
        |> List.filter BozzettoError.isOverloadError
        |> List.iter (fun e ->
          BozzettoError.toHttpStatus e
          |> Expect.equal
            (sprintf "overload error %A should be 503" e) 503)
      }
    ]

    testList "classification properties" [
      test "every case is in exactly one category" {
        allErrorCases
        |> List.iter (fun e ->
          let categories =
            [ BozzettoError.isClientError e
              BozzettoError.isServerError e
              BozzettoError.isGatewayError e
              BozzettoError.isInfraError e
              BozzettoError.isOverloadError e ]
            |> List.filter id
            |> List.length
          categories
          |> Expect.equal
            (sprintf "case %A should be in exactly 1 category" e) 1)
      }

      test "describe returns non-empty for all cases" {
        allErrorCases
        |> List.iter (fun e ->
          BozzettoError.describe e
          |> String.length
          |> fun len ->
            (len, 0)
            |> Expect.isGreaterThan
              (sprintf "describe for %A should be non-empty" e))
      }

      test "toHttpStatus is valid HTTP code for all cases" {
        allErrorCases
        |> List.iter (fun e ->
          let status = BozzettoError.toHttpStatus e
          (status, 100)
          |> Expect.isGreaterThanOrEqual
            (sprintf "status for %A should be >= 100" e)
          (status, 600)
          |> Expect.isLessThan
            (sprintf "status for %A should be < 600" e))
      }

      test "toLogLevel is valid for all cases" {
        allErrorCases
        |> List.iter (fun e ->
          let level = BozzettoError.toLogLevel e
          // LogLevel.None = 6, should never be None
          (int level, int Microsoft.Extensions.Logging.LogLevel.None)
          |> Expect.isLessThan
            (sprintf "log level for %A should not be None" e))
      }
    ]
  ]
