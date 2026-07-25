// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;

namespace VideoSerialVisualizer.Views;

public partial class MarkerDialog : Window
{
    public string? ResultText { get; private set; }

    public MarkerDialog(string currentText, string? title = null)
    {
        InitializeComponent();
        if (title is not null)
            Title = title;

        TextBox.Text = currentText;
        Loaded += (_, _) =>
        {
            TextBox.Focus();
            TextBox.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var text = TextBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        ResultText = text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static string? Prompt(string currentText, Window? owner, string? title = null)
    {
        var dialog = new MarkerDialog(currentText, title) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }
}
