-- ═══════════════════════════════════════════════════════════════════════════
-- 数据库对齐迁移脚本  v2
-- 作用：将 MySQL GlassWarehouseDB 现有表结构与 C# EF Core 模型完全对齐
-- 兼容：MySQL 8.0+（使用 ADD COLUMN IF NOT EXISTS）
-- 说明：对已有数据安全，不删除任何列，仅新增缺失列并修正类型
-- ═══════════════════════════════════════════════════════════════════════════
USE GlassWarehouseDB;

-- ═══════════════════════════════════════════════════════════════════════════
-- 1. Orders 表
--    缺失：ID INT AUTO_INCREMENT（C# Order.Id 映射列，Pomelo 需要唯一自增列）
-- ═══════════════════════════════════════════════════════════════════════════
ALTER TABLE Orders
  ADD COLUMN IF NOT EXISTS ID INT NOT NULL AUTO_INCREMENT UNIQUE
    COMMENT '自增行号（业务主键为 OrderID 字符串列）' FIRST;

-- ═══════════════════════════════════════════════════════════════════════════
-- 2. Materials 表
--    缺失 1：ID INT AUTO_INCREMENT（C# Material.Id）
--    缺失 2：SlotNo INT —— FinalizeInboundTransaction 写入同层入库顺序号
--            缺少此列将导致 SaveChanges() 抛出 Unknown column 异常
-- ═══════════════════════════════════════════════════════════════════════════
ALTER TABLE Materials
  ADD COLUMN IF NOT EXISTS ID INT NOT NULL AUTO_INCREMENT UNIQUE
    COMMENT '自增行号（业务主键为 GlassID）' FIRST,
  ADD COLUMN IF NOT EXISTS SlotNo INT NULL
    COMMENT '层内槽位号：同一层已入库数量+1，由 FinalizeInboundTransaction 计算写入'
    AFTER CurrentLayer;

-- ═══════════════════════════════════════════════════════════════════════════
-- 3. Cages 表
--    缺失 1：ID INT AUTO_INCREMENT（C# Cage.Id）
--    缺失 2：Space DECIMAL(10,2) —— CageFinder 计算 PLC 坐标：
--            TargetPosValue = GridStartCoord + (LayerNo - 1) * Space
--    缺失 3：IsOnline BIT —— CageRepository.GetOnlineCagesWithLayers() 过滤条件
--            缺少此列 EF 查询会映射成 NULL，所有笼均视为在线（业务可接受但结构不对齐）
-- ═══════════════════════════════════════════════════════════════════════════
ALTER TABLE Cages
  ADD COLUMN IF NOT EXISTS ID INT NOT NULL AUTO_INCREMENT UNIQUE
    COMMENT '自增行号（业务主键为 CageCode）' FIRST,
  ADD COLUMN IF NOT EXISTS Space DECIMAL(10,2) NULL
    COMMENT '层间距（mm），PLC坐标计算：TargetPos = GridStartCoord + (LayerNo-1)*Space'
    AFTER GridStartCoord,
  ADD COLUMN IF NOT EXISTS IsOnline BIT NULL DEFAULT 1
    COMMENT '笼是否在线（1或NULL=在线可参与分配；0=下线不参与）'
    AFTER UpdateTime;

-- ═══════════════════════════════════════════════════════════════════════════
-- 4. Layers 表
--    缺失 1：ID INT AUTO_INCREMENT（C# Layer.Id）
--    缺失 2：RemainingLength DOUBLE —— CageFinder 核心判断字段
--            缺少此列导致 CageFinder 永远找不到可用层（所有层 remaining=null < required）
--    缺失 3：GlassSpec VARCHAR(100) —— CageFinder 同规格优先分配
--            缺少此列导致第一步（同规格匹配）永远不命中，退化为全空层分配
--    缺失 4：Length DOUBLE —— 物理层长度上限
--    缺失 5：Width DOUBLE —— 当前层玻璃宽度快照（看板展示用）
--    缺失 6：StartNo INT —— 层内起始片号
--    缺失 7：Space DOUBLE —— 层级间隙（优先于 Cage.InboundGap 使用）
-- ═══════════════════════════════════════════════════════════════════════════
ALTER TABLE Layers
  ADD COLUMN IF NOT EXISTS ID INT NOT NULL AUTO_INCREMENT UNIQUE
    COMMENT '自增行号（业务主键为 LayerID）' FIRST,
  ADD COLUMN IF NOT EXISTS Length DOUBLE NULL
    COMMENT '物理层长度上限（mm）'
    AFTER PLCAddress,
  ADD COLUMN IF NOT EXISTS RemainingLength DOUBLE NULL
    COMMENT '该层剩余可用长度（mm）；初始=笼Length；每入库一片扣减 Width+GlassSpacing'
    AFTER Length,
  ADD COLUMN IF NOT EXISTS GlassSpec VARCHAR(100) NULL
    COMMENT '当前层规格字符串（如"1200×800"）；NULL 或空字符串表示该层完全空闲'
    AFTER RemainingLength,
  ADD COLUMN IF NOT EXISTS Width DOUBLE NULL
    COMMENT '当前层最后入库玻璃的宽度快照（mm），用于看板展示'
    AFTER GlassSpec,
  ADD COLUMN IF NOT EXISTS StartNo INT NULL
    COMMENT '层内起始片号'
    AFTER Width,
  ADD COLUMN IF NOT EXISTS Space DOUBLE NULL
    COMMENT '该层独立间隙设定（mm），优先于笼级 Cage.InboundGap 使用'
    AFTER StartNo;

-- 对已有层：将 RemainingLength 和 Length 初始化为所属笼子的 Length
-- 仅补全 NULL 行，不覆盖已有数值
UPDATE Layers l
  JOIN Cages c ON l.CageID = c.CageCode
  SET l.RemainingLength = CAST(c.Length AS DOUBLE),
      l.Length          = CAST(c.Length AS DOUBLE)
WHERE l.RemainingLength IS NULL
  AND c.Length IS NOT NULL;

-- ═══════════════════════════════════════════════════════════════════════════
-- 5. Logs 表
--    缺失：Type VARCHAR(50) —— FinalizeInboundTransaction 写入 "Info"/"Warning"/"Error"
--          缺少此列将导致 INSERT INTO Logs(..., Type) 抛出 Unknown column 异常
-- ═══════════════════════════════════════════════════════════════════════════
ALTER TABLE Logs
  ADD COLUMN IF NOT EXISTS Type VARCHAR(50) NULL
    COMMENT '日志级别：Info / Warning / Error'
    AFTER RecordTime;

-- ═══════════════════════════════════════════════════════════════════════════
-- 6. Config 表（同时兼顾旧宽表格式 和 新键值行格式）
--
--    背景：Windows MySQL 大小写不敏感，"Config" == "config"，
--    代码中存在两个模型映射到同一张表：
--      Models.Config   → ToTable("Config")  旧宽表（ConfigID=1单行）
--      ConfigRow       → ToTable("config")  新键值行（每参数一行）
--
--    策略：在原宽表基础上增加键值行所需列，两种读取方式均可正常工作：
--      AppConfig.LoadConfigMap() 优先读 Describe+Value 列（新格式），
--      若为空则回退读取 ConfigID=1 的旧宽表列（兼容旧数据）
--
--    缺失 1：ConfigID 需要 AUTO_INCREMENT（AppConfigSeeder INSERT 不指定 ID）
--    缺失 2：Describe VARCHAR(100)
--    缺失 3：KeyName  VARCHAR(100)
--    缺失 4：Type     VARCHAR(50)
--    缺失 5：Value    VARCHAR(200)
--    缺失 6：Address  INT
-- ═══════════════════════════════════════════════════════════════════════════
ALTER TABLE Config
  MODIFY COLUMN ConfigID INT NOT NULL AUTO_INCREMENT,
  ADD COLUMN IF NOT EXISTS Describe VARCHAR(100) NULL
    COMMENT '参数名称键（如 MeasureErrorAllowance、Addr_In_GlassLength）'
    AFTER ConfigID,
  ADD COLUMN IF NOT EXISTS KeyName VARCHAR(100) NULL
    COMMENT '参数键名（与 Describe 冗余，兼容 AppConfig 旧版 KeyName 读取路径）'
    AFTER Describe,
  ADD COLUMN IF NOT EXISTS `Type` VARCHAR(50) NULL
    COMMENT '参数数据类型标识：Int / Float / Bool'
    AFTER KeyName,
  ADD COLUMN IF NOT EXISTS `Value` VARCHAR(200) NULL
    COMMENT '参数值（字符串存储，由 AppConfig.GetFloat/GetInt 进行类型转换）'
    AFTER `Type`,
  ADD COLUMN IF NOT EXISTS Address INT NULL
    COMMENT 'PLC 地址整型值（Addr_* 前缀条目专用，非零时优先于 Value 列使用）'
    AFTER `Value`;

-- 确保旧宽表格式的 ConfigID=1 行存在（AppConfig 旧格式回退读取使用）
INSERT INTO Config (ConfigID)
  SELECT 1 FROM DUAL
  WHERE NOT EXISTS (SELECT 1 FROM Config WHERE ConfigID = 1);

-- ═══════════════════════════════════════════════════════════════════════════
-- 6b. Config 表 — 补全所有代码中用到的配置项
--
-- 使用 INSERT ... SELECT ... WHERE NOT EXISTS 模式：
--   • 按 Describe 列检查是否已存在，存在则跳过，不存在才插入
--   • 不修改已有值，不产生重复行
--   • 可安全重复执行（幂等），无论运行多少次结果一致
--
-- 覆盖范围（与 AppConfigSeeder.RequiredEntries 完全对齐）：
--   ① 业务参数        10 条
--   ② PLC 连接参数     5 条
--   ③ PLC 地址映射    15 条（含 ShiftService 旧版兼容键）
-- ═══════════════════════════════════════════════════════════════════════════

-- ① 业务参数 ──────────────────────────────────────────────────────────────
INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'MeasureErrorAllowance','MeasureErrorAllowance','Float','5',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='MeasureErrorAllowance');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'DataSourceType','DataSourceType','Int','1',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='DataSourceType');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'MaxScanRetryTimes','MaxScanRetryTimes','Int','3',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='MaxScanRetryTimes');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'GlassSpacing','GlassSpacing','Float','10',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='GlassSpacing');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'PlcPollTimeoutSeconds','PlcPollTimeoutSeconds','Int','30',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='PlcPollTimeoutSeconds');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'InboundRetryMax','InboundRetryMax','Int','3',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='InboundRetryMax');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'InboundLoopIdleDelayMs','InboundLoopIdleDelayMs','Int','200',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='InboundLoopIdleDelayMs');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'InboundErrorRetryDelayMs','InboundErrorRetryDelayMs','Int','1000',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='InboundErrorRetryDelayMs');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'InboundNoCageRetryDelayMs','InboundNoCageRetryDelayMs','Int','3000',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='InboundNoCageRetryDelayMs');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'InboundPollIntervalMs','InboundPollIntervalMs','Int','100',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='InboundPollIntervalMs');

-- 出笼服务轮询参数
INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'OutboundPollIntervalMs','OutboundPollIntervalMs','Int','200',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `KeyName`='OutboundPollIntervalMs');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'OutboundPollTimeoutMs','OutboundPollTimeoutMs','Int','60000',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `KeyName`='OutboundPollTimeoutMs');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'OutboundSafetyPollIntervalMs','OutboundSafetyPollIntervalMs','Int','200',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `KeyName`='OutboundSafetyPollIntervalMs');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'OutboundRetryMax','OutboundRetryMax','Int','3',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `KeyName`='OutboundRetryMax');

-- ② PLC 连接参数 ──────────────────────────────────────────────────────────
INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'PlcIp','PlcIp','String','192.168.1.8',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='PlcIp');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'PlcPort','PlcPort','String','502',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='PlcPort');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'PlcStation','PlcStation','String','1',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='PlcStation');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'PlcDataFormat','PlcDataFormat','String','CDAB',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='PlcDataFormat');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`UpdateTime`)
  SELECT 'PlcAddressStartWithZero','PlcAddressStartWithZero','String','true',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='PlcAddressStartWithZero');

-- ③ PLC 地址映射（Addr_* 前缀）────────────────────────────────────────────
-- Address=0 时 AppConfig 回退使用 Value 列的字符串地址
INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_SystemStart','Addr_SystemStart','Int','M0.0',0,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_SystemStart');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_GlassArrived','Addr_In_GlassArrived','Int','M0.1',1,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_In_GlassArrived');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_GlassLength','Addr_In_GlassLength','Float','MD10',10,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_In_GlassLength');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_GlassWidth','Addr_In_GlassWidth','Float','MD14',14,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_In_GlassWidth');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_TargetCagePos','Addr_In_TargetCagePos','Float','MD18',18,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_In_TargetCagePos');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_PosReached','Addr_In_PosReached','Int','M0.2',2,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_In_PosReached');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_EnterCmd','Addr_In_EnterCmd','Int','M0.3',3,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_In_EnterCmd');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_EnterDone','Addr_In_EnterDone','Int','M0.4',4,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_In_EnterDone');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_CompareError','Addr_In_CompareError','Int','M0.5',5,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_In_CompareError');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_EnterReached','Addr_In_EnterReached','Int','M0.6',6,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_In_EnterReached');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_GlobalEStop','Addr_GlobalEStop','Int','M0.7',7,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_GlobalEStop');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_CageShiftReq','Addr_CageShiftReq','Int','M1.0',8,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_CageShiftReq');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_CageShiftDone','Addr_CageShiftDone','Int','M1.1',9,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_CageShiftDone');

-- 旧版 ShiftService 兼容键（catch 分支回退）
INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_ShiftCmd','Addr_ShiftCmd','Int','M1.0',8,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_ShiftCmd');

INSERT INTO Config (`Describe`,`KeyName`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_ShiftDone','Addr_ShiftDone','Int','M1.1',9,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM Config WHERE `Describe`='Addr_ShiftDone');

-- ═══════════════════════════════════════════════════════════════════════════
-- 7. MaterialsHistory 表（与 Materials 表保持列对齐）
--    缺失：SlotNo INT
-- ═══════════════════════════════════════════════════════════════════════════
ALTER TABLE MaterialsHistory
  ADD COLUMN IF NOT EXISTS SlotNo INT NULL
    COMMENT '层内槽位号（归档时从 Materials 同步）'
    AFTER CurrentLayer;

-- ═══════════════════════════════════════════════════════════════════════════
-- 验证：查看迁移后的表结构
-- ═══════════════════════════════════════════════════════════════════════════
-- SHOW COLUMNS FROM Orders;
-- SHOW COLUMNS FROM Materials;
-- SHOW COLUMNS FROM Cages;
-- SHOW COLUMNS FROM Layers;
-- SHOW COLUMNS FROM Logs;
-- SHOW COLUMNS FROM Config;
-- SHOW COLUMNS FROM MaterialsHistory;

SELECT 'migrate_schema_v2.sql 执行完成' AS status;
