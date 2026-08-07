// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Localization;
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
            Title = Loc.I["Scan_PickFolder"]
        };

        if (dialog.ShowDialog() != true)
            return;

        IsScanning = true;
        StatusMessage = Loc.I["Scan_Searching"];
        ScanProgressPercent = 0;

        try
        {
            var added = await _scannerService.ScanFolderAsync(dialog.FolderName,
                new Progress<ScanProgress>(p =>
                {
                    StatusMessage = string.Format(Loc.I["Scan_Processing"], p.Current, p.Total, p.FileName);
                    ScanProgressPercent = p.Total > 0 ? p.Current / (double)p.Total * 100.0 : 0;
                }));

            StatusMessage = added.Count > 0
                ? string.Format(Loc.I["Scan_Added"], added.Count)
                : Loc.I["Scan_NoNewVideos"];

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
        var name = RenameCategoryDialog.PromptForName(string.Empty, Application.Current.MainWindow, Loc.I["Category_NewTitle"]);
        if (string.IsNullOrWhiteSpace(name))
            return;

        name = name.Trim();

        if (Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(string.Format(Loc.I["Category_Duplicate_Message"], name), Loc.I["Category_Duplicate_Title"],
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

        var newName = RenameCategoryDialog.PromptForName(category.Name, Application.Current.MainWindow, Loc.I["Category_RenameTitle"]);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        newName = newName.Trim();

        if (Categories.Any(c => c.Id != category.Id && string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(string.Format(Loc.I["Category_Duplicate_Message"], newName), Loc.I["Category_Duplicate_Title"],
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
            string.Format(Loc.I["Category_Delete_Message"], category.Name),
            Loc.I["Category_Delete_Title"],
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
            string.Format(Loc.I["Group_Delete_Message"], card.DisplayName, card.VideoCount),
            Loc.I["Group_Delete_Title"],
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

        // El conteo y la duracion total por carpeta se resuelven con GROUP BY en SQL: una fila por
        // grupo (con COUNT y SUM), en vez de traer la tabla Videos entera a memoria.
        var folderCounts = await db.Videos.AsNoTracking()
            .GroupBy(v => v.CarpetaOrigen)
            .Select(g => new { FolderPath = g.Key, Count = g.Count(), TotalMs = g.Sum(v => v.DuracionMs) })
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

                // Portada elegida a mano (clic derecho en la barra del reproductor) tiene prioridad
                // sobre la miniatura del ultimo video. Si el archivo ya no esta, se cae al default.
                var coverPath = category?.CoverImagePath;
                var effectiveThumbnail = !string.IsNullOrEmpty(coverPath) && File.Exists(coverPath)
                    ? coverPath
                    : thumbnailPath;

                return new FolderCardViewModel(
                    group.FolderPath, group.Count, group.TotalMs, effectiveThumbnail,
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
