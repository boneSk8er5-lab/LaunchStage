@echo off
rem Builds LaunchStage into the "out" folder next to this file:
rem   LaunchStage.exe     the app (launcher window + tray icon)
rem   LaunchStageCli.exe  the command-line tool
rem The command-line tool builds first so it can never clean away the app.

rem Ask a running LaunchStage to exit (gently) so its files can be replaced, then give it a moment.
if exist "%~dp0out\LaunchStage.exe" (
    echo Closing LaunchStage if it's running...
    "%~dp0out\LaunchStage.exe" --exit
    ping -n 4 127.0.0.1 >nul
)

echo Building the command-line tool...
dotnet publish "%~dp0src\LaunchStage.Cli\LaunchStage.Cli.csproj" -c Release -o "%~dp0out"
if errorlevel 1 goto failed

echo.
echo Building the app...
dotnet publish "%~dp0src\LaunchStage.App\LaunchStage.App.csproj" -c Release -o "%~dp0out"
if errorlevel 1 goto failed

echo.
echo Built: %~dp0out\LaunchStage.exe
pause
exit /b 0

:failed
echo.
echo Build failed.
echo If LaunchStage is running, right-click its tray icon, choose Exit, and run this again.
echo Otherwise copy the red errors above and send them to Claude.
pause
exit /b 1
