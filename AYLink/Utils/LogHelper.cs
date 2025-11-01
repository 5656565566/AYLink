using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace AYLink.Utils;

/// <summary>
/// 封装的NLog日志工具类
/// </summary>
public sealed class LogHelper
{
    #region 单例实现

    private static readonly Lazy<LogHelper> _instance = new(() => new LogHelper());

    /// <summary>
    /// 获取LogHelper的单例实例
    /// </summary>
    public static LogHelper Instance => _instance.Value;

    private readonly Logger _logger;

    private LogHelper()
    {
        SetupNLogConfiguration();
        _logger = LogManager.GetLogger("swLog");
    }
    #endregion

    #region 配置NLog
    private static void SetupNLogConfiguration()
    {
        CleanOldLogs();

        var config = new LoggingConfiguration();

        // 控制台目标
        var consoleTarget = new ColoredConsoleTarget("console")
        {
            Layout = "${longdate} ${message} ${exception:format=tostring}"
        };
        config.AddTarget(consoleTarget);

        // 文件目标
        var fileTarget = new FileTarget("file")
        {
            FileName = "${basedir}/Logs/${shortdate}/${level}.log",
            Layout = "${longdate} [${threadid}] | ${message} ${onexception:${exception:format=tostring} ${newline} ${stacktrace} ${newline}",
            Encoding = Encoding.Default,
            WriteBom = false
        };
        config.AddTarget(fileTarget);

        // 规则
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, consoleTarget);
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);

        LogManager.Configuration = config;
    }

    /// <summary>
    /// 清理一周前的日志文件夹
    /// </summary>
    private static void CleanOldLogs()
    {
        try
        {
            string logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logsDirectory))
            {
                return;
            }

            var cutoffDate = DateTime.Now.AddDays(-7);
            var logDirectories = Directory.GetDirectories(logsDirectory);

            foreach (var dir in logDirectories)
            {
                // 从文件夹名称解析日期
                if (DateTime.TryParse(Path.GetFileName(dir), out var folderDate))
                {
                    if (folderDate < cutoffDate)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
    }
    #endregion

    #region 公共方法

    /// <summary>
    /// 写入信息级别日志
    /// </summary>
    /// <param name="message">日志消息</param>
    public void Info(string message)
    {
        if (_logger.IsInfoEnabled)
        {
            _logger.Info(message);
        }
    }

    /// <summary>
    /// 写入格式化信息级别日志
    /// </summary>
    /// <param name="format">格式化字符串</param>
    /// <param name="args">参数</param>
    public void Info(string format, params object?[] args)
    {
        if (_logger.IsInfoEnabled)
        {
            _logger.Info(format, args);
        }
    }

    /// <summary>
    /// 写入异常信息级别日志
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <param name="message">附加消息</param>
    public void Info(Exception exception, [StringSyntax("CompositeFormat")] string? message = null)
    {
        if (_logger.IsInfoEnabled)
        {
            _logger.Info(exception, message ?? string.Empty);
        }
    }

    /// <summary>
    /// 写入错误级别日志
    /// </summary>
    /// <param name="message">日志消息</param>
    public void Error(string message)
    {
        if (_logger.IsErrorEnabled)
        {
            _logger.Error(message);
        }
    }

    /// <summary>
    /// 写入格式化错误级别日志
    /// </summary>
    /// <param name="format">格式化字符串</param>
    /// <param name="args">参数</param>
    public void Error(string format, params object?[] args)
    {
        if (_logger.IsErrorEnabled)
        {
            _logger.Error(format, args);
        }
    }

    /// <summary>
    /// 写入异常错误级别日志
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <param name="message">附加消息</param>
    public void Error(Exception exception, [StringSyntax("CompositeFormat")] string? message = null)
    {
        if (_logger.IsErrorEnabled)
        {
            _logger.Error(exception, message ?? string.Empty);
        }
    }

    /// <summary>
    /// 写入警告级别日志
    /// </summary>
    /// <param name="message">日志消息</param>
    public void Warn(string message)
    {
        if (_logger.IsWarnEnabled)
        {
            _logger.Warn(message);
        }
    }

    /// <summary>
    /// 写入格式化警告级别日志
    /// </summary>
    /// <param name="format">格式化字符串</param>
    /// <param name="args">参数</param>
    public void Warn(string format, params object?[] args)
    {
        if (_logger.IsWarnEnabled)
        {
            _logger.Warn(format, args);
        }
    }

    /// <summary>
    /// 写入异常警告级别日志
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <param name="message">附加消息</param>
    public void Warn(Exception exception, [StringSyntax("CompositeFormat")] string? message = null)
    {
        if (_logger.IsWarnEnabled)
        {
            _logger.Warn(exception, message ?? string.Empty);
        }
    }
    #endregion
}