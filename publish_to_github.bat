@echo off
title Publish to GitHub
echo ==============================================================
echo       AUTO-PUBLISH AI PARTNER STUDY KE GITHUB (SEYAN88)
echo ==============================================================
echo.

echo Memastikan Anda sudah login di GitHub CLI...
gh auth status
if %ERRORLEVEL% NEQ 0 (
    echo Anda belum login! Silakan jalankan 'gh auth login' terlebih dahulu.
    pause
    exit /b
)

echo.
echo Mendorong (push) kode terbaru ke GitHub...
git remote get-url origin >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Repository belum ada di GitHub. Membuat repository publik "AI-Partner-Study"...
    gh repo create AI-Partner-Study --public --source=. --remote=origin --push
) else (
    echo Repository sudah terhubung. Memulai git push...
    git push origin master
)

echo.
echo ==============================================================
echo Selesai! Kode dan README terbaru sudah di-push!
echo Cek repo Anda di: https://github.com/seyan88/AI-Partner-Study
echo ==============================================================
pause
