namespace SageFs

open System
open System.Threading.Tasks
open SageFs.Utils

/// Resilient wrappers for MailboxProcessor loops and fire-and-forget tasks.
///
/// Actor loops (MailboxProcessor) can silently die if an unhandled exception
/// escapes the message-processing function. ResilientActor.wrapLoop catches
/// non-cancellation exceptions, logs them, increments an OTel counter, and
/// returns the previous state so the loop continues.
///
/// Fire-and-forget tasks (Task.Run |> ignore) can lose exceptions to the
/// unobserved task exception handler. SafeFireAndForget.startTask wraps
/// the work with structured logging and metrics.
[<RequireQualifiedAccess>]
module ResilientActor =

  /// Wraps a MailboxProcessor loop body so that exceptions in message
  /// processing are caught, logged, and the loop continues with previous state.
  /// OperationCanceledException propagates (actor should stop on cancellation).
  let inline wrapLoop
    (logger: ILogger)
    (actorName: string)
    (processMessage: 'State -> 'Msg -> Async<'State>)
    : 'State -> 'Msg -> Async<'State> =
    fun state msg ->
      async {
        try
          return! processMessage state msg
        with
        | :? OperationCanceledException -> return! raise (OperationCanceledException())
        | ex ->
          logger.LogError(
            sprintf "[%s] Unhandled exception processing message, continuing with previous state: %s"
              actorName ex.Message)
          Instrumentation.actorErrors.Add(
            1L,
            Collections.Generic.KeyValuePair("actor.name", actorName :> obj))
          return state
      }

[<RequireQualifiedAccess>]
module SafeFireAndForget =

  /// Wraps a fire-and-forget task so that unhandled exceptions are logged and
  /// metricked, not lost to TaskScheduler.UnobservedTaskException.
  /// OperationCanceledException is silently absorbed (expected during shutdown).
  let startTask (logger: ILogger) (name: string) (work: unit -> Task) =
    Task.Run(fun () ->
      task {
        try
          do! work ()
        with
        | :? OperationCanceledException -> ()
        | ex ->
          logger.LogWarning(
            sprintf "[%s] Fire-and-forget task failed: %s" name ex.Message)
          Instrumentation.fireAndForgetErrors.Add(
            1L,
            Collections.Generic.KeyValuePair("task.name", name :> obj))
      } :> Task
    ) |> ignore
