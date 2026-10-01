@echo off
setlocal
cd /d "C:\Users\sulas\AI-Partner-Study"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF
set OUT_DESKTOP=C:\Users\sulas\OneDrive\Desktop\AI Partner Study.exe
set OUT_LOCAL=C:\Users\sulas\AI-Partner-Study\AI Partner Study.exe

echo Compiling Standalone AI Partner Study...
"%CSC%" /target:winexe /optimize+ /reference:System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll,"%WPF%\UIAutomationClient.dll","%WPF%\UIAutomationTypes.dll" /out:"%OUT_DESKTOP%" "C:\Users\sulas\AI-Partner-Study\Program.cs"

if %ERRORLEVEL% equ 0 (
    echo Compilation succeeded! Copying to local folder...
    copy /Y "%OUT_DESKTOP%" "%OUT_LOCAL%" >nul
    echo Done.
) else (
    echo Compilation failed with code %ERRORLEVEL%.
)
