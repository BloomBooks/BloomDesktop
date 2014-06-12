#!/bin/bash
# server=build.palaso.org
# project=Bloom
# build=Bloom-Default-Win32-Auto (Bloom 3)
# root_dir=..
# $Id: 0b75ca980cea444bf053cfdd852cb3e370225ffe $

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
# build: Bloom-Default-Win32-Auto (Bloom 3) (bt222)
# project: Bloom
# URL: http://build.palaso.org/viewType.html?buildTypeId=bt222
# VCS: https://bitbucket.org/hatton/bloom-desktop [default]
# dependencies:
# [0] build: bloom-3.0.-win32-static-dependencies (bt396)
#     project: Bloom
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt396
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"wkhtmltopdf-0.10.0_rc2.zip!**"=>"DistFiles", "geckofx.zip!**"=>"lib/dotnet", "optipng.exe"=>"DistFiles", "connections.dll"=>"DistFiles", "*.chm"=>"DistFiles"}
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
# [3] build: chorus-win32-master Continuous (bt2)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt2
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"policy_9_0_Microsoft_VC90_CRT_x86.msm"=>"build\\ChorusInstallerStuff", "Vulcan.Uczniowie.HelpProvider.dll"=>"output/release", "Microsoft_VC90_CRT_x86.msm"=>"build\\ChorusInstallerStuff", "ChorusMergeModule.msm"=>"build\\ChorusInstallerStuff", "*.exe"=>"lib/dotnet", "*.dll"=>"lib/dotnet", "Mercurial.zip!**"=>"Mercurial", "MercurialExtensions"=>"MercurialExtensions"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [4] build: XulRunner29-win32 (bt400)
#     project: GeckoFx
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt400
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"xulrunner-29.0.en-US.win32.zip!**"=>"lib"}
# [5] build: palaso-win32-master Continuous (bt223)
#     project: libpalaso
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt223
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"Palaso.BuildTasks.dll"=>"build/", "exiftool/*.*"=>"DistFiles", "*.dll"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/libpalaso.git []
# [6] build: PdfDroplet-Win-Dev-Continuous (bt54)
#     project: PdfDroplet
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt54
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"PdfDroplet.exe"=>"lib/dotnet", "PdfSharp.dll"=>"lib/dotnet"}
#     VCS: http://hg.palaso.org/pdfdroplet [default]
# [7] build: TidyManaged-master-win32-continuous (bt349)
#     project: TidyManaged
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt349
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.*"=>"lib/dotnet"}
#     VCS: https://github.com/hatton/TidyManaged.git [master]

# make sure output directories exist
mkdir -p ../DistFiles
mkdir -p ../DistFiles/pdf
mkdir -p ../Downloads
mkdir -p ../Mercurial
mkdir -p ../MercurialExtensions
mkdir -p ../build/
mkdir -p ../build/ChorusInstallerStuff
mkdir -p ../lib
mkdir -p ../lib/dotnet
mkdir -p ../output/release
mkdir -p ../src/BloomBrowserUI/bookEdit/js/libsynphony
mkdir -p ../src/BloomBrowserUI/bookEdit/test/libsynphony

# download artifact dependencies
copy_auto http://build.palaso.org/guestAuth/repository/download/bt396/latest.lastSuccessful/wkhtmltopdf-0.10.0_rc2.zip ../Downloads/wkhtmltopdf-0.10.0_rc2.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt396/latest.lastSuccessful/geckofx.zip ../Downloads/geckofx.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt396/latest.lastSuccessful/optipng.exe ../DistFiles/optipng.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt396/latest.lastSuccessful/connections.dll ../DistFiles/connections.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt396/latest.lastSuccessful/Bloom.chm ../DistFiles/Bloom.chm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt394/latest.lastSuccessful/libsynphony-js.zip ../Downloads/libsynphony-js.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt394/latest.lastSuccessful/libsynphony-test-js.zip ../Downloads/libsynphony-test-js.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt401/latest.lastSuccessful/pdfjs-viewer.zip ../Downloads/pdfjs-viewer.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/policy_9_0_Microsoft_VC90_CRT_x86.msm ../build/ChorusInstallerStuff/policy_9_0_Microsoft_VC90_CRT_x86.msm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Vulcan.Uczniowie.HelpProvider.dll ../output/release/Vulcan.Uczniowie.HelpProvider.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Microsoft_VC90_CRT_x86.msm ../build/ChorusInstallerStuff/Microsoft_VC90_CRT_x86.msm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusMergeModule.msm ../build/ChorusInstallerStuff/ChorusMergeModule.msm
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Chorus.exe ../lib/dotnet/Chorus.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusHub.exe ../lib/dotnet/ChorusHub.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusHubApp.exe ../lib/dotnet/ChorusHubApp.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/ChorusMerge.exe ../lib/dotnet/ChorusMerge.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/debug/Chorus.exe ../lib/dotnet/Chorus.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/debug/ChorusHub.exe ../lib/dotnet/ChorusHub.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/debug/ChorusMerge.exe ../lib/dotnet/ChorusMerge.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Autofac.dll ../lib/dotnet/Autofac.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/LibChorus.TestUtilities.dll ../lib/dotnet/LibChorus.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/LibChorus.dll ../lib/dotnet/LibChorus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Vulcan.Uczniowie.HelpProvider.dll ../lib/dotnet/Vulcan.Uczniowie.HelpProvider.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/debug/Autofac.dll ../lib/dotnet/Autofac.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/debug/LibChorus.TestUtilities.dll ../lib/dotnet/LibChorus.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/debug/LibChorus.dll ../lib/dotnet/LibChorus.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Mercurial.zip ../Downloads/Mercurial.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions ../MercurialExtensions/MercurialExtensions
copy_auto http://build.palaso.org/guestAuth/repository/download/bt400/latest.lastSuccessful/xulrunner-29.0.en-US.win32.zip ../Downloads/xulrunner-29.0.en-US.win32.zip
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
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/PalasoUIWindowsForms.GeckoBrowserAdapter.dll ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/Interop.WIA.dll ../lib/dotnet/Interop.WIA.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/Ionic.Zip.dll ../lib/dotnet/Ionic.Zip.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/L10NSharp.dll ../lib/dotnet/L10NSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/Palaso.BuildTasks.dll ../lib/dotnet/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/Palaso.DictionaryServices.dll ../lib/dotnet/Palaso.DictionaryServices.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/Palaso.Lift.dll ../lib/dotnet/Palaso.Lift.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/Palaso.Media.dll ../lib/dotnet/Palaso.Media.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/Palaso.TestUtilities.dll ../lib/dotnet/Palaso.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/Palaso.Tests.dll ../lib/dotnet/Palaso.Tests.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/PalasoUIWindowsForms.GeckoBrowserAdapter.dll ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/icu.net.dll ../lib/dotnet/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/icudt40.dll ../lib/dotnet/icudt40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/icuin40.dll ../lib/dotnet/icuin40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/debug/icuuc40.dll ../lib/dotnet/icuuc40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icu.net.dll ../lib/dotnet/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icudt40.dll ../lib/dotnet/icudt40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icuin40.dll ../lib/dotnet/icuin40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/latest.lastSuccessful/icuuc40.dll ../lib/dotnet/icuuc40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfDroplet.exe ../lib/dotnet/PdfDroplet.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfSharp.dll ../lib/dotnet/PdfSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt349/latest.lastSuccessful/TidyManaged.dll ../lib/dotnet/TidyManaged.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt349/latest.lastSuccessful/TidyManaged.dll.config ../lib/dotnet/TidyManaged.dll.config
copy_auto http://build.palaso.org/guestAuth/repository/download/bt349/latest.lastSuccessful/libtidy.dll ../lib/dotnet/libtidy.dll
# extract downloaded zip files
unzip -uqo ../Downloads/wkhtmltopdf-0.10.0_rc2.zip -d ../DistFiles
unzip -uqo ../Downloads/geckofx.zip -d ../lib/dotnet
unzip -uqo ../Downloads/libsynphony-js.zip -d ../src/BloomBrowserUI/bookEdit/js/libsynphony
unzip -uqo ../Downloads/libsynphony-test-js.zip -d ../src/BloomBrowserUI/bookEdit/test/libsynphony
unzip -uqo ../Downloads/pdfjs-viewer.zip -d ../DistFiles/pdf
unzip -uqo ../Downloads/Mercurial.zip -d ../Mercurial
unzip -uqo ../Downloads/xulrunner-29.0.en-US.win32.zip -d ../lib
# End of script
