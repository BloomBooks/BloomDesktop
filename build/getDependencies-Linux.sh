#!/bin/bash
# server=build.palaso.org
# project=Bloom
# build=Bloom-linux-precise64-continuous
# root_dir=$(dirname $0)/..
# $Id: 50ae191e9e7146711d602e58cde080678d265c48 $

cd "$(dirname "$0")"

# *** Functions ***
force=0
clean=0

while getopts fc opt; do
case $opt in
f) force=1 ;;
c) clean=1 ;;
esac
done

copy_auto() {
if [ "$clean" == "1" ]
then
echo cleaning $2
rm -f ""$2""
else
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
# build: Bloom-linux-precise64-continuous (bt338)
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
# [4] build: TidyManaged-master-precise64-continuous (bt351)
#     project: TidyManaged
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt351
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"TidyManaged.dll*"=>"lib/"}
#     VCS: https://github.com/hatton/TidyManaged.git [master]

# make sure output directories exist
mkdir -p "$(dirname $0)/../lib/dotnet"
mkdir -p "$(dirname $0)/../build/"
mkdir -p "$(dirname $0)/../lib/"

# download artifact dependencies
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Chorus.exe "$(dirname $0)/../lib/dotnet/Chorus.exe"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusHub.exe "$(dirname $0)/../lib/dotnet/ChorusHub.exe"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusMerge.exe "$(dirname $0)/../lib/dotnet/ChorusMerge.exe"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/Chorus.exe "$(dirname $0)/../lib/dotnet/Chorus.exe"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/ChorusHub.exe "$(dirname $0)/../lib/dotnet/ChorusHub.exe"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/ChorusMerge.exe "$(dirname $0)/../lib/dotnet/ChorusMerge.exe"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Autofac.dll "$(dirname $0)/../lib/dotnet/Autofac.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Geckofx-Winforms-14.dll "$(dirname $0)/../lib/dotnet/Geckofx-Winforms-14.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.TestUtilities.dll "$(dirname $0)/../lib/dotnet/LibChorus.TestUtilities.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.dll "$(dirname $0)/../lib/dotnet/LibChorus.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/NDesk.DBus.dll "$(dirname $0)/../lib/dotnet/NDesk.DBus.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/Autofac.dll "$(dirname $0)/../lib/dotnet/Autofac.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/Geckofx-Winforms-14.dll "$(dirname $0)/../lib/dotnet/Geckofx-Winforms-14.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/LibChorus.TestUtilities.dll "$(dirname $0)/../lib/dotnet/LibChorus.TestUtilities.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/LibChorus.dll "$(dirname $0)/../lib/dotnet/LibChorus.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/NDesk.DBus.dll "$(dirname $0)/../lib/dotnet/NDesk.DBus.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/geckofx-core-14.dll "$(dirname $0)/../lib/dotnet/geckofx-core-14.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/geckofx-core-14.dll "$(dirname $0)/../lib/dotnet/geckofx-core-14.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt339/latest.lastSuccessful/geckofx-11.dll "$(dirname $0)/../lib/dotnet/geckofx-11.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt339/latest.lastSuccessful/geckofx-11.dll.config "$(dirname $0)/../lib/dotnet/geckofx-11.dll.config"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.BuildTasks.dll "$(dirname $0)/../build/Palaso.BuildTasks.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Enchant.Net.dll "$(dirname $0)/../lib/dotnet/Enchant.Net.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Ionic.Zip.dll "$(dirname $0)/../lib/dotnet/Ionic.Zip.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/L10NSharp.dll "$(dirname $0)/../lib/dotnet/L10NSharp.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/NDesk.DBus.dll "$(dirname $0)/../lib/dotnet/NDesk.DBus.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.BuildTasks.dll "$(dirname $0)/../lib/dotnet/Palaso.BuildTasks.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.DictionaryServices.dll "$(dirname $0)/../lib/dotnet/Palaso.DictionaryServices.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Lift.dll "$(dirname $0)/../lib/dotnet/Palaso.Lift.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Media.dll "$(dirname $0)/../lib/dotnet/Palaso.Media.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.TestUtilities.dll "$(dirname $0)/../lib/dotnet/Palaso.TestUtilities.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Tests.dll "$(dirname $0)/../lib/dotnet/Palaso.Tests.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.dll "$(dirname $0)/../lib/dotnet/Palaso.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.dll "$(dirname $0)/../lib/dotnet/PalasoUIWindowsForms.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/SIL.Archiving.dll "$(dirname $0)/../lib/dotnet/SIL.Archiving.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Enchant.Net.dll "$(dirname $0)/../lib/dotnet/Enchant.Net.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Ionic.Zip.dll "$(dirname $0)/../lib/dotnet/Ionic.Zip.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/L10NSharp.dll "$(dirname $0)/../lib/dotnet/L10NSharp.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/NDesk.DBus.dll "$(dirname $0)/../lib/dotnet/NDesk.DBus.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.BuildTasks.dll "$(dirname $0)/../lib/dotnet/Palaso.BuildTasks.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.DictionaryServices.dll "$(dirname $0)/../lib/dotnet/Palaso.DictionaryServices.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Lift.dll "$(dirname $0)/../lib/dotnet/Palaso.Lift.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Media.dll "$(dirname $0)/../lib/dotnet/Palaso.Media.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.TestUtilities.dll "$(dirname $0)/../lib/dotnet/Palaso.TestUtilities.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Tests.dll "$(dirname $0)/../lib/dotnet/Palaso.Tests.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.dll "$(dirname $0)/../lib/dotnet/Palaso.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/PalasoUIWindowsForms.dll "$(dirname $0)/../lib/dotnet/PalasoUIWindowsForms.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/SIL.Archiving.dll "$(dirname $0)/../lib/dotnet/SIL.Archiving.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/ibusdotnet.dll "$(dirname $0)/../lib/dotnet/ibusdotnet.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/icu.net.dll "$(dirname $0)/../lib/dotnet/icu.net.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/ibusdotnet.dll "$(dirname $0)/../lib/dotnet/ibusdotnet.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/icu.net.dll "$(dirname $0)/../lib/dotnet/icu.net.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfDroplet.exe "$(dirname $0)/../lib/dotnet/PdfDroplet.exe"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfSharp.dll "$(dirname $0)/../lib/dotnet/PdfSharp.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt351/latest.lastSuccessful/TidyManaged.dll "$(dirname $0)/../lib/TidyManaged.dll"
copy_auto http://build.palaso.org/guestAuth/repository/download/bt351/latest.lastSuccessful/TidyManaged.dll.config "$(dirname $0)/../lib/TidyManaged.dll.config"
# End of script
