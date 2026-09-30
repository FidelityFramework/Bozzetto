#load "/home/will/Work/Bozzetto/Bozzetto/WorkerProxyWait.fs"
open Bozzetto
let d = WorkerProxyWait.delaysByDefault
printfn "delays = %A" d
printfn "sum    = %d" (List.sum d)
// find the first pair that does not grow
let bad =
  d |> List.pairwise |> List.tryFind (fun (a,b) -> a >= b)
printfn "first non-growing pair: %A" bad
