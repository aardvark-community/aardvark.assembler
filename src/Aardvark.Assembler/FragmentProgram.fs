namespace Aardvark.Assembler

open System
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open Aardvark.Base
open Aardvark.Base.Runtime

#nowarn "9"

type internal FragmentProgramState<'a> =
    {
        differential : bool
        toWriteJump : System.Collections.Generic.HashSet<Fragment<'a>>
        toUpdate : System.Collections.Generic.HashSet<Fragment<'a>>
        manager : MemoryManager
        mutable prolog : Fragment<'a>
        mutable epilog : Fragment<'a>
    }

/// A FragmentProgram represents a native program that can be modified on Fragment-level.
/// New Fragments can be Inserted at arbitrary positions, and old Fragments can be removed.
/// Each Fragment is identified by a unique tag and can update its executed code.
and FragmentProgram<'a> internal(differential : bool, compile : option<'a> -> 'a -> IAssemblerStream -> unit) =
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
    let mutable entry = NativePtr.alloc<nativeint> 1
    let mutable action = Unchecked.defaultof<System.Action>

    let toUpdate = System.Collections.Generic.HashSet<Fragment<'a>>()
    let toWriteJump = System.Collections.Generic.HashSet<Fragment<'a>>()

    
    let state =
        {
            toWriteJump = toWriteJump
            toUpdate = toUpdate
            manager = manager
            differential = differential
            prolog = null
            epilog = null
        }

    let mutable first, last =
        let prolog = toMemory (fun ass -> ass.BeginFunction())
        let epilog = toMemory (fun ass -> ass.EndFunction(); ass.Ret())

        let pProlog = 
            let block = manager.Alloc(nativeint prolog.Length)
            JitMem.Copy(prolog, block)
            block
            
        let pEpilog = 
            let block = manager.Alloc(nativeint epilog.Length)
            JitMem.Copy(epilog, block)
            block

        let fProlog = new Fragment<'a>(state, Unchecked.defaultof<'a>, pProlog)
        let fEpilog = new Fragment<'a>(state, Unchecked.defaultof<'a>, pEpilog)
        state.prolog <- fProlog
        state.epilog <- fEpilog

        fProlog.Next <- fEpilog
        fEpilog.Prev <- fProlog
        fProlog.WriteJump()
        fProlog, fEpilog

    /// Inserts a new Fragment directly after `ref` with the given tag.
    /// Note that when the program is "differential" the subsequent Fragment will also be recompiled.
    /// This Method assumes that `ref` is part of this program and is also not disposed.
    member x.InsertAfter(ref : Fragment<'a>, tag : 'a) =
        if not (isNull ref) && ref.IsDisposed then raise <| ObjectDisposedException "FragmentProgram.InsertAfter reference is disposed" 
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
        let frag = new Fragment<'a>(state, tag, block)

        frag.Next <- next
        frag.Prev <- ref
        next.Prev <- frag
        ref.Next <- frag
        toWriteJump.Add frag |> ignore
        toWriteJump.Add ref |> ignore

        if differential && not (Object.ReferenceEquals(next, last)) then
            toUpdate.Add(next) |> ignore

        frag

    /// Inserts a new Fragment directly before `ref` with the given tag.
    /// Note that when the program is "differential" the `ref` Fragment will also be recompiled.
    /// This Method assumes that `ref` is part of this program and is also not disposed.
    member x.InsertBefore(ref : Fragment<'a>, tag : 'a) =
        if not (isNull ref) && ref.IsDisposed then raise <| ObjectDisposedException "FragmentProgram.InsertBefore reference is disposed" 
        let ref = if isNull ref then last else ref
        x.InsertAfter(ref.Prev, tag)

    /// Inserts a new Fragment at the end of the program with the given tag.
    member x.Append(tag : 'a) =
        x.InsertBefore(null, tag)

    /// Inserts a new Fragment at the beginning of the program with the given tag.
    member x.Prepend(tag : 'a) =
        x.InsertAfter(null, tag)

    /// The (unchangeable) first Fragment in the Program.
    member x.Prolog = first

    /// The (unchangeable) last Fragment in the Program.
    member x.Epilog = last

    /// Relases all resources associated with this FragmentProgram.
    member x.Dispose() =
        if not (isNull first) then
            first <- null
            last <- null
            manager.Dispose()
            action <- Unchecked.defaultof<_>
            pAction <- 0n
            NativePtr.free entry
            entry <- NativePtr.zero

    /// Deletes all Fragments from the Program and resets it to its initial state.
    member x.Clear() =
        let mutable f = first.Next
        while not (Object.ReferenceEquals(f, last)) do
            let n = f.Next
            f.Dispose()
            f <- n

    /// Recompiles all necessary Fragments and ensures the program is ready to be executed.
    member x.Update() =
        for u in toUpdate do u.Update(compile)
        toUpdate.Clear()

        for j in toWriteJump do j.WriteJump()
        toWriteJump.Clear()

        lock wrapLock (fun () ->
            let ptr = manager.Pointer + first.Offset
            if ptr <> pAction then
                NativePtr.write entry ptr
                pAction <- ptr
                action <- Marshal.GetDelegateForFunctionPointer<System.Action>(ptr)
        )

    /// Updates and Executes the program.
    member x.Run() =
        x.Update()
        action.Invoke()

    /// A `nativeptr<nativeint>` holding the entry-pointer for the program.
    member x.EntryPointer = entry

    interface IDisposable with
        member x.Dispose() = x.Dispose()

    /// Creates a new FragmentProgram with the given "differential" compile-function.
    new(compile : option<'a> -> 'a -> IAssemblerStream -> unit) = new FragmentProgram<'a>(true, compile)
    
    /// Creates a new FragmentProgram with the given "non-differential" compile-function.
    new(compile : 'a -> IAssemblerStream -> unit) = new FragmentProgram<'a>(false, fun _ t s -> compile t s)

/// A Fragment is a part of a FragmentProgram.
and [<AllowNullLiteral>] Fragment<'a> internal(state : FragmentProgramState<'a>, tag : 'a, ptr : managedptr) =

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

    /// Gets the previous Fragment in the Program (or null if none).
    member x.Prev
        with get() : Fragment<'a> = prev
        and internal set (p : Fragment<'a>) = prev <- p

    /// Gets the next Fragment in the Program (or null if none).
    member x.Next
        with get() : Fragment<'a> = next
        and internal set (n : Fragment<'a>) = next <- n

    /// True if the Fragment has previously been disposed.
    member x.IsDisposed : bool = ptr.Free

    /// The start-offset of the Fragment in the Program.
    member x.Offset : nativeint = ptr.Offset

    /// Rewrites the final jump-instruction
    member internal x.WriteJump() : unit =
        if isNull next then 
            writeJump 0
        else 
            let ref = ptr.Offset + ptr.Size
            writeJump (int (next.Offset - ref))

    /// Updates the Fragment's content. (also resizing it if necessary)
    member private x.Write(data : Memory<byte>) =
        let size = nativeint data.Length
        if size = ptr.Size then
            JitMem.Copy(data, ptr)
        else
            let old = ptr
            let n = state.manager.Alloc(size)
            JitMem.Copy(data, n)
            ptr <- n
            if not (isNull prev) then state.toWriteJump.Add prev |> ignore
            state.manager.Free old

        state.toWriteJump.Add x |> ignore
            
    /// The tag specified upon creation of the Fragment.
    member x.Tag : 'a = tag

    /// Updates the Fragment's code.
    member internal x.Update(compile : OptimizedClosures.FSharpFunc<option<'a>, 'a, IAssemblerStream, unit>) : unit =
        let prevTag = 
            if Object.ReferenceEquals(prev, state.prolog) then None
            else Some prev.Tag

        let code = 
            AssemblerStream.toMemory (fun ass -> 
                compile.Invoke(prevTag, tag, ass)
                ass.Jump(0)
            )

        x.Write code

    /// Deletes the Fragment from the FragmentProgram and releases all allocated resources.
    member x.Dispose() : unit =
        let p = prev
        let n = next

        if not (isNull n) then n.Prev <- p
        if not (isNull p) then p.Next <- n

        state.manager.Free ptr
        if not (isNull p) then state.toWriteJump.Add p |> ignore
        if state.differential then
            state.toUpdate.Remove x |> ignore
            if not (isNull n) && not (Object.ReferenceEquals(n, state.epilog)) then state.toUpdate.Add n |> ignore

        state.toWriteJump.Remove x |> ignore
        prev <- null
        next <- null
        


