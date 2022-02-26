module AssemblerTests

open System
open Aardvark.Base
open Expecto
open Aardvark.Assembler
open Microsoft.FSharp.NativeInterop
open System.Runtime.InteropServices
open FsCheck

#nowarn "9"

type IntAction = delegate of int -> unit
type NativeIntAction = delegate of nativeint -> unit
type Float32Action = delegate of float32 -> unit
type FloatAction = delegate of float -> unit
type ManyArgs = delegate of int * int * int * int * int * int * int * int * int * int * int -> unit
type ManyFloats = delegate of float32 * float32 * float32 * float32 * float32 * float32 * float32 * float32 * float32 * float32 * float32 -> unit



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
                use ms = new SystemMemoryStream()
                use ass = AssemblerStream.create ms

                ass.BeginFunction()
                ass.EndFunction()
                ass.Ret()
                ms.ToMemory()

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
                use ms = new SystemMemoryStream()
                use ass = AssemblerStream.create ms

                ass.BeginFunction()

                ass.BeginCall(1)
                ass.PushArg 123
                ass.Call pAction
                
                ass.BeginCall(1)
                ass.PushArg 321
                ass.Call pAction

                ass.EndFunction()
                ass.Ret()
                ms.ToMemory()

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
                    use ms = new SystemMemoryStream()
                    use ass = AssemblerStream.create ms

                    ass.BeginFunction()

                    ass.BeginCall(1)
                    ass.PushIntArg (NativePtr.toNativeInt mem)
                    ass.Call pAction
                    
                    ass.BeginCall(1)
                    ass.PushIntArg (NativePtr.toNativeInt (NativePtr.add mem 1))
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                    ms.ToMemory()

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
                use ms = new SystemMemoryStream()
                use ass = AssemblerStream.create ms

                ass.BeginFunction()

                ass.BeginCall(1)
                ass.PushArg 123n
                ass.Call pAction
                
                ass.BeginCall(1)
                ass.PushArg 321n
                ass.Call pAction

                ass.EndFunction()
                ass.Ret()
                ms.ToMemory()

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
                    use ms = new SystemMemoryStream()
                    use ass = AssemblerStream.create ms

                    ass.BeginFunction()

                    ass.BeginCall(1)
                    ass.PushPtrArg (NativePtr.toNativeInt mem)
                    ass.Call pAction
                    
                    ass.BeginCall(1)
                    ass.PushPtrArg (NativePtr.toNativeInt (NativePtr.add mem 1))
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                    ms.ToMemory()

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
                use ms = new SystemMemoryStream()
                use ass = AssemblerStream.create ms

                ass.BeginFunction()

                ass.BeginCall(1)
                ass.PushArg 1.23f
                ass.Call pAction
                
                ass.BeginCall(1)
                ass.PushArg 3.21f
                ass.Call pAction

                ass.EndFunction()
                ass.Ret()
                ms.ToMemory()

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
                    use ms = new SystemMemoryStream()
                    use ass = AssemblerStream.create ms

                    ass.BeginFunction()

                    ass.BeginCall(1)
                    ass.PushFloatArg (NativePtr.toNativeInt mem)
                    ass.Call pAction
                    
                    ass.BeginCall(1)
                    ass.PushFloatArg (NativePtr.toNativeInt (NativePtr.add mem 1))
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                    ms.ToMemory()

                let ptr = JitMem.Alloc(nativeint code.Length)
                JitMem.Copy(code, ptr)

                let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
                action.Invoke()

                JitMem.Free(ptr, nativeint code.Length)
                Expect.equal (Seq.toList values) [1.23f; 3.21f] "inconsistent calls"
        }

        // test "DirectFloat64" {
        //     init()

        //     let values = System.Collections.Generic.List<float>()
        //     let action = FloatAction values.Add
        //     let pAction = Marshal.GetFunctionPointerForDelegate action

        //     let code =
        //         use ms = new SystemMemoryStream()
        //         use ass = AssemblerStream.create ms

        //         ass.BeginFunction()

        //         ass.BeginCall(1)
        //         ass.PushArg 1.23
        //         ass.Call pAction
                
        //         ass.BeginCall(1)
        //         ass.PushArg 3.21
        //         ass.Call pAction

        //         ass.EndFunction()
        //         ass.Ret()
        //         ms.ToMemory()

        //     let ptr = JitMem.Alloc(nativeint code.Length)
        //     JitMem.Copy(code, ptr)

        //     let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
        //     action.Invoke()

        //     JitMem.Free(ptr, nativeint code.Length)
        //     Expect.equal (Seq.toList values) [1.23; 3.21] "inconsistent calls"
        // }
        
        test "IndirectFloat64" {
            init()
            do
                let values = System.Collections.Generic.List<float>()
                let action = FloatAction values.Add
                let pAction = Marshal.GetFunctionPointerForDelegate action

                use mem = fixed [| 1.23; 3.21 |]

                let code =
                    use ms = new SystemMemoryStream()
                    use ass = AssemblerStream.create ms

                    ass.BeginFunction()

                    ass.BeginCall(1)
                    ass.PushDoubleArg (NativePtr.toNativeInt mem)
                    ass.Call pAction
                    
                    ass.BeginCall(1)
                    ass.PushDoubleArg (NativePtr.toNativeInt (NativePtr.add mem 1))
                    ass.Call pAction

                    ass.EndFunction()
                    ass.Ret()
                    ms.ToMemory()

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
                use ms = new SystemMemoryStream()
                use ass = AssemblerStream.create ms

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
                ms.ToMemory()

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
                use ms = new SystemMemoryStream()
                use ass = AssemblerStream.create ms

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
                ms.ToMemory()

            let ptr = JitMem.Alloc(nativeint code.Length)
            JitMem.Copy(code, ptr)

            let action = Marshal.GetDelegateForFunctionPointer<Action>(ptr)
            action.Invoke()

            JitMem.Free(ptr, nativeint code.Length)
            Expect.equal (Seq.toList values) [(0.0f,1.0f,2.0f,3.0f,4.0f,5.0f,6.0f,7.0f,8.0f,9.0f,10.0f); (10.0f,11.0f,12.0f,13.0f,14.0f,15.0f,16.0f,17.0f,18.0f,19.0f,20.0f)] "inconsistent calls"
        }
    ]
