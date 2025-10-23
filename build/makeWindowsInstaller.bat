REM call "C:\Program Files (x86)\Microsoft Visual Studio 12.0\VC\vcvarsall.bat"
REM Must be run in a DEVELOPMENT shell (e.g., start menu, type dev, choose "Developer Command Prompt for VS 2022 ")
REM Makes a release build with some debug-friendly settings and then makes a Velopack installer,
REM currently in ../output/installer/result
REM (Note that Installer depends on Build, so a release build will be done even if you remove the first command)
REM To test the update process, patch ApplicationUpdateSupport.GetUpdateUrl to temporarily return an absolute path to the output directory
REM If there are existing releases in the output directory, you must make sure the BUILD_NUMBER is adjusted to something larger than any
REM of them. The major and minor numbers here are ignored.

REM Ensure we run from the directory of this script, so msbuild finds Bloom.proj
pushd "%~dp0"

REM you may need to run msbuild /t:RestoreBuildTasks
if not exist ..\packages dotnet msbuild /t:RestoreBuildTasks
REM Build the installer (Installer depends on Build and EnsureVelopackCli)
REM Note: BUILD_NUMBER must be 4 parts (a.b.c.d). Only the 3rd part (BuildCounter) matters for Version; the others can be 0.
REM Clean the output folder to avoid vpk complaining about equal/greater existing releases
set OUTPUT_DIR=..\output\installer\result
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%" 2>nul
dotnet msbuild "Bloom.proj" /p:channel=LocalBuilt /p:InstallerOutputFolder="%OUTPUT_DIR%" /p:BUILD_NUMBER=0.0.9999.0 /t:Installer /verbosity:detailed
popd

