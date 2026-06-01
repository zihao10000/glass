using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

var connStr = "server=127.0.0.1;port=3306;database=GlassWarehouseDB;user=root;password=123456;";
using var conn = new MySqlConnection(connStr);
conn.Open();

// ============================================================
// 代码中用到的所有配置项（Describe, Type, DefaultValue, Address, 中文说明）
// ============================================================
var required = new List<(string Describe, string Type, string Value, int? Address, string Chinese)>
{
    // ─── 业务参数 ───
    ("MeasureErrorAllowance",     "Float",  "5",       null, "测量误差容限(mm)"),
    ("DataSourceType",            "Int",    "1",       null, "数据来源(0=扫码,1=PLC测量)"),
    ("MaxScanRetryTimes",         "Int",    "3",       null, "最大扫码重试次数"),
    ("GlassSpacing",              "Float",  "10",      null, "玻璃安全间距(mm)"),
    ("PlcPollTimeoutSeconds",     "Int",    "30",      null, "PLC等待反馈超时(秒)"),
    ("InboundRetryMax",           "Int",    "3",       null, "入库重试最大次数"),
    ("InboundLoopIdleDelayMs",    "Int",    "200",     null, "空闲轮询间隔(ms)"),
    ("InboundErrorRetryDelayMs",  "Int",    "1000",    null, "错误重试延迟(ms)"),
    ("InboundNoCageRetryDelayMs", "Int",    "3000",    null, "无笼位等待间隔(ms)"),
    ("InboundPollIntervalMs",     "Int",    "100",     null, "PLC状态轮询间隔(ms)"),

    // ─── Redis 缓存开关 ───
    ("EnableRedis",               "Bool",   "0",       null, "启用Redis缓存(0=禁用直连MySQL,1=启用)"),

    // ─── 出笼服务轮询参数 ───
    ("OutboundPollIntervalMs",      "Int",    "200",     null, "出笼PLC轮询间隔(ms)"),
    ("OutboundPollTimeoutMs",       "Int",    "60000",   null, "出笼PLC等待超时(ms)"),
    ("OutboundSafetyPollIntervalMs","Int",    "200",     null, "出笼急停监控轮询间隔(ms)"),
    ("OutboundRetryMax",            "Int",    "3",       null, "出笼重试最大次数"),

    // ─── PLC 连接参数 ───
    ("PlcIp",                     "String", "192.168.1.8", null, "PLC设备IP地址"),
    ("PlcPort",                   "String", "502",         null, "PLC端口号"),
    ("PlcStation",                "String", "1",           null, "Modbus站号"),
    ("PlcDataFormat",             "String", "CDAB",        null, "数据字节序(CDAB/ABCD)"),
    ("PlcAddressStartWithZero",   "String", "true",        null, "Modbus地址从0开始"),

    // ─── PLC 地址映射（真实 Modbus 保持寄存器地址）───
    ("Addr_GlobalEStop",          "Int",    "100",    100,  "全局急停信号(1=正常,0=急停)"),
    ("Addr_SystemStart",          "Int",    "101",    101,  "系统启动信号(1=上位机就绪)"),
    ("Addr_In_GlassArrived",      "Int",    "102",    102,  "玻璃到位检测(1=到达)"),
    ("Addr_In_GlassLength",       "Float",  "103",    103,  "测量玻璃长度(mm)"),
    ("Addr_In_GlassWidth",        "Float",  "105",    105,  "测量玻璃宽度(mm)"),
    ("Addr_In_TargetCagePos",     "Float",  "107",    107,  "目标层高坐标(写入PLC)"),
    ("Addr_In_PosReached",        "Int",    "108",    108,  "升降机到位反馈(1=到位)"),
    ("Addr_In_EnterCmd",          "Int",    "109",    109,  "横推入笼指令(1=执行)"),
    ("Addr_In_EnterDone",         "Int",    "110",    110,  "横推完成反馈(1=完成)"),
    ("Addr_In_CompareError",      "Int",    "111",    111,  "比对错误码(2=匹配失败)"),
    ("Addr_In_EnterReached",      "Int",    "112",    112,  "入笼到位反馈(1=到位)"),
    ("Addr_CageShiftReq",         "Int",    "1000",   1000, "顺移请求指令"),
    ("Addr_CageShiftDone",        "Int",    "1001",   1001, "顺移完成反馈"),
    ("Addr_ShiftCmd",             "Int",    "1000",   1000, "顺移指令(兼容旧版)"),
    ("Addr_ShiftDone",            "Int",    "1001",   1001, "顺移完成(兼容旧版)"),
};

// ============================================================
// 1. 读取数据库中已有的 Describe
// ============================================================
var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT `Describe` FROM config WHERE `Describe` IS NOT NULL";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var d = reader[0]?.ToString();
        if (!string.IsNullOrWhiteSpace(d)) existing.Add(d);
    }
}

// ============================================================
// 2. 逐条检查：存在则更新 KeyName 中文注释；不存在则插入
// ============================================================
var sb = new StringBuilder();
sb.AppendLine("=== Config Check Result ===");

foreach (var (describe, type, value, address, chinese) in required)
{
    if (existing.Contains(describe))
    {
        // 已存在：更新中文注释
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE config SET `KeyName` = @cn WHERE `Describe` = @desc";
        cmd.Parameters.AddWithValue("@cn", chinese);
        cmd.Parameters.AddWithValue("@desc", describe);
        cmd.ExecuteNonQuery();
        sb.AppendLine($"  [OK]  {describe,-30} <- {chinese}");
    }
    else
    {
        // 不存在：插入
        using var cmd = conn.CreateCommand();
        if (address.HasValue)
        {
            cmd.CommandText = "INSERT INTO config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`) VALUES(@d,@cn,@t,@v,@a,NOW())";
            cmd.Parameters.AddWithValue("@a", address.Value);
        }
        else
        {
            cmd.CommandText = "INSERT INTO config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`) VALUES(@d,@cn,@t,@v,NOW())";
        }
        cmd.Parameters.AddWithValue("@d", describe);
        cmd.Parameters.AddWithValue("@cn", chinese);
        cmd.Parameters.AddWithValue("@t", type);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
        sb.AppendLine($"  [NEW] {describe,-30} <- {chinese}  (Value={value}, Addr={address?.ToString() ?? "-"})");
    }
}

// ============================================================
// 3. 输出最终全表验证
// ============================================================
sb.AppendLine("\n=== Final Config Table ===");
sb.AppendLine($"{"Describe",-32}{"KeyName",-30}{"Value",-12}{"Address",-8}");
sb.AppendLine(new string('-', 82));
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT `Describe`,`KeyName`,`Value`,`Address` FROM config ORDER BY `Describe`";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var d = reader["Describe"]?.ToString() ?? "";
        var k = reader["KeyName"]?.ToString() ?? "";
        var v = reader["Value"]?.ToString() ?? "";
        var a = reader["Address"] == DBNull.Value ? "-" : reader["Address"]?.ToString() ?? "-";
        sb.AppendLine($"{d,-32}{k,-30}{v,-12}{a,-8}");
    }
}

var result = sb.ToString();
System.IO.File.WriteAllText("config_final.txt", result, Encoding.UTF8);
Console.WriteLine(result);
