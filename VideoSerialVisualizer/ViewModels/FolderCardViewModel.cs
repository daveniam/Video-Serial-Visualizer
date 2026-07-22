using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoSerialVisualizer.Helpers;

namespace VideoSerialVisualizer.ViewModels;

public partial class FolderCardViewModel : ObservableObject
{
    public string FolderPath { get; }
    public string FolderName { get; }
    public int VideoCount { get; }
    public string? ThumbnailPath { get; }

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool favorito;

    [ObservableProperty]
    private int? categoryId;

    [ObservableProperty]
    private ImageSource? thumbnailImage;

    public FolderCardViewModel(string folderPath, int videoCount, string? thumbnailPath, string? customDisplayName, bool favorito, int? categoryId)
    {
        FolderPath = folderPath;

        FolderName = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(FolderName))
            FolderName = folderPath;

        VideoCount = videoCount;
        ThumbnailPath = thumbnailPath;
        displayName = string.IsNullOrWhiteSpace(customDisplayName) ? FolderName : customDisplayName;
        this.favorito = favorito;
        this.categoryId = categoryId;
    }

    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(ThumbnailPath))
            return;

        ThumbnailImage = await ThumbnailLoader.LoadCachedAsync(ThumbnailPath);
    }
}
