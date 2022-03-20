namespace Aardvark.Assembler

open System
open System.IO
open System.Runtime.InteropServices
open Aardvark.Base.Runtime

/// AssemblerStream functions.
module AssemblerStream =
    /// Creates a new AssemblerStream writing to the given stream.
    let create (s : Stream) =
        match RuntimeInformation.ProcessArchitecture with
        | Architecture.X64 -> new Aardvark.Assembler.AMD64.AssemblerStream(s, true) :> IAssemblerStream
        | Architecture.Arm64 -> new ARM64.Arm64Stream(s, true) :> IAssemblerStream
        | a -> raise <| new NotSupportedException(sprintf "Architecture %A not supported" a)

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