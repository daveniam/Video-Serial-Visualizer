// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Controls;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.Views;

public partial class AssignCategoryDialog : Window
{
    private readonly List<RadioButton> _optionButtons = new();

    public int? ResultCategoryId { get; private set; }

    public AssignCategoryDialog(IReadOnlyList<Category> categories, int? currentCategoryId)
    {
        InitializeComponent();

        AddOption("Sin categoría", null, currentCategoryId is null);
        foreach (var category in categories)
            AddOption(category.Name, category.Id, currentCategoryId == category.Id);
    }

    private void AddOption(string text, int? id, bool isChecked)
    {
        var radio = new RadioButton
        {
            GroupName = "CategoryOptions",
            Content = text,
            IsChecked = isChecked,
            Tag = id,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 14
        };
        _optionButtons.Add(radio);
        OptionsPanel.Children.Add(radio);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selected = _optionButtons.FirstOrDefault(r => r.IsChecked == true);
        ResultCategoryId = selected?.Tag as int?;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static (bool Confirmed, int? CategoryId) PromptAssign(IReadOnlyList<Category> categories, int? currentCategoryId, Window? owner)
    {
        var dialog = new AssignCategoryDialog(categories, currentCategoryId) { Owner = owner };
        var confirmed = dialog.ShowDialog() == true;
        return (confirmed, confirmed ? dialog.ResultCategoryId : currentCategoryId);
    }
}
