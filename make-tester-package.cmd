@echo off
rem Makes the zip to send to testers: dist\LaunchStage test.zip
rem It runs on any Windows 10 or 11 PC without installing anything (.NET is included, so it's bigger).
rem Inside: the LaunchStage folder (app + command-line tool + LICENSE.txt), READ ME FIRST.txt, LICENSE.txt,
rem the guide PDF and the Stream Deck plugin (when they've been made with build-guide.cmd / build-streamdeck.cmd).
rem This doesn't touch the "out" folder that build.cmd makes.

set "DIST=%~dp0dist"
set "PKG=%DIST%\package"
set "APP=%PKG%\LaunchStage"
set "ZIP=%DIST%\LaunchStage test.zip"

if exist "%PKG%" rmdir /s /q "%PKG%"
if exist "%ZIP%" del /q "%ZIP%"

rem Same order as build.cmd: the command-line tool first, the app second.
echo Building the command-line tool...
dotnet publish "%~dp0src\LaunchStage.Cli\LaunchStage.Cli.csproj" -c Release -r win-x64 --self-contained true -o "%APP%"
if errorlevel 1 goto failed

echo.
echo Building the app...
dotnet publish "%~dp0src\LaunchStage.App\LaunchStage.App.csproj" -c Release -r win-x64 --self-contained true -o "%APP%"
if errorlevel 1 goto failed

echo.
echo Adding the uninstaller...
> "%APP%\Uninstall LaunchStage.cmd" (
    echo @echo off
    echo rem Removes LaunchStage and everything it added to this PC. It asks first.
    echo start "" "%%~dp0LaunchStage.exe" --uninstall
)
rem The list of LaunchStage's own files: Remove LaunchStage deletes only these (never other files in the folder).
powershell -NoProfile -Command "$app = '%APP%'; $files = @(Get-ChildItem -LiteralPath $app -Recurse -File | ForEach-Object { $_.FullName.Substring($app.Length + 1) }) + 'uninstall-files.txt'; Set-Content -LiteralPath (Join-Path $app 'uninstall-files.txt') -Value $files -Encoding UTF8"
if errorlevel 1 goto failed

echo.
echo Adding the guide, license and instructions...
copy /y "%~dp0LICENSE.txt" "%PKG%\LICENSE.txt" >nul
copy /y "%~dp0tester\READ ME FIRST.txt" "%PKG%\READ ME FIRST.txt" >nul
if exist "%~dp0docs\LaunchStage guide.pdf" (
    copy /y "%~dp0docs\LaunchStage guide.pdf" "%PKG%\LaunchStage guide.pdf" >nul
) else (
    echo   No guide PDF yet: run build-guide.cmd first if you want it in the zip.
)
if exist "%~dp0streamdeck\dist\com.bones84.launchstage.streamDeckPlugin" (
    mkdir "%PKG%\Stream Deck plugin"
    copy /y "%~dp0streamdeck\dist\com.bones84.launchstage.streamDeckPlugin" "%PKG%\Stream Deck plugin\" >nul
)

rem Zip with .NET's zip routine (it stops with an error instead of quietly skipping a busy file), then check that
rem every file made it in.
echo Zipping...
powershell -NoProfile -Command "$ErrorActionPreference = 'Stop'; Add-Type -AssemblyName System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::CreateFromDirectory('%PKG%', '%ZIP%'); $zip = [IO.Compression.ZipFile]::OpenRead('%ZIP%'); $inZip = @($zip.Entries | Where-Object { $_.Name }).Count; $zip.Dispose(); $onDisk = @(Get-ChildItem -LiteralPath '%PKG%' -Recurse -File).Count; if ($inZip -ne $onDisk) { Write-Host \"Only $inZip of $onDisk files made it into the zip.\"; exit 1 }; Write-Host \"$inZip files zipped.\""
if errorlevel 1 goto failed

echo.
echo Done: %ZIP%
echo Send that file to your testers.
pause
exit /b 0

:failed
echo.
echo Making the tester package failed. Copy the red errors above and send them to Claude.
pause
exit /b 1
