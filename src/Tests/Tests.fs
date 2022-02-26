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


    ]

// let fragments =
//     testList "Fragment" [
//         test "basic" {
//             init()
//             let l = System.Collections.Generic.List<int>()
//             let action = 
//             use f = new FragmentProgram<int>()
//         }
//     ]