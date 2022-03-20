(*** condition: prepare ***)
#I __SOURCE_DIRECTORY__
#r "../bin/Debug/net6.0/FSharp.Data.Adaptive.dll"
#r "../bin/Debug/net6.0/Aardvark.Base.dll"
#r "../bin/Debug/net6.0/Aardvark.Base.FSharp.dll"
#r "../bin/Debug/net6.0/Aardvark.Base.Runtime.dll"
#r "../bin/Debug/net6.0/Aardvark.Assembler.dll"

open System
open System.IO
open System.Runtime.InteropServices
open Aardvark.Base.Runtime
open Aardvark.Base
open FSharp.Data.Adaptive
open Aardvark.Assembler
open Microsoft.FSharp.NativeInterop
IntrospectionProperties.CustomEntryAssembly <- typeof<JitMem>.Assembly
Environment.CurrentDirectory <- Path.Combine(__SOURCE_DIRECTORY__, "..", "bin", "Debug", "net6.0")
Aardvark.Init()


(**
# FragmentProgram

`FragmentProgram<'a>` represents a native procedure/function that can be modified using `Fragment<'a>`s. These
fragments represent code-snippets that are linked together using jump-instructions.

The program uses *tags* that need to be specified upon creation of each fragment which are the used by the `compile` function given in the constructor.

Conceptually there are two different **modes** for a FragmentProgram:

1. *differential* mode where each `compile` invocation also gets tag of its immediate predecessor.
2. *pure* (*non-differential*) mode where `compile` only gets the tag of the newly created fragment.

*)

let differential = 
    new FragmentProgram<int>(fun (prev : option<int>) (self : int) (stream : IAssemblerStream) -> 
        // `prev` is the tag of the previous fragment.
        // `self` is the tag of the current fragment.

        () // compile some code using the stream
    )

let pure = 
    new FragmentProgram<int>(fun (self : int) (stream : IAssemblerStream) -> 
        // `self` is the tag of the current fragment.

        () // compile some code using the stream
    )

(**
It allows flexible addition of these Fragments via methods like `InsertAfter`, `InsertBefore` and removal via
disposing the returned Fragments. 

*)


(** 
## Example
Here we setup a `FragmentProgram<int>` that simply calls `printf "%d "` on each input value.
*)

// we need to create a non-generic delegate here in order to get an unmanaged pointer.
type Del = delegate of int -> unit
let print = Del(printf "%d ") 
let fptr = Marshal.GetFunctionPointerForDelegate print

// for each `tag` we simply create a call to out print-function.
let compile (tag : int) (ass : IAssemblerStream) =
    ass.BeginCall 1
    ass.PushArg tag
    ass.Call fptr

let program = new FragmentProgram<int>(compile)

(** 
With the `FragmentProgram<int>` we can now easily append a call at its end with tag `1`. Running the program will then simply print `1`
 *)
let f1 = program.Append 1
program.Run()
(*** include-output ***)

(** 
`FragmentProgram<'a>` provides several methods for inserting new Fragments.
*)
let f0 = program.InsertBefore(f1, 0)
let f2 = program.InsertAfter(f1, 2)
let fn = program.Prepend -1
program.Run()
(*** include-output ***)


(**
Any `Fragment<'a>` can also be disposed, which removes it from the program.
*)
f0.Dispose()
program.Run()
(*** include-output ***)




(*** hide ***)
ignore print
program.Dispose()