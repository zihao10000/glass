using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using GlassWarehouseSystem.Services;

namespace GlassWarehouseSystem.Services
{
    /// <summary>
    /// 【性能与架构组件】通用的三级穿透防雪崩缓存中间查询服务组件。
    /// （依赖于 RedisHelper 对联络缓存微服务的把控）
    ///  其职责逻辑就是标准的穿透旁路策略：
    ///   命中 → "先向外部 Redis 高速节点核查有无存根记录，如果存了且格式正确可解析则短距离返回！省去 MySQL 多表关联合并查询开锁开销。"
    ///   未命中 → "如果没有记录/或者强行反序列化解析报错毁坏，则落回到被包裹在外部传入进来的真实 EF Core dbQueryFunc 回调匿名函数做硬拉。并在得到正确真实结果向外层分发时前，打好JSON补丁包裹存向缓存Redis且留有强制过期清理标记保证数据新鲜。"
    /// </summary>
    public static class CacheQueryService
    {
        /// <summary>
        /// 获取缓存数据的（阻塞同步轮休）版本使用。
        /// </summary>
        /// <typeparam name="T">期望返抵的数据实体的原始类推导泛型对象类型形式（多用于传 List<Material>等集合）</typeparam>
        /// <param name="cacheKey">全局分布式系统认可存放寻找地址路由该数据的名称特征Redis键盘</param>
        /// <param name="dbQueryFunc">由于在非热缓存没被打满击穿时，这里装载传入用匿名委托装好的将真实要查询的数据取值的逻辑体回掉委托。</param>
        /// <param name="expiry">该数据的自然界有效期流逝设置（过期会被抛弃重取） 缺省默认是60秒自动销毁抛弃陈旧项确保查询业务与时差性兼得</param>
        /// <param name="tryRedisFirst">是否优先尝试从 Redis 读取缓存。false 时直接读取 MySQL，然后再回写缓存。</param>
        /// <returns>最终结果返回正确解析出来的可用结果（要么从超快内存中，要么从硬盘MySQL表中新取出）</returns>
        public static List<T> GetCachedData<T>(
            string cacheKey,
            Func<List<T>> dbQueryFunc,
            TimeSpan? expiry = null,
            bool tryRedisFirst = true)
        {
            if (tryRedisFirst)
            {
                // 1. 先从 Redis 读缓存节点
                var cachedJson = RedisHelper.Db.StringGet(cacheKey);
                
                if (cachedJson.HasValue)
                {
                    // Redis 有缓存，尝试反序列化回内存原生C#实体结构
                    try
                    {
                        return JsonConvert.DeserializeObject<List<T>>(cachedJson)!;
                    }
                    catch
                    {
                        // 反序列化异常毁损崩溃的脏烂结构则手动强制删除，当它从来没存在过。进而触发底下打回到安全平底查Mysql逻辑避雷不闪退。
                        RedisHelper.Db.KeyDelete(cacheKey);
                    }
                }
            }

            // 2. 缓存中并没有 → 被迫调传入被封装好的实体查数据库去获得源底值。
            var data = dbQueryFunc();

            // 3. 将新鲜获取到珍贵成果拿JSON化装袋后顺道给回放到缓存节点占领位置备用接下来其它使用者查询。
            if (tryRedisFirst)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(data, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
                    RedisHelper.Db.StringSet(cacheKey, json, expiry ?? TimeSpan.FromMinutes(1));
                }
                catch
                {
                    // 若由于Redis没连上、进程不让写写入失败不要去报异常给系统抛错阻塞主干核心功能正常流分发回抛展现使用成果数据（退回为单纯的读库只用业务）
                }
            }

            return data;
        }

        /// <summary>
        /// 获取缓存数据的（Task异步）版本使用
        /// 实现方式完全等价于上述GetCachedData同步只差异在其支持全链异步。
        /// </summary>
        public static async Task<List<T>> GetCachedDataAsync<T>(
            string cacheKey,
            Func<Task<List<T>>> dbQueryFunc,
            TimeSpan? expiry = null,
            bool tryRedisFirst = true)
        {
            if (tryRedisFirst)
            {
                var cachedJson = await RedisHelper.Db.StringGetAsync(cacheKey);
                
                if (cachedJson.HasValue)
                {
                    try
                    {
                        return JsonConvert.DeserializeObject<List<T>>(cachedJson)!;
                    }
                    catch
                    {
                        await RedisHelper.Db.KeyDeleteAsync(cacheKey);
                    }
                }
            }

            var data = await dbQueryFunc();

            if (tryRedisFirst)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(data, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
                    await RedisHelper.Db.StringSetAsync(cacheKey, json, expiry ?? TimeSpan.FromMinutes(1));
                }
                catch
                {
                    // Redis 写入失败不影响返回业务呈现数据流程
                }
            }

            return data;
        }

        /// <summary>直接按精确 key 删除缓存项，比 ClearCacheByPattern 更可靠（不依赖 GetServer().Keys()）。</summary>
        public static void ClearCacheByKeys(params string[] keys)
        {
            try
            {
                foreach (var key in keys)
                    RedisHelper.Db.KeyDelete(key);
            }
            catch
            {
                // Redis 不可用时静默忽略，下次访问会回源数据库
            }
        }

        /// <summary>
        /// 用于管理员或重要事务数据发生（比如顺出货出完库存已大为变更需要强制告诉显示统计界面要赶紧扔掉当前所维持暂看的结果做全新盘点核算），清除掉对应业务键相关的在列。
        /// </summary>
        /// <param name="pattern">符合带通配符在内键寻址串，比如想删除所有计划明细则穿： "plan:materials:*" 会删除以此打头的所有在库缓存项进行重新换血查库。</param>
        public static void ClearCacheByPattern(string pattern)
        {
            try
            {
                // 使用原厂内建扫描器方法找到该规则所有缓存的标识
                var server = RedisHelper.GetServer();
                foreach (var key in server.Keys(pattern: pattern))
                {
                    // 对每一个匹配扫描到的无情抛弃扫地走清零操作
                    RedisHelper.Db.KeyDelete(key);
                }
            }
            catch
            {
                // 清除非主键非必须保证，出现找不到无响应的情况不打扰主营业处理执行通过。
            }
        }
    }
}
