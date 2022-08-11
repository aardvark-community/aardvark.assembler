module AssemblerTests

open System
open Aardvark.Base
open FSharp.Data.Adaptive
open Expecto
open Aardvark.Assembler
open Microsoft.FSharp.NativeInterop
open System.Runtime.InteropServices
open FsCheck

#nowarn "9"
open Aardvark.Base.Runtime

type IntAction = delegate of int -> unit
type NativeIntAction = delegate of nativeint -> unit
type Float32Action = delegate of float32 -> unit
type FloatAction = delegate of float -> unit
type ManyArgs = delegate of int * int * int * int * int * int * int * int * int * int * int -> unit
type ManyFloats = delegate of float32 * float32 * float32 * float32 * float32 * float32 * float32 * float32 * float32 * float32 * float32 -> unit

type ManyMixed = delegate of int * float32 * nativeint * float32 * int * int * float32 * float32 * int * float32 * int -> unit



let init() =
    Report.RootTarget <- Report.NoTarget
    Aardvark.Init()



[<Tests>]
let jitMem =
    testList "JitMem" [
        test "CanAllocAndFree" {
            init()
            let ptr = JitMem.Alloc 1024n
            JitMem.Free(ptr, 1024n)
        }

        test "CanCopyTo" {
            init()
            let ptr = JitMem.Alloc 1024n
            let data = [|1uy; 2uy|]
            JitMem.Copy(Memory(data), ptr)
            JitMem.Free(ptr, 1024n)
        }

        testProperty "ConsistentWriteRead" (fun (NonEmptyArray(data : byte[])) ->
            init()
            let size = nativeint data.Length
            let ptr = JitMem.Alloc size

            JitMem.Copy(Memory(data), ptr)

            let test = Array.zeroCreate<byte> data.Length
            Marshal.Copy(ptr, test, 0, data.Length)

            
            JitMem.Free(ptr, 1024n)

            data = test
        )

        test "Executable" {
            init()
            let code = 
                AssemblerStream.toMemory (fun ass ->
                    ass.BeginFunction()
                    ass.EndFunction()
                    ass.Ret()
                )

            let ptr = JitMem.Alloc(nativeint code.Length)
            JitMem.Copy(code, ptr)

            let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
            action.Invoke()

            JitMem.Free(ptr, nativeint code.Length)

            ()
        }

        
        test "DirectInt" {
            init()

            let values = System.Collections.Generic.List<int>()
            let action = IntAction values.Add
            let pAction = Marshal.GetFunctionPointerForDelegate action

            let code =
                AssemblerStream.toMemory (fun ass ->
                    ass.BeginFunction()

                    ass.BeginCall(1)
                    ass.PushArg 123
                    ass.Call pAction
                    
                    ass.BeginCall(1)
                    ass.PushArg 321
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                )

            let ptr = JitMem.Alloc(nativeint code.Length)
            JitMem.Copy(code, ptr)

            let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
            action.Invoke()

            JitMem.Free(ptr, nativeint code.Length)
            Expect.equal (Seq.toList values) [123; 321] "inconsistent calls"
        }
        
        test "IndirectInt" {
            init()
            do
                let values = System.Collections.Generic.List<int>()
                let action = IntAction values.Add
                let pAction = Marshal.GetFunctionPointerForDelegate action

                use mem = fixed [| 123; 321 |]

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        ass.BeginFunction()

                        ass.BeginCall(1)
                        ass.PushIntArg (NativePtr.toNativeInt mem)
                        ass.Call pAction
                        
                        ass.BeginCall(1)
                        ass.PushIntArg (NativePtr.toNativeInt (NativePtr.add mem 1))
                        ass.Call pAction

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                JitMem.Copy(code, ptr)

                let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
                action.Invoke()

                JitMem.Free(ptr, nativeint code.Length)
                Expect.equal (Seq.toList values) [123; 321] "inconsistent calls"
        }
        
        test "DirectNativeInt" {
            init()

            let values = System.Collections.Generic.List<nativeint>()
            let action = NativeIntAction values.Add
            let pAction = Marshal.GetFunctionPointerForDelegate action

            let code =
                AssemblerStream.toMemory (fun ass ->
                    ass.BeginFunction()

                    ass.BeginCall(1)
                    ass.PushArg 123n
                    ass.Call pAction
                    
                    ass.BeginCall(1)
                    ass.PushArg 321n
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                )

            let ptr = JitMem.Alloc(nativeint code.Length)
            JitMem.Copy(code, ptr)

            let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
            action.Invoke()

            JitMem.Free(ptr, nativeint code.Length)
            Expect.equal (Seq.toList values) [123n; 321n] "inconsistent calls"
        }
        
        test "IndirectNativeInt" {
            init()
            do
                let values = System.Collections.Generic.List<nativeint>()
                let action = NativeIntAction values.Add
                let pAction = Marshal.GetFunctionPointerForDelegate action

                use mem = fixed [| 123n; 321n |]

                let code =
                    AssemblerStream.toMemory (fun ass -> 
                        ass.BeginFunction()

                        ass.BeginCall(1)
                        ass.PushPtrArg (NativePtr.toNativeInt mem)
                        ass.Call pAction
                        
                        ass.BeginCall(1)
                        ass.PushPtrArg (NativePtr.toNativeInt (NativePtr.add mem 1))
                        ass.Call pAction

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                JitMem.Copy(code, ptr)

                let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
                action.Invoke()

                JitMem.Free(ptr, nativeint code.Length)
                Expect.equal (Seq.toList values) [123n; 321n] "inconsistent calls"
        }

        test "DirectFloat32" {
            init()

            let values = System.Collections.Generic.List<float32>()
            let action = Float32Action values.Add
            let pAction = Marshal.GetFunctionPointerForDelegate action

            let code =
                AssemblerStream.toMemory (fun ass ->
                    ass.BeginFunction()

                    ass.BeginCall(1)
                    ass.PushArg 1.23f
                    ass.Call pAction
                    
                    ass.BeginCall(1)
                    ass.PushArg 3.21f
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                )

            let ptr = JitMem.Alloc(nativeint code.Length)
            JitMem.Copy(code, ptr)

            let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
            action.Invoke()

            JitMem.Free(ptr, nativeint code.Length)
            Expect.equal (Seq.toList values) [1.23f; 3.21f] "inconsistent calls"
        }
        
        test "IndirectFloat32" {
            init()
            do
                let values = System.Collections.Generic.List<float32>()
                let action = Float32Action values.Add
                let pAction = Marshal.GetFunctionPointerForDelegate action

                use mem = fixed [| 1.23f; 3.21f |]

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        ass.BeginFunction()

                        ass.BeginCall(1)
                        ass.PushFloatArg (NativePtr.toNativeInt mem)
                        ass.Call pAction
                        
                        ass.BeginCall(1)
                        ass.PushFloatArg (NativePtr.toNativeInt (NativePtr.add mem 1))
                        ass.Call pAction

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                JitMem.Copy(code, ptr)

                let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
                action.Invoke()

                JitMem.Free(ptr, nativeint code.Length)
                Expect.equal (Seq.toList values) [1.23f; 3.21f] "inconsistent calls"
        }

        
        test "IndirectFloat64" {
            init()
            do
                let values = System.Collections.Generic.List<float>()
                let action = FloatAction values.Add
                let pAction = Marshal.GetFunctionPointerForDelegate action

                use mem = fixed [| 1.23; 3.21 |]

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        ass.BeginFunction()

                        ass.BeginCall(1)
                        ass.PushDoubleArg (NativePtr.toNativeInt mem)
                        ass.Call pAction
                        
                        ass.BeginCall(1)
                        ass.PushDoubleArg (NativePtr.toNativeInt (NativePtr.add mem 1))
                        ass.Call pAction

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                JitMem.Copy(code, ptr)

                let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
                action.Invoke()

                JitMem.Free(ptr, nativeint code.Length)
                Expect.equal (Seq.toList values) [1.23; 3.21] "inconsistent calls"
        }


        test "ManyInts" {
            let values = System.Collections.Generic.List<int * int * int * int * int * int * int * int * int * int * int>()
            let del = ManyArgs (fun a b c d e f g h i j k -> values.Add(a,b,c,d,e,f,g,h,i,j,k))
            let pAction = Marshal.GetFunctionPointerForDelegate del

        
            let code =
                AssemblerStream.toMemory (fun ass ->
                    ass.BeginFunction()

                    ass.BeginCall(11)
                    ass.PushArg 10
                    ass.PushArg 9
                    ass.PushArg 8
                    ass.PushArg 7
                    ass.PushArg 6
                    ass.PushArg 5
                    ass.PushArg 4
                    ass.PushArg 3
                    ass.PushArg 2
                    ass.PushArg 1
                    ass.PushArg 0
                    ass.Call pAction

                    
                    ass.BeginCall(11)
                    ass.PushArg 20
                    ass.PushArg 19
                    ass.PushArg 18
                    ass.PushArg 17
                    ass.PushArg 16
                    ass.PushArg 15
                    ass.PushArg 14
                    ass.PushArg 13
                    ass.PushArg 12
                    ass.PushArg 11
                    ass.PushArg 10
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                )

            let ptr = JitMem.Alloc(nativeint code.Length)
            JitMem.Copy(code, ptr)

            let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
            action.Invoke()

            JitMem.Free(ptr, nativeint code.Length)
            Expect.equal (Seq.toList values) [(0,1,2,3,4,5,6,7,8,9,10); (10,11,12,13,14,15,16,17,18,19,20)] "inconsistent calls"
        }
        
        test "ManyFloats" {
            let values = System.Collections.Generic.List<_>()
            let del = ManyFloats (fun a b c d e f g h i j k -> values.Add(a,b,c,d,e,f,g,h,i,j,k))
            let pAction = Marshal.GetFunctionPointerForDelegate del

        
            let code =
                AssemblerStream.toMemory (fun ass ->
                    ass.BeginFunction()

                    ass.BeginCall(11)
                    ass.PushArg 10.0f
                    ass.PushArg 9.0f
                    ass.PushArg 8.0f
                    ass.PushArg 7.0f
                    ass.PushArg 6.0f
                    ass.PushArg 5.0f
                    ass.PushArg 4.0f
                    ass.PushArg 3.0f
                    ass.PushArg 2.0f
                    ass.PushArg 1.0f
                    ass.PushArg 0.0f
                    ass.Call pAction

                    
                    ass.BeginCall(11)
                    ass.PushArg 20.0f
                    ass.PushArg 19.0f
                    ass.PushArg 18.0f
                    ass.PushArg 17.0f
                    ass.PushArg 16.0f
                    ass.PushArg 15.0f
                    ass.PushArg 14.0f
                    ass.PushArg 13.0f
                    ass.PushArg 12.0f
                    ass.PushArg 11.0f
                    ass.PushArg 10.0f
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                )

            let ptr = JitMem.Alloc(nativeint code.Length)
            JitMem.Copy(code, ptr)

            let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
            action.Invoke()

            JitMem.Free(ptr, nativeint code.Length)
            Expect.equal (Seq.toList values) [(0.0f,1.0f,2.0f,3.0f,4.0f,5.0f,6.0f,7.0f,8.0f,9.0f,10.0f); (10.0f,11.0f,12.0f,13.0f,14.0f,15.0f,16.0f,17.0f,18.0f,19.0f,20.0f)] "inconsistent calls"
        }


        
        test "ManyMixed" {
            let values = System.Collections.Generic.List<_>()
            let del = ManyMixed (fun a b c d e f g h i j k -> values.Add(a,b,c,d,e,f,g,h,i,j,k))
            let pAction = Marshal.GetFunctionPointerForDelegate del

        
            let code =
                AssemblerStream.toMemory (fun ass ->
                    ass.BeginFunction()
                    //  delegate of int * float32 * nativeint * float32 * int * int * float32 * float32 * int * float32 * int -> unit
                    ass.BeginCall(11)
                    ass.PushArg 10
                    ass.PushArg 9.0f
                    ass.PushArg 8
                    ass.PushArg 7.0f
                    ass.PushArg 6.0f
                    ass.PushArg 5
                    ass.PushArg 4
                    ass.PushArg 3.0f
                    ass.PushArg 2n
                    ass.PushArg 1.0f
                    ass.PushArg 0
                    ass.Call pAction

                    
                    ass.BeginCall(11)
                    ass.PushArg 20
                    ass.PushArg 19.0f
                    ass.PushArg 18
                    ass.PushArg 17.0f
                    ass.PushArg 16.0f
                    ass.PushArg 15
                    ass.PushArg 14
                    ass.PushArg 13.0f
                    ass.PushArg 12n
                    ass.PushArg 11.0f
                    ass.PushArg 10
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                )

            let ptr = JitMem.Alloc(nativeint code.Length)
            JitMem.Copy(code, ptr)

            let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
            action.Invoke()

            JitMem.Free(ptr, nativeint code.Length)
            Expect.equal (Seq.toList values) [(0,1.0f,2n,3.0f,4,5,6.0f,7.0f,8,9.0f,10); (10,11.0f,12n,13.0f,14,15,16.0f,17.0f,18,19.0f,20)] "inconsistent calls"
        }
    

        test "Store" {
            init()
            do
                let dst = [| 1 |]
                use pDst = fixed dst

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        let r0 = ass.ReturnRegister
                        let r1 = ass.ArgumentRegisters.[1]

                        ass.BeginFunction()
                        ass.Set(r0, 123)
                        ass.Set(r1, NativePtr.toNativeInt pDst)
                        ass.Store(r1, r0, false)

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                try
                    JitMem.Copy(code, ptr)

                    let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)

                    action.Invoke()
                    Expect.equal dst.[0] 123 "store failed"
                finally
                    JitMem.Free(ptr, nativeint code.Length)
        }
    
        test "Add" {
            init()
            do
                let dst = [| 1 |]
                use pDst = fixed dst

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        let r0 = ass.ReturnRegister
                        let r1 = ass.ArgumentRegisters.[1]

                        ass.BeginFunction()
                        ass.Set(r0, 123)
                        ass.Set(r1, 321)
                        ass.AddInt(r0, r1, false)

                        ass.Set(r1, NativePtr.toNativeInt pDst)
                        ass.Store(r1, r0, false)

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                try
                    JitMem.Copy(code, ptr)

                    let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)

                    action.Invoke()
                    Expect.equal dst.[0] 444 "add failed"
                finally
                    JitMem.Free(ptr, nativeint code.Length)
        }

        test "Mul" {
            init()
            do
                let dst = [| 1 |]
                use pDst = fixed dst

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        let r0 = ass.ReturnRegister
                        let r1 = ass.ArgumentRegisters.[1]

                        ass.BeginFunction()
                        ass.Set(r0, 123)
                        ass.Set(r1, 321)
                        ass.MulInt(r0, r1, false)

                        ass.Set(r1, NativePtr.toNativeInt pDst)
                        ass.Store(r1, r0, false)

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                try
                    JitMem.Copy(code, ptr)

                    let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)

                    action.Invoke()
                    Expect.equal dst.[0] 39483 "mul failed"
                finally
                    JitMem.Free(ptr, nativeint code.Length)
        }

        test "Copy" {
            init()
            do
                let src = [| 123 |]
                let dst = [| 0 |]
                use pSrc = fixed src
                use pDst = fixed dst

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        ass.BeginFunction()
                        
                        ass.Copy(NativePtr.toNativeInt pSrc, NativePtr.toNativeInt pDst, false)

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                try
                    JitMem.Copy(code, ptr)

                    let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)

                    action.Invoke()
                    Expect.equal dst.[0] 123 "copy failed"
                finally
                    JitMem.Free(ptr, nativeint code.Length)
        }


        test "PushPop" {
            init()
            do
                let dst = [| 0 |]
                use pDst = fixed dst

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        let r = ass.ReturnRegister
                        let r1 = ass.ArgumentRegisters.[1]

                        ass.BeginFunction()
                        
                        ass.Set(r, 123)
                        ass.Push r
                        ass.Set(r, 321)
                        ass.Pop r

                        ass.Set(r1, NativePtr.toNativeInt pDst)
                        ass.Store(r1, r, false)

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                try
                    JitMem.Copy(code, ptr)

                    let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)

                    action.Invoke()
                    Expect.equal dst.[0] 123 "push/pop failed"
                finally
                    JitMem.Free(ptr, nativeint code.Length)
        }

        test "CalleeSaved" {

            init()
            do
                let code =
                    AssemblerStream.toMemory (fun ass ->
                        ass.BeginFunction()
                        ass.Set(ass.CalleeSavedRegisters.[0], 10)
                        ass.Set(ass.CalleeSavedRegisters.[1], 11)
                        ass.Set(ass.CalleeSavedRegisters.[2], 12)
                        ass.Set(ass.CalleeSavedRegisters.[3], 13)
                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                try
                    JitMem.Copy(code, ptr)
                    let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
                    action.Invoke()
                finally
                    JitMem.Free(ptr, nativeint code.Length)
        }

        test "BackwardJump" {

            init()
            do
                use ptr = fixed [| 0 |]
                let code =
                    AssemblerStream.toMemory (fun ass ->
                        ass.BeginFunction()
                        
                        
                        let l = ass.NewLabel()
                        ass.Set(ass.ReturnRegister, 0)
                        ass.Mark l
                        ass.Set(ass.ArgumentRegisters.[1], 1)
                        ass.AddInt(ass.ReturnRegister, ass.ArgumentRegisters.[1], false)
                        ass.Set(ass.ArgumentRegisters.[1], NativePtr.toNativeInt ptr)
                        ass.Store(ass.ArgumentRegisters.[1], ass.ReturnRegister, false)
                        ass.Cmp(NativePtr.toNativeInt ptr, 10)
                        ass.Jump(JumpCondition.Less, l)
                        ass.EndFunction()
                        ass.Ret()
                    )

                let mem = JitMem.Alloc(nativeint code.Length)
                try
                    JitMem.Copy(code, mem)
                    let action = Marshal.GetDelegateForFunctionPointer<Action>(mem)
                    action.Invoke()
                    
                    Expect.equal (NativePtr.read ptr) 10 ""
                finally
                    JitMem.Free(mem, nativeint code.Length)
        }

        test "UnconditionalForwardJump" {
            init()
            do
                let values = System.Collections.Generic.List<int>()
                let action = IntAction values.Add
                let pAction = Marshal.GetFunctionPointerForDelegate action

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        ass.BeginFunction()

                        let l = ass.NewLabel()
                        ass.Jump(l)

                        ass.BeginCall(1)
                        ass.PushArg 123
                        ass.Call pAction
                        
                        ass.Mark l
                        ass.BeginCall(1)
                        ass.PushArg 321
                        ass.Call pAction

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                try
                    JitMem.Copy(code, ptr)

                    let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)

                    action.Invoke()
                    Expect.equal (Seq.toList values) [321] "inconsistent calls"
                    values.Clear()

                finally
                    JitMem.Free(ptr, nativeint code.Length)
        }

        test "ConditionalForwardJump" {
            init()
            do
                let values = System.Collections.Generic.List<int>()
                let action = IntAction values.Add
                let pAction = Marshal.GetFunctionPointerForDelegate action

                let cond = [| 1 |]
                use pCond = fixed cond

                let code =
                    AssemblerStream.toMemory (fun ass ->
                        ass.BeginFunction()

                        let l = ass.NewLabel()
                        ass.Cmp(NativePtr.toNativeInt pCond, 0)
                        ass.Jump(JumpCondition.Equal, l)

                        ass.BeginCall(1)
                        ass.PushArg 123
                        ass.Call pAction
                        
                        ass.Mark l
                        ass.BeginCall(1)
                        ass.PushArg 321
                        ass.Call pAction

                        ass.EndFunction()
                        ass.Ret()
                    )

                let ptr = JitMem.Alloc(nativeint code.Length)
                try
                    JitMem.Copy(code, ptr)

                    let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)

                    action.Invoke()
                    Expect.equal (Seq.toList values) [123; 321] "inconsistent calls"
                    values.Clear()

                    cond.[0] <- 0
                    action.Invoke()
                    Expect.equal (Seq.toList values) [321] "inconsistent calls"
                    values.Clear()
                finally
                    JitMem.Free(ptr, nativeint code.Length)
        }
    ]

type Operation<'a> =
    | Insert of index : int * value : 'a
    | Remove of index : int


let createProgram() =

    let values = System.Collections.Generic.List<int>()
    let callback (value : int) =
        values.Add value

    let del = IntAction callback
    let fPtr = Marshal.GetFunctionPointerForDelegate del

    let compile (value : int) (ass : IAssemblerStream) =
        ass.BeginCall(1)
        ass.PushArg value
        ass.Call fPtr

    let prog = new FragmentProgram<int>(compile)

    let run() =
        prog.Run()
        let res = Seq.toList values
        values.Clear()
        res
    let gc = GCHandle.Alloc del
    prog, run, { new IDisposable with member x.Dispose() = gc.Free() }


module Delegate =
    open System.Reflection
    open System.Reflection.Emit
    
    module internal DelegateBuilder = 
        let assembly = new AssemblyName();
        assembly.Version <- new Version(1, 0, 0, 0);
        assembly.Name <- "ReflectionEmitDelegateTest";
        let assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assembly, AssemblyBuilderAccess.Run)
        let modbuilder = assemblyBuilder.DefineDynamicModule("MyModule")

        let mutable delegateIndex = 0
        let buildDelegate (argTypes : Type[]) (ret : Type) =
            let delegateIndex = System.Threading.Interlocked.Increment(&delegateIndex)
            let name = sprintf "DelegateType%d" delegateIndex

            let typeBuilder = modbuilder.DefineType(
                                name, 
                                TypeAttributes.Class ||| TypeAttributes.Public ||| TypeAttributes.Sealed, 
                                typeof<System.MulticastDelegate>)

            let constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [| typeof<obj>; typeof<System.IntPtr> |])
            constructorBuilder.SetImplementationFlags(MethodImplAttributes.Runtime ||| MethodImplAttributes.Managed);

            let methodBuilder = typeBuilder.DefineMethod("Invoke", MethodAttributes.Public, ret, argTypes);
            methodBuilder.SetImplementationFlags(MethodImplAttributes.Runtime ||| MethodImplAttributes.Managed);

            typeBuilder.CreateType()

    let createNAry (args : Type[]) (action : obj[] -> 'b) =
        let n = args.Length

        let ret = 
            if typeof<'b> = typeof<unit> then typeof<System.Void>
            else typeof<'b>


        let typ =   
            DelegateBuilder.buildDelegate args ret

        let bType = 
            DelegateBuilder.modbuilder.DefineType(typ.Name + "Dispatcher", TypeAttributes.Class ||| TypeAttributes.Public ||| TypeAttributes.Sealed, typeof<obj>)

        let f = bType.DefineField("action", typeof<obj[] -> 'b>, FieldAttributes.Public)

        let bCtor = bType.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [| typeof<obj[] -> 'b> |])
        let il = bCtor.GetILGenerator()
        il.Emit(OpCodes.Ldarg_0)
        il.Emit(OpCodes.Ldarg_1)
        il.Emit(OpCodes.Stfld, f)
        il.Emit(OpCodes.Ret)

        let bRun = bType.DefineMethod("Run", MethodAttributes.Final ||| MethodAttributes.Public, ret, args)
        let il = bRun.GetILGenerator()
        let arr = il.DeclareLocal typeof<obj[]>
        il.Emit(OpCodes.Ldc_I4, n)
        il.Emit(OpCodes.Newarr, typeof<obj>)
        il.Emit(OpCodes.Stloc, arr)

        for i in 0 .. n - 1 do
            let t = args.[i]
            il.Emit(OpCodes.Ldloc, arr)
            il.Emit(OpCodes.Ldc_I4, i)
            il.Emit(OpCodes.Ldarg, i+1)
            if t.IsValueType then il.Emit(OpCodes.Box, t)
            il.Emit(OpCodes.Stelem, typeof<obj>)

        il.Emit(OpCodes.Ldarg_0)
        il.Emit(OpCodes.Ldfld, f)
        il.Emit(OpCodes.Ldloc, arr)
        il.EmitCall(OpCodes.Callvirt, typeof<obj[] -> 'b>.GetMethod "Invoke", null)
        if typeof<'b> = typeof<unit> then il.Emit(OpCodes.Pop)
        il.Emit(OpCodes.Ret)
        
        let tt = bType.CreateType()
        let o = Activator.CreateInstance(tt, action)
        Delegate.CreateDelegate(typ, o, "Run")

[<Struct>]
type RandomInt(value : int) =
    member x.Value = value

type Argument =
    | Int of value : int
    | IntRef of int
    | Float of NormalFloat
    | FloatRef of NormalFloat

    member x.Type =
        match x with
        | Int _ | IntRef _ -> typeof<int>
        | Float _ | FloatRef _ -> typeof<float32>


type ArbitraryModifiers() =
    static let random = System.Random()

    static member Random = random

    static member RandomInt() : Arbitrary<_> =
        Gen.fresh (fun () -> random.Next(2048) - 1024)
        |> Gen.map RandomInt
        |> Arb.fromGen
        

[<Tests>]
let fragmentTests =
    testList "FragmentTests" [

        test "SimpleAddRemove" {
            init()
            let (prog, run, disp) = createProgram()
            use __ = disp
            use prog = prog
            // Prepend
            let a = prog.InsertAfter(null, 1)
            Expect.equal (run()) [1] "inconsistent result"

            // InsertAfter
            let b = prog.InsertAfter(a, 2)
            Expect.equal (run()) [1;2] "inconsistent result"

            // InsertBefore
            let c = prog.InsertBefore(a, 0)
            Expect.equal (run()) [0;1;2] "inconsistent result"
            
            // Remove
            a.Dispose()
            Expect.equal (run()) [0;2] "inconsistent result"

            // Dummy Insert & Remove
            let d = prog.InsertAfter(c, 100)
            d.Dispose()
            Expect.equal (run()) [0;2] "inconsistent result"

            // InsertAfter disposed
            try 
                prog.InsertAfter(a, 10) |> ignore
                failwith "insert after disposed should fail"
            with 
                | :? ObjectDisposedException -> ()
                | e -> failwithf "insert after disposed should fail with ObjectDisposedException but was: %A" e
            Expect.equal (run()) [0;2] "inconsistent result"

            // InsertBefore disposed
            try 
                prog.InsertBefore(a, 10) |> ignore
                failwith "insert after disposed should fail"
            with 
                | :? ObjectDisposedException -> ()
                | e -> failwithf "insert after disposed should fail with ObjectDisposedException but was: %A" e
            Expect.equal (run()) [0;2] "inconsistent result"

            // Append
            let e = prog.InsertBefore(null, 3)
            Expect.equal (run()) [0;2;3] "inconsistent result"

            // Remove First
            c.Dispose()
            Expect.equal (run()) [2;3] "inconsistent result"
            
            // Remove Last
            e.Dispose()
            Expect.equal (run()) [2] "inconsistent result"
            
            // Remove All
            b.Dispose()
            Expect.equal (run()) [] "inconsistent result"


        }

        let config = { FsCheckConfig.defaultConfig with maxTest = 1000; endSize = 1000; arbitrary = [ typeof<ArbitraryModifiers> ] }
        testPropertyWithConfig config "InsertRemove" (fun (NonEmptyArray (arr : Operation<RandomInt>[])) ->
            init()
            let mutable fragments = IndexList.empty
            let (prog, run, disp) = createProgram()
            use __ = disp
            use prog = prog
            for op in arr do
                let cnt = fragments.Count
                match op with
                | Insert(index, value) ->
                    if cnt = 0 then 
                        let f = prog.InsertAfter(null, value.Value)
                        fragments <- IndexList.single f
                    else
                        let idx = ((index % cnt) + cnt) % cnt
                        let ref = fragments.[idx]
                        let f = prog.InsertBefore(ref, value.Value)
                        fragments <- IndexList.insertAt idx f fragments
                | Remove(index) ->
                    if cnt > 0 then 
                        let idx = ((index % cnt) + cnt) % cnt
                        let f = fragments.[idx]
                        f.Dispose()
                        fragments <- IndexList.removeAt idx fragments
                    else
                        let value = ArbitraryModifiers.Random.Next(2048) - 1024
                        let f = prog.InsertAfter(null, value)
                        fragments <- IndexList.single f

                let actual = run()
                let expected = fragments |> Seq.toList |> List.map (fun f -> f.Tag)
                Expect.equal actual expected "inconsistent result"
        )

        let config = { FsCheckConfig.defaultConfig with maxTest = 200; endSize = 1000; arbitrary = [ typeof<ArbitraryModifiers> ] }
        testPropertyWithConfig config "NAryCalls" (fun (NonEmptyArray (arr : Operation<option<Argument> * option<Argument> * option<Argument> * option<Argument> * option<Argument> * list<Argument>>[])) ->
            init()
            let calls = System.Collections.Generic.List()
            let delegates = Dict<list<Type>, PinnedDelegate>()

            let mem = Marshal.AllocHGlobal (8 <<< 20)
            try
                let mutable current = mem

                let compile (args : list<Argument>) (ass : IAssemblerStream)=
                    let types = args |> List.map (fun a -> a.Type)
                    let del = 
                        delegates.GetOrCreate(types, fun types ->
                            let del = 
                                Delegate.createNAry (List.toArray types) (fun args ->
                                    calls.Add args
                                )
                            Marshal.PinDelegate del
                        )

                    ass.BeginCall(List.length args)
                    for a in List.rev args do
                        match a with
                        | Int v -> ass.PushArg v
                        | IntRef v -> 
                            NativePtr.write (NativePtr.ofNativeInt<int> current) v
                            ass.PushIntArg current
                            current <- current + 8n
                        | Float (NormalFloat v) -> ass.PushArg (float32 v)
                        | FloatRef (NormalFloat v)-> 
                            NativePtr.write (NativePtr.ofNativeInt<float32> current) (float32 v)
                            ass.PushFloatArg current
                            current <- current + 8n
                    ass.Call(del.Pointer)

                use prog = new FragmentProgram<_>(compile)
                let run() =
                    prog.Run()
                    let result = calls |> Seq.map (fun a -> Array.toList a) |> Seq.toList
                    calls.Clear()
                    result

                let toList (a, b, c, d, e, f) =
                    Option.toList a @ Option.toList b @ Option.toList c @ Option.toList d @ Option.toList e @ f

                let arr =
                    arr |> Array.map (function
                        | Insert(index, value) -> Insert(index, toList value)
                        | Remove index -> Remove index
                    )

                let mutable fragments = IndexList.empty
                for op in arr do
                    let cnt = fragments.Count
                    match op with
                    | Insert(index, value) ->
                        if cnt = 0 then 
                            let f = prog.InsertAfter(null, value)
                            fragments <- IndexList.single f
                        else
                            let idx = ((index % cnt) + cnt) % cnt
                            let ref = fragments.[idx]
                            let f = prog.InsertBefore(ref, value)
                            fragments <- IndexList.insertAt idx f fragments
                    | Remove(index) ->
                        if cnt > 0 then 
                            let idx = ((index % cnt) + cnt) % cnt
                            let f = fragments.[idx]
                            f.Dispose()
                            fragments <- IndexList.removeAt idx fragments


                    let toObj (a : Argument) =
                        match a with
                        | Int v -> v :> obj
                        | IntRef v -> v :> obj
                        | Float (NormalFloat v) -> float32 v :> obj
                        | FloatRef (NormalFloat v) -> float32 v :> obj

                    let actual = run()
                    let expected = fragments |> Seq.toList |> List.map (fun f -> f.Tag |> List.map toObj)
                    Expect.equal actual expected "inconsistent result"
                ()
            finally
                Marshal.FreeHGlobal mem
        


        )
    ]