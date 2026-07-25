// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Data;
using System.IO;
using Microsoft.EntityFrameworkCore;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.Data;

public class AppDbContext : DbContext
{
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<WatchProgress> Progress => Set<WatchProgress>();
    public DbSet<FolderCategory> FolderCategories => Set<FolderCategory>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<VideoMarker> Markers => Set<VideoMarker>();

    /// <summary>
    /// IMPORTANTE: se mantiene el nombre historico "TutorialHub" a proposito, aunque el proyecto
    /// pasara a llamarse Video Serial Visualizer. Las rutas de las miniaturas se guardan ABSOLUTAS
    /// dentro de la base (ver ThumbnailService), asi que renombrar esta carpeta dejaria a todas las
    /// miniaturas existentes apuntando a un lugar inexistente. Es una carpeta interna en AppData
    /// que el usuario no ve, asi que no hay nada que ganar cambiandola.
    /// </summary>
    public static string DatabaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TutorialHub");

    public static string DatabasePath { get; } = Path.Combine(DatabaseDirectory, "tutorialhub.db");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        Directory.CreateDirectory(DatabaseDirectory);
        optionsBuilder.UseSqlite($"Data Source={DatabasePath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Video>()
            .HasIndex(v => v.RutaAbsoluta)
            .IsUnique();

        // Se filtra y agrupa por carpeta en casi toda la app (Explorar y la biblioteca).
        modelBuilder.Entity<Video>()
            .HasIndex(v => v.CarpetaOrigen);

        modelBuilder.Entity<WatchProgress>()
            .HasIndex(p => p.VideoId)
            .IsUnique();

        modelBuilder.Entity<WatchProgress>()
            .HasOne(p => p.Video)
            .WithOne(v => v.Progress)
            .HasForeignKey<WatchProgress>(p => p.VideoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FolderCategory>()
            .HasIndex(f => f.FolderPath)
            .IsUnique();

        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Name)
            .IsUnique();

        modelBuilder.Entity<FolderCategory>()
            .HasOne(f => f.Category)
            .WithMany()
            .HasForeignKey(f => f.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Un video puede tener muchas etiquetas; se borran solas si se borra el video (no tiene
        // sentido dejar etiquetas huerfanas apuntando a un VideoId que ya no existe).
        modelBuilder.Entity<VideoMarker>()
            .HasIndex(m => m.VideoId);

        modelBuilder.Entity<VideoMarker>()
            .HasOne(m => m.Video)
            .WithMany()
            .HasForeignKey(m => m.VideoId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// Crea la base si no existe y agrega columnas/tablas nuevas a bases ya existentes
    /// (no usamos EF Migrations, asi que EnsureCreated no altera un esquema previo).
    /// </summary>
    public static async Task EnsureSchemaUpToDateAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Videos)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existingColumns.Add(reader.GetString(1));
        }

        if (!existingColumns.Contains("ThumbnailPath"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Videos ADD COLUMN ThumbnailPath TEXT NULL");

        if (!existingColumns.Contains("Favorito"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Videos ADD COLUMN Favorito INTEGER NOT NULL DEFAULT 0");

        // Indice para filtrar/agrupar por carpeta. IF NOT EXISTS lo hace idempotente, asi que
        // tambien cubre las bases ya creadas antes de agregarlo al modelo.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_Videos_CarpetaOrigen ON Videos (CarpetaOrigen)");

        var tableExists = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='FolderCategories'";
            var result = await cmd.ExecuteScalarAsync();
            tableExists = result is not null;
        }

        if (!tableExists)
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE FolderCategories (
                    Id INTEGER NOT NULL CONSTRAINT PK_FolderCategories PRIMARY KEY AUTOINCREMENT,
                    FolderPath TEXT NOT NULL,
                    DisplayName TEXT NULL,
                    Favorito INTEGER NOT NULL DEFAULT 0,
                    CategoryId INTEGER NULL
                )");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IX_FolderCategories_FolderPath ON FolderCategories (FolderPath)");
        }
        else
        {
            var categoryColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(FolderCategories)";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    categoryColumns.Add(reader.GetString(1));
            }

            if (!categoryColumns.Contains("Favorito"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE FolderCategories ADD COLUMN Favorito INTEGER NOT NULL DEFAULT 0");

            if (!categoryColumns.Contains("CategoryId"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE FolderCategories ADD COLUMN CategoryId INTEGER NULL");
        }

        var categoriesTableExists = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Categories'";
            var result = await cmd.ExecuteScalarAsync();
            categoriesTableExists = result is not null;
        }

        if (!categoriesTableExists)
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE Categories (
                    Id INTEGER NOT NULL CONSTRAINT PK_Categories PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                )");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IX_Categories_Name ON Categories (Name)");
        }

        var markersTableExists = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Markers'";
            var result = await cmd.ExecuteScalarAsync();
            markersTableExists = result is not null;
        }

        if (!markersTableExists)
        {
            // ON DELETE CASCADE a mano: EnsureCreated/estas sentencias manuales no pasan por las
            // relaciones configuradas en OnModelCreating, asi que hay que declarar la FK explicita.
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE Markers (
                    Id INTEGER NOT NULL CONSTRAINT PK_Markers PRIMARY KEY AUTOINCREMENT,
                    VideoId INTEGER NOT NULL,
                    TimeMs INTEGER NOT NULL,
                    Texto TEXT NOT NULL,
                    CONSTRAINT FK_Markers_Videos_VideoId FOREIGN KEY (VideoId) REFERENCES Videos (Id) ON DELETE CASCADE
                )");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_Markers_VideoId ON Markers (VideoId)");
        }
    }
}
