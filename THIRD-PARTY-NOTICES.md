# Avisos de terceros

Video Serial Visualizer incluye y utiliza los siguientes componentes de terceros.
Las licencias indicadas fueron tomadas de los metadatos de cada paquete NuGet distribuido.

Los textos completos de esas licencias estan en la carpeta `licenses/`, que se distribuye
junto al ejecutable. La licencia de esta aplicacion esta en el archivo `LICENSE`.

---

## VLC / libVLC — VideoLAN

- **Componente:** `VideoLAN.LibVLC.Windows` **3.0.21** (binarios de libVLC: `libvlc.dll`,
  `libvlccore.dll` y el conjunto de plugins en `libvlc/win-x64/plugins`)
- **Autor:** VideoLAN y el equipo de VLC
- **Sitio:** https://www.videolan.org/vlc/
- **Licencia del paquete NuGet:** LGPL-2.1-or-later (texto en `licenses/LGPL-2.1.txt`)
- **Atención:** el núcleo de libVLC es LGPL-2.1-or-later, pero **el conjunto de plugins que
  acompaña a VLC contiene componentes bajo GPL** (por ejemplo el codificador x264 y los
  plugins derivados de FFmpeg compilados en modo GPL). Distribuir el set completo de plugins
  implica que la obra distribuida queda sujeta a la GPL. Ver la sección "Licenciamiento" más abajo.

Se utiliza sin modificaciones, cargado dinámicamente desde la carpeta `libvlc/win-x64`.

## LibVLCSharp — VideoLAN

- **Componentes:** `LibVLCSharp`, `LibVLCSharp.WPF` **3.9.0**
- **Autor:** VideoLAN
- **Sitio:** https://code.videolan.org/videolan/LibVLCSharp
- **Licencia:** LGPL-2.1-or-later (texto en `licenses/LGPL-2.1.txt`)

Se utiliza sin modificaciones, referenciado como biblioteca dinámica.

## CommunityToolkit.Mvvm — .NET Foundation / Microsoft

- **Versión:** 8.3.2
- **Sitio:** https://github.com/CommunityToolkit/dotnet
- **Licencia:** MIT (texto y avisos de copyright en `licenses/MIT.txt`)

## Entity Framework Core (proveedor SQLite) — Microsoft

- **Componentes:** `Microsoft.EntityFrameworkCore.Sqlite` **8.0.10**, `Microsoft.Data.Sqlite`
  y sus dependencias `Microsoft.Extensions.*`
- **Sitio:** https://github.com/dotnet/efcore
- **Licencia:** MIT (texto y avisos de copyright en `licenses/MIT.txt`)

## SQLitePCLRaw — SourceGear, LLC / Eric Sink

- **Componentes:** `SQLitePCLRaw.core`, `SQLitePCLRaw.batteries_v2`,
  `SQLitePCLRaw.provider.e_sqlite3` **2.1.6** y la biblioteca nativa `e_sqlite3.dll`
- **Copyright:** Copyright 2014-2023 SourceGear, LLC
- **Sitio:** https://github.com/ericsink/SQLitePCL.raw
- **Licencia:** Apache-2.0 (texto en `licenses/Apache-2.0.txt`)

Llega como dependencia de Microsoft.Data.Sqlite y es lo que provee el motor SQLite nativo.

## Velopack

- **Versión:** 1.2.0
- **Copyright:** Copyright © Velopack Ltd.
- **Sitio:** https://github.com/velopack/velopack
- **Licencia:** MIT (texto y avisos de copyright en `licenses/MIT.txt`)

Se usa para el instalador y las actualizaciones automáticas.

## .NET Runtime — .NET Foundation / Microsoft

- **Componente:** runtime de .NET 8 y bibliotecas base, incluidas en la publicación
  *self-contained* (`System.*.dll`, `PresentationFramework.dll`, `WindowsBase.dll`, etc.)
- **Sitio:** https://github.com/dotnet/runtime
- **Licencia:** MIT (texto y avisos de copyright en `licenses/MIT.txt`)

## SQLite

El motor SQLite viaja dentro de `e_sqlite3.dll` (ver SQLitePCLRaw). SQLite es de
**dominio público** y no impone condiciones de redistribución.
Sitio: https://www.sqlite.org/copyright.html

---

## Cómo obtener el código fuente

La GPL y la LGPL no piden solamente atribución: piden que quien recibe el binario pueda
obtener el **código fuente correspondiente** de todo lo que se le distribuyó.

### De esta aplicación

Código fuente completo, bajo GPL-3.0-or-later:

    https://github.com/daveniam/Video-Serial-Visualizer

Cada release publicada tiene su tag correspondiente en ese repositorio. La versión exacta
que estás ejecutando figura en la ventana **Acerca de**.

### De libVLC y sus plugins

Los binarios de libVLC que acompañan a esta aplicación **no fueron compilados por este
proyecto**: se toman sin modificación alguna del paquete NuGet oficial de VideoLAN
`VideoLAN.LibVLC.Windows` versión **3.0.21**, publicado en
https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.21

El código fuente correspondiente a esa versión lo publica VideoLAN en:

    https://download.videolan.org/pub/videolan/vlc/3.0.21/
    https://code.videolan.org/videolan/vlc/-/tree/3.0.21

El empaquetado NuGet de esos binarios está en:

    https://code.videolan.org/videolan/libvlc-nuget

### De LibVLCSharp

    https://code.videolan.org/videolan/LibVLCSharp

### Del resto de los componentes

Los componentes MIT y Apache-2.0 listados arriba enlazan a su repositorio público, donde
está disponible el código fuente de cada versión distribuida.

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
