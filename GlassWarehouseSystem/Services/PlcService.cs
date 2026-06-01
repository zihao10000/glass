using GlassWarehouseSystem.Config;

namespace GlassWarehouseSystem.Services;

/// <summary>
/// 【PLC通讯代理层】PLC 服务类，是业务代码与底层 PLC 通讯之间的桥梁。
/// 
/// 核心职责：
///   接收业务层传入的【逻辑地址名称】（如 "Addr_In_GlassArrived"），
///   通过 AppConfig 将其翻译为【真实物理地址】（如 "DB10.DBW0"），
///   再转发给底层 PlcClient 执行实际的读/写操作。
/// 
/// 调用链路示例：
///   InboundService → _plc.ReadInt("Addr_In_GlassArrived")
///     → PlcService.ReadInt("Addr_In_GlassArrived")
///       → AppConfig.GetPlcAddress("Addr_In_GlassArrived").RealAddress = "10"
///         → PlcClient.ReadInt("DB10.DBW0")
///           → 返回 PLC 寄存器中的值
/// 

/// </summary>
public class PlcService
{
    private readonly PlcClient _client;

    public Action<string>? OnLogActivity
    {
        get => _client.OnLogActivity;
        set => _client.OnLogActivity = value;
    }

    public PlcService(PlcClient client)
    {
        _client = client;
    }
    public void ReadBool_Hmi(string realAddres)
    {
        _client.ReadBool(realAddres);
    }
    public void ReadFloat_Hmi(string realAddres)
    {
        _client.ReadFloat(realAddres);
    }
    public void Write_Hmi(string realAddress, float value)
    {
        _client.WriteFloat(realAddress, value);
    }
    public void Write_Hmi(string realAddress, bool value)
    {
        _client.WriteBool(realAddress, value);
    }

    public bool ReadBool(string keyName)
        => _client.ReadBool(AppConfig.GetPlcAddress(keyName).RealAddress);

    public short ReadShort(string keyName)
        => _client.ReadShort(AppConfig.GetPlcAddress(keyName).RealAddress);

    /// <summary>
    /// 尝试读取 BOOL 值，返回是否读取成功。
    /// 成功 = PLC 已连接且 Modbus 操作 IsSuccess，此时 value 为真实线圈值。
    /// 失败 = PLC 未连接或通信错误，value 为 default(false)，不代表 PLC 真实状态。
    /// </summary>
    public bool TryReadBool(string keyName, out bool value)
        => _client.TryReadBool(AppConfig.GetPlcAddress(keyName).RealAddress, out value);

    /// <summary>
    /// 尝试读取 SHORT 值，返回是否读取成功。
    /// 成功 = PLC 已连接且 Modbus 操作 IsSuccess，此时 value 为真实寄存器值。
    /// 失败 = PLC 未连接或通信错误，value 为 default(0)，不代表 PLC 真实状态。
    /// </summary>
    public bool TryReadShort(string keyName, out short value)
        => _client.TryReadShort(AppConfig.GetPlcAddress(keyName).RealAddress, out value);

    /// <summary>
    /// 通过逻辑名读取 PLC 整数值。
    /// </summary>
    /// <param name="keyName">逻辑地址名（如 "Addr_In_GlassArrived"）</param>
    /// <returns>PLC 寄存器中的整数值</returns>
    public int ReadInt(string keyName)
        => _client.ReadInt(AppConfig.GetPlcAddress(keyName).RealAddress);

    /// <summary>
    /// 通过逻辑名读取 PLC 浮点数值。
    /// </summary>
    public float ReadFloat(string keyName)
        => _client.ReadFloat(AppConfig.GetPlcAddress(keyName).RealAddress);

    public bool WriteBool(string keyName, bool value)
        => _client.WriteBool(AppConfig.GetPlcAddress(keyName).RealAddress, value);

    public bool WriteShort(string keyName, short value)
        => _client.WriteShort(AppConfig.GetPlcAddress(keyName).RealAddress, value);

    /// <summary>
    /// 通过逻辑名向 PLC 写入整数值。
    /// </summary>
    /// <param name="keyName">逻辑地址名（如 "Addr_In_EnterCmd"）</param>
    /// <param name="value">要写入的值</param>
    public bool WriteInt(string keyName, int value)
        => _client.WriteInt(AppConfig.GetPlcAddress(keyName).RealAddress, value);

    /// <summary>
    /// 通过逻辑名向 PLC 写入浮点数值。
    /// </summary>
    public bool WriteFloat(string keyName, float value)
        => _client.WriteFloat(AppConfig.GetPlcAddress(keyName).RealAddress, value);

}
