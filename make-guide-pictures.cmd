@echo off
rem Takes the pictures for the guide (guide\images\*.png) from LaunchStage's real windows, drawn off-screen with
rem made-up demo profiles, so the guide never shows your own profiles, windows, games or folders.
rem Run it after changing how a window looks, then run build.cmd so the app gets the new pictures.

echo Taking the guide's pictures...
dotnet run --project "%~dp0tools\GuidePictures\GuidePictures.csproj" -c Release
if errorlevel 1 goto failed

echo.
echo Done. Now run build.cmd so LaunchStage's Help shows the new pictures.
pause
exit /b 0

:failed
echo.
echo Taking the pictures failed. Copy the red errors above and send them to Claude.
pause
exit /b 1
