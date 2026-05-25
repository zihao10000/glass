using System.Globalization;
using GlassWarehouseSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace GlassWarehouseSystem.Services;

/// <summary>
/// 从 MySQL <c>config</c> 表按 <see cref="KeyName"/> 解析 Modbus 地址字符串（与 HslCommunication 中地址格式一致，如 "200"）。
/// 优先使用 <c>Value</c> 列；为空时回退 <c>Address</c> 整型转字符串。
/// </summary>
public static class HmiPlcConfigService
{
    // --- BOOL keys (写 1 / 写 0) ---
    public const string KeyTransXPlus = "Addr_Trans_X+";
    public const string KeyTransXMinus = "Addr_Trans_X-";

    public const string KeyMeasureXPlus = "Addr_Measure_X+";
    public const string KeyMeasureXMinus = "Addr_Measure_X-";

    public const string KeyMeasureZPlus = "Addr_Measure_Z+";
    public const string KeyMeasureZMinus = "Addr_Measure_Z-";

    public const string KeyTestEnable = "Addr_Test_Enable";
    public const string KeyMeasureModeEnable = "Addr_MeasureMode_Enable";
    public const string KeyXPosEnable = "Addr_XPos_Enable"; // 目前未要求写入/读取

    public const string KeyLinkedXPlus = "Addr_Linked_X+";
    public const string KeyLinkedXMinus = "Addr_Linked_X-";
    public const string KeyLinkedZPlus = "Addr_Linked_Z+";
    public const string KeyLinkedZMinus = "Addr_Linked_Z-";

    public const string KeyCageALiftUp = "Addr_CageA_Lift_Up";
    public const string KeyCageALiftDown = "Addr_CageA_Lift_Down";
    public const string KeyCageAXMinus = "Addr_CageA_X-";
    public const string KeyCageAXPlus = "Addr_CageA_X+";
    public const string KeyCageAYPlus = "Addr_CageA_Y+";
    public const string KeyCageAYMinus = "Addr_CageA_Y-";
    public const string KeyCageAXSelectConveyor = "Addr_CageA_X_Select_Conveyor";
    public const string KeyCageAXSelectMeasure = "Addr_CageA_X_Select_Measure";
    public const string KeyCageAXSelectOneWay = "Addr_CageA_X_Select_OneWay";
    public const string KeyCageAXSelectLinked = "Addr_CageA_X_Select_Linked";

    public const string KeyCageBLiftUp = "Addr_CageB_Lift_Up";
    public const string KeyCageBLiftDown = "Addr_CageB_Lift_Down";
    public const string KeyCageBXMinus = "Addr_CageB_X-";
    public const string KeyCageBXPlus = "Addr_CageB_X+";
    public const string KeyCageBYPlus = "Addr_CageB_Y+";
    public const string KeyCageBYMinus = "Addr_CageB_Y-";
    public const string KeyCageBXSelectConveyor = "Addr_CageB_X_Select_Conveyor";
    public const string KeyCageBXSelectMeasure = "Addr_CageB_X_Select_Measure";
    public const string KeyCageBXSelectOneWay = "Addr_CageB_X_Select_OneWay";
    public const string KeyCageBXSelectLinked = "Addr_CageB_X_Select_Linked";

    public const string KeyOutManualXMinus = "Addr_Out_Manual_X-";
    public const string KeyOutManualXPlus = "Addr_Out_Manual_X+";
    public const string KeyOutXMinus = "Addr_Out_X-";
    public const string KeyOutXPlus = "Addr_Out_X+";
    public const string KeyOutXSelectOneWay = "Addr_Out_X_Select_OneWay";
    public const string KeyOutXSelectOut = "Addr_Out_X_Select_Out";
    public const string KeyOutXSelectLinked = "Addr_Out_X_Select_Linked";

    // --- FLOAT keys ---
    public const string KeyTransPos = "Addr_Trans_Pos";
    public const string KeyMeasurePos = "Addr_Measure_Pos";
    public const string KeyOneWayPos = "Addr_OneWay_Pos";
    public const string KeyCageAYRealtime = "Addr_CageA_Y_Pos";
    public const string KeyCageBYRealtime = "Addr_CageB_Y_Pos";
    public const string KeyCageAYSet = "Addr_CageA_Y_Set";
    public const string KeyCageBYSet = "Addr_CageB_Y_Set";
    public const string KeyCageAXSet = "Addr_CageA_X_Set";
    public const string KeyCageBXSet = "Addr_CageB_X_Set";
    public const string KeyOutXSet = "Addr_Out_X_Set";
    public const string KeyZPos = "Addr_Z_Pos";
    public const string KeyZOriginSet = "Addr_ZOrigin_Set";
    public const string KeyYPosSet = "Addr_YPos_Set";
    public const string KeyXPosSet = "Addr_XPos_Set";

    /// <summary>
    /// 尝试解析 Modbus 地址；失败返回 null。
    /// </summary>
    public static string? TryGetModbusAddress(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            return null;

        using var context = new WarehouseDbContext();
        var conn = context.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
            conn.Open();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT `Value`, `Address` FROM `config` WHERE `KeyName` = @key LIMIT 1";
            var p = cmd.CreateParameter();
            p.ParameterName = "@key";
            p.Value = keyName.Trim();
            cmd.Parameters.Add(p);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            var valueObj = reader["Value"];
            var addrObj = reader["Address"];

            if (valueObj != null && valueObj != DBNull.Value)
            {
                var s = Convert.ToString(valueObj, CultureInfo.InvariantCulture)?.Trim();
                if (!string.IsNullOrEmpty(s))
                    return s;
            }

            if (addrObj != null && addrObj != DBNull.Value)
            {
                if (int.TryParse(Convert.ToString(addrObj, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
                    return a.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }
        finally
        {
            if (!wasOpen)
                conn.Close();
        }
    }

    /// <summary>
    /// 同时读取地址与类型（<c>config.Type</c>）。地址解析规则同 <see cref="TryGetModbusAddress"/>。
    /// </summary>
    public static (string? Address, string? Type) TryGetModbusAddressAndType(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            return (null, null);

        using var context = new WarehouseDbContext();
        var conn = context.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
            conn.Open();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT `Type`, `Value`, `Address` FROM `config` WHERE `KeyName` = @key LIMIT 1";
            var p = cmd.CreateParameter();
            p.ParameterName = "@key";
            p.Value = keyName.Trim();
            cmd.Parameters.Add(p);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return (null, null);

            string? type = null;
            var typeObj = reader["Type"];
            if (typeObj != null && typeObj != DBNull.Value)
                type = Convert.ToString(typeObj, CultureInfo.InvariantCulture)?.Trim();

            string? address = null;
            var valueObj = reader["Value"];
            var addrObj = reader["Address"];

            if (valueObj != null && valueObj != DBNull.Value)
            {
                var s = Convert.ToString(valueObj, CultureInfo.InvariantCulture)?.Trim();
                if (!string.IsNullOrEmpty(s))
                    address = s;
            }

            if (string.IsNullOrEmpty(address) && addrObj != null && addrObj != DBNull.Value)
            {
                if (int.TryParse(Convert.ToString(addrObj, CultureInfo.InvariantCulture), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var a))
                    address = a.ToString(CultureInfo.InvariantCulture);
            }

            return (address, type);
        }
        finally
        {
            if (!wasOpen)
                conn.Close();
        }
    }
}
