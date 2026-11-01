#define AppName "StorageStandby"
#define AppVersion "0.0.1"
#define InstallDir "{autopf}\StorageStandby"

[Setup]
AppId={{8E2F3A3B-1D0B-4B15-9D2D-6B4E3A7F91C2}}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={#InstallDir}
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release\installer
OutputBaseFilename=StorageStandby-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
CloseApplications=yes
CloseApplicationsFilter=StorageStandby.Desktop.exe,StorageStandby.Backend.exe
UninstallDisplayIcon={app}\Desktop\StorageStandby.Desktop.exe

[Files]
Source: "..\release\desktop\*"; DestDir: "{app}\Desktop"; Flags: recursesubdirs ignoreversion
Source: "..\release\backend\*"; DestDir: "{app}\Backend"; Flags: recursesubdirs ignoreversion
Source: "prerequisites\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Dirs]
Name: "{commonappdata}\StorageStandby"; Permissions: users-modify
Name: "{commonappdata}\StorageStandby\data-protection-keys"; Permissions: users-modify
Name: "{commonappdata}\StorageStandby\logs"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\StorageStandby"; Filename: "{app}\Desktop\StorageStandby.Desktop.exe"
Name: "{autodesktop}\StorageStandby"; Filename: "{app}\Desktop\StorageStandby.Desktop.exe"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated; Check: not IsWebView2Installed
Filename: "{app}\Desktop\StorageStandby.Desktop.exe"; Description: "Launch StorageStandby"; Flags: nowait postinstall skipifsilent

[Code]
function IsWebView2InstalledAt(RootKey: Integer; RegistryPath: String): Boolean;
var
	Version: String;
begin
	Result := RegQueryStringValue(RootKey, RegistryPath, 'pv', Version) and
			(Version <> '');
end;

function IsWebView2Installed(): Boolean;
begin
	Result :=
		IsWebView2InstalledAt(HKLM64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
		IsWebView2InstalledAt(HKCU64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
		IsWebView2InstalledAt(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
		IsWebView2InstalledAt(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
		IsWebView2InstalledAt(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
		IsWebView2InstalledAt(HKCU, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');
end;

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM StorageStandby.Desktop.exe /T /F"; Flags: runhidden waituntilterminated; RunOnceId: "KillStorageStandbyDesktop"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM StorageStandby.Backend.exe /T /F"; Flags: runhidden waituntilterminated; RunOnceId: "KillStorageStandbyBackend"