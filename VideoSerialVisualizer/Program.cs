// Video Serial Visualizer - reproductor de series de video que conserva tu progreso exacto
// en la linea de tiempo entre sesiones, y organiza carpetas enteras de cursos en un solo lugar.
// Copyright (C) 2026  David Nieves
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Windows;
using Velopack;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.Services;

namespace VideoSerialVisualizer;

/// <summary>
/// Punto de entrada explicito de la aplicacion.
///
/// WPF normalmente genera este Main solo (a partir de App.xaml), pero Velopack necesita ejecutarse
/// como PRIMERA instruccion del proceso: durante instalar, actualizar o desinstalar, el instalador
/// corre este mismo exe con argumentos especiales, hace la tarea y termina. Si WPF ya se hubiera
/// inicializado, esos pasos podrian llegar a mostrar ventana o quedar a medias.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        // El idioma se fija antes de crear la ventana: la pantalla de carga ya aparece traducida.
        // Si el usuario nunca eligio uno, se usa el de Windows (con ingles como respaldo).
        var settings = AppSettings.Load();
        Loc.I.SetLanguage(settings.Language ?? Loc.DetectSystemLanguage());

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
