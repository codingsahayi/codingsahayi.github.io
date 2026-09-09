using System;
using System.IO;
using Microsoft.Data.Sqlite;
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
            // Resolve the database path from user configuration (defaults to AppData).
            var dbPath = SettingsManager.DatabasePath;
            if (string.IsNullOrWhiteSpace(dbPath))
                dbPath = SettingsManager.DefaultDatabasePath;

            // Ensure the parent directory exists before opening a connection.
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Cache=Shared + Default Timeout reduce SQLITE_BUSY under concurrent access
            // (agent loop writes logs/knowledge while the UI thread reads).
            var connectionString = $"Data Source={dbPath};Cache=Shared;Default Timeout=5";

            var connection = new SqliteConnection(connectionString);

            // Enable Write-Ahead Logging the first time the connection opens so that
            // concurrent readers are not blocked while a write transaction is in progress.
            connection.StateChange += (sender, e) =>
            {
                if (e.CurrentState == System.Data.ConnectionState.Open)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "PRAGMA journal_mode=WAL;";
                    cmd.ExecuteNonQuery();
                }
            };

            optionsBuilder.UseSqlite(connection);
        }

        /// <summary>
        /// Ensures the database directory exists and the schema/tables are created at the
        /// configured <see cref="SettingsManager.DatabasePath" />. WAL mode is enabled by
        /// the connection open handler. Call this after changing the database path so the
        /// new location is materialised immediately.
        /// </summary>
        public static void InitializeDatabase()
        {
            var dbPath = SettingsManager.DatabasePath;
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using var db = new AppDbContext();
            db.Database.EnsureCreated();
        }
    }
}