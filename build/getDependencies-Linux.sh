#!/bin/bash
# server=build.palaso.org
# project=Bloom
# build=Bloom-linux-precise64-continuous
# root_dir=..
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
# [0] build: bloom-1.1.-win32-static-dependencies (bt326)
#     project: Bloom
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt326
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"connections.dll"=>"DistFiles", "*.chm"=>"DistFiles"}
# [1] build: chorus-precise64-master Continuous (bt323)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt323
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.exe*"=>"lib/dotnet", "*.dll*"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [2] build: palaso-precise64-master Continuous (bt322)
#     project: libpalaso
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt322
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Palaso.BuildTasks.dll"=>"build/", "*.dll*"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/libpalaso.git [master]
# [3] build: PdfDroplet-Win-Dev-Continuous (bt54)
#     project: PdfDroplet
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt54
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"PdfDroplet.exe*"=>"lib/dotnet", "PdfSharp.dll*"=>"lib/dotnet"}
#     VCS: http://hg.palaso.org/pdfdroplet [default]
# [4] build: TidyManaged-master-precise64-continuous (bt351)
#     project: TidyManaged
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt351
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"TidyManaged.dll*"=>"lib/dotnet"}
#     VCS: https://github.com/hatton/TidyManaged.git [master]

# make sure output directories exist
mkdir -p ../DistFiles
mkdir -p ../lib/dotnet
mkdir -p ../build/

# download artifact dependencies
copy_auto http://build.palaso.org/guestAuth/repository/download/bt326/latest.lastSuccessful/connections.dll ../DistFiles/connections.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt326/latest.lastSuccessful/Bloom.chm ../DistFiles/Bloom.chm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Chorus.exe ../lib/dotnet/Chorus.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Chorus.exe.mdb ../lib/dotnet/Chorus.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusHub.exe ../lib/dotnet/ChorusHub.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusHub.exe.mdb ../lib/dotnet/ChorusHub.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusMerge.exe ../lib/dotnet/ChorusMerge.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusMerge.exe.mdb ../lib/dotnet/ChorusMerge.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/Chorus.exe ../lib/dotnet/Chorus.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/Chorus.exe.mdb ../lib/dotnet/Chorus.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/ChorusHub.exe ../lib/dotnet/ChorusHub.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/ChorusHub.exe.mdb ../lib/dotnet/ChorusHub.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/ChorusMerge.exe ../lib/dotnet/ChorusMerge.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/ChorusMerge.exe.mdb ../lib/dotnet/ChorusMerge.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Autofac.dll ../lib/dotnet/Autofac.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Geckofx-Winforms-14.dll ../lib/dotnet/Geckofx-Winforms-14.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.TestUtilities.dll ../lib/dotnet/LibChorus.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.TestUtilities.dll.mdb ../lib/dotnet/LibChorus.TestUtilities.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.dll ../lib/dotnet/LibChorus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.dll.mdb ../lib/dotnet/LibChorus.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/NDesk.DBus.dll ../lib/dotnet/NDesk.DBus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/NDesk.DBus.dll.config ../lib/dotnet/NDesk.DBus.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/Autofac.dll ../lib/dotnet/Autofac.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/Geckofx-Winforms-14.dll ../lib/dotnet/Geckofx-Winforms-14.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/LibChorus.TestUtilities.dll ../lib/dotnet/LibChorus.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/LibChorus.TestUtilities.dll.mdb ../lib/dotnet/LibChorus.TestUtilities.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/LibChorus.dll ../lib/dotnet/LibChorus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/LibChorus.dll.mdb ../lib/dotnet/LibChorus.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/NDesk.DBus.dll ../lib/dotnet/NDesk.DBus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/NDesk.DBus.dll.config ../lib/dotnet/NDesk.DBus.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/geckofx-core-14.dll ../lib/dotnet/geckofx-core-14.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/debug/geckofx-core-14.dll.config ../lib/dotnet/geckofx-core-14.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/geckofx-core-14.dll ../lib/dotnet/geckofx-core-14.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/geckofx-core-14.dll.config ../lib/dotnet/geckofx-core-14.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.BuildTasks.dll ../build/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Enchant.Net.dll ../lib/dotnet/Enchant.Net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Enchant.Net.dll.config ../lib/dotnet/Enchant.Net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Ionic.Zip.dll ../lib/dotnet/Ionic.Zip.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/L10NSharp.dll ../lib/dotnet/L10NSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/NDesk.DBus.dll ../lib/dotnet/NDesk.DBus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/NDesk.DBus.dll.config ../lib/dotnet/NDesk.DBus.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.BuildTasks.dll ../lib/dotnet/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.DictionaryServices.dll ../lib/dotnet/Palaso.DictionaryServices.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.DictionaryServices.dll.mdb ../lib/dotnet/Palaso.DictionaryServices.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Lift.dll ../lib/dotnet/Palaso.Lift.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Lift.dll.mdb ../lib/dotnet/Palaso.Lift.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Media.dll ../lib/dotnet/Palaso.Media.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Media.dll.config ../lib/dotnet/Palaso.Media.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Media.dll.mdb ../lib/dotnet/Palaso.Media.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.TestUtilities.dll ../lib/dotnet/Palaso.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.TestUtilities.dll.mdb ../lib/dotnet/Palaso.TestUtilities.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Tests.dll ../lib/dotnet/Palaso.Tests.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.Tests.dll.mdb ../lib/dotnet/Palaso.Tests.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.dll.config ../lib/dotnet/Palaso.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/Palaso.dll.mdb ../lib/dotnet/Palaso.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.dll.config ../lib/dotnet/PalasoUIWindowsForms.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.dll.mdb ../lib/dotnet/PalasoUIWindowsForms.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/SIL.Archiving.dll.config ../lib/dotnet/SIL.Archiving.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/SIL.Archiving.dll.mdb ../lib/dotnet/SIL.Archiving.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Enchant.Net.dll ../lib/dotnet/Enchant.Net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Enchant.Net.dll.config ../lib/dotnet/Enchant.Net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Ionic.Zip.dll ../lib/dotnet/Ionic.Zip.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/L10NSharp.dll ../lib/dotnet/L10NSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/NDesk.DBus.dll ../lib/dotnet/NDesk.DBus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/NDesk.DBus.dll.config ../lib/dotnet/NDesk.DBus.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.BuildTasks.dll ../lib/dotnet/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.DictionaryServices.dll ../lib/dotnet/Palaso.DictionaryServices.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.DictionaryServices.dll.mdb ../lib/dotnet/Palaso.DictionaryServices.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Lift.dll ../lib/dotnet/Palaso.Lift.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Lift.dll.mdb ../lib/dotnet/Palaso.Lift.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Media.dll ../lib/dotnet/Palaso.Media.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Media.dll.config ../lib/dotnet/Palaso.Media.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Media.dll.mdb ../lib/dotnet/Palaso.Media.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.TestUtilities.dll ../lib/dotnet/Palaso.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.TestUtilities.dll.mdb ../lib/dotnet/Palaso.TestUtilities.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Tests.dll ../lib/dotnet/Palaso.Tests.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.Tests.dll.mdb ../lib/dotnet/Palaso.Tests.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.dll.config ../lib/dotnet/Palaso.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/Palaso.dll.mdb ../lib/dotnet/Palaso.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/PalasoUIWindowsForms.dll.config ../lib/dotnet/PalasoUIWindowsForms.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/PalasoUIWindowsForms.dll.mdb ../lib/dotnet/PalasoUIWindowsForms.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/SIL.Archiving.dll.config ../lib/dotnet/SIL.Archiving.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/SIL.Archiving.dll.mdb ../lib/dotnet/SIL.Archiving.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/ibusdotnet.dll ../lib/dotnet/ibusdotnet.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/icu.net.dll ../lib/dotnet/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/debug/icu.net.dll.config ../lib/dotnet/icu.net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/ibusdotnet.dll ../lib/dotnet/ibusdotnet.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/icu.net.dll ../lib/dotnet/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/icu.net.dll.config ../lib/dotnet/icu.net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfDroplet.exe ../lib/dotnet/PdfDroplet.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfSharp.dll ../lib/dotnet/PdfSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt351/latest.lastSuccessful/TidyManaged.dll ../lib/dotnet/TidyManaged.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt351/latest.lastSuccessful/TidyManaged.dll.config ../lib/dotnet/TidyManaged.dll.config
# End of script
