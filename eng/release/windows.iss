#ifndef Payload
  #error Payload is required
#endif
[Setup]
AppId={{50AE2197-7C7B-43DA-BE98-1CDEBE86B273}
AppName=NexaCL
AppVersion={#ProductVersion}
VersionInfoVersion={#NumericVersion}.0
DefaultDirName={autopf}\NexaCL
DefaultGroupName=NexaCL
UsePreviousAppDir=no
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed={#InstallArch}
ArchitecturesInstallIn64BitMode={#InstallArch}
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile={#IconPath}
UninstallDisplayIcon={app}\Nexa.Desktop.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
DisableFinishedPage=no
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "explorerintegration"; Description: "Enable Windows 11 context menu (trusts the NexaCL signing certificate for all users)"; Flags: unchecked
[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{commonprograms}\NexaCL"; Filename: "{app}\Nexa.Desktop.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\NexaCL"; Filename: "{app}\Nexa.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\NexaCL"; ValueType: string; ValueName: "MUIVerb"; ValueData: "NexaCL"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\NexaCL"; ValueType: string; ValueName: "SubCommands"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\NexaCL"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\Nexa.Desktop.exe"
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\NexaCL\shell\modpack"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Install as modpack"
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\NexaCL\shell\modpack\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Nexa.Desktop.exe"" --install-modpack ""%1"""
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\NexaCL\shell\theme"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Install as theme"
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\NexaCL\shell\theme\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Nexa.Desktop.exe"" --install-theme ""%1"""
Root: HKLM; Subkey: "Software\Classes\NexaCL.NexaModpack"; ValueType: string; ValueName: ""; ValueData: "Nexa Modpack"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\NexaCL.NexaModpack\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Assets\FileTypes\nmp.ico"",0"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\NexaCL.NexaModpack\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Nexa.Desktop.exe"" --install-modpack ""%1"""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.nmp\OpenWithProgids"; ValueType: string; ValueName: "NexaCL.NexaModpack"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.nmp"; ValueType: string; ValueName: ""; ValueData: "NexaCL.NexaModpack"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\NexaCL.ThemePack"; ValueType: string; ValueName: ""; ValueData: "Nexa Theme Pack"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\NexaCL.ThemePack\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Assets\FileTypes\ntp.ico"",0"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\NexaCL.ThemePack\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Nexa.Desktop.exe"" --install-theme ""%1"""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.ntp\OpenWithProgids"; ValueType: string; ValueName: "NexaCL.ThemePack"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.ntp"; ValueType: string; ValueName: ""; ValueData: "NexaCL.ThemePack"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\NexaCL.ModrinthPack"; ValueType: string; ValueName: ""; ValueData: "Modrinth Modpack"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\NexaCL.ModrinthPack\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Assets\FileTypes\mrpack.ico"",0"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\NexaCL.ModrinthPack\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Nexa.Desktop.exe"" --install-modpack ""%1"""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.mrpack\OpenWithProgids"; ValueType: string; ValueName: "NexaCL.ModrinthPack"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\NexaCL.Pack"; ValueType: string; ValueName: ""; ValueData: "Nexa Modpack"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\NexaCL.Pack\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Assets\FileTypes\nexapack.ico"",0"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\NexaCL.Pack\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Nexa.Desktop.exe"" --install-modpack ""%1"""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.nexapack\OpenWithProgids"; ValueType: string; ValueName: "NexaCL.Pack"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Microsoft\Active Setup\Installed Components\{{522C4ED3-0A1F-47E8-B018-961652D63816}"; ValueType: string; ValueName: ""; ValueData: "NexaCL Explorer integration"; Flags: uninsdeletekey; Tasks: explorerintegration
Root: HKLM; Subkey: "Software\Microsoft\Active Setup\Installed Components\{{522C4ED3-0A1F-47E8-B018-961652D63816}"; ValueType: string; ValueName: "Version"; ValueData: "1,0,0,0"; Tasks: explorerintegration
Root: HKLM; Subkey: "Software\Microsoft\Active Setup\Installed Components\{{522C4ED3-0A1F-47E8-B018-961652D63816}"; ValueType: string; ValueName: "StubPath"; ValueData: """{app}\ShellIntegration\Nexa.ShellRegistration.exe"" --register"; Tasks: explorerintegration

[Run]
Filename: "{app}\Nexa.Desktop.exe"; Description: "Open NexaCL"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\ShellIntegration\Nexa.ShellRegistration.exe"; Parameters: "--unregister-all"; Flags: runhidden waituntilterminated; RunOnceId: "NexaCL.ExplorerIntegration"
Filename: "{app}\ShellIntegration\Nexa.ShellRegistration.exe"; Parameters: "--remove-trust"; Flags: runhidden waituntilterminated; RunOnceId: "NexaCL.ExplorerTrust"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  Helper: String;
  Code: Integer;
begin
  if (CurStep <> ssPostInstall) or not WizardIsTaskSelected('explorerintegration') then Exit;
  Helper := ExpandConstant('{app}\ShellIntegration\Nexa.ShellRegistration.exe');
  if not Exec(Helper, '--install-trust', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
  begin
    Exec(Helper, '--remove-trust', '', SW_HIDE, ewWaitUntilTerminated, Code);
    RegDeleteKeyIncludingSubkeys(HKLM64, 'Software\Microsoft\Active Setup\Installed Components\{522C4ED3-0A1F-47E8-B018-961652D63816}');
    RaiseException('Could not install the NexaCL Explorer signing certificate.');
  end;
  try
    if not ExecAsOriginalUser(Helper, '--register', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('Could not register the NexaCL Explorer extension.');
  except
    Exec(Helper, '--remove-trust', '', SW_HIDE, ewWaitUntilTerminated, Code);
    RegDeleteKeyIncludingSubkeys(HKLM64, 'Software\Microsoft\Active Setup\Installed Components\{522C4ED3-0A1F-47E8-B018-961652D63816}');
    RaiseException(GetExceptionMessage);
  end;
end;

// The pinned Inno 6.4 compiler emits a 32-bit Setup process on both Windows RIDs.
// Set the token default owner before creating files, rather than repairing their
// permissions after copy while the installation account can still modify them.
function NexaGetCurrentProcess: THandle;
  external 'GetCurrentProcess@kernel32.dll stdcall';
function NexaOpenProcessToken(Process: THandle; Access: LongWord; var Token: THandle): Integer;
  external 'OpenProcessToken@advapi32.dll stdcall';
function NexaConvertStringSidToSid(SidText: String; var Sid: LongWord): Integer;
  external 'ConvertStringSidToSidW@advapi32.dll stdcall';
function NexaSetTokenOwner(Token: THandle; Kind: Integer; var Owner: LongWord; Length: LongWord): Integer;
  external 'SetTokenInformation@advapi32.dll stdcall';
function NexaLocalFree(Address: LongWord): LongWord;
  external 'LocalFree@kernel32.dll stdcall';
function NexaCloseHandle(Handle: THandle): Integer;
  external 'CloseHandle@kernel32.dll stdcall';

function InitializeSetup: Boolean;
var
  Token: THandle;
  Owner: LongWord;
begin
  Result := False;
  Token := 0;
  Owner := 0;
  try
    // TOKEN_ADJUST_DEFAULT; TokenOwner contains one pointer-sized SID field.
    if NexaOpenProcessToken(NexaGetCurrentProcess, $80, Token) <> 0 then
      if NexaConvertStringSidToSid('S-1-5-32-544', Owner) <> 0 then
        Result := NexaSetTokenOwner(Token, 4, Owner, 4) <> 0;
  finally
    if Owner <> 0 then NexaLocalFree(Owner);
    if Token <> 0 then NexaCloseHandle(Token);
  end;
  if not Result then
    RaiseException('Cannot establish administrator-owned system installation.');
end;
