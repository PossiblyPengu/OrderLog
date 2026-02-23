using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Services;

/// <summary>
/// SQLite-backed repository for orders persistence.
/// Uses singleton pattern to prevent multiple connections from conflicting.
/// WAL mode enables concurrent access from multiple processes (main app + widget).
/// </summary>
public sealed class OrderLogRepository : IOrderLogService
{
    private static readonly object _lock = new();
    private static OrderLogRepository? _instance;
    private static int _refCount;

    private readonly string _connectionString;
    private readonly ILogger<OrderLogRepository>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: true) }
    };

    /// <summary>
    /// Gets or creates the singleton instance of the repository.
    /// </summary>
    public static OrderLogRepository GetInstance(ILogger<OrderLogRepository>? logger = null)
    {
        lock (_lock)
        {
            if (_instance == null || _instance._disposed)
            {
                _instance = new OrderLogRepository(logger);
            }
            _refCount++;
            return _instance;
        }
    }

    private OrderLogRepository(ILogger<OrderLogRepository>? logger = null)
    {
        _logger = logger;

        try
        {
            Directory.CreateDirectory(Core.AppPaths.OrderLogDir);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Core.AppPaths.OrderLogDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            InitializeDatabase();

            _logger?.LogInformation("OrderLogRepository (SQLite) initialized at {Path}", Core.AppPaths.OrderLogDbPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize OrderLogRepository");
            throw;
        }
    }

    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();

        // Enable WAL mode for multi-process support (main app + widget)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }

        // Create orders table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Orders (
                    Id TEXT PRIMARY KEY,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    Data TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Orders_SortOrder ON Orders(SortOrder);
            ";
            cmd.ExecuteNonQuery();
        }
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    public async Task<List<OrderItem>> LoadAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Data FROM Orders ORDER BY SortOrder ASC";

            var items = new List<OrderItem>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var json = reader.GetString(0);
                var item = JsonSerializer.Deserialize<OrderItem>(json, JsonOptions);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            _logger?.LogInformation("Loaded {Count} orders", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load orders");
            return [];
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveAsync(List<OrderItem> items)
    {
        await _semaphore.WaitAsync();
        try
        {
            // Do not persist practically-empty placeholder orders. Filter them out here
            // so they are never saved to disk.
            var toSave = items?.Where(i => !i.IsPracticallyEmpty).ToList() ?? [];

            using var connection = CreateConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Get all current IDs from the filtered set
                var currentIds = toSave.Select(i => i.Id).ToHashSet();

                // Delete items that are no longer in the list
                using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "SELECT Id FROM Orders";

                    var idsToDelete = new List<Guid>();
                    using (var reader = deleteCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var id = Guid.Parse(reader.GetString(0));
                            if (!currentIds.Contains(id))
                            {
                                idsToDelete.Add(id);
                            }
                        }
                    }

                    foreach (var id in idsToDelete)
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText = "DELETE FROM Orders WHERE Id = @Id";
                        cmd.Parameters.AddWithValue("@Id", id.ToString());
                        cmd.ExecuteNonQuery();
                    }

                    if (idsToDelete.Count > 0)
                    {
                        _logger?.LogDebug("Deleted {Count} obsolete records", idsToDelete.Count);
                    }
                }

                // Upsert all items
                foreach (var item in toSave)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO Orders (Id, SortOrder, Data)
                        VALUES (@Id, @SortOrder, @Data)
                        ON CONFLICT(Id) DO UPDATE SET
                            SortOrder = excluded.SortOrder,
                            Data = excluded.Data
                    ";
                    cmd.Parameters.AddWithValue("@Id", item.Id.ToString());
                    cmd.Parameters.AddWithValue("@SortOrder", item.Order);
                    cmd.Parameters.AddWithValue("@Data", JsonSerializer.Serialize(item, JsonOptions));
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                _logger?.LogInformation("Saved {Count} orders", toSave.Count);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save orders");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _refCount--;
            if (_refCount > 0) return;

            if (_disposed) return;
            _disposed = true;
            _semaphore.Dispose();
            _instance = null;
            _logger?.LogInformation("OrderLogRepository disposed");
        }
    }
}
