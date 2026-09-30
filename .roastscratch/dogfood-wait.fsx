#load "/home/will/Work/Bozzetto/Bozzetto/WorkerProxyWait.fs"
open Bozzetto
let d = WorkerProxyWait.delaysByDefault
printfn "attempts=%d totalMs=%d" d.Length (List.sum d)
printfn "delays=%A" d
