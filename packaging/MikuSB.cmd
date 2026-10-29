@echo off
rem Keeps the console window open when MikuSB fails to start.
rem Explorer closes the window the moment a console app exits, which is why a crash looks like
rem "nothing happened". Double-click this file instead of MikuSB.exe when reporting a problem.
setlocal
cd /d "%~dp0"

if not exist "MikuSB.exe" (
    echo [MikuSB] MikuSB.exe not found next to this script.
    goto :hold
)

echo [MikuSB] starting ...
"MikuSB.exe" %*
set "RC=%ERRORLEVEL%"
if "%RC%"=="0" goto :done

echo.
echo [MikuSB] exited with code %RC%
rem 0x8000807x / 0x8000808x / 0x8000809x are the apphost's "cannot start the runtime" family
rem (-2147450768 .. -2147450721). Match on the prefix because cmd's numeric compare is fragile.
echo %RC% | findstr /b /c:"-21474507" >nul && goto :framework
rem 0x80008096 (-2147450730) = required framework missing, 0x80008083 (-2147450749) = missing host files.
echo [MikuSB] See the logs folder for crash-*.log with the full stack trace.
goto :hold

:framework
echo [MikuSB] The .NET 10 runtime this build needs is missing or incomplete on this machine.
echo [MikuSB] Unpack the self-contained release again (MikuSB-win-x64.zip, ~84 MB):
echo [MikuSB]   https://github.com/dubstream3/MikuSB/releases/latest
echo [MikuSB] Or install the runtime and keep this build:
echo [MikuSB]   https://aka.ms/dotnet-core-applaunch?framework=Microsoft.AspNetCore.App^&framework_version=10.0.0^&arch=x64^&rid=win-x64
goto :hold

:hold
echo.
pause

:done
endlocal
exit /b %RC%
