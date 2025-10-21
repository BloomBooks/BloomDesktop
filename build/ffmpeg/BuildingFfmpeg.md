                                                June 7, 2018 (revised October 22, 2025)
Building a Reduced ffmpeg for Windows
=====================================

These websites gave the inspiration and basic direction for building a reduced
ffmpeg on Windows, but details differed in a lot of places.  The first website
assumes you're running on Linux, and doesn't include libvpx which is needed for
reading mp4 files produced by mozilla code.  (Also, the python script he talks
about won't run on Windows.)  The second website is a bit dated, as it talks
about building on Windows 7.

  - [Build-your-own-tiny-FFMPEG](https://github.com/alberthdev/alberthdev-misc/wiki/Build-your-own-tiny-FFMPEG)
  - [building-with-libvpx](http://wiki.webmproject.org/ffmpeg/building-with-libvpx)

msys2
-----
1. Download the [msys2 installer](https://github.com/msys2/msys2-installer/releases/).  This is a 64-bit system.  You want a file named something like mys2-x86_64-20250830.exe.
2. Run the installer, install it for all users if you like.  The default location C:\msys64 is fine.

Building ffmpeg for x86_64
--------------------------
1. Open the shell window provided for msys2 using the mingw64.exe program.  (This opens the shell window in the right state for compiling programs.)  cd to the desired spot in the filesystem for where you want to build ffmpeg.  (Some sort of temporary or scratch position would do.  Even C:\ (aka /c inside msys2) would probably work as the root.
2. Execute the shell script <BloomDesktop>/build/build-x86_64.sh and wait until it finishes.  If it succeeds, the finished product (ffmpeg.exe) should be at /win-x64/bin/ffmpeg.exe (or C:\msys64\win-x64\bin\ffmpeg.exe if you prefer).  If the build fails, the ffmpeg-x86_64-build.log file may contain helpful information about the nature of the failure.
```
    .../build/ffmpeg/build-x86_64.sh | tee ffmpeg-x86_64-build.log
```
On a reasonably fast developer machine with a reasonably fast internet connection, the shell script should take about 20-30 minutes to complete.

The current full static ffmpeg.exe downloaded from the internet is about 189MB in size.  The reduced static ffmpeg.exe built through the process above is about 12MB in size.

Building ffmpeg for arm64
-------------------------
1. Open the shell window provided for msys2 using the mingw64.exe program.  (This opens the shell window in the right state for compiling programs.)  cd to the desired spot in the filesystem for where you want to build ffmpeg.  (Some sort of temporary or scratch position would do.  Even C:\ (aka /c inside msys2) would probably work as the root.
2. Execute the shell script <BloomDesktop>/build/build-arm64.sh and wait until it finishes.  If it succeeds, the finished product (ffmpeg.exe) should be at /arm64/bin/ffmpeg.exe (or C:\msys64\arm64\bin\ffmpeg.exe if you prefer).  If the build fails, the ffmpeg-arm64-build.log file may contain helpful information about the nature of the failure.
```
    .../build/ffmpeg/build-arm64.sh | tee ffmpeg-arm64-build.log
```
On a reasonably fast developer machine with a reasonably fast internet connection, the shell script should take about 20-30 minutes to complete.

The current full static ffmpeg.exe downloaded from the internet is about 127MB in size.  The reduced static ffmpeg.exe built through the process above is about 25MB in size.  (It may well be that the "full build" for arm64 did not include everything in the "full build" for x86_64.  ffmpeg is a complex program with lots of options.)

Testing ffmpeg
--------------
A number of test cases have been added to the `Testing/ffmpeg` folder in the shared `Bloom Team` Google drive.
