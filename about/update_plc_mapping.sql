13USE GlassWarehouseDB;

-- 7. PLC 地址映射?(PlcAddressMapping)
CREATE TABLE IF NOT EXISTS PlcAddressMapping (
    KeyName VARCHAR(100) PRIMARY KEY COMMENT '逻辑地址名称，如：Addr_In_GlassArrived',
    RealAddress VARCHAR(100) COMMENT '真实物理地址，如：DB10.DBW0',
    DataType VARCHAR(50) COMMENT '数据类型，如：Int, Float, Bool',
    Description VARCHAR(200) COMMENT '地址描述，如：入笼检查到?
) COMMENT='PLC物理地址映射配置?;

-- 插入默认的入笼设?PLC 映射数据 (以模拟地坢为初始?
INSERT INTO PlcAddressMapping (KeyName, RealAddress, DataType, Description) VALUES
('Addr_SystemStart', 'M0.0', 'Int', '系统启动信号 (A)'),
('Addr_In_GlassArrived', 'M0.1', 'Int', '玻璃到达棢?(B)'),
('Addr_In_GlassLength', 'MD10', 'Float', '上报玻璃长度 (C)'),
('Addr_In_GlassWidth', 'MD14', 'Float', '上报玻璃宽度 (D)'),
('Addr_In_TargetCagePos', 'MD18', 'Float', '下发目标笼位?(E)'),
('Addr_In_PosReached', 'M0.2', 'Int', '笼移动到位信?(F)'),
('Addr_In_EnterCmd', 'M0.3', 'Int', '下发入笼指令 (G)'),
('Addr_In_EnterDone', 'M0.4', 'Int', '入笼完成反馈 (H)'),
('Addr_In_CompareError', 'M0.5', 'Int', '(2=)'),
('Addr_In_EnterReached', 'M0.6', 'Int', '(1=)');

