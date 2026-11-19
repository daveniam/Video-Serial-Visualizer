// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using VideoSerialVisualizer.Data;
using VideoSerialVisualizer.Localization;
using VideoSerialVisualizer.Models;
using VideoSerialVisualizer.Services;
using VideoSerialVisualizer.Views;

namespace VideoSerialVisualizer.ViewModels;

/// <summary>
/// Vista de un curso: sus grupos en orden (se reordenan arrastrando), el progreso total, "Continuar
/// curso" e "Importar carpetas" (un grupo por subcarpeta).
/// </summary>
public partial class CourseViewModel : ObservableObject
{
    private readonly FolderScannerService _scannerService;
    private readonly Action<string> _openGroup;
    private readonly Action<Video, IReadOnlyList<Video>> _playVideos;
    private readonly Action _goBack;

    public int CourseId { get; private set; }

    public ObservableCollection<FolderCardViewModel> Groups { get; } = new();

    [ObservableProperty]
    private string courseName = string.Empty;

    [ObservableProperty]
    private int watchedCount;

    [ObservableProperty]
    private int totalVideoCount;

    [ObservableProperty]
    private double watchedPercent;

    [ObservableProperty]
    private bool allWatched;

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private double scanProgressPercent;

    public bool HasGroups => Groups.Count > 0;

    public string WatchedSummaryText => string.Format(Loc.I["Explore_WatchedProgress"], WatchedCount, TotalVideoCount);

    /// <summary>"Empezar curso" si no se vio nada todavia; "Continuar curso" si ya se empezo.</summary>
    public string ContinueButtonText => WatchedCount == 0 ? Loc.I["Course_Start"] : Loc.I["Course_Continue"];

    public CourseViewModel(FolderScannerService scannerService, Action<string> openGroup,
        Action<Video, IReadOnlyList<Video>> playVideos, Action goBack)
    {
        _scannerService = scannerService;
        _openGroup = openGroup;
        _playVideos = playVideos;
        _goBack = goBack;
    }

    public async Task LoadAsync(int courseId)
    {
        CourseId = courseId;
        StatusMessage = string.Empty;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        await using var db = new AppDbContext();

        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CourseId);
        if (course is null)
        {
            _goBack(); // borrado mientras tanto
            return;
        }

        CourseName = course.Name;

        var groups = (await GroupCatalog.LoadGroupCardsAsync(db))
            .Where(g => g.CourseId == CourseId)
            .OrderBy(g => g.CourseOrder)
            .ToList();

        Groups.Clear();
        foreach (var group in groups)
        {
            group.Position = Groups.Count + 1;
            Groups.Add(group);
        }

        TotalVideoCount = groups.Sum(g => g.VideoCount);
        WatchedCount = groups.Sum(g => g.WatchedCount);
        WatchedPercent = TotalVideoCount > 0 ? WatchedCount / (double)TotalVideoCount * 100.0 : 0;
        AllWatched = TotalVideoCount > 0 && WatchedCount == TotalVideoCount;
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(WatchedSummaryText));
        OnPropertyChanged(nameof(ContinueButtonText));
        ContinueCourseCommand.NotifyCanExecuteChanged();

        foreach (var group in groups)
            _ = group.LoadThumbnailAsync();
    }

    [RelayCommand]
    private void Back() => _goBack();

    [RelayCommand]
    private void OpenGroup(FolderCardViewModel? group)
    {
        if (group is not null)
            _openGroup(group.FolderPath);
    }

    private bool CanContinueCourse() => TotalVideoCount > 0;

    /// <summary>
    /// Abre el primer video sin terminar del curso (grupo por grupo, en el orden del curso) y
    /// reproduce con TODO el curso como lista: al terminar un grupo sigue con el siguiente. Si ya se
    /// vio todo, arranca desde el principio.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanContinueCourse))]
    private async Task ContinueCourseAsync()
    {
        var playlist = await GroupCatalog.GetCoursePlaylistAsync(CourseId);
        if (playlist.Count == 0)
            return;

        var next = playlist.FirstOrDefault(v => v.Progress?.Completado != true) ?? playlist[0];
        _playVideos(next, playlist);
    }

    /// <summary>
    /// Importa una o varias carpetas al curso. Cada subcarpeta con videos pasa a ser un grupo (ver
    /// <see cref="FolderScannerService.ImportAsSubfolderGroupsAsync"/>), agregado al final del curso.
    /// </summary>
    [RelayCommand]
    private async Task ImportFoldersAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.I["Course_ImportPick"],
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true || dialog.FolderNames.Length == 0)
            return;

        IsScanning = true;
        StatusMessage = Loc.I["Scan_Searching"];
        ScanProgressPercent = 0;

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                StatusMessage = string.Format(Loc.I["Scan_Processing"], p.Current, p.Total, p.FileName);
                ScanProgressPercent = p.Total > 0 ? p.Current / (double)p.Total * 100.0 : 0;
            });

            var groupPaths = new List<string>();
            foreach (var folder in dialog.FolderNames.OrderBy(f => f, Helpers.NaturalStringComparer.Instance))
                groupPaths.AddRange(await _scannerService.ImportAsSubfolderGroupsAsync(folder, progress));

            // Solo las carpetas que terminaron con videos son grupos de verdad.
            await using (var db = new AppDbContext())
            {
                var withVideos = (await db.Videos.AsNoTracking()
                        .Where(v => groupPaths.Contains(v.CarpetaOrigen))
                        .Select(v => v.CarpetaOrigen)
                        .Distinct()
                        .ToListAsync())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                groupPaths = groupPaths.Where(withVideos.Contains).ToList();
            }

            await GroupCatalog.AddGroupsToCourseAsync(CourseId, groupPaths);

            StatusMessage = string.Format(Loc.I["Course_Imported"], groupPaths.Count);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Empty;
            MessageBox.Show(string.Format(Loc.I["Course_ImportError"], ex.Message), Loc.I["Course_ImportTitle"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsScanning = false;
            ScanProgressPercent = 0;
        }
    }

    [RelayCommand]
    private async Task RenameCourseAsync()
    {
        var newName = RenameCategoryDialog.PromptForName(CourseName, Application.Current.MainWindow, Loc.I["Course_RenameTitle"]);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        await using var db = new AppDbContext();
        var entry = await db.Courses.FirstOrDefaultAsync(c => c.Id == CourseId);
        if (entry is null)
            return;

        entry.Name = newName.Trim();
        await db.SaveChangesAsync();
        CourseName = entry.Name;
    }

    [RelayCommand]
    private async Task RenameGroupAsync(FolderCardViewModel? group)
    {
        if (group is null)
            return;

        var newName = RenameCategoryDialog.PromptForName(group.DisplayName, Application.Current.MainWindow);
        if (newName is null)
            return;

        await using var db = new AppDbContext();
        var entry = await GroupCatalog.GetOrCreateFolderEntryAsync(db, group.FolderPath);
        entry.DisplayName = newName;
        await db.SaveChangesAsync();

        group.DisplayName = string.IsNullOrWhiteSpace(newName) ? group.FolderName : newName;
    }

    /// <summary>La portada del grupo pasa a ser la del curso.</summary>
    [RelayCommand]
    private async Task UseGroupCoverForCourseAsync(FolderCardViewModel? group)
    {
        if (group?.ThumbnailPath is null)
            return;

        await using var db = new AppDbContext();
        var entry = await db.Courses.FirstOrDefaultAsync(c => c.Id == CourseId);
        if (entry is null)
            return;

        entry.CoverImagePath = group.ThumbnailPath;
        await db.SaveChangesAsync();
        StatusMessage = Loc.I["Course_CoverSet"];
    }

    /// <summary>Saca el grupo del curso: vuelve a quedar suelto en Explorar (no se borra nada).</summary>
    [RelayCommand]
    private async Task RemoveGroupFromCourseAsync(FolderCardViewModel? group)
    {
        if (group is null)
            return;

        await using (var db = new AppDbContext())
        {
            var entry = await db.FolderCategories.FirstOrDefaultAsync(f => f.FolderPath == group.FolderPath);
            if (entry is not null)
            {
                entry.CourseId = null;
                entry.CourseOrder = 0;
                await db.SaveChangesAsync();
            }
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Reordena arrastrando: <paramref name="dragged"/> pasa a ocupar el lugar de
    /// <paramref name="target"/> (los demas se corren). Se guarda el orden completo.
    /// </summary>
    public async Task MoveGroupAsync(FolderCardViewModel dragged, FolderCardViewModel target)
    {
        var from = Groups.IndexOf(dragged);
        var to = Groups.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
            return;

        Groups.Move(from, to);
        await SaveOrderAsync();
    }

    private async Task SaveOrderAsync()
    {
        await using var db = new AppDbContext();
        var paths = Groups.Select(g => g.FolderPath).ToList();
        var entries = await db.FolderCategories.Where(f => paths.Contains(f.FolderPath)).ToListAsync();

        for (var i = 0; i < Groups.Count; i++)
        {
            Groups[i].CourseOrder = i;
            Groups[i].Position = i + 1;
            var entry = entries.FirstOrDefault(e => e.FolderPath == Groups[i].FolderPath);
            if (entry is not null)
                entry.CourseOrder = i;
        }

        await db.SaveChangesAsync();
    }
}
