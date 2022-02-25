module AssemblerTests

open System
open Aardvark.Base
open Expecto
open Aardvark.Assembler
open System.Runtime.InteropServices
open FsCheck

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

    ]