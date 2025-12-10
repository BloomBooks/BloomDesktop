@REM This batch file mimics what TeamCity uses to build installers.
@REM Edit BUILDCOUNT, CHANNEL, and SHAREDDIR to fit your needs for testing.
@REM %0% help will tell you how to change these variables on the command line.

@if "%~1" EQU "help" goto help
@if "%~2" EQU "help" goto help
@if "%~3" EQU "help" goto help

where msbuild
if %errorlevel% == 1 call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" amd64

@pushd .

@set CHANNEL=BetaInternal
@if NOT "%~1" EQU "" set CHANNEL=%1%
@if NOT %CHANNEL% EQU Alpha if NOT %CHANNEL% EQU Beta if NOT %CHANNEL% EQU Release if NOT %CHANNEL% EQU BetaInternal if NOT %CHANNEL% EQU ReleaseInternal echo WARNING: Unknown channel %CHANNEL%
@echo CHANNEL=%CHANNEL%

@set BUILDCOUNT=99
@if NOT "%~2" == "" set BUILDCOUNT=%2%
@echo BUILDCOUNT=%BUILDCOUNT%

@set SHAREDDIR="%TEMP%"
@if NOT "%~3" == "" set SHAREDDIR=%3%
@echo SHAREDDIR=%SHAREDDIR%

msbuild /t:Build build\Bloom.proj /p:Configuration="Release" /p:Platform="Any CPU"

msbuild build\Bloom.proj /t:UploadSignIfPossible /p:Configuration=Release /p:Platform="Any CPU" /p:Label=%CHANNEL% /p:channel=%CHANNEL% /p:SquirrelReleaseFolder="../output/installer" /p:BuildConfigurationID=xyz123 /p:SharedBuildDir="%SHAREDDIR%" /p:BUILD_NUMBER="*.*.%BUILDCOUNT%.123456789" /p:Minor="1" /v:detailed

@popd

@echo expect a build failure reporting "Unable to get AWS credentials from the credential profile store"
@echo the installer should exist in output\installer

@exit /B

:help
@echo USAGE: %0 Channel Count TempDir
@echo Channel should be one of Alpha, Beta, BetaInternal, Release, or ReleaseInternal.
@echo         The default is BetaInternal.
@echo Count is the build count for the channel, which is the third item in the version.
@echo         The default is 99.
@echo TempDir is someplace where temp files can be stored.  The default is %TEMP%.
@echo If you want to skip an argument on the command line, use "" as its value.
