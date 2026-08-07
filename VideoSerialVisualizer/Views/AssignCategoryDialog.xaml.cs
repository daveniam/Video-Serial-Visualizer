// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.Views;

public partial class AssignCategoryDialog : Window
{
    private readonly List<CheckBox> _optionBoxes = new();

    public IReadOnlyList<int> ResultCategoryIds { get; private set; } = Array.Empty<int>();

    public AssignCategoryDialog(IReadOnlyList<Category> categories, IReadOnlyCollection<int> currentCategoryIds)
    {
        InitializeComponent();

        if (categories.Count == 0)
        {
            // Sin categorias creadas todavia: se avisa en vez de dejar la lista vacia y confusa.
            OptionsPanel.Children.Add(new TextBlock
            {
                Text = Loc.I["Category_NoneYet"],
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            });
            return;
        }

        foreach (var category in categories)
            AddOption(category.Name, category.Id, currentCategoryIds.Contains(category.Id));
    }

    private void AddOption(string text, int id, bool isChecked)
    {
        var box = new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            Tag = id,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 14
        };
        _optionBoxes.Add(box);
        OptionsPanel.Children.Add(box);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultCategoryIds = _optionBoxes
            .Where(b => b.IsChecked == true)
            .Select(b => (int)b.Tag)
            .ToList();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>Abre el dialogo para elegir las categorias (varias) del grupo. Devuelve el conjunto
    /// elegido; si se cancela, devuelve el actual sin cambios.</summary>
    public static (bool Confirmed, IReadOnlyList<int> CategoryIds) PromptAssign(
        IReadOnlyList<Category> categories, IReadOnlyCollection<int> currentCategoryIds, Window? owner)
    {
        var dialog = new AssignCategoryDialog(categories, currentCategoryIds) { Owner = owner };
        var confirmed = dialog.ShowDialog() == true;
        return (confirmed, confirmed ? dialog.ResultCategoryIds : currentCategoryIds.ToList());
    }
}
