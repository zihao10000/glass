using GlassWarehouseSystem.Config;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.ModBus;
using System;
using System.Globalization;

namespace GlassWarehouseSystem.Services;

/// <summary>
/// PLC Modbus-TCP 底层通讯客户端。
/// 提供 Bool / Short / Int / Float 四种类型的读写方法，
/// 并通过 EnsureConnected() 实现惰性连接和 5 秒重连节流。
///
/// 新增 TryReadShort / TryReadInt：
///   - 返回 bool 指示本次读取是否成功（包括连接成功且 IsSuccess=true）。
///   - 用于 GlobalSafetyLoop 以区分"PLC 真实返回 0（急停）"和
///     "PLC 未连接时本地返回的默认值 0"，避免误触发软急停。
/// </summary>
/// 
public class PlcClient
{
    private static readonly object SyncRoot = new();

    /// <summary>
    /// 全应用共享的 PlcClient 单例。
    /// 入笼界面、入笼服务、顺移服务、出笼界面、出笼服务全部通过此实例访问 PLC，
    /// 整个程序对同一台 Modbus TCP 服务器只维持一条物理连接，
    /// 彻底避免出现多连接竞争导致的"读取失败/连接被拒绝"问题。
    /// 线程安全由内部 lock(SyncRoot) 保证。
    /// </summary>
    public static PlcClient Instance { get; } = new();

    private ModbusTcpNet? _modbus;
    private DateTime _lastConnectAttempt = DateTime.MinValue;

    /// <summary>
    /// 通讯日志回调。底层是 Action&lt;string&gt; 多播委托，
    /// 调用方应使用 += 订阅、-= 取消订阅；为避免内存泄漏，
    /// 窗口/服务在关闭/销毁时必须用 -= 取消自己的订阅。
    /// </summary>
    public Action<string>? OnLogActivity { get; set; }

    // 
    //  读取方法（失败时返回 false / 默认值，不抛异常）
    // 

    public bool ReadBool(string realAddress)
    {
        lock (SyncRoot)
        {
            // 读取成功不输出逐条日志，避免 WaitForBool 轮询时刷屏；
            // 失败时的详细诊断信息由 TryReadFromModbus 内部记录。
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadBool(address), out bool value))
            {
                return value;
            }
            throw new InvalidOperationException($"读取 [BOOL] {realAddress} 失败");
        }
    }

    public short ReadShort(string realAddress)
    {
        lock (SyncRoot)
        {
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadInt16(address), out short value))
            {
                return value;
            }
            throw new InvalidOperationException($"读取 [SHORT] {realAddress} 失败");
        }
    }

    /// <summary>
    /// 尝试读取 BOOL 值，同时返回是否成功的标志。
    /// 与 ReadBool 的区别：调用方可以据此区分"PLC 返回 false"和"连接失败导致返回默认 false"。
    /// </summary>
    public bool TryReadBool(string realAddress, out bool value)
    {
        lock (SyncRoot)
        {
            // 成功静默；失败日志由 TryReadFromModbus 内部产生。
            return TryReadFromModbus(realAddress, address => _modbus!.ReadBool(address), out value);
        }
    }

    /// <summary>
    /// 尝试读取 SHORT 值，同时返回是否成功的标志。
    /// 与 ReadShort 的区别：调用方可以据此区分"PLC 返回 0"和"连接失败导致返回默认 0"。
    /// </summary>
    public bool TryReadShort(string realAddress, out short value)
    {
        lock (SyncRoot)
        {
            return TryReadFromModbus(realAddress, address => _modbus!.ReadInt16(address), out value);
        }
    }


    public int ReadInt(string realAddress)
    {
        lock (SyncRoot)
        {
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadInt32(address), out int value))
            {
                return value;
            }
            throw new InvalidOperationException($"读取 [INT] {realAddress} 失败");
        }
    }

    public float ReadFloat(string realAddress)
    {
        lock (SyncRoot)
        {
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadFloat(address), out float value))
            {
                return value;
            }
            throw new InvalidOperationException($"读取 [FLOAT] {realAddress} 失败");
        }
    }

    // 
    //  写入方法（失败时静默记录日志，不抛异常）
    // 

    public bool WriteBool(string realAddress, bool value)
    {
        lock (SyncRoot)
        {
            var success = TryWriteToModbus(realAddress, () => _modbus?.Write(realAddress, value));
            OnLogActivity?.Invoke(success
                ? $"写入 [BOOL] {realAddress} 成功: {value}"
                : $"写入 [BOOL] {realAddress} 失败: {value}");
            return success;
        }
    }

    public bool WriteShort(string realAddress, short value)
    {
        lock (SyncRoot)
        {
            var success = TryWriteToModbus(realAddress, () => _modbus?.Write(realAddress, value));
            OnLogActivity?.Invoke(success
                ? $"写入 [SHORT] {realAddress} 成功: {value}"
                : $"写入 [SHORT] {realAddress} 失败: {value}");
            return success;
        }
    }

    public bool WriteInt(string realAddress, int value)
    {
        lock (SyncRoot)
        {
            var success = TryWriteToModbus(realAddress, () => _modbus?.Write(realAddress, value));
            OnLogActivity?.Invoke(success
                ? $"写入 [INT] {realAddress} 成功: {value}"
                : $"写入 [INT] {realAddress} 失败: {value}");
            return success;
        }
    }

    public bool WriteFloat(string realAddress, float value)
    {
        lock (SyncRoot)
        {
            var success = TryWriteToModbus(realAddress, () => _modbus?.Write(realAddress, value));
            OnLogActivity?.Invoke(success
                ? $"写入 [FLOAT] {realAddress} 成功: {value}"
                : $"写入 [FLOAT] {realAddress} 失败: {value}");
            return success;
        }
    }

    // 
    //  内部辅助方法
    // 

    private bool TryReadFromModbus<T>(string realAddress, Func<string, OperateResult<T>>? readFunc, out T value)
    {
        value = default!;
        if (readFunc == null || !CanUseModbusAddress(realAddress))
        {
            OnLogActivity?.Invoke($"读取 {realAddress} 失败（地址非法或读取函数为空）");
            return false;
        }
        if (!EnsureConnected())
        {
            OnLogActivity?.Invoke($"读取 {realAddress} 失败（PLC 连接未建立）");
            return false;
        }

        var op = readFunc(realAddress);
        if (!op.IsSuccess)
        {
            // 暴露 HslCommunication 真实错误信息（含 ErrorCode），便于定位"非法数据地址/超时/校验失败"等
            OnLogActivity?.Invoke($"读取 {realAddress} 失败: ErrorCode={op.ErrorCode}, Msg={op.Message}");
            // 读取失败可能是连接已断开，尝试重置连接以便下次重连
            _modbus?.ConnectClose();
            _modbus = null;
            return false;
        }

        value = op.Content;
        return true;
    }

    private bool TryWriteToModbus(string realAddress, Func<OperateResult?>? writeFunc)
    {
        if (writeFunc == null || !CanUseModbusAddress(realAddress))
        {
            OnLogActivity?.Invoke($"写入 {realAddress} 失败（地址非法或写入函数为空）");
            return false;
        }
        if (!EnsureConnected())
        {
            OnLogActivity?.Invoke($"写入 {realAddress} 失败（PLC 连接未建立）");
            return false;
        }

        var op = writeFunc();
        if (op == null || !op.IsSuccess)
        {
            OnLogActivity?.Invoke($"写入 {realAddress} 失败: ErrorCode={op?.ErrorCode}, Msg={op?.Message}");
            _modbus?.ConnectClose();
            _modbus = null;
            return false;
        }
        return true;
    }

    /// <summary>
    /// 惰性连接，每次调用时检查连接是否可用，最多每 2 秒尝试一次重连。
    /// </summary>
    private bool EnsureConnected()
    {
        // 已有可用连接，直接返回
        if (_modbus != null)
        {
            return true;
        }

        if ((DateTime.UtcNow - _lastConnectAttempt).TotalSeconds < 2)
        {
            return false;
        }

        _lastConnectAttempt = DateTime.UtcNow;

        try
        {
            var ip             = AppConfig.GetString("PlcIp");
            var port           = AppConfig.GetInt("PlcPort");
            var station        = (byte)AppConfig.GetInt("PlcStation");
            var dataFormatText = AppConfig.GetString("PlcDataFormat");
            var startWithZero  = AppConfig.GetBool("PlcAddressStartWithZero");

            if (!Enum.TryParse<DataFormat>(dataFormatText, true, out var dataFormat))
                throw new InvalidOperationException($"PlcDataFormat 配置值无效：{dataFormatText}，应为 ABCD/BADC/CDAB/DCBA");

            var modbus = new ModbusTcpNet(ip, port, station)
            {
                AddressStartWithZero = startWithZero,
                DataFormat           = dataFormat
            };

            var result = modbus.ConnectServer();
            if (!result.IsSuccess)
            {
                modbus.ConnectClose();
                OnLogActivity?.Invoke($"PLC 连接失败（{ip}:{port}）: {result.Message}");
                return false;
            }

            _modbus = modbus;
            OnLogActivity?.Invoke($"PLC 连接成功（{ip}:{port}）。");
            return true;
        }
        catch (Exception ex)
        {
            OnLogActivity?.Invoke($"PLC 连接异常: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 判断地址字符串是否有效（非空即可，支持 "1000"、"4x1000"、"x=4;1000" 等格式）。
    /// </summary>
    private static bool CanUseModbusAddress(string realAddress)
        => !string.IsNullOrWhiteSpace(realAddress);
}
