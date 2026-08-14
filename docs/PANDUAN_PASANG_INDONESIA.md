# Panduan HP ke Monitor (Bahasa Indonesia)

**HP ke Monitor** — oleh **PT Teleraya Digital Group** (company.teleraya.com).
Ubah HP/tablet Android jadi **layar kedua** PC Windows, sepenuhnya **offline**.

Panduan super simpel: (A) download file, (B) pasang aplikasi HP (.apk), (C) pasang aplikasi PC & tekan **Pasang & Mulai** (driver terpasang otomatis — tanpa PowerShell).

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

1. Ekstrak **`SecondScreenLocal-Windows-x64`**, lalu jalankan **`SecondScreenLocal.exe`**.
2. Pastikan opsi **"Buat Layar 2 virtual (driver IddCx)"** tercentang.
3. Klik **Pasang & Mulai**.
   - Aplikasi otomatis **memeriksa driver**: kalau sudah terpasang dengan versi yang cocok →
     **dilewati**. Kalau belum → **dipasang otomatis** (muncul dialog **UAC** Windows → klik **Yes**).
   - Semua konfigurasi (buat Layar 2, encoder, enkripsi) dilakukan di dalam aplikasi.
4. Di HP, tekan **Cari PC** → pilih PC kamu → **Sambung**, lalu masukkan **kode 6 digit** yang muncul di PC.

Selesai — HP kamu jadi **Layar 2**. Atur posisi lewat tombol **Pengaturan Layar**.

### Kalau driver ditolak karena "tanda tangan" (hanya sekali di PC baru)
Driver ini belum bertanda tangan resmi (WHQL). Kalau instalasi gagal karena signature, saat muncul
peringatan, pilih **YA** untuk **mengaktifkan Test Signing** langsung dari aplikasi
(butuh **1x restart** Windows — ini aturan Windows, tidak bisa dilewati). Setelah restart, buka lagi
aplikasi dan tekan **Pasang & Mulai** — driver akan terpasang mulus.

> Setelah Test Signing aktif, biasanya muncul teks kecil "Test Mode" di pojok layar — itu normal.

---

## Masalah umum
- **HP tidak menemukan PC** → pastikan PC & HP di **Wi-Fi yang sama**, lalu tekan **Cari PC** lagi.
- **Device Manager menampilkan Code 43/10** → pakai build **terbaru yang HIJAU**, install ulang lewat
  **Pasang & Mulai**. Kalau masih, kirim kode error-nya ke tim.
- **Belum bisa Layar 2** → kamu tetap bisa jalan dengan menangkap **layar utama** (pilih "TIDAK" saat
  ditanya) sambil menyiapkan driver.
