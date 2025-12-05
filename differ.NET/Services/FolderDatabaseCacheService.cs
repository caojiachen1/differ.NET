using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace differ.NET.Services;

/// <summary>
/// 文件夹级别的SQLite数据库缓存服务，数据库文件保存在扫描的文件夹根目录
/// </summary>
public class FolderDatabaseCacheService : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;
    private bool _disposed;
    private readonly string _folderPath;

    /// <summary>
    /// 获取数据库文件路径
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    /// 获取关联的文件夹路径
    /// </summary>
    public string FolderPath => _folderPath;

    public FolderDatabaseCacheService(string folderPath)
    {
        _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        
        // 为每个文件夹创建独立的数据库文件
        DatabasePath = Path.Combine(folderPath, ".differ_cache.db");
        
        // 如果数据库文件存在但损坏，删除它
        if (File.Exists(DatabasePath) && IsDatabaseCorrupted())
        {
            try
            {
                File.Delete(DatabasePath);
                Console.WriteLine($"[FolderDatabaseCache] Deleted corrupted database file: {DatabasePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FolderDatabaseCache] Failed to delete corrupted database: {ex.Message}");
            }
        }
        
        _connectionString = $"Data Source={DatabasePath};Mode=ReadWriteCreate;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        InitializeDatabase();
    }

    /// <summary>
    /// 检查数据库是否损坏
    /// </summary>
    private bool IsDatabaseCorrupted()
    {
        try
        {
            using var tempConnection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadOnly");
            tempConnection.Open();
            
            // 尝试执行简单的查询来验证数据库完整性
            using var command = tempConnection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' LIMIT 1;";
            command.ExecuteScalar();
            
            return false; // 数据库正常
        }
        catch
        {
            return true; // 数据库损坏
        }
    }

    /// <summary>
    /// 初始化数据库表结构
    /// </summary>
    private void InitializeDatabase()
    {
        try
        {
            _connection.Open();
            
            // 创建图片特征缓存表
            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS ImageFeatures (
                    FilePath TEXT PRIMARY KEY,
                    FileName TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    LastModified INTEGER NOT NULL,
                    DinoFeatures BLOB NOT NULL,
                    DinoFeatureLength INTEGER NOT NULL,
                    CreatedAt INTEGER NOT NULL DEFAULT (strftime('%s', 'now')),
                    UpdatedAt INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))
                );

                CREATE INDEX IF NOT EXISTS idx_imagefeatures_lastmodified ON ImageFeatures(LastModified);
            ";

            _connection.Execute(createTableSql);
            
            // 创建数据库版本表，用于未来迁移
            const string createVersionTableSql = @"
                CREATE TABLE IF NOT EXISTS DatabaseVersion (
                    Version INTEGER PRIMARY KEY,
                    AppliedAt INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))
                );

                INSERT OR IGNORE INTO DatabaseVersion (Version) VALUES (1);
            ";

            _connection.Execute(createVersionTableSql);
            
            Console.WriteLine($"[FolderDatabaseCache] Initialized database for folder: {_folderPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderDatabaseCache] Failed to initialize database: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 获取缓存的DINOv3图片特征（如果文件未修改且存在缓存）
    /// </summary>
    public async Task<CachedImageFeatures?> GetCachedFeaturesAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[FolderDatabaseCache] File not found: {filePath}");
                return null;
            }

            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;
            var lastModified = fileInfo.LastWriteTimeUtc.ToFileTimeUtc();

            Console.WriteLine($"[FolderDatabaseCache] Checking cache for: {filePath}");
            Console.WriteLine($"[FolderDatabaseCache] File size: {fileSize}, Last modified: {lastModified}");

            const string sql = @"
                SELECT FilePath, FileName, FileSize, LastModified, 
                       DinoFeatures, DinoFeatureLength
                FROM ImageFeatures 
                WHERE FilePath = @FilePath 
                  AND FileSize = @FileSize 
                  AND LastModified = @LastModified
                LIMIT 1";

            // 使用原生ADO.NET读取BLOB数据，因为Dapper无法正确处理SQLite BLOB到byte[]的映射
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@FilePath", filePath);
            command.Parameters.AddWithValue("@FileSize", fileSize);
            command.Parameters.AddWithValue("@LastModified", lastModified);
            
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                Console.WriteLine($"[FolderDatabaseCache] Cache HIT for: {filePath}");
                
                var filePathResult = reader.GetString(0);
                var fileName = reader.GetString(1);
                var fileSizeResult = reader.GetInt64(2);
                var lastModifiedResult = reader.GetInt64(3);
                var dinoFeatureLength = reader.GetInt32(5);
                
                Console.WriteLine($"[FolderDatabaseCache] Cached feature length: {dinoFeatureLength}");
                
                byte[] dinoFeaturesBytes;
                if (!reader.IsDBNull(4))
                {
                    var blobSize = reader.GetBytes(4, 0, null, 0, 0);
                    Console.WriteLine($"[FolderDatabaseCache] BLOB Size: {blobSize}");
                    
                    dinoFeaturesBytes = new byte[blobSize];
                    reader.GetBytes(4, 0, dinoFeaturesBytes, 0, (int)blobSize);
                    Console.WriteLine($"[FolderDatabaseCache] Successfully read {dinoFeaturesBytes.Length} bytes");
                }
                else
                {
                    Console.WriteLine($"[FolderDatabaseCache] BLOB is NULL");
                    dinoFeaturesBytes = Array.Empty<byte>();
                }
                
                var result = new CachedImageFeatures
                {
                    FilePath = filePathResult,
                    FileName = fileName,
                    FileSize = fileSizeResult,
                    LastModified = lastModifiedResult,
                    DinoFeaturesBytes = dinoFeaturesBytes,
                    DinoFeatureLength = dinoFeatureLength
                };
                
                return result;
            }
            else
            {
                Console.WriteLine($"[FolderDatabaseCache] Cache MISS for: {filePath}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderDatabaseCache] Failed to get cached features for {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 缓存DINOv3图片特征
    /// </summary>
    public async Task<bool> CacheFeaturesAsync(string filePath, float[] dinoFeatures)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            var fileInfo = new FileInfo(filePath);
            var fileName = fileInfo.Name;
            var fileSize = fileInfo.Length;
            var lastModified = fileInfo.LastWriteTimeUtc.ToFileTimeUtc();
            var now = DateTime.UtcNow.ToFileTimeUtc();

            Console.WriteLine($"[FolderDatabaseCache] Caching DINO features for: {filePath}");
            Console.WriteLine($"[FolderDatabaseCache] Feature count: {dinoFeatures.Length}");
            Console.WriteLine($"[FolderDatabaseCache] Feature sample (first 5): {string.Join(", ", dinoFeatures.Take(5))}");

            byte[] dinoFeaturesBytes = new byte[dinoFeatures.Length * sizeof(float)];
            Buffer.BlockCopy(dinoFeatures, 0, dinoFeaturesBytes, 0, dinoFeaturesBytes.Length);

            Console.WriteLine($"[FolderDatabaseCache] Converted to bytes: {dinoFeaturesBytes.Length} bytes");

            const string sql = @"
                INSERT OR REPLACE INTO ImageFeatures 
                (FilePath, FileName, FileSize, LastModified, 
                 DinoFeatures, DinoFeatureLength, UpdatedAt)
                VALUES (@FilePath, @FileName, @FileSize, @LastModified,
                        @DinoFeatures, @DinoFeatureLength, @UpdatedAt)";

            var parameters = new
            {
                FilePath = filePath,
                FileName = fileName,
                FileSize = fileSize,
                LastModified = lastModified,
                DinoFeatures = dinoFeaturesBytes,
                DinoFeatureLength = dinoFeatures.Length,
                UpdatedAt = now
            };

            var rowsAffected = await _connection.ExecuteAsync(sql, parameters);
            
            if (rowsAffected > 0)
            {
                Console.WriteLine($"[FolderDatabaseCache] Successfully cached DINOv3 features for {filePath}");
            }
            else
            {
                Console.WriteLine($"[FolderDatabaseCache] Failed to cache features for {filePath} - no rows affected");
            }
            
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderDatabaseCache] Failed to cache features for {filePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 批量缓存DINOv3图片特征
    /// </summary>
    public async Task<bool> CacheFeaturesBatchAsync(IEnumerable<(string filePath, float[] dinoFeatures)> items)
    {
        try
        {
            using var transaction = _connection.BeginTransaction();
            var successCount = 0;

            foreach (var (filePath, dinoFeatures) in items)
            {
                if (await CacheFeaturesAsync(filePath, dinoFeatures))
                {
                    successCount++;
                }
            }

            transaction.Commit();
            Console.WriteLine($"[FolderDatabaseCache] Cached {successCount}/{items.Count()} image features");
            return successCount > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderDatabaseCache] Failed to batch cache features: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 清理过期缓存（删除文件不存在的记录）
    /// </summary>
    public async Task<int> CleanExpiredCacheAsync()
    {
        try
        {
            const string sql = "SELECT FilePath FROM ImageFeatures";
            var cachedPaths = await _connection.QueryAsync<string>(sql);
            
            var expiredPaths = cachedPaths.Where(path => !File.Exists(path)).ToList();
            
            if (expiredPaths.Any())
            {
                const string deleteSql = "DELETE FROM ImageFeatures WHERE FilePath = @FilePath";
                var deletedCount = 0;
                
                foreach (var path in expiredPaths)
                {
                    deletedCount += await _connection.ExecuteAsync(deleteSql, new { FilePath = path });
                }
                
                Console.WriteLine($"[FolderDatabaseCache] Cleaned {deletedCount} expired cache entries");
                return deletedCount;
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderDatabaseCache] Failed to clean expired cache: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// 获取DINOv3缓存统计信息
    /// </summary>
    public async Task<CacheStatistics> GetCacheStatisticsAsync()
    {
        try
        {
            const string sql = @"
                SELECT 
                    COUNT(*) as TotalEntries,
                    COUNT(CASE WHEN DinoFeatures IS NOT NULL THEN 1 END) as DinoEntries,
                    0 as PHashOnlyEntries
                FROM ImageFeatures";

            var stats = await _connection.QueryFirstAsync<CacheStatistics>(sql);
            return stats;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FolderDatabaseCache] Failed to get cache statistics: {ex.Message}");
            return new CacheStatistics();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _connection?.Dispose();
    }
}
