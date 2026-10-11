module Bozzetto.Tests.BozzettoErrorJsonTests

open System
open Fidelity.Data.JSON
open Expecto
open Expecto.Flip
open Microsoft.FSharp.Reflection
open Bozzetto

/// Reuse the exhaustive case generator from classification tests.
let private allErrorCases : BozzettoError list =
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
        | t when t = typeof<exn> -> box (Exception "test")
        | t when t = typeof<string list> -> box ([ "a"; "b" ] : string list)
        | t when t = typeof<SessionState> -> box SessionState.Ready
        | t when t = typeof<BuildDiagnostic list> -> box ([ BuildDiagnostic.ofLine "test error" ] : BuildDiagnostic list)
        | t when t = typeof<ProjectCompatibility.UnsupportedTfmReason> -> box ProjectCompatibility.UnsupportedTfmReason.NetFramework
        | _ -> box "unknown")
    FSharpValue.MakeUnion(case, fields) :?> BozzettoError)
  |> Array.toList

[<Tests>]
let bozzettoErrorJsonTests =
  testList "BozzettoError.toJson" [

    testList "produces correct structure for representative cases" [
      test "WarmupContextFailed has case, fields, message, suggestedAction" {
        let err = BozzettoError.WarmupContextFailed("sess-1", "timeout")
        let json = BozzettoError.toJson err
        json.case
        |> Expect.equal "case should be WarmupContextFailed" "WarmupContextFailed"
        json.fields.["sessionId"] |> JsonValue.asString |> Option.get
        |> Expect.equal "sessionId field" "sess-1"
        json.fields.["reason"] |> JsonValue.asString |> Option.get
        |> Expect.equal "reason field" "timeout"
        json.message
        |> Expect.stringContains "message mentions session" "sess-1"
        json.suggestedAction
        |> Expect.stringContains "suggests hard reset" "hard_reset"
      }

      test "PortInUse preserves port field as int" {
        let err = BozzettoError.PortInUse 8080
        let json = BozzettoError.toJson err
        json.case
        |> Expect.equal "case" "PortInUse"
        json.fields.["port"] |> JsonValue.tryAsInt64 |> Option.get |> int
        |> Expect.equal "port field" 8080
        json.suggestedAction
        |> Expect.stringContains "mentions mcp-port" "mcp-port"
      }

      test "Unexpected serializes exn message, not the exn object" {
        let err = BozzettoError.Unexpected(InvalidOperationException "boom")
        let json = BozzettoError.toJson err
        json.case
        |> Expect.equal "case" "Unexpected"
        json.fields.Count
        |> Expect.equal "one field" 1
        // The unnamed field is called "Item" by FSharp.Reflection
        let fieldValue = json.fields.Values |> Seq.head |> JsonValue.asString |> Option.get
        fieldValue
        |> Expect.equal "exn field is message string" "boom"
      }

      test "NoActiveSessions has empty fields" {
        let err = BozzettoError.NoActiveSessions
        let json = BozzettoError.toJson err
        json.case
        |> Expect.equal "case" "NoActiveSessions"
        json.fields.Count
        |> Expect.equal "no fields" 0
        json.message
        |> Expect.isNonEmpty "message should not be empty"
      }

      test "WorkerTimeout preserves float field" {
        let err = BozzettoError.WorkerTimeout("s1", "eval", 30.5)
        let json = BozzettoError.toJson err
        json.case
        |> Expect.equal "case" "WorkerTimeout"
        json.fields.["sessionId"] |> JsonValue.asString |> Option.get
        |> Expect.equal "sessionId" "s1"
        json.fields.["operation"] |> JsonValue.asString |> Option.get
        |> Expect.equal "operation" "eval"
        json.fields.["timeoutSec"] |> JsonValue.asNumber |> Option.get
        |> Expect.equal "timeoutSec" 30.5
      }
    ]

    test "toJson works for every DU case without throwing" {
      allErrorCases
      |> List.iter (fun err ->
        let json = BozzettoError.toJson err
        json.case
        |> Expect.isNonEmpty "case should not be empty"
        json.message
        |> Expect.isNonEmpty "message should not be empty"
        json.suggestedAction
        |> Expect.isNonEmpty "suggestedAction should not be empty")
    }

    test "every error including nested build diagnostics roundtrips through Fidelity.Data" {
      allErrorCases
      |> List.iter (fun error ->
        let value = BozzettoError.toJsonValue error
        let parsed = value |> Json.serialize |> Json.parse |> Result.defaultWith failtest
        parsed |> JsonValue.prop "case" |> Option.bind JsonValue.asString
        |> Expect.equal "case survives the actual JSON boundary" (Some (BozzettoError.toJson error).case)
        parsed |> JsonValue.prop "fields" |> Option.get |> JsonValue.count
        |> Expect.equal "every declared field survives" (BozzettoError.toJson error).fields.Count)
    }

    test "suggestedAction covers every DU case" {
      let caseCount = FSharpType.GetUnionCases(typeof<BozzettoError>).Length
      allErrorCases
      |> List.length
      |> Expect.equal "should cover all cases" caseCount
      allErrorCases
      |> List.iter (fun err ->
        BozzettoError.suggestedAction err
        |> Expect.isNonEmpty "suggestedAction should not be empty")
    }

    test "toJson case name matches FSharpReflection case name" {
      allErrorCases
      |> List.iter (fun err ->
        let info, _ = FSharpValue.GetUnionFields(err, typeof<BozzettoError>)
        let json = BozzettoError.toJson err
        json.case
        |> Expect.equal (sprintf "case name for %s" info.Name) info.Name)
    }

    test "toJson fields count matches DU field count" {
      allErrorCases
      |> List.iter (fun err ->
        let info, _ = FSharpValue.GetUnionFields(err, typeof<BozzettoError>)
        let json = BozzettoError.toJson err
        json.fields.Count
        |> Expect.equal
             (sprintf "field count for %s" info.Name)
             (info.GetFields().Length))
    }

    test "toJson message equals describe output" {
      allErrorCases
      |> List.iter (fun err ->
        let json = BozzettoError.toJson err
        json.message
        |> Expect.equal "message should match describe" (BozzettoError.describe err))
    }
  ]
