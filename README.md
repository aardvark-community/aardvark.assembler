# Aardvark.Assembler

[![Publish](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/publish.yml/badge.svg)](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/publish.yml)
[![Version](https://img.shields.io/nuget/vpre/aardvark.assembler)](https://www.nuget.org/packages/aardvark.assembler/)
[![Downloads](https://img.shields.io/nuget/dt/aardvark.assembler)](https://www.nuget.org/packages/aardvark.assembler/)

Provides APIs for:
* simple native-code generation via `IAssemblerStream`
* *Fragment* based API for dynamic programs via `FragmentProgram<'a>`
* `AdaptiveFragmentProgram` for automatically grouping and compiling `aset<'a>`

## Building

* run `buildnative.(cmd|sh)` to build the native dependency (CMake and C++ tools required)
  alternatively you can copy the content of the `prebuilt` folder to a new folder `libs`
* run `build.(cmd|sh)` 

## Testing

There is a number of tests that check whether or not the Assembler works on your platform/architecture which can be run via `dotnet test`
