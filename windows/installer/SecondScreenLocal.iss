; Inno Setup script — produces windows/installer/Output/SecondScreenLocal-Setup.exe
; Build the app first (docs/BUILD_WINDOWS.md), then run: iscc SecondScreenLocal.iss
; Requires Inno Setup 6.

#define AppName "HP ke Monitor"
#define AppVersion "1.0.0"
#define Publisher "PT Teleraya Digital Group"
#define PublisherURL "https://company.teleraya.com"
; Path to the published Desktop output (adjust if you used a different config/runtime).
#define AppDir "..\SecondScreen.Desktop\bin\Release\net8.0-windows"
; Path to the built + signed driver package folder (optional; comment out the [Files]/[Run]
; driver lines if you don't ship the driver).
#define DriverDir "..\SecondScreen.DisplayDriver\x64\Release\SecondScreenDisplay"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL={#PublisherURL}
AppSupportURL={#PublisherURL}
DefaultDirName={autopf}\HP ke Monitor
DefaultGroupName=HP ke Monitor
OutputBaseFilename=HPkeMonitor-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern

[Files]
Source: "{#AppDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; Driver package (optional). Requires the driver to be built + signed beforehand.
Source: "{#DriverDir}\*"; DestDir: "{app}\driver"; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\HP ke Monitor"; Filename: "{app}\SecondScreenLocal.exe"
Name: "{commondesktop}\HP ke Monitor"; Filename: "{app}\SecondScreenLocal.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Buat pintasan di Desktop"; GroupDescription: "Ikon tambahan:"
Name: "installdriver"; Description: "Pasang driver layar virtual sekarang (bisa juga otomatis dari dalam aplikasi)"; GroupDescription: "Komponen opsional:"

[Run]
; Install the IddCx driver via pnputil when the user opts in (needs a signed .cat).
Filename: "pnputil.exe"; Parameters: "/add-driver ""{app}\driver\SecondScreenDisplay.inf"" /install"; \
  Flags: runhidden; Tasks: installdriver; StatusMsg: "Memasang driver layar virtual..."
Filename: "{app}\SecondScreenLocal.exe"; Description: "Jalankan HP ke Monitor"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "pnputil.exe"; Parameters: "/delete-driver SecondScreenDisplay.inf /uninstall /force"; Flags: runhidden; RunOnceId: "RemoveDriver"
