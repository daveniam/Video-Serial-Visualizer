# Avisos de terceros

Video Serial Visualizer incluye y utiliza los siguientes componentes de terceros.
Las licencias indicadas fueron tomadas de los metadatos de cada paquete NuGet distribuido.

---

## VLC / libVLC — VideoLAN

- **Componente:** `VideoLAN.LibVLC.Windows` (binarios de libVLC: `libvlc.dll`, `libvlccore.dll` y plugins)
- **Autor:** VideoLAN y el equipo de VLC
- **Sitio:** https://www.videolan.org/vlc/
- **Licencia del paquete NuGet:** LGPL-2.1-or-later
- **Atención:** el núcleo de libVLC es LGPL-2.1-or-later, pero **el conjunto de plugins que
  acompaña a VLC contiene componentes bajo GPL**. Distribuir el set completo de plugins implica
  que la obra distribuida queda sujeta a la GPL. Ver la sección "Licenciamiento" más abajo.

Se utiliza sin modificaciones, cargado dinámicamente desde la carpeta `libvlc/win-x64`.

## LibVLCSharp — VideoLAN

- **Componentes:** `LibVLCSharp`, `LibVLCSharp.WPF`
- **Autor:** VideoLAN
- **Sitio:** https://code.videolan.org/videolan/LibVLCSharp
- **Licencia:** LGPL-2.1-or-later

Se utiliza sin modificaciones, referenciado como biblioteca dinámica.

## CommunityToolkit.Mvvm — .NET Foundation / Microsoft

- **Sitio:** https://github.com/CommunityToolkit/dotnet
- **Licencia:** MIT

## Entity Framework Core (proveedor SQLite) — Microsoft

- **Componente:** `Microsoft.EntityFrameworkCore.Sqlite`
- **Sitio:** https://docs.microsoft.com/ef/core/
- **Licencia:** MIT

## Velopack

- **Sitio:** https://github.com/velopack/velopack
- **Licencia:** MIT

Se usa para el instalador y las actualizaciones automáticas.

## SQLite

Incluido a través del proveedor de EF Core. SQLite es de **dominio público**.
Sitio: https://www.sqlite.org/copyright.html

---

## Licenciamiento de este proyecto

Video Serial Visualizer
Copyright (C) 2026 David Nieves

Se distribuye bajo la **GNU General Public License v3.0 o posterior** (ver el archivo `LICENSE`).

La razón de elegir GPL es concreta: la aplicación distribuye el conjunto de plugins de VLC, y
algunos de esos plugins están licenciados bajo GPL. Al distribuirlos junto con la aplicación, la
obra resultante queda sujeta a los términos de la GPL. Publicar el código fuente bajo GPL es la
forma más simple y segura de cumplir con esa condición.

Como consecuencia, cualquiera puede usar, estudiar, modificar y redistribuir este software,
siempre que las obras derivadas se distribuyan también bajo la GPL y con su código fuente
disponible.

**Nota:** este documento es un resumen informativo con fines de atribución y no constituye
asesoría legal. Si el proyecto cambiara a un modelo comercial o de código cerrado, el punto de los
plugins GPL de VLC debe revisarse con asesoramiento profesional: VideoLAN publica orientación sobre
cómo generar una distribución de libVLC que incluya únicamente componentes LGPL.
