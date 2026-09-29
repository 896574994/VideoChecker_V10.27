@echo off
rem ============================================================
rem  VideoChecker - build script
rem
rem  Uses the in-box .NET Framework compiler (csc.exe).
rem  Visual Studio is NOT required.
rem
rem  Output: dist\video_checker_gui.exe
rem
rem  NOTE: keep this file ASCII-only. cmd.exe reads .bat files with
rem  the system ANSI codepage, so non-ASCII comments can swallow the
rem  commands that follow them.
rem ============================================================
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found.
    echo         Please install .NET Framework 4.x ^(it ships with Windows^).
    pause
    exit /b 1
)

if not exist "dist" mkdir "dist"

echo Compiling src\*.cs ...
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
    /out:"dist\video_checker_gui.exe" ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
    /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Security.dll ^
    /r:Microsoft.CSharp.dll ^
    /win32icon:"assets\app.ico" ^
    src\*.cs

if errorlevel 1 (
    echo.
    echo [ERROR] build failed - see the messages above.
    pause
    exit /b 1
)

echo.
echo [OK] dist\video_checker_gui.exe
echo.
echo Next step: copy ffmpeg.exe and ffprobe.exe into  dist\ffmpeg\bin\
echo            FFmpeg is NOT bundled with this repository - see README.md
echo.
pause
