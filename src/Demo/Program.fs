open Aardvark.Base
open System.Runtime.InteropServices
open FSharp.Data.Adaptive
open Aardvark.Assembler

#nowarn "9"

type IntDelegate = delegate of unit -> int


// type MyFun = delegate of uint32 * uint32 * uint32 * uint32 * uint32 * uint32 * uint32 * uint32 -> unit

type MyFun = delegate of nativeint * uint64 * uint64 * uint64 * uint64 * uint64 * uint64 * uint64 * uint32 * uint64 * float -> unit
type IntDel = delegate of int -> unit

let thing = MyFun (fun a b c d e f g h i j k -> printfn "yeah: %A" (a,b,c,d,e,f,g,h,i,j,k))

let print =
    IntDel (fun a -> 
        Log.line "%d" a
    )


[<EntryPoint>]
let main _ =
    Aardvark.Init()

    let printInt = Marshal.GetFunctionPointerForDelegate print

    let compile (prev : option<int>) (value : int) (s : IAssemblerStream) =
        let value =
            match prev with
            | Some b -> b * 1000 + value
            | None -> value
        s.BeginCall 1
        s.PushArg value
        s.Call printInt


    let set = cset [1;2]

    let project (value : int) =
        [value % 2 :> obj; value :> obj]

    let prog = new AdaptiveFragmentProgram<int>(set, project, compile)

    // let prog = new FragmentProgram<int>(compile)


    // let a = prog.Prepend(1)
    // let b = prog.InsertAfter(a, 2)

    Log.start "run"
    prog.Run()
    Log.stop()
    
    transact (fun () -> set.Remove 1 |> ignore)


    Log.start "run"
    prog.Run()
    Log.stop()

    transact (fun () -> set.Add 3 |> ignore)

    Log.start "run"
    prog.Run()
    Log.stop()

    
    transact (fun () -> set.UnionWith [4;5;6] |> ignore)

    Log.start "run"
    prog.Run()
    Log.stop()


    Log.line "done"

    0
