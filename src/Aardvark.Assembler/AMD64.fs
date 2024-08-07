namespace Aardvark.Assembler

open System
open System.Collections.Generic
open System.IO
open Aardvark.Base
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open FSharp.Data.Adaptive

#nowarn "9"
#nowarn "51"

/// internal AMD64/x64 Assembler.
module AMD64 =
    open System.Runtime.CompilerServices
    type Amd64AssemblerLabel internal() =
        let mutable position = -1L

        member x.Position
            with get() = position
            and internal set p = position <- p

    type Register =
        | Rax = 0
        | Rcx = 1
        | Rdx = 2
        | Rbx = 3
        | Rsp = 4
        | Rbp = 5
        | Rsi = 6
        | Rdi = 7

        | R8  = 8
        | R9  = 9
        | R10 = 10
        | R11 = 11
        | R12 = 12
        | R13 = 13
        | R14 = 14
        | R15 = 15

        | XMM0 = 16
        | XMM1 = 17
        | XMM2 = 18
        | XMM3 = 19
        | XMM4 = 20
        | XMM5 = 21
        | XMM6 = 22
        | XMM7 = 23
        | XMM8 = 24
        | XMM9 = 25
        | XMM10 = 26
        | XMM11 = 27
        | XMM12 = 28
        | XMM13 = 29
        | XMM14 = 30
        | XMM15 = 31

    type CallingConvention = { shadowSpace : bool; registers : Register[]; floatRegisters : Register[]; calleeSaved : Register[] }
    
    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    module CallingConvention =
        let windows = 
            { 
                shadowSpace = true
                registers = [| Register.Rcx; Register.Rdx; Register.R8; Register.R9 |] 
                floatRegisters = [| Register.XMM0; Register.XMM1; Register.XMM2; Register.XMM3 |]
                calleeSaved = [|Register.R12; Register.R13; Register.R14; Register.R15 |]
            }

        let linux = 
            { 
                shadowSpace = false
                registers = [| Register.Rdi; Register.Rsi; Register.Rdx; Register.Rcx; Register.R8; Register.R9 |] 
                floatRegisters = [| Register.XMM0; Register.XMM1; Register.XMM2; Register.XMM3; Register.XMM4; Register.XMM5; Register.XMM6; Register.XMM7 |]
                calleeSaved = [|Register.R12; Register.R13; Register.R14; Register.R15 |]
            }

    [<AutoOpen>]
    module private Utils = 
        let inline rexAndModRM (wide : bool) (left : byte) (right : byte) (rex : byref<byte>) (modRM : byref<byte>) =
            let r = if left >= 8uy then 1uy else 0uy
            let b = if right >= 8uy then 1uy else 0uy
            let w = if wide then 1uy else 0uy
            rex <- 0x40uy ||| (w <<< 3) ||| (r <<< 2) ||| b
                
            let left = left &&& 0x07uy
            let right = right &&& 0x07uy
            modRM <- 0xC0uy ||| (left <<< 3) ||| right

        let inline rexAndModRM0 (wide : bool) (left : byte) (right : byte) (rex : byref<byte>) (modRM : byref<byte>) =
            let r = if left >= 8uy then 1uy else 0uy
            let b = if right >= 8uy then 1uy else 0uy
            let w = if wide then 1uy else 0uy
            rex <- 0x40uy ||| (w <<< 3) ||| (r <<< 2) ||| b
                
            let left = left &&& 0x07uy
            let right = right &&& 0x07uy
            modRM <- 0x00uy ||| (left <<< 3) ||| right

        let inline rexAndModRMSIB (wide : bool) (left : byte) (rex : byref<byte>) (modRM : byref<byte>) =
            let r = if left >= 8uy then 1uy else 0uy
            let w = if wide then 1uy else 0uy
            rex <- 0x40uy ||| (w <<< 3) ||| (r <<< 2)
                
            let left = left &&& 0x07uy
            modRM <- 0x40uy ||| (left <<< 3) ||| 0x04uy


        let inline dec (v : byref<int>) =
            let o = v
            v <- o - 1
            if o < 0 then failwith "argument index out of bounds"
            o

    let private localConvention =
        match Environment.OSVersion with
            | Windows -> CallingConvention.windows
            | _ -> CallingConvention.linux


    let private registers =
        [|
            Aardvark.Assembler.Register("rax", 0)
            Aardvark.Assembler.Register("rcx", 1)
            Aardvark.Assembler.Register("rdx", 2)
            Aardvark.Assembler.Register("rbx", 3)
            Aardvark.Assembler.Register("rsp", 4)
            Aardvark.Assembler.Register("rbp", 5)
            Aardvark.Assembler.Register("rsi", 6)
            Aardvark.Assembler.Register("rdi", 7)
            Aardvark.Assembler.Register("r8", 8)
            Aardvark.Assembler.Register("r9", 9)
            Aardvark.Assembler.Register("r10", 10)
            Aardvark.Assembler.Register("r11", 11)
            Aardvark.Assembler.Register("r12", 12)
            Aardvark.Assembler.Register("r13", 13)
            Aardvark.Assembler.Register("r14", 14)
            Aardvark.Assembler.Register("r15", 15)
            Aardvark.Assembler.Register("xmm0", 16)
            Aardvark.Assembler.Register("xmm1", 17)
            Aardvark.Assembler.Register("xmm2", 18)
            Aardvark.Assembler.Register("xmm3", 19)
            Aardvark.Assembler.Register("xmm4", 20)
            Aardvark.Assembler.Register("xmm5", 21)
            Aardvark.Assembler.Register("xmm6", 22)
            Aardvark.Assembler.Register("xmm7", 23)
            Aardvark.Assembler.Register("xmm8", 24)
            Aardvark.Assembler.Register("xmm9", 25)
            Aardvark.Assembler.Register("xmm10", 26)
            Aardvark.Assembler.Register("xmm11", 27)
            Aardvark.Assembler.Register("xmm12", 28)
            Aardvark.Assembler.Register("xmm13", 29)
            Aardvark.Assembler.Register("xmm14", 30)
            Aardvark.Assembler.Register("xmm15", 31)
        |]

    let private calleeSaved = localConvention.calleeSaved |> Array.map (int >> Array.get registers)
    let private argumentRegisters = localConvention.registers |> Array.map (int >> Array.get registers)
    let private returnRegister = registers.[0]

    // Register.R12; Register.R13; Register.R14; Register.R15

 
    module private Bitwise =
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let float32Bits (v : float32) =
            let ptr = NativePtr.stackalloc 1
            NativePtr.write ptr v
            NativePtr.read (NativePtr.ofNativeInt<uint32> (NativePtr.toNativeInt ptr))
            
        [<MethodImpl(MethodImplOptions.NoInlining)>]
        let floatBits (v : float) =
            let ptr = NativePtr.stackalloc 1
            NativePtr.write ptr v
            NativePtr.read (NativePtr.ofNativeInt<uint64> (NativePtr.toNativeInt ptr))


    [<AutoOpen>]
    module private Amd64Arguments =
        type Reg = Aardvark.Assembler.Register

        let inline reg (r : Reg) = unbox<Register> r.Tag

        [<Flags>]
        type ArgumentKind =
            | None = 0
            | UInt32 = 1
            | UInt64 = 2
            | Float = 4
            | Double = 8
            | Indirect = 0x10

            | TypeMask = 0xF
            
        [<Struct>]
        type Argument =
            {
                Kind    : ArgumentKind
                Value   : uint64
            }

            member x.Integral =
                match x.Kind &&& ArgumentKind.TypeMask with
                | ArgumentKind.UInt32 | ArgumentKind.UInt64 -> true
                | _ -> false

            member x.ArgumentSize =
                if x.Kind &&& ArgumentKind.UInt32 <> ArgumentKind.None then 4
                elif x.Kind &&& ArgumentKind.UInt64 <> ArgumentKind.None then 8
                elif x.Kind &&& ArgumentKind.Float <> ArgumentKind.None then 4
                elif x.Kind &&& ArgumentKind.Double <> ArgumentKind.None then 8
                else failwithf "bad argument kind: %A" x.Kind

            static member UInt32(value : uint32) = { Kind = ArgumentKind.UInt32; Value = uint64 value }
            static member UInt64(value : uint64) = { Kind = ArgumentKind.UInt64; Value = value }
            static member Float(value : float32) = { Kind = ArgumentKind.Float; Value = uint64 (Bitwise.float32Bits value) }
            static member Double(value : float) = { Kind = ArgumentKind.Double; Value = Bitwise.floatBits value }

            static member UInt32Ptr(value : nativeptr<uint32>) = { Kind = ArgumentKind.Indirect ||| ArgumentKind.UInt32; Value = uint64 (NativePtr.toNativeInt value) }
            static member UInt64Ptr(value : nativeptr<uint64>) = { Kind = ArgumentKind.Indirect ||| ArgumentKind.UInt64; Value = uint64 (NativePtr.toNativeInt value) }
            static member FloatPtr(value : nativeptr<float32>) = { Kind = ArgumentKind.Indirect ||| ArgumentKind.Float; Value = uint64 (NativePtr.toNativeInt value) }
            static member DoublePtr(value : nativeptr<float>) = { Kind = ArgumentKind.Indirect ||| ArgumentKind.Double; Value = uint64 (NativePtr.toNativeInt value) }
 
    type AssemblerStream(stream : Stream, leaveOpen : bool) =
        let writer = new BinaryWriter(stream, Text.Encoding.UTF8, leaveOpen)

        static let localConvention =
            match Environment.OSVersion with
                | Windows -> CallingConvention.windows
                | _ -> CallingConvention.linux

        let mutable stackOffset = 0
        let mutable paddingPtr = []
        let mutable argumentOffset = 0
        let mutable argumentIndex = 0

        let mutable arguments : Argument[] = null

        static let push             = [| 0x48uy; 0x89uy; 0x44uy; 0x24uy |]
        static let callRax          = [| 0xFFuy; 0xD0uy |]

        static let oneByteNop       = [| 0x90uy |]
        static let twoByteNop       = [| 0x66uy; 0x90uy |]
        static let threeByteNop     = [| 0x0Fuy; 0x1Fuy; 0x00uy |]
        static let fourByteNop      = [| 0x0Fuy; 0x1Fuy; 0x40uy; 0x00uy |]
        static let fiveByteNop      = [| 0x0Fuy; 0x1Fuy; 0x44uy; 0x00uy; 0x00uy |]
        static let eightByteNop     = [| 0x0Fuy; 0x1Fuy; 0x84uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy |]

        let pendingOffsets = Dict<Amd64AssemblerLabel, List<int64>>()

        member x.NewLabel() =   
            let l = Amd64AssemblerLabel()
            Unsafe.As<AssemblerLabel> l

        member x.Mark(l : AssemblerLabel) =
            let l = Unsafe.As<Amd64AssemblerLabel> l
            match pendingOffsets.TryRemove l with
                | (true, positions) ->
                    let oldPos = stream.Position
                    
                    for p in positions do
                        stream.Position <- p
                        writer.Write(int (oldPos - (4L + p)))

                    stream.Position <- oldPos
                | _ ->
                    ()
            l.Position <- stream.Position

        member x.Cmp(l : Register, v : uint32) =
            if l >= Register.XMM0 then
                failwith "[AMD64] cannot compare media register"
            else
                let mutable rex = if l >= Register.R8 then 0x41uy else 0x40uy
                //rex <- 0x40uy ||| (w <<< 3) ||| (r <<< 2) ||| b
                let l = byte l &&& 0x7uy

                if rex <> 0x40uy then writer.Write(rex)
                writer.Write(0x81uy)
                writer.Write(0xF8uy + l)
                writer.Write(v)
                
        member x.Cmp(l : Register, v : uint64) =
            if l >= Register.XMM0 then
                failwith "[AMD64] cannot compare media register"
            else
                let mutable rex = if l >= Register.R8 then 0x49uy else 0x48uy
                let l = byte l &&& 0x7uy

                if rex <> 0x40uy then writer.Write(rex)
                writer.Write(0x3Buy)
                writer.Write(0xF8uy + l)
                writer.Write(v)

        member x.Mul(dst : Register, src : Register, wide : bool) =
            if dst >= Register.XMM0 || src >= Register.XMM0 then
                failwith "[AMD64] cannot multiply media register"
            else
                let mutable rex = 0uy
                let mutable modRM = 0uy
                rexAndModRM wide (byte dst) (byte src) &rex &modRM
                if rex <> 0x40uy then writer.Write(rex)
                writer.Write(0x0Fuy)
                writer.Write(0xAFuy)
                writer.Write(modRM)
                

        member x.Cmp(l : Register, r : Register, wide : bool) =
            if l >= Register.XMM0 || r >= Register.XMM0 then
                failwith "[AMD64] cannot compare media register"
            else
                let mutable rex = 0uy
                let mutable modRM = 0uy
                rexAndModRM wide (byte l) (byte r) &rex &modRM
                if rex <> 0x40uy then writer.Write(rex)
                writer.Write(0x3B)
                writer.Write(modRM)

        member x.Jump(l : AssemblerLabel) =
            let l = Unsafe.As<Amd64AssemblerLabel> l
            if l.Position >= 0L then
                let offset = l.Position - (stream.Position + 5L)
                x.Jmp(int offset)
            else
                x.Jmp(0)
                let set = pendingOffsets.GetOrCreate(l, fun _ -> List())
                set.Add(stream.Position - 4L)

        member x.Jump(cond : JumpCondition, l : AssemblerLabel) =
            let l = Unsafe.As<Amd64AssemblerLabel> l
            if l.Position >= 0L then
                let offset = l.Position - (stream.Position + 6L)
                x.Jmp(cond, int offset)
            else
                x.Jmp(cond, 0)
                let set = pendingOffsets.GetOrCreate(l, fun _ -> List())
                set.Add(stream.Position - 4L)

        member x.Jmp(cond : JumpCondition, offset : int) =
            writer.Write(0x0Fuy)
            writer.Write(byte cond)
            writer.Write(offset)

        member x.Leave() =
            writer.Write(0xC9uy)

        member x.Begin() =
            x.Push(Register.Rbp)
            x.Mov(Register.Rbp, Register.Rsp, true)
            stackOffset <- stackOffset - 8

            for r in localConvention.calleeSaved do
                x.Push r

        member x.End() =
            for i in localConvention.calleeSaved.Length - 1 .. -1 .. 0 do
                x.Pop localConvention.calleeSaved.[i]
                
            x.Leave()
            stackOffset <- stackOffset - 8

        member x.Mov(target : Register, source : Register, wide : bool) =
            if source <> target then
                let targetMedia = target >= Register.XMM0
                let sourceMedia = source >= Register.XMM0

                let mutable rex = 0x40uy
                let mutable modRM = 0uy

                let dst = byte target &&& 0x0Fuy
                let src = byte source &&& 0x0Fuy


                if targetMedia && sourceMedia then
                    rexAndModRM wide dst src &rex &modRM
                    writer.Write(0xF3uy)
                    if rex <> 0x40uy then writer.Write(rex)
                    writer.Write(0x0Fuy)
                    writer.Write(0x7Euy)
                    writer.Write(modRM)

                elif sourceMedia then
                    rexAndModRM wide src dst &rex &modRM
                    // MOVD  reg/mem32, xmm         66 0F 7E /r
                    // MOVD  reg/mem64, xmm         66 0F 7E /r

                    writer.Write(0x66uy)
                    if rex <> 0x40uy then writer.Write(rex)
                    writer.Write(0x0Fuy)
                    writer.Write(0x7Euy)
                    writer.Write(modRM)

                elif targetMedia then
                    // MOVD  xmm, reg/mem32         66 0F 6E /r
                    // MOVD  xmm, reg/mem64         66 0F 6E /r
                
                    rexAndModRM wide dst src &rex &modRM
                    writer.Write(0x66uy)
                    if rex <> 0x40uy then writer.Write(rex)
                    writer.Write(0x0Fuy)
                    writer.Write(0x6Euy)
                    writer.Write(modRM)

                else
                    // MOV   reg64, reg/mem64       8B/r
                    // MOV   reg32, reg/mem32       8B/r

                    rexAndModRM wide dst src &rex &modRM
                    if rex <> 0x40uy then writer.Write(rex)
                    writer.Write(0x8Buy)
                    writer.Write(modRM)

        member x.Load(target : Register, source : Register, wide : bool) =
            let targetMedia = target >= Register.XMM0
            let sourceMedia = source >= Register.XMM0
            
            let dst = byte target &&& 0x0Fuy
            let src = byte source &&& 0x0Fuy

            let mutable rex = 0x40uy
            let mutable modRM = 0uy

            if sourceMedia then
                failwith "mov reg|xmm, (xmm) not implemented"

            elif targetMedia then
                if wide then
                    rexAndModRM0 false dst src &rex &modRM
                    writer.Write(0xF3uy)
                    if rex <> 0x40uy then writer.Write(rex)
                    writer.Write(0x0Fuy)
                    writer.Write(0x7Euy)
                    writer.Write(modRM)
                    if source = Register.Rsp then writer.Write(0x24uy)

                else
                    // MOVD  xmm, reg/mem32         66 0F 6E /r
                    // MOVD  xmm, reg/mem64         66 0F 6E /r
                    rexAndModRM0 wide dst src &rex &modRM
                    writer.Write(0x66uy)
                    if rex <> 0x40uy then writer.Write(rex)
                    writer.Write(0x0Fuy)
                    writer.Write(0x6Euy)
                    writer.Write(modRM)
                    if source = Register.Rsp then writer.Write(0x24uy)

            else
                // MOV   reg64, reg/mem64       8B/r
                // MOV   reg32, reg/mem32       8B/r
                rexAndModRM0 wide dst src &rex &modRM
                if rex <> 0x40uy then writer.Write(rex)
                writer.Write(0x8Buy)
                writer.Write(modRM)
                if source = Register.Rsp then writer.Write(0x24uy)

        member x.Store(target : Register, source : Register, wide : bool) =
            let targetMedia = target >= Register.XMM0
            let sourceMedia = source >= Register.XMM0
            
            let mutable rex = 0x40uy
            let mutable modRM = 0uy

            let dst = byte target &&& 0x0Fuy
            let src = byte source &&& 0x0Fuy

            if targetMedia then
                failwith "mov (xmm), reg|xmm not implemented"

            elif sourceMedia then
                if wide then
                    rexAndModRM0 false src dst &rex &modRM
                    writer.Write(0x66uy)
                    if rex <> 0x40uy then writer.Write(rex)
                    writer.Write(0x0Fuy)
                    writer.Write(0xD6uy)
                    writer.Write(modRM)
                    if target = Register.Rsp then writer.Write(0x24uy)
                else
                    // MOVD  reg/mem32, xmm         66 0F 7E /r
                    // MOVD  reg/mem64, xmm         66 0F 7E /r

                    rexAndModRM0 wide src dst &rex &modRM
                    writer.Write(0x66uy)
                    if rex <> 0x40uy then writer.Write(rex)
                    writer.Write(0x0Fuy)
                    writer.Write(0x7Euy)
                    writer.Write(modRM)
                    if target = Register.Rsp then writer.Write(0x24uy)
            else
                // MOV   reg/mem64, reg64       89/r
                // MOV   reg/mem32, reg32       89/r

                rexAndModRM0 wide src dst &rex &modRM
                if rex <> 0x40uy then writer.Write(rex)
                writer.Write(0x89uy)
                writer.Write(modRM)
                if target = Register.Rsp then writer.Write(0x24uy)

        member x.Mov(target : Register, value : uint64) =
            if target < Register.XMM0 then
                let tb = target |> byte
                if tb >= 8uy then
                    let tb = tb - 8uy
                    let rex = 0x49uy
                    writer.Write(rex)
                    writer.Write(0xB8uy + tb)
                else
                    let rex = 0x48uy
                    writer.Write(rex)
                    writer.Write(0xB8uy + tb)

                writer.Write value 
                
            else
                x.Mov(Register.Rax, value)
                x.Mov(target, Register.Rax, true)

        member x.Mov(target : Register, value : uint32) =
            if target < Register.XMM0 then
                let tb = target |> byte
                if tb >= 8uy then
                    let tb = tb - 8uy
                    let rex = 0x41uy
                    writer.Write(rex)
                    writer.Write(0xB8uy + tb)
                else
                    writer.Write(0xB8uy + tb)

                writer.Write value 
                
            else
                x.Mov(Register.Rax, value)
                x.Mov(target, Register.Rax, false)


        member inline x.MovQWord(target : Register, source : Register) =
            x.Mov(target, source, true)

        member inline x.MovDWord(target : Register, source : Register) =
            x.Mov(target, source, false)

        member inline x.Mov(target : Register, value : nativeint) =
            x.Mov(target, uint64 value)

        member inline x.Mov(target : Register, value : unativeint) =
            x.Mov(target, uint64 value)

        member inline x.Mov(target : Register, value : int) =
            x.Mov(target, uint32 value)

        member inline x.Mov(target : Register, value : int64) =
            x.Mov(target, uint64 value)

        member inline x.Mov(target : Register, value : int8) =
            x.Mov(target, uint32 value)

        member inline x.Mov(target : Register, value : uint8) =
            x.Mov(target, uint32 value)

        member inline x.Mov(target : Register, value : int16) =
            x.Mov(target, uint32 value)

        member inline x.Mov(target : Register, value : uint16) =
            x.Mov(target, uint32 value)

        member inline x.Mov(target : Register, value : float32) =
            let mutable value = value
            let iv : uint32 = &&value |> NativePtr.cast |> NativePtr.read
            x.Mov(target, iv)

        member inline x.Mov(target : Register, value : float) =
            let mutable value = value
            let iv : uint64 = &&value |> NativePtr.cast |> NativePtr.read
            x.Mov(target, iv)

        member inline x.Load(target : Register, ptr : nativeint, wide : bool) =
            x.Mov(target, uint64 ptr)
            x.Load(target, target, wide)
            

        member inline x.Load(target : Register, ptr : nativeptr<'a>) =
            x.Load(target, NativePtr.toNativeInt ptr, sizeof<'a> = 8)

        member inline x.LoadQWord(target : Register, ptr : nativeint) =
            x.Load(target, ptr, true)

        member inline x.LoadDWord(target : Register, ptr : nativeint) =
            x.Load(target, ptr, false)

        member x.Add(target : Register, source : Register, wide : bool) =
            let mutable rex = 0x40uy
            let mutable modRM = 0uy
            
            if source >= Register.XMM0 || target >= Register.XMM0 then
                failwith "cannot add media register"
            else
                rexAndModRM wide (byte target) (byte source) &rex &modRM

                if rex <> 0x40uy then writer.Write(rex)
                writer.Write(0x03uy)
                writer.Write(modRM)

        member x.Add(target : Register, value : uint32) =
            if value > 0u then
                if target >= Register.XMM0 then
                    failwith "cannot add media register"
                else
                    let r = target |> byte
                    let b = if r >= 8uy then 1uy else 0uy //(r &&& 0xF8uy) >>> 3
                    let r = r &&& 0x07uy
                    let rex = 0x48uy ||| b
                
                    writer.Write(rex)
                    writer.Write(0x81uy)
                    writer.Write(0xC0uy + r)
                    writer.Write(value)

        member x.Sub(target : Register, value : uint32) = 
            if value > 0u then
                if target >= Register.XMM0 then
                    failwith "cannot add media register"
                else
                    let r = target |> byte
                    let b = if r >= 8uy then 1uy else 0uy //(r &&& 0xF8uy) >>> 3
                    let r = r &&& 0x07uy
                    let rex = 0x48uy ||| b
                
                    writer.Write(rex)
                    writer.Write(0x81uy)
                    writer.Write(0xE8uy + r)
                    writer.Write(value)

        member x.Sub(target : Register, source : Register, wide : bool) = 
            if target >= Register.XMM0 || source >= Register.XMM0 then
                failwith "cannot sub media register"
            else
                let mutable rex = 0x40uy
                let mutable modRM = 0uy

                rexAndModRM wide (byte source) (byte target) &rex &modRM

                if rex <> 0x40uy then writer.Write(rex)
                writer.Write(0x29uy)
                writer.Write(modRM)



        member x.Push(r : Register) =
            stackOffset <- stackOffset + 8
            if r >= Register.XMM0 then
                x.Sub(Register.Rsp, 8u)
                x.Store(Register.Rsp, r, true)
            else
                let r = r |> byte
                let b = if r >= 8uy then 1uy else 0uy //(r &&& 0xF8uy) >>> 3
                let r = r &&& 0x07uy
                let w = 1uy
                let rex = 0x40uy ||| (w <<< 3) ||| b

                let code = 0x50uy + r
                if rex <> 0x4uy then writer.Write(rex)
                writer.Write(code)

        member x.Push(value : uint64) =
            x.Mov(Register.Rax, value)
            x.Push(Register.Rax)
//            writer.Write(0x48uy)
//            writer.Write(0x68uy)
//            writer.Write(value)

        member x.Push(value : uint32) =
            stackOffset <- stackOffset + 8
            writer.Write(0x68uy)
            writer.Write(value)

        member x.Push(value : float) =
            stackOffset <- stackOffset + 8
            writer.Write(0x48uy)
            writer.Write(0x68uy)
            writer.Write(value)

        member x.Push(value : float32) =
            stackOffset <- stackOffset + 8
            writer.Write(0x68uy)
            writer.Write(value)
            

        member x.Pop(r : Register) =
            stackOffset <- stackOffset - 8
            if r >= Register.XMM0 then
                x.Load(r, Register.Rsp, true)
                x.Add(Register.Rsp, 8u)
                ()
            else
                let r = r |> byte

                let b = (r &&& 0xF8uy) >>> 3
                let r = r &&& 0x07uy
                let w = 1uy
                let rex = 0x40uy ||| (w <<< 3) ||| b

                let code = 0x58uy + r
                if rex <> 0x4uy then writer.Write(rex)
                writer.Write(code)


        member x.Jmp(offset : int) =
            writer.Write 0xE9uy
            writer.Write offset
            
        member x.Nop(width : int) =
            if width > 0 then
                match width with
                    | 1 -> writer.Write oneByteNop
                    | 2 -> writer.Write twoByteNop
                    | 3 -> writer.Write threeByteNop
                    | 4 -> writer.Write fourByteNop
                    | 5 -> writer.Write fiveByteNop
                    | 6 -> writer.Write threeByteNop; writer.Write threeByteNop // TODO: find good 6 byte nop sequence
                    | 7 -> writer.Write fourByteNop; writer.Write threeByteNop // TODO: find good 7 byte nop sequence
                    | _ -> writer.Write eightByteNop; x.Nop (width - 8)

        member x.BeginCall(args : int) =
            x.Sub(Register.Rsp, 8u)
            stream.Seek(-4L, SeekOrigin.Current) |> ignore
            let ptr = stream.Position
            writer.Write(0u)
            paddingPtr <- ptr :: paddingPtr
            argumentOffset <- 0
            argumentIndex <- args - 1
            arguments <- Array.zeroCreate args

        member private x.PrepareArguments(cc : CallingConvention) =
            if not (RuntimeInformation.IsOSPlatform OSPlatform.Windows) then
                let mutable ii = 0
                let mutable fi = 0

                let stack = System.Collections.Generic.List<Argument>()


                for a in arguments do
                    let indirect = a.Kind &&& ArgumentKind.Indirect <> ArgumentKind.None
                    match a.Kind &&& ArgumentKind.TypeMask with
                    | ArgumentKind.UInt32 ->
                        if ii < cc.registers.Length then 
                            if indirect then x.Load(cc.registers[ii], nativeint a.Value, false)
                            else x.Mov(cc.registers[ii], uint32 a.Value)
                        else 
                            stack.Add a
                        ii <- ii + 1
                    | ArgumentKind.UInt64 ->
                        if ii < cc.registers.Length then 
                            if indirect then x.Load(cc.registers[ii], nativeint a.Value, true)
                            else x.Mov(cc.registers[ii], a.Value)
                        else 
                            stack.Add a
                        ii <- ii + 1
                    | ArgumentKind.Float ->
                        if fi < cc.floatRegisters.Length then 
                            if indirect then x.Load(Register.Rax, nativeint a.Value, false)
                            else x.Mov(Register.Rax, uint32 a.Value)
                            x.Mov(cc.floatRegisters.[fi], Register.Rax, false)
                        else
                            stack.Add a
                        fi <- fi + 1
                    | ArgumentKind.Double ->
                        if fi < cc.floatRegisters.Length then 
                            if indirect then x.Load(Register.Rax, nativeint a.Value, true)
                            else x.Mov(Register.Rax, a.Value)
                            x.Mov(cc.floatRegisters.[fi], Register.Rax, true)
                        else
                            stack.Add a
                        fi <- fi + 1
                    | _ ->
                        failwith "not implemented"

                for i in 1 .. stack.Count do
                    let i = stack.Count - i
                    let a = stack.[i]
                    let indirect = a.Kind &&& ArgumentKind.Indirect <> ArgumentKind.None
                    match a.Kind &&& ArgumentKind.TypeMask with
                    | ArgumentKind.UInt32 | ArgumentKind.Float ->
                        if indirect then x.Load(Register.Rax, nativeint a.Value, false)
                        else x.Mov(Register.Rax, uint32 a.Value)
                        x.Push(Register.Rax)
                        argumentOffset <- argumentOffset + 8

                    | ArgumentKind.UInt64 | ArgumentKind.Double ->
                        if indirect then x.Load(Register.Rax, nativeint a.Value, true)
                        else x.Mov(Register.Rax, a.Value)
                        x.Push(Register.Rax)
                        argumentOffset <- argumentOffset + 8
                    | _ ->
                        failwith "bad argument"

                arguments <- null

        member x.Call(cc : CallingConvention, load : Register -> unit) =
            x.PrepareArguments(cc)

            let paddingPtr =
                match paddingPtr with
                | h :: rest ->
                    paddingPtr <- rest
                    h
                | _ ->
                    failwith "no padding offset"

            let additional = 
                if stackOffset % 16 <> 0 then
                    let p = stream.Position
                    stream.Position <- paddingPtr
                    writer.Write(8u)
                    stream.Position <- p
                    8u
                else
                    0u

            if cc.shadowSpace then
                x.Sub(Register.Rsp, 8u * uint32 cc.registers.Length)

            load Register.Rax
            let r = byte Register.Rax
            if r >= 8uy then
                writer.Write(0x41uy)
                writer.Write(0xFFuy)
                writer.Write(0xD0uy + (r - 8uy))

            else
                writer.Write(0xFFuy)
                writer.Write(0xD0uy + r)
                
            let popSize =
                (if cc.shadowSpace then 8u * uint32 cc.registers.Length else 0u) +
                uint32 argumentOffset +
                additional

            if popSize > 0u then
                x.Add(Register.Rsp, popSize)

            stackOffset <- stackOffset - argumentOffset
            argumentOffset <- 0

        member x.Ret() =
            writer.Write(0xC3uy)

        member x.PushArg(cc : CallingConvention, value : uint64) =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                let i = dec &argumentIndex
                if i < cc.registers.Length then
                    x.Mov(cc.registers.[i], value)
                else
                    argumentOffset <- argumentOffset + 8
                    x.Push(value)
            else
                let i = dec &argumentIndex
                arguments.[i] <- Argument.UInt64 value

         member x.PushArg(cc : CallingConvention, value : uint32) =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                let i = dec &argumentIndex
                if i < cc.registers.Length then
                    x.Mov(cc.registers.[i], value)
                else
                    argumentOffset <- argumentOffset + 8
                    x.Push(value)   
            else
                let i = dec &argumentIndex
                arguments.[i] <- Argument.UInt32 value 

         member x.PushArg(cc : CallingConvention, value : float32) =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                let i = dec &argumentIndex
                if i < cc.floatRegisters.Length then
                    x.Mov(cc.floatRegisters.[i], value)
                else
                    argumentOffset <- argumentOffset + 8
                    x.Push(value)  
            else
                let i = dec &argumentIndex
                arguments.[i] <- Argument.Float value 

         member x.PushArg(cc : CallingConvention, value : float) =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                let i = dec &argumentIndex
                if i < cc.floatRegisters.Length then
                    x.Mov(cc.floatRegisters.[i], value)
                else
                    argumentOffset <- argumentOffset + 8
                    x.Push(value)  
            else
                let i = dec &argumentIndex
                arguments.[i] <- Argument.Double value 

        member x.PushIntArg(cc : CallingConvention, location : nativeint, wide : bool) =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                let i = dec &argumentIndex
                x.Load(Register.Rax, location, wide)
                if i < cc.registers.Length then
                    x.Mov(cc.registers.[i], Register.Rax, wide)
                else
                    argumentOffset <- argumentOffset + 8
                    x.Push(Register.Rax)
            else
                let i = dec &argumentIndex
                arguments.[i] <- if wide then Argument.UInt64Ptr (NativePtr.ofNativeInt location) else Argument.UInt32Ptr (NativePtr.ofNativeInt location)


        member x.PushFloatArg(cc : CallingConvention, location : nativeint, wide : bool) =
            if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                let i = dec &argumentIndex
                x.Load(Register.Rax, location, wide)
                if i < cc.floatRegisters.Length then
                    x.Mov(cc.floatRegisters.[i], Register.Rax, wide)
                else
                    argumentOffset <- argumentOffset + 8
                    x.Push(Register.Rax)
            else
                let i = dec &argumentIndex
                arguments.[i] <- if wide then Argument.DoublePtr (NativePtr.ofNativeInt location) else Argument.FloatPtr (NativePtr.ofNativeInt location)


        member private x.Dispose(disposing : bool) =
            if disposing then 
                GC.SuppressFinalize(x)
                if pendingOffsets.Count > 0 then
                    failwith "[AMD64] some labels have not been defined"

            writer.Dispose()


        member x.ConditionalCall(condition : aval<'a>, callback : 'a -> unit) =
            let outOfDate : nativeptr<int> = NativePtr.alloc 1
            NativePtr.write outOfDate (if condition.OutOfDate then 1 else 0)
            let sub = condition.AddMarkingCallback(fun () -> NativePtr.write outOfDate 1)
        

            let callback () =
                let value = 
                    lock condition (fun () ->
                        let res = condition.GetValue(AdaptiveToken.Top)
                        NativePtr.write outOfDate 0
                        res
                    )
                callback value
                
            let del = Marshal.PinDelegate(System.Action callback)
            
            let noEval = x.NewLabel()
            x.Load(Register.Rax, outOfDate)
            x.Cmp(Register.Rax, 0u)
            x.Jump(JumpCondition.Equal,noEval)
            x.BeginCall(0)
            x.Call(localConvention, fun r -> x.Mov(r, del.Pointer))
            x.Mark noEval

            { new IDisposable with
                member x.Dispose() =
                    sub.Dispose()
                    NativePtr.write outOfDate 0
                    NativePtr.free outOfDate
                    del.Dispose()
            }




        member x.Dispose() = x.Dispose(true)
        override x.Finalize() = x.Dispose(false)

        

        interface IDisposable with
            member x.Dispose() = x.Dispose()

        interface IAssemblerStream with

            member x.Registers = registers
            member x.CalleeSavedRegisters = calleeSaved
            member x.ArgumentRegisters = argumentRegisters
            member x.ReturnRegister = returnRegister


            member x.Push(r) = x.Push(unbox<Register> r.Tag)
            member x.Pop(r) = x.Pop(unbox<Register> r.Tag)
            member x.Mov(target, source) = x.Mov(unbox<Register> target.Tag, unbox<Register> source.Tag, true)
            member x.Load(target, source, wide : bool) = x.Load(unbox<Register> target.Tag, unbox<Register> source.Tag, wide)
            member x.Store(target, source, wide : bool) = x.Store(unbox<Register> target.Tag, unbox<Register> source.Tag, wide)

            
            member x.NewLabel() = x.NewLabel()
            member x.Mark(l) = x.Mark(l)
            member x.Jump(cond : JumpCondition, label : AssemblerLabel) = x.Jump(cond, label)
            member x.Jump(label : AssemblerLabel) = x.Jump(label)

            member x.Copy(srcPtr : nativeint, dstPtr : nativeint, wide : bool) =
                let temp = localConvention.registers.[0]
                x.Load(Register.Rax, srcPtr, wide)
                x.Mov(temp, dstPtr)
                x.Store(temp, Register.Rax, wide)

            member x.Cmp(location : nativeint, value : int) =
                x.Load(Register.Rax, location, false)
                x.Cmp(Register.Rax, uint32 value)

            member x.AddInt(dst, src, wide) =
                x.Add(unbox dst.Tag, unbox src.Tag, wide)
                
            member x.MulInt(dst, src, wide) =
                x.Mul(unbox dst.Tag, unbox src.Tag, wide)


            member x.BeginFunction() = x.Begin()
            member x.EndFunction() = x.End()
            member x.BeginCall(args : int) = x.BeginCall(args)
            member x.Call (ptr : nativeint) = x.Call(localConvention, fun r -> x.Mov(r, ptr))
            member x.CallIndirect(ptr : nativeptr<nativeint>) =
                x.Call(localConvention, fun r -> x.Load(r, ptr))

            member x.PushArg(v : nativeint) = x.PushArg(localConvention, uint64 v)
            member x.PushArg(v : int) = x.PushArg(localConvention, uint32 v)
            member x.PushArg(v : float32) = x.PushArg(localConvention, v)
            member x.PushPtrArg(loc) = x.PushIntArg(localConvention, loc, true)
            member x.PushIntArg(loc) = x.PushIntArg(localConvention, loc, false)
            member x.PushFloatArg(loc) = x.PushFloatArg(localConvention, loc, false)
            member x.PushDoubleArg(loc) = x.PushFloatArg(localConvention, loc, true)

            member x.Ret() = x.Ret()

            member x.WriteOutput(v : nativeint) = x.Mov(Register.Rax, v)
            member x.WriteOutput(v : int) = x.Mov(Register.Rax, v)
            member x.WriteOutput(v : float32) = x.Mov(Register.XMM0, v)

            member x.Set(target : Aardvark.Assembler.Register, value : nativeint) = x.Mov(unbox target.Tag, value)
            member x.Set(target : Aardvark.Assembler.Register, value : int) = x.Mov(unbox target.Tag, value)
            member x.Set(target : Aardvark.Assembler.Register, value : float32) = x.Mov(unbox target.Tag, value)

            member x.Jump(offset : int) = x.Jmp(offset)

        new(stream : Stream) = new AssemblerStream(stream, false)

