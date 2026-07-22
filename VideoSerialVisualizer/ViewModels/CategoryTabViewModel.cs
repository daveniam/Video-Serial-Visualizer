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
