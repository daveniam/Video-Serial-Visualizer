// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Controls;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.Services;

namespace VideoSerialVisualizer.Views;

public partial class SettingsWindow : Window
{
    private bool _isLoadingLanguages;
    private bool _isInitializing;

    public SettingsWindow()
    {
        InitializeComponent();

        // Se rellenan los controles con el estado actual sin que eso dispare el guardado.
        _isInitializing = true;

        _isLoadingLanguages = true;
        LanguageCombo.ItemsSource = Loc.Available;
        LanguageCombo.SelectedItem = Loc.Available.FirstOrDefault(l => l.Code == Loc.I.CurrentCode);
        _isLoadingLanguages = false;

        AnimatorModeCheck.IsChecked = AppSettings.Load().AnimatorModeEnabled;

        _isInitializing = false;
    }

    private void AnimatorMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        // Se guarda al momento, igual que el idioma: si la app se cierra de golpe, la preferencia
        // ya quedo registrada. Toma efecto la proxima vez que se abre un video.
        var settings = AppSettings.Load();
        settings.AnimatorModeEnabled = AnimatorModeCheck.IsChecked == true;
        settings.Save();
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingLanguages || LanguageCombo.SelectedItem is not LanguageOption option)
            return;

        Loc.I.SetLanguage(option.Code);

        // La preferencia se guarda al momento: si la app se cierra de forma abrupta, el idioma
        // elegido ya quedo registrado.
        var settings = AppSettings.Load();
        settings.Language = option.Code;
        settings.Save();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
