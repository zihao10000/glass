using System;

namespace GlassWarehouseSystem.Services
{
    /// <summary>
    /// 【安全保护与版权控制】当前运行宿主物理机器的身份序列号强绑定审核服务。
    /// 确保此工厂管理程序的复制品副本被随意被带去其他的工控主板设备里运行时会启动自毁保护机制报错退出。
    ///  - 它主要用作基础的软件商交付授权安全锁定防护措施。
    /// </summary>
    public static class HardwareBindingService
    {
        /// <summary>
        /// 将本系统允许准入的唯一受信任硬件编号记录存放入该长字符里。
        /// 若有需多设备共用准入时使用';'半角分割打进多串（如： "178A..;BFEB..."）。
        /// 这些合法码只能由运维实施技术人员预先使用获取序列号的小工具或者在初次部署时手动查看到改修重编加入白名单进行编译定版散发。
        /// </summary>
        private const string AllowedCpuProcessorIds = "178BFBFF00A70F52";

        /// <summary>
        /// 在 APP 启动的第一时间入口处被调用。用来审查跑在此刻CPU核心与授权码本记录是否有异。
        /// </summary>
        /// <exception cref="InvalidOperationException">如果当前机器未得到认证允许，直接给外层抛出非常规错断送截断APP应用生成阻止生命启动循环引发抛窗告警强制宕死不让被无授权人使用</exception>
        public static void EnsureHardwareMatches()
        {
            // 通过 WMI 取硬件信息的私有底层帮助方法截取读取此时该实体承载该程序的本机内含有的不可变唯一真实底层基准机器识别值序列串。
            var current = HardwareIdService.GetCpuProcessorId();

            var allowed = AllowedCpuProcessorIds
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // 让当前读到的东西与许可明文遍历匹配。如果有交集证明身份可靠直接通过返回给框架。
            foreach (var id in allowed)
            {
                if (string.Equals(current, id, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            // 无匹配即当内鬼机器，报错并终止这套系统的非授权运作。把当朝读取信息带上发还便于给厂长展示报告申请授权增加用参考。
            throw new InvalidOperationException($"硬件不匹配。当前 CPU 序列号: {current}");
        }
    }
}
