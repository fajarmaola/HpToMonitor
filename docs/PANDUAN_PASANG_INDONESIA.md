# Panduan SecondScreen Local (Bahasa Indonesia)

Panduan super simpel: (A) download file, (B) pasang aplikasi HP (.apk), (C) pasang driver di PC.

---

## A. Download semua file dari GitHub Actions

Semua file jadi tersimpan sebagai **Artifacts** di tiap run. Buka:
`https://github.com/teleraya-official/SecondScreenLocal/actions`

### 1. Aplikasi PC + Driver  (dari workflow "Windows")
- Klik run **Windows** paling baru yang HIJAU (contoh: "Windows #13").
- Scroll ke bawah sampai bagian **"Artifacts"**.
- Download:
  - **`SecondScreenLocal-Windows-x64`** → isinya aplikasi PC: `SecondScreenLocal.exe` + `SecondScreen.Native.dll`
  - **`SecondScreenLocal-DisplayDriver`** → isinya driver layar: `.dll` + `.inf` + `.cat`

### 2. Aplikasi HP / APK  (dari workflow "Android")  ← INI LETAK APK-nya
- Di halaman Actions, klik workflow **"Android"** di daftar kiri (atau buka run **Android** paling baru yang HIJAU, contoh "Android #14").
- Scroll ke bagian **"Artifacts"**.
- Download artifact bernama **`SecondScreenLocal-apk`**.
- Setelah di-unzip, isinya file **`app-debug.apk`** → inilah aplikasi Android-nya.

> Catatan: Artifact berbentuk .zip. Ekstrak dulu sebelum dipakai.

---

## B. Pasang aplikasi di HP Android

1. Pindahkan file **`app-debug.apk`** ke HP (via kabel USB / kirim ke diri sendiri lewat chat / dsb).
2. Buka file `app-debug.apk` di HP.
3. Kalau muncul peringatan "Install from unknown sources", izinkan (Settings → izinkan install dari sumber ini).
4. Tap **Install**. Selesai — buka aplikasi **SecondScreen Local**.

---

## C. Pasang driver layar virtual di PC Windows

> Driver ini **belum ditandatangani resmi** (biaya sertifikat mahal), jadi untuk pemakaian pribadi kita
> nyalakan **Test Signing**. Ini aman untuk testing. Butuh **1x restart PC**.

### Langkah 1 — Nyalakan Test Signing (sekali saja)
1. Klik Start, ketik **cmd**, klik kanan **Command Prompt** → **Run as administrator**.
2. Ketik perintah ini lalu Enter:
   ```
   bcdedit /set testsigning on
   ```
3. **Restart PC.** Setelah nyala lagi, biasanya muncul tulisan kecil "Test Mode" di pojok kanan bawah layar — itu normal.

### Langkah 2 — Buat & percayai sertifikat test (sekali saja)
Buka **Command Prompt as administrator**, masuk ke folder driver (yang tadi di-download), lalu:
```
REM buat sertifikat test
makecert -r -pe -ss PrivateCertStore -n "CN=SecondScreenTest" SecondScreenTest.cer

REM percayai sertifikat tersebut
certutil -addstore -f Root SecondScreenTest.cer
certutil -addstore -f TrustedPublisher SecondScreenTest.cer

REM tandatangani driver + catalog dengan sertifikat test
signtool sign /v /s PrivateCertStore /n SecondScreenTest /fd sha256 SecondScreenDisplay.dll
signtool sign /v /s PrivateCertStore /n SecondScreenTest /fd sha256 SecondScreenDisplay.cat
```
> `makecert`, `signtool`, `certutil` tersedia kalau kamu punya Visual Studio / Windows SDK.
> Kalau tidak punya, cara termudah: install **Windows SDK** (gratis) atau jalankan dari
> "Developer Command Prompt".

### Langkah 3 — Pasang driver
Di **Command Prompt as administrator**, di folder driver:
```
pnputil /add-driver SecondScreenDisplay.inf /install
```

### Langkah 4 — Munculkan "Display 2"
- Cara paling gampang: jalankan **`SecondScreenLocal.exe`** (aplikasi PC). Aplikasi akan otomatis
  membuat perangkat layar virtual → Windows memunculkan **Display 2**.
- Cek: **Settings → System → Display** → sekarang ada 2 layar.
- Pilih **"Extend these displays"** supaya layar kedua jadi perluasan (bukan duplikat).

### Langkah 5 — Sambungkan HP & mulai streaming
1. Pastikan PC dan HP **satu jaringan WiFi yang sama**.
2. Buka **SecondScreenLocal.exe** di PC dan aplikasi **SecondScreen Local** di HP.
3. HP akan menemukan PC otomatis (LAN discovery). Lakukan **pairing** (masukkan PIN yang tampil).
4. Selesai — layar Display 2 dari PC akan tampil di HP. 🎉

---

## Kalau mau copot driver
```
pnputil /delete-driver SecondScreenDisplay.inf /uninstall /force
```
Dan untuk mematikan test mode:
```
bcdedit /set testsigning off
```
(lalu restart)

---

## Ringkasan letak file
| File | Ada di mana |
|---|---|
| `SecondScreenLocal.exe` + `.dll` (aplikasi PC) | Artifact **SecondScreenLocal-Windows-x64** (workflow Windows) |
| `SecondScreenDisplay.dll/.inf/.cat` (driver) | Artifact **SecondScreenLocal-DisplayDriver** (workflow Windows) |
| `app-debug.apk` (aplikasi HP) | Artifact **SecondScreenLocal-apk** (workflow **Android**) |
