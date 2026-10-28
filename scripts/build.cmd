@echo off
rem Builds DragFlow.exe, the test runner and DragFlow-Setup.exe.
rem Uses the C# compiler that ships with Windows (no SDK needed). If scripts\get-compiler.ps1 has been run,
rem the newer Roslyn compiler in tools\roslyn is used instead (same code, newer language allowed).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WM=%WINDIR%\System32\WinMetadata
set ROOT=%~dp0..
set OUT=%ROOT%\bin
set CSC=%FW%\csc.exe
set LANG=
if exist "%ROOT%\tools\roslyn\csc.exe" (
    set CSC=%ROOT%\tools\roslyn\csc.exe
    set LANG=/langversion:latest
)
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll /r:Microsoft.VisualBasic.dll /r:System.Security.dll /r:%FW%\WPF\UIAutomationClient.dll /r:%FW%\WPF\UIAutomationTypes.dll /r:%FW%\WPF\WindowsBase.dll /r:%FW%\WPF\PresentationFramework.dll /r:%FW%\WPF\PresentationCore.dll /r:%FW%\System.Xaml.dll /r:%FW%\WPF\WindowsFormsIntegration.dll
if defined LANG set REFS=%REFS% /r:%FW%\mscorlib.dll /r:%FW%\System.Xml.dll /nostdlib+
rem Windows' built-in OCR, PDF, image codecs and Share window (WinRT)
set REFS=%REFS% /r:%FW%\System.Runtime.WindowsRuntime.dll /r:%FW%\System.Runtime.InteropServices.WindowsRuntime.dll /r:%FW%\System.Runtime.dll /r:%FW%\System.Threading.Tasks.dll /r:%FW%\System.IO.dll /r:%WM%\Windows.Foundation.winmd /r:%WM%\Windows.Media.winmd /r:%WM%\Windows.Graphics.winmd /r:%WM%\Windows.Storage.winmd /r:%WM%\Windows.Data.winmd /r:%WM%\Windows.ApplicationModel.winmd
if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo %LANG% /target:winexe /optimize+ /codepage:65001 /out:"%OUT%\DragFlow.exe" /win32manifest:"%ROOT%\src\app.manifest" /win32icon:"%ROOT%\src\DragFlow.ico" %REFS% "%ROOT%\src\*.cs"
if errorlevel 1 exit /b 1

"%CSC%" /nologo %LANG% /target:exe /codepage:65001 /out:"%OUT%\DragFlow.Tests.exe" %REFS% "%ROOT%\src\*.cs" "%ROOT%\tests\*.cs" /main:DragFlow.Tests.TestMain
if errorlevel 1 exit /b 1

rem The installer carries DragFlow.exe inside it.
"%CSC%" /nologo %LANG% /target:winexe /optimize+ /codepage:65001 /out:"%OUT%\DragFlow-Setup.exe" /win32manifest:"%ROOT%\src\app.manifest" /win32icon:"%ROOT%\src\DragFlow.ico" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll /resource:"%OUT%\DragFlow.exe",DragFlow.exe "%ROOT%\src\installer\Setup.cs"
if errorlevel 1 exit /b 1
echo Build OK: %OUT%
