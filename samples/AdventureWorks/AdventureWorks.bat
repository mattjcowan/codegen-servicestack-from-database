echo off
set cenv=%cd%
set cpath=%~dp0
CALL :NORMALIZEPATH "%cpath%\..\..\lib\cs-script\"
SET CSSCRIPT_DIR=%RETVAL%
"%cpath%\..\..\lib\cs-script\cscs.exe" /sconfig "%cpath%\..\..\codegen.cs" -ns="AdventureWorks" -dialect="SqlServer" -connectionstring="Data Source=.\sqlexpress;Initial Catalog=AdventureWorks2014;Integrated Security=True" -output="%cpath%\" -templates="%cpath%\..\..\templates" 
cd %cenv%
goto End

:: ========== FUNCTIONS ==========
EXIT /B

:NORMALIZEPATH
  SET RETVAL=%~dpfn1
  EXIT /B

:End
timeout /t 3