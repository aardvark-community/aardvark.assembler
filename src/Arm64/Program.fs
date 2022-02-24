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
        Log.line "%d" a
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


    static member Copy(src : Memory<byte>, dst : nativeint) =
        if src.Length > 0 then
            use hSrc = src.Pin()
            let pSrc = hSrc.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
            ecpy(dst, pSrc, unativeint src.Length)
            
    static member Copy(src : Memory<byte>, dst : managedptr) =
        if src.Length > 0 then
            if nativeint src.Length <> dst.Size then failwithf "inconsitent copy-size: %d vs %d" src.Length dst.Size
            dst.Use (fun pDst ->
                use hSrc = src.Pin()
                let pSrc = hSrc.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
                ecpy(pDst, pSrc, unativeint src.Length)
            )
            
    static member Copy(src : Memory<byte>, dst : managedptr, dstOffset : nativeint) =
        if src.Length > 0 then
            if dstOffset + nativeint src.Length > dst.Size then failwithf "copy range exceeds dst size: %d + %d vs %d" dstOffset src.Length dst.Size
            dst.Use (fun pDst ->
                use hSrc = src.Pin()
                let pSrc = hSrc.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
                ecpy(pDst + dstOffset, pSrc, unativeint src.Length)
            )
            

type FragmentProgram<'a> private(differential : bool, compile : option<'a> -> 'a -> IAssemblerStream -> unit) =
    static let initialCapacity = 64n <<< 10
    static let config = 
        {
            MemoryManagerConfig.malloc = fun size -> JitMem.Alloc size
            MemoryManagerConfig.mfree = fun ptr size -> JitMem.Free(ptr, size)
            MemoryManagerConfig.mcopy = fun src dst size -> JitMem.Copy(src, dst, size)
        }
    
    static let toMemory (action : IAssemblerStream -> unit) : Memory<byte> =
        use ms = new SystemMemoryStream()
        use ass = AssemblerStream.create ms
        action ass
        ass.Jump 0
        ms.ToMemory()

    let compile = OptimizedClosures.FSharpFunc<_,_,_,_>.Adapt compile
    let manager = new MemoryManager(initialCapacity, config)

    let wrapLock = obj()
    let mutable pAction = 0n
    let mutable action = Unchecked.defaultof<System.Action>

    let toUpdate = System.Collections.Generic.HashSet<Fragment<'a>>()
    let toWriteJump = System.Collections.Generic.HashSet<Fragment<'a>>()


    let mutable first, last =
        let prolog = toMemory (fun ass -> ass.BeginFunction())
        let epilog = toMemory (fun ass -> ass.EndFunction(); ass.Ret())

        let pProlog = 
            let block = manager.Alloc(prolog.Length)
            JitMem.Copy(prolog, block)
            block
            
        let pEpilog = 
            let block = manager.Alloc(epilog.Length)
            JitMem.Copy(epilog, block)
            block

        let fProlog = new Fragment<'a>(differential, toWriteJump, toUpdate, null, manager, Unchecked.defaultof<'a>, pProlog)
        let fEpilog = new Fragment<'a>(differential, toWriteJump, toUpdate, fProlog, manager, Unchecked.defaultof<'a>, pEpilog)
        fProlog.Next <- fEpilog
        fEpilog.Prev <- fProlog
        fProlog.WriteJump()
        fProlog, fEpilog


    member x.InsertAfter(ref : Fragment<'a>, tag : 'a) =
        let mutable ref = ref
        let mutable prevTag = None

        if isNull ref then ref <- first
        else prevTag <- Some ref.Tag
        let next = ref.Next

        let code = 
            toMemory (fun s ->
                compile.Invoke(prevTag, tag, s)
                s.Jump(0)
            )

        let block = manager.Alloc(nativeint code.Length)
        JitMem.Copy(code, block)
        let frag = new Fragment<'a>(differential, toWriteJump, toUpdate, first, manager, tag, block)

        frag.Next <- next
        frag.Prev <- ref
        next.Prev <- frag
        ref.Next <- frag
        toWriteJump.Add frag |> ignore
        toWriteJump.Add ref |> ignore

        if differential && not (Object.ReferenceEquals(next, last)) then
            toUpdate.Add(next) |> ignore

        frag

    member x.InsertBefore(ref : Fragment<'a>, tag : 'a) =
        let ref = if isNull ref then last else ref
        x.InsertAfter(ref.Prev, tag)

    member x.Append(tag : 'a) =
        x.InsertBefore(null, tag)

    member x.Prepend(tag : 'a) =
        x.InsertAfter(null, tag)

    member x.Prolog = first
    member x.Epilog = last

    member x.Dispose() =
        if not (isNull first) then
            first <- null
            last <- null
            manager.Dispose()
            action <- Unchecked.defaultof<_>
            pAction <- 0n

    member x.Run() =
        if isNull first then raise <| ObjectDisposedException "FragmentProgram"

        for u in toUpdate do u.Update(compile)
        toUpdate.Clear()

        for j in toWriteJump do j.WriteJump()
        toWriteJump.Clear()

        let action = 
            lock wrapLock (fun () ->
                let ptr = manager.Pointer + first.Offset
                if ptr <> pAction then
                    pAction <- ptr
                    action <- Marshal.GetDelegateForFunctionPointer<System.Action>(ptr)
                action
            )
        action.Invoke()

    interface IDisposable with
        member x.Dispose() = x.Dispose()

    new(compile : option<'a> -> 'a -> IAssemblerStream -> unit) = new FragmentProgram<'a>(true, compile)
    new(compile : 'a -> IAssemblerStream -> unit) = new FragmentProgram<'a>(false, fun _ t s -> compile t s)

and [<AllowNullLiteral>] Fragment<'a>(differential : bool, toWriteJump : System.Collections.Generic.HashSet<Fragment<'a>>, toUpdate : System.Collections.Generic.HashSet<Fragment<'a>>, prolog : Fragment<'a>, manager : MemoryManager, tag : 'a, ptr : managedptr) =
    static let toMemory (action : System.IO.Stream -> IAssemblerStream -> unit) : Memory<byte> =
        use ms = new SystemMemoryStream()
        use ass = AssemblerStream.create ms
        action ms ass
        ass.Jump 0
        ms.ToMemory()

    let mutable ptr = ptr
    let mutable prev : Fragment<'a> = null
    let mutable next : Fragment<'a> = null


    let writeJump(offset : int) =  
        let code = 
            use ms = new SystemMemoryStream()
            use ass = AssemblerStream.create ms
            ass.Jump offset
            ms.ToMemory()

        JitMem.Copy(code, ptr, ptr.Size - nativeint code.Length)


    member x.Prev
        with get() : Fragment<'a> = prev
        and internal set (p : Fragment<'a>) = prev <- p

    member x.Next
        with get() : Fragment<'a> = next
        and internal set (n : Fragment<'a>) = next <- n

    member x.Offset : nativeint = ptr.Offset

    member x.WriteJump() : unit =
        if isNull next then 
            writeJump 0
        else 
            let ref = ptr.Offset + ptr.Size
            writeJump (int (next.Offset - ref))

    member private x.Write(data : Memory<byte>) =
        let size = nativeint data.Length
        if size = ptr.Size then
            JitMem.Copy(data, ptr)
        else
            let old = ptr
            let n = manager.Alloc(size)
            JitMem.Copy(data, n)
            ptr <- n
            if not (isNull prev) then toWriteJump.Add prev |> ignore
            manager.Free old

        toWriteJump.Add x |> ignore
            
    member x.Tag : 'a = tag

    member internal x.Update(compile : OptimizedClosures.FSharpFunc<option<'a>, 'a, IAssemblerStream, unit>) : unit =
        let prevTag = 
            if Object.ReferenceEquals(prev, prolog) then None
            else Some prev.Tag

        let code = 
            toMemory (fun s ass -> 
                compile.Invoke(prevTag, tag, ass)
                ass.Jump(0)
            )

        x.Write code


    member x.Dispose() =
        let p = prev
        let n = next

        n.Prev <- p
        p.Next <- n

        manager.Free ptr
        toWriteJump.Add p |> ignore
        if differential then
            toUpdate.Remove x |> ignore
            toUpdate.Add n |> ignore

        toWriteJump.Remove x |> ignore
        



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

    Log.line "done"

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
