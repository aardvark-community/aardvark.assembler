#include "Executable.h"

DllExport(uint32_t) epageSize() 
{
    #if _WIN32
    SYSTEM_INFO si;
    GetSystemInfo(&si);
    return (uint32_t)si.dwPageSize; 
    #else
    return (uint32_t)sysconf(_SC_PAGESIZE);
    #endif
}

DllExport(void*) ealloc(size_t size)
{
    #if _WIN32
    void* ptr = VirtualAlloc(NULL, size, MEM_COMMIT, PAGE_EXECUTE_READWRITE);
    return ptr;
    #elif __APPLE__
    return mmap(NULL, size, PROT_READ | PROT_WRITE | PROT_EXEC, MAP_ANON | MAP_PRIVATE | MAP_JIT, -1, 0);
    #else
    return mmap(NULL, size, PROT_READ | PROT_WRITE | PROT_EXEC, MAP_ANON | MAP_PRIVATE, -1, 0);
    #endif
}

DllExport(void) efree(void* ptr, size_t size)
{
    #if _WIN32
    VirtualFree(ptr, 0, MEM_RELEASE); // returns 0 if error
    #else
    munmap(ptr, size); // returns 0 if success
    #endif
}

DllExport(void) ecpy(void* dst, void* src, size_t size)
{
    #if __APPLE__
    pthread_jit_write_protect_np(0);
    memcpy(dst, src, size);
    sys_icache_invalidate(dst, size);
    pthread_jit_write_protect_np(1);
    #else
    memcpy(dst, src, size);
    #endif
}

DllExport(bool) eiswritable()
{
    #if _WIN32
    return true;
    #elif __APPLE__
    return false;
    #else
    return true;
    #endif
}