open System
open Aardvark.Base
open System.Runtime.InteropServices
open System.Runtime.CompilerServices
open Microsoft.FSharp.NativeInterop

#nowarn "9"
open Aardvark.Base.Runtime
open System.Collections.Generic
open Aardvark.Base.Runtime

type IntDelegate = delegate of unit -> int


// type MyFun = delegate of uint32 * uint32 * uint32 * uint32 * uint32 * uint32 * uint32 * uint32 -> unit

type MyFun = delegate of nativeint * uint64 * uint64 * uint64 * uint64 * uint64 * uint64 * uint64 * uint32 * uint64 * float -> unit
type IntDel = delegate of int -> unit

let thing = MyFun (fun a b c d e f g h i j k -> printfn "yeah: %A" (a,b,c,d,e,f,g,h,i,j,k))

let print =
    IntDel (fun a -> 
        printfn "%A" a
    )

[<EntryPoint>]
let main _ =
    Aardvark.Init()
    let printInt = Marshal.GetFunctionPointerForDelegate print

    let baseStream = new SystemMemoryStream()
    let s = AssemblerStream.create baseStream

    let store = Marshal.AllocHGlobal (sizeof<int> * 16)
    for i in 0 .. 15 do
        NativePtr.set (NativePtr.ofNativeInt store) i ((i+1))

    let ff = Marshal.GetFunctionPointerForDelegate thing

    // code.mov(uint64 store, Register.R1)
    // code.load(false, Register.R1, 0u, Register.R0)
    // code.push Register.R0
    // code.add(false, Register.R0, 128us, Register.R0)
    // code.store(false, Register.R0, 0u, Register.R1)
    // code.pop Register.R0

    use pData = fixed [| 6UL |]
    use pFloat = fixed [| 2.5f |]
    use pDbl = fixed [| -3.0 |]
    
    use counter = fixed [| 3 |]

    s.BeginFunction()
    s.BeginCall(11)
    s.PushDoubleArg (NativePtr.toNativeInt pDbl)
    s.PushArg 9n
    s.PushArg 8
    s.PushArg 7n
    s.PushPtrArg (NativePtr.toNativeInt pData)
    s.PushArg 5n
    s.PushArg 4n
    s.PushArg 3n
    s.PushArg 2n
    s.PushArg 1n
    s.PushArg 0n
    s.Call(ff)

    // let r0 = s.ArgumentRegisters.[0]
    // let r1 = s.ArgumentRegisters.[1]
    // let r2 = s.ArgumentRegisters.[2]
    // let b = s.NewLabel()
    // s.Mark b

    // s.BeginCall(1)
    // s.PushIntArg(NativePtr.toNativeInt counter)
    // s.Call printInt

    // s.Set(r0, NativePtr.toNativeInt counter)
    // s.Load(r1, r0, false)
    // s.Set(r2, -1)
    // s.AddInt(r1, r2, false)
    // s.Store(r0, r1, false)




    // s.Cmp(NativePtr.toNativeInt counter, 0)
    // s.Jump(JumpCondition.GreaterEqual, b)


    // let dst = s.NewLabel()
    // s.Cmp(NativePtr.toNativeInt counter, 0)
    // s.Jump(JumpCondition.Less, dst)

    // s.BeginCall(1)
    // s.PushIntArg(NativePtr.toNativeInt counter)
    // s.Call printInt

    // s.Mark dst


    s.EndFunction()
    s.Ret()

    s.Dispose()

    let ptr = ExecutableMemory.Alloc (unativeint baseStream.Length)
    let code = baseStream.ToMemory()
    ptr.Write(code, 0n)
    
    printfn "%A %A" ptr.Size ptr.Capacity
    ptr.Realloc(unativeint code.Length)
    printfn "%A %A" ptr.Size ptr.Capacity




    let a = Marshal.GetDelegateForFunctionPointer<IntDelegate>(ptr.Pointer)
    a.Invoke() |> printfn "result: %d"


    NativePtr.ofNativeInt<uint32> store |> NativePtr.read |> printfn "store[0]: %d"
    ptr.Dispose()
    0
