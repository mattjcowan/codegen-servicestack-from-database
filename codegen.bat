@echo off
set cenv=%cd%
set cpath=%~dp0

set CSSCRIPT_DIR=%cpath%lib\cs-script
set path=%CSSCRIPT_DIR%;%path%

set App=""
set Dialect=""
set ConnectionString=""
set Output=""

set /p App= What is the name/namespace for your application? 
if "%App%"=="" goto End
set App=%App: =%

:Dialect
set /p Dialect= What is the database dialect (SqlServer, SqlServerCe, PostgreSQL, MySql, Oracle, SqLite)? 
if "%Dialect%"=="" goto End
set Dialect=%Dialect: =%

:ConnectionString
set /p ConnectionString= What is the connection string to your database? 
if "%ConnectionString%"=="" goto End

:Output
set /p Output= Where would you like your code to be output? 
if "%Output%"=="" goto End
set RETVAL=""
CALL :NORMALIZEPATH "%Output%"
SET Output=%RETVAL%

:Start
mkdir "%Output%"
echo "%CSSCRIPT_DIR%\cscs.exe" /sconfig "%cpath%codegen.cs" -ns="%App%" -dialect="%Dialect%" -connectionstring="%ConnectionString%" -output="%Output%" -templates="%cpath%templates" > "%Output%\codegen-%App%.bat"
"%CSSCRIPT_DIR%\cscs.exe" /sconfig "%cpath%codegen.cs" -ns="%App%" -dialect="%Dialect%" -connectionstring="%ConnectionString%" -output="%Output%" -templates="%cpath%templates"
goto Done

:End
echo Aborting, missing information ...

:Done
cd %cenv%

:: ========== FUNCTIONS ==========
EXIT /B

:NORMALIZEPATH
  SET RETVAL=%~dpfn1
  EXIT /B
