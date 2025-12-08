REM This batch file mimics what TeamCity uses to build installers.
REM Edit BUILDCOUNT, CHANNEL, and SHAREDDIR to fit your needs for testing.

call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" amd64

pushd .

set BUILDCOUNT=99
set CHANNEL=BetaInternal
set SHAREDDIR=C:\MyTest

msbuild /t:Build build\Bloom.proj /p:Configuration="Release" /p:Platform="Any CPU"

msbuild build\Bloom.proj /t:UploadSignIfPossible /p:Configuration=Release /p:Platform="Any CPU" /p:Label=%CHANNEL% /p:channel=%CHANNEL% /p:SquirrelReleaseFolder="../output/installer" /p:BuildConfigurationID=xyz123 /p:SharedBuildDir="%SHAREDDIR%" /p:BUILD_NUMBER="*.*.%BUILDCOUNT%.123456789" /p:Minor="1" /v:detailed

popd

echo expect a build failure reporting "Unable to get AWS credentials from the credential profile store"
echo the installer should exist in output\installer
pause
