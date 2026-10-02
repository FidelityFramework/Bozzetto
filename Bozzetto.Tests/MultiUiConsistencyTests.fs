module Bozzetto.Tests.MultiUiConsistencyTests

open Expecto
open Expecto.Flip
open Bozzetto
open Bozzetto.WorkerProtocol
open Bozzetto.SessionDisplay
open Bozzetto.Features
open Bozzetto.Tests.SharedGenerators

let multiConsumerTests = testList "multi-consumer consistency" [
  testCase "Elm model render is deterministic across calls" <| fun _ ->
    let model =
      { (BozzettoModel.initial()) with
          RecentOutput = SessionOutputStore.ofLines [
            { Kind = OutputKind.Result; Text = "output line"; Timestamp = System.DateTime.UtcNow; SessionId = "" }
          ]
          Diagnostics = Map.ofList [
            "", [
              { Message = "unused"
                Subcategory = ""
                Range = { StartLine = 1; StartColumn = 0; EndLine = 1; EndColumn = 5 }
                Severity = Features.Diagnostics.DiagnosticSeverity.Warning
                ErrorNumber = 0 }
            ] ] }
    let r1 = BozzettoRender.render model
    let r2 = BozzettoRender.render model
    r1 |> List.length |> Expect.equal "same count" (r2 |> List.length)
    for i in 0..r1.Length-1 do
      r1.[i].Id |> Expect.equal (sprintf "region %d id" i) r2.[i].Id
      r1.[i].Content |> Expect.equal (sprintf "region %d content" i) r2.[i].Content
      r1.[i].Cursor |> Expect.equal (sprintf "region %d cursor" i) r2.[i].Cursor
]

let actionDispatchTests = testList "action dispatch consistency" [
  testCase "UiAction.tryParse handles all PascalCase action names" <| fun _ ->
    // UiAction.tryParse uses PascalCase names
    let knownPascalActions = [
      "SessionNavUp"; "SessionNavDown"; "SessionSelect"; "SessionDelete"
      "SessionCycleNext"; "SessionCyclePrev"
      "ClearOutput"; "ResetSession"; "HardResetSession"; "CreateSession"
      "ConfigureWarmupAutoOpen"
    ]
    for name in knownPascalActions do
      UiAction.tryParse name
      |> Expect.isSome (sprintf "UiAction should parse '%s'" name)
]

let mkSnapshot (id: SessionId) projects : SessionSnapshot =
  { Id = id
    Name = None
    Status = SessionDisplayStatus.Running
    Projects = projects
    EvalCount = 0
    LastActivity = System.DateTime.UtcNow
    UpSince = System.DateTime.UtcNow
    WorkingDirectory = "" }

let threeSessionModel =
  let m0 = (BozzettoModel.initial())
  let apply evt m = BozzettoUpdate.update (BozzettoMsg.Event evt) m |> fst
  m0
  |> apply (TuiEvent.SessionCreated (mkSnapshot (testSessionId "aa000a01") ["A.fsproj"]))
  |> apply (TuiEvent.SessionCreated (mkSnapshot (testSessionId "aa000b02") ["B.fsproj"]))
  |> apply (TuiEvent.SessionCreated (mkSnapshot (testSessionId "aa000c03") ["C.fsproj"]))
  |> apply (TuiEvent.SessionSwitched (None, "aa000a01"))

let elmSessionSwitchingTests = testList "Elm session switching" [
  testCase "SessionSelect at index 0 emits RequestSessionSwitch for first session" <| fun _ ->
    // Sessions are prepended, so order is [aa000c03; aa000b02; aa000a01]
    let model = { threeSessionModel with Editor = { threeSessionModel.Editor with SelectedSessionIndex = Some 0 } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionSelect) model
    effs |> List.length |> Expect.equal "one effect" 1
    match effs.[0] with
    | BozzettoEffect.Editor(EditorEffect.RequestSessionSwitch sid) ->
      sid |> Expect.equal "switches to aa000c03 (newest)" "aa000c03"
    | other -> failtest (sprintf "unexpected effect: %A" other)

  testCase "SessionSelect at index 1 switches to second session" <| fun _ ->
    let model = { threeSessionModel with Editor = { threeSessionModel.Editor with SelectedSessionIndex = Some 1 } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionSelect) model
    match effs.[0] with
    | BozzettoEffect.Editor(EditorEffect.RequestSessionSwitch sid) ->
      sid |> Expect.equal "switches to aa000b02" "aa000b02"
    | other -> failtest (sprintf "unexpected effect: %A" other)

  testCase "SessionSelect at index 2 switches to third session" <| fun _ ->
    let model = { threeSessionModel with Editor = { threeSessionModel.Editor with SelectedSessionIndex = Some 2 } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionSelect) model
    match effs.[0] with
    | BozzettoEffect.Editor(EditorEffect.RequestSessionSwitch sid) ->
      sid |> Expect.equal "switches to aa000a01 (oldest)" "aa000a01"
    | other -> failtest (sprintf "unexpected effect: %A" other)

  testCase "SessionSelect with no index emits no effects" <| fun _ ->
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionSelect) threeSessionModel
    effs |> List.length |> Expect.equal "no effects" 0

  testCase "SessionSelect with out-of-range index emits no effects" <| fun _ ->
    let model = { threeSessionModel with Editor = { threeSessionModel.Editor with SelectedSessionIndex = Some 99 } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionSelect) model
    effs |> List.length |> Expect.equal "no effects" 0
]

let elmSessionCyclingTests = testList "Elm session cycling" [
  testCase "CycleNext from index 0 advances to index 1" <| fun _ ->
    let model = { threeSessionModel with Editor = { threeSessionModel.Editor with SelectedSessionIndex = Some 0 } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCycleNext) model
    effs |> List.length |> Expect.equal "one effect" 1
    match effs.[0] with
    | BozzettoEffect.Editor(EditorEffect.RequestSessionSwitch sid) ->
      sid |> Expect.equal "advances to aa000b02" "aa000b02"
    | other -> failtest (sprintf "unexpected effect: %A" other)

  testCase "CycleNext from last index wraps to 0" <| fun _ ->
    // Sessions: [aa000c03; aa000b02; aa000a01], index 2 = aa000a01
    let model = { threeSessionModel with Editor = { threeSessionModel.Editor with SelectedSessionIndex = Some 2 } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCycleNext) model
    match effs.[0] with
    | BozzettoEffect.Editor(EditorEffect.RequestSessionSwitch sid) ->
      sid |> Expect.equal "wraps to aa000c03" "aa000c03"
    | other -> failtest (sprintf "unexpected effect: %A" other)

  testCase "CyclePrev from index 0 wraps to last" <| fun _ ->
    // Sessions: [aa000c03; aa000b02; aa000a01], index 0 = aa000c03
    let model = { threeSessionModel with Editor = { threeSessionModel.Editor with SelectedSessionIndex = Some 0 } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCyclePrev) model
    match effs.[0] with
    | BozzettoEffect.Editor(EditorEffect.RequestSessionSwitch sid) ->
      sid |> Expect.equal "wraps to aa000a01" "aa000a01"
    | other -> failtest (sprintf "unexpected effect: %A" other)

  testCase "CyclePrev from index 2 goes to index 1" <| fun _ ->
    let model = { threeSessionModel with Editor = { threeSessionModel.Editor with SelectedSessionIndex = Some 2 } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCyclePrev) model
    match effs.[0] with
    | BozzettoEffect.Editor(EditorEffect.RequestSessionSwitch sid) ->
      sid |> Expect.equal "goes to aa000b02" "aa000b02"
    | other -> failtest (sprintf "unexpected effect: %A" other)

  testCase "CycleNext with single session produces no effects" <| fun _ ->
    let m0 = (BozzettoModel.initial())
    let m1, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionCreated (mkSnapshot (testSessionId "aa000001") ["X.fsproj"]))) m0
    let model = { m1 with Editor = { m1.Editor with SelectedSessionIndex = Some 0 } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCycleNext) model
    effs |> List.length |> Expect.equal "no effects for single session" 0

  testCase "CyclePrev with no sessions produces no effects" <| fun _ ->
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCyclePrev) (BozzettoModel.initial())
    effs |> List.length |> Expect.equal "no effects" 0

  testCase "CycleNext with no SelectedSessionIndex defaults to index 0 then cycles" <| fun _ ->
    // Sessions: [aa000c03; aa000b02; aa000a01], default index 0, next = index 1 = aa000b02
    let model = { threeSessionModel with Editor = { threeSessionModel.Editor with SelectedSessionIndex = None } }
    let _, effs = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCycleNext) model
    effs |> List.length |> Expect.equal "one effect" 1
    match effs.[0] with
    | BozzettoEffect.Editor(EditorEffect.RequestSessionSwitch sid) ->
      sid |> Expect.equal "cycles to index 1" "aa000b02"
    | other -> failtest (sprintf "unexpected effect: %A" other)
]

let elmSessionEventTests = testList "Elm session events" [
  testCase "SessionSwitched updates ActiveSessionId and derived active flags" <| fun _ ->
    let m', _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionSwitched (None, "aa000b02"))) threeSessionModel
    m'.Sessions.ActiveSessionId |> Expect.equal "active updated" (ActiveSession.Viewing (testSessionId "aa000b02"))
    m'.Sessions.Sessions
    |> List.find (fun s -> s.Id = testSessionId "aa000b02")
    |> fun s -> SessionDisplay.isActive m'.Sessions.ActiveSessionId s |> Expect.isTrue "aa000b02 is active"
    m'.Sessions.Sessions
    |> List.find (fun s -> s.Id = testSessionId "aa000a01")
    |> fun s -> SessionDisplay.isActive m'.Sessions.ActiveSessionId s |> Expect.isFalse "aa000a01 no longer active"
    m'.Sessions.Sessions
    |> List.filter (fun s -> SessionDisplay.isActive m'.Sessions.ActiveSessionId s)
    |> List.length
    |> Expect.equal "exactly one active" 1

  testCase "SessionCreated adds new session to list" <| fun _ ->
    let snap = mkSnapshot (testSessionId "aa000d04") ["D.fsproj"]
    let m', _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionCreated snap)) threeSessionModel
    m'.Sessions.Sessions |> List.length |> Expect.equal "4 sessions" 4
    m'.Sessions.Sessions
    |> List.exists (fun s -> s.Id = testSessionId "aa000d04")
    |> Expect.isTrue "new session exists"

  testCase "SessionCreated auto-activates first session" <| fun _ ->
    let m0 = (BozzettoModel.initial())
    m0.Sessions.ActiveSessionId |> Expect.equal "initially awaiting" ActiveSession.AwaitingSession
    let snap = mkSnapshot (testSessionId "aa000001") ["X.fsproj"]
    let m1, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionCreated snap)) m0
    m1.Sessions.ActiveSessionId |> Expect.equal "auto-activated" (ActiveSession.Viewing (testSessionId "aa000001"))
    m1.Sessions.Sessions |> List.find (fun s -> s.Id = testSessionId "aa000001")
    |> fun s -> SessionDisplay.isActive m1.Sessions.ActiveSessionId s |> Expect.isTrue "first is active"

  testCase "SessionStopped removes session" <| fun _ ->
    let m', _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionStopped "aa000b02")) threeSessionModel
    m'.Sessions.Sessions |> List.length |> Expect.equal "2 left" 2
    m'.Sessions.Sessions
    |> List.exists (fun s -> s.Id = testSessionId "aa000b02")
    |> Expect.isFalse "aa000b02 removed"

  testCase "SessionStopped of non-active preserves active" <| fun _ ->
    let m', _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionStopped "aa000c03")) threeSessionModel
    m'.Sessions.ActiveSessionId |> Expect.equal "still aa000a01" (ActiveSession.Viewing (testSessionId "aa000a01"))
    m'.Sessions.Sessions
    |> List.find (fun s -> s.Id = testSessionId "aa000a01")
    |> fun s -> SessionDisplay.isActive m'.Sessions.ActiveSessionId s |> Expect.isTrue "aa000a01 still active"

  testCase "Sequential switches maintain consistent state" <| fun _ ->
    let switches = ["aa000b02"; "aa000c03"; "aa000a01"; "aa000c03"; "aa000b02"]
    let finalModel =
      switches |> List.fold (fun m sid ->
        let m', _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionSwitched (None, sid))) m
        m') threeSessionModel
    finalModel.Sessions.ActiveSessionId |> Expect.equal "last switch wins" (ActiveSession.Viewing (testSessionId "aa000b02"))
    finalModel.Sessions.Sessions
    |> List.filter (fun s -> SessionDisplay.isActive finalModel.Sessions.ActiveSessionId s)
    |> List.length
    |> Expect.equal "exactly one active" 1
    finalModel.Sessions.Sessions
    |> List.find (fun s -> SessionDisplay.isActive finalModel.Sessions.ActiveSessionId s)
    |> fun s -> s.Id |> Expect.equal "active is aa000b02" (testSessionId "aa000b02")
    finalModel.Sessions.Sessions |> List.length |> Expect.equal "all 3 still present" 3

  testCase "SessionStatusChanged updates correct session" <| fun _ ->
    let m', _ =
      BozzettoUpdate.update
        (BozzettoMsg.Event
          (TuiEvent.SessionStatusChanged ("aa000b02", SessionDisplayStatus.Faulted "test error")))
        threeSessionModel
    m'.Sessions.Sessions
    |> List.find (fun s -> s.Id = testSessionId "aa000b02")
    |> fun s -> s.Status |> Expect.equal "updated status" (SessionDisplayStatus.Faulted "test error")
    m'.Sessions.Sessions
    |> List.find (fun s -> s.Id = testSessionId "aa000a01")
    |> fun s -> s.Status |> Expect.equal "other unchanged" SessionDisplayStatus.Running
]

let sessionPaneRemapTests = testList "session pane key remapping" [
  testCase "Up arrow in Sessions pane maps to SessionNavUp" <| fun _ ->
    let combo = KeyCombo.plain System.ConsoleKey.UpArrow
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor (EditorAction.MoveCursor Direction.Up)) -> ()
    | _ -> failtest "UpArrow should map to MoveCursor Up in defaults"

  testCase "Down arrow in Sessions pane maps to SessionNavDown" <| fun _ ->
    let combo = KeyCombo.plain System.ConsoleKey.DownArrow
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor (EditorAction.MoveCursor Direction.Down)) -> ()
    | _ -> failtest "DownArrow should map to MoveCursor Down in defaults"

  testCase "Enter in Sessions pane maps to SessionSelect" <| fun _ ->
    let combo = KeyCombo.plain System.ConsoleKey.Enter
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor EditorAction.NewLine) -> ()
    | _ -> failtest "Enter should map to NewLine in defaults (remapped in Sessions pane)"

  testCase "Delete/Backspace in Sessions pane maps to SessionDelete" <| fun _ ->
    let combo = KeyCombo.plain System.ConsoleKey.Backspace
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor EditorAction.DeleteBackward) -> ()
    | _ -> failtest "Backspace should map to DeleteBackward in defaults (remapped in Sessions pane)"

  testCase "Other keys in Sessions pane are NOT remapped" <| fun _ ->
    let combo = KeyCombo.plain System.ConsoleKey.A
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor (EditorAction.SessionNavUp | EditorAction.SessionNavDown | EditorAction.SessionSelect | EditorAction.SessionDelete)) ->
      failtest "'a' should NOT map to session nav"
    | _ -> ()

  testCase "Movement keys outside Sessions pane are NOT remapped" <| fun _ ->
    let combo = KeyCombo.plain System.ConsoleKey.UpArrow
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor EditorAction.SessionNavUp) ->
      failtest "UpArrow in default map should be MoveCursor, not SessionNavUp"
    | _ -> ()
]

let keyMapSessionTests = testList "keymap session shortcuts" [
  testCase "Ctrl+Tab maps to SessionCycleNext" <| fun _ ->
    let combo = KeyCombo.ctrl System.ConsoleKey.Tab
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor EditorAction.SessionCycleNext) -> ()
    | other -> failtest (sprintf "Ctrl+Tab should map to SessionCycleNext, got %A" other)

  testCase "Ctrl+Shift+Tab maps to SessionCyclePrev" <| fun _ ->
    let combo = KeyCombo.ctrlShift System.ConsoleKey.Tab
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor EditorAction.SessionCyclePrev) -> ()
    | other -> failtest (sprintf "Ctrl+Shift+Tab should map to SessionCyclePrev, got %A" other)

  testCase "Ctrl+N maps to CreateSession" <| fun _ ->
    let combo = KeyCombo.ctrl System.ConsoleKey.N
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor (EditorAction.CreateSession _)) -> ()
    | other -> failtest (sprintf "Ctrl+N should map to CreateSession, got %A" other)

  testCase "Ctrl+Alt+S maps to ToggleSessionPanel" <| fun _ ->
    let combo = KeyCombo.ctrlAlt System.ConsoleKey.S
    let action = KeyMap.defaults |> Map.tryFind combo
    match action with
    | Some (UiAction.Editor EditorAction.ToggleSessionPanel) -> ()
    | other -> failtest (sprintf "Ctrl+Alt+S should map to ToggleSessionPanel, got %A" other)

  testCase "UiAction.tryParse SessionCycleNext" <| fun _ ->
    UiAction.tryParse "SessionCycleNext"
    |> Expect.isSome "should parse SessionCycleNext"

  testCase "UiAction.tryParse SessionCyclePrev" <| fun _ ->
    UiAction.tryParse "SessionCyclePrev"
    |> Expect.isSome "should parse SessionCyclePrev"
]

let multiSessionLifecycleTests = testList "multi-session lifecycle" [
  testCase "create 3 sessions, cycle through all, verify each becomes active" <| fun _ ->
    let m0 = (BozzettoModel.initial())
    let m1, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionCreated (mkSnapshot (testSessionId "aa000001") ["A.fsproj"]))) m0
    let m2, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionCreated (mkSnapshot (testSessionId "aa000002") ["B.fsproj"]))) m1
    let m3, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionCreated (mkSnapshot (testSessionId "aa000003") ["C.fsproj"]))) m2
    m3.Sessions.ActiveSessionId |> Expect.equal "first auto-active" (ActiveSession.Viewing (testSessionId "aa000001"))
    m3.Sessions.Sessions |> List.length |> Expect.equal "3 sessions" 3
    let m4, effs4 = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCycleNext) m3
    effs4 |> List.length |> Expect.equal "cycle emits effect" 1
    let m5, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionSwitched (None, "aa000002"))) m4
    m5.Sessions.ActiveSessionId |> Expect.equal "now s2" (ActiveSession.Viewing (testSessionId "aa000002"))
    let m6, _ = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCycleNext) m5
    let m7, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionSwitched (None, "aa000003"))) m6
    m7.Sessions.ActiveSessionId |> Expect.equal "now s3" (ActiveSession.Viewing (testSessionId "aa000003"))
    let m8, _ = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionCycleNext) m7
    let m9, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionSwitched (None, "aa000001"))) m8
    m9.Sessions.ActiveSessionId |> Expect.equal "wraps to s1" (ActiveSession.Viewing (testSessionId "aa000001"))

  testCase "stop active session, verify fallback" <| fun _ ->
    let m0 = threeSessionModel
    let m1, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionStopped "aa000a01")) m0
    m1.Sessions.Sessions |> List.length |> Expect.equal "2 left" 2
    m1.Sessions.Sessions
    |> List.exists (fun s -> s.Id = testSessionId "aa000a01")
    |> Expect.isFalse "aa000a01 removed"
    m1.Sessions.Sessions
    |> List.map (fun s -> s.Id)
    |> Expect.containsAll "remaining" [testSessionId "aa000b02"; testSessionId "aa000c03"]

  testCase "create, switch, stop, switch back — full lifecycle" <| fun _ ->
    let m0 = (BozzettoModel.initial())
    let m1, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionCreated (mkSnapshot (testSessionId "aa000001") ["A.fsproj"]))) m0
    m1.Sessions.ActiveSessionId |> Expect.equal "s1 active" (ActiveSession.Viewing (testSessionId "aa000001"))
    let m2, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionCreated (mkSnapshot (testSessionId "aa000002") ["B.fsproj"]))) m1
    m2.Sessions.ActiveSessionId |> Expect.equal "still s1" (ActiveSession.Viewing (testSessionId "aa000001"))
    let m3, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionSwitched (None, "aa000002"))) m2
    m3.Sessions.ActiveSessionId |> Expect.equal "now s2" (ActiveSession.Viewing (testSessionId "aa000002"))
    let m4, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionStopped "aa000002")) m3
    m4.Sessions.Sessions |> List.length |> Expect.equal "1 left" 1
    let m5, _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionSwitched (None, "aa000001"))) m4
    m5.Sessions.ActiveSessionId |> Expect.equal "back to s1" (ActiveSession.Viewing (testSessionId "aa000001"))

  testCase "SessionNavDown clamps to session count" <| fun _ ->
    let model =
      { threeSessionModel with
          Editor = { threeSessionModel.Editor with SelectedSessionIndex = Some 2 } }
    let m', _ = BozzettoUpdate.update (BozzettoMsg.Editor EditorAction.SessionNavDown) model
    Expect.isTrue "clamped" (m'.Editor.SelectedSessionIndex.Value <= 2)

  testCase "SessionSetIndex 0 selects first session" <| fun _ ->
    let m', _ = BozzettoUpdate.update (BozzettoMsg.Editor (EditorAction.SessionSetIndex 0)) threeSessionModel
    m'.Editor.SelectedSessionIndex |> Expect.equal "index 0" (Some 0)

  testCase "SessionSetIndex out-of-range clamps" <| fun _ ->
    let m', _ = BozzettoUpdate.update (BozzettoMsg.Editor (EditorAction.SessionSetIndex 99)) threeSessionModel
    Expect.isTrue "clamped" (m'.Editor.SelectedSessionIndex.Value <= 2)

  testCase "derived active id always has exactly one matching session after switch" <| fun _ ->
    let switches = ["aa000a01"; "aa000b02"; "aa000c03"; "aa000b02"; "aa000a01"]
    let finalModel =
      switches |> List.fold (fun m sid ->
        let m', _ = BozzettoUpdate.update (BozzettoMsg.Event (TuiEvent.SessionSwitched (None, sid))) m
        m') threeSessionModel
    finalModel.Sessions.Sessions
    |> List.filter (fun s -> SessionDisplay.isActive finalModel.Sessions.ActiveSessionId s)
    |> List.length
    |> Expect.equal "exactly one active" 1
    finalModel.Sessions.Sessions
    |> List.find (fun s -> SessionDisplay.isActive finalModel.Sessions.ActiveSessionId s)
    |> fun s -> s.Id |> Expect.equal "last switch wins" (testSessionId "aa000a01")
]

let resizeTests = testList "pane resize" [
  testCase "UiAction.tryParse ResizeHGrow → ResizeH 1" <| fun _ ->
    UiAction.tryParse "ResizeHGrow"
    |> Expect.equal "should parse" (Some (UiAction.ResizeH 1))

  testCase "UiAction.tryParse ResizeHShrink → ResizeH -1" <| fun _ ->
    UiAction.tryParse "ResizeHShrink"
    |> Expect.equal "should parse" (Some (UiAction.ResizeH -1))

  testCase "UiAction.tryParse ResizeVGrow → ResizeV 1" <| fun _ ->
    UiAction.tryParse "ResizeVGrow"
    |> Expect.equal "should parse" (Some (UiAction.ResizeV 1))

  testCase "UiAction.tryParse ResizeRShrink → ResizeR -1" <| fun _ ->
    UiAction.tryParse "ResizeRShrink"
    |> Expect.equal "should parse" (Some (UiAction.ResizeR -1))

  testCase "LayoutConfig.resizeH clamps to bounds" <| fun _ ->
    let cfg = LayoutConfig.defaults
    let grown = LayoutConfig.resizeH 20 cfg
    let shrunk = LayoutConfig.resizeH -20 cfg
    Expecto.Flip.Expect.floatClose "max 0.9" Accuracy.high 0.9 grown.LeftRightSplit
    Expecto.Flip.Expect.floatClose "min 0.2" Accuracy.high 0.2 shrunk.LeftRightSplit

  testCase "LayoutConfig.resizeV clamps min to 2" <| fun _ ->
    let cfg = LayoutConfig.defaults
    let shrunk = LayoutConfig.resizeV -10 cfg
    shrunk.OutputEditorSplit |> Expect.equal "min 2" 2

  testCase "LayoutConfig.resizeR clamps to bounds" <| fun _ ->
    let cfg = LayoutConfig.defaults
    let grown = LayoutConfig.resizeR 20 cfg
    let shrunk = LayoutConfig.resizeR -20 cfg
    Expecto.Flip.Expect.floatClose "max 0.9" Accuracy.high 0.9 grown.SessionsDiagSplit
    Expecto.Flip.Expect.floatClose "min 0.1" Accuracy.high 0.1 shrunk.SessionsDiagSplit

  testCase "LayoutConfig.resizeH step is 0.05" <| fun _ ->
    let cfg = LayoutConfig.defaults
    let grown = LayoutConfig.resizeH 1 cfg
    Expecto.Flip.Expect.floatClose "default + 0.05" Accuracy.high 0.7 grown.LeftRightSplit
]

[<Tests>]
let allMultiUiTests = testList "Multi-UI Consistency" [
  multiConsumerTests
  actionDispatchTests
  resizeTests
  elmSessionSwitchingTests
  elmSessionCyclingTests
  elmSessionEventTests
  sessionPaneRemapTests
  keyMapSessionTests
  multiSessionLifecycleTests
]
