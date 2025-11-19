# Aardvark.Assembler

[![Build](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/build.yml/badge.svg)](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/build.yml)
[![Publish](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/publish.yml/badge.svg)](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/publish.yml)
[![Version](https://img.shields.io/nuget/vpre/aardvark.assembler)](https://www.nuget.org/packages/aardvark.assembler/)
[![Downloads](https://img.shields.io/nuget/dt/aardvark.assembler)](https://www.nuget.org/packages/aardvark.assembler/)

Provides APIs for:
* simple native-code generation via `IAssemblerStream`
* *Fragment* based API for dynamic programs via `FragmentProgram<'a>`
* `AdaptiveFragmentProgram` for automatically grouping and compiling `aset<'a>`

## Building
1. (Optional) Run `buildnative.(cmd|sh)` to build the native library for your platform (CMake and C++ tools required).
1. Run `build.(cmd|sh)`.

## Testing
There are a number of tests that check whether the Assembler works on your platform / architecture, which can be run via `dotnet test`.

## Deploying
1. Build the native libraries (if required).
    * Commit your changes and manually trigger the [Build Native](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/build-native.yml) workflow. The CI builds the native library for all supported platforms, run tests, and create a pull request with the updated files.
    * Merge the pull request.
2. Update `RELEASE_NOTES.md` and commit the changes to start the [Publish](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/publish.yml) workflow. This workflow creates and deploys packages to GitHub and NuGet.
