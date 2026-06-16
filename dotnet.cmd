@echo off
setlocal

set "REQUIRED_VERSION=10.0.301"
set "DOTNET_EXE=%USERPROFILE%\.dotnet\dotnet.exe"

if not exist "%DOTNET_EXE%" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-sdk.ps1"
  if errorlevel 1 exit /b %errorlevel%
)

for /f "usebackq delims=" %%V in (`"%DOTNET_EXE%" --version`) do set "ACTUAL_VERSION=%%V"
if not "%ACTUAL_VERSION%"=="%REQUIRED_VERSION%" (
  echo Expected .NET SDK %REQUIRED_VERSION%, got "%ACTUAL_VERSION%" from "%DOTNET_EXE%". 1>&2
  exit /b 1
)

"%DOTNET_EXE%" %*
exit /b %errorlevel%
