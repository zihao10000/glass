using System;
using System.Linq;
using System.Management;

namespace GlassWarehouseSystem.Services
{
    /// <summary>
    /// 【系统底层设施接入支持库】专门调借 WMI（Windows Management Instrumentation）核心接口做微软下设设备身份查询调取功能模块。
    /// 配合权限审核等对底层依赖安全唯一可信源身份提取的工作层。
    /// </summary>
    public static class HardwareIdService
    {
        /// <summary>
        /// 基于底层的 Win32_Processor 系统表利用查询来提取目前电脑上CPU硬烙印在出场芯片的唯一辨识号：ProcessorId。
        /// </summary>
        /// <returns>返回去除掉头尾空白空格之后的这串无懈可击身份串</returns>
        /// <exception cref="InvalidOperationException">一旦电脑拒绝提供或者没有支持该WMI服务的服务等原因会导致强行跑出失败异常警告调用者未抓到</exception>
        public static string GetCpuProcessorId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                using var results = searcher.Get();

                // 解析取出对应键的实际字符表示串
                var processorId = results
                    .Cast<ManagementObject>()
                    .Select(mo => mo["ProcessorId"]?.ToString())
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

                if (string.IsNullOrWhiteSpace(processorId))
                    throw new InvalidOperationException("未获取到 CPU ProcessorId。");

                return processorId.Trim();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("读取 CPU 序列号失败（WMI）。", ex);
            }
        }
    }
}
