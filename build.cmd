@echo off
dotnet tool restore
dotnet paket restore
dotnet build Arm64.sln