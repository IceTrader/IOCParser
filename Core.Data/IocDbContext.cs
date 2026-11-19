using Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Core.Data;

public class IocDbContext : DbContext
{
    public DbSet<IocDocument> Documents { get; set; }
    public DbSet<IocRecord> Iocs { get; set; }
    public DbSet<DocumentIoc> DocumentIocs { get; set; }

    public DbSet<IocWhitelist> Whitelist { get; set; }

    public IocDbContext(DbContextOptions<IocDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Уникальный индекс на комбинацию Type + Value
        modelBuilder.Entity<IocRecord>()
            .HasIndex(i => new { i.Type, i.Value })
            .IsUnique();

        // 2. Уникальный индекс на хэш файла документа (чтобы не парсить дубликаты)
        modelBuilder.Entity<IocDocument>()
            .HasIndex(d => d.FileHash)
            .IsUnique();

        // 3. Настройка связи Many-to-Many через DocumentIoc
        modelBuilder.Entity<DocumentIoc>()
            .HasKey(di => new { di.DocumentId, di.IocId });

        modelBuilder.Entity<DocumentIoc>()
            .HasOne(di => di.Document)
            .WithMany(d => d.DocumentIocs)
            .HasForeignKey(di => di.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentIoc>()
            .HasOne(di => di.Ioc)
            .WithMany(i => i.DocumentIocs)
            .HasForeignKey(di => di.IocId)
            .OnDelete(DeleteBehavior.Cascade);

        // 4. Индекс для быстрого поиска IOC по значению
        modelBuilder.Entity<IocRecord>()
            .HasIndex(i => i.Value);

        modelBuilder.Entity<IocWhitelist>()
            .HasIndex(w => new { w.Type, w.Value })
            .IsUnique();
    }
}