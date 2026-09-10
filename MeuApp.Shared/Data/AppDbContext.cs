using Microsoft.EntityFrameworkCore;
using MeuApp.Shared.Models;

namespace MeuApp.Shared.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Loja> Lojas => Set<Loja>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<RegistroValidade> RegistrosValidade => Set<RegistroValidade>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Loja>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nome).IsRequired().HasMaxLength(150);
            entity.Property(e => e.CodigoLoja).IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<Produto>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CodigoBarras).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Nome).IsRequired().HasMaxLength(250);

            // Código de barras único no catálogo global
            entity.HasIndex(e => e.CodigoBarras).IsUnique();
        });

        modelBuilder.Entity<RegistroValidade>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.MotivoBaixa).HasMaxLength(150);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DataValidade);
            entity.HasIndex(e => e.DataBaixa);
            entity.HasIndex(e => e.LojaId);

            entity.HasOne(e => e.Produto)
                  .WithMany(p => p.Validades)
                  .HasForeignKey(e => e.ProdutoId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Loja)
                  .WithMany(l => l.Validades)
                  .HasForeignKey(e => e.LojaId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nome).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(180);
            entity.Property(e => e.SenhaHash).IsRequired();
            entity.Property(e => e.SenhaSalt).IsRequired();

            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.TokenConfirmacao);
            entity.HasIndex(e => e.CodigoConfirmacao);
        });
    }
}
