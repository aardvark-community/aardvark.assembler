
rm -dfr src/jitmem/build
cmake -S src/jitmem/ -B src/jitmem/build $ARCH_FLAGS \
    -DCMAKE_BUILD_TYPE=Release

cmake --build src/jitmem/build --config Release
cmake --install src/jitmem/build --config Release