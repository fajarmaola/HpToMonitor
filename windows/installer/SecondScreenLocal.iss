; Inno Setup script — produces windows/installer/Output/SecondScreenLocal-Setup.exe
; Build the app first (docs/BUILD_WINDOWS.md), then run: iscc SecondScreenLocal.iss
; Requires Inno Setup 6.

#define AppName "HP ke Monitor"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define Publisher "PT Teleraya Digital Group"
#define PublisherURL "https://company.teleraya.com"
; Path to the published Desktop output (overridable from CI via ISCC /DAppDir=...).
#ifndef AppDir
  #define AppDir "..\SecondScreen.Desktop\bin\Release\net8.0-windows"
#endif
; Path to the built driver package folder (overridable via ISCC /DDriverDir=...).
#ifndef DriverDir
  #define DriverDir "..\SecondScreen.DisplayDriver\x64\Release\SecondScreen.DisplayDriver"
#endif

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
; Let Restart Manager close the running app during a silent in-app update so its files can be
; replaced without a manual reinstall. The app is relaunched by the silent [Run] entry below
; (RestartApplications=no avoids a double launch).
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#AppDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; Driver package (optional). Requires the driver to be built + signed beforehand.
Source: "{#DriverDir}\*"; DestDir: "{app}\driver"; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\HP ke Monitor"; Filename: "{app}\SecondScreenLocal.exe"
Name: "{commondesktop}\HP ke Monitor"; Filename: "{app}\SecondScreenLocal.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Buat pintasan di Desktop"; GroupDescription: "Ikon tambahan:"

[Run]
; Install the IddCx virtual-display driver automatically DURING setup (installer already runs
; elevated, so no extra UAC). This is the ONLY place the driver is installed.
Filename: "pnputil.exe"; Parameters: "/add-driver ""{app}\driver\SecondScreenDisplay.inf"" /install"; \
  Flags: runhidden; StatusMsg: "Memasang driver Layar 2..."; Check: FileExists(ExpandConstant('{app}\driver\SecondScreenDisplay.inf'))
Filename: "{app}\SecondScreenLocal.exe"; Description: "Jalankan HP ke Monitor"; Flags: postinstall nowait skipifsilent
; During a silent in-app update, relaunch the app automatically (postinstall is skipped when silent).
Filename: "{app}\SecondScreenLocal.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "pnputil.exe"; Parameters: "/delete-driver SecondScreenDisplay.inf /uninstall /force"; Flags: runhidden; RunOnceId: "RemoveDriver"
