namespace Aardvark.Assembler

open Aardvark.Base
open System
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop

#nowarn "9"

/// Abstractions for handling executable memory
[<AbstractClass; Sealed>]
type JitMem private() =
    [<DllImport("jitmem")>]
    static extern bool eiswritable()

    [<DllImport("jitmem")>]
    static extern uint32 epageSize()

    [<DllImport("jitmem")>]
    static extern nativeint ealloc(unativeint size)
    
    [<DllImport("jitmem")>]
    static extern void efree(nativeint ptr, unativeint size)
    
    [<DllImport("jitmem")>]
    static extern void ecpy(nativeint dst, nativeint src, unativeint size)

    static let mutable pageSize = ref 0un

    static let isWriteable = eiswritable()

    /// Indicates whether the executable memory is directly writeable while being used for execution.
    static member IsWritable = isWriteable

    /// The size of a page in bytes.
    static member PageSize = 
        lock pageSize (fun () ->
            if pageSize.Value = 0un then
                let s = epageSize() |> unativeint
                pageSize.Value <- s
                s
            else
                pageSize.Value
        )

    /// Allocates executable memory with (at least) the given size.
    static member Alloc(size : nativeint) =
        if size <= 0n then
            0n
        else
            let ps = JitMem.PageSize
            let effectiveSize =
                if unativeint size % ps = 0un then unativeint size
                else (1un + unativeint size / ps) * ps
            ealloc effectiveSize

    /// Frees executable memory. The size shall be idenitcal to the one used in `Alloc`.
    static member Free(ptr : nativeint, size : nativeint) =
        if size > 0n then
            let ps = JitMem.PageSize
            let effectiveSize =
                if unativeint size % ps = 0un then unativeint size
                else (1un + unativeint size / ps) * ps
            efree(ptr, effectiveSize)

    /// Copies the source-pointer to the executable memory-pointer.
    static member Copy(src : nativeint, dst : nativeint, size : nativeint) =
        if size > 0n then
            if isWriteable then
                let src = System.Span<byte>(NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> src), int size)
                let dst = System.Span<byte>(NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> dst), int size)
                src.CopyTo dst
            else 
                ecpy(dst, src, unativeint size)

    /// Copies the given `Memory<byte>` to the executable memory-pointer.
    static member Copy(src : Memory<byte>, dst : nativeint) =
        if src.Length > 0 then
            use hSrc = src.Pin()
            let pSrc = hSrc.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
            JitMem.Copy(pSrc, dst, nativeint src.Length)
            
    /// Copies the given `Memory<byte>` to the executable memory-pointer.
    static member Copy(src : Memory<byte>, dst : managedptr) =
        if src.Length > 0 then
            if nativeint src.Length <> dst.Size then failwithf "inconsitent copy-size: %d vs %d" src.Length dst.Size
            dst.Use (fun pDst ->
                use hSrc = src.Pin()
                let pSrc = hSrc.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
                JitMem.Copy(pSrc, pDst, dst.Size)
            )
            
    /// Copies the given `Memory<byte>` to the executable memory-pointer with a given offset.
    static member Copy(src : Memory<byte>, dst : managedptr, dstOffset : nativeint) =
        if src.Length > 0 then
            if dstOffset + nativeint src.Length > dst.Size then failwithf "copy range exceeds dst size: %d + %d vs %d" dstOffset src.Length dst.Size
            dst.Use (fun pDst ->
                use hSrc = src.Pin()
                let pSrc = hSrc.Pointer |> NativePtr.ofVoidPtr<byte> |> NativePtr.toNativeInt
                JitMem.Copy(pSrc, pDst + dstOffset, nativeint src.Length)
            )
