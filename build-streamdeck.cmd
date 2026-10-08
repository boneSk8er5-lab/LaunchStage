@echo off
rem Builds the Stream Deck plugin (for the Elgato Stream Deck app 7.1 or newer) into:
rem   streamdeck\dist\com.bones84.launchstage.streamDeckPlugin   (double-click it to install)
rem Needs Node.js (https://nodejs.org). The first run downloads Elgato's free plugin tools into
rem the streamdeck folder (about 50 MB). This is separate from build.cmd, which builds LaunchStage itself.

cd /d "%~dp0streamdeck"

if not exist node_modules (
    echo Downloading the plugin tools, first time only...
    call npm install --no-fund --no-audit
    if errorlevel 1 goto failed
)

echo Building the Stream Deck plugin...
call npm run build
if errorlevel 1 goto failed

echo.
echo Packing it...
call npm run pack
if errorlevel 1 goto failed

echo.
echo Built: %~dp0streamdeck\dist\com.bones84.launchstage.streamDeckPlugin
echo Double-click that file on a PC with the Stream Deck app to install it.
pause
exit /b 0

:failed
echo.
echo Building the Stream Deck plugin failed.
echo If Node.js isn't installed, get it from https://nodejs.org and run this again.
echo Otherwise copy the red errors above and send them to Claude.
pause
exit /b 1
