namespace Bozzetto.Diagnostics

open Fidelity.PSG

type CaptureMetadata = {
  Demand: string
  SourceVersion: string
  ExpectedScopes: ScopeContentStamp list
}

/// A complete source-selected occurrence account, not an executable Revision.
/// Declaration headers and explicitly omitted children are typed handles, not
/// requests to recover retained or inactive bodies.
type OccurrenceCapture = {
  Metadata: CaptureMetadata
  Delivery: OccurrenceDelivery
  ContextHeaders: SourceContextHeader list
  ContextRoots: NodeId list
}

type CaptureReceipt = {
  Digest: string
  PayloadBytes: int64
  Stage: string
}

module Capture =
  [<Literal>]
  let Stage = "occurrence-structure-v1"

  let private nonempty (value: string) = not (isNull value) && value.Trim().Length > 0

  type private Errors() =
    let values = ResizeArray<string>()
    let mutable characters = 0
    let mutable truncated = false
    member _.Add(message: string) =
      let remaining = 8000 - characters
      if values.Count >= 64 || remaining <= 0 then truncated <- true
      else
        let mutable length = min message.Length remaining
        if length < message.Length then
          truncated <- true
          if length > 0 && message[length - 1] >= '\uD800' && message[length - 1] <= '\uDBFF' then
            length <- length - 1
        values.Add(message.Substring(0, length))
        characters <- characters + length + 1
    member _.Count = values.Count
    member _.Render() =
      String.concat "\n" values + (if truncated then "\n[diagnostic validation errors truncated]" else "")

  /// This establishes representation, occurrence and reference accounts only.
  /// Support truth, complete semantic table integrity, proof and current-source
  /// eligibility are deliberately outside this diagnostic contract.
  let validate (capture: OccurrenceCapture) : Result<unit, string> =
    let errors = Errors()
    let refuse condition message = if condition then errors.Add message
    let delivery = capture.Delivery
    let transaction = delivery.Transaction
    refuse (not (nonempty capture.Metadata.Demand)) "Capture demand identity is empty."
    refuse (capture.Metadata.Demand <> transaction.TargetCursor.Subscription)
      "Capture demand identity differs from its source delivery subscription."
    refuse (not (nonempty capture.Metadata.SourceVersion)) "Capture source version is empty."
    refuse capture.Metadata.ExpectedScopes.IsEmpty "Capture must name its demanded scope stamps."
    refuse (Set.ofList capture.Metadata.ExpectedScopes |> Set.count <> capture.Metadata.ExpectedScopes.Length)
      "Capture scope stamps contain duplicates."
    refuse (transaction.BaseCursor.Ordinal <> 0UL || transaction.TargetCursor.Ordinal <> 1UL)
      "Standalone capture requires an empty subscription base and its first delivery."
    refuse (transaction.BaseRevision <> transaction.TargetRevision)
      "Standalone capture must bind its empty base to the captured checked revision."
    refuse (transaction.Changes |> List.exists (function ScopeChange.Add _ -> false | _ -> true))
      "Standalone capture accepts only newly declared scopes, never a delta or retirement."
    refuse (not transaction.Withdrawals.IsEmpty) "Standalone capture cannot withdraw an unstored scope."
    let actualScopes = delivery.Sections |> List.map (fun section -> section.Descriptor.Content)
    refuse (Set.ofList actualScopes <> Set.ofList capture.Metadata.ExpectedScopes)
      "Captured scope stamps do not match the explicitly demanded scope account."
    match OccurrenceDelivery.check delivery with
    | [] -> ()
    | failures -> errors.Add (sprintf "Invalid occurrence delivery (first findings): %A" (List.truncate 4 failures))
    match ScopedPublication.start transaction.BaseCursor.Subscription transaction.BaseRevision with
    | Error failures -> errors.Add (sprintf "Invalid empty capture base (first findings): %A" (List.truncate 4 failures))
    | Ok empty ->
      match ScopedPublication.apply transaction empty with
      | Error failures -> errors.Add (sprintf "Invalid standalone source transaction (first findings): %A" (List.truncate 4 failures))
      | Ok _ -> ()
    let occurrences = delivery.Sections |> List.collect (fun section -> Map.toList section.Occurrences)
    let bodyIds = occurrences |> List.map fst |> Set.ofList
    let headerIds = capture.ContextHeaders |> List.map _.Identity |> Set.ofList
    let known = Set.union bodyIds headerIds
    refuse (bodyIds.Count <> occurrences.Length) "A body is repeated across captured scopes."
    refuse (headerIds.Count <> capture.ContextHeaders.Length) "Context header identities are repeated."
    refuse (Set.intersect bodyIds headerIds |> Set.isEmpty |> not)
      "A context header cannot replace or duplicate a captured live body."
    let roots = Set.ofList capture.ContextRoots
    refuse (roots.Count <> capture.ContextRoots.Length) "Context root identities are repeated."
    refuse (not (Set.isSubset roots known)) "A declared context root has no captured body or header."
    let headers = capture.ContextHeaders |> List.map (fun header -> header.Identity, header) |> Map.ofList
    let bodies = Map.ofList occurrences
    let mutable portStamps : Map<NodeId * OccurrencePort, int * string> = Map.empty
    let omitted =
      occurrences |> List.collect (fun (_, occurrence) ->
        occurrence.Children |> List.choose (fun child ->
          match child.Traversal with ChildTraversal.SourceOmitted(_, node) -> Some node | _ -> None))
      |> Set.ofList
    let allDeclared = Set.union known omitted
    for section in delivery.Sections do
      refuse (not section.Descriptor.RequiredBoundaries.IsEmpty)
        "Occurrence capture cannot claim imported boundary facts are self-contained."
      for support in section.Descriptor.RequiredSupports do
        match support with
        | SupportKey.Node node when not (allDeclared.Contains node) ->
          errors.Add "A node support has neither a captured body nor an explicit source handle."
        | _ -> ()
      for KeyValue(identity, occurrence) in section.Occurrences do
        let localOmissions =
          occurrence.Children |> List.choose (fun child ->
            match child.Traversal with
            | ChildTraversal.SourceOmitted(_, node) -> Some node
            | ChildTraversal.EnterImported _ ->
              errors.Add "Imported occurrence traversal requires an unavailable boundary payload."
              None
            | ChildTraversal.EnterLocal _ -> None)
          |> Set.ofList
        let declared = Set.union known localOmissions
        for reference in IntegrityNamed.nodeReferences occurrence.Body do
          refuse (not (declared.Contains reference))
            (sprintf "Body %A refers to undeclared node %A." identity reference)
        for path in occurrence.OccurrenceContexts do
          let mutable child = identity
          let mutable seen = Set.singleton identity
          for frame in path do
            let key = frame.Parent, frame.Port
            let account = frame.Extent, frame.Stamp
            match portStamps.TryFind key with
            | None -> portStamps <- portStamps.Add(key, account)
            | Some expected ->
              refuse (account <> expected) "Source occurrence port stamps or extents disagree within the capture."
            refuse (seen.Contains frame.Parent) "An occurrence context contains a repeated ancestor."
            seen <- seen.Add frame.Parent
            match frame.Port, bodies.TryFind frame.Parent, headers.TryFind frame.Parent with
            | OccurrencePort.StructuralChild, Some parent, _ ->
              refuse (parent.Body.Children.Length <> frame.Extent
                || List.tryItem frame.Ordinal parent.Body.Children <> Some child)
                "An occurrence context disagrees with its live parent position."
              match List.tryItem frame.Ordinal parent.Children with
              | Some { Traversal = ChildTraversal.EnterLocal node } when node = child -> ()
              | _ -> errors.Add "An occurrence context does not select a local child."
            | OccurrencePort.ModuleDeclaration, _, Some header ->
              match header.Ports.TryFind frame.Port with
              | Some port when port.Extent = frame.Extent && port.Stamp = frame.Stamp
                               && port.Positions.TryFind frame.Ordinal = Some child -> ()
              | _ -> errors.Add "An occurrence context disagrees with its declaration header port."
            | _ -> errors.Add "An occurrence context has no appropriate parent body or declaration header."
            child <- frame.Parent
          refuse (not (roots.Contains child)) "An occurrence path does not end at an explicitly declared source root."
    for header in capture.ContextHeaders do
      refuse (not (nonempty header.Name)) "A context header has no source name."
      for KeyValue(portKind, port) in header.Ports do
        refuse (portKind <> OccurrencePort.ModuleDeclaration) "Declaration headers cannot supply structural parent bodies."
        refuse (port.Extent < 0 || not (nonempty port.Stamp)) "A declaration port has an invalid extent or stamp."
        for KeyValue(ordinal, _) in port.Positions do
          refuse (ordinal < 0 || ordinal >= port.Extent) "A declaration port position is outside its original extent."
      for reference in IntegrityNamed.contextHeaderReferences header do
        refuse (not (known.Contains reference)) "A sparse declaration port refers to an uncaptured position."
    if errors.Count = 0 then Ok ()
    else Error (errors.Render())
