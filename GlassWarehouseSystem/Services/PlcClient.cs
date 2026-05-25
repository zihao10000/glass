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

    private ModbusTcpNet? _modbus;
    private DateTime _lastConnectAttempt = DateTime.MinValue;

    public Action<string>? OnLogActivity { get; set; }

    // 
    //  读取方法（失败时返回 false / 默认值，不抛异常）
    // 

    public bool ReadBool(string realAddress)
    {
        lock (SyncRoot)
        {
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadBool(address), out bool value))
            {
                OnLogActivity?.Invoke($"读取 [BOOL] {realAddress} 成功: {value}");
                return value;
            }
            var msg = $"读取 [BOOL] {realAddress} 失败（连接不可用或通信错误）";
            OnLogActivity?.Invoke(msg);
            throw new InvalidOperationException(msg);
        }
    }

    public short ReadShort(string realAddress)
    {
        lock (SyncRoot)
        {
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadInt16(address), out short value))
            {
                OnLogActivity?.Invoke($"读取 [SHORT] {realAddress} 成功: {value}");
                return value;
            }
            var msg = $"读取 [SHORT] {realAddress} 失败（连接不可用或通信错误）";
            OnLogActivity?.Invoke(msg);
            throw new InvalidOperationException(msg);
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
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadBool(address), out value))
            {
                OnLogActivity?.Invoke($"读取 [BOOL] {realAddress} 成功: {value}");
                return true;
            }
            OnLogActivity?.Invoke($"读取 [BOOL] {realAddress} 失败（连接不可用或通信错误）");
            return false;
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
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadInt16(address), out value))
            {
                OnLogActivity?.Invoke($"读取 [SHORT] {realAddress} 成功: {value}");
                return true;
            }
            OnLogActivity?.Invoke($"读取 [SHORT] {realAddress} 失败（连接不可用或通信错误）");
            return false;
        }
    }


    public int ReadInt(string realAddress)
    {
        lock (SyncRoot)
        {
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadInt32(address), out int value))
            {
                OnLogActivity?.Invoke($"读取 [INT] {realAddress} 成功: {value}");
                return value;
            }
            var msg = $"读取 [INT] {realAddress} 失败（连接不可用或通信错误）";
            OnLogActivity?.Invoke(msg);
            throw new InvalidOperationException(msg);
        }
    }

    public float ReadFloat(string realAddress)
    {
        lock (SyncRoot)
        {
            if (TryReadFromModbus(realAddress, address => _modbus!.ReadFloat(address), out float value))
            {
                OnLogActivity?.Invoke($"读取 [FLOAT] {realAddress} 成功: {value}");
                return value;
            }
            var msg = $"读取 [FLOAT] {realAddress} 失败（连接不可用或通信错误）";
            OnLogActivity?.Invoke(msg);
            throw new InvalidOperationException(msg);
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
        if (readFunc == null || !CanUseModbusAddress(realAddress) || !EnsureConnected())
        {
            return false;
        }

        var op = readFunc(realAddress);
        if (!op.IsSuccess)
        {
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
        if (writeFunc == null || !CanUseModbusAddress(realAddress) || !EnsureConnected())
        {
            return false;
        }

        var op = writeFunc();
        if (op == null || !op.IsSuccess)
        {
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
