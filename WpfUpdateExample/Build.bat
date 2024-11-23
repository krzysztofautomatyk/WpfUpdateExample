@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version]
    exit /b 1
)

set "version=%~1"
set "output_dir=C:\Update"


echo.
echo Compiling WpfUpdateExample with dotnet...
dotnet publish -c Release -o %~dp0publish

echo.
echo Building Velopack Release v%version%
vpk pack -u WpfUpdateExample -v %version% -o %~dp0releases -p %~dp0publish -f net8-x64-desktop

echo.
echo Copying files from releases to %output_dir%...
if not exist "%output_dir%" mkdir "%output_dir%"
xcopy /E /Y /Q "%~dp0releases\*" "%output_dir%\"

echo.
echo Copy operation completed!