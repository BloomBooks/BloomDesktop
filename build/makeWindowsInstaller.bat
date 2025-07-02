call "C:\Program Files (x86)\Microsoft Visual Studio 12.0\VC\vcvarsall.bat"
REM Must be run in a DEVELOPMENT shell (e.g., start menu, type dev, choose "Developer Command Prompt for VS 2022 ")
REM Makes a release build with some debug-friendly settings and then makes a Velopack installer,
REM currently in ../output/installer/result
REM (Note that Installer depends on Build, so a release build will be done even if you remove the first command)
REM To test the update process, patch ApplicationUpdateSupport.GetUpdateUrl to temporarily return an absolute path to the output directory
REM If there are existing releases in the output directory, you must make sure the BUILD_NUMBER is adjusted to something larger than any
REM of them. The major and minor numbers here are ignored.

pushd .
REM squirrel days: MSbuild /p:Label=Beta /p:channel=LocalBuilt /property:SquirrelReleaseFolder="../output/installer" /p:BuildConfigurationID=xyz123 /p:WarningLevel=0 /target:Build /property:teamcity_build_checkoutDir=..\ /verbosity:detailed  /property:LargeFilesDir="C:\dev\teamcitybuilddownloads" /property:teamcity_dotnet_nunitlauncher_msbuild_task="notthere" /property:BUILD_NUMBER="*.*.999.999" /property:Minor="1"
MSbuild /p:Label=Beta /p:channel=LocalBuilt /p:InstallerOutputFolder=../output/installer/result /p:BuildConfigurationID=xyz123 /p:WarningLevel=0 /target:Build /property:teamcity_build_checkoutDir=..\ /verbosity:detailed  /property:LargeFilesDir="C:\dev\teamcitybuilddownloads" /property:teamcity_dotnet_nunitlauncher_msbuild_task="notthere" /property:BUILD_NUMBER="6.3.1007.0" /property:Minor="1"

REM this only needs doing once, really, but putting it here so devs don't forget it.
dotnet tool install -g vpk

REM review: do any of these properties I'm not using still apply? I'm not sure the Label does anything.
REM squirrel: MSbuild /p:Label=Beta /p:channel=LocalBuilt /property:SquirrelReleaseFolder="../output/installer" /p:BuildConfigurationID=xyz123 /p:WarningLevel=0 /target:Installer /property:teamcity_build_checkoutDir=..\ /verbosity:detailed  /property:LargeFilesDir="C:\dev\teamcitybuilddownloads" /property:teamcity_dotnet_nunitlauncher_msbuild_task="notthere" /property:BUILD_NUMBER="*.*.999.999" /property:Minor="1"
MSbuild /p:Label=Beta /p:channel=LocalBuilt /p:InstallerOutputFolder=../output/installer/result /p:BUILD_NUMBER=6.3.1007.0 /t:Installer
popd
PAUSE
