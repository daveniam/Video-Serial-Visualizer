// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoSerialVisualizer.ViewModels;

public partial class CategoryTabViewModel : ObservableObject
{
    public int Id { get; }
    public string Name { get; }

    [ObservableProperty]
    private bool isSelected;

    public CategoryTabViewModel(int id, string name)
    {
        Id = id;
        Name = name;
    }
}
