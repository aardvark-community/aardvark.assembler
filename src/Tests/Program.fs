open Expecto.Tests
open AssemblerTests

[<EntryPoint>]
let main argv = 
    //runTestsInAssemblyWithCLIArgs [] argv

    let del = 
        Delegate.createNAry [| typeof<int>; typeof<float32>; typeof<int> |] (fun (l : obj[]) ->
            printfn "%0A" l
            1
        )
    //let ptr = Marshal.GetFunctionPointerForDelegate del
    del.DynamicInvoke [| 1 :> obj; 2.0f :> obj; 3 :> obj|] |> ignore

    0
