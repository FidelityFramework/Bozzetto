namespace Bozzetto

open System

/// Pure completion data for injected test-result producers. This module owns
/// no network client or F# worker transport.
module WorkerStream =
  [<RequireQualifiedAccess>]
  type StreamOutcome =
    | Completed
    | TimedOut of after: TimeSpan
    | Cancelled

  let noResultReason (outcome: StreamOutcome) : Features.LiveTesting.NoResultReason =
    match outcome with
    | StreamOutcome.Completed -> Features.LiveTesting.NoResultReason.StreamEnded
    | StreamOutcome.TimedOut after -> Features.LiveTesting.NoResultReason.StreamStalled after
    | StreamOutcome.Cancelled -> Features.LiveTesting.NoResultReason.RunCancelled
