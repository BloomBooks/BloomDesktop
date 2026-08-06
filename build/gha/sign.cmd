@echo off
rem Shim so that the `sign "<file>"` commands in build/Bloom.proj work on GitHub Actions
rem runners (the release-installer workflow puts this directory on PATH). See sign.ps1.
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign.ps1" %*
exit /b %ERRORLEVEL%
