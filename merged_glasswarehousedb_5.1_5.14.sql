CREATE DATABASE IF NOT EXISTS `GlassWarehouseDB` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE `GlassWarehouseDB`;
SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS `layers`,`materialshistory`,`materials`,`orders`,`cages`,`logs`,`config`,`plcaddressmapping`,`controlplcuser`;

CREATE TABLE `cages` (
  `ID` int NOT NULL AUTO_INCREMENT,
  `CageCode` varchar(50) NOT NULL,
  `EquipmentName` varchar(100) DEFAULT NULL,
  `CageType` int DEFAULT NULL,
  `LayerCount` int DEFAULT NULL,
  `Length` decimal(10,2) DEFAULT NULL,
  `Width` decimal(10,2) DEFAULT NULL,
  `Height` decimal(10,2) DEFAULT NULL,
  `ZeroCoordinate` decimal(10,2) DEFAULT NULL,
  `NegativeLimit` decimal(10,2) DEFAULT NULL,
  `PositiveLimit` decimal(10,2) DEFAULT NULL,
  `InboundGap` decimal(8,2) DEFAULT NULL,
  `OutboundGap` decimal(8,2) DEFAULT NULL,
  `CageSequenceNo` int DEFAULT NULL,
  `XCoordinate` decimal(10,2) DEFAULT NULL,
  `YCoordinate` decimal(10,2) DEFAULT NULL,
  `TransitionLayerNo` int DEFAULT NULL,
  `GridInitSeqNo` int DEFAULT NULL,
  `GridStartCoord` decimal(10,2) DEFAULT NULL,
  `Space` decimal(10,3) DEFAULT NULL,
  `IsOnline` bit(1) DEFAULT NULL,
  `IsOneWay` bit(1) DEFAULT NULL,
  `LocationType` varchar(10) DEFAULT NULL,
  `UpdateTime` datetime DEFAULT NULL,
  PRIMARY KEY (`CageCode`),
  UNIQUE KEY `UK_cages_ID` (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `orders` (
  `ID` int NOT NULL AUTO_INCREMENT,
  `OrderID` varchar(50) NOT NULL,
  `OrderNo` varchar(50) DEFAULT NULL,
  `FlowCardNo` varchar(50) DEFAULT NULL,
  `CustomerName` varchar(100) DEFAULT NULL,
  `Status` int DEFAULT NULL,
  `CreateTime` datetime DEFAULT NULL,
  `DeliveryDate` datetime DEFAULT NULL,
  `TotalCount` int DEFAULT NULL,
  PRIMARY KEY (`OrderID`),
  UNIQUE KEY `UK_orders_ID` (`ID`),
  KEY `IX_orders_FlowCardNo` (`FlowCardNo`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `materials` (
  `ID` int NOT NULL AUTO_INCREMENT,
  `GlassID` varchar(50) NOT NULL,
  `OrderID` varchar(50) DEFAULT NULL,
  `OrderName` varchar(100) DEFAULT NULL,
  `ProductName` varchar(100) DEFAULT NULL,
  `Length` decimal(10,2) DEFAULT NULL,
  `Width` decimal(10,2) DEFAULT NULL,
  `Thickness` decimal(5,2) DEFAULT NULL,
  `Status` int DEFAULT NULL,
  `IsDamaged` bit(1) DEFAULT NULL,
  `CurrentCage` varchar(20) DEFAULT NULL,
  `CurrentLayer` int DEFAULT NULL,
  `SlotNo` int DEFAULT NULL,
  `InboundTime` datetime DEFAULT NULL,
  `OutboundTime` datetime DEFAULT NULL,
  `ImportTime` datetime DEFAULT NULL,
  `ErrorMessage` varchar(200) DEFAULT NULL,
  `GroupID` varchar(200) DEFAULT NULL,
  PRIMARY KEY (`GlassID`),
  UNIQUE KEY `UK_materials_ID` (`ID`),
  KEY `IX_materials_OrderID` (`OrderID`),
  CONSTRAINT `FK_materials_orders` FOREIGN KEY (`OrderID`) REFERENCES `orders` (`OrderID`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO `materials` VALUES (43, 'GLS-2026-0015', 'ORD-2026-01', NULL, '中空玻璃', 1600.00, 800.00, 12.00, 0, b'0', '', NULL, NULL, '2026-05-11 11:24:03', NULL, '2026-04-22 15:45:51', NULL, 'GLS-2026-0015+GLS-2026-0016');
INSERT INTO `materials` VALUES (42, 'GLS-2026-0010', 'ORD-2026-03', NULL, 'LOW-E玻璃', 2600.00, 1300.00, 6.00, 0, NULL, '', NULL, NULL, '2026-05-11 11:24:10', NULL, '2026-04-22 15:45:51', NULL, NULL);
INSERT INTO `materials` VALUES (41, 'GLS-2026-0009', 'ORD-2026-03', NULL, 'LOW-E玻璃', 2600.00, 1300.00, 6.00, 0, NULL, '', NULL, NULL, '2026-05-11 14:54:47', NULL, '2026-04-22 15:45:51', NULL, NULL);
INSERT INTO `materials` VALUES (40, 'GLS-2026-0008', 'ORD-2026-02', NULL, '夹胶玻璃', 2200.00, 1100.00, 10.00, 0, NULL, '', NULL, NULL, '2026-05-11 14:54:53', NULL, '2026-04-22 15:45:51', NULL, NULL);
INSERT INTO `materials` VALUES (38, 'GLS-2026-0006', 'ORD-2026-02', NULL, '超白玻璃', 3000.00, 1500.00, 8.00, 0, NULL, '', NULL, NULL, '2026-05-11 11:24:13', NULL, '2026-04-22 15:45:51', NULL, NULL);
INSERT INTO `materials` VALUES (37, 'GLS-2026-0005', 'ORD-2026-01', NULL, '中空玻璃', 1600.00, 800.00, 12.00, 0, NULL, '', NULL, NULL, '2026-05-11 14:55:12', NULL, '2026-04-22 15:45:51', NULL, 'GLS-2026-0004+GLS-2026-0005');
INSERT INTO `materials` VALUES (36, 'GLS-2026-0004', 'ORD-2026-01', NULL, '中空玻璃', 1600.00, 800.00, 12.00, 0, NULL, '', NULL, NULL, '2026-05-11 15:14:09', NULL, '2026-04-22 15:45:51', NULL, 'GLS-2026-0004+GLS-2026-0005');
INSERT INTO `materials` VALUES (12, '85669874', '5587', NULL, 'Glass_2', 1250.00, 750.00, 5.00, 0, b'0', NULL, NULL, NULL, '2026-04-16 16:03:55', NULL, NULL, NULL, NULL);
INSERT INTO `materials` VALUES (11, '9431106', '1145', NULL, 'Glass', 800.00, 600.00, 5.00, 0, b'0', NULL, NULL, NULL, '2026-04-16 15:58:08', NULL, NULL, NULL, NULL);
INSERT INTO `materials` VALUES (7, 'GLS-T-1005', 'ORD-T-003', NULL, 'Low-E', 1100.00, 650.00, 5.00, 0, b'0', NULL, NULL, NULL, '2026-04-16 16:04:06', NULL, NULL, NULL, NULL);
INSERT INTO `materials` VALUES (5, 'GLS-T-2002', 'ORD-T-002', NULL, '夹胶玻璃', 1500.00, 900.00, 6.00, 0, b'0', 'B', NULL, NULL, '2026-04-30 10:20:07', NULL, NULL, NULL, NULL);
INSERT INTO `materials` VALUES (4, 'GLS-T-2001', 'ORD-T-001', NULL, '钢化白玻', 1200.00, 800.00, 5.00, 0, b'0', NULL, NULL, NULL, '2026-04-16 15:58:40', NULL, NULL, NULL, NULL);
INSERT INTO `materials` VALUES (2, 'GLS-T-1002', 'ORD-T-001', NULL, '钢化白玻', 1200.00, 800.00, 5.00, 0, b'0', 'B', 0, NULL, '2026-04-16 15:58:23', NULL, NULL, NULL, NULL);
INSERT INTO `materials` VALUES (1, 'GLS-T-1001', 'ORD-T-001', NULL, '钢化白玻', 1200.00, 800.00, 5.00, 0, b'0', 'B', 0, NULL, '2026-04-16 15:58:24', NULL, NULL, NULL, NULL);


CREATE TABLE `materialshistory` (
  `HistoryID` int NOT NULL AUTO_INCREMENT,
  `GlassID` varchar(50) NOT NULL,
  `OrderID` varchar(50) DEFAULT NULL,
  `OrderName` varchar(100) DEFAULT NULL,
  `ProductName` varchar(100) DEFAULT NULL,
  `Length` decimal(10,2) DEFAULT NULL,
  `Width` decimal(10,2) DEFAULT NULL,
  `Thickness` decimal(5,2) DEFAULT NULL,
  `Status` int DEFAULT NULL,
  `IsDamaged` bit(1) DEFAULT NULL,
  `CurrentCage` varchar(20) DEFAULT NULL,
  `CurrentLayer` int DEFAULT NULL,
  `SlotNo` int DEFAULT NULL,
  `InboundTime` datetime DEFAULT NULL,
  `OutboundTime` datetime DEFAULT NULL,
  `ArchiveTime` datetime DEFAULT NULL,
  `OutboundBatchNo` varchar(50) DEFAULT NULL,
  `GroupID` varchar(200) DEFAULT NULL,
  `ErrorMessage` varchar(200) DEFAULT NULL,
  PRIMARY KEY (`HistoryID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


CREATE TABLE `layers` (
  `ID` int NOT NULL AUTO_INCREMENT,
  `LayerID` varchar(50) NOT NULL,
  `CageID` varchar(50) DEFAULT NULL,
  `LayerNo` int DEFAULT NULL,
  `Length` double DEFAULT NULL,
  `Width` double DEFAULT NULL,
  `IsOccupied` bit(1) DEFAULT NULL,
  `GlassID` varchar(50) DEFAULT NULL,
  `StartNo` int DEFAULT NULL,
  `Space` double DEFAULT NULL,
  `RemainingLength` double DEFAULT NULL,
  `GlassSpec` varchar(100) DEFAULT NULL,
  `PLCAddress` varchar(50) DEFAULT NULL,
  `Coordinate` decimal(10,4) DEFAULT NULL,
  `IsDamaged` bit(1) DEFAULT NULL,
  PRIMARY KEY (`LayerID`),
  UNIQUE KEY `UK_layers_ID` (`ID`),
  KEY `IX_layers_CageID` (`CageID`),
  KEY `IX_layers_GlassID` (`GlassID`),
  CONSTRAINT `FK_layers_cages` FOREIGN KEY (`CageID`) REFERENCES `cages` (`CageCode`) ON DELETE CASCADE,
  CONSTRAINT `FK_layers_materials` FOREIGN KEY (`GlassID`) REFERENCES `materials` (`GlassID`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `logs` (
  `LogID` int NOT NULL AUTO_INCREMENT,
  `LogContent` varchar(500) DEFAULT NULL,
  `RecordTime` datetime DEFAULT NULL,
  `Type` varchar(255) DEFAULT NULL,
  `RelatedID` varchar(50) DEFAULT NULL,
  `Module` varchar(30) DEFAULT NULL,
  PRIMARY KEY (`LogID`),
  KEY `IX_logs_RecordTime` (`RecordTime`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `plcaddressmapping` (
  `KeyName` varchar(100) NOT NULL,
  `RealAddress` varchar(100) DEFAULT NULL,
  `DataType` varchar(50) DEFAULT NULL,
  `Description` varchar(200) DEFAULT NULL,
  PRIMARY KEY (`KeyName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `config` (
  `ConfigID` int NOT NULL AUTO_INCREMENT,
  `Type` varchar(255) DEFAULT NULL,
  `type2` varchar(50) DEFAULT NULL,
  `KeyName` varchar(255) DEFAULT NULL,
  `Value` varchar(255) DEFAULT NULL,
  `Address` int DEFAULT NULL,
  `Describe` varchar(255) DEFAULT NULL,
  `UpdateTime` datetime DEFAULT NULL,
  PRIMARY KEY (`ConfigID`),
  UNIQUE KEY `UK_config_KeyName` (`KeyName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `controlplcuser` (
  `id` int NOT NULL AUTO_INCREMENT,
  `username` varchar(50) NOT NULL,
  `password` varchar(255) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `UK_controlplcuser_username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO `controlplcuser` (`username`,`password`) VALUES ('admin','12345');
INSERT INTO `cages` VALUES (1,'A','INBOUND-01',0,120,2600.00,2000.00,2000.00,0.00,-1000.00,1000.00,5.00,5.00,1,10.00,10.00,1,1,0.00,5.000,b'1',b'0','A笼','2026-03-18 15:42:13'),(3,'B','OUTBOUND-01',1,120,2600.00,2000.00,2000.00,0.00,-1000.00,1000.00,5.00,5.00,3,30.00,10.00,1,1,0.00,NULL,b'1',b'0','B笼','2026-03-18 15:42:13');
INSERT INTO `config` (`ConfigID`,`Type`,`type2`,`KeyName`,`Value`,`Address`,`Describe`,`UpdateTime`) VALUES
(7, 'float','软件参数', 'CageInnerDistanceK', '0.0', NULL, '笼内距离K', '2026-03-18 15:37:09'),
(23,'String','软件参数','PlcIp','192.168.1.10',NULL,'PLC设备IP地址','2026-03-24 17:11:08'),
(24,'Int','软件参数','PlcPort','502',NULL,'PLC端口号','2026-03-24 17:11:08'),
(25,'Int','软件参数','PlcStation','1',NULL,'Modbus站号','2026-03-24 17:11:08'),
(26,'String','软件参数','PlcDataFormat','CDAB',NULL,'数据字节序','2026-03-24 17:11:08'),
(27,'Bool','软件参数','PlcAddressStartWithZero','true',NULL,'Modbus地址是否从0开始','2026-03-24 17:11:08'),
(30,'Float','软件参数','MeasureErrorAllowance','5',NULL,'测量误差容限(mm)','2026-03-31 15:40:48'),
(31,'Int','软件参数','DataSourceType','1',NULL,'数据来源','2026-03-31 15:40:48'),
(32,'Int','软件参数','MaxScanRetryTimes','3',NULL,'最大扫码重试次数','2026-03-31 15:40:48'),
(33,'Float','软件参数','GlassSpacing','10',NULL,'玻璃安全间距(mm)','2026-03-31 15:40:48'),
(34,'Int','软件参数','PlcPollTimeoutSeconds','120',NULL,'PLC等待反馈超时(秒)','2026-03-31 15:40:48'),
(35,'Int','软件参数','InboundRetryMax','3',NULL,'入库重试最大次数','2026-03-31 15:40:48'),
(39,'Int','软件参数','InboundPollIntervalMs','100',NULL,'PLC状态轮询间隔(ms)','2026-03-31 15:40:48'),
(40,'Bool','软件参数','Addr_SystemStart','101',101,'系统启动信号','2026-05-12 11:09:29'),
(41,'Bool','软件参数','Addr_In_GlassArrived','110',110,'玻璃到位检测','2026-05-12 10:29:33'),
(42,'Float','软件参数','Addr_In_GlassLength','5000',5000,'测量玻璃长度','2026-05-12 10:29:33'),
(43,'Float','软件参数','Addr_In_TargetCagePos','5004',5004,'目标层高坐标','2026-05-12 10:29:33'),
(45,'Bool','软件参数','Addr_In_PosReached','111',111,'升降机到位反馈','2026-05-12 10:29:33'),
(46,'Bool','软件参数','Addr_In_EnterCmd','112',112,'横推入笼指令','2026-05-12 10:29:33'),
(47,'Bool','软件参数','Addr_In_EnterDone','113',113,'横推完成反馈','2026-05-12 10:29:33'),
(50,'Bool','软件参数','Addr_GlobalEStop','99',99,'全局急停信号','2026-05-12 11:11:16'),
(55,'Bool','软件参数','EnableActivityLog','0',NULL,'窗口日志写入数据库','2026-04-01 14:47:36'),
(62,'Bool','软件参数','Addr_Out_StartCmd','100',100,'出笼启动指令','2026-04-16 10:53:07'),
(64,'Bool','软件参数','Addr_Out_PosReached','102',102,'出笼笼位到位','2026-04-16 10:53:07'),
(65,'Bool','软件参数','Addr_Out_PopCmd','103',103,'出片启动指令','2026-04-16 10:53:07'),
(66,'Bool','软件参数','Addr_Out_PopDone','104',104,'出片完成信号','2026-04-16 10:53:07'),
(69,'Float','软件参数','Addr_Out_MoveDistance','158',158,'出笼移动距离','2026-04-16 14:53:07'),
(70,'String','软件参数','OutSortField','Length',NULL,'出笼排序字段','2026-04-16 10:53:07'),
(71,'String','软件参数','OutSortDir','Desc',NULL,'出笼排序方向','2026-04-16 10:53:07'),
(73,'Int','软件参数','OutboundPollIntervalMs','200',NULL,'出库轮询间隔(ms)','2026-05-13 14:57:53'),
(74,'Int','软件参数','OutboundPollTimeoutMs','120000',NULL,'出库超时(ms)','2026-05-13 14:57:53'),
(75,'Int','软件参数','OutboundSafetyPollIntervalMs','200',NULL,'出库安全轮询间隔(ms)','2026-05-13 14:57:53'),
(76,'Int','软件参数','OutboundRetryMax','3',NULL,'出库重试最大次数','2026-05-13 14:57:53');
-- 测量台 HMI 相关 KeyName（仅初始化 Type/KeyName/Describe，由你手动填写 Value/Address；已存在同名 KeyName 则跳过）
-- BOOL：手动测试使能
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Test_Enable', '手动测试使能(1=手动测试使能)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Test_Enable' LIMIT 1);

-- BOOL：独立手动 X/Z 点动
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Trans_X+', '传送台X+点动(1=正向运行)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Trans_X+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Trans_X-', '传送台X-点动(1=反向运行)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Trans_X-' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Measure_X+', '测量台X+点动(1=正向运行)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Measure_X+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Measure_X-', '测量台X-点动(1=反向运行)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Measure_X-' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Measure_Z+', '测量台Z+点动(1=向上)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Measure_Z+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Measure_Z-', '测量台Z-点动(1=向下)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Measure_Z-' LIMIT 1);

-- BOOL：测量模式 & X 位置清零 / 联动点动
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_MeasureMode_Enable', '启用测量模式(1=启用)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_MeasureMode_Enable' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_XPos_Enable', 'X轴位置相关使能(预留)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_XPos_Enable' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Linked_X+', '联动手动X+点动(1=同时正向)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Linked_X+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Linked_X-', '联动手动X-点动(1=同时反向)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Linked_X-' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Linked_Z+', '联动手动Z+点动(1=同时向上)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Linked_Z+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Linked_Z-', '联动手动Z-点动(1=同时向下)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Linked_Z-' LIMIT 1);

-- FLOAT：位置/目标位置
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_Trans_Pos', '传送台当前位置(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Trans_Pos' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_Measure_Pos', '测量台当前位置(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Measure_Pos' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_OneWay_Pos', '单向台当前位置(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_OneWay_Pos' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_CageA_Y_Pos', 'A笼Y轴实时位置(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_Y_Pos' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_CageB_Y_Pos', 'B笼Y轴实时位置(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_Y_Pos' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_CageA_Y_Set', 'A笼Y轴目标位置写入(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_Y_Set' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_CageB_Y_Set', 'B笼Y轴目标位置写入(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_Y_Set' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_CageA_X_Set', 'A笼X轴目标位置写入(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_X_Set' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_CageB_X_Set', 'B笼X轴目标位置写入(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_X_Set' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_Out_X_Set', '出片台X轴目标位置写入(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Out_X_Set' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_Z_Pos', '测量台Z轴当前位置(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Z_Pos' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_ZOrigin_Set', '测量台Z轴原点设置写入(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_ZOrigin_Set' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_YPos_Set', '联动Z轴目标位置写入(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_YPos_Set' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Float', 'Addr_XPos_Set', 'X轴目标位置/清零目标(浮点)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_XPos_Set' LIMIT 1);

-- A/B笼 HMI 相关 BOOL KeyName（按住按钮写 true，松开写 false；勾选框选中写 true，取消写 false）
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_Lift_Up', 'A笼界面-升降气缸-上升按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_Lift_Up' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_Lift_Down', 'A笼界面-升降气缸-下降按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_Lift_Down' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_X-', 'A笼界面-X轴手动-X-按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_X-' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_X+', 'A笼界面-X轴手动-X+按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_X+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_Y+', 'A笼界面-Y轴手动-Y+按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_Y+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_Y-', 'A笼界面-Y轴手动-Y-按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_Y-' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_X_Select_Conveyor', 'A笼界面-X轴手动-输送台勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_X_Select_Conveyor' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_X_Select_Measure', 'A笼界面-X轴手动-测量台勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_X_Select_Measure' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_X_Select_OneWay', 'A笼界面-X轴手动-单向台勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_X_Select_OneWay' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageA_X_Select_Linked', 'A笼界面-X轴手动-联动勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageA_X_Select_Linked' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_Lift_Up', 'B笼界面-升降气缸-上升按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_Lift_Up' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_Lift_Down', 'B笼界面-升降气缸-下降按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_Lift_Down' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_X-', 'B笼界面-X轴手动-X-按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_X-' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_X+', 'B笼界面-X轴手动-X+按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_X+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_Y+', 'B笼界面-Y轴手动-Y+按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_Y+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_Y-', 'B笼界面-Y轴手动-Y-按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_Y-' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_X_Select_Conveyor', 'B笼界面-X轴手动-输送台勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_X_Select_Conveyor' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_X_Select_Measure', 'B笼界面-X轴手动-测量台勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_X_Select_Measure' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_X_Select_OneWay', 'B笼界面-X轴手动-单向台勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_X_Select_OneWay' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_CageB_X_Select_Linked', 'B笼界面-X轴手动-联动勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_CageB_X_Select_Linked' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Out_Manual_X-', '出片台手动-X-按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Out_Manual_X-' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Out_Manual_X+', '出片台手动-X+按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Out_Manual_X+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Out_X-', '出片台X轴手动-X-按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Out_X-' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Out_X+', '出片台X轴手动-X+按钮(按住=1，松开=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Out_X+' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Out_X_Select_OneWay', '出片台X轴手动-单向台勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Out_X_Select_OneWay' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Out_X_Select_Out', '出片台X轴手动-出片台勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Out_X_Select_Out' LIMIT 1);
INSERT INTO `config` (`Type`, `KeyName`, `Describe`, `UpdateTime`)
SELECT 'Bool', 'Addr_Out_X_Select_Linked', '出片台X轴手动-联动勾选框(选中=1，取消=0)', NOW() FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `config` c WHERE c.`KeyName` = 'Addr_Out_X_Select_Linked' LIMIT 1);

UPDATE `config` SET `Address` = 1000, `Value` = '1000' WHERE `KeyName` = 'Addr_Test_Enable';
UPDATE `config` SET `Address` = 1001, `Value` = '1001' WHERE `KeyName` = 'Addr_Trans_X+';
UPDATE `config` SET `Address` = 1002, `Value` = '1002' WHERE `KeyName` = 'Addr_Trans_X-';
UPDATE `config` SET `Address` = 1003, `Value` = '1003' WHERE `KeyName` = 'Addr_Measure_X+';
UPDATE `config` SET `Address` = 1004, `Value` = '1004' WHERE `KeyName` = 'Addr_Measure_X-';
UPDATE `config` SET `Address` = 1005, `Value` = '1005' WHERE `KeyName` = 'Addr_Measure_Z+';
UPDATE `config` SET `Address` = 1006, `Value` = '1006' WHERE `KeyName` = 'Addr_Measure_Z-';
UPDATE `config` SET `Address` = 1007, `Value` = '1007' WHERE `KeyName` = 'Addr_MeasureMode_Enable';
UPDATE `config` SET `Address` = 1008, `Value` = '1008' WHERE `KeyName` = 'Addr_XPos_Enable';
UPDATE `config` SET `Address` = 1009, `Value` = '1009' WHERE `KeyName` = 'Addr_Linked_X+';
UPDATE `config` SET `Address` = 1010, `Value` = '1010' WHERE `KeyName` = 'Addr_Linked_X-';
UPDATE `config` SET `Address` = 1011, `Value` = '1011' WHERE `KeyName` = 'Addr_Linked_Z+';
UPDATE `config` SET `Address` = 1012, `Value` = '1012' WHERE `KeyName` = 'Addr_Linked_Z-';
UPDATE `config` SET `Address` = 1013, `Value` = '1013' WHERE `KeyName` = 'Addr_Trans_Pos';
UPDATE `config` SET `Address` = 1014, `Value` = '1014' WHERE `KeyName` = 'Addr_Measure_Pos';
UPDATE `config` SET `Address` = 1015, `Value` = '1015' WHERE `KeyName` = 'Addr_OneWay_Pos';
UPDATE `config` SET `Address` = 1016, `Value` = '1016' WHERE `KeyName` = 'Addr_CageA_Y_Pos';
UPDATE `config` SET `Address` = 1017, `Value` = '1017' WHERE `KeyName` = 'Addr_CageB_Y_Pos';
UPDATE `config` SET `Address` = 1018, `Value` = '1018' WHERE `KeyName` = 'Addr_CageA_Y_Set';
UPDATE `config` SET `Address` = 1019, `Value` = '1019' WHERE `KeyName` = 'Addr_CageB_Y_Set';
UPDATE `config` SET `Address` = 1020, `Value` = '1020' WHERE `KeyName` = 'Addr_CageA_X_Set';
UPDATE `config` SET `Address` = 1021, `Value` = '1021' WHERE `KeyName` = 'Addr_CageB_X_Set';
UPDATE `config` SET `Address` = 1022, `Value` = '1022' WHERE `KeyName` = 'Addr_Out_X_Set';
UPDATE `config` SET `Address` = 1023, `Value` = '1023' WHERE `KeyName` = 'Addr_Z_Pos';
UPDATE `config` SET `Address` = 1024, `Value` = '1024' WHERE `KeyName` = 'Addr_ZOrigin_Set';
UPDATE `config` SET `Address` = 1025, `Value` = '1025' WHERE `KeyName` = 'Addr_YPos_Set';
UPDATE `config` SET `Address` = 1026, `Value` = '1026' WHERE `KeyName` = 'Addr_XPos_Set';
UPDATE `config` SET `Address` = 1027, `Value` = '1027' WHERE `KeyName` = 'Addr_CageA_Lift_Up';
UPDATE `config` SET `Address` = 1028, `Value` = '1028' WHERE `KeyName` = 'Addr_CageA_Lift_Down';
UPDATE `config` SET `Address` = 1029, `Value` = '1029' WHERE `KeyName` = 'Addr_CageA_X-';
UPDATE `config` SET `Address` = 1030, `Value` = '1030' WHERE `KeyName` = 'Addr_CageA_X+';
UPDATE `config` SET `Address` = 1031, `Value` = '1031' WHERE `KeyName` = 'Addr_CageA_Y+';
UPDATE `config` SET `Address` = 1032, `Value` = '1032' WHERE `KeyName` = 'Addr_CageA_Y-';
UPDATE `config` SET `Address` = 1033, `Value` = '1033' WHERE `KeyName` = 'Addr_CageA_X_Select_Conveyor';
UPDATE `config` SET `Address` = 1034, `Value` = '1034' WHERE `KeyName` = 'Addr_CageA_X_Select_Measure';
UPDATE `config` SET `Address` = 1035, `Value` = '1035' WHERE `KeyName` = 'Addr_CageA_X_Select_OneWay';
UPDATE `config` SET `Address` = 1036, `Value` = '1036' WHERE `KeyName` = 'Addr_CageA_X_Select_Linked';
UPDATE `config` SET `Address` = 1037, `Value` = '1037' WHERE `KeyName` = 'Addr_CageB_Lift_Up';
UPDATE `config` SET `Address` = 1038, `Value` = '1038' WHERE `KeyName` = 'Addr_CageB_Lift_Down';
UPDATE `config` SET `Address` = 1039, `Value` = '1039' WHERE `KeyName` = 'Addr_CageB_X-';
UPDATE `config` SET `Address` = 1040, `Value` = '1040' WHERE `KeyName` = 'Addr_CageB_X+';
UPDATE `config` SET `Address` = 1041, `Value` = '1041' WHERE `KeyName` = 'Addr_CageB_Y+';
UPDATE `config` SET `Address` = 1042, `Value` = '1042' WHERE `KeyName` = 'Addr_CageB_Y-';
UPDATE `config` SET `Address` = 1043, `Value` = '1043' WHERE `KeyName` = 'Addr_CageB_X_Select_Conveyor';
UPDATE `config` SET `Address` = 1044, `Value` = '1044' WHERE `KeyName` = 'Addr_CageB_X_Select_Measure';
UPDATE `config` SET `Address` = 1045, `Value` = '1045' WHERE `KeyName` = 'Addr_CageB_X_Select_OneWay';
UPDATE `config` SET `Address` = 1046, `Value` = '1046' WHERE `KeyName` = 'Addr_CageB_X_Select_Linked';
UPDATE `config` SET `Address` = 1047, `Value` = '1047' WHERE `KeyName` = 'Addr_Out_Manual_X-';
UPDATE `config` SET `Address` = 1048, `Value` = '1048' WHERE `KeyName` = 'Addr_Out_Manual_X+';
UPDATE `config` SET `Address` = 1049, `Value` = '1049' WHERE `KeyName` = 'Addr_Out_X-';
UPDATE `config` SET `Address` = 1050, `Value` = '1050' WHERE `KeyName` = 'Addr_Out_X+';
UPDATE `config` SET `Address` = 1051, `Value` = '1051' WHERE `KeyName` = 'Addr_Out_X_Select_OneWay';
UPDATE `config` SET `Address` = 1052, `Value` = '1052' WHERE `KeyName` = 'Addr_Out_X_Select_Out';
UPDATE `config` SET `Address` = 1053, `Value` = '1053' WHERE `KeyName` = 'Addr_Out_X_Select_Linked';
-- ========== 已有库升级 Layers（表已存在时 CREATE IF NOT EXISTS 不会加列，请按需执行）==========
SET FOREIGN_KEY_CHECKS = 1;

-- set default type2 for all config rows
UPDATE config SET type2 = '软件参数' WHERE type2 IS NULL OR TRIM(type2) = '';

SET FOREIGN_KEY_CHECKS = 1;
