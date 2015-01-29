#!/bin/bash
# server=build.palaso.org
# project=Bloom
# build=Bloom-Default-precise64-Auto (master)
# root_dir=..
# $Id: d32984f53cd52f171a9cba46cd3879538ad23431 $

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

shift $((OPTIND - 1))

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

# clean destination directories
rm -rf ../src/BloomBrowserUI/bookEdit/js/libsynphony
rm -rf ../src/BloomBrowserUI/bookEdit/test/libsynphony

# *** Results ***
# build: Bloom-Default-precise64-Auto (master) (bt403)
# project: Bloom
# URL: http://build.palaso.org/viewType.html?buildTypeId=bt403
# VCS: git://github.com/BloomBooks/BloomDesktop.git [master]
# dependencies:
# [0] build: bloom-3.-win32-static-dependencies (bt396)
#     project: Bloom
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt396
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"connections.dll"=>"DistFiles", "*.chm"=>"DistFiles", "MSBuild.Community.Tasks.dll"=>"build/", "MSBuild.Community.Tasks.Targets"=>"build/"}
# [1] build: LibSynphony (bt394)
#     project: Bloom
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt394
#     clean: true
#     revision: latest.lastSuccessful
#     paths: {"libsynphony-js.zip!**"=>"src/BloomBrowserUI/bookEdit/js/libsynphony", "libsynphony-test-js.zip!**"=>"src/BloomBrowserUI/bookEdit/test/libsynphony"}
#     VCS: https://bitbucket.org/phillip_hopper/synphony [default]
# [2] build: pdf.js (bt401)
#     project: BuildTasks
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt401
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"pdfjs-viewer.zip!**"=>"DistFiles/pdf"}
#     VCS: https://github.com/mozilla/pdf.js.git [gh-pages]
# [3] build: chorus-precise64-master Continuous (bt323)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt323
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.exe*"=>"lib/dotnet", "*.dll*"=>"lib/dotnet", "Mercurial-x86_64.zip!**"=>"Mercurial-x86_64", "Mercurial-i686.zip!**"=>"Mercurial-i686"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [4] build: palaso-precise64-master Continuous (bt322)
#     project: libpalaso
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt322
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Palaso.BuildTasks.dll"=>"build/", "*.dll*"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/libpalaso.git [master]
# [5] build: icucil-precise64-Continuous (bt281)
#     project: Libraries
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt281
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"icu.net.*"=>"lib/dotnet/icu48"}
#     VCS: https://github.com/sillsdev/icu-dotnet [master]
# [6] build: icucil-precise64-icu52 Continuous (bt413)
#     project: Libraries
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt413
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"icu.net.*"=>"lib/dotnet/icu52"}
#     VCS: https://github.com/sillsdev/icu-dotnet [master]
# [7] build: PdfDroplet-Win-Dev-Continuous (bt54)
#     project: PdfDroplet
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt54
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"PdfDroplet.exe"=>"lib/dotnet", "PdfSharp.dll*"=>"lib/dotnet"}
#     VCS: http://bitbucket.org/hatton/pdfdroplet [default]
# [8] build: TidyManaged-master-precise64-continuous (bt351)
#     project: TidyManaged
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt351
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"TidyManaged.dll*"=>"lib/dotnet"}
#     VCS: https://github.com/hatton/TidyManaged.git [master]

# make sure output directories exist
mkdir -p ../DistFiles
mkdir -p ../DistFiles/pdf
mkdir -p ../Downloads
mkdir -p ../Mercurial-i686
mkdir -p ../Mercurial-x86_64
mkdir -p ../build/
mkdir -p ../lib/dotnet
mkdir -p ../lib/dotnet/icu48
mkdir -p ../lib/dotnet/icu52
mkdir -p ../src/BloomBrowserUI/bookEdit/js/libsynphony
mkdir -p ../src/BloomBrowserUI/bookEdit/test/libsynphony

# download artifact dependencies
copy_auto http://build.palaso.org/guestAuth/repository/download/bt396/latest.lastSuccessful/connections.dll ../DistFiles/connections.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt396/latest.lastSuccessful/Bloom.chm ../DistFiles/Bloom.chm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt396/latest.lastSuccessful/MSBuild.Community.Tasks.dll ../build/MSBuild.Community.Tasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt396/latest.lastSuccessful/MSBuild.Community.Tasks.Targets ../build/MSBuild.Community.Tasks.Targets
copy_auto http://build.palaso.org/guestAuth/repository/download/bt394/latest.lastSuccessful/libsynphony-js.zip ../Downloads/libsynphony-js.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt394/latest.lastSuccessful/libsynphony-test-js.zip ../Downloads/libsynphony-test-js.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt401/latest.lastSuccessful/pdfjs-viewer.zip ../Downloads/pdfjs-viewer.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Chorus.exe ../lib/dotnet/Chorus.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Chorus.exe.mdb ../lib/dotnet/Chorus.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusHub.exe ../lib/dotnet/ChorusHub.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusHub.exe.mdb ../lib/dotnet/ChorusHub.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusHubApp.exe ../lib/dotnet/ChorusHubApp.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusHubApp.exe.mdb ../lib/dotnet/ChorusHubApp.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusMerge.exe ../lib/dotnet/ChorusMerge.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/ChorusMerge.exe.mdb ../lib/dotnet/ChorusMerge.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Autofac.dll ../lib/dotnet/Autofac.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.TestUtilities.dll ../lib/dotnet/LibChorus.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.TestUtilities.dll.mdb ../lib/dotnet/LibChorus.TestUtilities.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.dll ../lib/dotnet/LibChorus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/LibChorus.dll.mdb ../lib/dotnet/LibChorus.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/NDesk.DBus.dll ../lib/dotnet/NDesk.DBus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/NDesk.DBus.dll.config ../lib/dotnet/NDesk.DBus.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Vulcan.Uczniowie.HelpProvider.dll ../lib/dotnet/Vulcan.Uczniowie.HelpProvider.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Mercurial-x86_64.zip ../Downloads/Mercurial-x86_64.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/latest.lastSuccessful/Mercurial-i686.zip ../Downloads/Mercurial-i686.zip
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
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.GeckoBrowserAdapter.dll ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.GeckoBrowserAdapter.dll.mdb ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.dll.config ../lib/dotnet/PalasoUIWindowsForms.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/PalasoUIWindowsForms.dll.mdb ../lib/dotnet/PalasoUIWindowsForms.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/SIL.Archiving.dll.config ../lib/dotnet/SIL.Archiving.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/SIL.Archiving.dll.mdb ../lib/dotnet/SIL.Archiving.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/ibusdotnet.dll ../lib/dotnet/ibusdotnet.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/latest.lastSuccessful/taglib-sharp.dll ../lib/dotnet/taglib-sharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt281/latest.lastSuccessful/icu.net.dll ../lib/dotnet/icu48/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt281/latest.lastSuccessful/icu.net.dll.config ../lib/dotnet/icu48/icu.net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt281/latest.lastSuccessful/icu.net.dll.mdb ../lib/dotnet/icu48/icu.net.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt413/latest.lastSuccessful/icu.net.dll ../lib/dotnet/icu52/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt413/latest.lastSuccessful/icu.net.dll.config ../lib/dotnet/icu52/icu.net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt413/latest.lastSuccessful/icu.net.dll.mdb ../lib/dotnet/icu52/icu.net.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfDroplet.exe ../lib/dotnet/PdfDroplet.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfSharp.dll ../lib/dotnet/PdfSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt351/latest.lastSuccessful/TidyManaged.dll ../lib/dotnet/TidyManaged.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt351/latest.lastSuccessful/TidyManaged.dll.config ../lib/dotnet/TidyManaged.dll.config
# extract downloaded zip files
unzip -uqo ../Downloads/libsynphony-js.zip -d ../src/BloomBrowserUI/bookEdit/js/libsynphony
unzip -uqo ../Downloads/libsynphony-test-js.zip -d ../src/BloomBrowserUI/bookEdit/test/libsynphony
unzip -uqo ../Downloads/pdfjs-viewer.zip -d ../DistFiles/pdf
unzip -uqo ../Downloads/Mercurial-x86_64.zip -d ../Mercurial-x86_64
unzip -uqo ../Downloads/Mercurial-i686.zip -d ../Mercurial-i686
# End of script
