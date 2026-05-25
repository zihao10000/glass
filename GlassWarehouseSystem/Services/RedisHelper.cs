using GlassWarehouseSystem.Config;
using StackExchange.Redis;
using System;

namespace GlassWarehouseSystem.Services
{
    /// <summary>
    /// 【组件层扩展链接工具】提供一个简便访问快速获取且自动初始化和托管 StackExchange的Redis 全局连路通道配置助手静态环境搭建。
    /// 此处的连接使用基于 C# 的懒加载(Lazy机制)来使得未引用缓存的时候绝不发生无谓抢锁强链接拖延速度的占用策略。
    /// </summary>
    public class RedisHelper
    {
        private static readonly object SyncRoot = new();
        private static ConnectionMultiplexer? _connection;
        private static string? _currentConnStr;

        private static string BuildConnStr() => AppConfig.GetRedisConnectionString();

        public static ConnectionMultiplexer Connection
        {
            get
            {
                var connStr = BuildConnStr();
                lock (SyncRoot)
                {
                    if (_connection == null || !_connection.IsConnected || !string.Equals(_currentConnStr, connStr, StringComparison.OrdinalIgnoreCase))
                    {
                        _connection?.Dispose();
                        _connection = ConnectionMultiplexer.Connect(connStr);
                        _currentConnStr = connStr;
                    }

                    return _connection;
                }
            }
        }

        public static void ResetConnection()
        {
            lock (SyncRoot)
            {
                _connection?.Dispose();
                _connection = null;
                _currentConnStr = null;
            }
        }

        public static IDatabase GetDatabase(int? dbNum = null)
        {
            var index = dbNum ?? AppConfig.LocalSettings.Database.RedisDbIndex;
            return Connection.GetDatabase(index);
        }

        public static IDatabase Db => GetDatabase();

        public static IServer GetServer()
        {
            var endpoint = Connection.GetEndPoints()[0];
            return Connection.GetServer(endpoint);
        }
    }
}
