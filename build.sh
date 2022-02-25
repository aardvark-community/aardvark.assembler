#!/bin/sh
dotnet tool restore
dotnet paket restore
dotnet build Arm64.sln