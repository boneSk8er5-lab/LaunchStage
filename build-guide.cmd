@echo off
rem Makes the shareable copy of the guide in the "docs" folder next to this file:
rem   docs\guide.html             the guide as a web page (with docs\images)
rem   docs\LaunchStage guide.pdf  the same guide as a PDF (made with Microsoft Edge, which comes with Windows)
rem Run build.cmd first, so the guide matches the app.

set "DOCS=%~dp0docs"

if not exist "%~dp0out\LaunchStage.exe" (
    echo LaunchStage isn't built yet. Double-click build.cmd first, then run this again.
    goto failed
)

echo Saving the guide as a web page...
start "" /wait "%~dp0out\LaunchStage.exe" --export-guide "%DOCS%"
if not exist "%DOCS%\guide.html" (
    echo The web page wasn't made. Check the log: Settings, Open log.
    goto failed
)

echo Making the PDF...
set "EDGE=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE%" set "EDGE=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE%" (
    echo Microsoft Edge wasn't found, so there's no PDF. Open docs\guide.html in a browser and print it to PDF instead.
    goto done
)

"%EDGE%" --headless --disable-gpu --no-pdf-header-footer --user-data-dir="%TEMP%\LaunchStage guide pdf" --print-to-pdf="%DOCS%\LaunchStage guide.pdf" "%DOCS%\guide.html"
if not exist "%DOCS%\LaunchStage guide.pdf" (
    echo The PDF wasn't made. Open docs\guide.html in a browser and print it to PDF instead.
    goto done
)

:done
echo.
echo Done: %DOCS%
pause
exit /b 0

:failed
echo.
pause
exit /b 1
