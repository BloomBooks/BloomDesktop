call "c:\Program Files (x86)\Microsoft Visual Studio 10.0\VC\vcvarsall.bat"

pushd .
MSbuild /target:installer /property:teamcity_build_checkoutDir=..\ /verbosity:detailed /property:XulRunnerZipLocation="c:\dev" /property:teamcity_dotnet_nunitlauncher_msbuild_task="notthere" /property:BUILD_NUMBER="*.*.6.789" /property:Minor="1"
popd
PAUSE