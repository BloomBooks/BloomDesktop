#!/bin/bash
# server=build.palaso.org
# build_type=bt392
# root_dir=..
# $Id: 4634ff740f8de8637c6b5cc04f4a1b2ee0954b5d $

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


# *** Results ***
# build: Bloom 2 Publish (bt392)
# project: Bloom
# URL: http://build.palaso.org/viewType.html?buildTypeId=bt392
# VCS: https://bitbucket.org/hatton/bloom-desktop [Version2.0]
# dependencies:
# [0] build: bloom-2.0.-win32-static-dependencies (bt326)
#     project: Bloom
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt326
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"wkhtmltopdf-0.10.0_rc2.zip!**"=>"lib", "connections.dll"=>"DistFiles", "*.chm"=>"DistFiles"}
# [1] build: chorus-win32-master Continuous (bt2)
#     project: Chorus
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt2
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"policy_9_0_Microsoft_VC90_CRT_x86.msm"=>"build\\ChorusInstallerStuff", "Vulcan.Uczniowie.HelpProvider.dll"=>"output/release", "Microsoft_VC90_CRT_x86.msm"=>"build\\ChorusInstallerStuff", "ChorusMergeModule.msm"=>"build\\ChorusInstallerStuff", "*.exe"=>"lib/dotnet", "*.dll"=>"lib/dotnet", "Mercurial.zip!**"=>"Mercurial", "MercurialExtensions/**"=>"MercurialExtensions"}
#     VCS: https://github.com/sillsdev/chorus.git [master]
# [2] build: geckofx-11 Continuous (bt143)
#     project: GeckoFx
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt143
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"*.dll"=>"lib/dotnet"}
#     VCS: https://bitbucket.org/hatton/geckofx-11.0 [default]
# [3] build: XulRunner11-win32 (bt332)
#     project: GeckoFx
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt332
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"xulrunner-11.0.en-US.win32.zip!**"=>"lib"}
# [4] build: palaso-win32-master Continuous (bt223)
#     project: libpalaso
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt223
#     clean: false
#     revision: Bloom2.tcbuildtag
#     paths: {"Palaso.BuildTasks.dll"=>"build/", "exiftool/*.*"=>"DistFiles", "*.dll"=>"lib/dotnet"}
#     VCS: https://github.com/sillsdev/libpalaso.git []
# [5] build: PdfDroplet-Win-Dev-Continuous (bt54)
#     project: PdfDroplet
#     URL: http://build.palaso.org/viewType.html?buildTypeId=bt54
#     clean: false
#     revision: latest.lastSuccessful
#     paths: {"PdfDroplet.exe"=>"lib/dotnet", "PdfSharp.dll"=>"lib/dotnet"}
#     VCS: http://hg.palaso.org/pdfdroplet [default]

# make sure output directories exist
mkdir -p ../DistFiles
mkdir -p ../Mercurial
mkdir -p ../MercurialExtensions
mkdir -p ../MercurialExtensions/fixutf8
mkdir -p ../build/
mkdir -p ../build/ChorusInstallerStuff
mkdir -p ../lib
mkdir -p ../lib/dotnet
mkdir -p ../output/release

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
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/Mercurial.zip ../Mercurial/Mercurial.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/.guidsForInstaller.xml ../MercurialExtensions/.guidsForInstaller.xml
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/Dummy.txt ../MercurialExtensions/Dummy.txt
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/.gitignore ../MercurialExtensions/fixutf8/.gitignore
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/.guidsForInstaller.xml ../MercurialExtensions/fixutf8/.guidsForInstaller.xml
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/.hg_archival.txt ../MercurialExtensions/fixutf8/.hg_archival.txt
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/.hgignore ../MercurialExtensions/fixutf8/.hgignore
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/README. ../MercurialExtensions/fixutf8/README.
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/buildcpmap.py ../MercurialExtensions/fixutf8/buildcpmap.py
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/cpmap.pyc ../MercurialExtensions/fixutf8/cpmap.pyc
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/fixutf8.py ../MercurialExtensions/fixutf8/fixutf8.py
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/fixutf8.pyc ../MercurialExtensions/fixutf8/fixutf8.pyc
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/fixutf8.pyo ../MercurialExtensions/fixutf8/fixutf8.pyo
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/osutil.py ../MercurialExtensions/fixutf8/osutil.py
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/osutil.pyc ../MercurialExtensions/fixutf8/osutil.pyc
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/osutil.pyo ../MercurialExtensions/fixutf8/osutil.pyo
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/win32helper.py ../MercurialExtensions/fixutf8/win32helper.py
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/win32helper.pyc ../MercurialExtensions/fixutf8/win32helper.pyc
copy_auto http://build.palaso.org/guestAuth/repository/download/bt2/latest.lastSuccessful/MercurialExtensions/fixutf8/win32helper.pyo ../MercurialExtensions/fixutf8/win32helper.pyo
copy_auto http://build.palaso.org/guestAuth/repository/download/bt143/latest.lastSuccessful/geckofx-11.dll ../lib/dotnet/geckofx-11.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt332/latest.lastSuccessful/xulrunner-11.0.en-US.win32.zip ../lib/xulrunner-11.0.en-US.win32.zip
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Palaso.BuildTasks.dll ../build/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/exiftool/exiftool.exe ../DistFiles/exiftool.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Interop.WIA.dll ../lib/dotnet/Interop.WIA.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Ionic.Zip.dll ../lib/dotnet/Ionic.Zip.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/L10NSharp.dll ../lib/dotnet/L10NSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Palaso.BuildTasks.dll ../lib/dotnet/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Palaso.DictionaryServices.dll ../lib/dotnet/Palaso.DictionaryServices.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Palaso.Lift.dll ../lib/dotnet/Palaso.Lift.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Palaso.Media.dll ../lib/dotnet/Palaso.Media.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Palaso.TestUtilities.dll ../lib/dotnet/Palaso.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Palaso.Tests.dll ../lib/dotnet/Palaso.Tests.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/PalasoUIWindowsForms.GeckoBrowserAdapter.dll ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/Interop.WIA.dll ../lib/dotnet/Interop.WIA.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/Ionic.Zip.dll ../lib/dotnet/Ionic.Zip.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/L10NSharp.dll ../lib/dotnet/L10NSharp.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/Palaso.BuildTasks.dll ../lib/dotnet/Palaso.BuildTasks.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/Palaso.DictionaryServices.dll ../lib/dotnet/Palaso.DictionaryServices.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/Palaso.Lift.dll ../lib/dotnet/Palaso.Lift.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/Palaso.Media.dll ../lib/dotnet/Palaso.Media.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/Palaso.TestUtilities.dll ../lib/dotnet/Palaso.TestUtilities.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/Palaso.Tests.dll ../lib/dotnet/Palaso.Tests.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/Palaso.dll ../lib/dotnet/Palaso.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/PalasoUIWindowsForms.GeckoBrowserAdapter.dll ../lib/dotnet/PalasoUIWindowsForms.GeckoBrowserAdapter.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/PalasoUIWindowsForms.dll ../lib/dotnet/PalasoUIWindowsForms.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/SIL.Archiving.dll ../lib/dotnet/SIL.Archiving.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/icu.net.dll ../lib/dotnet/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/icudt40.dll ../lib/dotnet/icudt40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/icuin40.dll ../lib/dotnet/icuin40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/debug/icuuc40.dll ../lib/dotnet/icuuc40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/icu.net.dll ../lib/dotnet/icu.net.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/icudt40.dll ../lib/dotnet/icudt40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/icuin40.dll ../lib/dotnet/icuin40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt223/Bloom2.tcbuildtag/icuuc40.dll ../lib/dotnet/icuuc40.dll
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfDroplet.exe ../lib/dotnet/PdfDroplet.exe
copy_auto http://build.palaso.org/guestAuth/repository/download/bt54/latest.lastSuccessful/PdfSharp.dll ../lib/dotnet/PdfSharp.dll
# extract downloaded zip files
unzip -uqo ../lib/wkhtmltopdf-0.10.0_rc2.zip -d ../lib
unzip -uqo ../Mercurial/Mercurial.zip -d ../Mercurial
unzip -uqo ../lib/xulrunner-11.0.en-US.win32.zip -d ../lib
# End of script
