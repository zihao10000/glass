using System;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;

namespace GlassWarehouseSystem.Repositories;

/// <summary>
/// 【数据访问层】日志仓库类。
/// 提供向 MySQL Logs 表写入系统日志的简便方法。
/// 
/// 使用场景：
///   例如 InboundService 中的异常记录、ShiftService 中的顺移完成记录等。
/// 
/// 注意：此方法会创建独立的 DbContext 实例并立即 SaveChanges()，
/// </summary>
public class LogRepository
{
    /// <summary>
    /// 向数据库写入一条系统日志。
    /// </summary>
    /// <param name="content">日志内容（如 "入笼完成 GlassID=xxx"）</param>
    /// <param name="type">日志类型（"信息"、"警告"、"错误"）</param>
    public void Insert(string content, string type)
    {
        using var context = new WarehouseDbContext();
        context.Logs.Add(new SystemLog
        {
            LogContent = content,
            RecordTime = DateTime.Now,
            Type = type
        });
        context.SaveChanges();
    }
}
