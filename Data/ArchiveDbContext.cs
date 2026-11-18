using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using DocuArchivePro.Models;

namespace DocuArchivePro.Data
{
    public class ArchiveDbContext : DbContext
    {
        public DbSet<Document> Documents { get; set; } = null!;
        public DbSet<DocumentAttachment> Attachments { get; set; } = null!;
        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<CorrespondentEntity> CorrespondentEntities { get; set; } = null!;
        public DbSet<AppUser> Users { get; set; } = null!;
        public DbSet<SystemSettings> SystemSettings { get; set; } = null!;
        public DbSet<AuditLogEntry> AuditLogs { get; set; } = null!;

        public static string DatabasePath
        {
            get
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocuArchivePro");
                if (!Directory.Exists(appData))
                {
                    Directory.CreateDirectory(appData);
                }
                return Path.Combine(appData, "DocuArchive.db");
            }
        }

        public ArchiveDbContext()
        {
        }

        public ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite($"Data Source={DatabasePath}");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Document indexes for high performance
            modelBuilder.Entity<Document>()
                .HasIndex(d => d.RegistrationNumber)
                .IsUnique();

            modelBuilder.Entity<Document>()
                .HasIndex(d => d.Type);

            modelBuilder.Entity<Document>()
                .HasIndex(d => d.RegistrationDate);

            modelBuilder.Entity<Document>()
                .HasIndex(d => d.Subject);

            modelBuilder.Entity<Document>()
                .HasIndex(d => d.Sender);

            modelBuilder.Entity<Document>()
                .HasIndex(d => d.Recipient);

            // Composite and specialized indexes for high-speed queries on large archives
            modelBuilder.Entity<Document>()
                .HasIndex(d => new { d.IsDeleted, d.Type, d.RegistrationDate });

            modelBuilder.Entity<Document>()
                .HasIndex(d => d.FollowUpDate);

            modelBuilder.Entity<Document>()
                .HasIndex(d => d.RelatedRegistrationNumber);

            modelBuilder.Entity<AuditLogEntry>()
                .HasIndex(a => a.Timestamp);

            modelBuilder.Entity<Document>()
                .HasMany(d => d.Attachments)
                .WithOne(a => a.Document)
                .HasForeignKey(a => a.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
