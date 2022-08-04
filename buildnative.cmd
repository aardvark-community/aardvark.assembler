@echo off
IF EXIST src\jitmem\build RMDIR /S /Q src\jitmem\build
cmake -DCMAKE_BUILD_TYPE=Release -S src\jitmem -B src\jitmem\build 
cmake --build src\jitmem\build  --config Release
cmake --install src\jitmem\build  --config Release

