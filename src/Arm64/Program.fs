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


type JitMem private() =
    [<DllImport("jitmem")>]
    static extern uint32 epageSize()

    [<DllImport("jitmem")>]
    static extern nativeint ealloc(unativeint size)
    
    [<DllImport("jitmem")>]
    static extern void efree(nativeint ptr, unativeint size)
    
    [<DllImport("jitmem")>]
    static extern void ecpy(nativeint dst, nativeint src, unativeint size)

    static let mutable pageSize = ref 0un

    static member PageSize = 
        lock pageSize (fun () ->
            if pageSize.Value = 0un then
                let s = epageSize() |> unativeint
                pageSize.Value <- s
                s
            else
                pageSize.Value
        )

    static member Alloc(size : nativeint) =
        if size <= 0n then
            0n
        else
            let ps = ExecutableMemory.PageSize
            let effectiveSize =
                if unativeint size % ps = 0un then unativeint size
                else (1un + unativeint size / ps) * ps
            ealloc effectiveSize

    static member Free(ptr : nativeint, size : nativeint) =
        if size > 0n then
            let ps = ExecutableMemory.PageSize
            let effectiveSize =
                if unativeint size % ps = 0un then unativeint size
                else (1un + unativeint size / ps) * ps
            efree(ptr, effectiveSize)

    static member Copy(src : nativeint, dst : nativeint, size : nativeint) =
        if size > 0n then
            ecpy(dst, src, unativeint size)

type FragmentProgram<'a>() =
    static let initialCapacity = 64n <<< 10
    static let config = 
        {
            MemoryManagerConfig.malloc = fun size -> JitMem.Alloc size
            MemoryManagerConfig.mfree = fun ptr size -> JitMem.Free(ptr, size)
            MemoryManagerConfig.mcopy = fun src dst size -> JitMem.Copy(src, dst, size)
        }
    
    static let compile (action : IAssemblerStream -> unit) : Memory<byte> =
        use ms = new SystemMemoryStream()
        use ass = AssemblerStream.create ms
        action ass
        ass.Jump 0
        ms.ToMemory()

    let manager = new Aardvark.Base.MemoryManager(initialCapacity, config)

    let wrapLock = obj()
    let mutable pAction = 0n
    let mutable action = Unchecked.defaultof<System.Action>

    let first, last =
        let prolog = compile (fun ass -> ass.BeginFunction())
        let epilog = compile (fun ass -> ass.EndFunction(); ass.Ret())

        let pProlog = 
            let block = manager.Alloc(prolog.Length)
            block.Use(fun pDst -> 
                use src = prolog.Pin()
                let pSrc = NativePtr.toNativeInt (NativePtr.ofVoidPtr<byte> src.Pointer)
                JitMem.Copy(pSrc, pDst, nativeint prolog.Length)
            )
            block
            
        let pEpilog = 
            let block = manager.Alloc(epilog.Length)
            block.Use(fun pDst -> 
                use src = epilog.Pin()
                let pSrc = NativePtr.toNativeInt (NativePtr.ofVoidPtr<byte> src.Pointer)
                JitMem.Copy(pSrc, pDst, nativeint epilog.Length)
            )
            block

        let fProlog = Fragment<'a>(manager, Unchecked.defaultof<'a>, pProlog)
        let fEpilog = Fragment<'a>(manager, Unchecked.defaultof<'a>, pEpilog)
        fProlog.Next <- fEpilog
        fEpilog.Prev <- fProlog
        fProlog.WriteJump()
        fProlog, fEpilog


    member x.InsertAfter(ref : Fragment<'a>, tag : 'a, compile : option<'a> -> 'a -> IAssemblerStream -> unit) =
        let ref = if isNull ref then first else ref
        use ms = new SystemMemoryStream()
        use ass = AssemblerStream.create ms
        let prevTag = 
            if ref = first then None
            else Some ref.Tag

        compile prevTag tag ass
        ass.Jump(0)

        let code = ms.ToMemory()
        let block = manager.Alloc(nativeint code.Length)

        block.Use (fun pDst ->
            use src = code.Pin()
            let pSrc = NativePtr.toNativeInt (NativePtr.ofVoidPtr<byte> src.Pointer)
            JitMem.Copy(pSrc, pDst, nativeint code.Length)
        )

        let frag = new Fragment<'a>(manager, tag, block)

        let oldNext = ref.Next
        frag.Next <- oldNext
        frag.Prev <- ref
        oldNext.Prev <- frag
        ref.Next <- frag
        frag.WriteJump()
        ref.WriteJump()

        frag

    member x.InsertBefore(ref : Fragment<'a>, tag : 'a, compile : option<'a> -> 'a -> IAssemblerStream -> unit) =
        let ref = if isNull ref then last else ref
        x.InsertAfter(ref.Prev, tag, compile)

    member x.Append(tag : 'a, compile : option<'a> -> 'a -> IAssemblerStream -> unit) =
        x.InsertBefore(null, tag, compile)

    member x.Prepend(tag : 'a, compile : option<'a> -> 'a -> IAssemblerStream -> unit) =
        x.InsertAfter(null, tag, compile)

    member x.Run() =
        let action = 
            lock wrapLock (fun () ->
                let ptr = manager.Pointer + first.Offset
                if ptr <> pAction then
                    pAction <- ptr
                    action <- Marshal.GetDelegateForFunctionPointer<System.Action>(ptr)
                action
            )
        action.Invoke()





and [<AllowNullLiteral>] Fragment<'a>(manager : MemoryManager, tag : 'a, ptr : managedptr) =
    let mutable ptr = ptr
    let mutable prev : Fragment<'a> = null
    let mutable next : Fragment<'a> = null

    static let jumpSize =
        use ms = new SystemMemoryStream()
        use ass = AssemblerStream.create ms
        ass.Jump(0)
        ms.ToMemory().Length

    let writeJump(offset : int) =  
        let code = 
            use ms = new SystemMemoryStream()
            use ass = AssemblerStream.create ms
            ass.Jump offset
            ms.ToMemory()

        ptr.Use (fun dstPtr ->
            let pDst = dstPtr + ptr.Size - nativeint code.Length
            use src = code.Pin()
            let srcPtr = src.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
            JitMem.Copy(srcPtr, pDst, nativeint code.Length)
        )

    member x.Prev
        with get() : Fragment<'a> = prev
        and set (p : Fragment<'a>) = prev <- p

    member x.Next
        with get() : Fragment<'a> = next
        and set (n : Fragment<'a>) = next <- n


    member x.Offset : nativeint = ptr.Offset

    member x.WriteJump() : unit =
        if isNull next then writeJump 0
        else 
            let ref = ptr.Offset + ptr.Size
            writeJump (int (next.Offset - ref))

    // member x.Write(data : Memory<byte>) =
    //     let size = nativeint data.Length
    //     if size = ptr.Size then
    //         use src = data.Pin()
    //         let srcPtr = src.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
    //         ptr.Use(fun dstPtr -> JitMem.Copy(srcPtr, dstPtr, ptr.Size))
    //     else
    //         let n = manager.Alloc(size)
    //         use src = data.Pin()
    //         let srcPtr = src.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
    //         n.Use(fun dstPtr -> JitMem.Copy(srcPtr, dstPtr, ptr.Size))
    //         manager.Free ptr
    //         ptr <- n
    //         if not (isNull prev) then prev.WriteJump()

    //     x.WriteJump()
            
    member x.Tag : 'a = tag

    // member x.Update(compile : option<'a> -> 'a -> IAssemblerStream -> unit) =
    //     let prevTag = 
    //         if isNull prev then None
    //         else Some prev.Tag
    //     use ms = new SystemMemoryStream()
    //     use ass = AssemblerStream.create ms
    //     compile prevTag tag ass
    //     let jumpOffset = ms.Position
    //     ass.Jump(0)

    //     let code = ms.ToMemory()
    //     let block = manager.Alloc(nativeint code.Length)

    //     code.CopyTo(System.Span<byte>())

    //     ptr.Use (fun dstPtr ->
    //         let pDst = dstPtr + ptr.Size - nativeint code.Length
    //         use src = code.Pin()
    //         let srcPtr = src.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
    //         JitMem.Copy(srcPtr, pDst, nativeint code.Length)
    //     )

    //     x.WriteJump()

    member x.Dispose() =
        let p = prev
        let n = next

        n.Prev <- p
        p.Next <- n

        prev <- null
        next <- null
        manager.Free ptr
        p.WriteJump()



[<EntryPoint>]
let main _ =
    Aardvark.Init()
    let printInt = Marshal.GetFunctionPointerForDelegate print

    let prog = FragmentProgram<int>()

    let a = 
        prog.InsertAfter(null, 1, fun _ v s ->
            s.BeginCall 1
            s.PushArg v
            s.Call printInt
        )

    let b = 
        prog.InsertAfter(a, 2, fun _ v s ->
            s.BeginCall 1
            s.PushArg v
            s.Call printInt
        )
    printfn "run"
    prog.Run()
    printfn "done"
    
    a.Dispose()
    printfn "run"
    prog.Run()
    printfn "done"


    exit 0

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
