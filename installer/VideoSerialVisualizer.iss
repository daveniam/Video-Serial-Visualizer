; Instalador clasico (Inno Setup) para Video Serial Visualizer.
;
; A diferencia del Setup.exe de Velopack (un clic, carpeta fija en %LocalAppData%), este muestra
; un asistente que DEJA ELEGIR la carpeta de instalacion. A cambio, la app instalada asi NO se
; auto-actualiza: para actualizar se descarga y se corre el nuevo Setup, que reinstala en el lugar.
;
; La version se pasa desde build-installer.ps1 con /DMyAppVersion=<x.y.z>; si no, cae al valor de
; abajo. El contenido a empaquetar sale de la carpeta 'publish' que genera "dotnet publish".

#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif

#define MyAppName "Video Serial Visualizer"
#define MyAppPublisher "David Nieves"
#define MyAppURL "https://github.com/daveniam/Video-Serial-Visualizer"
#define MyAppExeName "VideoSerialVisualizer.exe"
#define MyPublishDir "..\VideoSerialVisualizer\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
; AppId FIJO: identifica el producto entre versiones para que un Setup nuevo actualice en el lugar
; en vez de instalar una copia paralela. No cambiar nunca una vez publicado.
AppId={{7F3A9C21-4B6E-4D8A-9F12-2E5C7A9B3D40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; Se instala por usuario (sin pedir administrador). El usuario puede navegar a cualquier carpeta
; sobre la que tenga permiso de escritura, incluida otra unidad (D:\, etc.).
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Video Serial Visualizer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; La pagina de "Seleccionar carpeta de destino" queda VISIBLE (es justamente lo que se pedia).
DisableDirPage=no
OutputDir=..\Releases\inno
OutputBaseFilename=VideoSerialVisualizer-Setup
SetupIconFile=..\VideoSerialVisualizer\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; La app es x64-only (self-contained win-x64).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Mismo mutex de instancia unica que crea la app (App.xaml.cs): si esta abierta, el asistente pide
; cerrarla antes de instalar, para no reemplazar archivos en uso.
AppMutex=VideoSerialVisualizer-SingleInstance-9F3B2A7C

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; Todo el resultado de 'dotnet publish' (exe + DLLs + LibVLC + FFmpeg + licencias) va a la carpeta
; elegida. 'ignoreversion' evita que el versionado de DLLs de terceros impida sobrescribir al
; actualizar.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
{ Quita las OTRAS instalaciones de la app antes de copiar los archivos, para que nunca queden dos
  programas instalados (la app hace lo mismo del otro lado al instalarse con Velopack, ver
  Services\OtherInstallsCleaner.cs):
    * Velopack (Setup con auto-actualizacion): %LocalAppData%\VideoSerialVisualizer. Se usa su
      propio desinstalador en modo silencioso, que tambien quita sus accesos directos y su entrada
      en "Aplicaciones".
    * Install.ps1 (el script de las primeras versiones, sin desinstalador): una carpeta en
      %LocalAppData%\Programs; se borran la carpeta y SUS accesos directos.
  Solo se borran programas: la biblioteca, el progreso y las miniaturas viven en
  %LocalAppData%\TutorialHub y no se tocan. }

{ True si Path es Dir o esta dentro de Dir (sin distinguir mayusculas). }
function IsSameOrInside(Path, Dir: String): Boolean;
begin
  Path := Lowercase(RemoveBackslashUnlessRoot(Path));
  Dir := Lowercase(RemoveBackslashUnlessRoot(Dir));
  Result := (Path = Dir) or (Pos(Dir + '\', Path) = 1);
end;

{ Borra un acceso directo solo si apunta DENTRO de Dir. Se mira el destino y no el nombre: todas
  las instalaciones crean un "Video Serial Visualizer.lnk", y por nombre se borraria tambien el
  de esta instalacion. }
procedure DeleteShortcutIfInside(LinkPath, Dir: String);
var
  Shell, Link: Variant;
begin
  if not FileExists(LinkPath) then
    exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Link := Shell.CreateShortcut(LinkPath);
    if IsSameOrInside(Link.TargetPath, Dir) then
      DeleteFile(LinkPath);
  except
    { un acceso directo ilegible no frena la instalacion }
  end;
end;

{ Borra una instalacion que es solo una carpeta (la del script viejo), con sus accesos directos.
  Nunca la carpeta donde se esta instalando ahora. }
procedure RemoveFolderInstall(Dir: String);
begin
  if not DirExists(Dir) then
    exit;
  if IsSameOrInside(WizardDirValue, Dir) or IsSameOrInside(Dir, WizardDirValue) then
    exit;
  Log('Quitando instalacion anterior: ' + Dir);
  DeleteShortcutIfInside(ExpandConstant('{userdesktop}\Video Serial Visualizer.lnk'), Dir);
  DeleteShortcutIfInside(ExpandConstant('{userprograms}\Video Serial Visualizer.lnk'), Dir);
  DeleteShortcutIfInside(ExpandConstant('{userdesktop}\TutorialHub.lnk'), Dir);
  DeleteShortcutIfInside(ExpandConstant('{userprograms}\TutorialHub.lnk'), Dir);
  DelTree(Dir, True, True, True);
end;

{ Desinstala en silencio la copia de Velopack, si la hay. }
procedure RemoveVelopackInstall();
var
  Dir, UpdateExe: String;
  ResultCode: Integer;
begin
  Dir := ExpandConstant('{localappdata}\VideoSerialVisualizer');
  UpdateExe := Dir + '\Update.exe';
  if not FileExists(UpdateExe) then
    exit;
  if IsSameOrInside(WizardDirValue, Dir) then
    exit;
  Log('Desinstalando la copia de Velopack: ' + Dir);
  if not Exec(UpdateExe, '--silent uninstall', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('No se pudo ejecutar el desinstalador de Velopack: ' + SysErrorMessage(ResultCode))
  else
    Log('Desinstalador de Velopack termino con codigo ' + IntToStr(ResultCode));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { ssInstall: justo antes de copiar archivos y crear accesos directos. Asi los desinstaladores
    de las otras copias no pueden llevarse los accesos directos que crea esta instalacion. }
  if CurStep = ssInstall then
  begin
    RemoveVelopackInstall();
    RemoveFolderInstall(ExpandConstant('{localappdata}\Programs\VideoSerialVisualizer'));
    RemoveFolderInstall(ExpandConstant('{localappdata}\Programs\TutorialHub'));
  end;
end;
