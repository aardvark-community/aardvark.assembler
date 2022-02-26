open System
open Aardvark.Base
open System.Runtime.InteropServices
open System.Runtime.CompilerServices
open Microsoft.FSharp.NativeInterop

#nowarn "9"
open Aardvark.Base.Runtime
open Aardvark.Assembler
open System.Collections.Generic

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

    let prog = new FragmentProgram<int>(compile)


    let a = prog.Prepend(1)
    let b = prog.InsertAfter(a, 2)

    Log.start "run"
    prog.Run()
    Log.stop()
    
    a.Dispose()


    Log.start "run"
    prog.Run()
    Log.stop()

    let c = prog.InsertBefore(b, 3)

    Log.start "run"
    prog.Run()
    Log.stop()

    let d = prog.InsertAfter(c, 4)
    let e = prog.InsertAfter(null, 6)
    
    Log.start "run"
    prog.Run()
    Log.stop()

    d.Dispose()
    e.Dispose()
    c.Dispose()
    b.Dispose()
    
    Log.start "run"
    prog.Run()
    Log.stop()

    let x = prog.Prepend 2
    let y = prog.Prepend 1

    Log.start "run"
    prog.Run()
    Log.stop()

    prog.Clear()
    
    Log.start "run"
    prog.Run()
    Log.stop()

    Log.line "done"

    0
