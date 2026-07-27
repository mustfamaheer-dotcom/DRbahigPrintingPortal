[Setup]
AppName=DR Bahig Books Portal
AppVersion=1.0.0
AppPublisher=DR Bahig Books
DefaultDirName={commonpf}\BookShopPrintAgent
DefaultGroupName=DR Bahig Books Portal
UninstallDisplayIcon={app}\BookShopPortalUI.exe
OutputDir=.
OutputBaseFilename=DR_Bahig_Books_Portal_Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
DisableProgramGroupPage=yes
DisableWelcomePage=no
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=book.ico
WizardImageFile=wizard.bmp
WizardSmallImageFile=wizardSmall.bmp
AppPublisherURL=https://drbaheegbook.runasp.net
AppSupportURL=https://drbaheegbook.runasp.net

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourcePath}\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commondesktop}\DR Bahig Books Portal"; Filename: "{app}\BookShopPortalUI.exe"; WorkingDir: "{app}"; IconFilename: "{app}\book.ico"
Name: "{group}\DR Bahig Books Portal"; Filename: "{app}\BookShopPortalUI.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall DR Bahig Books Portal"; Filename: "{uninstallexe}"

[Run]
Filename: "schtasks"; Parameters: "/create /tn ""BookShopPrintAgent"" /tr ""'{app}\BookShopPrintAgent.exe'"" /sc onstart /ru SYSTEM /rl highest /f"; Flags: runhidden; StatusMsg: "Creating scheduled task (auto-start on boot)..."
Filename: "{app}\BookShopPrintAgent.exe"; Flags: runhidden nowait; Description: "Start print agent (background service)"
Filename: "{app}\BookShopPortalUI.exe"; Flags: nowait runhidden; Description: "Start DR Bahig Books Portal now"

[UninstallRun]
Filename: "schtasks"; Parameters: "/delete /tn ""BookShopPrintAgent"" /f"; Flags: runhidden; RunOnceId: "RemoveScheduledTask"

[Code]
procedure KillProcess(ExeName: string);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im ' + ExeName + ' 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  KillProcess('BookShopPrintAgent.exe');
  KillProcess('BookShopPortalUI.exe');
  KillProcess('SumatraPDF-3.6.1-64.exe');
end;
