#!/bin/bash
# server=build.palaso.org
# project=Bloom
# build=Bloom-Default-Win32-Auto
# root_dir=.

# *** Results ***
# build: Bloom-Default-Win32-Auto (bt222)
# project: Bloom
# URL: http://build.palaso.org/viewType.html?buildTypeId=bt222
# VCS: https://bitbucket.org/hatton/bloom-desktop [default]
# dependencies:
# [0] build: wkhtmltopdf -win32 (bt326)
#     project: Bloom
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt326
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"wk*.zip "=>" lib"}
# [1] build: chorus-win32-master Continuous (bt2)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt2
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"policy_9_0_Microsoft_VC90_CRT_x86.msm"=>"build\\ChorusInstallerStuff"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [2] build: chorus-win32-master Continuous (bt2)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt2
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Vulcan.Uczniowie.HelpProvider.dll"=>"output/release"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [3] build: chorus-win32-master Continuous (bt2)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt2
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Microsoft_VC90_CRT_x86.msm"=>"build\\ChorusInstallerStuff"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [4] build: chorus-win32-master Continuous (bt2)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt2
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"ChorusMergeModule.msm"=>"build\\ChorusInstallerStuff"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [5] build: chorus-win32-master Continuous (bt2)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt2
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.exe"=>"lib/dotnet", "*.dll"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [6] build: geckofx-11 Continuous (bt143)
#     project: GeckoFx
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt143
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.dll"=>"lib/dotnet"}
#     VCS: https://bitbucket.org/hatton/geckofx-11.0 [default]
# [7] build: XulRunner11-win32 (bt332)
#     project: GeckoFx
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt332
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.zip"=>"lib"}
# [8] build: palaso-win32-master Continuous (bt223)
#     project: libpalaso
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt223
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Palaso.BuildTasks.dll"=>"build/"}
#     VCS: https://github.com/sillsdev/libpalaso.git []
# [9] build: palaso-win32-master Continuous (bt223)
#     project: libpalaso
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt223
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"exiftool/*.*"=>"DistFiles"}
#     VCS: https://github.com/sillsdev/libpalaso.git []
# [10] build: palaso-win32-master Continuous (bt223)
#     project: libpalaso
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt223
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.dll"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/libpalaso.git []
# [11] build: PdfDroplet-Win-Dev-Continuous (bt54)
#     project: PdfDroplet
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt54
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"PdfDroplet.exe"=>"lib/dotnet", "PdfSharp.dll"=>"lib/dotnet"}
#     VCS: http://hg.palaso.org/pdfdroplet [default]

# make sure output directories exist
mkdir -p ./build/ChorusInstallerStuff
mkdir -p ./output/release
mkdir -p ./lib/dotnet
mkdir -p ./lib
mkdir -p ./build/
mkdir -p ./DistFiles

# download artifact dependencies
curl -L -z ./build/ChorusInstallerStuff/policy_9_0_Microsoft_VC90_CRT_x86.msm -o ./build/ChorusInstallerStuff/policy_9_0_Microsoft_VC90_CRT_x86.msm http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/policy_9_0_Microsoft_VC90_CRT_x86.msm
curl -L -z ./output/release/Vulcan.Uczniowie.HelpProvider.dll -o ./output/release/Vulcan.Uczniowie.HelpProvider.dll http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Vulcan.Uczniowie.HelpProvider.dll
curl -L -z ./build/ChorusInstallerStuff/Microsoft_VC90_CRT_x86.msm -o ./build/ChorusInstallerStuff/Microsoft_VC90_CRT_x86.msm http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Microsoft_VC90_CRT_x86.msm
curl -L -z ./build/ChorusInstallerStuff/ChorusMergeModule.msm -o ./build/ChorusInstallerStuff/ChorusMergeModule.msm http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusMergeModule.msm
curl -L -z ./lib/dotnet/Chorus.exe -o ./lib/dotnet/Chorus.exe http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Chorus.exe
curl -L -z ./lib/dotnet/ChorusHub.exe -o ./lib/dotnet/ChorusHub.exe http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusHub.exe
curl -L -z ./lib/dotnet/ChorusMerge.exe -o ./lib/dotnet/ChorusMerge.exe http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusMerge.exe
curl -L -z ./lib/dotnet/Autofac.dll -o ./lib/dotnet/Autofac.dll http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Autofac.dll
curl -L -z ./lib/dotnet/LibChorus.TestUtilities.dll -o ./lib/dotnet/LibChorus.TestUtilities.dll http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/LibChorus.TestUtilities.dll
curl -L -z ./lib/dotnet/LibChorus.dll -o ./lib/dotnet/LibChorus.dll http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/LibChorus.dll
curl -L -z ./lib/dotnet/Palaso.dll -o ./lib/dotnet/Palaso.dll http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Palaso.dll
curl -L -z ./lib/dotnet/Vulcan.Uczniowie.HelpProvider.dll -o ./lib/dotnet/Vulcan.Uczniowie.HelpProvider.dll http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Vulcan.Uczniowie.HelpProvider.dll
curl -L -z ./lib/dotnet/geckofx-11.dll -o ./lib/dotnet/geckofx-11.dll http://build.palaso.org/guestAuth/repository/download/bt143/latest.lastSuccessful/geckofx-11.dll
curl -L -z ./lib/xulrunner-11.0.en-US.win32.zip -o ./lib/xulrunner-11.0.en-US.win32.zip http://build.palaso.org/guestAuth/repository/download/bt332/latest.lastSuccessful/xulrunner-11.0.en-US.win32.zip
curl -L -z ./build/Palaso.BuildTasks.dll -o ./build/Palaso.BuildTasks.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.BuildTasks.dll
curl -L -z ./DistFiles/exiftool/exiftool.exe -o ./DistFiles/exiftool/exiftool.exe http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/exiftool/exiftool.exe
curl -L -z ./lib/dotnet/Interop.WIA.dll -o ./lib/dotnet/Interop.WIA.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Interop.WIA.dll
curl -L -z ./lib/dotnet/Ionic.Zip.dll -o ./lib/dotnet/Ionic.Zip.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Ionic.Zip.dll
curl -L -z ./lib/dotnet/L10NSharp.dll -o ./lib/dotnet/L10NSharp.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/L10NSharp.dll
curl -L -z ./lib/dotnet/Palaso.BuildTasks.dll -o ./lib/dotnet/Palaso.BuildTasks.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.BuildTasks.dll
curl -L -z ./lib/dotnet/Palaso.DictionaryServices.dll -o ./lib/dotnet/Palaso.DictionaryServices.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.DictionaryServices.dll
curl -L -z ./lib/dotnet/Palaso.Lift.dll -o ./lib/dotnet/Palaso.Lift.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.Lift.dll
curl -L -z ./lib/dotnet/Palaso.Media.dll -o ./lib/dotnet/Palaso.Media.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.Media.dll
curl -L -z ./lib/dotnet/Palaso.TestUtilities.dll -o ./lib/dotnet/Palaso.TestUtilities.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.TestUtilities.dll
curl -L -z ./lib/dotnet/Palaso.Tests.dll -o ./lib/dotnet/Palaso.Tests.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.Tests.dll
curl -L -z ./lib/dotnet/Palaso.dll -o ./lib/dotnet/Palaso.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.dll
curl -L -z ./lib/dotnet/PalasoUIWindowsForms.dll -o ./lib/dotnet/PalasoUIWindowsForms.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/PalasoUIWindowsForms.dll
curl -L -z ./lib/dotnet/SIL.Archiving.dll -o ./lib/dotnet/SIL.Archiving.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/SIL.Archiving.dll
curl -L -z ./lib/dotnet/icu.net.dll -o ./lib/dotnet/icu.net.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icu.net.dll
curl -L -z ./lib/dotnet/icudt40.dll -o ./lib/dotnet/icudt40.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icudt40.dll
curl -L -z ./lib/dotnet/icuin40.dll -o ./lib/dotnet/icuin40.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icuin40.dll
curl -L -z ./lib/dotnet/icuuc40.dll -o ./lib/dotnet/icuuc40.dll http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icuuc40.dll
curl -L -z ./lib/dotnet/PdfDroplet.exe -o ./lib/dotnet/PdfDroplet.exe http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfDroplet.exe
curl -L -z ./lib/dotnet/PdfSharp.dll -o ./lib/dotnet/PdfSharp.dll http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfSharp.dll
