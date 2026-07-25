# Textos de licencia de los componentes distribuidos

Esta carpeta acompana a la aplicacion distribuida (se copia junto al ejecutable, ver el
`ItemGroup` de licencias en `VideoSerialVisualizer.csproj`). No es documentacion: es el
cumplimiento de las licencias de los componentes de terceros, que exigen que su texto
viaje con cada copia del binario.

| Archivo | Cubre |
|---|---|
| `../LICENSE` | Video Serial Visualizer — GPL-3.0-or-later |
| `MIT.txt` | .NET Runtime, CommunityToolkit.Mvvm, EF Core, Microsoft.Data.Sqlite, Velopack, FFMediaToolkit, Markdig.Wpf |
| `LGPL-2.1.txt` | libVLC, LibVLCSharp, FFmpeg (binarios nativos, build LGPL) |
| `Apache-2.0.txt` | SQLitePCLRaw (`e_sqlite3.dll` y bindings) |
| `BSD-2-Clause.txt` | Markdig |

SQLite (el motor en si, dentro de `e_sqlite3.dll`) es de dominio publico y no requiere
texto de licencia: https://www.sqlite.org/copyright.html

El detalle de cada componente, su version y donde obtener su codigo fuente esta en
`../THIRD-PARTY-NOTICES.md`.
