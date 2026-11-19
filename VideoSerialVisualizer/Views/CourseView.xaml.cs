// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.ViewModels;

namespace VideoSerialVisualizer.Views;

/// <summary>Vista de un curso. El code-behind solo maneja el arrastre para reordenar los grupos.</summary>
public partial class CourseView : UserControl
{
    public CourseView()
    {
        InitializeComponent();
    }

    private CourseViewModel? Vm => DataContext as CourseViewModel;

    private void Group_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => GroupCardDrag.OnPreviewMouseLeftButtonDown(sender, e);

    private void Group_PreviewMouseMove(object sender, MouseEventArgs e)
        => GroupCardDrag.OnPreviewMouseMove(sender, e);

    private static FolderCardViewModel? TargetOf(object sender) => (sender as FrameworkElement)?.DataContext as FolderCardViewModel;

    private void Group_DragEnter(object sender, DragEventArgs e) => UpdateDrag(sender, e);

    private void Group_DragOver(object sender, DragEventArgs e) => UpdateDrag(sender, e);

    private static void UpdateDrag(object sender, DragEventArgs e)
    {
        var dragged = GroupCardDrag.GetDragged(e);
        var target = TargetOf(sender);
        var accepts = dragged is not null && target is not null && !ReferenceEquals(dragged, target);
        e.Effects = accepts ? DragDropEffects.Move : DragDropEffects.None;
        if (target is not null)
            target.IsDropTarget = accepts;
        e.Handled = true;
    }

    private void Group_DragLeave(object sender, DragEventArgs e)
    {
        if (TargetOf(sender) is { } target)
            target.IsDropTarget = false;
    }

    private async void Group_Drop(object sender, DragEventArgs e)
    {
        var target = TargetOf(sender);
        if (target is not null)
            target.IsDropTarget = false;

        if (target is null || GroupCardDrag.GetDragged(e) is not { } dragged || Vm is null)
            return;

        e.Handled = true;
        await Vm.MoveGroupAsync(dragged, target);
    }
}
