using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace differ.NET.Services;

/// <summary>
/// 错误日志服务，用于收集和显示应用错误
/// </summary>
public class ErrorLogService
{
    private static readonly ConcurrentQueue<ErrorLogEntry> _errorLogs = new();
    private static readonly int MaxLogEntries = 1000;
    private static readonly object _fileLock = new();
    
    /// <summary>
    /// 错误日志事件
    /// </summary>
    public static event Action<ErrorLogEntry>? OnErrorLogged;

    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// 记录错误日志
    /// </summary>
    public static void LogError(string message, Exception? exception = null, LogLevel level = LogLevel.Error)
    {
        var entry = new ErrorLogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            Exception = exception,
            Level = level,
            ThreadId = Environment.CurrentManagedThreadId
        };

        _errorLogs.Enqueue(entry);

        // 限制日志数量
        while (_errorLogs.Count > MaxLogEntries)
        {
            _errorLogs.TryDequeue(out _);
        }

        // 写入文件日志
        Task.Run(() => WriteToFileAsync(entry));

        // 触发事件
        OnErrorLogged?.Invoke(entry);

        // 控制台输出
        var consoleMessage = $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] {entry.Message}";
        if (entry.Exception != null)
        {
            consoleMessage += $" | Exception: {entry.Exception.Message}";
        }
        Console.WriteLine(consoleMessage);
    }

    /// <summary>
    /// 记录信息日志
    /// </summary>
    public static void LogInfo(string message)
    {
        LogError(message, null, LogLevel.Info);
    }

    /// <summary>
    /// 记录警告日志
    /// </summary>
    public static void LogWarning(string message, Exception? exception = null)
    {
        LogError(message, exception, LogLevel.Warning);
    }

    /// <summary>
    /// 记录严重错误日志
    /// </summary>
    public static void LogCritical(string message, Exception? exception = null)
    {
        LogError(message, exception, LogLevel.Critical);
    }

    /// <summary>
    /// 获取最近的错误日志
    /// </summary>
    public static List<ErrorLogEntry> GetRecentLogs(int count = 100)
    {
        return _errorLogs.TakeLast(count).ToList();
    }

    /// <summary>
    /// 获取所有错误日志
    /// </summary>
    public static List<ErrorLogEntry> GetAllLogs()
    {
        return _errorLogs.ToList();
    }

    /// <summary>
    /// 清除所有日志
    /// </summary>
    public static void ClearLogs()
    {
        _errorLogs.Clear();
    }

    /// <summary>
    /// 获取特定级别的日志
    /// </summary>
    public static List<ErrorLogEntry> GetLogsByLevel(LogLevel level)
    {
        return _errorLogs.Where(log => log.Level == level).ToList();
    }

    /// <summary>
    /// 搜索日志
    /// </summary>
    public static List<ErrorLogEntry> SearchLogs(string searchTerm, LogLevel? level = null)
    {
        var logs = _errorLogs.AsEnumerable();
        
        if (level.HasValue)
        {
            logs = logs.Where(log => log.Level == level.Value);
        }
        
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            logs = logs.Where(log => 
                log.Message.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (log.Exception != null && log.Exception.Message.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
        }
        
        return logs.ToList();
    }

    /// <summary>
    /// 写入日志到文件
    /// </summary>
    private static async Task WriteToFileAsync(ErrorLogEntry entry)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "differ.NET",
                "logs"
            );
            
            Directory.CreateDirectory(logPath);
            
            var logFile = Path.Combine(logPath, $"error_log_{DateTime.Now:yyyy-MM-dd}.txt");
            
            var logMessage = new StringBuilder();
            logMessage.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}");
            
            if (entry.Exception != null)
            {
                logMessage.AppendLine($"Exception: {entry.Exception.GetType().Name}: {entry.Exception.Message}");
                logMessage.AppendLine($"Stack Trace: {entry.Exception.StackTrace}");
                
                if (entry.Exception.InnerException != null)
                {
                    logMessage.AppendLine($"Inner Exception: {entry.Exception.InnerException.Message}");
                }
            }
            
            logMessage.AppendLine($"Thread ID: {entry.ThreadId}");
            logMessage.AppendLine(new string('-', 80));
            
            lock (_fileLock)
            {
                File.AppendAllText(logFile, logMessage.ToString());
            }
        }
        catch (Exception ex)
        {
            // 文件写入失败，记录到控制台但不抛出异常
            Console.WriteLine($"[ErrorLogService] Failed to write log to file: {ex.Message}");
        }
    }

    /// <summary>
    /// 导出日志到文件
    /// </summary>
    public static async Task<bool> ExportLogsAsync(string filePath)
    {
        try
        {
            var logs = GetAllLogs();
            var content = new StringBuilder();
            
            content.AppendLine($"differ.NET Error Log Export");
            content.AppendLine($"Export Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            content.AppendLine($"Total Entries: {logs.Count}");
            content.AppendLine(new string('=', 80));
            content.AppendLine();
            
            foreach (var log in logs.OrderBy(l => l.Timestamp))
            {
                content.AppendLine($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{log.Level}] {log.Message}");
                
                if (log.Exception != null)
                {
                    content.AppendLine($"Exception: {log.Exception.GetType().Name}: {log.Exception.Message}");
                }
                
                content.AppendLine();
            }
            
            await File.WriteAllTextAsync(filePath, content.ToString());
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Failed to export logs to {filePath}", ex);
            return false;
        }
    }
}

/// <summary>
/// 错误日志条目
/// </summary>
public class ErrorLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public ErrorLogService.LogLevel Level { get; set; }
    public int ThreadId { get; set; }

    /// <summary>
    /// 获取显示文本
    /// </summary>
    public string DisplayText
    {
        get
        {
            var text = $"[{Timestamp:HH:mm:ss}] {Message}";
            if (Exception != null)
            {
                text += $" | {Exception.GetType().Name}: {Exception.Message}";
            }
            return text;
        }
    }

    /// <summary>
    /// 获取级别颜色
    /// </summary>
    public string LevelColor
    {
        get
        {
            return Level switch
            {
                ErrorLogService.LogLevel.Info => "#0066CC",
                ErrorLogService.LogLevel.Warning => "#FF9900",
                ErrorLogService.LogLevel.Error => "#CC0000",
                ErrorLogService.LogLevel.Critical => "#990000",
                _ => "#000000"
            };
        }
    }
}