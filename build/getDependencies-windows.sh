#!/bin/bash
# server=build.palaso.org
# build_type=bt222
# root_dir=..
# $Id: fddd609c79cf98392f7892a4a56d48a466d329de $

cd "$(dirname "$0")"

# *** Functions ***
force=

while getopts f opt; do
	case $opt in
	f)
		force=1
		;;

	esac
done

shift $((OPTIND - 1))


copy_auto() {
	where_curl=$(type -P curl)
	where_wget=$(type -P wget)
	if [ "$where_curl" != "" ]
	then
		copy_curl $1 $2
	elif [ "$where_wget" != "" ]
	then
		copy_wget $1 $2
	else
		echo "Missing curl or wget"
		exit 1
	fi
}

copy_curl() {
	echo "curl: $2 <= $1"
	if [ -e "$2" ] && [ "$force" != "1" ]
	then
		curl -# -L -z $2 -o $2 $1
	else
		curl -# -L -o $2 $1
	fi
}

copy_wget() {
	echo "wget: $2 <= $1"
	f=$(basename $2)
	d=$(dirname $2)
	cd $d
	wget -q -L -N $1
	cd -
}


# *** Results ***
# build: Bloom-Default-Win32-Auto (bt222)
# project: Bloom
# URL: http://build.palaso.org/viewType.html?buildTypeId=bt222
# VCS: https://bitbucket.org/hatton/bloom-desktop [default]
# dependencies:
# [0] build: bloom-1.1.-win32-static-dependencies (bt326)
#     project: Bloom
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt326
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"wk*.zip"=>"lib", "connections.dll"=>"DistFiles", "*.chm"=>"DistFiles"}
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
mkdir -p ../lib
mkdir -p ../DistFiles
mkdir -p ../build/ChorusInstallerStuff
mkdir -p ../output/release
mkdir -p ../lib/dotnet
mkdir -p ../build/

# download artifact dependencies
copy_auto http://build.palaso.org/guestAuth/repository/download/bt326/latest.lastSuccessful/wkhtmltopdf-0.10.0_rc2.zip ../lib/wkhtmltopdf-0.10.0_rc2.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt326/latest.lastSuccessful/connections.dll ../DistFiles/connections.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt326/latest.lastSuccessful/Bloom.chm ../DistFiles/Bloom.chm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/policy_9_0_Microsoft_VC90_CRT_x86.msm ../build/ChorusInstallerStuff/policy_9_0_Microsoft_VC90_CRT_x86.msm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Vulcan.Uczniowie.HelpProvider.dll ../output/release/Vulcan.Uczniowie.HelpProvider.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Microsoft_VC90_CRT_x86.msm ../build/ChorusInstallerStuff/Microsoft_VC90_CRT_x86.msm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusMergeModule.msm ../build/ChorusInstallerStuff/ChorusMergeModule.msm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Chorus.exe ../lib/dotnet/Chorus.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusHub.exe ../lib/dotnet/ChorusHub.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusMerge.exe ../lib/dotnet/ChorusMerge.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Autofac.dll ../lib/dotnet/Autofac.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/LibChorus.TestUtilities.dll ../lib/dotnet/LibChorus.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/LibChorus.dll ../lib/dotnet/LibChorus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Vulcan.Uczniowie.HelpProvider.dll ../lib/dotnet/Vulcan.Uczniowie.HelpProvider.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt143/latest.lastSuccessful/geckofx-11.dll ../lib/dotnet/geckofx-11.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt332/latest.lastSuccessful/xulrunner-11.0.en-US.win32.zip ../lib/xulrunner-11.0.en-US.win32.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.BuildTasks.dll ../build/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/exiftool/exiftool.exe ../DistFiles/exiftool.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Interop.WIA.dll ../lib/dotnet/Interop.WIA.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Ionic.Zip.dll ../lib/dotnet/Ionic.Zip.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/L10NSharp.dll ../lib/dotnet/L10NSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.BuildTasks.dll ../lib/dotnet/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.DictionaryServices.dll ../lib/dotnet/Palaso.DictionaryServices.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.Lift.dll ../lib/dotnet/Palaso.Lift.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.Media.dll ../lib/dotnet/Palaso.Media.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.TestUtilities.dll ../lib/dotnet/Palaso.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.Tests.dll ../lib/dotnet/Palaso.Tests.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icu.net.dll ../lib/dotnet/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icudt40.dll ../lib/dotnet/icudt40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icuin40.dll ../lib/dotnet/icuin40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icuuc40.dll ../lib/dotnet/icuuc40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfDroplet.exe ../lib/dotnet/PdfDroplet.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfSharp.dll ../lib/dotnet/PdfSharp.dll
# End of script
