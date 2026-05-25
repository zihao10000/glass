-- ═══════════════════════════════════════════════════════════════════════════
-- fix_config_keyname.sql
--
-- 目的：
--   1. 修复数据库 config 表中 Describe 与 KeyName 列值互换的问题
--   2. 将 Describe 列统一更新为中文说明，KeyName 列保持英文键名
--   3. 补全缺失的 Addr_* PLC 地址条目
--
-- 正确的列设计：
--   KeyName  = 英文键名（代码查找用，如 Addr_In_GlassArrived）
--   Describe = 中文说明（人工查阅用，如 "玻璃到位检测(1=到达)"）
--
-- AppConfig.cs（已同步修改）优先读 KeyName 列作为字典键。
--
-- 幂等性：可多次执行，不会产生重复行或意外修改已正确的数据。
-- ═══════════════════════════════════════════════════════════════════════════

USE GlassWarehouseDB;

-- ─────────────────────────────────────────────────────────────────────────
-- 第一步：修复 Describe / KeyName 互换问题
--
-- 判断依据：
--   若 Describe 是英文键名（Addr_ 前缀 或 已知业务参数名），
--   但 KeyName 不是同名英文键名 → 说明两列已互换，执行 SWAP。
-- ─────────────────────────────────────────────────────────────────────────

-- 1a. PLC 地址类（Addr_ 前缀）：Describe 是英文但 KeyName 不是 → 互换
UPDATE config
SET    `KeyName`  = `Describe`,
       `Describe` = `KeyName`
WHERE  `Describe` LIKE 'Addr_%'
  AND (`KeyName` IS NULL OR `KeyName` NOT LIKE 'Addr_%');

-- 1b. 业务参数类：Describe 是英文键名但 KeyName 不是同名 → 互换
UPDATE config
SET    `KeyName`  = `Describe`,
       `Describe` = `KeyName`
WHERE  `Describe` IN (
         'MeasureErrorAllowance','DataSourceType','MaxScanRetryTimes',
         'GlassSpacing','PlcPollTimeoutSeconds','InboundRetryMax',
         'InboundLoopIdleDelayMs','InboundErrorRetryDelayMs',
         'InboundNoCageRetryDelayMs','InboundPollIntervalMs',
         'PlcIp','PlcPort','PlcStation','PlcDataFormat','PlcAddressStartWithZero'
       )
  AND (KeyName IS NULL OR KeyName NOT IN (
         'MeasureErrorAllowance','DataSourceType','MaxScanRetryTimes',
         'GlassSpacing','PlcPollTimeoutSeconds','InboundRetryMax',
         'InboundLoopIdleDelayMs','InboundErrorRetryDelayMs',
         'InboundNoCageRetryDelayMs','InboundPollIntervalMs',
         'PlcIp','PlcPort','PlcStation','PlcDataFormat','PlcAddressStartWithZero'
       ));

-- ─────────────────────────────────────────────────────────────────────────
-- 第二步：将 Describe 列更新为统一的中文说明
--   （对已正确的行：KeyName=英文，但 Describe 仍为英文 → 改为中文）
-- ─────────────────────────────────────────────────────────────────────────

-- 业务参数
UPDATE config SET `Describe` = '测量误差容限(mm)'            WHERE `KeyName` = 'MeasureErrorAllowance';
UPDATE config SET `Describe` = '数据来源(0=扫码,1=PLC测量)' WHERE `KeyName` = 'DataSourceType';
UPDATE config SET `Describe` = '最大扫码重试次数'             WHERE `KeyName` = 'MaxScanRetryTimes';
UPDATE config SET `Describe` = '玻璃安全间距(mm)'             WHERE `KeyName` = 'GlassSpacing';
UPDATE config SET `Describe` = 'PLC等待反馈超时(秒)'          WHERE `KeyName` = 'PlcPollTimeoutSeconds';
UPDATE config SET `Describe` = '入库重试最大次数'             WHERE `KeyName` = 'InboundRetryMax';
UPDATE config SET `Describe` = '空闲轮询间隔(ms)'             WHERE `KeyName` = 'InboundLoopIdleDelayMs';
UPDATE config SET `Describe` = '错误重试延迟(ms)'             WHERE `KeyName` = 'InboundErrorRetryDelayMs';
UPDATE config SET `Describe` = '无笼位等待间隔(ms)'           WHERE `KeyName` = 'InboundNoCageRetryDelayMs';
UPDATE config SET `Describe` = 'PLC状态轮询间隔(ms)'          WHERE `KeyName` = 'InboundPollIntervalMs';

-- PLC 连接参数
UPDATE config SET `Describe` = 'PLC设备IP地址'               WHERE `KeyName` = 'PlcIp';
UPDATE config SET `Describe` = 'PLC端口号'                   WHERE `KeyName` = 'PlcPort';
UPDATE config SET `Describe` = 'Modbus站号'                  WHERE `KeyName` = 'PlcStation';
UPDATE config SET `Describe` = '数据字节序(CDAB/ABCD等)'      WHERE `KeyName` = 'PlcDataFormat';
UPDATE config SET `Describe` = 'Modbus地址是否从0开始'        WHERE `KeyName` = 'PlcAddressStartWithZero';

-- PLC 地址映射
UPDATE config SET `Describe` = '全局急停信号(1=正常,0=急停)' WHERE `KeyName` = 'Addr_GlobalEStop';
UPDATE config SET `Describe` = '系统启动信号(1=上位机就绪)'  WHERE `KeyName` = 'Addr_SystemStart';
UPDATE config SET `Describe` = '玻璃到位检测(1=到达)'        WHERE `KeyName` = 'Addr_In_GlassArrived';
UPDATE config SET `Describe` = '测量玻璃长度(mm)'            WHERE `KeyName` = 'Addr_In_GlassLength';
UPDATE config SET `Describe` = '测量玻璃宽度(mm)'            WHERE `KeyName` = 'Addr_In_GlassWidth';
UPDATE config SET `Describe` = '目标层高坐标(写入PLC)'       WHERE `KeyName` = 'Addr_In_TargetCagePos';
UPDATE config SET `Describe` = '升降机到位反馈(1=到位)'      WHERE `KeyName` = 'Addr_In_PosReached';
UPDATE config SET `Describe` = '横推入笼指令(1=执行)'        WHERE `KeyName` = 'Addr_In_EnterCmd';
UPDATE config SET `Describe` = '横推完成反馈(1=完成)'        WHERE `KeyName` = 'Addr_In_EnterDone';
UPDATE config SET `Describe` = '比对错误码(2=匹配失败)'      WHERE `KeyName` = 'Addr_In_CompareError';
UPDATE config SET `Describe` = '入笼到位反馈(1=到位)'        WHERE `KeyName` = 'Addr_In_EnterReached';
UPDATE config SET `Describe` = '顺移请求指令'                WHERE `KeyName` = 'Addr_CageShiftReq';
UPDATE config SET `Describe` = '顺移完成反馈'                WHERE `KeyName` = 'Addr_CageShiftDone';
UPDATE config SET `Describe` = '顺移指令(兼容旧版)'          WHERE `KeyName` = 'Addr_ShiftCmd';
UPDATE config SET `Describe` = '顺移完成(兼容旧版)'          WHERE `KeyName` = 'Addr_ShiftDone';

-- ─────────────────────────────────────────────────────────────────────────
-- 第三步：补全缺失的 Addr_* 条目
--
-- KeyName  = 英文键名（代码读取）
-- Describe = 中文说明（人工查阅）
-- Address  = Modbus 保持寄存器地址（整型，AppConfig 优先读此列）
-- Value    = 地址字符串（与 Address 一致，作为回退）
--
-- ⚠ 注意：Address 为默认占位值，请对照实际 PLC 地址表确认后修改。
-- ─────────────────────────────────────────────────────────────────────────

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_GlobalEStop','全局急停信号(1=正常,0=急停)','Int','100',100,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_GlobalEStop');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_SystemStart','系统启动信号(1=上位机就绪)','Int','101',101,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_SystemStart');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_GlassArrived','玻璃到位检测(1=到达)','Int','102',102,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_GlassArrived');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_GlassLength','测量玻璃长度(mm)','Float','103',103,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_GlassLength');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_GlassWidth','测量玻璃宽度(mm)','Float','105',105,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_GlassWidth');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_TargetCagePos','目标层高坐标(写入PLC)','Float','107',107,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_TargetCagePos');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_PosReached','升降机到位反馈(1=到位)','Int','108',108,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_PosReached');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_EnterCmd','横推入笼指令(1=执行)','Int','109',109,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_EnterCmd');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_EnterDone','横推完成反馈(1=完成)','Int','110',110,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_EnterDone');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_CompareError','比对错误码(2=匹配失败)','Int','111',111,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_CompareError');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_EnterReached','入笼到位反馈(1=到位)','Int','112',112,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_EnterReached');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_CageShiftReq','顺移请求指令','Int','1000',1000,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_CageShiftReq');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_CageShiftDone','顺移完成反馈','Int','1001',1001,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_CageShiftDone');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_ShiftCmd','顺移指令(兼容旧版)','Int','1000',1000,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_ShiftCmd');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_ShiftDone','顺移完成(兼容旧版)','Int','1001',1001,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_ShiftDone');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`UpdateTime`)
  SELECT 'EnableActivityLog','窗口日志写入数据库(1=启用,0=禁用)','bool','0',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'EnableActivityLog');

-- ─────────────────────────────────────────────────────────────────────────
-- 第四步：验证——输出全表最终状态
-- ─────────────────────────────────────────────────────────────────────────

SELECT
  `KeyName`   AS '英文键名(代码读取)',
  `Describe`  AS '中文说明',
  `Type`      AS '类型',
  `Value`     AS '值',
  `Address`   AS 'Modbus地址'
FROM config
ORDER BY
  CASE WHEN `KeyName` LIKE 'Addr_%' THEN 1 ELSE 0 END,
  `KeyName`;

SELECT 'fix_config_keyname.sql 执行完成' AS status;
