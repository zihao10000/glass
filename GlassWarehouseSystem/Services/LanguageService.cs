using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using GlassWarehouseSystem.Models;

namespace GlassWarehouseSystem.Services
{
    public static class LanguageService
    {
        private static readonly object SyncRoot = new();
        private static string _currentLanguage = "zh-CN";
        private static Dictionary<string, Dictionary<string, string>> _languageResources = new();

        public static event EventHandler? LanguageChanged;

        public static List<LanguageInfo> SupportedLanguages => new()
        {
            new LanguageInfo { Code = "zh-CN", Name = "中文", Flag = "🇨🇳" },
            new LanguageInfo { Code = "en-US", Name = "English", Flag = "🇺🇸" },
            new LanguageInfo { Code = "ru-RU", Name = "Русский", Flag = "🇷🇺" }
        };

        public static string CurrentLanguage => _currentLanguage;

        public static void Initialize()
        {
            LoadLanguageResources();
        }

        private static void LoadLanguageResources()
        {
            _languageResources = new Dictionary<string, Dictionary<string, string>>
            {
                ["zh-CN"] = new Dictionary<string, string>
                {
                    // 主菜单
                    ["Menu.System"] = "系统 (_X)",
                    ["Menu.Config"] = "配置 (_S)",
                    ["Menu.Plan"] = "计划 (_P)",
                    ["Menu.Report"] = "报表 (_R)",
                    ["Menu.Language"] = "语言 (_L)",
                    ["Menu.Exit"] = "退出 (_Q)",
                    
                    // 配置子菜单
                    ["Menu.ParamSettings"] = "参数设置 (_P)",
                    ["Menu.SystemSettings"] = "系统设置",
                    ["Menu.ParamManage"] = "参数管理",
                    ["Menu.GlobalParam"] = "全局参数",
                    ["Menu.PLCControl"] = "PLC 控制",
                    ["Menu.CageLayer"] = "层位管理",
                    ["Menu.License"] = "授权管理",
                    
                    // 计划子菜单
                    ["Menu.PrintLabel"] = "打印标签 (_T)",
                    
                    // 报表子菜单
                    ["Menu.DashboardA"] = "看板 A",
                    ["Menu.DashboardB"] = "看板 B",
                    ["Menu.QueryWindow"] = "查询窗口",
                    ["Menu.LogWindow"] = "日志窗口",
                    ["Menu.MeasurementPlatform"] = "测量平台",
                    
                    // 窗口标题
                    ["Window.Inbound"] = "入笼管理",
                    ["Window.Outbound"] = "出笼管理",
                    ["Window.SystemSettings"] = "系统配置",
                    ["Window.License"] = "授权管理",
                    ["Window.Query"] = "查询窗口",
                    ["Window.Log"] = "日志窗口",
                    ["Window.Measurement"] = "测量平台",
                    ["Window.CageDashboard"] = "笼子看板",
                    ["Window.CageLayer"] = "层位管理",
                    ["Window.GlobalParam"] = "全局参数",
                    ["Window.PLCControl"] = "PLC 控制",
                    ["Window.Print"] = "打印标签",
                    
                    // 通用按钮
                    ["Btn.Confirm"] = "确认",
                    ["Btn.Cancel"] = "取消",
                    ["Btn.Refresh"] = "刷新",
                    ["Btn.Add"] = "增加",
                    ["Btn.Delete"] = "删除",
                    ["Btn.Save"] = "保存",
                    ["Btn.Close"] = "关闭",
                    ["Btn.Exit"] = "退出",
                    ["Btn.Start"] = "启动",
                    ["Btn.Stop"] = "停止",
                    ["Btn.Import"] = "导入",
                    ["Btn.Export"] = "导出",
                    ["Btn.Search"] = "搜索",
                    ["Btn.Reset"] = "复位",
                    ["Btn.Print"] = "打印",
                    ["Btn.Manual"] = "手动",
                    ["Btn.Auto"] = "自动",
                    
                    // 入笼窗口
                    ["Inbound.ImportExcel"] = "导入 Excel",
                    ["Inbound.PrintLabel"] = "打印标签",
                    ["Inbound.ViewCage"] = "查看笼",
                    ["Inbound.PlanManage"] = "计划管理",
                    ["Inbound.CageStatus"] = "笼状态",
                    ["Inbound.SliceManage"] = "切片管理",
                    ["Inbound.SystemConfig"] = "系统配置",
                    ["Inbound.License"] = "许可",
                    ["Inbound.PhotoCheck"] = "照片/客户/产品",
                    ["Inbound.InputPlan"] = "入库计划",
                    ["Inbound.Breakage"] = "手动破损",
                    ["Inbound.ManualInbound"] = "手动确认入笼",
                    ["Inbound.ShiftAtoB"] = "顺移 A→B",
                    ["Inbound.PLCReset"] = "PLC 复位",
                    ["Inbound.ExportCSV"] = "导出比对记录 CSV",
                    ["Inbound.DetailedInfo"] = "详细随工单",
                    ["Inbound.ExceptionRecords"] = "异常数据",
                    ["Inbound.CompareRecords"] = "比对记录",
                    ["Inbound.RunLog"] = "运行日志",
                    ["Inbound.StartService"] = "启动服务",
                    ["Inbound.StopService"] = "停止服务",
                    
                    // 出笼窗口
                    ["Outbound.System"] = "系统 (_X)",
                    ["Outbound.Config"] = "配置 (_S)",
                    ["Outbound.Report"] = "报表 (_R)",
                    ["Outbound.Exit"] = "退出 (_Q)",
                    ["Outbound.ParamSettings"] = "参数设置 (_P)",
                    ["Outbound.Dashboard"] = "看板 (_S)",
                    ["Outbound.StartOutbound"] = "启动出笼",
                    ["Outbound.StopOutbound"] = "停止出笼",
                    ["Outbound.ManualOutbound"] = "手动确认出笼",
                    ["Outbound.TaskList"] = "出笼任务列表",
                    
                    // 状态
                    ["Status.Running"] = "运行中",
                    ["Status.Stopped"] = "已停止",
                    ["Status.Loading"] = "加载中...",
                    ["Status.Pending"] = "待入库",
                    ["Status.InWarehouse"] = "在库",
                    ["Status.Outbounded"] = "已出库",
                    ["Status.Breakage"] = "破损",
                    
                    // 消息
                    ["Msg.ConfirmExit"] = "确认退出系统吗？",
                    ["Msg.Title.Confirm"] = "确认",
                    ["Msg.Title.Info"] = "提示",
                    ["Msg.Title.Warning"] = "警告",
                    ["Msg.Title.Error"] = "错误",
                    ["Msg.Success"] = "成功",
                    ["Msg.Failed"] = "失败",
                    
                    // 表格列头
                    ["Col.ID"] = "行号",
                    ["Col.Status"] = "状态",
                    ["Col.OrderNo"] = "订单号",
                    ["Col.OrderName"] = "订单名称",
                    ["Col.Length"] = "长边 (mm)",
                    ["Col.Width"] = "短边 (mm)",
                    ["Col.GlassID"] = "GlassID",
                    ["Col.ProductName"] = "产品名称",
                    ["Col.Customer"] = "客户名称",
                    ["Col.CageCode"] = "笼号",
                    ["Col.LayerNo"] = "层位",
                    ["Col.InboundTime"] = "入库时间",
                    ["Col.OutboundTime"] = "出库时间",
                    
                    // 系统设置
                    ["Settings.System"] = "系统参数",
                    ["Settings.PLC"] = "PLC 配置",
                    ["Settings.Print"] = "标签打印",
                    ["Settings.Database"] = "数据库",
                    ["Settings.Redis"] = "Redis 缓存",
                    
                    // 全局参数
                    ["Param.CageDistance"] = "笼内距离 K",
                    ["Param.PLC_IP"] = "PLC IP 地址",
                    ["Param.PLC_Port"] = "PLC 端口号",
                    ["Param.PLC_Station"] = "Modbus 站号",
                    ["Param.MaxRetry"] = "最大扫码重试次数",
                    ["Param.GlassSpacing"] = "玻璃安全间距 (mm)",
                    ["Param.MeasureError"] = "测量误差容限 (mm)"
                },
                ["en-US"] = new Dictionary<string, string>
                {
                    // 主菜单
                    ["Menu.System"] = "System (_X)",
                    ["Menu.Config"] = "Config (_S)",
                    ["Menu.Plan"] = "Plan (_P)",
                    ["Menu.Report"] = "Report (_R)",
                    ["Menu.Language"] = "Language (_L)",
                    ["Menu.Exit"] = "Exit (_Q)",
                    
                    // 配置子菜单
                    ["Menu.ParamSettings"] = "Parameters (_P)",
                    ["Menu.SystemSettings"] = "System Settings",
                    ["Menu.ParamManage"] = "Param Management",
                    ["Menu.GlobalParam"] = "Global Params",
                    ["Menu.PLCControl"] = "PLC Control",
                    ["Menu.CageLayer"] = "Layer Manage",
                    ["Menu.License"] = "License",
                    
                    // 计划子菜单
                    ["Menu.PrintLabel"] = "Print Label (_T)",
                    
                    // 报表子菜单
                    ["Menu.DashboardA"] = "Dashboard A",
                    ["Menu.DashboardB"] = "Dashboard B",
                    ["Menu.QueryWindow"] = "Query Window",
                    ["Menu.LogWindow"] = "Log Window",
                    ["Menu.MeasurementPlatform"] = "Measurement Platform",
                    
                    // 窗口标题
                    ["Window.Inbound"] = "Inbound",
                    ["Window.Outbound"] = "Outbound",
                    ["Window.SystemSettings"] = "System Settings",
                    ["Window.License"] = "License",
                    ["Window.Query"] = "Query",
                    ["Window.Log"] = "Log",
                    ["Window.Measurement"] = "Measurement",
                    ["Window.CageDashboard"] = "Cage Dashboard",
                    ["Window.CageLayer"] = "Layer Management",
                    ["Window.GlobalParam"] = "Global Params",
                    ["Window.PLCControl"] = "PLC Control",
                    ["Window.Print"] = "Print Label",
                    
                    // 通用按钮
                    ["Btn.Confirm"] = "Confirm",
                    ["Btn.Cancel"] = "Cancel",
                    ["Btn.Refresh"] = "Refresh",
                    ["Btn.Add"] = "Add",
                    ["Btn.Delete"] = "Delete",
                    ["Btn.Save"] = "Save",
                    ["Btn.Close"] = "Close",
                    ["Btn.Exit"] = "Exit",
                    ["Btn.Start"] = "Start",
                    ["Btn.Stop"] = "Stop",
                    ["Btn.Import"] = "Import",
                    ["Btn.Export"] = "Export",
                    ["Btn.Search"] = "Search",
                    ["Btn.Reset"] = "Reset",
                    ["Btn.Print"] = "Print",
                    ["Btn.Manual"] = "Manual",
                    ["Btn.Auto"] = "Auto",
                    
                    // 入笼窗口
                    ["Inbound.ImportExcel"] = "Import Excel",
                    ["Inbound.PrintLabel"] = "Print Label",
                    ["Inbound.ViewCage"] = "View Cage",
                    ["Inbound.PlanManage"] = "Plan Manage",
                    ["Inbound.CageStatus"] = "Cage Status",
                    ["Inbound.SliceManage"] = "Slice Manage",
                    ["Inbound.SystemConfig"] = "System Config",
                    ["Inbound.License"] = "License",
                    ["Inbound.PhotoCheck"] = "Photo/Customer/Product",
                    ["Inbound.InputPlan"] = "Inbound Plan",
                    ["Inbound.Breakage"] = "Manual Breakage",
                    ["Inbound.ManualInbound"] = "Manual Inbound",
                    ["Inbound.ShiftAtoB"] = "Shift A→B",
                    ["Inbound.PLCReset"] = "PLC Reset",
                    ["Inbound.ExportCSV"] = "Export Compare CSV",
                    ["Inbound.DetailedInfo"] = "Detailed Info",
                    ["Inbound.ExceptionRecords"] = "Exceptions",
                    ["Inbound.CompareRecords"] = "Compare Records",
                    ["Inbound.RunLog"] = "Run Log",
                    ["Inbound.StartService"] = "Start Service",
                    ["Inbound.StopService"] = "Stop Service",
                    
                    // 出笼窗口
                    ["Outbound.System"] = "System (_X)",
                    ["Outbound.Config"] = "Config (_S)",
                    ["Outbound.Report"] = "Report (_R)",
                    ["Outbound.Exit"] = "Exit (_Q)",
                    ["Outbound.ParamSettings"] = "Parameters (_P)",
                    ["Outbound.Dashboard"] = "Dashboard (_S)",
                    ["Outbound.StartOutbound"] = "Start Outbound",
                    ["Outbound.StopOutbound"] = "Stop Outbound",
                    ["Outbound.ManualOutbound"] = "Manual Outbound",
                    ["Outbound.TaskList"] = "Outbound Tasks",
                    
                    // 状态
                    ["Status.Running"] = "Running",
                    ["Status.Stopped"] = "Stopped",
                    ["Status.Loading"] = "Loading...",
                    ["Status.Pending"] = "Pending",
                    ["Status.InWarehouse"] = "In Warehouse",
                    ["Status.Outbounded"] = "Outbounded",
                    ["Status.Breakage"] = "Breakage",
                    
                    // 消息
                    ["Msg.ConfirmExit"] = "Confirm to exit?",
                    ["Msg.Title.Confirm"] = "Confirm",
                    ["Msg.Title.Info"] = "Info",
                    ["Msg.Title.Warning"] = "Warning",
                    ["Msg.Title.Error"] = "Error",
                    ["Msg.Success"] = "Success",
                    ["Msg.Failed"] = "Failed",
                    
                    // 表格列头
                    ["Col.ID"] = "ID",
                    ["Col.Status"] = "Status",
                    ["Col.OrderNo"] = "Order No",
                    ["Col.OrderName"] = "Order Name",
                    ["Col.Length"] = "Length (mm)",
                    ["Col.Width"] = "Width (mm)",
                    ["Col.GlassID"] = "Glass ID",
                    ["Col.ProductName"] = "Product Name",
                    ["Col.Customer"] = "Customer",
                    ["Col.CageCode"] = "Cage Code",
                    ["Col.LayerNo"] = "Layer",
                    ["Col.InboundTime"] = "Inbound Time",
                    ["Col.OutboundTime"] = "Outbound Time",
                    
                    // 系统设置
                    ["Settings.System"] = "System Params",
                    ["Settings.PLC"] = "PLC Config",
                    ["Settings.Print"] = "Label Print",
                    ["Settings.Database"] = "Database",
                    ["Settings.Redis"] = "Redis Cache",
                    
                    // 全局参数
                    ["Param.CageDistance"] = "Cage Distance K",
                    ["Param.PLC_IP"] = "PLC IP Address",
                    ["Param.PLC_Port"] = "PLC Port",
                    ["Param.PLC_Station"] = "Modbus Station",
                    ["Param.MaxRetry"] = "Max Scan Retry",
                    ["Param.GlassSpacing"] = "Glass Spacing (mm)",
                    ["Param.MeasureError"] = "Measure Error (mm)"
                },
                ["ru-RU"] = new Dictionary<string, string>
                {
                    // 主菜单
                    ["Menu.System"] = "Система (_X)",
                    ["Menu.Config"] = "Конфиг (_S)",
                    ["Menu.Plan"] = "План (_P)",
                    ["Menu.Report"] = "Отчет (_R)",
                    ["Menu.Language"] = "Язык (_L)",
                    ["Menu.Exit"] = "Выход (_Q)",
                    
                    // 配置子菜单
                    ["Menu.ParamSettings"] = "Параметры (_P)",
                    ["Menu.SystemSettings"] = "Настройки системы",
                    ["Menu.ParamManage"] = "Управл. параметрами",
                    ["Menu.GlobalParam"] = "Глоб. параметры",
                    ["Menu.PLCControl"] = "Управление PLC",
                    ["Menu.CageLayer"] = "Управл. ярусами",
                    ["Menu.License"] = "Лицензия",
                    
                    // 计划子菜单
                    ["Menu.PrintLabel"] = "Печать этикетки (_T)",
                    
                    // 报表子菜单
                    ["Menu.DashboardA"] = "Панель A",
                    ["Menu.DashboardB"] = "Панель B",
                    ["Menu.QueryWindow"] = "Окно запроса",
                    ["Menu.LogWindow"] = "Окно журнала",
                    ["Menu.MeasurementPlatform"] = "Платформа измерений",
                    
                    // 窗口标题
                    ["Window.Inbound"] = "Приемка",
                    ["Window.Outbound"] = "Отгрузка",
                    ["Window.SystemSettings"] = "Настройки системы",
                    ["Window.License"] = "Лицензия",
                    ["Window.Query"] = "Запрос",
                    ["Window.Log"] = "Журнал",
                    ["Window.Measurement"] = "Измерение",
                    ["Window.CageDashboard"] = "Панель ячеек",
                    ["Window.CageLayer"] = "Управл. ярусами",
                    ["Window.GlobalParam"] = "Глоб. параметры",
                    ["Window.PLCControl"] = "Управление PLC",
                    ["Window.Print"] = "Печать этикетки",
                    
                    // 通用按钮
                    ["Btn.Confirm"] = "Подтвердить",
                    ["Btn.Cancel"] = "Отмена",
                    ["Btn.Refresh"] = "Обновить",
                    ["Btn.Add"] = "Добавить",
                    ["Btn.Delete"] = "Удалить",
                    ["Btn.Save"] = "Сохранить",
                    ["Btn.Close"] = "Закрыть",
                    ["Btn.Exit"] = "Выход",
                    ["Btn.Start"] = "Запуск",
                    ["Btn.Stop"] = "Стоп",
                    ["Btn.Import"] = "Импорт",
                    ["Btn.Export"] = "Экспорт",
                    ["Btn.Search"] = "Поиск",
                    ["Btn.Reset"] = "Сброс",
                    ["Btn.Print"] = "Печать",
                    ["Btn.Manual"] = "Вручную",
                    ["Btn.Auto"] = "Авто",
                    
                    // 入笼窗口
                    ["Inbound.ImportExcel"] = "Импорт Excel",
                    ["Inbound.PrintLabel"] = "Печать этикетки",
                    ["Inbound.ViewCage"] = "Просмотр ячейки",
                    ["Inbound.PlanManage"] = "Управл. планом",
                    ["Inbound.CageStatus"] = "Статус ячейки",
                    ["Inbound.SliceManage"] = "Управл. срезом",
                    ["Inbound.SystemConfig"] = "Настр. системы",
                    ["Inbound.License"] = "Лицензия",
                    ["Inbound.PhotoCheck"] = "Фото/Клиент/Продукт",
                    ["Inbound.InputPlan"] = "План приемки",
                    ["Inbound.Breakage"] = "Ручной брак",
                    ["Inbound.ManualInbound"] = "Ручная приемка",
                    ["Inbound.ShiftAtoB"] = "Сдвиг A→B",
                    ["Inbound.PLCReset"] = "Сброс PLC",
                    ["Inbound.ExportCSV"] = "Экспорт сравнения CSV",
                    ["Inbound.DetailedInfo"] = "Детальная инфо",
                    ["Inbound.ExceptionRecords"] = "Исключения",
                    ["Inbound.CompareRecords"] = "Записи сравнения",
                    ["Inbound.RunLog"] = "Журнал работы",
                    ["Inbound.StartService"] = "Запуск сервиса",
                    ["Inbound.StopService"] = "Остановка сервиса",
                    
                    // 出笼窗口
                    ["Outbound.System"] = "Система (_X)",
                    ["Outbound.Config"] = "Конфиг (_S)",
                    ["Outbound.Report"] = "Отчет (_R)",
                    ["Outbound.Exit"] = "Выход (_Q)",
                    ["Outbound.ParamSettings"] = "Параметры (_P)",
                    ["Outbound.Dashboard"] = "Панель (_S)",
                    ["Outbound.StartOutbound"] = "Запуск отгрузки",
                    ["Outbound.StopOutbound"] = "Остановка отгрузки",
                    ["Outbound.ManualOutbound"] = "Ручная отгрузка",
                    ["Outbound.TaskList"] = "Задачи отгрузки",
                    
                    // 状态
                    ["Status.Running"] = "Работает",
                    ["Status.Stopped"] = "Остановлено",
                    ["Status.Loading"] = "Загрузка...",
                    ["Status.Pending"] = "Ожидание",
                    ["Status.InWarehouse"] = "На складе",
                    ["Status.Outbounded"] = "Отгружено",
                    ["Status.Breakage"] = "Брак",
                    
                    // 消息
                    ["Msg.ConfirmExit"] = "Подтвердить выход?",
                    ["Msg.Title.Confirm"] = "Подтверждение",
                    ["Msg.Title.Info"] = "Информация",
                    ["Msg.Title.Warning"] = "Предупреждение",
                    ["Msg.Title.Error"] = "Ошибка",
                    ["Msg.Success"] = "Успешно",
                    ["Msg.Failed"] = "Ошибка",
                    
                    // 表格列头
                    ["Col.ID"] = "№",
                    ["Col.Status"] = "Статус",
                    ["Col.OrderNo"] = "Заказ №",
                    ["Col.OrderName"] = "Название заказа",
                    ["Col.Length"] = "Длина (мм)",
                    ["Col.Width"] = "Ширина (мм)",
                    ["Col.GlassID"] = "ID стекла",
                    ["Col.ProductName"] = "Название продукта",
                    ["Col.Customer"] = "Клиент",
                    ["Col.CageCode"] = "Код ячейки",
                    ["Col.LayerNo"] = "Ярус",
                    ["Col.InboundTime"] = "Время приемки",
                    ["Col.OutboundTime"] = "Время отгрузки",
                    
                    // 系统设置
                    ["Settings.System"] = "Параметры системы",
                    ["Settings.PLC"] = "Настр. PLC",
                    ["Settings.Print"] = "Печать этикеток",
                    ["Settings.Database"] = "База данных",
                    ["Settings.Redis"] = "Кэш Redis",
                    
                    // 全局参数
                    ["Param.CageDistance"] = "Дистанция K ячейки",
                    ["Param.PLC_IP"] = "IP адрес PLC",
                    ["Param.PLC_Port"] = "Порт PLC",
                    ["Param.PLC_Station"] = "Станция Modbus",
                    ["Param.MaxRetry"] = "Макс. попыток сканир.",
                    ["Param.GlassSpacing"] = "Интервал стекла (мм)",
                    ["Param.MeasureError"] = "Погрешность измер. (мм)"
                }
            };
        }

        public static void SetLanguage(string languageCode)
        {
            lock (SyncRoot)
            {
                if (!_languageResources.ContainsKey(languageCode))
                {
                    throw new ArgumentException($"不支持的语言：{languageCode}");
                }

                _currentLanguage = languageCode;
                
                // 设置当前线程的文化信息
                Thread.CurrentThread.CurrentCulture = new CultureInfo(languageCode);
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(languageCode);

                // 触发语言变更事件
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static string Get(string key)
        {
            if (_languageResources.TryGetValue(_currentLanguage, out var resources) &&
                resources.TryGetValue(key, out var value))
            {
                return value;
            }

            // 如果找不到，返回键名作为后备
            return key;
        }

        // 便捷方法
        public static string GetMenuSystem() => Get("Menu.System");
        public static string GetMenuConfig() => Get("Menu.Config");
        public static string GetMenuPlan() => Get("Menu.Plan");
        public static string GetMenuReport() => Get("Menu.Report");
        public static string GetMenuExit() => Get("Menu.Exit");
        public static string GetWindowInbound() => Get("Window.Inbound");
        public static string GetWindowOutbound() => Get("Window.Outbound");
    }
}
