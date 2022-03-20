# Aardvark.Assembler

[![Test](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/test.yml/badge.svg)](https://github.com/aardvark-community/aardvark.assembler/actions/workflows/test.yml)

Provides APIs for:
* simple native-code generation via `IAssemblerStream`
* *Fragment* based API for dynamic programs via `FragmentProgram<'a>`
* `AdaptiveFragmentProgram` for automatically grouping and compiling `aset<'a>`
