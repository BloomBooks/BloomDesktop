set build_type=Release
if EXIST %1 set build_type=%1

set squirrel=Squirrel.Windows
if NOT EXIST ..\..\%squirrel% set squirrel=squirrel

REM As of 12/5/16, these match the artifacts on TeamCity (http://build.palaso.org/admin/editBuild.html?id=buildType:Bloom_Squirrel)
REM src\Update\bin\Release\*.exe
REM src\Update\bin\Release\*.dll
REM src\Update\bin\Release\*.com
REM src\Update\bin\Release\*.xml
REM src\Setup\bin\Release\*.exe
REM src\SyncReleases\bin\Release\*.exe
REM src\WriteZipToSetup\bin\Release\*.exe

copy /Y ..\..\%squirrel%\src\Update\bin\%build_type%\*.exe  ..\lib\dotnet
copy /Y ..\..\%squirrel%\src\Update\bin\%build_type%\*.dll  ..\lib\dotnet
copy /Y ..\..\%squirrel%\src\Update\bin\%build_type%\*.com  ..\lib\dotnet
copy /Y ..\..\%squirrel%\src\Update\bin\%build_type%\*.xml  ..\lib\dotnet

copy /Y ..\..\%squirrel%\src\Setup\bin\%build_type%\*.exe  ..\lib\dotnet

copy /Y ..\..\%squirrel%\src\SyncReleases\bin\%build_type%\*.exe  ..\lib\dotnet

copy /Y ..\..\%squirrel%\src\WriteZipToSetup\bin\%build_type%\*.exe  ..\lib\dotnet

pause