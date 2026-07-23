// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace VideoSerialVisualizer.Views;

public partial class AboutWindow : Window
{
    public const string SourceCodeUrl = "https://github.com/daveniam/Video-Serial-Visualizer";

    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Versión {version?.ToString(3) ?? "1.0.0"}";
        CopyrightText.Text = "Copyright © 2026 David Nieves";
    }

    private void ViewLicense_Click(object sender, RoutedEventArgs e) => OpenBundledFile("LICENSE");

    private void ViewNotices_Click(object sender, RoutedEventArgs e) => OpenBundledFile("THIRD-PARTY-NOTICES.md");

    private void ViewSource_Click(object sender, RoutedEventArgs e) => OpenExternal(SourceCodeUrl);

    /// <summary>
    /// Abre un archivo que se distribuye junto al ejecutable (ver el ItemGroup de Content en el
    /// .csproj). Si no estuviera, se ofrece la copia online en vez de fallar en silencio.
    /// </summary>
    private void OpenBundledFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);

        if (File.Exists(path))
        {
            OpenExternal(path);
            return;
        }

        MessageBox.Show(
            $"No se encontró el archivo \"{fileName}\" junto a la aplicación.\n\n" +
            "Podés consultarlo en el repositorio del proyecto.",
            "Acerca de",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void OpenExternal(string target)
    {
        try
        {
            // UseShellExecute deja que Windows elija con que abrir cada cosa (navegador, bloc de notas).
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Sin aplicacion asociada o ruta invalida: no vale la pena molestar al usuario.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
