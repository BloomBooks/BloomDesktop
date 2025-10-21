#!/bin/sh

source $(dirname $0)/build-config.sh

# Install the necessary build tools.

pacman -S git autotools --noconfirm --needed
pacman -S mingw-w64-cross-toolchain --noconfirm --needed

export PATH="$PATH:/opt/bin"

# Create the scratch folders where we'll build everything.

mkdir -p /arm64
mkdir -p scratch-arm64
cd scratch-arm64

# Get the sources for libogg and build them
# The "git config core.auotcrlf false && git reset --hard" commands are to ensure
# that all of the files have the line endings that are stored in the repo, presumably
# the Unix style \n line endings.  This is required for some configure makefiles to
# work properly.

git clone https://github.com/xiph/ogg
cd ogg
git config core.autocrlf false && git reset --hard

./autogen.sh && ./configure --prefix=/arm64 --host=aarch64-w64-mingw32 && make install

cd ..

# Get the sources for libvorbis and build them

git clone https://github.com/xiph/vorbis
cd vorbis/
git config core.autocrlf false && git reset --hard

./autogen.sh
# I don't know why these next two lines are needed.
export OGG_CFLAGS="-I/arm64/include"
export OGG_LIBS="-L/arm64/lib -logg"
./configure --prefix=/arm64 --host=aarch64-w64-mingw32 && make install

cd ..

# Get the sources for libvpx and build them

git clone https://chromium.googlesource.com/webm/libvpx
cd libvpx/
git config core.autocrlf false && git reset --hard

export CC=/opt/bin/aarch64-w64-mingw32-gcc
export CXX=/opt/bin/aarch64-w64-mingw32-g++
export AR=/opt/bin/aarch64-w64-mingw32-ar
./configure $VPX_CONFIGFLAGS \
  --prefix=/arm64 --target=arm64-win64-gcc
make install
unset CC
unset CXX
unset AR

cd ..

# Get the sources for libx264 and build them.

git clone http://git.videolan.org/git/x264.git
cd x264/
git config core.autocrlf false && git reset --hard
mkdir build
cd build

../configure $X264_CONFIGFLAGS \
  --prefix=/arm64 --host=aarch64-w64-mingw32 --cross-prefix=aarch64-w64-mingw32-
make install

cd ../..

# Get the sources for libmp3lame and build them.

git clone https://github.com/BloomBooks/lame
cd lame
git config core.autocrlf false && git reset --hard
git checkout Bloom

./configure $LAME_CONFIGFLAGS \
  --prefix=/arm64 --host=aarch64-w64-mingw32

#  **************************************************************************
#  *                                                                        *
#  * You are cross compiling:                                               *
#  *   - I did not have a change to determine                               *
#  *     + the size of:                                                     *
#  *       - short                                                          *
#  *       - unsigned short                                                 *
#  *       - int                                                            *
#  *       - unsigned int                                                   *
#  *       - long                                                           *
#  *       - unsigned long                                                  *
#  *       - float                                                          *
#  *       - double                                                         *
#  *       - long double                                                    *
#  *     + the endianess of the system                                      *
#  *   - You have to provide appropriate defines for them in config.h, e.g. *
#  *     + define SIZEOF_SHORT to 2 if the size of a short is 2             *
#  *     + define WORDS_BIGENDIAN if your system is a big endian system     *
#  *                                                                        *
#  **************************************************************************
# I looked over these values in config.h and consulted the internet, the source of all
# information true false and indifferent, and determined that the default values set
# by the configure script are fine.

make install

cd ..

# Get the sources for ffmpeg and compile them.  The tag n8.0 points to the most recent official
# release at the time this script was written (October 2025).
# The configure script is designed to build in a separate build folder.
git clone https://github.com/FFmpeg/FFmpeg
cd FFmpeg/

git config core.autocrlf false && git reset --hard

git checkout n8.0
mkdir build
cd build
PKG_CONFIG_PATH="$PKG_CONFIG_PATH:/arm64/lib/pkgconfig" ../configure $FFMPEG_CONFIGFLAGS \
  --prefix=/arm64 --arch=aarch64 \
  --extra-ldflags="-static -L/arm64/lib" --extra-cflags="-I/arm64/include" \
  --cross-prefix=aarch64-w64-mingw32- --host-cc=x86_64-w64-mingw32-gcc --target-os=win64

make install
cd ../..
