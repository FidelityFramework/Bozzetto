module TestWorkspace.SampleTests

open Expecto

let add x y = x + y

[<Tests>]
let sampleTests =
  testList "Sample" [
    testCase "add 1 2 equals 3" (fun () ->
      Expect.equal (add 1 2) 3 "1+2=3")
    testCase "add 0 0 equals 0" (fun () ->
      Expect.equal (add 0 0) 0 "0+0=0")
    testCase "add negative" (fun () ->
      Expect.equal (add (-1) 1) 0 "-1+1=0")
  ]
