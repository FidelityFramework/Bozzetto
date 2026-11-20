namespace Bozzetto.Features

open Bozzetto.Cohort

/// The port `CohortOwner` (Features/CohortOwner.fs) uses to persist and
/// replay a cohort's ledger (Slice 1, cohort-integration-plan.md D1/D3).
/// `LedgerEntry` is `Cohort.fs`'s own type (Cohort.fs:802) — it is NEVER
/// redefined here, so the owner, the in-memory port, and the SQLite port
/// (Bozzetto/CohortLedger.fs, the daemon layer) all agree on one shape by
/// construction.
module CohortLedger =

  [<RequireQualifiedAccess>]
  type LedgerWriteError =
    | StorageUnavailable of detail: string
    | ConstraintViolation of detail: string
    | WriteFailed of detail: string

  module LedgerWriteError =
    let describe =
      function
      | LedgerWriteError.StorageUnavailable detail -> detail
      | LedgerWriteError.ConstraintViolation detail -> detail
      | LedgerWriteError.WriteFailed detail -> detail

  // A batch is durable as a unit before the owner publishes its new state.
  type LedgerPort<'m> = {
    AppendBatch: LedgerEntry<'m> list -> Result<unit, LedgerWriteError>
    /// Every entry recorded so far, in `Seq` order.
    ReadAll: unit -> LedgerEntry<'m> list
  }

  /// An in-memory ledger for tests and for a daemon with no persistence
  /// configured. Not durable across process restarts — `ReadAll` after the
  /// process exits sees nothing, same as never having recorded anything.
  module InMemory =
    let create<'m> () : LedgerPort<'m> =
      let store = ResizeArray<LedgerEntry<'m>>()
      let sync = obj ()
      {
        AppendBatch = fun entries -> lock sync (fun () -> store.AddRange entries; Ok ())
        ReadAll = fun () -> lock sync (fun () -> List.ofSeq store)
      }
