module TestWorkspace.Simple

let add x y = x + y

let ballSpeed = 3.0

let greet name = sprintf "Hello, %s!" name

let fibonacci n =
  let rec loop a b count =
    if count <= 0 then a
    else loop b (a + b) (count - 1)
  loop 0 1 n
