using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;
using GlassWarehouseSystem.Services;
using GlassWarehouseSystem.ViewModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace GlassWarehouseSystem
{
    public partial class CageDashboardWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<CageLayerViewModel> LayerList { get; set; }

        private CageLayerViewModel _selectedLayer;
        public CageLayerViewModel SelectedLayer
        {
            get => _selectedLayer;
            set { _selectedLayer = value; OnPropertyChanged(); }
        }

        // 核心标识：这是 A 笼
        private string currentCageCode = "A";

        // 加上下面这一行，给电子锁一个存放它的地方：
        private bool _isLoading = false;

        private Cage _currentCage;
        public Cage CurrentCage
        {
            get => _currentCage;
            set { _currentCage = value; OnPropertyChanged(); }
        }

        public ICommand SelectLayerCommand { get; set; }
        private DispatcherTimer _refreshTimer;

        public CageDashboardWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            //// --- 开始调试代码 ---
            //try
            //{
            //    using (var db = new WarehouseDbContext()) // 替换成你实际的 DbContext 类名
            //    {
            //        // 1. 检查连接
            //        bool canConnect = db.Database.CanConnect();

            //        // 2. 查一下笼子表有多少行数据
            //        int cageCount = db.Cages.Count();

            //        // 3. 查一下层表有多少行数据
            //        int layerCount = db.Layers.Count();

            //        MessageBox.Show($"数据库连接: {canConnect}\n笼子数量: {cageCount}\n层数数据: {layerCount}", "调试信息");

            //        // 4. 如果数量 > 0，打印第一个笼子的编码看看
            //        if (cageCount > 0)
            //        {
            //            var firstCage = db.Cages.FirstOrDefault();
            //            MessageBox.Show($"第一个笼子编码: {firstCage.CageCode}", "数据检查");
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    // 如果这里弹出报错，那就是连接字符串或者模型映射有问题
            //    MessageBox.Show($"数据库查询崩溃: {ex.Message}\n{ex.InnerException?.Message}", "致命错误");
            //}
            //// --- 结束调试代码 ---



            LayerList = new ObservableCollection<CageLayerViewModel>();
            SelectLayerCommand = new RelayCommand(OnLayerSelected);

            //// 1. 竖向排列初始化 120个格子
            //int rows = 10;
            //int cols = 12;
            //for (int i = 0; i < rows; i++)
            //{
            //    for (int j = 0; j < cols; j++)
            //    {
            //        int layerNo = (j * rows) + i + 1;
            //        LayerList.Add(new CageLayerViewModel
            //        {
            //            LayerNo = layerNo,
            //            CageID = currentCageCode,
            //            IsOccupied = false
            //        });
            //    }
            //}

            // 2. 启动初始化任务
            Task.Run(() =>
            {
                // 初始化时清理一次，确保第一次加载必走数据库拿最新数据
                CacheQueryService.ClearCacheByPattern($"CageLayer:{currentCageCode}:*");
                LoadData(false);
            });

            // 3. 定时器（每5秒刷新）
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(6);
            _refreshTimer.Tick += (s, e) => Task.Run(() => LoadData(false));
            _refreshTimer.Start();
        }

        private void LoadData(bool forceRefresh = false)
        {
            if (_isLoading) return; // 电子锁拦截
            //using (var db = new WarehouseDbContext())
            //{
            //    int layerCount = db.Layers.Count();
            //    int materialCount = db.Materials.Count();

            //    MessageBox.Show($"layers: {layerCount}, materials: {materialCount}");
            //}

            try
            {
                _isLoading = true; // 上锁

                // =================【核心新增：快照步】=================
                // 进来第一件事，把当前的全局变量的值，复制给一个局部变量 snapshotCageCode
                // 这样在本次 LoadData 运行期间，不管外面的全局变量怎么变，这个快照绝对不会变！
                string snapshotCageCode = this.currentCageCode;
                // =====================================================

                string cageCode = null; // 在外部声明

                //string cageCode = currentCageCode;
                using (var db = new WarehouseDbContext())
                {
                    // 在 cages 表中查找 LocationType 匹配的行
                    // 然后取出该行的 CageID 字段（这里假设你的模型属性名是 CageID）
                        cageCode = db.Cages
                        .Where(c => c.LocationType == snapshotCageCode)
                        .Select(c => c.CageCode) // 如果模型中字段叫 ID，请改为 .Select(c => c.ID)
                        .FirstOrDefault();

                    // 检查是否找到了结果，防止后续代码报错
                    if (string.IsNullOrEmpty(cageCode))
                    {
                        // 逻辑处理：如果没有找到对应的记录，可以赋默认值或抛出异常
                        cageCode = "NOT_FOUND";
                    }
                }
                // 【改动点 2-2】：紧接着查 Layers 表，获取该笼子实际拥有的层（格子）档案
                List<Layer> allLayersInDb = null;
                using (var db = new WarehouseDbContext())
                {
                    allLayersInDb = db.Layers
                                      .AsNoTracking()
                                      .Where(l => l.CageID == cageCode)
                                      .OrderBy(l => l.LayerNo) // 确保格子按层号顺序排布
                                      .ToList();
                }

                if (allLayersInDb == null || allLayersInDb.Count == 0)
                {
                    Application.Current.Dispatcher.Invoke(() => LayerList.Clear());
                    _isLoading = false;
                    return;
                }
                // 【改动点 1-1】：基于数据库实际查出的层数，动态生成 Redis Keys（不再写死 120）
                var keys = allLayersInDb.Select(l => (StackExchange.Redis.RedisKey)$"CageLayer:{cageCode}:{l.LayerNo}").ToArray();
                var redisValues = RedisHelper.Db.StringGet(keys);




                // A. 查笼子基础信息 (缓存30分钟)
                // 调用自定义的缓存服务工具类 CacheQueryService
                // <Cages> 表示这个函数最终会返回（或者缓存）一个 Cages 类型的列表
                var cageList = CacheQueryService.GetCachedData<Cage>(

                    // 参数 1：Redis 的 Key。例如 "CageInfo:A"
                    // 程序会先拿着这个 Key 去 Redis 查，如果查到了，直接返回数据，下面大括号里的代码根本不会运行
                    $"CageInfo:{cageCode}",

                    // 参数 2：查询委托（Callback）。这是一个“后备方案”
                    // 只有当 Redis 里找不到 "CageInfo:A" 时，程序才会执行下面这段代码
                    () =>
                    {
                        // 开启数据库上下文连接（执行完大括号后会自动关闭连接，释放资源）
                        using (var db = new WarehouseDbContext())
                        {
                            // 在 Cages 表里寻找 CageCode 等于当前笼子编号（如 "A"）的第一条记录
                            // .AsNoTracking() 是为了只读，不让 EF 跟踪，提高查询速度
                            // .Trim().ToUpper() 是为了防止数据库里有空格或者大小写不一致导致的匹配失败
                            var cage = db.Cages.AsNoTracking().FirstOrDefault(c => c.CageCode != null && cageCode != null && c.CageCode.Trim().ToUpper() == cageCode.ToUpper());

                            // 如果找到了(cage != null)，就把它包装成 List 返回
                            // 如果没找到，就返回一个空的 List
                            return cage != null ? new List<Cage> { cage } : new List<Cage>();
                        }
                    },

                    // 参数 3：缓存有效期。设置为 30 分钟
                    // 意思是从数据库拿回数据后，存入 Redis，并告诉 Redis：这个信息 30 分钟内不用再查数据库了
                    TimeSpan.FromMinutes(30)
                );

                // cageList 是你刚才查出来的列表。
                // 必须满足两个条件：1. 列表对象本身不是 null； 2. 列表里面至少有 1 条数据。
                // 这样可以防止后面取 cageList[0] 时因为“查无此笼”而导致程序崩溃（报“索引超出范围”错误）。
                if (cageList != null && cageList.Count > 0)
                {
                    // 第二步：跨线程调度（重点！）
                    // 因为 LoadData 是在 Task.Run 后台线程运行的，而 WPF 规定：只有主线程（UI线程）才能修改界面元素。
                    // Dispatcher.Invoke 的意思就是：“先等一下，把下面这行改界面的活儿，插队交给主线程去干”。
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 第三步：赋值更新
                        // 把查到的第 1 条笼子信息（List 里的索引 0），赋值给窗体的 CurrentCage 属性。
                        // 一旦赋值，界面（XAML）里绑定了笼子名称、规格的地方就会立刻“刷”地一下更新出来。
                        this.CurrentCage = cageList[0];
                    });
                }

                // --- 【核心改进部分】 ---
                //定位”和“存取”内存（Redis）中某一笼、某一层的整行数据
                // 1. 准备批量 Keys (避免在循环内重复创建)

                
                //Models 文件夹下，有一个专门的类文件叫 Layer.cs ，是List<Layer>的定义
               // List<Layer> allLayersInDb = null;
                List<Material> allMaterialsInDb = null;
                var tempLayers = new List<CageLayerViewModel>();
                // 声明一个临时字典，用来装本次数据库查询到的订单信息
                Dictionary<string, Order> ordersDict = null;
               
                for (int i = 0; i < allLayersInDb.Count; i++)
                {
                    var layerEntity = allLayersInDb[i]; // <--- 补上这一行，否则下面会报 layerEntity 不存在
                    int layerNo = layerEntity.LayerNo ?? 0; // 真实层号
                    CageLayerViewModel vm = null;

                    // 3. 直接从刚才拿到的“内存列表”里取数据
                    var redisValue = redisValues[i];
                    // 【核心改动 1】：如果是定时器触发 (forceRefresh = false) 且 Redis 有缓存
                    if (!forceRefresh && redisValue.HasValue)
                    {
                        // 1. 直接拿缓存用
                        vm = JsonConvert.DeserializeObject<CageLayerViewModel>(redisValue);

                        
                    }

                    // 【核心改动 2】：如果上面没拿到缓存（过期了），或者由于点击按钮被【强制刷新】穿透
                    if (vm == null || forceRefresh)
                    {    //循环第二层 此时 allLayersInDb 已经不是 null 了，它装满了 A 笼所有层的数据 跳过查数据库。
                        if (allMaterialsInDb == null)
                        {
                            using (var db = new WarehouseDbContext())
                            {     //查询数据库中属于特定笼子（如 A 笼）的所有层信息
                               // allLayersInDb = db.Layers.AsNoTracking().Where(l => l.CageID == cageCode).ToList();
                                //这行代码的作用确实是获取当前存放在 A 笼（或指定笼号）中的所有物料信息
                                allMaterialsInDb = db.Materials.AsNoTracking().Where(m => m.CurrentCage != null && m.CurrentCage.Trim().ToUpper() == snapshotCageCode.Trim().ToUpper()).ToList();

                                // 2. 【新增】一次性取出这批物料对应的所有订单，转成字典备用
                                var orderIds = allMaterialsInDb.Select(m => m.OrderID).Where(id => id != null).Distinct().ToList();
                                var ordersList = db.Orders.AsNoTracking()
                   .Where(o => o.OrderID != null && orderIds.Contains(o.OrderID))
                   .ToList();

                                ordersDict = new Dictionary<string, Order>();
                                foreach (var o in ordersList)
                                {
                                    if (o.OrderID != null && !ordersDict.ContainsKey(o.OrderID))
                                    {
                                        ordersDict.Add(o.OrderID, o);
                                    }
                                }
                            }
                        }

                        //“我是 A 笼 5 层，我现在准备好要显示了。”
                        vm = new CageLayerViewModel { LayerNo = layerNo, CageID = cageCode };
                        ////layerEntity 说：“我找到了数据库里关于 A 笼 5 层的原始档案。”
                        //var layerEntity = allLayersInDb
                        //    .FirstOrDefault(l => l.LayerNo == layerNo && l.CageID.Trim().ToUpper() == cageCode.Trim().ToUpper());

                        //如果在层中能找到信息 笼和层的信息 那我就在物料表中接着找 我要比对
                        if (layerEntity != null)
                        {
                            var mat = allMaterialsInDb .FirstOrDefault(m => m.CurrentLayer != null
                                && m.CurrentLayer == layerNo
                                && m.CurrentCage != null
                                && m.CurrentCage.Trim().ToUpper() == snapshotCageCode.Trim().ToUpper());
                            //MessageBox.Show($"layers: {allLayersInDb.Count}, materials: {allMaterialsInDb.Count}");
                            // --- 核心改动：从字典里拿数据 ---
                            Order currentOrder = null;
                            if (mat != null && ordersDict != null)
                            {
                                ordersDict.TryGetValue(mat.OrderID ?? "", out currentOrder);
                            }
                            // 传给 LoadFromEntity
                            vm.LoadFromEntity(
                                layerEntity,
                                mat,
                                currentOrder?.CustomerName ?? "",
                                currentOrder?.OrderNo ?? "",
                                currentOrder?.FlowCardNo ?? "",
                                currentOrder?.Id   // <-- 这里就是你要求的：订单表的自增ID作为定序
                            );
                        }
                        else
                        {
                            vm.IsOccupied = false;
                        }

                        // 5. 存回 Redis 并设置 4 秒强制过期，保证实时刷新
                        _ = RedisHelper.Db.StringSetAsync(keys[i], JsonConvert.SerializeObject(vm), TimeSpan.FromSeconds(5));
                    }
                    tempLayers.Add(vm);
                }

                // 6. 更新 UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // === 【新增：如果列表是空的，或者切笼子了，直接重新灌入对应的格子载体】 ===
                    if (LayerList.Count != tempLayers.Count)
                    {
                        LayerList.Clear();
                        foreach (var item in tempLayers)
                        {
                            // 创建一个干净的格子载体放进集合，以便后续动态更新
                            LayerList.Add(new CageLayerViewModel { LayerNo = item.LayerNo, CageID = item.CageID });
                        }
                    }
                    foreach (var newData in tempLayers)
                    {
                        var existing = LayerList.FirstOrDefault(x => x.LayerNo == newData.LayerNo);
                        if (existing != null)
                        {
                            // 只有数据变了才更新，减少 UI 重绘压力
                            // if (existing.MaterialID != newData.MaterialID || existing.IsOccupied != newData.IsOccupied)
                            // {
                            existing.IsOccupied = newData.IsOccupied;
                            existing.MaterialID = newData.MaterialID;

                            // --- 核心：确保这些来自 Orders 的字段被更新到 UI ---
                            existing.ClientName = newData.ClientName;
                            existing.OrderNo = newData.OrderNo;
                            existing.FlowCardNo = newData.FlowCardNo;

                            // 物理属性
                            existing.Length = newData.Length;
                            existing.Width = newData.Width;
                            existing.Thickness = newData.Thickness;
                            // --- 核心：确保定序（订单ID）也被更新到界面的格子中 ---
                            existing.Sequence = newData.Sequence;
                            // }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载失败:\n{ex.Message}\n{ex.InnerException?.Message}");
                
            }
            finally { _isLoading = false; }
        }

        private void OnLayerSelected(object parameter)
        {
            if (parameter is CageLayerViewModel layer)
            {
                if (SelectedLayer != null) SelectedLayer.IsSelected = false;
                SelectedLayer = layer;
                SelectedLayer.IsSelected = true;
            }
        }

        // 点击 A 笼按钮
        private void SwitchToCageA_Click(object sender, RoutedEventArgs e)
        {
            if (currentCageCode == "A") return;
            // ======= 【直接在这里变色】 =======
            btnCageA.Background = new System.Windows.Media.BrushConverter().ConvertFrom("#4169E1") as System.Windows.Media.Brush; // 变成蓝色
            btnCageA.Foreground = System.Windows.Media.Brushes.White;

            btnCageB.Background = System.Windows.Media.Brushes.LightGray; // B 恢复默认灰
            btnCageB.Foreground = System.Windows.Media.Brushes.Black;
            // =================================
            // 【安全修复】：先停掉定时器，防止它在切换的骨骨眼上插刀
            _refreshTimer.Stop();

            // 1. 切换核心标识
            currentCageCode = "A";

            // 2. 在主线程立刻把旧缓存端掉
            //CacheQueryService.ClearCacheByPattern("CageLayer:A:*");

            // 3. 异步去强制刷新最新数据
            Task.Run(() =>
            {
                LoadData(true);

                // 加载完了重新把定时器开起来
                Application.Current.Dispatcher.Invoke(() => _refreshTimer.Start());
            });
        }

        // 点击 B 笼按钮
        private void SwitchToCageB_Click(object sender, RoutedEventArgs e)
        {
            if (currentCageCode == "B") return;
            // ======= 【直接在这里变色】 =======
            btnCageB.Background = new System.Windows.Media.BrushConverter().ConvertFrom("#4169E1") as System.Windows.Media.Brush; // 变成蓝色
            btnCageB.Foreground = System.Windows.Media.Brushes.White;

            btnCageA.Background = System.Windows.Media.Brushes.LightGray; // A 恢复默认灰
            btnCageA.Foreground = System.Windows.Media.Brushes.Black;
            // =================================
            // 【安全修复】：先停掉定时器
            _refreshTimer.Stop();

            // 1. 切换核心标识为 B
            currentCageCode = "B";

            // 2. 在主线程立刻把旧缓存端掉
            //CacheQueryService.ClearCacheByPattern("CageLayer:B:*");

            // 3. 异步去强制刷新最新数据
            Task.Run(() =>
            {
                LoadData(true);

                // 加载完了重新把定时器开起来
                Application.Current.Dispatcher.Invoke(() => _refreshTimer.Start());
            });
        }




        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}