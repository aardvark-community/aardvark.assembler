namespace Aardvark.Assembler

open System
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open Aardvark.Base
open Aardvark.Base.Runtime
open FSharp.Data.Adaptive

#nowarn "9"

/// An adaptive assembled program compiling all elements in the `set`.
/// Ordering is arbitrary in principle, but grouping is performed via the use of `project`. 
/// Note that `project` is used as key in a prefix-trie and all keys must be unique.
type AdaptiveFragmentProgram<'a> internal(differential : bool, set : aset<'a>, project : 'a -> list<obj>, compile : option<'a> -> 'a -> IAssemblerStream -> unit) =
    inherit AdaptiveObject()
        
    let mutable isDisposed = false
    let mutable trie = OrderMaintenanceTrie<obj, Fragment<'a>>()
    let mutable reader = set.GetReader()
    let mutable program = new FragmentProgram<'a>(differential, compile)

    /// Updates all internals in order to make the program to be runnable.
    member x.Update(token : AdaptiveToken) =
        x.EvaluateIfNeeded token () (fun token ->
            if isDisposed then raise <| System.ObjectDisposedException("AdaptiveFragmentProgram")
            let ops = reader.GetChanges token
            for op in ops do
                match op with
                | Add(_, v) ->
                    let ref = trie.AddOrUpdate(project v, function ValueSome old -> old | ValueNone -> null)

                    if isNull ref.Value then
                        // new fragment
                        let prev = match ref.Prev with | ValueSome p -> p.Value | ValueNone -> null
                        //let next = match ref.Next with | ValueSome n -> n.Value | ValueNone -> null

                        let self = program.InsertAfter(prev, v)
                        ref.Value <- self
                    else
                        Log.warn "[AdaptiveFragmentProgram] update should not be possible: is: %A should: %A" ref.Value.Tag v
                | Rem(_, v) ->
                    let key = project v
                    match trie.TryGetReference key with
                    | ValueSome self ->   
                        if not (isNull self.Value) then self.Value.Dispose()
                        trie.TryRemove key |> ignore
                    | ValueNone ->  
                        Log.warn "[AdaptiveFragmentProgram] removing non-existing fragment: %A" v

            program.Update()

        )

    /// Updates all internals and executes the program.
    member x.Run(token : AdaptiveToken) =
        lock x (fun () ->
            x.Update token
            program.Run()
        )

    /// Updates all internals in order to make the program to be runnable.
    member x.Run() = x.Run AdaptiveToken.Top

    /// Updates all internals and executes the program.
    member x.Update() = x.Update AdaptiveToken.Top

    /// Releases all internal resources associated with the program.
    member x.Dispose() =
        lock x (fun () ->
            if not isDisposed then
                isDisposed <- true
                trie.Clear()
                program.Dispose()
                reader <- Unchecked.defaultof<_>
        )


    /// Creates an adaptive program with "differential" compilation.
    new(set : aset<'a>, project : 'a -> list<obj>, compile : option<'a> -> 'a -> IAssemblerStream -> unit) =
        new AdaptiveFragmentProgram<'a>(true, set, project, compile)

    /// Creates an adaptive program with "non-differential" compilation.
    new(set : aset<'a>, project : 'a -> list<obj>, compile : 'a -> IAssemblerStream -> unit) =
        new AdaptiveFragmentProgram<'a>(false, set, project, fun _ v -> compile v)