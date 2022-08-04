#ifndef _EXECUTABLE_H
#define _EXECUTABLE_H

#include <stdio.h>
#include <stdlib.h>
#include <sys/mman.h>
#include <string.h>
#include <errno.h>
#include <unistd.h>
#include <pthread.h>
#include <stdint.h>
#ifdef __APPLE__
#include <libkern/OSCacheControl.h>
#elif WIN32
#include <windows.h>
#include <memoryapi.h>
#endif


#ifdef __APPLE__
#define DllExport(t) extern "C" __attribute__((visibility("default"))) t
#elif __GNUC__
#define DllExport(t) extern "C" __attribute__((visibility("default"))) t
#else
#define DllExport(t) extern "C"  __declspec( dllexport ) t __cdecl
#endif

#endif