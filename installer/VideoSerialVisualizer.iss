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
