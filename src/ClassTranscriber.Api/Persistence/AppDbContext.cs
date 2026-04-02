using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Transcript> Transcripts => Set<Transcript>();
    public DbSet<GlobalSettings> GlobalSettings => Set<GlobalSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Folder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
            entity.Property(e => e.IconKey)
                .HasMaxLength(40)
                .HasDefaultValue(FolderAppearance.DefaultIconKey)
                .IsRequired();
            entity.Property(e => e.ColorHex)
                .HasMaxLength(7)
                .HasDefaultValue(FolderAppearance.DefaultColorHex)
                .IsRequired();
            entity.HasMany(e => e.Projects).WithOne(p => p.Folder).HasForeignKey(p => p.FolderId);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(300).IsRequired();
            entity.Property(e => e.OriginalFileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.StoredFileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.FileExtension).HasMaxLength(20);
            entity.Property(e => e.MediaPath).HasMaxLength(1000);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.MediaType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);

            entity.OwnsOne(e => e.Settings, settings =>
            {
                settings.Property(s => s.Engine).HasMaxLength(50);
                settings.Property(s => s.Model).HasMaxLength(50);
                settings.Property(s => s.LanguageMode).HasMaxLength(20);
                settings.Property(s => s.LanguageCode).HasMaxLength(20);
            });

            entity.HasOne(e => e.Transcript).WithOne(t => t.Project).HasForeignKey<Transcript>(t => t.ProjectId);
        });

        modelBuilder.Entity<Transcript>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DetectedLanguage).HasMaxLength(20);
        });

        modelBuilder.Entity<GlobalSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DefaultEngine).HasMaxLength(50);
            entity.Property(e => e.DefaultModel).HasMaxLength(50);
            entity.Property(e => e.DefaultLanguageMode).HasMaxLength(20);
            entity.Property(e => e.DefaultLanguageCode).HasMaxLength(20);
            entity.Property(e => e.DefaultTranscriptViewMode).HasMaxLength(20);

            entity.HasData(new GlobalSettings());
        });
    }
}
