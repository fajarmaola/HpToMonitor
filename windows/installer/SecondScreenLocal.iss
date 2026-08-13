; Inno Setup script — produces windows/installer/Output/SecondScreenLocal-Setup.exe
; Build the app first (docs/BUILD_WINDOWS.md), then run: iscc SecondScreenLocal.iss
; Requires Inno Setup 6.

#define AppName "SecondScreen Local"
#define AppVersion "1.0.0"
#define Publisher "SecondScreen Local"
; Path to the published Desktop output (adjust if you used a different config/runtime).
#define AppDir "..\SecondScreen.Desktop\bin\Release\net8.0-windows"
; Path to the built + signed driver package folder (optional; comment out the [Files]/[Run]
; driver lines if you don't ship the driver).
#define DriverDir "..\SecondScreen.DisplayDriver\x64\Release\SecondScreenDisplay"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\SecondScreenLocal
DefaultGroupName=SecondScreen Local
OutputBaseFilename=SecondScreenLocal-Setup
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
Name: "{group}\SecondScreen Local"; Filename: "{app}\SecondScreen.Desktop.exe"
Name: "{commondesktop}\SecondScreen Local"; Filename: "{app}\SecondScreen.Desktop.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"
Name: "installdriver"; Description: "Install the virtual display driver (enables true Display 2)"; GroupDescription: "Optional components:"

[Run]
; Install the IddCx driver via pnputil when the user opts in (needs a signed .cat).
Filename: "pnputil.exe"; Parameters: "/add-driver ""{app}\driver\SecondScreenDisplay.inf"" /install"; \
  Flags: runhidden; Tasks: installdriver; StatusMsg: "Installing virtual display driver..."
Filename: "{app}\SecondScreen.Desktop.exe"; Description: "Launch SecondScreen Local"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "pnputil.exe"; Parameters: "/delete-driver SecondScreenDisplay.inf /uninstall /force"; Flags: runhidden; RunOnceId: "RemoveDriver"
