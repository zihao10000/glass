using GlassWarehouseSystem.LocalConfig;
using GlassWarehouseSystem.Models;
using System;
using System.Data;
using System.Globalization;

namespace GlassWarehouseSystem.Config;

public static class AppConfig
{
    private static readonly object SyncRoot = new();
    private static Dictionary<string, PlcAddressInfo> _plcMap = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _configMap = new(StringComparer.OrdinalIgnoreCase);
    public static LocalSystemSettings LocalSettings { get; private set; } = LocalSystemConfigService.Load();

    public static bool IsInitialized { get; private set; }

    public static void Initialize(IDbConnection db)
    {
        lock (SyncRoot)
        {
            var configMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LoadConfigMap(db, configMap, typeMap);
            var plcMap = BuildPlcMapFromConfig(configMap, typeMap);

            _plcMap = plcMap;
            _configMap = configMap;
            IsInitialized = true;
        }
    }
    public static void ReloadLocalSettings()
    {
        LocalSettings = LocalSystemConfigService.Load();
    }

    public static string GetDatabaseConnectionString()
        => LocalSettings.Database.BuildConnectionString();

    public static string GetRedisConnectionString()
        => LocalSettings.Database.BuildRedisConnectionString();

    public static PlcConnectionConfig? GetPrimaryPlc()
        => LocalSettings.PlcConnections.OrderBy(p => p.Id).FirstOrDefault();

    public static PlcAddressInfo GetPlcAddress(string keyName)
        => _plcMap.TryGetValue(keyName, out var value)
            ? value
            : throw new KeyNotFoundException($"PLC 地址未配置：{keyName}");

    public static float GetFloat(string key)
        => float.Parse(GetValue(key), CultureInfo.InvariantCulture);

    public static int GetInt(string key)
        => int.Parse(GetValue(key), CultureInfo.InvariantCulture);

    public static bool GetBool(string key)
    {
        var value = GetValue(key);
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetString(string key, out string value)
    {
        if (_configMap.TryGetValue(key, out var val))
        {
            value = val;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public static string GetString(string key) => GetValue(key);

    public static string GetStringOrDefault(string key, string defaultValue)
        => _configMap.TryGetValue(key, out var value) ? value : defaultValue;

    private static string GetValue(string key)
    {
        if (_configMap.TryGetValue(key, out var value))
        {
            return value;
        }

        throw new KeyNotFoundException($"业务参数未配置：{key}");
    }

    private static Dictionary<string, PlcAddressInfo> BuildPlcMapFromConfig(
        Dictionary<string, string> configMap, Dictionary<string, string> typeMap)
    {
        var plcMap = new Dictionary<string, PlcAddressInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in configMap)
        {
            if (!kv.Key.StartsWith("Addr_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 数据类型必须从数据库 config 表的 Type 列获取，不允许硬编码
            if (!typeMap.TryGetValue(kv.Key, out var dbType) || string.IsNullOrWhiteSpace(dbType))
            {
                throw new InvalidOperationException(
                    $"PLC 地址 '{kv.Key}' 在 config 表中缺少 Type 列值，" +
                    $"请在数据库 config 表中为该行补充 Type（Bool/Int/Float）。");
            }

            plcMap[kv.Key] = new PlcAddressInfo
            {
                KeyName = kv.Key,
                RealAddress = kv.Value,
                DataType = dbType,
                Description = "From config"
            };
        }

        return plcMap;
    }

    private static void LoadConfigMap(IDbConnection db, Dictionary<string, string> configMap, Dictionary<string, string> typeMap)
    {
        // 1) Preferred schema: KeyName（英文键名）+ Value
        try
        {
            using var cfgCmd = db.CreateCommand();
            cfgCmd.CommandText = "SELECT `KeyName`, `Value`, `Address`, `Type` FROM config";
            using var reader = cfgCmd.ExecuteReader();
            while (reader.Read())
            {
                var key = Convert.ToString(reader["KeyName"]);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    var val = Convert.ToString(reader["Value"]) ?? string.Empty;
                    var typeVal = reader["Type"] != DBNull.Value ? Convert.ToString(reader["Type"]) : null;
                    if (!string.IsNullOrWhiteSpace(typeVal))
                        typeMap[key] = typeVal!;
                    if (key.StartsWith("Addr_", StringComparison.OrdinalIgnoreCase))
                    {
                        // Addr_* 优先使用 Address 列，但 Address 为空或非数字时回退到 Value 列。
                        // 注意：Address=0 是合法的 Modbus 寄存器地址，不可排除
                        if (reader["Address"] != DBNull.Value &&
                            int.TryParse(Convert.ToString(reader["Address"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var addrInt) &&
                            addrInt >= 0)
                        {
                            configMap[key] = addrInt.ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            configMap[key] = val;
                        }
                    }
                    else
                    {
                        configMap[key] = val;
                    }
                }
            }

            if (configMap.Count > 0)
            {
                return;
            }
        }
        catch
        {
            // Try Describe schema.
        }

        // 2) Alternate key-value schema: Describe + Value（兼容旧版数据，Describe 为英文键名）
        try
        {
            using var cfgCmd = db.CreateCommand();
            cfgCmd.CommandText = "SELECT `Describe`, `Value`, `Address`, `Type` FROM config";
            using var reader = cfgCmd.ExecuteReader();
            while (reader.Read())
            {
                var key = Convert.ToString(reader["Describe"]);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    var val = Convert.ToString(reader["Value"]) ?? string.Empty;
                    var typeVal = reader["Type"] != DBNull.Value ? Convert.ToString(reader["Type"]) : null;
                    if (!string.IsNullOrWhiteSpace(typeVal))
                        typeMap[key] = typeVal!;
                    if (key.StartsWith("Addr_", StringComparison.OrdinalIgnoreCase))
                    {
                        if (reader["Address"] != DBNull.Value &&
                            int.TryParse(Convert.ToString(reader["Address"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var addrInt) &&
                            addrInt >= 0)
                        {
                            configMap[key] = addrInt.ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            configMap[key] = val;
                        }
                    }
                    else
                    {
                        configMap[key] = val;
                    }
                }
            }

            if (configMap.Count > 0)
            {
                return;
            }
        }
        catch
        {
        }

        // 3) Legacy schema fallback.
        using var legacyCmd = db.CreateCommand();
        legacyCmd.CommandText = "SELECT MeasureErrorAllowance, DataSourceType, MaxScanRetryTimes, CageInnerDistanceK, GlassSpacing FROM Config WHERE ConfigID = 1";
        using var legacyReader = legacyCmd.ExecuteReader();
        if (legacyReader.Read())
        {
            configMap["MeasureErrorAllowance"] = Convert.ToString(legacyReader["MeasureErrorAllowance"], CultureInfo.InvariantCulture) ?? "0";
            configMap["DataSourceType"] = Convert.ToString(legacyReader["DataSourceType"], CultureInfo.InvariantCulture) ?? "0";
            configMap["MaxScanRetryTimes"] = Convert.ToString(legacyReader["MaxScanRetryTimes"], CultureInfo.InvariantCulture) ?? "0";
            configMap["CageInnerDistanceK"] = Convert.ToString(legacyReader["CageInnerDistanceK"], CultureInfo.InvariantCulture) ?? "0";
            configMap["GlassSpacing"] = Convert.ToString(legacyReader["GlassSpacing"], CultureInfo.InvariantCulture) ?? "0";
        }
    }
}
