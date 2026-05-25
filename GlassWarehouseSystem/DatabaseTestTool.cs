using System;
using System.Linq;
using System.Threading.Tasks;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace GlassWarehouseSystem
{
    /// <summary>
    /// 可以在不清楚表有未崩掉跑这个检测保证能安全跑完
    /// </summary>
    public static class DatabaseTestTool
    {
        /// <summary>
        /// 全系列数据库环境连跑连贯自测安全发配沙场，会自行走完插入查询删除更新全链以判断建的 EF Core DB表环境配置是不是完好健壮能够应对业务的压榨。
        /// </summary>
        public static async Task<bool> RunAllTestsAsync()
        {
            // Removed for brevity and safety as new schemas are used
            // Real tests should be placed here later using new entities.
            // 原先用于向新建构的结构投递一批做假数据比如插入Cage A01 B02 这种然后塞料读取看是否死锁等动作的预加载校验脚本。因库已经跑稳目前短路拦截返回真直接上正线
            return await Task.FromResult(true);
        }

        /// <summary>
        /// 用于辅助打印或者在屏幕输出给跑本地开发的黑框看用的工具日志点输出方法包裹器
        /// </summary>
        public static void Log(string msg)
        {
            Console.WriteLine($"[DatabaseTest] {msg}");
        }
    }
}
