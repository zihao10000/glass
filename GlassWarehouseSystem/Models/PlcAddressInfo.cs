namespace GlassWarehouseSystem.Models;

/// <summary>
/// PLC 地址信息的内存缓存对象。
/// 在 AppConfig.Initialize() 启动时从 MySQL 的 config 表中读取 Addr_* 键值，
/// 随后常驻于 AppConfig._plcMap 字典中，供 PlcService 在运行时快速查找使用。
/// </summary>
public class PlcAddressInfo
{
    /// <summary>
    /// 逻辑地址名称（键），如 "Addr_In_GlassArrived"、"Addr_SystemStart"。
    /// 业务代码中使用此名称引用，而非硬编码真实 PLC 地址。
    /// </summary>
    public string KeyName { get; set; } = string.Empty;

    /// <summary>
    /// 真实物理地址。
    /// PlcClient 最终使用此值与 PLC 设备通信。
    /// </summary>
    public string RealAddress { get; set; } = string.Empty;

    /// <summary>
    /// 数据类型标识，如 "Int"、"Float"、"Bool"。
    /// 用于将来进行类型安全的读写验证。
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// 中文描述说明，如 "入笼检查到位"。
    /// </summary>
    public string? Description { get; set; }
}
