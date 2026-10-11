/// `HealthAnomaly` is a pure classical anomaly detector (EWMA + CUSUM) over
/// a named signal's observations — see the module doc comment in
/// Bozzetto.Core/Features/HealthAnomaly.fs for the math and why it's shaped
/// this way. This file proves the properties the module is held to, then
/// replays the two real incidents that motivated it: `/health` latency
/// stuck at 60s for a minute, and worker RSS climbing to 37GB over hours.
/// Those two, plus the flat-with-noise control, are the acceptance check.
module Bozzetto.Tests.HealthAnomalyTests

open System
open Expecto
open Expecto.Flip
open FsCheck
open FsCheck.FSharp
open Bozzetto.Features
open Bozzetto.Features.HealthAnomaly

let private epoch = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
let private atSec (n: int) = epoch.AddSeconds(float n)

let private flatNoisy (rng: Random) (n: int) (baseVal: float) (noiseAmp: float) : Observation list =
  [ for i in 0 .. n - 1 -> { At = atSec i; Value = baseVal + (rng.NextDouble() - 0.5) * 2.0 * noiseAmp } ]

/// Reindex a concatenated series so `At` is strictly increasing across the
/// whole thing, not just within each piece that built it.
let private reindexed (series: Observation list) : Observation list =
  series |> List.mapi (fun i o -> { o with At = atSec i })

let private fires =
  function
  | Verdict.Drifting _
  | Verdict.Broken _ -> true
  | Verdict.Normal
  | Verdict.InsufficientHistory -> false

let private isBroken =
  function
  | Verdict.Broken _ -> true
  | _ -> false

let private evaluate (series: Observation list) = evaluateSeries (SignalId.Custom "test") series

// ── Generators for the property tests ──

let private propConfig = { FsCheckConfig.defaultConfig with maxTest = 200 }

/// All randomness is owned by FsCheck, including the seed of observation
/// noise. No independent Gen.sample inside a property or series constructor.
type private Scenario = {
  Seed: int
  Baseline: int
  NoisePercent: int
  Factor: int
  DriftFraction: float
}

let private genScenario factorBounds = gen {
  let! seed = Gen.choose (1, 1_000_000)
  let! baseline = Gen.choose (1, 100_000)
  let! noisePercent = Gen.choose (1, 20)
  let! factor = Gen.choose factorBounds
  let! fraction = Gen.choose (0, Int32.MaxValue - 1)
  return { Seed = seed; Baseline = baseline; NoisePercent = noisePercent
           Factor = factor; DriftFraction = 0.25 + 0.3 * float fraction / float Int32.MaxValue }
}

/// Shrink magnitude/seed/factor towards their valid lower bounds without
/// changing lengths, weakening noise/rate domains or creating zero baselines.
let private shrinkScenario (factorMin, _) s = seq {
  if s.Seed > 1 then yield { s with Seed = max 1 (s.Seed / 2) }
  if s.Baseline > 1 then yield { s with Baseline = max 1 (s.Baseline / 2) }
  if s.NoisePercent > 1 then yield { s with NoisePercent = max 1 (s.NoisePercent / 2) }
  if s.Factor > factorMin then yield { s with Factor = factorMin + (s.Factor - factorMin) / 2 }
  if s.DriftFraction > 0.250001 then yield { s with DriftFraction = 0.25 + (s.DriftFraction - 0.25) / 2.0 }
}

let private scenarios bounds = Arb.fromGenShrink (genScenario bounds, shrinkScenario bounds)

let private shape s = float s.Baseline, float s.Baseline * float s.NoisePercent / 100.0

let private seriesFor kind s =
  let rng = Random s.Seed
  let baseVal, noiseAmp = shape s
  let factor = float s.Factor
  match kind with
  | "flat" -> flatNoisy rng 250 baseVal noiseAmp
  | "step" -> (flatNoisy rng 30 baseVal noiseAmp @ flatNoisy rng 60 (baseVal * factor) noiseAmp) |> reindexed
  | "spike" -> (flatNoisy rng 30 baseVal noiseAmp @ [{ At = atSec 0; Value = baseVal * factor }] @ flatNoisy rng 60 baseVal noiseAmp) |> reindexed
  | "recovery" -> (flatNoisy rng 30 baseVal noiseAmp @ flatNoisy rng 30 (baseVal * factor) (noiseAmp * factor) @ flatNoisy rng 300 baseVal noiseAmp) |> reindexed
  | "drift" ->
    let warmup = flatNoisy rng 20 baseVal noiseAmp
    let ratePerSample = noiseAmp / baseVal * s.DriftFraction
    let drift = [ for i in 0 .. 149 ->
                    { At = atSec 0; Value = baseVal * (1.0 + ratePerSample * float i) + (rng.NextDouble() - 0.5) * 2.0 * noiseAmp } ]
    (warmup @ drift) |> reindexed
  | _ -> invalidArg "kind" kind

let private driftOrdered verdicts =
  let firstDrifting = verdicts |> List.tryFindIndex (function Verdict.Drifting _ -> true | _ -> false)
  match verdicts |> List.tryFindIndex isBroken with
  | None -> true
  | Some brokenAt -> firstDrifting |> Option.exists (fun driftingAt -> driftingAt < brokenAt)

/// Print every round-trip observation plus the evolving owner state, so a
/// failure is reconstructible independently of outer FsCheck run metadata.
let private trace series =
  let _, rows =
    ((initial, []), series |> List.indexed) ||> List.fold (fun (state, rows) (i, obs) ->
      let next, verdict = step defaultParams (SignalId.Custom "test") obs state
      let priorVariance = if state.Count = defaultParams.MinWarmupSamples then state.Variance / float (max 1 (state.Count - 1)) else state.Variance
      let sigma = max (sqrt (max 0.0 priorVariance)) (max 1e-9 (abs state.Mean * defaultParams.MinRelativeStdDev))
      let rawZ = (obs.Value - state.Mean) / sigma
      let row = sprintf "%d value=%s sigma=%s rawZ=%s magnitude=%s breakCount=%d breakSince=%A mean=%s variance=%s pos=%s neg=%s streak=%d verdict=%s"
                  i (obs.Value.ToString("R", Globalization.CultureInfo.InvariantCulture))
                  (if state.Count < defaultParams.MinWarmupSamples then "n/a(warmup)" else sigma.ToString("R", Globalization.CultureInfo.InvariantCulture))
                  (if state.Count < defaultParams.MinWarmupSamples then "n/a(warmup)" else rawZ.ToString("R", Globalization.CultureInfo.InvariantCulture))
                  ((max next.CusumPos -next.CusumNeg).ToString("R", Globalization.CultureInfo.InvariantCulture))
                  next.BreakStreak next.BreakSince
                  (next.Mean.ToString("R", Globalization.CultureInfo.InvariantCulture))
                  (next.Variance.ToString("R", Globalization.CultureInfo.InvariantCulture))
                  (next.CusumPos.ToString("R", Globalization.CultureInfo.InvariantCulture))
                  (next.CusumNeg.ToString("R", Globalization.CultureInfo.InvariantCulture))
                  next.BreachStreak (verdictName verdict)
      next, row :: rows)
  rows |> List.rev |> String.concat "\n"

let private checkScenario kind s predicate =
  let series = seriesFor kind s
  if not (predicate (evaluate series)) then
    failtestf "scenario=%A driftFraction=%s kind=%s epoch=%s cadenceSeconds=1 params=%A\n%s"
      s (s.DriftFraction.ToString("R", Globalization.CultureInfo.InvariantCulture)) kind (epoch.ToString("O")) defaultParams (trace series)
  true

[<Tests>]
let tests =
  testList "HealthAnomaly" [

    testList "cold start" [
      testCase "the first sample is always InsufficientHistory, never Normal" <| fun _ ->
        let verdict = evaluate [ { At = atSec 0; Value = 4.0 } ]
        verdict |> Expect.equal "a single observation can't know what normal is yet" [ Verdict.InsufficientHistory ]

      testCase "a cold daemon does not fire on its first three samples" <| fun _ ->
        let series = [ { At = atSec 0; Value = 4.0 }; { At = atSec 1; Value = 900.0 }; { At = atSec 2; Value = 4.0 } ]
        evaluate series
        |> List.exists fires
        |> Expect.isFalse "warmup must never produce a verdict other than InsufficientHistory"

      testCase "every sample before MinWarmupSamples is InsufficientHistory" <| fun _ ->
        let rng = Random 1
        let series = flatNoisy rng (defaultParams.MinWarmupSamples - 1) 10.0 1.0
        evaluate series
        |> List.forall (function Verdict.InsufficientHistory -> true | _ -> false)
        |> Expect.isTrue "every pre-warmup sample must read as InsufficientHistory"
    ]

    testList "properties" [

      testPropertyWithConfig propConfig "a flat signal with noise never fires" <|
        Prop.forAll (scenarios (1, 1)) (fun s -> checkScenario "flat" s (List.exists fires >> not))

      testPropertyWithConfig propConfig "a sustained step fires within a bounded number of samples" <|
        Prop.forAll (scenarios (3, 15)) (fun s ->
          checkScenario "step" s (List.indexed >> List.exists (fun (i, v) -> i >= 30 && i < 55 && fires v)))

      testPropertyWithConfig propConfig "a slow drift is detected as drift before it's ever detected as a break" <|
        Prop.forAll (scenarios (1, 1)) (fun s -> checkScenario "drift" s driftOrdered)

      // Retained ordering rationale: slow is relative to this signal's noise.
      // CUSUM can eventually reach Broken; it must have read Drifting first.

      testPropertyWithConfig propConfig "a single spike doesn't fire" <|
        Prop.forAll (scenarios (20, 500)) (fun s -> checkScenario "spike" s (List.exists fires >> not))

      testPropertyWithConfig propConfig "recovery clears it" <|
        Prop.forAll (scenarios (3, 50)) (fun s ->
          checkScenario "recovery" s (fun verdicts ->
            verdicts |> List.skip (verdicts.Length - 30) |> List.forall (function Verdict.Normal -> true | _ -> false)))
    ]

    testList "scenario harness" [
      test "generated scenario replay reconstructs the same observations" {
        for kind, bounds in ["flat", (1, 1); "step", (3, 15); "drift", (1, 1); "spike", (20, 500); "recovery", (3, 50)] do
          let generator = genScenario bounds
          let sample () = (Gen.sampleWithSeed (Rnd(13095813027196974459UL, 8198700296333566383UL)) 97 1 generator).[0]
          let first = sample ()
          let replay = sample ()
          replay |> Expect.equal "the same FsCheck generator seed/size replays the case, not just its verdicts" first
          let expected = seriesFor kind first
          let actual = seriesFor kind replay
          if actual <> expected then
            failtestf "series construction generated outside the replayed scenario=%A kind=%s driftFraction=%s\nEXPECTED\n%s\nACTUAL\n%s"
              first kind (first.DriftFraction.ToString("R", Globalization.CultureInfo.InvariantCulture)) (trace expected) (trace actual)
      }
      test "all scenario generators and shrinks stay in their original valid domains" {
        for kind, bounds, length in ["flat", (1, 1), 250; "step", (3, 15), 90; "drift", (1, 1), 170; "spike", (20, 500), 91; "recovery", (3, 50), 360] do
          let generated = Gen.sampleWithSeed (Rnd(123UL, 457UL)) 100 200 (genScenario bounds)
          for original in generated do
            for s in Seq.append [original] (shrinkScenario bounds original) do
              let lo, hi = bounds
              let valid = s.Seed >= 1 && s.Seed <= 1_000_000 && s.Baseline >= 1 && s.Baseline <= 100_000 && s.NoisePercent >= 1 && s.NoisePercent <= 20 && s.Factor >= lo && s.Factor <= hi && s.DriftFraction >= 0.25 && s.DriftFraction < 0.55
              valid |> Expect.isTrue (sprintf "generated/shrunk case stays valid: %s %A" kind s)
              seriesFor kind s |> List.length |> Expect.equal "shrinking does not reduce the observation horizon" length
      }
    ]

    testList "ordering regression" [
      test "seed 6891 slow drift warns before genuinely sustained Broken" {
        // Newly executed diagnosis, NOT a replay of the unknown case from
        // the old independent Gen.sample harness. Rate is strictly within
        // the original [0.25, 0.55) domain, not the diagnostic's upper endpoint.
        let s = { Seed = 6891; Baseline = 100; NoisePercent = 20; Factor = 1; DriftFraction = 0.5499 }
        checkScenario "drift" s driftOrdered |> ignore
        let series = seriesFor "drift" s
        let states = series |> List.scan (fun (state, _) obs -> step defaultParams (SignalId.Custom "test") obs state) (initial, Verdict.InsufficientHistory) |> List.tail
        states.[28] |> snd |> verdictName |> Expect.equal "original first settled breach now warns" "drifting"
        match states |> List.tryFind (snd >> isBroken) with
        | Some (state, Verdict.Broken e) ->
          state.BreakStreak |> Expect.equal "first Broken requires four actual break-level samples" 4
          e.SamplesSustained |> Expect.equal "evidence does not borrow drift-only samples" 4
          e.SustainedFor |> Expect.equal "four consecutive 1Hz break samples span three seconds" (TimeSpan.FromSeconds 3.0)
          printfn "seed6891 restored trace\n%s" (trace series)
        | other -> failtestf "ordering must not pass by never reaching Broken: %A\n%s" other (trace series)
      }
      test "same seeded noise without the ramp is a Normal control" {
        let s = { Seed = 6891; Baseline = 100; NoisePercent = 20; Factor = 1; DriftFraction = 0.5499 }
        checkScenario "flat" s (List.exists fires >> not) |> ignore
      }
      test "a genuine abrupt step fires within the original bound and reaches Broken" {
        let s = { Seed = 6891; Baseline = 100; NoisePercent = 20; Factor = 15; DriftFraction = 0.5499 }
        checkScenario "step" s (fun vs ->
          vs |> List.skip 30 |> List.truncate 25 |> List.exists fires
          && vs |> List.skip 30 |> List.truncate 25 |> List.exists isBroken) |> ignore
      }
    ]

    testList "independent break sustain" [
      // Frozen baseline makes controlled residuals exact; seeded drift
      // evidence deliberately predates break evidence, which cannot borrow it.
      let p = { defaultParams with Alpha = 0.0 }
      let seed = { initial with Count = 21; Mean = 100.0; Variance = 1.0; CusumPos = 6.0; BreachStreak = 4; BreachSince = Some (atSec 0) }
      let advance at z state = step p (SignalId.Custom "sustain") { At = atSec at; Value = state.Mean + z } state

      test "fewer than four break-level samples cannot Broken despite sustained drift" {
        let s0, v0 = advance 10 4.0 seed
        v0 |> verdictName |> Expect.equal "magnitude nine is sustained drift, not break" "drifting"
        s0.BreakStreak |> Expect.equal "drift-level samples never count as break evidence" 0
        let mutable state = s0
        for i in 1 .. 3 do
          let next, verdict = advance (10 + i) (if i = 1 then 4.0 else 1.0) state
          verdict |> verdictName |> Expect.equal "break magnitude alone is insufficient" "drifting"
          next.BreakStreak |> Expect.equal "only actual consecutive break samples counted" i
          state <- next
        let next, verdict = advance 14 1.0 state
        match verdict with
        | Verdict.Broken e ->
          next.BreakStreak |> Expect.equal "four break samples finally settle Broken" 4
          e.SamplesSustained |> Expect.equal "not the older drift count" 4
          next.BreakSince |> Expect.equal "break onset excludes drift-only sample" (Some (atSec 11))
          e.SustainedFor |> Expect.equal "elapsed from actual break onset" (TimeSpan.FromSeconds 3.0)
          e.ObservedAt |> Expect.equal "one current verdict, current observation" (atSec 14)
        | other -> failtestf "sustained break did not settle: %A" other
      }
      test "interrupted break resets even while drift remains sustained" {
        let a, _ = advance 10 4.0 seed
        let b, _ = advance 11 4.0 a
        let interrupted, v = advance 12 -4.0 b
        v |> verdictName |> Expect.equal "magnitude seven still sustains drift" "drifting"
        (interrupted.BreakStreak, interrupted.BreakSince) |> Expect.equal "below break resets count and onset, not hold" (0, None)
        let restarted, v1 = advance 13 4.0 interrupted
        v1 |> isBroken |> Expect.isFalse "old break sample cannot be borrowed"
        (restarted.BreakStreak, restarted.BreakSince) |> Expect.equal "new break episode has new onset" (1, Some (atSec 13))
        let settled = [14; 15; 16] |> List.fold (fun (st, _) at -> advance at 1.0 st) (restarted, v1)
        match snd settled with
        | Verdict.Broken e ->
          e.SamplesSustained |> Expect.equal "four new break observations only" 4
          e.SustainedFor |> Expect.equal "restarted break span" (TimeSpan.FromSeconds 3.0)
        | other -> failtestf "new sustained break never fired: %A" other
      }
      test "recovery clears both severities and a new episode cannot inherit evidence" {
        let a, _ = advance 10 4.0 seed
        let b, _ = advance 11 4.0 a
        let settled, broken = [12; 13; 14] |> List.fold (fun (st, _) at -> advance at 1.0 st) (b, Verdict.Normal)
        broken |> isBroken |> Expect.isTrue "recovery starts from a genuinely sustained Broken"
        // Quiet baseline observations decay BOTH directional accumulators.
        // Opposite clipped residuals would build a new negative CUSUM, not recovery.
        let cleared, v = [15 .. 24] |> List.fold (fun (st, _) at -> advance at 0.0 st) (settled, broken)
        v |> Expect.equal "magnitude below clear returns Normal" Verdict.Normal
        (cleared.CusumPos, cleared.CusumNeg, cleared.BreachStreak, cleared.BreachSince, cleared.BreakStreak, cleared.BreakSince)
        |> Expect.equal "full recovery removes old evidence" (0.0, 0.0, 0, None, 0, None)
        let newState, _ = [25; 26; 27; 28] |> List.fold (fun (st, _) at -> advance at 4.0 st) (cleared, v)
        (newState.BreakStreak, newState.BreakSince) |> Expect.equal "next break episode starts with one honest sample" (1, Some (atSec 28))
      }
      test "warmup never carries break evidence and first post-warmup starts honestly" {
        let dirty = { initial with BreakStreak = 3; BreakSince = Some epoch }
        let warmed, verdict = step defaultParams (SignalId.Custom "warmup") { At = epoch; Value = 100.0 } dirty
        verdict |> Expect.equal "warmup remains InsufficientHistory" Verdict.InsufficientHistory
        (warmed.BreakStreak, warmed.BreakSince) |> Expect.equal "warmup clears break evidence" (0, None)
        let first, _ = advance 20 4.0 { seed with Count = 20; Variance = 19.0; CusumPos = 0.0; BreachStreak = 0; BreachSince = None }
        (first.BreakStreak, first.BreakSince) |> Expect.equal "first learned sample at magnitude three is not break" (0, None)
      }
      test "drift hysteresis and original alarm onset are unchanged" {
        let held, v = advance 10 1.0 { seed with CusumPos = 4.0 }
        (held.BreachStreak, held.BreachSince) |> Expect.equal "dead zone holds drift evidence" (4, Some epoch)
        v |> verdictName |> Expect.equal "held settled drift still warns" "drifting"
        let states = [1 .. 8] |> List.scan (fun (st, _) at -> advance at 4.0 st) ({ seed with CusumPos = 0.0; BreachStreak = 0; BreachSince = None }, Verdict.Normal) |> List.tail
        states |> List.tryFindIndex (snd >> fires) |> Expect.equal "drift alarm onset stays at fifth clipped sample" (Some 4)
        states |> List.tryFindIndex (snd >> isBroken) |> Expect.equal "break independently settles two samples later" (Some 6)
      }
      test "custom single-sample sustain can directly Broken without transition ceremony" {
        let custom = { p with MinSustainSamples = 1; DriftThreshold = 1.0; BreakThreshold = 2.0 }
        let _, verdict = step custom (SignalId.Custom "direct") { At = atSec 1; Value = 104.0 } { seed with CusumPos = 0.0; BreachStreak = 0; BreachSince = None }
        match verdict with
        | Verdict.Broken e ->
          e.SamplesSustained |> Expect.equal "one real break sample is sufficient for this custom contract" 1
          e.SustainedFor |> Expect.equal "no artificial prior warning required" TimeSpan.Zero
        | other -> failtestf "custom Params must not acquire a first-Drifting latch: %A" other
      }
      test "default threshold gap exceeds bounded per-sample rise" {
        (defaultParams.BreakThreshold - defaultParams.DriftThreshold, defaultParams.ClipSigmas - defaultParams.Slack)
        |> Expect.equal "ordering proof uses gap five versus rise three, not a Params refusal" (5.0, 3.0)
      }
      testPropertyWithConfig propConfig "default ordering holds on arbitrary finite input observations" <|
        let finite = gen {
          let! exponent = Gen.choose (-1074, 1023)
          let! mantissa = Gen.choose (-Int32.MaxValue, Int32.MaxValue)
          return Math.ScaleB(float mantissa / float Int32.MaxValue, exponent)
        }
        let inputs = Arb.fromGenShrink (Gen.listOfLength 250 finite, fun xs -> seq { if not (List.isEmpty xs) then yield List.truncate (xs.Length / 2) xs })
        Prop.forAll inputs (fun values ->
          let series = values |> List.mapi (fun i value -> { At = atSec i; Value = value })
          if not (evaluate series |> driftOrdered) then failtestf "default finite-input ordering violated\n%s" (trace series)
          true)
    ]

    // ── Acceptance check: replay the two real incidents, plus the control. ──
    testList "replayed incidents (the acceptance check)" [

      testCase "INCIDENT 1: /health latency flat at a few ms, jumps to 60s for a minute, returns — fires and recovers" <| fun _ ->
        let rng = Random 20260922
        // A minute of samples at 1Hz either side, a minute of the incident itself.
        let normal = flatNoisy rng 60 4.0 1.2
        let stuck = flatNoisy rng 60 60_000.0 2_000.0
        let recovered = flatNoisy rng 250 4.0 1.2
        let series = (normal @ stuck @ recovered) |> reindexed
        let verdicts = evaluate series

        let duringIncident = verdicts |> List.indexed |> List.filter (fun (i, _) -> i >= 60 && i < 120)
        duringIncident
        |> List.exists (fun (_, v) -> isBroken v)
        |> Expect.isTrue "the daemon's own health endpoint going from ~4ms to 60,000ms for a minute must read as Broken"

        verdicts
        |> List.skip (verdicts.Length - 30)
        |> List.forall (function Verdict.Normal -> true | _ -> false)
        |> Expect.isTrue "once latency has been back to ~4ms for a while, the verdict must clear back to Normal"

      testCase "INCIDENT 2: worker RSS climbs steadily from 30MB to 37,000MB over hours — fires and never falsely clears mid-climb" <| fun _ ->
        let rng = Random 37000
        let baseline = flatNoisy rng 60 30.0 1.0
        let climb =
          [ for i in 0 .. 300 ->
              { At = atSec 0
                Value = 30.0 + float i * (37_000.0 - 30.0) / 300.0 + (rng.NextDouble() - 0.5) * 2.0 } ]
        let series = (baseline @ climb) |> reindexed
        let verdicts = evaluate series

        verdicts
        |> List.skip 90 // comfortably past MinSustainSamples once the climb starts at index 60
        |> List.exists fires
        |> Expect.isTrue "a steady climb to 37GB must eventually read as Drifting or Broken"

        verdicts
        |> List.skip (verdicts.Length - 30)
        |> List.forall fires
        |> Expect.isTrue "an ongoing, never-stabilizing climb must still be flagged at the end — it never got the chance to become a new stable normal"

      testCase "CONTROL: flat with noise never fires" <| fun _ ->
        let rng = Random 4
        let series = flatNoisy rng 300 4.0 1.2
        evaluate series
        |> List.exists fires
        |> Expect.isFalse "a daemon behaving exactly as usual must never be flagged"
    ]

    testList "evidence and messages" [
      testCase "Broken evidence names the right direction and carries the raw deviation" <| fun _ ->
        let rng = Random 5
        let before = flatNoisy rng 30 4.0 1.0
        let after = flatNoisy rng 30 900.0 20.0
        let series = (before @ after) |> reindexed
        let evidence =
          evaluate series
          |> List.tryPick (function Verdict.Broken e -> Some e | _ -> None)
        match evidence with
        | None -> failtest "expected the step to 900 to read as Broken somewhere in the series"
        | Some e ->
          e.Direction |> Expect.equal "900 is well above the ~4 baseline" SignalDirection.Increased
          (e.ObservedValue, 100.0) |> Expect.isGreaterThan "the evidence carries the actual out-of-range value"
          (e.SamplesSustained, defaultParams.MinSustainSamples) |> Expect.isGreaterThanOrEqual "sustain count matches the streak gate"

      testCase "describe returns None for Normal and InsufficientHistory, Some for Drifting/Broken" <| fun _ ->
        describe Verdict.Normal |> Expect.isNone "Normal has no evidence to describe"
        describe Verdict.InsufficientHistory |> Expect.isNone "InsufficientHistory has no evidence to describe"

      testCase "signalName is exhaustive text for every known signal, plus Custom" <| fun _ ->
        [ SignalId.HealthLatency; SignalId.EvalLatency; SignalId.WorkerRss; SignalId.MailboxQueueDepth; SignalId.WarmupDuration ]
        |> List.map signalName
        |> Expect.equal
          "each known signal has a stable name"
          [ "health_latency"; "eval_latency"; "worker_rss"; "mailbox_queue_depth"; "warmup_duration" ]
        signalName (SignalId.Custom "my_custom_signal") |> Expect.equal "Custom passes the name through" "my_custom_signal"
    ]
  ]
