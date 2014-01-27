#!/bin/bash
# server=build.palaso.org
# project=Bloom
# build=Bloom-precise64-linux-continuous
# root_dir=..
# $Id: da666a7e5eb1d63b434514279cd14cacd26c730f $

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
# build: Bloom-precise64-linux-continuous (bt338)
# project: Bloom
# URL: http://build.palaso.org/viewType.html?buildTypeId=bt338
# VCS: https://bitbucket.org/hatton/bloom-desktop [linux]
# dependencies:
# [0] build: chorus-precise64-master Continuous (bt323)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt323
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.exe"=>"lib/dotnet", "*.dll"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [1] build: geckofx-11-linux-continuous (bt339)
#     project: GeckoFx
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt339
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.*"=>"lib/dotnet"}
#     VCS: https://bitbucket.org/hatton/geckofx-11.0 [default]
# [2] build: palaso-precise64-master Continuous (bt322)
#     project: libpalaso
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt322
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Palaso.BuildTasks.dll"=>"build/", "*.dll"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/libpalaso.git [master]
# [3] build: PdfDroplet-Win-Dev-Continuous (bt54)
#     project: PdfDroplet
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt54
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"PdfDroplet.exe"=>"lib/dotnet", "PdfSharp.dll"=>"lib/dotnet"}
#     VCS: http://hg.palaso.org/pdfdroplet [default]

# make sure output directories exist
mkdir -p ../lib/dotnet
mkdir -p ../build/

# download artifact dependencies
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Chorus.exe ../lib/dotnet/Chorus.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusMerge.exe ../lib/dotnet/ChorusMerge.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Autofac.dll ../lib/dotnet/Autofac.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.TestUtilities.dll ../lib/dotnet/LibChorus.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.dll ../lib/dotnet/LibChorus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt339/latest.lastSuccessful/geckofx-11.dll ../lib/dotnet/geckofx-11.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt339/latest.lastSuccessful/geckofx-11.dll.config ../lib/dotnet/geckofx-11.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.BuildTasks.dll ../build/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Enchant.Net.dll ../lib/dotnet/Enchant.Net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Ionic.Zip.dll ../lib/dotnet/Ionic.Zip.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/L10NSharp.dll ../lib/dotnet/L10NSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/NDesk.DBus.dll ../lib/dotnet/NDesk.DBus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.BuildTasks.dll ../lib/dotnet/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.TestUtilities.dll ../lib/dotnet/Palaso.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Tests.dll ../lib/dotnet/Palaso.Tests.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/ibusdotnet.dll ../lib/dotnet/ibusdotnet.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/icu.net.dll ../lib/dotnet/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfDroplet.exe ../lib/dotnet/PdfDroplet.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfSharp.dll ../lib/dotnet/PdfSharp.dll
# End of script
