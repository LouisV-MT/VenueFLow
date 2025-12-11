using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace VenueFlow.Data.Models;

public partial class VenueFlowDbContext : DbContext
{
    public VenueFlowDbContext()
    {
    }

    public VenueFlowDbContext(DbContextOptions<VenueFlowDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Guest> Guests { get; set; }

    public virtual DbSet<MenuOption> MenuOptions { get; set; }

    public virtual DbSet<SeatingPreference> SeatingPreferences { get; set; }

    public virtual DbSet<Table> Tables { get; set; }

    public virtual DbSet<Wedding> Weddings { get; set; }

  
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
  
            string dbUser = Environment.GetEnvironmentVariable("VENUEFLOW_USER") ?? "venueadmin";
            string dbPass = Environment.GetEnvironmentVariable("VENUEFLOW_PASS") ?? "";

          
            string connectionString = $"Server=tcp:venueflow-server.database.windows.net,1433;Initial Catalog=VenueFlowDB;Persist Security Info=False;User ID={dbUser};Password={dbPass};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

         
            optionsBuilder.UseSqlServer(connectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Guest>(entity =>
        {
            entity.HasKey(e => e.GuestId).HasName("PK__Guests__0C423C32D0E3530D");

            entity.Property(e => e.GuestId).HasColumnName("GuestID");
            entity.Property(e => e.Allergies).HasMaxLength(255);
            entity.Property(e => e.DietaryRestrictions).HasMaxLength(255);
            entity.Property(e => e.FamilyGroup).HasMaxLength(50);
            entity.Property(e => e.GuestName).HasMaxLength(100);
            entity.Property(e => e.MenuOptionId).HasColumnName("MenuOptionID");
            entity.Property(e => e.TableId).HasColumnName("TableID");
            entity.Property(e => e.WeddingId).HasColumnName("WeddingID");

            entity.HasOne(d => d.MenuOption).WithMany(p => p.Guests)
                .HasForeignKey(d => d.MenuOptionId)
                .HasConstraintName("FK_Guests_MenuOptions");

            entity.HasOne(d => d.Table).WithMany(p => p.Guests)
                .HasForeignKey(d => d.TableId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Guests_Tables");

            entity.HasOne(d => d.Wedding).WithMany(p => p.Guests)
                .HasForeignKey(d => d.WeddingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Guests_Weddings");
        });

        modelBuilder.Entity<MenuOption>(entity =>
        {
            entity.HasKey(e => e.MenuOptionId).HasName("PK__MenuOpti__C9B774038D4C2FD6");

            entity.Property(e => e.MenuOptionId).HasColumnName("MenuOptionID");
            entity.Property(e => e.AlergyInfo).HasMaxLength(255);
            entity.Property(e => e.Category).HasMaxLength(50);
            entity.Property(e => e.OptionName).HasMaxLength(100);
        });

        modelBuilder.Entity<SeatingPreference>(entity =>
        {
            entity.HasKey(e => e.PreferenceId).HasName("PK__SeatingP__E228490FCEFE166D");

            entity.Property(e => e.PreferenceId).HasColumnName("PreferenceID");
            entity.Property(e => e.GuestIdSource).HasColumnName("GuestID_Source");
            entity.Property(e => e.GuestIdTarget).HasColumnName("GuestID_Target");
            entity.Property(e => e.IsMustSitWith).HasColumnName("IsMustSitWith");

            entity.HasOne(d => d.GuestIdSourceNavigation).WithMany(p => p.SeatingPreferenceGuestIdSourceNavigations)
                .HasForeignKey(d => d.GuestIdSource)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Preference_Source");

            entity.HasOne(d => d.GuestIdTargetNavigation).WithMany(p => p.SeatingPreferenceGuestIdTargetNavigations)
                .HasForeignKey(d => d.GuestIdTarget)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Preference_Target");
        });

        modelBuilder.Entity<Table>(entity =>
        {
            entity.HasKey(e => e.TableId).HasName("PK__Tables__7D5F018E55694998");

            entity.Property(e => e.TableId).HasColumnName("TableID");
            entity.Property(e => e.WeddingId).HasColumnName("WeddingID");

            entity.HasOne(d => d.Wedding).WithMany(p => p.Tables)
                .HasForeignKey(d => d.WeddingId)
                .HasConstraintName("FK_Tables_Weddings");
        });

        modelBuilder.Entity<Wedding>(entity =>
        {
            entity.HasKey(e => e.WeddingId).HasName("PK__Weddings__68028BD3ED6FFF6A");

            entity.Property(e => e.WeddingId).HasColumnName("WeddingID");
            entity.Property(e => e.Name).HasMaxLength(100);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}