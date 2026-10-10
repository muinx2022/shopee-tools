using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using ShopeeBackend.Models;

namespace ShopeeBackend.Services
{
    public static class DataManager
    {
        private static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShopeeBackend", "Data");
        private static readonly string DbPath = Path.Combine(DataFolder, "shopee_data.db");
        private static readonly string ConnectionString = $"Data Source={DbPath}";

        public static void InitializeDatabase()
        {
            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            
            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Accounts (
                    Id TEXT PRIMARY KEY,
                    Username TEXT,
                    Password TEXT,
                    Cookie TEXT,
                    ProfileName TEXT,
                    Status TEXT
                );
                
                CREATE TABLE IF NOT EXISTS Proxies (
                    Id TEXT PRIMARY KEY,
                    Name TEXT,
                    Address TEXT
                );

                CREATE TABLE IF NOT EXISTS Categories (
                    Url TEXT PRIMARY KEY,
                    Name TEXT,
                    CurrentSubIndex INTEGER DEFAULT 1,
                    CurrentPage INTEGER DEFAULT 1,
                    TotalSubCategories INTEGER DEFAULT 0,
                    Status TEXT DEFAULT 'Chờ quét',
                    LastScrapedAt DATETIME
                );

                CREATE TABLE IF NOT EXISTS Keywords (
                    Keyword TEXT PRIMARY KEY,
                    CurrentPage INTEGER DEFAULT 1,
                    TotalPages INTEGER DEFAULT 0,
                    ProductCount INTEGER DEFAULT 0,
                    Status TEXT DEFAULT 'Chờ quét',
                    LastScrapedAt DATETIME
                );

                CREATE TABLE IF NOT EXISTS ScrapedProducts (
                    Id TEXT PRIMARY KEY,
                    CategoryUrl TEXT,
                    CategoryPath TEXT,
                    ItemId TEXT,
                    ShopId TEXT,
                    Name TEXT,
                    Price REAL,
                    Rating REAL,
                    RatingCount INTEGER,
                    HistoricalSold INTEGER,
                    MonthlySold INTEGER,
                    Location TEXT,
                    ImageUrl TEXT,
                    ProductUrl TEXT,
                    ScrapedAt DATETIME,
                    PreviousSold INTEGER DEFAULT 0,
                    SoldDelta INTEGER DEFAULT 0,
                    ScanCount INTEGER DEFAULT 1,
                    FirstScrapedAt DATETIME
                );

                CREATE TABLE IF NOT EXISTS ProductSnapshots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ShopId TEXT,
                    ItemId TEXT,
                    HistoricalSold INTEGER,
                    Price REAL,
                    Rating REAL,
                    RatingCount INTEGER,
                    ScrapedAt DATETIME
                );
                CREATE INDEX IF NOT EXISTS idx_snapshots_item ON ProductSnapshots(ShopId, ItemId, ScrapedAt);

                CREATE TABLE IF NOT EXISTS SubCategories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ParentUrl TEXT,
                    SubUrl TEXT,
                    Name TEXT,
                    SubIndex INTEGER,
                    Status TEXT DEFAULT 'Chờ quét',
                    ScrapedAt DATETIME
                );
                CREATE UNIQUE INDEX IF NOT EXISTS idx_subcats_parent_url ON SubCategories(ParentUrl, SubUrl);

                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT
                );
            ";
            try { command.ExecuteNonQuery(); } catch { }

            // Migration helpers for existing databases
            string[] alterStatements = new[]
            {
                "ALTER TABLE Accounts ADD COLUMN Cookie TEXT;",
                "ALTER TABLE ScrapedProducts ADD COLUMN Rating REAL;",
                "ALTER TABLE ScrapedProducts ADD COLUMN RatingCount INTEGER;",
                "ALTER TABLE ScrapedProducts ADD COLUMN MonthlySold INTEGER;",
                "ALTER TABLE ScrapedProducts ADD COLUMN Location TEXT;",
                "ALTER TABLE ScrapedProducts ADD COLUMN ImageUrl TEXT;",
                "ALTER TABLE ScrapedProducts ADD COLUMN CategoryPath TEXT;",
                "ALTER TABLE ScrapedProducts ADD COLUMN ProductUrl TEXT;",
                "ALTER TABLE ScrapedProducts ADD COLUMN PreviousSold INTEGER DEFAULT 0;",
                "ALTER TABLE ScrapedProducts ADD COLUMN SoldDelta INTEGER DEFAULT 0;",
                "ALTER TABLE ScrapedProducts ADD COLUMN ScanCount INTEGER DEFAULT 1;",
                "ALTER TABLE ScrapedProducts ADD COLUMN FirstScrapedAt DATETIME;",
                "ALTER TABLE ScrapedProducts ADD COLUMN LastTotalAt DATETIME;",
                "ALTER TABLE Categories ADD COLUMN CurrentSubIndex INTEGER DEFAULT 1;",
                "ALTER TABLE Categories ADD COLUMN CurrentPage INTEGER DEFAULT 1;",
                "ALTER TABLE Categories ADD COLUMN TotalSubCategories INTEGER DEFAULT 0;",
                "ALTER TABLE Categories ADD COLUMN Status TEXT DEFAULT 'Chờ quét';",
                "ALTER TABLE Categories ADD COLUMN LastScrapedAt DATETIME;"
            };

            foreach (var sql in alterStatements)
            {
                try
                {
                    using var altCmd = connection.CreateCommand();
                    altCmd.CommandText = sql;
                    altCmd.ExecuteNonQuery();
                }
                catch { }
            }

            ResetMixedSoldDeltaOnce(connection);
        }

        private static void ResetMixedSoldDeltaOnce(SqliteConnection connection)
        {
            try
            {
                using var check = connection.CreateCommand();
                check.CommandText = "SELECT Value FROM AppSettings WHERE Key = 'SoldDeltaMixedReset' LIMIT 1;";
                if (check.ExecuteScalar() as string == "1")
                    return;

                using var reset = connection.CreateCommand();
                reset.CommandText = @"
                    UPDATE ScrapedProducts
                    SET MonthlySold = 0
                    WHERE HistoricalSold > 0 AND MonthlySold = HistoricalSold;";
                reset.ExecuteNonQuery();

                using var clearFake = connection.CreateCommand();
                clearFake.CommandText = @"
                    UPDATE ScrapedProducts
                    SET SoldDelta = 0, PreviousSold = HistoricalSold
                    WHERE HistoricalSold > 0 AND SoldDelta * 1.0 > HistoricalSold * 0.5;";
                clearFake.ExecuteNonQuery();

                using var flag = connection.CreateCommand();
                flag.CommandText = "INSERT OR REPLACE INTO AppSettings (Key, Value) VALUES ('SoldDeltaMixedReset', '1');";
                flag.ExecuteNonQuery();
            }
            catch { }
        }

        public static void LoadData()
        {
            InitializeDatabase();
            
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            
            // 1. Load Accounts
            GlobalState.Accounts.Clear();
            var accCmd = connection.CreateCommand();
            accCmd.CommandText = "SELECT Id, Username, Password, ProfileName, Status, Cookie FROM Accounts";
            try {
                using (var reader = accCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        GlobalState.Accounts.Add(new AccountInfo {
                            Id = reader.GetString(0),
                            Username = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Password = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            ProfileName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Status = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            Cookie = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : ""
                        });
                    }
                }
            } catch { }

            // 2. Load Proxies
            GlobalState.Proxies.Clear();
            var pxCmd = connection.CreateCommand();
            pxCmd.CommandText = "SELECT Id, Name, Address FROM Proxies";
            try {
                using (var reader = pxCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        GlobalState.Proxies.Add(new ProxyInfo {
                            Id = reader.GetString(0),
                            Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            ApiKey = reader.IsDBNull(2) ? "" : reader.GetString(2)
                        });
                    }
                }
            } catch { }

            // 3. Load Categories
            GlobalState.Categories.Clear();
            var catCmd = connection.CreateCommand();
            catCmd.CommandText = "SELECT Url, Name, CurrentSubIndex, CurrentPage, TotalSubCategories, Status, LastScrapedAt FROM Categories";
            try {
                using (var reader = catCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string catStatus = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : "Chờ quét";
                        if (catStatus == "Đang quét") catStatus = "Chờ quét";

                        GlobalState.Categories.Add(new CategoryInfo {
                            Url = reader.GetString(0),
                            Name = ShopeeCategoryTranslator.Translate(reader.IsDBNull(1) ? "" : reader.GetString(1)),
                            CurrentSubIndex = reader.FieldCount > 2 && !reader.IsDBNull(2) ? reader.GetInt32(2) : 1,
                            CurrentPage = reader.FieldCount > 3 && !reader.IsDBNull(3) ? reader.GetInt32(3) : 1,
                            TotalSubCategories = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetInt32(4) : 0,
                            Status = catStatus,
                            LastScrapedAt = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetDateTime(6) : null
                        });
                    }
                }
            } catch { }

            // 3b. Load Keywords
            GlobalState.Keywords.Clear();
            var kwCmd = connection.CreateCommand();
            kwCmd.CommandText = "SELECT Keyword, CurrentPage, TotalPages, ProductCount, Status, LastScrapedAt FROM Keywords";
            try {
                using (var reader = kwCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string kwStatus = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : "Chờ quét";
                        if (kwStatus == "Đang quét") kwStatus = "Chờ quét";

                        GlobalState.Keywords.Add(new KeywordInfo {
                            Keyword = reader.GetString(0),
                            CurrentPage = reader.FieldCount > 1 && !reader.IsDBNull(1) ? reader.GetInt32(1) : 1,
                            TotalPages = reader.FieldCount > 2 && !reader.IsDBNull(2) ? reader.GetInt32(2) : 0,
                            ProductCount = reader.FieldCount > 3 && !reader.IsDBNull(3) ? reader.GetInt32(3) : 0,
                            Status = kwStatus,
                            LastScrapedAt = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetDateTime(5) : null
                        });
                    }
                }
            } catch { }

            // 4. Load ScrapedProducts
            LoadScrapedProducts();

            // 5. Load Settings
            GlobalState.AutoRerunDays = GetAutoRerunDays();
            CheckAndResetExpiredCategories();
            GlobalState.ImageStorageFolder = GetImageStorageFolder();
            GlobalState.AutoDownloadImages = GetAutoDownloadImages();
            EnsureFolderExists(GlobalState.ImageStorageFolder);
            GlobalState.MaxConcurrentFlows = GetMaxConcurrentFlows();
        }

        public static void LoadScrapedProducts()
        {
            GlobalState.ScrapedProducts.RaiseListChangedEvents = false;
            GlobalState.ScrapedProducts.Clear();
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

            var prodCmd = connection.CreateCommand();
            prodCmd.CommandText = @"
                SELECT Id, CategoryUrl, CategoryPath, ItemId, ShopId, Name, Price, 
                       Rating, RatingCount, HistoricalSold, MonthlySold, Location, 
                       ImageUrl, ProductUrl, ScrapedAt, PreviousSold, SoldDelta, ScanCount, FirstScrapedAt, LastTotalAt 
                FROM ScrapedProducts 
                ORDER BY ScrapedAt DESC";

            try
            {
                using var reader = prodCmd.ExecuteReader();
                while (reader.Read())
                {
                    long histSold = reader.IsDBNull(9) ? 0 : reader.GetInt64(9);
                    long monthSold = reader.IsDBNull(10) ? 0 : reader.GetInt64(10);

                    long prevSold = reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetInt64(15) : 0;
                    long delta = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetInt64(16) : 0;
                    int scanCnt = reader.FieldCount > 17 && !reader.IsDBNull(17) ? reader.GetInt32(17) : 1;
                    DateTime? firstAt = reader.FieldCount > 18 && !reader.IsDBNull(18) ? reader.GetDateTime(18) : null;
                    DateTime? lastTotalAt = reader.FieldCount > 19 && !reader.IsDBNull(19) ? reader.GetDateTime(19) : null;

                    var p = new ScrapedProduct
                    {
                        Id = reader.GetString(0),
                        CategoryUrl = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        CategoryPath = ShopeeCategoryTranslator.Translate(reader.IsDBNull(2) ? "" : reader.GetString(2)),
                        ItemId = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        ShopId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        Name = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        Price = reader.IsDBNull(6) ? 0 : Convert.ToDecimal(reader.GetDouble(6)),
                        Rating = reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                        RatingCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                        HistoricalSold = histSold,
                        MonthlySold = monthSold,
                        Location = reader.IsDBNull(11) ? "" : reader.GetString(11),
                        ImageUrl = reader.IsDBNull(12) ? "" : reader.GetString(12),
                        ProductUrl = reader.IsDBNull(13) ? "" : reader.GetString(13),
                        ScrapedAt = reader.IsDBNull(14) ? DateTime.Now : reader.GetDateTime(14),
                        PreviousSold = prevSold,
                        SoldDelta = delta,
                        ScanCount = scanCnt,
                        FirstScrapedAt = firstAt,
                        LastTotalAt = lastTotalAt
                    };

                    if (p.LastTotalAt.HasValue)
                    {
                        double days = (p.ScrapedAt - p.LastTotalAt.Value).TotalDays;
                        p.DaysTracked = days < 0 ? 0 : days;
                        p.DailyVelocity = p.DaysTracked >= 1 ? (double)p.SoldDelta / p.DaysTracked : 0;
                    }

                    p.EvaluateOpportunity();
                    GlobalState.ScrapedProducts.Add(p);
                }
            }
            catch { }
            }
            finally
            {
                GlobalState.ScrapedProducts.RaiseListChangedEvents = true;
                GlobalState.ScrapedProducts.ResetBindings();
            }
        }

        public static void SaveData()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            
            using var transaction = connection.BeginTransaction();
            
            try
            {
                var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                
                cmd.CommandText = "DELETE FROM Accounts";
                cmd.ExecuteNonQuery();
                
                foreach (var acc in GlobalState.Accounts)
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = "INSERT INTO Accounts (Id, Username, Password, ProfileName, Status, Cookie) VALUES ($id, $usr, $pwd, $pname, $status, $cookie)";
                    insertCmd.Parameters.AddWithValue("$id", acc.Id ?? Guid.NewGuid().ToString());
                    insertCmd.Parameters.AddWithValue("$usr", acc.Username ?? "");
                    insertCmd.Parameters.AddWithValue("$pwd", acc.Password ?? "");
                    insertCmd.Parameters.AddWithValue("$pname", acc.ProfileName ?? "");
                    insertCmd.Parameters.AddWithValue("$status", acc.Status ?? "");
                    insertCmd.Parameters.AddWithValue("$cookie", acc.Cookie ?? "");
                    insertCmd.ExecuteNonQuery();
                }

                cmd.CommandText = "DELETE FROM Proxies";
                cmd.ExecuteNonQuery();
                
                foreach (var px in GlobalState.Proxies)
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = "INSERT INTO Proxies (Id, Name, Address) VALUES ($id, $name, $addr)";
                    insertCmd.Parameters.AddWithValue("$id", px.Id ?? Guid.NewGuid().ToString());
                    insertCmd.Parameters.AddWithValue("$name", px.Name ?? "");
                    insertCmd.Parameters.AddWithValue("$addr", px.ApiKey ?? "");
                    insertCmd.ExecuteNonQuery();
                }

                cmd.CommandText = "DELETE FROM Categories";
                cmd.ExecuteNonQuery();
                
                foreach (var cat in GlobalState.Categories)
                {
                    var url = string.IsNullOrWhiteSpace(cat.Url) ? Guid.NewGuid().ToString() : cat.Url;

                    var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = @"INSERT OR REPLACE INTO Categories 
                        (Url, Name, CurrentSubIndex, CurrentPage, TotalSubCategories, Status, LastScrapedAt) 
                        VALUES ($url, $name, $sub, $page, $total, $status, $scraped)";
                    string catName = ShopeeCategoryTranslator.Translate(cat.Name ?? "");
                    if (string.IsNullOrWhiteSpace(catName) || catName == "Danh mục mới")
                    {
                        string fromUrl = ShopeeCategoryTranslator.ExtractCategoryNameFromUrl(cat.Url);
                        if (!string.IsNullOrWhiteSpace(fromUrl)) catName = fromUrl;
                    }
                    insertCmd.Parameters.AddWithValue("$url", url);
                    insertCmd.Parameters.AddWithValue("$name", catName);
                    insertCmd.Parameters.AddWithValue("$sub", cat.CurrentSubIndex);
                    insertCmd.Parameters.AddWithValue("$page", cat.CurrentPage);
                    insertCmd.Parameters.AddWithValue("$total", cat.TotalSubCategories);
                    insertCmd.Parameters.AddWithValue("$status", cat.Status ?? "Chờ quét");
                    insertCmd.Parameters.AddWithValue("$scraped", (object?)cat.LastScrapedAt ?? DBNull.Value);
                    insertCmd.ExecuteNonQuery();
                }

                cmd.CommandText = "DELETE FROM Keywords";
                cmd.ExecuteNonQuery();

                foreach (var kw in GlobalState.Keywords)
                {
                    if (string.IsNullOrWhiteSpace(kw.Keyword)) continue;
                    var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = @"INSERT OR REPLACE INTO Keywords 
                        (Keyword, CurrentPage, TotalPages, ProductCount, Status, LastScrapedAt) 
                        VALUES ($kw, $page, $total, $prods, $status, $scraped)";
                    insertCmd.Parameters.AddWithValue("$kw", kw.Keyword.Trim());
                    insertCmd.Parameters.AddWithValue("$page", kw.CurrentPage);
                    insertCmd.Parameters.AddWithValue("$total", kw.TotalPages);
                    insertCmd.Parameters.AddWithValue("$prods", kw.ProductCount);
                    insertCmd.Parameters.AddWithValue("$status", kw.Status ?? "Chờ quét");
                    insertCmd.Parameters.AddWithValue("$scraped", (object?)kw.LastScrapedAt ?? DBNull.Value);
                    insertCmd.ExecuteNonQuery();
                }

                transaction.Commit();
                SetMaxConcurrentFlows(GlobalState.MaxConcurrentFlows);
            }
            catch (Exception)
            {
                transaction.Rollback();
            }
        }

        public static void UpdateKeywordProgress(string keyword, int currentPage, int totalPages, int productCount, string status)
        {
            try
            {
                string normKw = keyword.Trim();
                var kw = GlobalState.Keywords.FirstOrDefault(k => k.Keyword.Equals(normKw, StringComparison.OrdinalIgnoreCase));
                if (kw != null)
                {
                    kw.CurrentPage = Math.Max(1, currentPage);
                    if (totalPages > 0) kw.TotalPages = totalPages;
                    if (productCount > 0) kw.ProductCount = productCount;
                    kw.Status = status;
                    kw.LastScrapedAt = DateTime.Now;

                    if (System.Windows.Forms.Application.OpenForms.Count > 0)
                    {
                        var main = System.Windows.Forms.Application.OpenForms[0];
                        main?.BeginInvoke(new Action(() =>
                        {
                            int idx = GlobalState.Keywords.IndexOf(kw);
                            if (idx >= 0) GlobalState.Keywords.ResetItem(idx);
                        }));
                    }
                }

                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Keywords (Keyword, CurrentPage, TotalPages, ProductCount, Status, LastScrapedAt)
                    VALUES ($kw, $page, $total, $prods, $status, $time)
                    ON CONFLICT(Keyword) DO UPDATE SET
                        CurrentPage = excluded.CurrentPage,
                        TotalPages = CASE WHEN excluded.TotalPages > 0 THEN excluded.TotalPages ELSE Keywords.TotalPages END,
                        ProductCount = CASE WHEN excluded.ProductCount > 0 THEN excluded.ProductCount ELSE Keywords.ProductCount END,
                        Status = excluded.Status,
                        LastScrapedAt = excluded.LastScrapedAt;";
                cmd.Parameters.AddWithValue("$kw", normKw);
                cmd.Parameters.AddWithValue("$page", Math.Max(1, currentPage));
                cmd.Parameters.AddWithValue("$total", totalPages);
                cmd.Parameters.AddWithValue("$prods", productCount);
                cmd.Parameters.AddWithValue("$status", status);
                cmd.Parameters.AddWithValue("$time", DateTime.Now);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static void ResetKeywordProgress(string keyword)
        {
            UpdateKeywordProgress(keyword, 1, 0, 0, "Chờ quét");
        }

        public static void UpdateCategoryProgress(string url, int currentSubIndex, int currentPage, int totalSubs, string status)
        {
            try
            {
                var cat = GlobalState.Categories.FirstOrDefault(c => c.Url == url);
                if (cat != null)
                {
                    cat.CurrentSubIndex = Math.Max(1, currentSubIndex);
                    cat.CurrentPage = Math.Max(1, currentPage);
                    if (totalSubs > 0) cat.TotalSubCategories = totalSubs;
                    cat.Status = status;
                    cat.LastScrapedAt = DateTime.Now;

                    if (System.Windows.Forms.Application.OpenForms.Count > 0)
                    {
                        var main = System.Windows.Forms.Application.OpenForms[0];
                        main?.BeginInvoke(new Action(() =>
                        {
                            int idx = GlobalState.Categories.IndexOf(cat);
                            if (idx >= 0) GlobalState.Categories.ResetItem(idx);
                        }));
                    }
                }

                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Categories 
                    SET CurrentSubIndex = $sub, 
                        CurrentPage = $page, 
                        TotalSubCategories = CASE WHEN $total > 0 THEN $total ELSE TotalSubCategories END, 
                        Status = $status, 
                        LastScrapedAt = $time 
                    WHERE Url = $url;";
                cmd.Parameters.AddWithValue("$sub", Math.Max(1, currentSubIndex));
                cmd.Parameters.AddWithValue("$page", Math.Max(1, currentPage));
                cmd.Parameters.AddWithValue("$total", totalSubs);
                cmd.Parameters.AddWithValue("$status", status);
                cmd.Parameters.AddWithValue("$time", DateTime.Now);
                cmd.Parameters.AddWithValue("$url", url);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static void ResetCategoryProgress(string url)
        {
            UpdateCategoryProgress(url, 1, 1, 0, "Chờ quét");
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE SubCategories SET Status = 'Chờ quét' WHERE ParentUrl = $url;";
                cmd.Parameters.AddWithValue("$url", url);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static void SaveDiscoveredSubCategories(string parentUrl, IEnumerable<(int Index, string Name, string Url)> subCats)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();
                foreach (var s in subCats)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO SubCategories (ParentUrl, SubUrl, Name, SubIndex, Status)
                        VALUES ($parent, $url, $name, $idx, 'Chờ quét')
                        ON CONFLICT(ParentUrl, SubUrl) DO UPDATE SET Name = excluded.Name, SubIndex = excluded.SubIndex;";
                    cmd.Parameters.AddWithValue("$parent", parentUrl);
                    cmd.Parameters.AddWithValue("$url", s.Url);
                    cmd.Parameters.AddWithValue("$name", ShopeeCategoryTranslator.Translate(s.Name));
                    cmd.Parameters.AddWithValue("$idx", s.Index);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch { }
        }

        public static void MarkSubCategoryCompleted(string parentUrl, int subIndex, string subName)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE SubCategories 
                    SET Status = 'Hoàn thành', ScrapedAt = $time 
                    WHERE ParentUrl = $parent AND (SubIndex = $idx OR Name = $name OR Name = $origName);";
                cmd.Parameters.AddWithValue("$parent", parentUrl);
                cmd.Parameters.AddWithValue("$idx", subIndex);
                cmd.Parameters.AddWithValue("$name", ShopeeCategoryTranslator.Translate(subName));
                cmd.Parameters.AddWithValue("$origName", subName ?? "");
                cmd.Parameters.AddWithValue("$time", DateTime.Now);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static void SaveScrapedProducts(IEnumerable<ScrapedProduct> products)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var p in products)
                {
                    p.CategoryPath = ShopeeCategoryTranslator.Translate(p.CategoryPath ?? "");
                    string recordId = !string.IsNullOrWhiteSpace(p.ShopId) && !string.IsNullOrWhiteSpace(p.ItemId) 
                        ? $"{p.ShopId}_{p.ItemId}" 
                        : (p.Id ?? Guid.NewGuid().ToString());
                    p.Id = recordId;

                    bool wroteTotal = p.HistoricalSold > 0;
                    bool wroteMonth = p.MonthlySold > 0;

                    // Check existing product in DB to compute ScanCount, PreviousSold, SoldDelta
                    using (var findCmd = connection.CreateCommand())
                    {
                        findCmd.Transaction = transaction;
                        findCmd.CommandText = "SELECT HistoricalSold, MonthlySold, PreviousSold, ScanCount, FirstScrapedAt, SoldDelta, LastTotalAt FROM ScrapedProducts WHERE Id = $id LIMIT 1;";
                        findCmd.Parameters.AddWithValue("$id", recordId);
                        using var r = findCmd.ExecuteReader();
                        if (r.Read())
                        {
                            long oldHistSold = r.IsDBNull(0) ? 0 : r.GetInt64(0);
                            long oldMonthSold = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                            long oldPrevious = r.IsDBNull(2) ? 0 : r.GetInt64(2);
                            int scanCount = r.IsDBNull(3) ? 1 : r.GetInt32(3);
                            DateTime firstAt = r.IsDBNull(4) ? p.ScrapedAt : r.GetDateTime(4);
                            long oldDelta = r.IsDBNull(5) ? 0 : r.GetInt64(5);
                            DateTime? oldLastTotal = r.IsDBNull(6) ? null : r.GetDateTime(6);

                            p.ScanCount = scanCount + 1;
                            p.FirstScrapedAt = firstAt;

                            if (!wroteTotal && oldHistSold > 0) p.HistoricalSold = oldHistSold;
                            if (!wroteMonth && oldMonthSold > 0) p.MonthlySold = oldMonthSold;

                            if (wroteTotal)
                            {
                                var reading = SoldRules.ApplyTotalReading(
                                    oldHistSold,
                                    oldPrevious,
                                    oldDelta,
                                    oldLastTotal,
                                    p.HistoricalSold,
                                    p.ScrapedAt,
                                    GlobalState.AutoRerunDays);
                                p.SoldDelta = reading.SoldDelta;
                                p.PreviousSold = reading.PreviousSold;
                                ApplyVelocity(p, oldLastTotal);
                                p.LastTotalAt = reading.BaselineMoved ? reading.LastTotalAt : oldLastTotal;
                            }
                            else
                            {
                                p.SoldDelta = SoldRules.KeepDeltaOnMonthScan(oldDelta);
                                p.PreviousSold = oldPrevious;
                                p.LastTotalAt = oldLastTotal;
                                ApplyVelocity(p, oldLastTotal);
                            }
                        }
                        else
                        {
                            p.ScanCount = 1;
                            p.FirstScrapedAt = p.ScrapedAt;
                            if (wroteTotal)
                            {
                                p.PreviousSold = p.HistoricalSold;
                                p.LastTotalAt = p.ScrapedAt;
                            }
                            else
                            {
                                p.PreviousSold = 0;
                                p.LastTotalAt = null;
                            }
                            p.SoldDelta = 0;
                            p.DaysTracked = 0;
                            p.DailyVelocity = 0;
                        }
                    }

                    var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO ScrapedProducts 
                        (Id, CategoryUrl, CategoryPath, ItemId, ShopId, Name, Price, Rating, RatingCount, HistoricalSold, MonthlySold, Location, ImageUrl, ProductUrl, ScrapedAt, PreviousSold, SoldDelta, ScanCount, FirstScrapedAt, LastTotalAt) 
                        VALUES ($id, $cat, $cpath, $item, $shop, $name, $price, $rating, $rcount, $sold, $msold, $loc, $img, $purl, $time, $prevSold, $delta, $scnt, $firstAt, $lastTotal)";

                    cmd.Parameters.AddWithValue("$id", recordId);
                    cmd.Parameters.AddWithValue("$cat", p.CategoryUrl ?? "");
                    cmd.Parameters.AddWithValue("$cpath", p.CategoryPath ?? "");
                    cmd.Parameters.AddWithValue("$item", p.ItemId ?? "");
                    cmd.Parameters.AddWithValue("$shop", p.ShopId ?? "");
                    cmd.Parameters.AddWithValue("$name", p.Name ?? "");
                    cmd.Parameters.AddWithValue("$price", Convert.ToDouble(p.Price));
                    cmd.Parameters.AddWithValue("$rating", p.Rating);
                    cmd.Parameters.AddWithValue("$rcount", p.RatingCount);
                    cmd.Parameters.AddWithValue("$sold", p.HistoricalSold);
                    cmd.Parameters.AddWithValue("$msold", p.MonthlySold);
                    cmd.Parameters.AddWithValue("$loc", p.Location ?? "");
                    cmd.Parameters.AddWithValue("$img", p.ImageUrl ?? "");
                    cmd.Parameters.AddWithValue("$purl", p.ProductUrl ?? "");
                    cmd.Parameters.AddWithValue("$time", p.ScrapedAt);
                    cmd.Parameters.AddWithValue("$prevSold", p.PreviousSold);
                    cmd.Parameters.AddWithValue("$delta", p.SoldDelta);
                    cmd.Parameters.AddWithValue("$scnt", p.ScanCount);
                    cmd.Parameters.AddWithValue("$firstAt", p.FirstScrapedAt ?? p.ScrapedAt);
                    cmd.Parameters.AddWithValue("$lastTotal", p.LastTotalAt.HasValue ? p.LastTotalAt.Value : DBNull.Value);

                    cmd.ExecuteNonQuery();

                    if (wroteTotal && p.HistoricalSold > 0)
                    {
                        var snapCmd = connection.CreateCommand();
                        snapCmd.Transaction = transaction;
                        snapCmd.CommandText = @"
                            INSERT INTO ProductSnapshots 
                            (ShopId, ItemId, HistoricalSold, Price, Rating, RatingCount, ScrapedAt) 
                            VALUES ($shop, $item, $sold, $price, $rating, $rcount, $time)";
                        snapCmd.Parameters.AddWithValue("$shop", p.ShopId ?? "");
                        snapCmd.Parameters.AddWithValue("$item", p.ItemId ?? "");
                        snapCmd.Parameters.AddWithValue("$sold", p.HistoricalSold);
                        snapCmd.Parameters.AddWithValue("$price", Convert.ToDouble(p.Price));
                        snapCmd.Parameters.AddWithValue("$rating", p.Rating);
                        snapCmd.Parameters.AddWithValue("$rcount", p.RatingCount);
                        snapCmd.Parameters.AddWithValue("$time", p.ScrapedAt);
                        snapCmd.ExecuteNonQuery();
                    }

                    p.EvaluateOpportunity();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static void ApplyVelocity(ScrapedProduct p, DateTime? anchor)
        {
            if (!anchor.HasValue)
            {
                p.DaysTracked = 0;
                p.DailyVelocity = 0;
                return;
            }

            double days = (p.ScrapedAt - anchor.Value).TotalDays;
            p.DaysTracked = days < 0 ? 0 : days;
            p.DailyVelocity = p.DaysTracked >= 1 ? (double)p.SoldDelta / p.DaysTracked : 0;
        }

        public static void DeleteScrapedProducts(IEnumerable<string> ids)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var id in ids)
                {
                    var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM ScrapedProducts WHERE Id = $id";
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
            }
        }

        public static void ClearAllScrapedProducts()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM ScrapedProducts";
            cmd.ExecuteNonQuery();
        }

        public static string GetSetting(string key, string defaultValue = "")
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key LIMIT 1;";
                cmd.Parameters.AddWithValue("$key", key);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                    return res.ToString() ?? defaultValue;
            }
            catch { }
            return defaultValue;
        }

        public static void SetSetting(string key, string value)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO AppSettings (Key, Value) VALUES ($key, $val)
                    ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
                cmd.Parameters.AddWithValue("$key", key);
                cmd.Parameters.AddWithValue("$val", value ?? "");
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static int GetAutoRerunDays()
        {
            string val = GetSetting("AutoRerunDays", "7");
            if (int.TryParse(val, out int days) && days >= 0)
                return days;
            return 7;
        }

        public static void SetAutoRerunDays(int days)
        {
            int safeDays = Math.Max(0, days);
            SetSetting("AutoRerunDays", safeDays.ToString());
            GlobalState.AutoRerunDays = safeDays;
        }

        public static int GetMaxConcurrentFlows()
        {
            string val = GetSetting("MaxConcurrentFlows", "3");
            if (int.TryParse(val, out int flows) && flows >= 1 && flows <= 20)
                return flows;
            return 3;
        }

        public static void SetMaxConcurrentFlows(int flows)
        {
            int safeFlows = Math.Max(1, Math.Min(20, flows));
            SetSetting("MaxConcurrentFlows", safeFlows.ToString());
            GlobalState.MaxConcurrentFlows = safeFlows;
        }

        public static (int Width, int Height, bool Maximized) GetWindowBounds(int defaultW, int defaultH)
        {
            string wStr = GetSetting("WindowWidth", defaultW.ToString());
            string hStr = GetSetting("WindowHeight", defaultH.ToString());
            string mStr = GetSetting("WindowMaximized", "0");
            int.TryParse(wStr, out int w);
            int.TryParse(hStr, out int h);
            bool max = mStr == "1";
            if (w < 1200) w = defaultW;
            if (h < 700) h = defaultH;
            return (w, h, max);
        }

        public static void SaveWindowBounds(int w, int h, bool maximized)
        {
            SetSetting("WindowWidth", w.ToString());
            SetSetting("WindowHeight", h.ToString());
            SetSetting("WindowMaximized", maximized ? "1" : "0");
        }

        public static int CheckAndResetExpiredCategories()
        {
            int days = GlobalState.AutoRerunDays;
            if (days <= 0) return 0;

            int resetCount = 0;
            var now = DateTime.Now;
            foreach (var cat in GlobalState.Categories)
            {
                if (cat.Status == "Hoàn thành" && cat.LastScrapedAt.HasValue)
                {
                    if ((now - cat.LastScrapedAt.Value).TotalDays >= days)
                    {
                        ResetCategoryProgress(cat.Url);
                        cat.CurrentSubIndex = 1;
                        cat.CurrentPage = 1;
                        cat.Status = "Chờ quét";
                        resetCount++;
                    }
                }
            }
            return resetCount;
        }

        public static string GetImageStorageFolder()
        {
            string val = GetSetting("ImageStorageFolder", @"e:\images\phan-tich-shopee");
            if (string.IsNullOrWhiteSpace(val)) val = @"e:\images\phan-tich-shopee";
            EnsureFolderExists(val);
            return val;
        }

        public static void SetImageStorageFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) folder = @"e:\images\phan-tich-shopee";
            EnsureFolderExists(folder);
            SetSetting("ImageStorageFolder", folder);
            GlobalState.ImageStorageFolder = folder;
        }

        public static bool GetAutoDownloadImages()
        {
            string val = GetSetting("AutoDownloadImages", "1");
            return val != "0";
        }

        public static void SetAutoDownloadImages(bool enable)
        {
            SetSetting("AutoDownloadImages", enable ? "1" : "0");
            GlobalState.AutoDownloadImages = enable;
        }

        public static void EnsureFolderExists(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch { }
        }

        public static void UpdateAccountStatus(string accountId, string status)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Accounts SET Status = $status WHERE Id = $id";
                cmd.Parameters.AddWithValue("$status", status ?? "");
                cmd.Parameters.AddWithValue("$id", accountId);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }
}
