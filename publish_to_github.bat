@echo off
title Publish to GitHub
echo ==============================================================
echo       AUTO-PUBLISH AI PARTNER STUDY KE GITHUB (SEYAN88)
echo ==============================================================
echo.

echo [1/3] Menginisialisasi Git Repository...
git init
git config user.name "seyan88"
git config user.email "sulastiansetiadi@gmail.com"

echo [2/3] Mengonfigurasi .gitignore dan melakukan commit...
echo .vs/ > .gitignore
echo bin/ >> .gitignore
echo obj/ >> .gitignore
echo *.user >> .gitignore
echo packages/ >> .gitignore

git add .
git commit -m "Initial commit: AI Partner Study C# App dengan ekstraksi DOM dan SPA Race Condition fixes"

echo.
echo [3/3] Membuat repository publik di GitHub dan melakukan push...
echo Pastikan Anda sudah login di GitHub CLI (jika belum, jalankan: gh auth login)
gh repo create AI-Partner-Study --public --source=. --remote=origin --push

echo.
echo ==============================================================
echo Selesai! Jika tidak ada error merah di atas, repo berhasil dibuat!
echo Cek repo Anda di: https://github.com/seyan88/AI-Partner-Study
echo ==============================================================
pause
