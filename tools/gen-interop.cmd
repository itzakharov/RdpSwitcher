@echo off
rem Generates MSTSCLib.dll and AxMSTSCLib.dll into RdpSwitcher\Interop.
rem Run from a Developer Command Prompt (aximp.exe is part of the Windows SDK .NET tools).
setlocal
set OUT=%~dp0..\RdpSwitcher\Interop
if not exist "%OUT%" mkdir "%OUT%"
pushd "%OUT%"
aximp.exe "%WINDIR%\System32\mstscax.dll"
set RC=%ERRORLEVEL%
popd
if not "%RC%"=="0" (
  echo aximp failed with code %RC%.
  echo Run this script from a Developer Command Prompt for VS, or add the Windows SDK "NETFX 4.8 Tools" folder to PATH.
  exit /b %RC%
)
echo Generated into %OUT%
endlocal
