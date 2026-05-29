using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using OrderLog.Data.Entities;

namespace OrderLog.Data;

/// <summary>
/// Shared SQLite context for vendor dictionary data.
/// Thread-safe for concurrent access using WAL mode.
/// </summary>
public sealed class DictionaryDbContext : IDisposable
{
    private static readonly Lazy<DictionaryDbContext> _instance = new(() => new DictionaryDbContext());
    private static readonly object _lock = new();
    private readonly string _connectionString;
    private bool _disposed;

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

    #endregion

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
