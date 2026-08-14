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

## C. Pasang driver layar virtual di PC Windows (metode PowerShell — TANPA Visual Studio/SDK)

> Driver ini **belum ditandatangani resmi**, jadi kita nyalakan **Test Signing** + pakai sertifikat
> test buatan sendiri. Semua pakai **PowerShell bawaan Windows** (tidak perlu makecert/signtool/SDK).

### Langkah 1 — Nyalakan Test Signing (sekali saja, butuh 1x restart)
1. Klik Start, ketik **powershell**, klik kanan **Windows PowerShell** → **Run as administrator**.
2. Jalankan:
   ```
   bcdedit /set testsigning on
   ```
3. **Restart PC.** Setelah nyala lagi biasanya ada tulisan "Test Mode" di pojok kanan bawah (normal).

### Langkah 2 — Pindah ke folder driver
1. Buka File Explorer, masuk ke folder hasil ekstrak **`SecondScreenLocal-DisplayDriver`**
   (folder yang berisi `SecondScreenDisplay.inf`, `.dll`, `.cat`).
2. Klik kolom alamat di atas, **salin path-nya** (contoh: `C:\Users\Nama\Downloads\SecondScreenLocal-DisplayDriver`).
3. Di **PowerShell (Administrator)**, ketik `cd ` lalu tempel path tadi di dalam tanda kutip:
   ```
   cd "C:\Users\Nama\Downloads\SecondScreenLocal-DisplayDriver"
   ```
4. Cek file-nya ada:
   ```
   dir SecondScreenDisplay.*
   ```
   Harus muncul `SecondScreenDisplay.inf`, `.dll`, dan `.cat`.

### Langkah 3 — Buat sertifikat test, percayai, tandatangani catalog, lalu pasang
Salin-tempel **seluruh blok** ini ke PowerShell (Administrator), tekan Enter:
```powershell
# buat sertifikat code-signing test (tanpa makecert)
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=SecondScreenTest" -CertStoreLocation "Cert:\CurrentUser\My"

# percayai sertifikat: import ke Trusted Root + Trusted Publishers
Export-Certificate -Cert $cert -FilePath "$env:TEMP\SST.cer" | Out-Null
Import-Certificate -FilePath "$env:TEMP\SST.cer" -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
Import-Certificate -FilePath "$env:TEMP\SST.cer" -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null

# tandatangani CATALOG saja (jangan tandatangani .dll — nanti hash di .cat jadi tidak cocok)
Set-AuthenticodeSignature -FilePath ".\SecondScreenDisplay.cat" -Certificate $cert

# pasang driver
pnputil /add-driver SecondScreenDisplay.inf /install
```
Kalau berhasil, muncul: `Driver package added successfully` / `Added driver packages: 1`.

### Langkah 4 — Munculkan "Display 2"
- Jalankan **`SecondScreenLocal.exe`** (aplikasi PC). Aplikasi otomatis membuat perangkat layar
  virtual → Windows memunculkan **Display 2**.
- Cek: **Settings → System → Display** → sekarang ada 2 layar.
- Pilih **"Extend these displays"**.

### Langkah 5 — Sambungkan HP & mulai streaming
1. Pastikan PC dan HP **satu jaringan WiFi**.
2. Buka **SecondScreenLocal.exe** di PC dan aplikasi **SecondScreen Local** di HP.
3. Lakukan **pairing** (masukkan PIN yang tampil).
4. Layar Display 2 tampil di HP. 🎉

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
