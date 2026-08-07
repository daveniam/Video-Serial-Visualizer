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
    public DbSet<FolderTag> FolderTags => Set<FolderTag>();
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

        // "Foreign Keys=True" activa PRAGMA foreign_keys en CADA conexion. Es imprescindible: sin
        // esto, SQLite no aplica las cascadas ON DELETE, asi que borrar un video dejaba su progreso
        // y sus etiquetas huerfanos. Esas filas huerfanas son las que despues rompen un guardado con
        // "FOREIGN KEY constraint failed". Con la cascada activa, borrar un video se lleva lo suyo.
        optionsBuilder.UseSqlite($"Data Source={DatabasePath};Foreign Keys=True");
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

        // Enlace grupo<->categoria (muchos a muchos). Una carpeta no puede tener dos veces la misma
        // categoria (indice unico); borrar una categoria se lleva sus enlaces (cascade).
        modelBuilder.Entity<FolderTag>()
            .HasIndex(t => new { t.FolderPath, t.CategoryId })
            .IsUnique();

        modelBuilder.Entity<FolderTag>()
            .HasIndex(t => t.CategoryId);

        modelBuilder.Entity<FolderTag>()
            .HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

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
                    CategoryId INTEGER NULL,
                    CoverImagePath TEXT NULL
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

            if (!categoryColumns.Contains("CoverImagePath"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE FolderCategories ADD COLUMN CoverImagePath TEXT NULL");
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

        var folderTagsTableExists = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='FolderTags'";
            var result = await cmd.ExecuteScalarAsync();
            folderTagsTableExists = result is not null;
        }

        if (!folderTagsTableExists)
        {
            // Enlace muchos-a-muchos grupo<->categoria. FK a Categories con cascade a mano (estas
            // sentencias no pasan por OnModelCreating).
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE FolderTags (
                    Id INTEGER NOT NULL CONSTRAINT PK_FolderTags PRIMARY KEY AUTOINCREMENT,
                    FolderPath TEXT NOT NULL,
                    CategoryId INTEGER NOT NULL,
                    CONSTRAINT FK_FolderTags_Categories_CategoryId FOREIGN KEY (CategoryId) REFERENCES Categories (Id) ON DELETE CASCADE
                )");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IX_FolderTags_FolderPath_CategoryId ON FolderTags (FolderPath, CategoryId)");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_FolderTags_CategoryId ON FolderTags (CategoryId)");

            // Migracion UNICA (solo al crear la tabla): traslada la categoria unica que tenia cada
            // grupo en la columna vieja FolderCategories.CategoryId a un enlace en FolderTags. Correr
            // esto en cada arranque re-agregaria una categoria que el usuario haya quitado, por eso
            // va aca dentro y no afuera.
            await db.Database.ExecuteSqlRawAsync(@"
                INSERT INTO FolderTags (FolderPath, CategoryId)
                SELECT FolderPath, CategoryId FROM FolderCategories
                WHERE CategoryId IS NOT NULL AND CategoryId IN (SELECT Id FROM Categories)");
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

        // Auto-reparacion: borra filas hijas huerfanas (progreso y etiquetas cuyo video ya no existe).
        // Pueden haber quedado de una version anterior a la que se le activara la cascada de FK, en la
        // que borrar un video no se llevaba su progreso/etiquetas. Esas huerfanas son las que rompian
        // un guardado posterior con "FOREIGN KEY constraint failed"; sin padre no le sirven a nadie.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Progress WHERE VideoId NOT IN (SELECT Id FROM Videos)");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Markers WHERE VideoId NOT IN (SELECT Id FROM Videos)");
    }
}
