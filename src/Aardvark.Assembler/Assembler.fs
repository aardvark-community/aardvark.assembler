namespace Aardvark.Assembler

open System
open System.IO
open System.Runtime.InteropServices
open Aardvark.Base
open Aardvark.Base.Runtime

/// AssemblerStream functions.
module AssemblerStream =
    /// Creates a new AssemblerStream writing to the given stream.
    let create (s : Stream) =
        match RuntimeInformation.ProcessArchitecture with
        | Architecture.X64 -> new Aardvark.Assembler.AMD64.AssemblerStream(s, true) :> IAssemblerStream
        | Architecture.Arm64 -> new ARM64.Arm64Stream(s, true) :> IAssemblerStream
        | a -> raise <| new NotSupportedException(sprintf "unsupported architecture: %A" a)

    /// Assembles commands to a `Memory<byte>`.
    let toMemory (action : IAssemblerStream -> unit) =
        use ms = new SystemMemoryStream()
        use ass = create ms
        action ass
        ms.ToMemory()

    /// Assembles commands to a `byte[]`.
    let toArray (action : IAssemblerStream -> unit) =
        use ms = new MemoryStream()
        use ass = create ms
        action ass
        ms.ToArray()


    type private JitMemAction<'a, 'b>(ptr : nativeint, size : nativeint) =
        inherit FSharpFunc<'a, 'b>()
        let mutable wrapped : 'a -> 'b = UnmanagedFunctions.wrap ptr

        override x.Finalize() =
            printfn "free"
            wrapped <- Unchecked.defaultof<_>
            JitMem.Free(ptr, size)

        override x.Invoke(a) =
            wrapped a

    /// Compiles an executable action as `unit -> unit`.
    let compile (action : IAssemblerStream -> unit) : unit -> unit =
        use ms = new SystemMemoryStream()
        use ass = create ms
        ass.BeginFunction()
        action ass
        ass.EndFunction()
        ass.Ret()
        let size = nativeint ms.Length
        let ptr = JitMem.Alloc(size)
        try
            JitMem.Copy(ms.ToMemory(), ptr)
            JitMemAction<unit, unit>(ptr, size) |> unbox<unit -> unit>
        with e ->
            JitMem.Free(ptr, size)
            reraise()