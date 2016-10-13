call "C:\Program Files (x86)\Microsoft Visual Studio 12.0\VC\vcvarsall.bat"

pushd .
MSbuild /p:Label=Beta /p:channel=LocalBuilt /property:SquirrelReleaseFolder="../output/installer" /p:BuildConfigurationID=xyz123 /p:WarningLevel=0 /target:Installer /property:teamcity_build_checkoutDir=..\ /verbosity:detailed  /property:LargeFilesDir="C:\dev\teamcitybuilddownloads" /property:teamcity_dotnet_nunitlauncher_msbuild_task="notthere" /property:BUILD_NUMBER="*.*.999.999" /property:Minor="1"
popd
PAUSE