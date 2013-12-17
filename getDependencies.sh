#!/bin/bash
# server=build.palaso.org
# project=Bloom
# build=Bloom-Default-Win32-Auto

#### Results ####
# build: Bloom-Default-Win32-Auto (bt222)
# project: Bloom
# URL: http://build.palaso.org/viewType.html?buildTypeId=bt222
# VCS: https://bitbucket.org/hatton/bloom-desktop [default]
# dependencies:
# [0] build: chorus-win32-Bloom1.0 (bt221)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt221
#     VCS: http://hg.palaso.org/chorus [Bloom1.0]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.exe"=>"lib/dotnet", "*.dll"=>"lib/dotnet"}
# [1] build: chorus-win32-Bloom1.0 (bt221)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt221
#     VCS: http://hg.palaso.org/chorus [Bloom1.0]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"ChorusMergeModule.msm"=>"build\\ChorusInstallerStuff"}
# [2] build: chorus-win32-Bloom1.0 (bt221)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt221
#     VCS: http://hg.palaso.org/chorus [Bloom1.0]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Microsoft_VC90_CRT_x86.msm"=>"build\\ChorusInstallerStuff"}
# [3] build: chorus-win32-Bloom1.0 (bt221)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt221
#     VCS: http://hg.palaso.org/chorus [Bloom1.0]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"policy_9_0_Microsoft_VC90_CRT_x86.msm"=>"build\\ChorusInstallerStuff"}
# [4] build: chorus-win32-Bloom1.0 (bt221)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt221
#     VCS: http://hg.palaso.org/chorus [Bloom1.0]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Vulcan.Uczniowie.HelpProvider.dll"=>"output/release"}
# [5] build: geckofx-11 Continuous (bt143)
#     project: GeckoFx
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt143
#     VCS: https://bitbucket.org/hatton/geckofx-11.0 [default]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.dll"=>"lib/dotnet"}
# [6] build: palaso-win32-default Continuous (bt223)
#     project: Palaso Library
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt223
#     VCS: http://hg.palaso.org/palaso [default]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Palaso.BuildTasks.dll"=>"build/"}
# [7] build: palaso-win32-default Continuous (bt223)
#     project: Palaso Library
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt223
#     VCS: http://hg.palaso.org/palaso [default]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"exiftool/*.*"=>"DistFiles"}
# [8] build: palaso-win32-default Continuous (bt223)
#     project: Palaso Library
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt223
#     VCS: http://hg.palaso.org/palaso [default]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.dll"=>"lib/dotnet"}
# [9] build: PdfDroplet-Win-Dev-Continuous (bt54)
#     project: PdfDroplet
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt54
#     VCS: http://hg.palaso.org/pdfdroplet [default]
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"PdfDroplet.exe"=>"lib/dotnet", "PdfSharp.dll"=>"lib/dotnet"}

# download artifact dependencies
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Chorus.exe http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/Chorus.exe
mkdir -p lib/dotnet
curl -L -o lib/dotnet/ChorusHub.exe http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/ChorusHub.exe
mkdir -p lib/dotnet
curl -L -o lib/dotnet/ChorusMerge.exe http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/ChorusMerge.exe
mkdir -p lib/dotnet
curl -L -o lib/dotnet/LibChorus.TestUtilities.dll http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/LibChorus.TestUtilities.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/LibChorus.dll http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/LibChorus.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Vulcan.Uczniowie.HelpProvider.dll http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/Vulcan.Uczniowie.HelpProvider.dll
mkdir -p build\ChorusInstallerStuff
curl -L -o build\ChorusInstallerStuff/ChorusMergeModule.msm http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/ChorusMergeModule.msm
mkdir -p build\ChorusInstallerStuff
curl -L -o build\ChorusInstallerStuff/Microsoft_VC90_CRT_x86.msm http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/Microsoft_VC90_CRT_x86.msm
mkdir -p build\ChorusInstallerStuff
curl -L -o build\ChorusInstallerStuff/policy_9_0_Microsoft_VC90_CRT_x86.msm http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/policy_9_0_Microsoft_VC90_CRT_x86.msm
mkdir -p output/release
curl -L -o output/release/Vulcan.Uczniowie.HelpProvider.dll http://build.palaso.org/guestAuth/repository/download/bt221/latest.lastSuccessful/Vulcan.Uczniowie.HelpProvider.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/geckofx-11.dll http://build.palaso.org/guestAuth/repository/download/bt143/latest.lastSuccessful/geckofx-11.dll
mkdir -p build/
curl -L -o build/Palaso.BuildTasks.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.BuildTasks.dll
mkdir -p DistFiles
curl -L -o DistFiles/exiftool/exiftool.exe http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/exiftool/exiftool.exe
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Interop.WIA.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Interop.WIA.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Ionic.Zip.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Ionic.Zip.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/L10NSharp.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/L10NSharp.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Palaso.BuildTasks.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.BuildTasks.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Palaso.DictionaryServices.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.DictionaryServices.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Palaso.Lift.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.Lift.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Palaso.Media.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.Media.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Palaso.TestUtilities.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.TestUtilities.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Palaso.Tests.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.Tests.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/Palaso.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/PalasoUIWindowsForms.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/PalasoUIWindowsForms.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/SIL.Archiving.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/SIL.Archiving.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/icu.net.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icu.net.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/icudt40.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icudt40.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/icuin40.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icuin40.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/icuuc40.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icuuc40.dll
mkdir -p lib/dotnet
curl -L -o lib/dotnet/PdfDroplet.exe http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfDroplet.exe
mkdir -p lib/dotnet
curl -L -o lib/dotnet/PdfSharp.dll http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfSharp.dll
