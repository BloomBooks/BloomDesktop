#!/bin/bash
# server=build.palaso.org
# project=Bloom
# build=Bloom 3.0 AllChildrenReading Linux
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
# build: Bloom 3.0 AllChildrenReading Linux (bt420)
# project: Bloom
# URL: http://build.palaso.org/viewType.html?buildTypeId=bt420
# VCS: https://bitbucket.org/hatton/bloom-desktop [bloom-3.0]
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
#     revision: bloom-3.0.tcbuildtag
#     paths: {"pdfjs-viewer.zip!**"=>"DistFiles/pdf"}
#     VCS: https://github.com/mozilla/pdf.js.git [gh-pages]
# [3] build: chorus-precise64-master Continuous (bt323)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt323
#     clean: false
#     revision: bloom-3.0.tcbuildtag
#     paths: {"*.exe*"=>"lib/dotnet", "*.dll*"=>"lib/dotnet", "Mercurial-x86_64.zip!**"=>"Mercurial-x86_64", "Mercurial-i686.zip!**"=>"Mercurial-i686"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [4] build: palaso-precise64-master Continuous (bt322)
#     project: libpalaso
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt322
#     clean: false
#     revision: bloom-3.0.tcbuildtag
#     paths: {"Palaso.BuildTasks.dll"=>"build/", "*.dll*"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/libpalaso.git [master]
# [5] build: icucil-precise64-Continuous (bt281)
#     project: Libraries
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt281
#     clean: false
#     revision: bloom-3.0.tcbuildtag
#     paths: {"icu.net.*"=>"lib/dotnet/icu48"}
#     VCS: https://github.com/sillsdev/icu-dotnet [master]
# [6] build: icucil-precise64-icu52 Continuous (bt413)
#     project: Libraries
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt413
#     clean: false
#     revision: bloom-3.0.tcbuildtag
#     paths: {"icu.net.*"=>"lib/dotnet/icu52"}
#     VCS: https://github.com/sillsdev/icu-dotnet [master]
# [7] build: PdfDroplet-Win-Dev-Continuous (bt54)
#     project: PdfDroplet
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt54
#     clean: false
#     revision: bloom-3.0.tcbuildtag
#     paths: {"PdfDroplet.exe"=>"lib/dotnet", "PdfSharp.dll*"=>"lib/dotnet"}
#     VCS: http://bitbucket.org/hatton/pdfdroplet [default]
# [8] build: TidyManaged-master-precise64-continuous (bt351)
#     project: TidyManaged
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt351
#     clean: false
#     revision: bloom-3.0.tcbuildtag
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
copy_auto http://build.palaso.org/guestAuth/repository/download/bt401/bloom-3.0.tcbuildtag/pdfjs-viewer.zip ../Downloads/pdfjs-viewer.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/Chorus.exe ../lib/dotnet/Chorus.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/Chorus.exe.mdb ../lib/dotnet/Chorus.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/ChorusHub.exe ../lib/dotnet/ChorusHub.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/ChorusHub.exe.mdb ../lib/dotnet/ChorusHub.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/ChorusHubApp.exe ../lib/dotnet/ChorusHubApp.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/ChorusHubApp.exe.mdb ../lib/dotnet/ChorusHubApp.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/ChorusMerge.exe ../lib/dotnet/ChorusMerge.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/ChorusMerge.exe.mdb ../lib/dotnet/ChorusMerge.exe.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/Autofac.dll ../lib/dotnet/Autofac.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/LibChorus.TestUtilities.dll ../lib/dotnet/LibChorus.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/LibChorus.TestUtilities.dll.mdb ../lib/dotnet/LibChorus.TestUtilities.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/LibChorus.dll ../lib/dotnet/LibChorus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/LibChorus.dll.mdb ../lib/dotnet/LibChorus.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/NDesk.DBus.dll ../lib/dotnet/NDesk.DBus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/NDesk.DBus.dll.config ../lib/dotnet/NDesk.DBus.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/Vulcan.Uczniowie.HelpProvider.dll ../lib/dotnet/Vulcan.Uczniowie.HelpProvider.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/Mercurial-x86_64.zip ../Downloads/Mercurial-x86_64.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt323/bloom-3.0.tcbuildtag/Mercurial-i686.zip ../Downloads/Mercurial-i686.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.BuildTasks.dll ../build/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Enchant.Net.dll ../lib/dotnet/Enchant.Net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Enchant.Net.dll.config ../lib/dotnet/Enchant.Net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Ionic.Zip.dll ../lib/dotnet/Ionic.Zip.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/L10NSharp.dll ../lib/dotnet/L10NSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/NDesk.DBus.dll ../lib/dotnet/NDesk.DBus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/NDesk.DBus.dll.config ../lib/dotnet/NDesk.DBus.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.BuildTasks.dll ../lib/dotnet/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.DictionaryServices.dll ../lib/dotnet/Palaso.DictionaryServices.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.DictionaryServices.dll.mdb ../lib/dotnet/Palaso.DictionaryServices.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.Lift.dll ../lib/dotnet/Palaso.Lift.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.Lift.dll.mdb ../lib/dotnet/Palaso.Lift.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.Media.dll ../lib/dotnet/Palaso.Media.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.Media.dll.config ../lib/dotnet/Palaso.Media.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.Media.dll.mdb ../lib/dotnet/Palaso.Media.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.TestUtilities.dll ../lib/dotnet/Palaso.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.TestUtilities.dll.mdb ../lib/dotnet/Palaso.TestUtilities.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.Tests.dll ../lib/dotnet/Palaso.Tests.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.Tests.dll.mdb ../lib/dotnet/Palaso.Tests.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.dll.config ../lib/dotnet/Palaso.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/Palaso.dll.mdb ../lib/dotnet/Palaso.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/PalasoUIWindowsForms.GeckoBrowserAdapter.dll ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/PalasoUIWindowsForms.GeckoBrowserAdapter.dll.mdb ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/PalasoUIWindowsForms.dll.config ../lib/dotnet/PalasoUIWindowsForms.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/PalasoUIWindowsForms.dll.mdb ../lib/dotnet/PalasoUIWindowsForms.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/SIL.Archiving.dll.config ../lib/dotnet/SIL.Archiving.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/SIL.Archiving.dll.mdb ../lib/dotnet/SIL.Archiving.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Enchant.Net.dll ../lib/dotnet/Enchant.Net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Enchant.Net.dll.config ../lib/dotnet/Enchant.Net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Ionic.Zip.dll ../lib/dotnet/Ionic.Zip.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/L10NSharp.dll ../lib/dotnet/L10NSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/NDesk.DBus.dll ../lib/dotnet/NDesk.DBus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/NDesk.DBus.dll.config ../lib/dotnet/NDesk.DBus.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.BuildTasks.dll ../lib/dotnet/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.DictionaryServices.dll ../lib/dotnet/Palaso.DictionaryServices.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.DictionaryServices.dll.mdb ../lib/dotnet/Palaso.DictionaryServices.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.Lift.dll ../lib/dotnet/Palaso.Lift.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.Lift.dll.mdb ../lib/dotnet/Palaso.Lift.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.Media.dll ../lib/dotnet/Palaso.Media.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.Media.dll.config ../lib/dotnet/Palaso.Media.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.Media.dll.mdb ../lib/dotnet/Palaso.Media.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.TestUtilities.dll ../lib/dotnet/Palaso.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.TestUtilities.dll.mdb ../lib/dotnet/Palaso.TestUtilities.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.Tests.dll ../lib/dotnet/Palaso.Tests.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.Tests.dll.mdb ../lib/dotnet/Palaso.Tests.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.dll.config ../lib/dotnet/Palaso.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/Palaso.dll.mdb ../lib/dotnet/Palaso.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/PalasoUIWindowsForms.GeckoBrowserAdapter.dll ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/PalasoUIWindowsForms.GeckoBrowserAdapter.dll.mdb ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/PalasoUIWindowsForms.dll.config ../lib/dotnet/PalasoUIWindowsForms.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/PalasoUIWindowsForms.dll.mdb ../lib/dotnet/PalasoUIWindowsForms.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/SIL.Archiving.dll.config ../lib/dotnet/SIL.Archiving.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/SIL.Archiving.dll.mdb ../lib/dotnet/SIL.Archiving.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/ibusdotnet.dll ../lib/dotnet/ibusdotnet.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/icu.net.dll ../lib/dotnet/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/icu.net.dll.config ../lib/dotnet/icu.net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/debug/taglib-sharp.dll ../lib/dotnet/taglib-sharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/ibusdotnet.dll ../lib/dotnet/ibusdotnet.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt322/bloom-3.0.tcbuildtag/taglib-sharp.dll ../lib/dotnet/taglib-sharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt281/bloom-3.0.tcbuildtag/icu.net.dll ../lib/dotnet/icu48/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt281/bloom-3.0.tcbuildtag/icu.net.dll.config ../lib/dotnet/icu48/icu.net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt281/bloom-3.0.tcbuildtag/icu.net.dll.mdb ../lib/dotnet/icu48/icu.net.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt413/bloom-3.0.tcbuildtag/icu.net.dll ../lib/dotnet/icu52/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt413/bloom-3.0.tcbuildtag/icu.net.dll.config ../lib/dotnet/icu52/icu.net.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt413/bloom-3.0.tcbuildtag/icu.net.dll.mdb ../lib/dotnet/icu52/icu.net.dll.mdb
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/bloom-3.0.tcbuildtag/PdfDroplet.exe ../lib/dotnet/PdfDroplet.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/bloom-3.0.tcbuildtag/PdfSharp.dll ../lib/dotnet/PdfSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt351/bloom-3.0.tcbuildtag/TidyManaged.dll ../lib/dotnet/TidyManaged.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt351/bloom-3.0.tcbuildtag/TidyManaged.dll.config ../lib/dotnet/TidyManaged.dll.config
# extract downloaded zip files
unzip -uqo ../Downloads/libsynphony-js.zip -d ../src/BloomBrowserUI/bookEdit/js/libsynphony
unzip -uqo ../Downloads/libsynphony-test-js.zip -d ../src/BloomBrowserUI/bookEdit/test/libsynphony
unzip -uqo ../Downloads/pdfjs-viewer.zip -d ../DistFiles/pdf
unzip -uqo ../Downloads/Mercurial-x86_64.zip -d ../Mercurial-x86_64
unzip -uqo ../Downloads/Mercurial-i686.zip -d ../Mercurial-i686
# End of script
