@echo off
rem whale-sitter build script
rem Compiles the C# WinForms source with the csc.exe that ships with
rem .NET Framework on Windows (no extra toolchain needed).
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. .NET Framework 4.x is required.
    exit /b 1
)
"%CSC%" /nologo /target:winexe /out:whale-sitter.exe /win32icon:whale-sitter.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.FileSystem.dll whale-sitter.cs
if errorlevel 1 (
    echo [ERROR] build failed.
    exit /b 1
)
echo whale-sitter.exe built OK.
