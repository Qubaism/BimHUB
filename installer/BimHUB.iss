#ifndef AppVersion
  #define AppVersion "0.1.0-alpha.2"
#endif

#ifndef NumericVersion
  #define NumericVersion "0.1.0.2"
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
CloseApplications=no
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
UsePreviousAppDir=yes
UsePreviousLanguage=yes

[Files]
Source: "{#PayloadRoot}\Revit2023\*"; DestDir: "{app}\Revit\2023"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallVersion('2023')
Source: "{#PayloadRoot}\Revit2024\*"; DestDir: "{app}\Revit\2024"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallVersion('2024')
Source: "{#PayloadRoot}\Revit2025\*"; DestDir: "{app}\Revit\2025"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallVersion('2025')
Source: "{#PayloadRoot}\Revit2026\*"; DestDir: "{app}\Revit\2026"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallVersion('2026')

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

function RevitExecutablePath(Version: String): String;
begin
  Result := ExpandConstant('{commonpf64}\Autodesk\Revit ' + Version +
    '\Revit.exe');
end;

function IsRevitInstalled(Version: String): Boolean;
begin
  Result := FileExists(RevitExecutablePath(Version));
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
  else
    Result := False;
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
begin
  ItemIndex := -1;
  if IsRevitInstalled(Version) then
  begin
    ItemIndex := RevitPage.CheckListBox.Items.Count;
    RevitPage.Add('Autodesk Revit ' + Version);
    RevitPage.Values[ItemIndex] := True;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := IsRevitInstalled('2023') or IsRevitInstalled('2024') or
    IsRevitInstalled('2025') or IsRevitInstalled('2026');

  if not Result then
    MsgBox('BimHUB requires Autodesk Revit 2023, 2024, 2025, or 2026. ' +
      'No supported Revit installation was detected.', mbError, MB_OK);
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
end;

function AnyVersionSelected(): Boolean;
begin
  Result := SelectedAt(Revit2023Index) or SelectedAt(Revit2024Index) or
    SelectedAt(Revit2025Index) or SelectedAt(Revit2026Index);
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
  for I := 0 to 3 do
  begin
    case I of
      0: Version := '2023';
      1: Version := '2024';
      2: Version := '2025';
      3: Version := '2026';
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
    CreateManifest(Version)
  else
  begin
    RemoveOwnedManifest(ManifestPath(Version));
    PackagePath := ExpandConstant('{app}\Revit\' + Version);
    if DirExists(PackagePath) then
      DelTree(PackagePath, True, True, True);
  end;

  { Remove the manifest name used by early development deployments, but only
    when it contains BimHUB's exact add-in identity. }
  RemoveOwnedManifest(LegacyManifestPath(Version));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    FinishVersion('2023');
    FinishVersion('2024');
    FinishVersion('2025');
    FinishVersion('2026');
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
    RemoveOwnedManifest(LegacyManifestPath('2023'));
    RemoveOwnedManifest(LegacyManifestPath('2024'));
    RemoveOwnedManifest(LegacyManifestPath('2025'));
    RemoveOwnedManifest(LegacyManifestPath('2026'));
  end;
end;
