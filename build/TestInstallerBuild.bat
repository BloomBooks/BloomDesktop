call "c:\Program Files (x86)\Microsoft Visual Studio 10.0\VC\vcvarsall.bat"

pushd .
MSbuild /target:installer /property:teamcity_build_checkoutDir=..\ /verbosity:detailed  /property:LargeFilesDir="C:\dev\teamcitybuilddownloads" /property:teamcity_dotnet_nunitlauncher_msbuild_task="notthere" /property:BUILD_NUMBER="*.*.36.999" /property:Minor="1"
popd
PAUSE