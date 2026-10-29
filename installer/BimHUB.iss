#ifndef AppVersion
  #define AppVersion "0.2.0-alpha.1"
#endif

#ifndef NumericVersion
  #define NumericVersion "0.2.0.1"
#endif

#ifndef PayloadRoot
  #error PayloadRoot must point to the staged Revit payloads.
#endif

#ifndef OutputRoot
  #define OutputRoot ".\Output"
#endif

#define ProductId "QBTools.BimHUB.E42E8434-08C2-4F1F-858E-61EA81B71C8C"
#define AddInId "830DF0C2-7562-4041-A65C-00C189748996"
#define AddInClass "QBTools.BimHUB.BimHubApplication"

[Setup]
AppId={#ProductId}
AppName=BimHUB
AppVerName=BimHUB {#AppVersion}
AppVersion={#AppVersion}
AppPublisher=QBTools
AppPublisherURL=https://github.com/Qubaism/BimHUB
AppSupportURL=https://github.com/Qubaism/BimHUB/issues
AppUpdatesURL=https://github.com/Qubaism/BimHUB/releases
AppCopyright=Copyright (C) QBTools contributors
DefaultDirName={localappdata}\Programs\QBTools\BimHUB
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern dynamic
WizardSmallImageFile=..\src\QBTools.BimHUB\Assets\BimHUB-32.png
WizardSmallImageBackColor=none
SetupIconFile=..\src\QBTools.BimHUB\Assets\BimHUB.ico
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutputRoot}
OutputBaseFilename=BimHUB-{#AppVersion}-Setup
VersionInfoVersion={#NumericVersion}
VersionInfoCompany=QBTools
VersionInfoDescription=BimHUB installer
VersionInfoProductName=BimHUB
VersionInfoProductTextVersion={#AppVersion}
Uninstallable=yes
CreateUninstallRegKey=yes
UninstallDisplayName=BimHUB by QBTools
UninstallDisplayIcon={app}\BimHUB.ico
CloseApplications=no
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
UsePreviousAppDir=yes
UsePreviousLanguage=yes

[Files]
Source: "..\src\QBTools.BimHUB\Assets\BimHUB.ico"; DestDir: "{app}"; Flags: ignoreversion
#ifdef HasRevit2023Net48
Source: "{#PayloadRoot}\Revit2023-net48\*"; DestDir: "{app}\Revit\2023"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallPayload('2023', 'net48')
#endif
#ifdef HasRevit2024Net48
Source: "{#PayloadRoot}\Revit2024-net48\*"; DestDir: "{app}\Revit\2024"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallPayload('2024', 'net48')
#endif
#ifdef HasRevit2025Net8
Source: "{#PayloadRoot}\Revit2025-net8\*"; DestDir: "{app}\Revit\2025"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallPayload('2025', 'net8')
#endif
#ifdef HasRevit2025Net10
Source: "{#PayloadRoot}\Revit2025-net10\*"; DestDir: "{app}\Revit\2025"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallPayload('2025', 'net10')
#endif
#ifdef HasRevit2026Net8
Source: "{#PayloadRoot}\Revit2026-net8\*"; DestDir: "{app}\Revit\2026"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallPayload('2026', 'net8')
#endif
#ifdef HasRevit2026Net10
Source: "{#PayloadRoot}\Revit2026-net10\*"; DestDir: "{app}\Revit\2026"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallPayload('2026', 'net10')
#endif
#ifdef HasRevit2027Net10
Source: "{#PayloadRoot}\Revit2027-net10\*"; DestDir: "{app}\Revit\2027"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallPayload('2027', 'net10')
#endif

[InstallDelete]
Type: filesandordirs; Name: "{app}\Revit\2023\*"; Check: ShouldInstallVersion('2023')
Type: filesandordirs; Name: "{app}\Revit\2024\*"; Check: ShouldInstallVersion('2024')
Type: filesandordirs; Name: "{app}\Revit\2025\*"; Check: ShouldInstallVersion('2025')
Type: filesandordirs; Name: "{app}\Revit\2026\*"; Check: ShouldInstallVersion('2026')
Type: filesandordirs; Name: "{app}\Revit\2027\*"; Check: ShouldInstallVersion('2027')

[Code]
const
  BimHubAddInId = '{#AddInId}';
  BimHubClassName = '{#AddInClass}';

var
  RevitPage: TInputOptionWizardPage;
  Revit2023Index: Integer;
  Revit2024Index: Integer;
  Revit2025Index: Integer;
  Revit2026Index: Integer;
  Revit2027Index: Integer;

function TryFindRevitExecutableInUninstallRoot(
  RootKey: Integer; Version: String; var ExecutablePath: String): Boolean;
var
  Candidate: String;
  DisplayName: String;
  I: Integer;
  InstallLocation: String;
  KeyPath: String;
  Subkeys: TArrayOfString;
begin
  Result := False;
  KeyPath := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall';
  if not RegGetSubkeyNames(RootKey, KeyPath, Subkeys) then
    Exit;

  for I := 0 to GetArrayLength(Subkeys) - 1 do
  begin
    if not RegQueryStringValue(
      RootKey, KeyPath + '\' + Subkeys[I], 'DisplayName', DisplayName) then
      Continue;
    if CompareText(Trim(DisplayName), 'Autodesk Revit ' + Version) <> 0 then
      Continue;
    if not RegQueryStringValue(
      RootKey, KeyPath + '\' + Subkeys[I], 'InstallLocation', InstallLocation) then
      Continue;

    Candidate := AddBackslash(InstallLocation) + 'Revit ' + Version +
      '\Revit.exe';
    if FileExists(Candidate) then
    begin
      ExecutablePath := Candidate;
      Result := True;
      Exit;
    end;

    Candidate := AddBackslash(InstallLocation) + 'Revit.exe';
    if FileExists(Candidate) then
    begin
      ExecutablePath := Candidate;
      Result := True;
      Exit;
    end;
  end;
end;

function RevitExecutablePath(Version: String): String;
begin
  if TryFindRevitExecutableInUninstallRoot(HKLM64, Version, Result) then
    Exit;
  if TryFindRevitExecutableInUninstallRoot(HKCU64, Version, Result) then
    Exit;

  Result := ExpandConstant('{commonpf64}\Autodesk\Revit ' + Version +
    '\Revit.exe');
end;

function IsRevitInstalled(Version: String): Boolean;
begin
  Result := FileExists(RevitExecutablePath(Version));
end;

function RequiredRuntime(Version: String): String;
var
  Major: Word;
  Minor: Word;
  Revision: Word;
  Build: Word;
begin
  if (Version = '2023') or (Version = '2024') then
  begin
    Result := 'net48';
    Exit;
  end;

  if Version = '2027' then
  begin
    Result := 'net10';
    Exit;
  end;

  if not GetVersionComponents(RevitExecutablePath(Version), Major, Minor,
    Revision, Build) then
    RaiseException('BimHUB could not determine the installed Revit ' +
      Version + ' build version.');

  if Minor >= 5 then
    Result := 'net10'
  else
    Result := 'net8';
end;

function HasPayload(Version: String; Runtime: String): Boolean;
begin
  Result := False;
#ifdef HasRevit2023Net48
  if (Version = '2023') and (Runtime = 'net48') then Result := True;
#endif
#ifdef HasRevit2024Net48
  if (Version = '2024') and (Runtime = 'net48') then Result := True;
#endif
#ifdef HasRevit2025Net8
  if (Version = '2025') and (Runtime = 'net8') then Result := True;
#endif
#ifdef HasRevit2025Net10
  if (Version = '2025') and (Runtime = 'net10') then Result := True;
#endif
#ifdef HasRevit2026Net8
  if (Version = '2026') and (Runtime = 'net8') then Result := True;
#endif
#ifdef HasRevit2026Net10
  if (Version = '2026') and (Runtime = 'net10') then Result := True;
#endif
#ifdef HasRevit2027Net10
  if (Version = '2027') and (Runtime = 'net10') then Result := True;
#endif
end;

function IsSupportedInstallation(Version: String): Boolean;
begin
  Result := False;
  if not IsRevitInstalled(Version) then
    Exit;

  Result := HasPayload(Version, RequiredRuntime(Version));
end;

function SelectedAt(Index: Integer): Boolean;
begin
  Result := (Index >= 0) and RevitPage.Values[Index];
end;

function ShouldInstallVersion(Version: String): Boolean;
begin
  if Version = '2023' then
    Result := SelectedAt(Revit2023Index)
  else if Version = '2024' then
    Result := SelectedAt(Revit2024Index)
  else if Version = '2025' then
    Result := SelectedAt(Revit2025Index)
  else if Version = '2026' then
    Result := SelectedAt(Revit2026Index)
  else if Version = '2027' then
    Result := SelectedAt(Revit2027Index)
  else
    Result := False;
end;

function IsVersionSelectable(Version: String): Boolean;
begin
  if Version = '2023' then
    Result := Revit2023Index >= 0
  else if Version = '2024' then
    Result := Revit2024Index >= 0
  else if Version = '2025' then
    Result := Revit2025Index >= 0
  else if Version = '2026' then
    Result := Revit2026Index >= 0
  else if Version = '2027' then
    Result := Revit2027Index >= 0
  else
    Result := False;
end;

function ShouldInstallPayload(Version: String; Runtime: String): Boolean;
begin
  Result := ShouldInstallVersion(Version) and
    (RequiredRuntime(Version) = Runtime);
end;

function ManifestPath(Version: String): String;
begin
  Result := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' +
    Version + '\QBTools.BimHUB.addin');
end;

function LegacyManifestPath(Version: String): String;
begin
  Result := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' +
    Version + '\BimHUB.addin');
end;

function IsOwnedManifest(Path: String): Boolean;
var
  Contents: AnsiString;
begin
  Result := False;
  if not FileExists(Path) then
    Exit;

  if LoadStringFromFile(Path, Contents) then
    Result := (Pos(BimHubAddInId, String(Contents)) > 0) and
      (Pos(BimHubClassName, String(Contents)) > 0);
end;

function XmlEscape(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '&', '&amp;', True);
  StringChangeEx(Result, '<', '&lt;', True);
  StringChangeEx(Result, '>', '&gt;', True);
  StringChangeEx(Result, '"', '&quot;', True);
  StringChangeEx(Result, '''', '&apos;', True);
end;

procedure RemoveOwnedManifest(Path: String);
begin
  if IsOwnedManifest(Path) then
    DeleteFile(Path);
end;

procedure CreateManifest(Version: String);
var
  AssemblyPath: String;
  DestinationPath: String;
  Lines: TArrayOfString;
  TemporaryPath: String;
begin
  DestinationPath := ManifestPath(Version);
  TemporaryPath := DestinationPath + '.tmp';
  AssemblyPath := ExpandConstant('{app}\Revit\' + Version +
    '\QBTools.BimHUB.dll');

  if FileExists(DestinationPath) and not IsOwnedManifest(DestinationPath) then
    RaiseException('BimHUB cannot install for Revit ' + Version +
      ' because another add-in owns ' + DestinationPath + '.');

  ForceDirectories(ExtractFileDir(DestinationPath));
  SetArrayLength(Lines, 11);
  Lines[0] := '<?xml version="1.0" encoding="utf-8" standalone="no"?>';
  Lines[1] := '<RevitAddIns>';
  Lines[2] := '  <AddIn Type="Application">';
  Lines[3] := '    <Name>BimHUB</Name>';
  Lines[4] := '    <Assembly>' + XmlEscape(AssemblyPath) + '</Assembly>';
  Lines[5] := '    <AddInId>' + BimHubAddInId + '</AddInId>';
  Lines[6] := '    <FullClassName>' + BimHubClassName + '</FullClassName>';
  Lines[7] := '    <VendorId>QBTS</VendorId>';
  Lines[8] := '    <VendorDescription>QBTools</VendorDescription>';
  Lines[9] := '  </AddIn>';
  Lines[10] := '</RevitAddIns>';

  DeleteFile(TemporaryPath);
  if not SaveStringsToUTF8FileWithoutBOM(TemporaryPath, Lines, False) then
    RaiseException('BimHUB could not write the Revit ' + Version +
      ' add-in manifest.');

  if FileExists(DestinationPath) and not DeleteFile(DestinationPath) then
    RaiseException('BimHUB could not replace its Revit ' + Version +
      ' add-in manifest.');

  if not RenameFile(TemporaryPath, DestinationPath) then
    RaiseException('BimHUB could not finish the Revit ' + Version +
      ' add-in manifest.');
end;

procedure AddDetectedVersion(Version: String; var ItemIndex: Integer);
var
  Runtime: String;
  RuntimeLabel: String;
begin
  ItemIndex := -1;
  if IsRevitInstalled(Version) then
  begin
    Runtime := RequiredRuntime(Version);
    if not HasPayload(Version, Runtime) then
      Exit;

    if Runtime = 'net48' then
      RuntimeLabel := '.NET Framework 4.8'
    else if Runtime = 'net8' then
      RuntimeLabel := '.NET 8'
    else
      RuntimeLabel := '.NET 10';

    ItemIndex := RevitPage.CheckListBox.Items.Count;
    RevitPage.Add('Autodesk Revit ' + Version + ' (' + RuntimeLabel + ')');
    RevitPage.Values[ItemIndex] := True;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := IsSupportedInstallation('2023') or
    IsSupportedInstallation('2024') or IsSupportedInstallation('2025') or
    IsSupportedInstallation('2026') or IsSupportedInstallation('2027');

  if not Result then
    MsgBox('BimHUB requires a supported Autodesk Revit 2023-2027 ' +
      'installation. No compatible Revit installation was detected in this ' +
      'installer package.', mbError, MB_OK);
end;

procedure InitializeWizard();
begin
  RevitPage := CreateInputOptionPage(wpLicense,
    'Select Revit versions',
    'BimHUB detected the following Autodesk Revit installations.',
    'Select the versions where BimHUB should be installed, then click Next.',
    False, False);

  AddDetectedVersion('2023', Revit2023Index);
  AddDetectedVersion('2024', Revit2024Index);
  AddDetectedVersion('2025', Revit2025Index);
  AddDetectedVersion('2026', Revit2026Index);
  AddDetectedVersion('2027', Revit2027Index);
end;

function AnyVersionSelected(): Boolean;
begin
  Result := SelectedAt(Revit2023Index) or SelectedAt(Revit2024Index) or
    SelectedAt(Revit2025Index) or SelectedAt(Revit2026Index) or
    SelectedAt(Revit2027Index);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = RevitPage.ID) and not AnyVersionSelected() then
  begin
    MsgBox('Select at least one detected Revit version.', mbError, MB_OK);
    Result := False;
  end;
end;

function IsRevitRunning(): Boolean;
var
  ResultCode: Integer;
  PowerShellPath: String;
  Parameters: String;
begin
  PowerShellPath := ExpandConstant(
    '{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoProfile -NonInteractive -Command "if ' +
    '(Get-Process -Name Revit -ErrorAction SilentlyContinue) ' +
    '{ exit 10 } else { exit 0 }"';

  Result := Exec(PowerShellPath, Parameters, '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) and (ResultCode = 10);
end;

function FindManifestConflict(): String;
var
  Version: String;
  Path: String;
  I: Integer;
begin
  Result := '';
  for I := 0 to 4 do
  begin
    case I of
      0: Version := '2023';
      1: Version := '2024';
      2: Version := '2025';
      3: Version := '2026';
      4: Version := '2027';
    end;

    if ShouldInstallVersion(Version) then
    begin
      Path := ManifestPath(Version);
      if FileExists(Path) and not IsOwnedManifest(Path) then
      begin
        Result := 'Revit ' + Version + ' already has an unrelated add-in ' +
          'manifest named QBTools.BimHUB.addin. BimHUB will not overwrite it:' +
          Chr(13) + Chr(10) + Path;
        Exit;
      end;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := FindManifestConflict();
  if Result <> '' then
    Exit;

  if IsRevitRunning() then
    Result := 'Close all Autodesk Revit windows before installing, repairing, ' +
      'upgrading, or uninstalling BimHUB.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := not IsRevitRunning();
  if not Result then
    MsgBox('Close all Autodesk Revit windows before uninstalling BimHUB.',
      mbError, MB_OK);
end;

procedure FinishVersion(Version: String);
var
  PackagePath: String;
begin
  if ShouldInstallVersion(Version) then
  begin
    CreateManifest(Version);
    { Remove the manifest name used by early development deployments, but only
      when this package is replacing that Revit version. }
    RemoveOwnedManifest(LegacyManifestPath(Version));
  end
  else if IsVersionSelectable(Version) or not IsRevitInstalled(Version) then
  begin
    RemoveOwnedManifest(ManifestPath(Version));
    RemoveOwnedManifest(LegacyManifestPath(Version));
    PackagePath := ExpandConstant('{app}\Revit\' + Version);
    if DirExists(PackagePath) then
      DelTree(PackagePath, True, True, True);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    FinishVersion('2023');
    FinishVersion('2024');
    FinishVersion('2025');
    FinishVersion('2026');
    FinishVersion('2027');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveOwnedManifest(ManifestPath('2023'));
    RemoveOwnedManifest(ManifestPath('2024'));
    RemoveOwnedManifest(ManifestPath('2025'));
    RemoveOwnedManifest(ManifestPath('2026'));
    RemoveOwnedManifest(ManifestPath('2027'));
    RemoveOwnedManifest(LegacyManifestPath('2023'));
    RemoveOwnedManifest(LegacyManifestPath('2024'));
    RemoveOwnedManifest(LegacyManifestPath('2025'));
    RemoveOwnedManifest(LegacyManifestPath('2026'));
    RemoveOwnedManifest(LegacyManifestPath('2027'));
  end;
end;
