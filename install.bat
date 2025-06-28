@rem instal batch demo
whoami /priv | find "SeDebugPrivilege" > nul
if %errorlevel% neq 0 (
	@powershell start-process Åh%~0" -verb runas
	exit
)
@echo off
cls
SET "APP=StorageDeviceMonitorService.exe"
SET "InstallSUBDIR=StorageDeviceMonitor"

SET "InstallDIR=%ProgramFiles%\%InstallSUBDIR%"
if "%ProgramFiles(x86)%" neq "" (
	SET "InstallDIR=%ProgramFiles(x86)%\%InstallSUBDIR%"
)

FOR /F "usebackq delims=" %%i IN (`dir /b /s "%windir%\Microsoft.NET\Framework\installutil.exe"`) DO "InstallUtil=%%i"


if /i "%~1" equ "/i" goto :pInstall
if /i "%~1" equ "/u" goto :pUninstall

echo [1] Install Service
echo [2] Uninstall Service
echo -----
echo [0] Cancel
echo.
set i=null
:i
set /p i=[1,2,0]? 
if /i "%i%" equ "1" goto :pInstall
if /i "%i%" equ "2" goto :pUninstall
if /i "%i%" equ "0" exit /b
goto :i

:pInstall
MKDIR "%InstallDIR"
copy /i "%APP%" "%InstallDIR%"
"%InstallUtil%" "%InstallDIR%\%APP%"
if "%~1" equ "" pause
exit /b

:pInstall
"%InstallUtil%" "%InstallDIR%\%APP%"
if "%~1" equ "" pause
exit /b
