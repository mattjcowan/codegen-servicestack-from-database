@echo off
set cenv=%cd%
set cpath=%~dp0
cd /d %cpath%
if exist "%cpath%\cs-script\" goto X
"%cpath%\7za" x -y "%cpath%\cs-script.7z"
"%cpath%\7za" x -y "%cpath%\cs-script.ExtensionPack.7z"

:X
cd %cenv%