using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OrderLog.Data.Entities;

namespace OrderLog.Data;

/// <summary>
/// Shared SQLite context for dictionary data (items and stores).
/// Uses a separate database from the main app data for clean separation.
/// Thread-safe for concurrent access using WAL mode.
/// </summary>
public sealed class DictionaryDbContext : IDisposable
{
    private static readonly Lazy<DictionaryDbContext> _instance = new(() => new DictionaryDbContext());
    private static readonly object _lock = new();
    private readonly string _connectionString;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Singleton instance for shared dictionary access
    /// </summary>
    public static DictionaryDbContext Instance => _instance.Value;

    /// <summary>
    /// Path to the shared dictionary database
    /// </summary>
    public static string DatabasePath => Core.AppPaths.DictionaryDbPath;

    private DictionaryDbContext()
    {
        Directory.CreateDirectory(Core.AppPaths.SharedDir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();

        // Enable WAL mode for multi-process support
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }

        // Create items table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Items (
                    Number TEXT PRIMARY KEY,
                    Description TEXT NOT NULL DEFAULT '',
                    Skus TEXT NOT NULL DEFAULT '[]',
                    IsEssential INTEGER NOT NULL DEFAULT 0,
                    IsPrivateLabel INTEGER NOT NULL DEFAULT 0,
                    Tags TEXT NOT NULL DEFAULT '[]'
                );
                CREATE INDEX IF NOT EXISTS IX_Items_Description ON Items(Description);
            ";
            cmd.ExecuteNonQuery();
        }

        // Create stores table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Stores (
                    Code TEXT PRIMARY KEY,
                    Name TEXT NOT NULL DEFAULT '',
                    Rank TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS IX_Stores_Name ON Stores(Name);
                CREATE INDEX IF NOT EXISTS IX_Stores_Rank ON Stores(Rank);
            ";
            cmd.ExecuteNonQuery();
        }

        // Create vendors table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Vendors (
                    Name TEXT PRIMARY KEY,
                    DisplayName TEXT NOT NULL DEFAULT '',
                    Code TEXT NOT NULL DEFAULT '',
                    UseCount INTEGER NOT NULL DEFAULT 0,
                    ColorHex TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS IX_Vendors_UseCount ON Vendors(UseCount DESC);
                CREATE INDEX IF NOT EXISTS IX_Vendors_DisplayName ON Vendors(DisplayName);
            ";
            cmd.ExecuteNonQuery();
        }

        // Seed vendors if table is empty
        SeedVendorsIfEmpty(connection);

        Serilog.Log.Debug("DictionaryDbContext SQLite initialized at {Path}", DatabasePath);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    private static void SeedVendorsIfEmpty(SqliteConnection connection)
    {
        // Check if vendors table already has data
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Vendors";
        var count = Convert.ToInt32(countCmd.ExecuteScalar());
        if (count > 0) return;

        // Initial vendor seed data
        var vendors = new[]
        {
            "A & R NATURELLES INC.", "ADVANTAGE PACKAGING LIMITED", "ALLURE LINGERIE", "ANB CANADA INC", "ANEROS",
            "B. CUMMING COMPANY", "BBL LLC", "BLUSH", "BUSH NOVELTIES", "BMS CDN", "BOB HEADQUARTERS INC", "BODYZONE",
            "BOOBY TAPE", "BUSHMAN PRODUCTS", "B-VIBE", "CAL EXOTICS", "CAL EXOTICS PL", "CANADIAN BATH BOMB CO PL",
            "CARRASHIELD LABS", "CHANNEL 1 RELEASING", "CHATEAU MARIS ELECTRONIQUE US PL", "CLANDESTINE DEVICES",
            "CLASSIC BRANDS", "COBBLESTONE PACKAGING", "COIN TRADING", "COQUETTE", "COQUETTE INT", "COQUETTE INT PL",
            "COUSINS GROUP", "CRAVE", "CREATIVE CONCEPTIONS", "D.N.B.", "DIABOLIC", "DISTROCAN INC.", "DMC VISIONS INC. PL",
            "DOC JOHNSON", "DOC JOHNSON PL", "EARTHLY BODY PL", "EAST COAST NEWS", "EAU ZONE", "EIS INC.", "ELECTRIC EEL INC",
            "EMPIRE LABORATORIES", "EP PRODUCTS", "EVOLVED NOVELTIES", "FANTASY LINGERIE", "FLAG MATRIX",
            "FLESHLIGHT CANADA DISTRIBUTION", "FULL CIRCLE DISTRIBUTION", "FUN FACTORY USA", "GEORGE'S FUN FACTORY",
            "GLOBAL PROTECTION CORP.", "GREEN BABY PL", "HONEY PLAY BOX", "HOT OCTOPUSS", "HOTT PRODUCTS",
            "IAC - ALL TRADES SWEETS PL", "JAL ENTERPRISES", "JOR WEAR SAS", "KAMA SUTRA", "KAYTEL VIDEO", "KHEPER GAMES",
            "KHEPER GAMES PL", "KIIROO B.V.", "LELO", "LIBERATOR", "LITTLE GENIE PRODUCTIONS", "LOVELY PLANET", "LXB WHOLESALE",
            "MALE EDGE", "MALEBASICS CORP", "MAYER LABS CANADA", "MD SCIENCE LAB", "MILE HIGH", "MJM NOVELTIES INC",
            "MY WORLD PL", "N/A - Internal", "NADGERZ INC", "NALPAC", "NEW EARTH TRADING LLC",
            "NEW WAY INTERNATIONAL RESOURCE CO. LIMITED PL", "NEXUS", "NON-FRICTION PRODUCTS INC PL", "NS NOVELTIES",
            "NURU PLAY INC", "ODILE TOYS INC.", "Omnibod", "OXBALLS", "OZZE CREATIONS", "P.H.S. INTERNATIONAL",
            "PAMCO DISTRIBUTION", "PD PRODUCTS LLC", "PDX BRANDS", "PLEASER USA", "PRODIGALSON VENTURES", "PUFF IMPORTS INC",
            "PUMP FASHIONS INC", "QUIVER", "RB HEALTH INC", "ROCK CANDY TOYS", "ROUGE GARMENTS LTD US", "RUBIES SALES",
            "SECWELL", "SEXY LIVING", "SHIBARI", "SHOTS AMERICA LLC", "SINALITE PL", "SLEAZY GREETINGS", "SPORTSHEETS",
            "SPORTSHEETS PL", "STOCKROOM WHOLESALE", "SVAKOM DESIGN", "TANTUS INC.", "THE AD SHOP", "THE AD SHOP PL",
            "TOPCO SALES", "TRIGG LABS", "TW TRADE", "UBERLUBE", "UM PRODUCTS LTD", "VALENCIA NATURALS LLC",
            "VALENCIA NATURALS LLC PL", "VASH DESIGNS LLC", "VENWEL LOGISTICS INC.", "VERY INTELLIGENT ECOMMERCE INC",
            "VIBRATEX", "WEALTHPRIMUS PL", "WICKED PICTURES.COM", "WICKED SENSUAL CARE", "WICKED SENSUAL CARE PL",
            "WOOD ROCKET LLC", "WOW Tech Canada Ltd.", "XGEN LLC", "XR BRANDS", "ZERO TOLERANCE", "ZUICE FOR MEN"
        };

        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var vendor in vendors)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT OR IGNORE INTO Vendors (Name, DisplayName, Code, UseCount, ColorHex) VALUES (@Name, @DisplayName, '', 0, '')";
                cmd.Parameters.AddWithValue("@Name", vendor.ToUpperInvariant());
                cmd.Parameters.AddWithValue("@DisplayName", vendor);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
            Serilog.Log.Information("Seeded {Count} vendors into database", vendors.Length);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    #region Items Operations

    /// <summary>
    /// Get all dictionary items
    /// </summary>
    public List<DictionaryItemEntity> GetAllItems()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Number, Description, Skus, IsEssential, IsPrivateLabel, Tags FROM Items";

            List<DictionaryItemEntity> items = new();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new DictionaryItemEntity
                {
                    Number = reader.GetString(0),
                    Description = reader.GetString(1),
                    Skus = JsonSerializer.Deserialize<List<string>>(reader.GetString(2), JsonOptions) ?? new(),
                    IsEssential = reader.GetInt32(3) == 1,
                    IsPrivateLabel = reader.GetInt32(4) == 1,
                    Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(5), JsonOptions) ?? new()
                });
            }
            return items;
        }
    }

    /// <summary>
    /// Get a dictionary item by number
    /// </summary>
    public DictionaryItemEntity? GetItem(string number)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Number, Description, Skus, IsEssential, IsPrivateLabel, Tags FROM Items WHERE Number = @Number";
            cmd.Parameters.AddWithValue("@Number", number);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new DictionaryItemEntity
                {
                    Number = reader.GetString(0),
                    Description = reader.GetString(1),
                    Skus = JsonSerializer.Deserialize<List<string>>(reader.GetString(2), JsonOptions) ?? new(),
                    IsEssential = reader.GetInt32(3) == 1,
                    IsPrivateLabel = reader.GetInt32(4) == 1,
                    Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(5), JsonOptions) ?? new()
                };
            }
            return null;
        }
    }

    /// <summary>
    /// Find items matching a predicate (loads all and filters in memory)
    /// </summary>
    public List<DictionaryItemEntity> FindItems(Func<DictionaryItemEntity, bool> predicate, int? maxResults = null)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Number, Description, Skus, IsEssential, IsPrivateLabel, Tags FROM Items";

            List<DictionaryItemEntity> results = new();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var item = new DictionaryItemEntity
                {
                    Number = reader.GetString(0),
                    Description = reader.GetString(1),
                    Skus = JsonSerializer.Deserialize<List<string>>(reader.GetString(2), JsonOptions) ?? new(),
                    IsEssential = reader.GetInt32(3) == 1,
                    IsPrivateLabel = reader.GetInt32(4) == 1,
                    Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(5), JsonOptions) ?? new()
                };

                if (predicate(item))
                {
                    results.Add(item);
                    if (maxResults.HasValue && results.Count >= maxResults.Value)
                        break;
                }
            }
            return results;
        }
    }

    /// <summary>
    /// Get the count of items
    /// </summary>
    public int GetItemCount()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Items";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    /// <summary>
    /// Insert or update a dictionary item
    /// </summary>
    public void UpsertItem(DictionaryItemEntity item)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Items (Number, Description, Skus, IsEssential, IsPrivateLabel, Tags)
                VALUES (@Number, @Description, @Skus, @IsEssential, @IsPrivateLabel, @Tags)
                ON CONFLICT(Number) DO UPDATE SET
                    Description = excluded.Description,
                    Skus = excluded.Skus,
                    IsEssential = excluded.IsEssential,
                    IsPrivateLabel = excluded.IsPrivateLabel,
                    Tags = excluded.Tags
            ";
            cmd.Parameters.AddWithValue("@Number", item.Number);
            cmd.Parameters.AddWithValue("@Description", item.Description);
            cmd.Parameters.AddWithValue("@Skus", JsonSerializer.Serialize(item.Skus, JsonOptions));
            cmd.Parameters.AddWithValue("@IsEssential", item.IsEssential ? 1 : 0);
            cmd.Parameters.AddWithValue("@IsPrivateLabel", item.IsPrivateLabel ? 1 : 0);
            cmd.Parameters.AddWithValue("@Tags", JsonSerializer.Serialize(item.Tags, JsonOptions));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Insert or update multiple dictionary items in a transaction
    /// </summary>
    public void UpsertItems(IEnumerable<DictionaryItemEntity> items)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var item in items)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO Items (Number, Description, Skus, IsEssential, IsPrivateLabel, Tags)
                        VALUES (@Number, @Description, @Skus, @IsEssential, @IsPrivateLabel, @Tags)
                        ON CONFLICT(Number) DO UPDATE SET
                            Description = excluded.Description,
                            Skus = excluded.Skus,
                            IsEssential = excluded.IsEssential,
                            IsPrivateLabel = excluded.IsPrivateLabel,
                            Tags = excluded.Tags
                    ";
                    cmd.Parameters.AddWithValue("@Number", item.Number);
                    cmd.Parameters.AddWithValue("@Description", item.Description);
                    cmd.Parameters.AddWithValue("@Skus", JsonSerializer.Serialize(item.Skus, JsonOptions));
                    cmd.Parameters.AddWithValue("@IsEssential", item.IsEssential ? 1 : 0);
                    cmd.Parameters.AddWithValue("@IsPrivateLabel", item.IsPrivateLabel ? 1 : 0);
                    cmd.Parameters.AddWithValue("@Tags", JsonSerializer.Serialize(item.Tags, JsonOptions));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    /// <summary>
    /// Delete a dictionary item
    /// </summary>
    public bool DeleteItem(string number)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Items WHERE Number = @Number";
            cmd.Parameters.AddWithValue("@Number", number);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>
    /// Delete all dictionary items
    /// </summary>
    public int DeleteAllItems()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Items";
            return cmd.ExecuteNonQuery();
        }
    }

    #endregion

    #region Stores Operations

    /// <summary>
    /// Get all stores
    /// </summary>
    public List<StoreEntity> GetAllStores()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Code, Name, Rank FROM Stores";

            List<StoreEntity> stores = new();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                stores.Add(new StoreEntity
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1),
                    Rank = reader.GetString(2)
                });
            }
            return stores;
        }
    }

    /// <summary>
    /// Get a store by code
    /// </summary>
    public StoreEntity? GetStore(string code)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Code, Name, Rank FROM Stores WHERE Code = @Code";
            cmd.Parameters.AddWithValue("@Code", code);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new StoreEntity
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1),
                    Rank = reader.GetString(2)
                };
            }
            return null;
        }
    }

    /// <summary>
    /// Find stores matching a predicate
    /// </summary>
    public List<StoreEntity> FindStores(Func<StoreEntity, bool> predicate, int? maxResults = null)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Code, Name, Rank FROM Stores";

            List<StoreEntity> results = new();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var store = new StoreEntity
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1),
                    Rank = reader.GetString(2)
                };

                if (predicate(store))
                {
                    results.Add(store);
                    if (maxResults.HasValue && results.Count >= maxResults.Value)
                        break;
                }
            }
            return results;
        }
    }

    /// <summary>
    /// Get the count of stores
    /// </summary>
    public int GetStoreCount()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Stores";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    /// <summary>
    /// Insert or update a store
    /// </summary>
    public void UpsertStore(StoreEntity store)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Stores (Code, Name, Rank)
                VALUES (@Code, @Name, @Rank)
                ON CONFLICT(Code) DO UPDATE SET
                    Name = excluded.Name,
                    Rank = excluded.Rank
            ";
            cmd.Parameters.AddWithValue("@Code", store.Code);
            cmd.Parameters.AddWithValue("@Name", store.Name);
            cmd.Parameters.AddWithValue("@Rank", store.Rank);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Insert or update multiple stores in a transaction
    /// </summary>
    public void UpsertStores(IEnumerable<StoreEntity> stores)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var store in stores)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO Stores (Code, Name, Rank)
                        VALUES (@Code, @Name, @Rank)
                        ON CONFLICT(Code) DO UPDATE SET
                            Name = excluded.Name,
                            Rank = excluded.Rank
                    ";
                    cmd.Parameters.AddWithValue("@Code", store.Code);
                    cmd.Parameters.AddWithValue("@Name", store.Name);
                    cmd.Parameters.AddWithValue("@Rank", store.Rank);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    /// <summary>
    /// Delete a store
    /// </summary>
    public bool DeleteStore(string code)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Stores WHERE Code = @Code";
            cmd.Parameters.AddWithValue("@Code", code);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>
    /// Delete all stores
    /// </summary>
    public int DeleteAllStores()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Stores";
            return cmd.ExecuteNonQuery();
        }
    }

    #endregion

    #region Vendors Operations

    /// <summary>
    /// Get all vendors, ordered by use count (most used first)
    /// </summary>
    public List<VendorEntity> GetAllVendors()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Name, DisplayName, Code, UseCount, ColorHex FROM Vendors ORDER BY UseCount DESC, DisplayName ASC";

            List<VendorEntity> vendors = new();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                vendors.Add(new VendorEntity
                {
                    Name = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    Code = reader.GetString(2),
                    UseCount = reader.GetInt32(3),
                    ColorHex = reader.GetString(4)
                });
            }
            return vendors;
        }
    }

    /// <summary>
    /// Get a vendor by name (case-insensitive lookup)
    /// </summary>
    public VendorEntity? GetVendor(string name)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name)) return null;

        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Name, DisplayName, Code, UseCount, ColorHex FROM Vendors WHERE Name = @Name";
            cmd.Parameters.AddWithValue("@Name", name.ToUpperInvariant());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new VendorEntity
                {
                    Name = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    Code = reader.GetString(2),
                    UseCount = reader.GetInt32(3),
                    ColorHex = reader.GetString(4)
                };
            }
            return null;
        }
    }

    /// <summary>
    /// Find vendors matching a predicate
    /// </summary>
    public List<VendorEntity> FindVendors(Func<VendorEntity, bool> predicate, int? limit = null)
    {
        ThrowIfDisposed();
        var all = GetAllVendors();
        var filtered = all.Where(predicate);
        return limit.HasValue ? filtered.Take(limit.Value).ToList() : filtered.ToList();
    }

    /// <summary>
    /// Search vendors by partial name match (for autocomplete)
    /// </summary>
    public List<VendorEntity> SearchVendors(string searchTerm, int limit = 10)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(searchTerm)) return new();

        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Name, DisplayName, Code, UseCount, ColorHex 
                FROM Vendors 
                WHERE DisplayName LIKE @Search OR Code LIKE @Search
                ORDER BY UseCount DESC, DisplayName ASC
                LIMIT @Limit";
            cmd.Parameters.AddWithValue("@Search", $"%{searchTerm}%");
            cmd.Parameters.AddWithValue("@Limit", limit);

            List<VendorEntity> vendors = new();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                vendors.Add(new VendorEntity
                {
                    Name = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    Code = reader.GetString(2),
                    UseCount = reader.GetInt32(3),
                    ColorHex = reader.GetString(4)
                });
            }
            return vendors;
        }
    }

    /// <summary>
    /// Get vendor count
    /// </summary>
    public int GetVendorCount()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Vendors";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    /// <summary>
    /// Insert or update a vendor
    /// </summary>
    public void UpsertVendor(VendorEntity vendor)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Vendors (Name, DisplayName, Code, UseCount, ColorHex)
                VALUES (@Name, @DisplayName, @Code, @UseCount, @ColorHex)
                ON CONFLICT(Name) DO UPDATE SET
                    DisplayName = excluded.DisplayName,
                    Code = excluded.Code,
                    UseCount = excluded.UseCount,
                    ColorHex = excluded.ColorHex
            ";
            cmd.Parameters.AddWithValue("@Name", vendor.Name.ToUpperInvariant());
            cmd.Parameters.AddWithValue("@DisplayName", vendor.DisplayName);
            cmd.Parameters.AddWithValue("@Code", vendor.Code);
            cmd.Parameters.AddWithValue("@UseCount", vendor.UseCount);
            cmd.Parameters.AddWithValue("@ColorHex", vendor.ColorHex);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Insert or update multiple vendors
    /// </summary>
    public void UpsertVendors(IEnumerable<VendorEntity> vendors)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var vendor in vendors)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO Vendors (Name, DisplayName, Code, UseCount, ColorHex)
                        VALUES (@Name, @DisplayName, @Code, @UseCount, @ColorHex)
                        ON CONFLICT(Name) DO UPDATE SET
                            DisplayName = excluded.DisplayName,
                            Code = excluded.Code,
                            UseCount = excluded.UseCount,
                            ColorHex = excluded.ColorHex
                    ";
                    cmd.Parameters.AddWithValue("@Name", vendor.Name.ToUpperInvariant());
                    cmd.Parameters.AddWithValue("@DisplayName", vendor.DisplayName);
                    cmd.Parameters.AddWithValue("@Code", vendor.Code);
                    cmd.Parameters.AddWithValue("@UseCount", vendor.UseCount);
                    cmd.Parameters.AddWithValue("@ColorHex", vendor.ColorHex);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    /// <summary>
    /// Increment the use count for a vendor (call when vendor is used in an order)
    /// </summary>
    public void IncrementVendorUseCount(string vendorName)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(vendorName)) return;

        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            // First try to increment existing
            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = "UPDATE Vendors SET UseCount = UseCount + 1 WHERE Name = @Name";
            updateCmd.Parameters.AddWithValue("@Name", vendorName.ToUpperInvariant());
            
            if (updateCmd.ExecuteNonQuery() == 0)
            {
                // Vendor doesn't exist, create it
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Vendors (Name, DisplayName, Code, UseCount, ColorHex)
                    VALUES (@Name, @DisplayName, '', 1, '')
                ";
                insertCmd.Parameters.AddWithValue("@Name", vendorName.ToUpperInvariant());
                insertCmd.Parameters.AddWithValue("@DisplayName", vendorName);
                insertCmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// Delete a vendor
    /// </summary>
    public bool DeleteVendor(string name)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name)) return false;

        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Vendors WHERE Name = @Name";
            cmd.Parameters.AddWithValue("@Name", name.ToUpperInvariant());
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>
    /// Delete all vendors
    /// </summary>
    public int DeleteAllVendors()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Vendors";
            return cmd.ExecuteNonQuery();
        }
    }

    #endregion

    /// <summary>
    /// Check if the database has been initialized with data
    /// </summary>
    public bool HasItems
    {
        get
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                using var connection = CreateConnection();
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM Items LIMIT 1)";
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }
    }

    /// <summary>
    /// Check if stores have been initialized
    /// </summary>
    public bool HasStores
    {
        get
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                using var connection = CreateConnection();
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM Stores LIMIT 1)";
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }
    }

    /// <summary>
    /// Check if vendors have been initialized
    /// </summary>
    public bool HasVendors
    {
        get
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                using var connection = CreateConnection();
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM Vendors LIMIT 1)";
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DictionaryDbContext));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
            Serilog.Log.Debug("DictionaryDbContext disposed successfully");
        }
    }

    /// <summary>
    /// Check if the context has been disposed
    /// </summary>
    public bool IsDisposed => _disposed;
}
