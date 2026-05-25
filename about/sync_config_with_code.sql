-- ═══════════════════════════════════════════════════════════════════════════
-- sync_config_with_code.sql
--
-- 目的：
--   将 config 表与 C# 代码实际使用的配置项完全对齐。
--
-- 关键约定：
--   PLC 地址以 Address 列（INT）为准，代码中 AppConfig.GetPlcAddress() 优先读取
--   Address 列并将其作为 PlcClient 的真实寄存器/线圈地址。
--   Value 列保持与 Address 一致（字符串形式），作为 Address 为空时的回退。
--
-- 地址分配（Modbus 协议，Bool 为线圈空间，Int/Float 为保持寄存器空间）：
--   全局信号       : 99 (GlobalEStop)、101 (SystemStart[未使用])
--   入笼 Bool 信号 : 110~116  (线圈)
--   入笼 Float/Int : 5000~5007 (保持寄存器，Float 占 2 个寄存器)
--   出笼 Bool 信号 : 100~104  (线圈，已有生产地址)
--   出笼 Float/Int : 150~188  (保持寄存器，已有生产地址)
--   顺移信号       : 130~131  (线圈)
--
-- 操作内容：
--   第一步：统一已有入笼地址的 Address/Value 列
--   第二步：补全代码中使用但 config 表中缺失的 Addr_* 入笼地址
--   第三步：补全代码中使用但 config 表中缺失的 Addr_* 出笼地址
--   第四步：补全缺失的业务参数
--   第五步：修正已有条目的 Type 值（Int → Bool）
--   第六步：标记 config 表中存在但代码未使用的地址
--
-- 幂等性：可多次执行，INSERT 使用 NOT EXISTS 防重，UPDATE 使用条件赋值。
-- ═══════════════════════════════════════════════════════════════════════════

SET NAMES utf8mb4;
USE GlassWarehouseDB;

-- ─────────────────────────────────────────────────────────────────────────
-- 第一步：统一已有入笼地址的 Address 和 Value 列
--
-- 确保所有 Addr_* 条目的 Value 列与 Address 列一致（字符串化的整数），
-- 消除早期脚本中 Value='M0.1' 之类的旧式西门子地址写法，
-- 项目实际使用 Modbus 整数地址（Address 列）。
-- ─────────────────────────────────────────────────────────────────────────

-- 全局（Addr_GlobalEStop 使用 99 避免与 Addr_Out_StartCmd=100 冲突）
UPDATE config SET `Address` = 99, `Value` = '99', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_GlobalEStop' AND (`Address` IS NULL OR `Address` != 99);

UPDATE config SET `Address` = 101, `Value` = '101', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_SystemStart' AND (`Address` IS NULL OR `Address` != 101);

-- 入笼 Bool 信号 (110~119)
UPDATE config SET `Address` = 110, `Value` = '110', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_GlassArrived' AND (`Address` IS NULL OR `Address` != 110);

UPDATE config SET `Address` = 111, `Value` = '111', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_PosReached' AND (`Address` IS NULL OR `Address` != 111);

UPDATE config SET `Address` = 112, `Value` = '112', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_EnterCmd' AND (`Address` IS NULL OR `Address` != 112);

UPDATE config SET `Address` = 113, `Value` = '113', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_EnterDone' AND (`Address` IS NULL OR `Address` != 113);

UPDATE config SET `Address` = 114, `Value` = '114', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_CompareError' AND (`Address` IS NULL OR `Address` != 114);

UPDATE config SET `Address` = 115, `Value` = '115', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_ScanOK' AND (`Address` IS NULL OR `Address` != 115);

UPDATE config SET `Address` = 116, `Value` = '116', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_EnterReached' AND (`Address` IS NULL OR `Address` != 116);

-- 入笼 Float/Int 寄存器 (5000~5019, Float 占 2 个寄存器)
UPDATE config SET `Address` = 5000, `Value` = '5000', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_GlassLength' AND (`Address` IS NULL OR `Address` != 5000);

UPDATE config SET `Address` = 5002, `Value` = '5002', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_GlassWidth' AND (`Address` IS NULL OR `Address` != 5002);

UPDATE config SET `Address` = 5004, `Value` = '5004', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_TargetCagePos' AND (`Address` IS NULL OR `Address` != 5004);

UPDATE config SET `Address` = 5006, `Value` = '5006', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_CurrentPos' AND (`Address` IS NULL OR `Address` != 5006);

-- 顺移 Bool 信号 (130~139)
UPDATE config SET `Address` = 130, `Value` = '130', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_CageShiftReq' AND (`Address` IS NULL OR `Address` != 130);

UPDATE config SET `Address` = 131, `Value` = '131', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_CageShiftDone' AND (`Address` IS NULL OR `Address` != 131);

UPDATE config SET `Address` = 130, `Value` = '130', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_ShiftCmd' AND (`Address` IS NULL OR `Address` != 130);

UPDATE config SET `Address` = 131, `Value` = '131', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_ShiftDone' AND (`Address` IS NULL OR `Address` != 131);

-- ─────────────────────────────────────────────────────────────────────────
-- 第二步：补全入笼（Inbound）缺失的 PLC 地址
--
-- InboundService 使用但 config 表中尚未收录的地址：
--   Addr_In_CurrentPos  — ReadFloat   传送台当前位置（TryMapPlcPositionToLayer）
--   Addr_In_ScanOK      — WriteBool   扫码/测量完成信号（MeasurementLoop）
-- ─────────────────────────────────────────────────────────────────────────

-- Addr_In_CurrentPos: Float 保持寄存器 5006-5007
INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_CurrentPos','入笼-传送台当前位置(mm,ReadFloat)','Float','5006',5006,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_CurrentPos');

-- 修正 Addr_In_CurrentPos 描述（早期版本为英文）
UPDATE config SET `Describe` = '入笼-传送台当前位置(mm,ReadFloat)', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_CurrentPos' AND `Describe` NOT LIKE '%传送台%';

-- Addr_In_ScanOK: Bool 线圈 115
INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_In_ScanOK','入笼-扫码/测量完成信号(WriteBool)','Bool','115',115,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_In_ScanOK');

-- ─────────────────────────────────────────────────────────────────────────
-- 第三步：补全出笼（Outbound）全部 PLC 地址
--
-- OutboundService 使用的所有 Addr_Out_* 地址在 config 表中均不存在。
-- ExecuteSingleOutbound 流程：
--   1. WriteFloat Addr_Out_MoveDistance → WriteBool Addr_Out_StartCmd=1
--   2. WaitForBool Addr_Out_PosReached=1（笼到位）
--   3. WriteBool Addr_Out_PopCmd=1 → WaitForBool Addr_Out_PopDone=1（出片完成）
--   4. WaitForInt Addr_Out_Status=1（正常完成）/ =2（故障）
-- ManualOutboundAsync 额外读取：
--   ReadFloat Addr_Out_CurrentPos（当前笼位坐标）
--
-- 出笼 Bool 线圈: 120~129
-- 出笼 Float/Int 保持寄存器: 5020~5039
-- ─────────────────────────────────────────────────────────────────────────

-- 出笼 Bool 信号（保留已有生产地址，仅在条目不存在时插入默认值）
INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_Out_StartCmd','出笼-启动指令(WriteBool,1=启动)','Bool','100',100,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_Out_StartCmd');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_Out_PosReached','出笼-笼到位信号(ReadBool,1=到位)','Bool','102',102,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_Out_PosReached');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_Out_PopCmd','出笼-推片指令(WriteBool,1=推片)','Bool','103',103,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_Out_PopCmd');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_Out_PopDone','出笼-出片完成信号(ReadBool,1=完成)','Bool','104',104,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_Out_PopDone');

-- 出笼 Float/Int 寄存器（保留已有生产地址）
INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_Out_CurrentPos','出笼-当前笼位坐标(mm,ReadFloat)','Float','188',188,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_Out_CurrentPos');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_Out_MoveDistance','出笼-移动距离(mm,带符号,WriteFloat)','Float','158',158,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_Out_MoveDistance');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`Address`,`UpdateTime`)
  SELECT 'Addr_Out_Status','出笼-运行状态(ReadInt,1=正常,2=故障)','Int','156',156,NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'Addr_Out_Status');

-- ─────────────────────────────────────────────────────────────────────────
-- 第四步：补全缺失的业务参数
--
-- OutboundService.LoadInStockMaterialsSortedAsync() 使用：
--   OutSortField — 出笼排序字段（Length / Width，默认 Length）
--   OutSortDir   — 出笼排序方向（Asc / Desc，默认 Desc）
-- ─────────────────────────────────────────────────────────────────────────

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`UpdateTime`)
  SELECT 'OutSortField','出笼排序字段(Length=长边/Width=短边)','String','Length',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'OutSortField');

INSERT INTO config (`KeyName`,`Describe`,`Type`,`Value`,`UpdateTime`)
  SELECT 'OutSortDir','出笼排序方向(Desc=从大到小/Asc=从小到大)','String','Desc',NOW()
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM config WHERE `KeyName` = 'OutSortDir');

-- ─────────────────────────────────────────────────────────────────────────
-- 第五步：修正已有 Addr_* 条目的 Type 值
--
-- 代码中使用 ReadBool/WriteBool/TryReadBool/WaitForBool 的地址，
-- 在早期 SQL 脚本中被错误标记为 Type='Int'，修正为 'Bool'。
-- Type 列供 AppConfig.BuildPlcMapFromConfig 存入 PlcAddressInfo.DataType，
-- 虽然不影响运行时读写方法的选择，但应与代码调用方式保持文档一致。
-- ─────────────────────────────────────────────────────────────────────────

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_GlobalEStop'     AND `Type` != 'Bool';

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_GlassArrived' AND `Type` != 'Bool';

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_PosReached'   AND `Type` != 'Bool';

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_EnterCmd'     AND `Type` != 'Bool';

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_EnterDone'    AND `Type` != 'Bool';

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_CompareError' AND `Type` != 'Bool';

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_CageShiftReq'   AND `Type` != 'Bool';

-- 出笼 Bool 类型修正
UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_Out_StartCmd'   AND BINARY `Type` != 'Bool';

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_Out_PosReached' AND BINARY `Type` != 'Bool';

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_Out_PopCmd'     AND BINARY `Type` != 'Bool';

UPDATE config SET `Type` = 'Bool', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_Out_PopDone'    AND BINARY `Type` != 'Bool';

-- ─────────────────────────────────────────────────────────────────────────
-- 第六步：标记 config 表中存在但代码未使用的 Addr_* 地址
--
-- 这些地址保留在表中（不删除），在 Describe 中添加"[未使用]"标注，
-- 便于 DBA 和运维人员识别哪些地址可安全忽略或在未来版本中清理。
-- ─────────────────────────────────────────────────────────────────────────

-- Addr_SystemStart：无任何 Service/Window 引用
UPDATE config SET `Describe` = '[未使用] 系统启动信号 - 当前代码未引用', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_SystemStart'
  AND `Describe` NOT LIKE '%未使用%';

-- Addr_In_EnterReached：代码使用 Addr_In_EnterDone 而非此地址
UPDATE config SET `Describe` = '[未使用] 入笼到位反馈 - 代码使用 Addr_In_EnterDone 替代', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_In_EnterReached'
  AND `Describe` NOT LIKE '%未使用%';

-- Addr_CageShiftDone：ShiftService 仅写 Addr_CageShiftReq，不读 Done
UPDATE config SET `Describe` = '[未使用] 顺移完成反馈 - ShiftService 仅写 Req 不读 Done', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_CageShiftDone'
  AND `Describe` NOT LIKE '%未使用%';

-- Addr_ShiftCmd：旧版兼容键，主代码路径使用 Addr_CageShiftReq
UPDATE config SET `Describe` = '[未使用] 顺移指令(旧版) - 主路径使用 Addr_CageShiftReq', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_ShiftCmd'
  AND `Describe` NOT LIKE '%未使用%';

-- Addr_ShiftDone：旧版兼容键，代码无引用
UPDATE config SET `Describe` = '[未使用] 顺移完成(旧版) - 代码无引用', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_ShiftDone'
  AND `Describe` NOT LIKE '%未使用%';

-- Addr_Out_TargetPos：代码使用 Addr_Out_MoveDistance（差值）替代绝对坐标
UPDATE config SET `Describe` = '[未使用] 出笼目标层坐标 - 代码使用 Addr_Out_MoveDistance 替代', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_Out_TargetPos'
  AND `Describe` NOT LIKE '%未使用%';

-- Addr_Out_PrintTrigger：打印已改为 C# 侧静默打印，不再通过 PLC 信号触发
UPDATE config SET `Describe` = '[未使用] 打印标签触发 - 已改为C#侧静默打印', `UpdateTime` = NOW()
WHERE `KeyName` = 'Addr_Out_PrintTrigger'
  AND `Describe` NOT LIKE '%未使用%';

-- ─────────────────────────────────────────────────────────────────────────
-- 验证：输出全表最终状态
-- ─────────────────────────────────────────────────────────────────────────

SELECT
  `KeyName`                              AS '键名(代码读取)',
  `Describe`                             AS '说明',
  `Type`                                 AS '数据类型',
  `Address`                              AS 'PLC地址(主)',
  `Value`                                AS '值/回退地址'
FROM config
WHERE `KeyName` LIKE 'Addr_%'
ORDER BY
  CASE
    WHEN `KeyName` LIKE 'Addr_In_%'  THEN 1
    WHEN `KeyName` LIKE 'Addr_Out_%' THEN 2
    WHEN `KeyName` LIKE 'Addr_%'     THEN 3
  END,
  `Address`;

SELECT '--- 业务参数 ---' AS '';
SELECT
  `KeyName`   AS '键名',
  `Describe`  AS '说明',
  `Type`      AS '类型',
  `Value`     AS '值'
FROM config
WHERE `KeyName` NOT LIKE 'Addr_%'
ORDER BY `KeyName`;

SELECT 'sync_config_with_code.sql 执行完成' AS status;
