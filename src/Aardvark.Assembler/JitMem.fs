namespace Aardvark.Assembler

open Aardvark.Base
open Aardvark.Base.Runtime
open System
open System.Runtime.InteropServices
open System.Runtime.CompilerServices
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
            if true || RuntimeInformation.IsOSPlatform OSPlatform.OSX then
                let ps = JitMem.PageSize
                let effectiveSize =
                    if unativeint size % ps = 0un then unativeint size
                    else (1un + unativeint size / ps) * ps
                ealloc effectiveSize
            else
                Aardvark.Base.ExecutableMemory.alloc size

    /// Frees executable memory. The size shall be idenitcal to the one used in `Alloc`.
    static member Free(ptr : nativeint, size : nativeint) =
        if size > 0n then
            if true || RuntimeInformation.IsOSPlatform OSPlatform.OSX then
                let ps = JitMem.PageSize
                let effectiveSize =
                    if unativeint size % ps = 0un then unativeint size
                    else (1un + unativeint size / ps) * ps
                efree(ptr, effectiveSize)
            else
                Aardvark.Base.ExecutableMemory.free ptr size

    /// Copies the source-pointer to the executable memory-pointer.
    static member Copy(src : nativeint, dst : nativeint, size : nativeint) =
        if size > 0n then
            if true || RuntimeInformation.IsOSPlatform OSPlatform.OSX then
                ecpy(dst, src, unativeint size)
            else
                let vSrc = NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> src)
                let vDst = NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> dst)
                let sSrc = System.Span<byte>(vSrc, int size)
                let sDst = System.Span<byte>(vDst, int size)
                sSrc.CopyTo(sDst)

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
