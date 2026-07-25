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

## FFMediaToolkit — Radosław Kmiotek

- **Versión:** 4.8.1
- **Copyright:** Copyright (c) 2019-2025 Radosław Kmiotek
- **Sitio:** https://github.com/radek-k/FFMediaToolkit
- **Licencia:** MIT (texto y avisos de copyright en `licenses/MIT.txt`)

Binding administrado sobre FFmpeg. Habilita la decodificación cuadro a cuadro exacta del modo
animador. El paquete NuGet **no** contiene binarios de FFmpeg; esos se distribuyen aparte (ver
"FFmpeg (binarios nativos)" a continuación).

## Markdig — Alexandre Mutel

- **Versión:** 0.22.0
- **Copyright:** Copyright (c) Alexandre Mutel. All rights reserved.
- **Sitio:** https://github.com/lunet-io/markdig
- **Licencia:** BSD-2-Clause (texto en `licenses/BSD-2-Clause.txt`)

Analizador de Markdown (CommonMark). Convierte el texto de las etiquetas de linea de tiempo
(modo animador) a un documento con formato.

## Markdig.Wpf — Nicolas Musset

- **Versión:** 0.5.0.1
- **Copyright:** Copyright © Nicolas Musset 2016-2021
- **Sitio:** https://github.com/Kryptos-FR/markdig-wpf
- **Licencia:** MIT (texto y avisos de copyright en `licenses/MIT.txt`)

Renderiza el documento de Markdig como un FlowDocument de WPF, para la vista previa de las
etiquetas de linea de tiempo.

## FFmpeg (binarios nativos) — proyecto FFmpeg

- **Componentes:** `avcodec-61.dll`, `avformat-61.dll`, `avutil-59.dll`, `swscale-8.dll`,
  `swresample-5.dll` de **FFmpeg 7.1**, en la carpeta `ffmpeg/` junto al ejecutable
- **Sitio:** https://ffmpeg.org/
- **Origen de este build:** build Windows **LGPL** *shared* de FFmpeg 7.1 publicado por el proyecto
  comunitario BtbN (https://github.com/BtbN/FFmpeg-Builds), sin modificaciones
- **Licencia:** LGPL-2.1-or-later (texto en `licenses/LGPL-2.1.txt`)

Se distribuye a propósito la variante **LGPL** y no la GPL: esta aplicación solo **decodifica** para
el paso a cuadro del modo animador, así que los componentes GPL del otro build (codificadores como
x264/x265) no hacen falta. La variante LGPL además pesa bastante menos.

Sólo se distribuyen esas cinco bibliotecas: son las únicas que el modo animador utiliza (`avfilter`,
`avdevice` y `postproc` vienen en el build oficial pero no se usan). El resto de la reproducción
sigue a cargo de libVLC, no de estas bibliotecas.

**Cumplimiento de la LGPL:** las bibliotecas se distribuyen como DLL separadas y sin modificar,
cargadas dinámicamente. Quien reciba una copia puede reemplazarlas por su propia versión de FFmpeg
7.x compatible: alcanza con sustituir los archivos de la carpeta `ffmpeg/`.

### Código fuente de FFmpeg

FFmpeg publica su código fuente en https://ffmpeg.org/download.html (la versión distribuida aquí es
la 7.1). Los scripts de build usados para generar estos binarios están en
https://github.com/BtbN/FFmpeg-Builds.

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
