#!/bin/sh

# libvpx configuration
export VPX_CONFIGFLAGS="--enable-static --enable-multithread --enable-libyuv --enable-webm-io \
--disable-vp9 --disable-unit-tests --disable-examples"

# libx264 configuration
export X264_CONFIGFLAGS="--enable-static --enable-lto --disable-cli --disable-gpl \
--disable-opencl --disable-avs --disable-swscale --disable-lavf --disable-ffms --disable-gpac \
--disable-lsmash"

# libmp3lame configuration
export LAME_CONFIGFLAGS="--disable-shared --enable-expopt=full"

# ffmpeg configuration
# The lists of decoders and demuxers are expanded beyond those suggested by claude sonnet 4 in
# October 2025 for creating a minimized ffmpeg for Bloom.
export FFMPEG_CONFIGFLAGS="--enable-gpl \
--enable-avcodec --enable-avdevice --enable-avformat --enable-avfilter --enable-swresample \
--enable-swscale --enable-libx264 --enable-libvorbis --enable-libvpx --enable-libmp3lame \
--disable-decoders \
--enable-decoder=h264,vp8,rawvideo,aac,mp3,mp3float,vorbis \
--enable-decoder=pcm_alaw,pcm_bluray,pcm_dvd,pcm_f16le,pcm_f24le,pcm_f32be,pcm_f32le \
--enable-decoder=pcm_f64be,pcm_f64le,pcm_lxf,pcm_mulaw,pcm_s16be,pcm_s16be_planar \
--enable-decoder=pcm_s16le,pcm_s16le_planar,pcm_s24be,pcm_s24daud,pcm_s24le \
--enable-decoder=pcm_s24le_planar,pcm_s32be,pcm_s32le,pcm_s32le_planar,pcm_s64be \
--enable-decoder=pcm_s64le,pcm_s8,pcm_s8_planar,pcm_sga,pcm_u16be,pcm_u16le,pcm_u24be \
--enable-decoder=pcm_u24le,pcm_u32be,pcm_u32le,pcm_u8,pcm_vidc \
--enable-decoder=h263,mpeg4,mpeg1video,mpeg2video,mpegvideo,msmpeg4v1,msmpeg4v2,msmpeg4 \
--enable-decoder=mjpeg,mjpegb,jpeg2000,jpegls,bmp,tiff,gif \
--enable-decoder=mp1,mp1float,mp2,mp2float,mp3adufloat,mp3adu,mp3on4float,mp3on4,als \
--enable-decoder=libvorbis \
--disable-encoders --enable-encoder=rawvideo,libx264,libvpx_vp8,aac,libmp3lame,h263 \
--disable-parsers --enable-parser=h264,vp8,mpegaudio \
--disable-protocols --enable-protocol=file,concat \
--disable-demuxers \
--enable-demuxer=mp4,mov,matroska,webm,avi,mpegvideo,h264,rawvideo,mp3,aac,wav,ogg \
--enable-demuxer=concat,image2,mjpeg,m4a,3gp,3g2,mj2 \
--enable-demuxer=h261,h263,mjpeg_2000,flac,gif,gdigrab \
--disable-muxers --enable-muxer=rawvideo,mp4,mp3,tgp \
--disable-filters --enable-filter=scale,adelay,afade,amix,aresample,volume \
--disable-indevs --enable-indev=gdigrab \
--disable-programs --enable-ffmpeg \
--disable-hwaccels --disable-bsfs --disable-outdevs --disable-autodetect --disable-doc \
--pkg-config-flags=--static --pkg-config=pkg-config"
