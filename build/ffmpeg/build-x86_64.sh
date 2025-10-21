#!/bin/sh

source $(dirname $0)/build-config.sh

# Install the necessary build tools.

pacman -S git cmake --noconfirm --needed
pacman -S mingw-w64-x86_64-toolchain --noconfirm --needed
pacman -S mingw-w64-x86_64-autotools --noconfirm --needed
pacman -S mingw-w64-x86_64-yasm --noconfirm --needed
pacman -S mingw-w64-x86_64-nasm --noconfirm --needed

# Create the scratch folders where we'll build everything.

mkdir -p /win-x64
mkdir -p scratch
cd scratch

# Get the sources for libogg and build them.
# The "git config core.auotcrlf false && git reset --hard" commands are to ensure
# that all of the files have the line endings that are stored in the repo, presumably
# the Unix style \n line endings.  This is required for some configure makefiles to
# work properly.

git clone https://github.com/xiph/ogg
cd ogg

git config core.autocrlf false && git reset --hard
# The "libtoolize / autoreconf / automake --add-missing" commands are needed to get
# the libtoolize operations in the configure scripts to work properly.  (Some of the
# tools have changed their behavior since some of the sources have been changed.)
libtoolize
autoreconf
automake --add-missing

./autogen.sh && ./configure --prefix=/win-x64 --target=x64-win32-gcc && make install
cd ..

# Get the sources for libvorbis and build them.

git clone https://github.com/xiph/vorbis
cd vorbis/

git config core.autocrlf false && git reset --hard
libtoolize
autoreconf
automake --add-missing

./autogen.sh && ./configure --prefix=/win-x64 && make install
cd ..

# Get the sources for libvpx and build them.

git clone https://chromium.googlesource.com/webm/libvpx
cd libvpx/

git config core.autocrlf false && git reset --hard

extralibs=-lmingwex ./configure $VPX_CONFIGFLAGS --as=yasm \
  --prefix=/win-x64 --target=x86_64-win64-gcc
make install
cd ..

# Get the sources for libx264 and build them.
# The configure script is designed to build in a separate build folder.

git clone http://git.videolan.org/git/x264.git
cd x264/

git config core.autocrlf false && git reset --hard

mkdir build
cd build
../configure $X264_CONFIGFLAGS \
  --prefix=/win-x64
make install
cd ../..

# Get the sources for libmp3lame and build them.
# The original sources are stored in a Subversion repository at Sourceforge and haven't changed
# in years (https://sourceforge.net/projects/lame/). We had to change a couple of files to get
# them to compile, so we created our own git repository and branch.  We had to edit one file to
# get it to compile with MinGW on Windows in 2018.  One more file was edited to compile with
# MSYS2 in 2025.

git clone https://github.com/BloomBooks/lame
cd lame
git config core.autocrlf false && git reset --hard
git checkout Bloom
./configure $LAME_CONFIGFLAGS \
  --prefix=/win-x64
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
PKG_CONFIG_PATH="$PKG_CONFIG_PATH:/win-x64/lib/pkgconfig" ../configure $FFMPEG_CONFIGFLAGS \
  --prefix=/win-x64 --arch=x86_64 \
  --extra-ldflags="-static -L/win-x64/lib" --extra-cflags="-I/win-x64/include"
make install
cd ../..
