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
    private readonly Action<int> _openCourse;
    private List<FolderCardViewModel> _allFolderCards = new();
    private List<CourseCardViewModel> _allCourseCards = new();

    /// <summary>
    /// Lo que muestra la grilla de Explorar: primero las tarjetas de curso
    /// (<see cref="CourseCardViewModel"/>) y despues los grupos sueltos
    /// (<see cref="FolderCardViewModel"/>). Cada tipo tiene su propia plantilla en la vista.
    /// </summary>
    public ObservableCollection<object> ExploreItems { get; } = new();

    /// <summary>Todos los cursos (para el menu "Mover a curso" de un grupo).</summary>
    public IReadOnlyList<CourseCardViewModel> Courses => _allCourseCards;

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

    public FoldersViewModel(FolderScannerService scannerService, Action<string> openFolder, Action<int> openCourse)
    {
        _scannerService = scannerService;
        _openFolder = openFolder;
        _openCourse = openCourse;
    }

    // --- Cursos ---

    [RelayCommand]
    private async Task CreateCourseAsync()
    {
        var name = RenameCategoryDialog.PromptForName(string.Empty, Application.Current.MainWindow, Loc.I["Course_NewTitle"]);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await using (var db = new AppDbContext())
        {
            db.Courses.Add(new Course { Name = name.Trim(), FechaCreado = DateTime.Now });
            await db.SaveChangesAsync();
        }

        // El curso nuevo tiene que verse aunque haya un filtro puesto: se vuelve a "Todas".
        SearchText = string.Empty;
        OpenAll();
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenCourse(CourseCardViewModel? course)
    {
        if (course is not null)
            _openCourse(course.Id);
    }

    [RelayCommand]
    private async Task RenameCourseAsync(CourseCardViewModel? course)
    {
        if (course is null)
            return;

        var newName = RenameCategoryDialog.PromptForName(course.Name, Application.Current.MainWindow, Loc.I["Course_RenameTitle"]);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        await using var db = new AppDbContext();
        var entry = await db.Courses.FirstOrDefaultAsync(c => c.Id == course.Id);
        if (entry is null)
            return;

        entry.Name = newName.Trim();
        await db.SaveChangesAsync();
        course.Name = entry.Name;
    }

    [RelayCommand]
    private async Task DeleteCourseAsync(CourseCardViewModel? course)
    {
        if (course is null)
            return;

        var confirm = MessageBox.Show(
            string.Format(Loc.I["Course_Delete_Message"], course.Name),
            Loc.I["Course_Delete_Title"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        await using (var db = new AppDbContext())
        {
            // La FK ya deja CourseId en NULL, pero se hace explicito por si la base vieja no la tiene.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE FolderCategories SET CourseId = NULL, CourseOrder = 0 WHERE CourseId = {course.Id}");
            var entry = await db.Courses.FirstOrDefaultAsync(c => c.Id == course.Id);
            if (entry is not null)
            {
                db.Courses.Remove(entry);
                await db.SaveChangesAsync();
            }
        }

        await RefreshAsync();
    }

    /// <summary>Mete un grupo en un curso (arrastrando la tarjeta encima, o desde el menu del grupo).
    /// Si estaba en otro curso, se muda.</summary>
    public async Task MoveGroupToCourseAsync(FolderCardViewModel group, CourseCardViewModel course)
    {
        if (group.CourseId == course.Id)
            return;

        await GroupCatalog.AddGroupsToCourseAsync(course.Id, new[] { group.FolderPath });
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveGroupFromCourseAsync(FolderCardViewModel? group)
    {
        if (group?.CourseId is null)
            return;

        await using (var db = new AppDbContext())
        {
            var entry = await db.FolderCategories.FirstOrDefaultAsync(f => f.FolderPath == group.FolderPath);
            if (entry is null)
                return;
            entry.CourseId = null;
            entry.CourseOrder = 0;
            await db.SaveChangesAsync();
        }

        await RefreshAsync();
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

        var (confirmed, selectedIds) = AssignCategoryDialog.PromptAssign(Categories.ToList(), card.CategoryIds, Application.Current.MainWindow);
        if (!confirmed)
            return;

        await using var db = new AppDbContext();

        // Se reemplaza el conjunto entero: se borran los enlaces actuales del grupo y se agregan los
        // elegidos. Simple y sin sorpresas frente a un merge incremental.
        var existing = await db.FolderTags.Where(t => t.FolderPath == card.FolderPath).ToListAsync();
        db.FolderTags.RemoveRange(existing);

        foreach (var id in selectedIds.Distinct())
            db.FolderTags.Add(new FolderTag { FolderPath = card.FolderPath, CategoryId = id });

        await db.SaveChangesAsync();

        card.CategoryIds = selectedIds.ToHashSet();

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

    private static Task<FolderCategory> GetOrCreateCategoryAsync(AppDbContext db, string folderPath)
        => GroupCatalog.GetOrCreateFolderEntryAsync(db, folderPath);

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

        _allFolderCards = await GroupCatalog.LoadGroupCardsAsync(db);
        var allCategories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var courses = await db.Courses.AsNoTracking().OrderBy(c => c.FechaCreado).ThenBy(c => c.Id).ToListAsync();

        // Tarjeta de cada curso: totales sumados de sus grupos; portada elegida, o la del primer grupo.
        var groupsByCourse = _allFolderCards
            .Where(g => g.CourseId is not null)
            .GroupBy(g => g.CourseId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CourseOrder).ToList());

        _allCourseCards = courses.Select(c =>
        {
            var groups = groupsByCourse.GetValueOrDefault(c.Id) ?? new List<FolderCardViewModel>();
            return new CourseCardViewModel(
                c.Id, c.Name, groups.Count,
                groups.Sum(g => g.VideoCount), groups.Sum(g => g.WatchedCount), groups.Sum(g => g.TotalDurationMs),
                GroupCatalog.EffectiveCover(c.CoverImagePath, groups.FirstOrDefault(g => g.ThumbnailPath is not null)?.ThumbnailPath));
        }).ToList();

        Categories.Clear();
        foreach (var category in allCategories)
            Categories.Add(category);

        CategoryTabs.Clear();
        foreach (var category in allCategories)
            CategoryTabs.Add(new CategoryTabViewModel(category.Id, category.Name));

        if (SelectedCategoryId is not null && allCategories.All(c => c.Id != SelectedCategoryId))
            SelectedCategoryId = null;

        UpdateTabSelection();

        OnPropertyChanged(nameof(Courses));
        ApplyFilter();

        foreach (var course in _allCourseCards)
            _ = course.LoadThumbnailAsync();
        foreach (var card in _allFolderCards)
            _ = card.LoadThumbnailAsync();
    }

    /// <summary>
    /// Sin filtros ("Todas" y sin busqueda) se ven los cursos y los grupos SUELTOS: los grupos de un
    /// curso se ven entrando al curso. Con un filtro puesto (favoritos, categoria o busqueda) se buscan
    /// en TODOS los grupos, esten o no en un curso, para que el filtro siga encontrando todo; los
    /// cursos aparecen solo si su nombre coincide con la busqueda.
    /// </summary>
    private void ApplyFilter()
    {
        ExploreItems.Clear();

        var hasSearch = !string.IsNullOrWhiteSpace(SearchText);
        var isFiltered = ShowFavoritesOnly || SelectedCategoryId is not null || hasSearch;

        if (!ShowFavoritesOnly && SelectedCategoryId is null)
        {
            foreach (var course in _allCourseCards)
            {
                if (!hasSearch || course.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    ExploreItems.Add(course);
            }
        }

        IEnumerable<FolderCardViewModel> filtered = isFiltered
            ? _allFolderCards
            : _allFolderCards.Where(f => f.CourseId is null);

        if (ShowFavoritesOnly)
            filtered = filtered.Where(f => f.Favorito);

        if (SelectedCategoryId is int categoryId)
            filtered = filtered.Where(f => f.CategoryIds.Contains(categoryId));

        if (hasSearch)
            filtered = filtered.Where(f => f.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var card in filtered)
            ExploreItems.Add(card);
    }
}
