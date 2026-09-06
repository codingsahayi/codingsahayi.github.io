using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace CodingSahayi.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Project> Projects { get; set; } = null!;
        public DbSet<Conversation> Conversations { get; set; } = null!;
        public DbSet<ChatMessageEntity> ChatMessages { get; set; } = null!;
        public DbSet<ProjectKnowledge> ProjectKnowledgeBase { get; set; } = null!;
        public DbSet<CodeChunk> CodeChunks { get; set; } = null!;
        public DbSet<ApiRequestLog> ApiRequestLogs { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var dbPath = Path.Join(path, "CodingSahayi", "coding_sahayi.db");
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }
}