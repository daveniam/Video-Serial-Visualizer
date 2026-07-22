using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Models;
using VideoSerialVisualizer.Services;
using VideoSerialVisualizer.Views;

namespace VideoSerialVisualizer.ViewModels;

public partial class FoldersViewModel : ObservableObject
{
    private readonly FolderScannerService _scannerService;
    private readonly Action<string> _openFolder;
    private List<FolderCardViewModel> _allFolderCards = new();

    public ObservableCollection<FolderCardViewModel> FolderCards { get; } = new();

    /// <summary>Categorias personalizadas creadas desde Configuración (para el filtro y el menu de asignar).</summary>
    public ObservableCollection<Category> Categories { get; } = new();

    /// <summary>Tabs de categoria para la fila de filtros de Explorar (Todas, Favoritos y cada categoria creada).</summary>
    public ObservableCollection<CategoryTabViewModel> CategoryTabs { get; } = new();

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool showFavoritesOnly;

    [ObservableProperty]
    private int? selectedCategoryId;

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private double scanProgressPercent;

    public FoldersViewModel(FolderScannerService scannerService, Action<string> openFolder)
    {
        _scannerService = scannerService;
        _openFolder = openFolder;
    }

    public async Task InitializeAsync() => await RefreshAsync();

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta de tutoriales"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsScanning = true;
        StatusMessage = "Buscando videos...";
        ScanProgressPercent = 0;

        try
        {
            var added = await _scannerService.ScanFolderAsync(dialog.FolderName,
                new Progress<ScanProgress>(p =>
                {
                    StatusMessage = $"Procesando {p.Current} de {p.Total}: {p.FileName}";
                    ScanProgressPercent = p.Total > 0 ? p.Current / (double)p.Total * 100.0 : 0;
                }));

            StatusMessage = added.Count > 0
                ? $"Se agregaron {added.Count} video(s)."
                : "No se encontraron videos nuevos.";

            await RefreshAsync();
        }
        finally
        {
            IsScanning = false;
            ScanProgressPercent = 0;
        }
    }

    /// <summary>Activo cuando el filtro "Todas" esta seleccionado (ni favoritos ni una categoria puntual).</summary>
    public bool IsAllActive => !ShowFavoritesOnly && SelectedCategoryId is null;

    [RelayCommand]
    private void OpenAll()
    {
        ShowFavoritesOnly = false;
        SelectedCategoryId = null;
        UpdateTabSelection();
    }

    [RelayCommand]
    private void ShowFavorites()
    {
        ShowFavoritesOnly = true;
        SelectedCategoryId = null;
        UpdateTabSelection();
    }

    [RelayCommand]
    private void SelectCategoryTab(CategoryTabViewModel? tab)
    {
        if (tab is null)
            return;

        ShowFavoritesOnly = false;
        SelectedCategoryId = tab.Id;
        UpdateTabSelection();
    }

    [RelayCommand]
    private void OpenFolder(FolderCardViewModel? card)
    {
        if (card is not null)
            _openFolder(card.FolderPath);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow { DataContext = this, Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    [RelayCommand]
    private async Task ToggleFolderFavoriteAsync(FolderCardViewModel? card)
    {
        if (card is null)
            return;

        card.Favorito = !card.Favorito;

        await using var db = new AppDbContext();
        var entry = await GetOrCreateCategoryAsync(db, card.FolderPath);
        entry.Favorito = card.Favorito;
        await db.SaveChangesAsync();

        if (ShowFavoritesOnly)
            ApplyFilter();
    }

    [RelayCommand]
    private async Task RenameFolderAsync(FolderCardViewModel? card)
    {
        if (card is null)
            return;

        var newName = RenameCategoryDialog.PromptForName(card.DisplayName, Application.Current.MainWindow);
        if (newName is null)
            return;

        await using var db = new AppDbContext();
        var entry = await GetOrCreateCategoryAsync(db, card.FolderPath);
        entry.DisplayName = newName;
        await db.SaveChangesAsync();

        card.DisplayName = string.IsNullOrWhiteSpace(newName) ? card.FolderName : newName;
    }

    [RelayCommand]
    private async Task AssignCategoryAsync(FolderCardViewModel? card)
    {
        if (card is null)
            return;

        var (confirmed, categoryId) = AssignCategoryDialog.PromptAssign(Categories.ToList(), card.CategoryId, Application.Current.MainWindow);
        if (!confirmed)
            return;

        await using var db = new AppDbContext();
        var entry = await GetOrCreateCategoryAsync(db, card.FolderPath);
        entry.CategoryId = categoryId;
        await db.SaveChangesAsync();

        card.CategoryId = categoryId;

        if (SelectedCategoryId is not null)
            ApplyFilter();
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        var name = RenameCategoryDialog.PromptForName(string.Empty, Application.Current.MainWindow, "Nueva categoría");
        if (string.IsNullOrWhiteSpace(name))
            return;

        name = name.Trim();

        if (Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Ya existe una categoría llamada \"{name}\".", "Categoría duplicada",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await using var db = new AppDbContext();
        db.Categories.Add(new Category { Name = name });
        await db.SaveChangesAsync();

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RenameCategoryAsync(Category? category)
    {
        if (category is null)
            return;

        var newName = RenameCategoryDialog.PromptForName(category.Name, Application.Current.MainWindow, "Renombrar categoría");
        if (string.IsNullOrWhiteSpace(newName))
            return;

        newName = newName.Trim();

        if (Categories.Any(c => c.Id != category.Id && string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Ya existe una categoría llamada \"{newName}\".", "Categoría duplicada",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await using var db = new AppDbContext();
        var entry = await db.Categories.FirstOrDefaultAsync(c => c.Id == category.Id);
        if (entry is null)
            return;

        entry.Name = newName;
        await db.SaveChangesAsync();

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync(Category? category)
    {
        if (category is null)
            return;

        var confirm = MessageBox.Show(
            $"¿Eliminar la categoría \"{category.Name}\"?\n\n" +
            "Los grupos que la tengan asignada quedan sin categoría; esto no borra ningún grupo ni archivo.",
            "Eliminar categoría",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        await using var db = new AppDbContext();

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE FolderCategories SET CategoryId = NULL WHERE CategoryId = {category.Id}");

        var entry = await db.Categories.FirstOrDefaultAsync(c => c.Id == category.Id);
        if (entry is not null)
        {
            db.Categories.Remove(entry);
            await db.SaveChangesAsync();
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(FolderCardViewModel? card)
    {
        if (card is null)
            return;

        var confirm = MessageBox.Show(
            $"¿Eliminar el grupo \"{card.DisplayName}\" con {card.VideoCount} video(s)?\n\n" +
            "Esto NO borra los archivos de tu computadora, solo los quita de Video Serial Visualizer.",
            "Eliminar grupo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        await using var db = new AppDbContext();

        var videos = await db.Videos.Where(v => v.CarpetaOrigen == card.FolderPath).ToListAsync();
        var thumbnailPaths = videos
            .Where(v => !string.IsNullOrEmpty(v.ThumbnailPath))
            .Select(v => v.ThumbnailPath!)
            .ToList();

        db.Videos.RemoveRange(videos);

        var category = await db.FolderCategories.FirstOrDefaultAsync(f => f.FolderPath == card.FolderPath);
        if (category is not null)
            db.FolderCategories.Remove(category);

        await db.SaveChangesAsync();

        foreach (var path in thumbnailPaths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // best effort
            }
        }

        await RefreshAsync();
    }

    private static async Task<FolderCategory> GetOrCreateCategoryAsync(AppDbContext db, string folderPath)
    {
        var entry = await db.FolderCategories.FirstOrDefaultAsync(f => f.FolderPath == folderPath);
        if (entry is not null)
            return entry;

        entry = new FolderCategory { FolderPath = folderPath };
        db.FolderCategories.Add(entry);
        return entry;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowFavoritesOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAllActive));
        ApplyFilter();
    }

    partial void OnSelectedCategoryIdChanged(int? value)
    {
        OnPropertyChanged(nameof(IsAllActive));
        ApplyFilter();
    }

    private void UpdateTabSelection()
    {
        foreach (var tab in CategoryTabs)
            tab.IsSelected = tab.Id == SelectedCategoryId;
    }

    public async Task RefreshAsync()
    {
        await using var db = new AppDbContext();

        // El conteo por carpeta se resuelve con GROUP BY en SQL: devuelve una fila por grupo en vez
        // de traer la tabla Videos entera a memoria solo para contarla.
        var folderCounts = await db.Videos.AsNoTracking()
            .GroupBy(v => v.CarpetaOrigen)
            .Select(g => new { FolderPath = g.Key, Count = g.Count() })
            .ToListAsync();

        // Para la caratula solo hacen falta los videos que TIENEN miniatura, y solo tres columnas
        // (no la entidad completa): la eleccion del "ultimo" usa orden natural, que es logica C#.
        var thumbnailCandidates = await db.Videos.AsNoTracking()
            .Where(v => v.ThumbnailPath != null && v.ThumbnailPath != "")
            .Select(v => new { v.CarpetaOrigen, v.NombreArchivo, v.ThumbnailPath })
            .ToListAsync();

        var thumbnailsByFolder = thumbnailCandidates
            .GroupBy(v => v.CarpetaOrigen)
            .ToDictionary(
                g => g.Key,
                // La caratula usa el ULTIMO video de la carpeta (orden natural): suele ser
                // el resultado final del proceso que ensena el tutorial.
                g => g.OrderBy(v => v.NombreArchivo, NaturalStringComparer.Instance).Last().ThumbnailPath);

        var categories = await db.FolderCategories.AsNoTracking().ToDictionaryAsync(c => c.FolderPath);
        var allCategories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

        Categories.Clear();
        foreach (var category in allCategories)
            Categories.Add(category);

        CategoryTabs.Clear();
        foreach (var category in allCategories)
            CategoryTabs.Add(new CategoryTabViewModel(category.Id, category.Name));

        if (SelectedCategoryId is not null && allCategories.All(c => c.Id != SelectedCategoryId))
            SelectedCategoryId = null;

        UpdateTabSelection();

        _allFolderCards = folderCounts
            .OrderBy(g => g.FolderPath, NaturalStringComparer.Instance)
            .Select(group =>
            {
                categories.TryGetValue(group.FolderPath, out var category);
                thumbnailsByFolder.TryGetValue(group.FolderPath, out var thumbnailPath);

                return new FolderCardViewModel(
                    group.FolderPath, group.Count, thumbnailPath,
                    category?.DisplayName, category?.Favorito ?? false, category?.CategoryId);
            })
            .ToList();

        ApplyFilter();

        foreach (var card in _allFolderCards)
            _ = card.LoadThumbnailAsync();
    }

    private void ApplyFilter()
    {
        FolderCards.Clear();

        IEnumerable<FolderCardViewModel> filtered = _allFolderCards;

        if (ShowFavoritesOnly)
            filtered = filtered.Where(f => f.Favorito);

        if (SelectedCategoryId is int categoryId)
            filtered = filtered.Where(f => f.CategoryId == categoryId);

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(f => f.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var card in filtered)
            FolderCards.Add(card);
    }
}
