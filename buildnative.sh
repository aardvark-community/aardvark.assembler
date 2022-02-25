
OS=`uname -s`

ARCH=""
ARCH_FLAGS=""

a="/$0"; a=${a%/*}; a=${a#/}; a=${a:-.}; BASEDIR=$(cd "$a"; pwd)

if [ "$OS" = "Darwin" ];
then
    echo "MacOS"
    if [ "$1" = "x86_64" ]; then
        ARCH="x86_64"
    elif [ "$1" = "arm64" ]; then
        ARCH="arm64"
    else
        ARCH=`uname -m | tail -1`
    fi

    ARCH_FLAGS="-DCMAKE_OSX_ARCHITECTURES=$ARCH"

else
    echo "Linux"
fi
echo $ARCH



rm -dfr src/jitmem/build
cmake -S src/jitmem/ -B src/jitmem/build $ARCH_FLAGS \
    -DCMAKE_BUILD_TYPE=Release

cmake --build src/jitmem/build  --config Release
cmake --install src/jitmem/build --config Release