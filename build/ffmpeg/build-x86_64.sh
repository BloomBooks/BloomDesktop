#!/bin/sh

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
# The "libtoolize / autoreconf / automake --add-missing" commands are needed to get
# the libtoolize operations in the configure scripts to work properly.  (Some of the
# tools have changed their behavior since some of the sources have been changed.)

git clone https://github.com/xiph/ogg
cd ogg

git config core.autocrlf false && git reset --hard
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

extralibs=-lmingwex ./configure --enable-static --enable-multithread --disable-vp9 --as=yasm \
  --enable-libyuv --enable-webm-io --prefix=/win-x64 --target=x86_64-win64-gcc \
  --disable-unit-tests
make install
cd ..

# Get the sources for libx264 and build them.
# The configure script is designed to build in a separate build folder.

git clone http://git.videolan.org/git/x264.git
cd x264/

git config core.autocrlf false && git reset --hard

mkdir build
cd build
../configure --enable-static --disable-cli --disable-gpl --disable-opencl --disable-avs \
  --disable-swscale --disable-lavf --disable-ffms --disable-gpac --disable-lsmash --enable-lto \
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
./configure --prefix=/win-x64 --disable-shared --enable-expopt=full
make install
cd ..

# Get the sources for ffmpeg and compile them.  The tag n8.0 points to the most recent official
# release at the time this script was written (October 2025).
# The configure script is designed to build in a separate build folder.
# ffmpeg provides almost 500 different decoders.  If we disabled all decoders, and enabled only
# the ones we need, it might be possible to get the program size down to around 10MB instead of
# being around 25MB.  But we don't really know which decoders we actually need.

git clone https://github.com/FFmpeg/FFmpeg
cd FFmpeg/

git config core.autocrlf false && git reset --hard

git checkout n8.0
mkdir build
cd build
PKG_CONFIG_PATH="$PKG_CONFIG_PATH:/win-x64/lib/pkgconfig" ../configure --enable-gpl \
  --enable-avcodec --enable-avdevice --enable-avformat --enable-avfilter --enable-swresample \
  --enable-swscale --enable-libx264 --enable-libvorbis --enable-libvpx --enable-libmp3lame \
  --disable-decoders \
  --enable-decoder='h264,vp8,rawvideo,aac,mp3,mp3float,vorbis' \
  --enable-decoder='pcm_alaw,pcm_bluray,pcm_dvd,pcm_f16le,pcm_f24le,pcm_f32be,pcm_f32le' \
  --enable-decoder='pcm_f64be,pcm_f64le,pcm_lxf,pcm_mulaw,pcm_s16be,pcm_s16be_planar' \
  --enable-decoder='pcm_s16le,pcm_s16le_planar,pcm_s24be,pcm_s24daud,pcm_s24le' \
  --enable-decoder='pcm_s24le_planar,pcm_s32be,pcm_s32le,pcm_s32le_planar,pcm_s64be' \
  --enable-decoder='pcm_s64le,pcm_s8,pcm_s8_planar,pcm_sga,pcm_u16be,pcm_u16le,pcm_u24be' \
  --enable-decoder='pcm_u24le,pcm_u32be,pcm_u32le,pcm_u8,pcm_vidc' \
  --enable-decoder='h263,mpeg4,mpeg1video,mpeg2video,mpegvideo,msmpeg4v1,msmpeg4v2,msmpeg4' \
  --enable-decoder='mjpeg,mjpegb,jpeg2000,jpegls,bmp,tiff,gif' \
  --enable-decoder='mp1,mp1float,mp2,mp2float,mp3adufloat,mp3adu,mp3on4float,mp3on4,als' \
  --enable-decoder='libvorbis' \
  --disable-encoders --enable-encoder='rawvideo,libx264,libvpx_vp8,aac,libmp3lame,h263' \
  --disable-parsers --enable-parser=h264,vp8,mpegaudio \
  --disable-protocols --enable-protocol='file,concat' \
  --disable-demuxers \
  --enable-demuxer='mp4,mov,matroska,webm,avi,mpegvideo,h264,rawvideo,mp3,aac,wav,ogg' \
  --enable-demuxer='concat,image2,mjpeg,m4a,3gp,3g2,mj2' \
  --enable-demuxer='h261,h263,mjpeg_2000,flac,gif,gdigrab' \
  --disable-muxers --enable-muxer='rawvideo,mp4,mp3,tgp' \
  --disable-filters --enable-filter='scale,adelay,afade,amix,aresample,volume' \
  --disable-indevs --enable-indev=gdigrab \
  --disable-programs --enable-ffmpeg \
  --disable-hwaccels --disable-bsfs --disable-outdevs --disable-autodetect --disable-doc \
  --prefix=/win-x64 --arch=x86_64 --pkg-config-flags=--static --pkg-config=pkg-config \
  --extra-ldflags="-static -L/win-x64/lib" --extra-cflags="-I/win-x64/include"
make install
cd ../..
