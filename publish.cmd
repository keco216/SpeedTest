@echo off
setlocal
cd /d "%~dp0"

echo === 1/2: SpeedTest.Gui veroeffentlichen (self-contained, eine EXE) ===
dotnet publish SpeedTest.Gui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 exit /b 1

echo.
echo === 2/2: MSI-Installer bauen ===
dotnet build installer -c Release
if errorlevel 1 exit /b 1

echo.
echo Fertig. MSI liegt unter installer\bin\Release\
dir /b installer\bin\Release\*.msi
endlocal
