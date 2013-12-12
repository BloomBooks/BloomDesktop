@echo off
REM Usage: BloomJade mytemplate.jade --someargument someargument
REM the %~2 %~3 here are just places for optional arguments to get
REM passed through to JADE. If they're missing, no problem.
REM The %~1 is the jade file name. 
call jade --pretty %~2 %~3 "%~1"   >&2
if errorlevel 1 goto :eof
call del "%~n1.htm"
rename "%~n1.html"  "%~n1.htm"
echo Saved as "%~n1.htm"