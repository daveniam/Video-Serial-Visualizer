// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.ViewModels;

namespace VideoSerialVisualizer.Views;

public partial class FoldersView : UserControl
{
    public FoldersView()
    {
        InitializeComponent();
    }

    private FoldersViewModel? Vm => DataContext as FoldersViewModel;

    // --- Arrastrar un grupo sobre un curso ---

    private void Group_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => GroupCardDrag.OnPreviewMouseLeftButtonDown(sender, e);

    private void Group_PreviewMouseMove(object sender, MouseEventArgs e)
        => GroupCardDrag.OnPreviewMouseMove(sender, e);

    private static CourseCardViewModel? CourseOf(object sender) => (sender as FrameworkElement)?.DataContext as CourseCardViewModel;

    private void Course_DragEnter(object sender, DragEventArgs e) => UpdateCourseDrag(sender, e);

    private void Course_DragOver(object sender, DragEventArgs e) => UpdateCourseDrag(sender, e);

    private static void UpdateCourseDrag(object sender, DragEventArgs e)
    {
        var accepts = GroupCardDrag.GetDragged(e) is not null && CourseOf(sender) is not null;
        e.Effects = accepts ? DragDropEffects.Move : DragDropEffects.None;
        if (CourseOf(sender) is { } course)
            course.IsDropTarget = accepts;
        e.Handled = true;
    }

    private void Course_DragLeave(object sender, DragEventArgs e)
    {
        if (CourseOf(sender) is { } course)
            course.IsDropTarget = false;
    }

    private async void Course_Drop(object sender, DragEventArgs e)
    {
        var course = CourseOf(sender);
        if (course is not null)
            course.IsDropTarget = false;

        if (course is null || GroupCardDrag.GetDragged(e) is not { } group || Vm is null)
            return;

        e.Handled = true;
        await Vm.MoveGroupToCourseAsync(group, course);
    }

    // --- Menu del grupo: "Mover a curso" con los cursos existentes ---

    private void Group_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu, DataContext: FolderCardViewModel group } || Vm is null)
            return;

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Tag as string)
            {
                case "CourseMenu":
                    FillCourseMenu(item, group);
                    break;
                case "RemoveFromCourse":
                    item.Visibility = group.CourseId is null ? Visibility.Collapsed : Visibility.Visible;
                    break;
            }
        }
    }

    private void FillCourseMenu(MenuItem parent, FolderCardViewModel group)
    {
        parent.Items.Clear();
        var itemStyle = (Style)FindResource("DarkMenuItemStyle");

        var courses = Vm!.Courses.Where(c => c.Id != group.CourseId).ToList();
        if (courses.Count == 0)
        {
            parent.Items.Add(new MenuItem { Header = Loc.I["Course_NoCourses"], IsEnabled = false, Style = itemStyle });
            return;
        }

        foreach (var course in courses)
        {
            var item = new MenuItem { Header = course.Name, Style = itemStyle };
            item.Click += async (_, _) => await Vm.MoveGroupToCourseAsync(group, course);
            parent.Items.Add(item);
        }
    }
}
