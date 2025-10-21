#!/bin/sh

# Install the necessary build tools.

pacman -S git autotools --noconfirm --needed
pacman -S mingw-w64-cross-toolchain --noconfirm --needed

export PATH="$PATH:/opt/bin"

# Create the scratch folders where we'll build everything.

mkdir -p /arm64
mkdir -p scratch-arm64
cd scratch-arm64

# Get the sources for libogg and build them

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
# I don't know why these next two lines are needed.  The .pc file produced by ogg
# seems to be defective.
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
./configure --enable-static --enable-multithread --disable-vp9 \
  --enable-libyuv --enable-webm-io --prefix=/arm64 --target=arm64-win64-gcc \
  --disable-unit-tests --disable-examples
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

../configure --enable-static --disable-cli --disable-gpl --disable-opencl --disable-avs \
  --disable-swscale --disable-lavf --disable-ffms --disable-gpac --disable-lsmash --enable-lto \
  --prefix=/arm64 --host=aarch64-w64-mingw32 --cross-prefix=aarch64-w64-mingw32-
make install

cd ../..

# Get the sources for libmp3lame and build them.

git clone https://github.com/BloomBooks/lame
cd lame
git config core.autocrlf false && git reset --hard
git checkout Bloom

./configure --prefix=/arm64 --disable-shared --enable-expopt=full --host=aarch64-w64-mingw32

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

git clone https://github.com/FFmpeg/FFmpeg
cd FFmpeg/

git config core.autocrlf false && git reset --hard

git checkout n8.0
mkdir build
cd build
PKG_CONFIG_PATH="$PKG_CONFIG_PATH:/arm64/lib/pkgconfig" ../configure --enable-gpl \
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
  --prefix=/arm64 --arch=aarch64 --pkg-config-flags=--static --pkg-config=pkg-config \
  --extra-ldflags="-static -L/arm64/lib" --extra-cflags="-I/arm64/include" \
  --cross-prefix=aarch64-w64-mingw32- --host-cc=x86_64-w64-mingw32-gcc --target-os=win64

make install
cd ../..
