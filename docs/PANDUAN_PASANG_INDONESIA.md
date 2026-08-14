# Panduan HP ke Monitor (Bahasa Indonesia)

**HP ke Monitor** — oleh **PT Teleraya Digital Group** (company.teleraya.com).
Ubah HP/tablet Android jadi **layar kedua** PC Windows, sepenuhnya **offline**.

Panduan super simpel: (A) download file, (B) pasang aplikasi HP (.apk), (C) pasang aplikasi PC (driver terpasang otomatis saat setup — tanpa PowerShell), lalu tekan **Mulai**.

---

## A. Download file dari GitHub Actions

Buka: `https://github.com/teleraya-official/SecondScreenLocal/actions`

### 1. Aplikasi PC (+ driver) — dari workflow "Windows"
- Klik run **Windows** terbaru yang **HIJAU**.
- Di bagian **Artifacts**, download:
  - **`SecondScreenLocal-Windows-x64`** → aplikasi PC (`SecondScreenLocal.exe` + `SecondScreen.Native.dll` + folder `driver`).
  - (opsional) **`SecondScreenLocal-DisplayDriver`** → paket driver terpisah (`.dll` + `.inf` + `.cat`).

### 2. Aplikasi HP / APK — dari workflow "Android"
- Klik workflow **Android** terbaru yang **HIJAU**.
- Di bagian **Artifacts**, download **`SecondScreenLocal-apk`** → berisi `app-debug.apk`.

> Artifact berbentuk .zip — ekstrak dulu sebelum dipakai.

---

## B. Pasang aplikasi di HP Android

1. Pindahkan **`app-debug.apk`** ke HP.
2. Buka file-nya, izinkan "Install from unknown sources" bila diminta, lalu tap **Install**.
3. Buka aplikasi **HP ke Monitor**.

---

## C. Pasang & jalankan di PC Windows (SATU LANGKAH — tanpa PowerShell)

1. Jalankan **installer** (`HPkeMonitor-Setup.exe`) — atau ekstrak **`SecondScreenLocal-Windows-x64`** dan jalankan **`SecondScreenLocal.exe`**.
   - Saat instalasi, **driver Layar 2 dipasang otomatis** (cek versi → skip kalau sudah cocok). Ini
     satu-satunya tempat driver diinstal; aplikasi tidak lagi mengecek driver saat dibuka.
2. Buka aplikasi, pastikan opsi **"Buat Layar 2 virtual (driver IddCx)"** tercentang, lalu klik **Mulai**.
3. Di HP, tekan **Cari PC** → pilih PC kamu → **Sambung**, lalu masukkan **kode 6 digit** yang muncul di PC.
4. Setelah tersambung, HP menampilkan **layar sukses + instruksi** — tekan **Mulai Tampilkan** untuk masuk mode Layar 2.

Selesai — HP kamu jadi **Layar 2**. Atur posisi lewat tombol **Pengaturan Layar**.

> Tombol **EN/ID** di pojok kanan atas mengganti bahasa aplikasi (tersimpan otomatis, default Indonesia).

### Cek Kesehatan (kalau ada masalah)
Buka tombol **Cek Kesehatan** di aplikasi untuk melihat status **Driver Layar 2**, **Test Signing**,
dan **Jaringan** — masing-masing punya tombol **Perbaiki/Aktifkan** (dijalankan hanya saat kamu tekan).
Ada juga **Uninstall Bersih Driver** untuk melepas Layar 2 dan kembali ke satu layar.

### Kalau driver ditolak karena "tanda tangan" (hanya sekali di PC baru)
Driver ini belum bertanda tangan resmi (WHQL). Kalau gagal karena signature, buka **Cek Kesehatan** →
pada baris **Test Signing** klik **Aktifkan** (butuh **1x restart** Windows — aturan Windows, tidak bisa
dilewati). Setelah restart, buka lagi aplikasi.

> Setelah Test Signing aktif, biasanya muncul teks kecil "Test Mode" di pojok layar — itu normal.

---

## Masalah umum
- **HP tidak menemukan PC** → pastikan PC & HP di **Wi-Fi yang sama**, lalu tekan **Cari PC** lagi.
- **Device Manager menampilkan Code 43/10** → pakai build **terbaru yang HIJAU**, lalu buka
  **Cek Kesehatan → Driver Layar 2 → Perbaiki**. Kalau masih, kirim kode error-nya ke tim.
- **Belum bisa Layar 2** → kamu tetap bisa jalan dengan menangkap **layar utama** (pilih "TIDAK" saat
  ditanya) sambil menyiapkan driver.
