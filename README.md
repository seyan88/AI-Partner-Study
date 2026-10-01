# 🧠 AI Partner Study - Smart Learning Companion

Aplikasi desktop C# (Windows Forms) otonom yang dirancang khusus untuk memantau, mengekstrak, dan menyusun materi pembelajaran dari **Dicoding** langsung ke dalam **Obsidian Vault** secara otomatis.

Aplikasi ini bertindak sebagai asisten cerdas yang menghilangkan kerepotan *copy-paste* manual, memastikan catatan Anda tersusun rapi, hierarkis, dan memiliki format Markdown yang sempurna.

## ✨ Fitur Utama

- 🤖 **Auto-Capture via UI Automation**
  Secara ajaib memantau browser Anda (Brave, Chrome, Edge, Firefox) menggunakan Windows UI Automation. Begitu mendeteksi Anda masuk ke halaman modul Dicoding, aplikasi akan otomatis mengekstrak materi tanpa perlu ekstensi browser.
  
- 📝 **Smart Markdown Formatting & Table Parsing**
  Mengonversi HTML kotor menjadi Markdown murni. Mengurai tabel kompleks, menyesuaikan jumlah kolom secara dinamis, mencegah sel terputus (baris baru), serta menyusun *ordered* (`<ol>`) dan *unordered lists* (`<ul>`) dengan rapi.

- 📐 **Obsidian Reading-View Optimized**
  Seluruh teks materi dibungkus otomatis dengan perataan (Justify) agar terlihat rapi layaknya membaca buku cetak saat Anda menekan `Ctrl+E` (*Reading View*) di Obsidian.

- 📸 **Visual Context Integration (Tombol F9)**
  Melihat diagram arsitektur atau potongan kode penting? Cukup tekan **F9**. Aplikasi akan mengambil tangkapan layar, menyimpannya di folder `assets`, dan menautkannya (`![[assets/img.png]]`) secara otomatis tepat di dalam catatan modul yang sedang Anda pelajari.

- 🗂️ **Vault Hierarchical Sync**
  Catatan tidak dilempar sembarangan. Aplikasi otomatis membangun struktur folder berdasarkan nama akademi (contoh: `Pembelajaran Dicoding/315 - Belajar Membuat Front-End Web untuk Pemula/16724 - Mencari DOM.md`).

- 🧹 **Content Purification (Pembersih Sampah)**
  Secara algoritmik membuang elemen yang tidak penting dari hasil ekstraksi, seperti menu *sidebar*, tombol navigasi ("Sebelumnya/Selanjutnya"), kolom komentar, dan *pop-up streak* ("Hari beruntun") milik Dicoding.

- 🧠 **Smart Auto-Fix & Duplicate Prevention**
  Memiliki sistem *cache* internal. Jika materi sudah pernah dicatat dengan sempurna, aplikasi akan melakukan **[SKIP]** untuk menghemat sumber daya. Namun, jika mendeteksi catatan lama yang memiliki format rusak (zaman kuno), aplikasi akan masuk ke **[OVERWRITE MODE]** dan memperbaikinya secara otomatis.

## 🛠️ Prasyarat & Teknologi

- **OS:** Windows 10/11
- **Platform:** .NET Framework (C# Windows Forms)
- **Editor:** Obsidian (dengan Vault aktif di WSL/Windows)

## 🚀 Cara Menjalankan

1. Pastikan Anda berada di direktori aplikasi.
2. Jalankan skrip kompilasi:
   ```bat
   compile.bat
   ```
3. Buka aplikasi hasil kompilasi:
   ```bat
   "AI Partner Study.exe"
   ```
4. Buka materi Dicoding di browser Anda, dan biarkan aplikasi bekerja secara gaib!

---
*Dibangun bersama Google Antigravity AI Partner untuk produktivitas belajar tanpa batas.*
