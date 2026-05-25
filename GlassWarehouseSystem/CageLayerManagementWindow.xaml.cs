using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;

namespace GlassWarehouseSystem
{
    public partial class CageLayerManagementWindow : Window
    {
        private List<Layer> _generatedLayers = new List<Layer>();

        public CageLayerManagementWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadCages();
        }

        private void LoadCages()
        {
            try
            {
                using (var context = new WarehouseDbContext())
                {
                    var cages = context.Cages.ToList();
                    CageListBox.ItemsSource = cages;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载笼列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddNew_Click(object sender, RoutedEventArgs e)
        {
            RightPanel.Visibility = Visibility.Visible;
            ClearInputs();
            CageListBox.SelectedItem = null;
        }

        private void CageListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CageListBox.SelectedItem is Cage selectedCage)
            {
                RightPanel.Visibility = Visibility.Visible;
                LoadCageData(selectedCage);
            }
        }

        private void LoadCageData(Cage cage)
        {
            TxtCageCode.Text = cage.CageCode;
            TxtEquipmentName.Text = cage.EquipmentName ?? "";
            TxtCageType.Text = cage.CageType?.ToString() ?? "";
            TxtLayerCount.Text = cage.LayerCount?.ToString() ?? "";
            TxtLength.Text = cage.Length?.ToString() ?? "";
            TxtWidth.Text = cage.Width?.ToString() ?? "";
            TxtHeight.Text = cage.Height?.ToString() ?? "";
            TxtZeroCoordinate.Text = cage.ZeroCoordinate?.ToString() ?? "";
            TxtNegativeLimit.Text = cage.NegativeLimit?.ToString() ?? "";
            TxtPositiveLimit.Text = cage.PositiveLimit?.ToString() ?? "";
            TxtInboundGap.Text = cage.InboundGap?.ToString() ?? "";
            TxtOutboundGap.Text = cage.OutboundGap?.ToString() ?? "";
            TxtCageSequenceNo.Text = cage.CageSequenceNo?.ToString() ?? "";
            TxtXCoordinate.Text = cage.XCoordinate?.ToString() ?? "";
            TxtYCoordinate.Text = cage.YCoordinate?.ToString() ?? "";
            TxtTransitionLayerNo.Text = cage.TransitionLayerNo?.ToString() ?? "";
            TxtGridInitSeqNo.Text = cage.GridInitSeqNo?.ToString() ?? "";
            TxtGridStartCoord.Text = cage.GridStartCoord?.ToString() ?? "";
            ApplyLocationTypeToRadio(cage.LocationType);
            TxtSpace.Text = cage.Space?.ToString() ?? "";
            ChkIsOneWay.IsChecked = cage.IsOneWay ?? false;

            using (var context = new WarehouseDbContext())
            {
                var layers = context.Layers.Where(l => l.CageID == cage.CageCode).OrderBy(l => l.LayerNo).ToList();
                foreach (var l in layers)
                {
                    l.LayerSpacing = cage.Space;
                }

                if (layers.Any())
                {
                    var first = layers.First();
                    TxtLayerLength.Text = first.Length?.ToString() ?? first.RemainingLength?.ToString() ?? "";
                    TxtLayerWidth.Text = first.Width?.ToString() ?? "";
                    TxtLayerBatchCount.Text = layers.Count.ToString();
                    TxtLayerStartSeq.Text = first.LayerNo?.ToString() ?? "";
                    // 与初始化公式「起始坐标 + n×间距」一致时，首层 (n=0) 的坐标即起始坐标
                    TxtLayerDefaultStartCoord.Text = first.Coordinate?.ToString() ?? "";

                    _generatedLayers = layers;
                    LayersDataGrid.ItemsSource = null;
                    LayersDataGrid.ItemsSource = _generatedLayers;
                }
                else
                {
                    TxtLayerLength.Clear();
                    TxtLayerWidth.Clear();
                    TxtLayerStartSeq.Clear();
                    TxtLayerBatchCount.Clear();
                    TxtLayerDefaultStartCoord.Clear();
                    _generatedLayers = new List<Layer>();
                    LayersDataGrid.ItemsSource = null;
                }
            }
        }

        private void ClearInputs()
        {
            TxtCageCode.Clear();
            TxtEquipmentName.Clear();
            TxtCageType.Clear();
            TxtLayerCount.Clear();
            TxtLength.Clear();
            TxtWidth.Clear();
            TxtHeight.Clear();
            TxtZeroCoordinate.Clear();
            TxtNegativeLimit.Clear();
            TxtPositiveLimit.Clear();
            TxtInboundGap.Clear();
            TxtOutboundGap.Clear();
            TxtCageSequenceNo.Clear();
            TxtXCoordinate.Clear();
            TxtYCoordinate.Clear();
            TxtTransitionLayerNo.Clear();
            TxtGridInitSeqNo.Clear();
            TxtGridStartCoord.Clear();
            RdoLocationA.IsChecked = false;
            RdoLocationB.IsChecked = false;
            TxtSpace.Clear();
            ChkIsOneWay.IsChecked = false;
            TxtLayerLength.Clear();
            TxtLayerWidth.Clear();
            TxtLayerStartSeq.Clear();
            TxtLayerBatchCount.Clear();
            TxtLayerDefaultStartCoord.Clear();
            _generatedLayers.Clear();
            LayersDataGrid.ItemsSource = null;
        }

        private void InitializeLayers_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputsForInit())
                return;

            _generatedLayers.Clear();

            string cageCode = TxtCageCode.Text.Trim();
            decimal space = decimal.Parse(TxtSpace.Text.Trim());
            decimal layerLength = decimal.Parse(TxtLayerLength.Text.Trim());
            decimal layerWidth = decimal.Parse(TxtLayerWidth.Text.Trim());
            int startSeq = string.IsNullOrWhiteSpace(TxtLayerStartSeq.Text)
                ? 0
                : int.Parse(TxtLayerStartSeq.Text.Trim());
            int batchCount = string.IsNullOrWhiteSpace(TxtLayerBatchCount.Text)
                ? int.Parse(TxtLayerCount.Text.Trim())
                : int.Parse(TxtLayerBatchCount.Text.Trim());
            decimal startCoord = string.IsNullOrWhiteSpace(TxtLayerDefaultStartCoord.Text)
                ? 0m
                : decimal.Parse(TxtLayerDefaultStartCoord.Text.Trim());

            for (int n = 0; n < batchCount; n++)
            {
                int layerNo = startSeq + n;
                // 坐标 = 起始坐标 + n×层间距（n 为本批第几层，从 0 起；层序号仍为「起始序号+n」）
                decimal coord = startCoord + n * space;
                var layer = new Layer
                {
                    LayerID = $"{cageCode}-{layerNo:D3}",
                    CageID = cageCode,
                    LayerNo = layerNo,
                    RemainingLength = (double)layerLength,
                    Length = (double)layerLength,
                    Width = (double)layerWidth,
                    LayerSpacing = space,
                    Coordinate = coord,
                    IsOccupied = false,
                    IsDamaged = false
                };
                _generatedLayers.Add(layer);
            }

            LayersDataGrid.ItemsSource = null;
            LayersDataGrid.ItemsSource = _generatedLayers;
            MessageBox.Show($"已生成 {batchCount} 层数据", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>初始化层：校验笼编码、层默认尺寸、间距；数量/起始序号/起始坐标可空（分别默认层数、0、0）。</summary>
        private bool ValidateInputsForInit()
        {
            if (string.IsNullOrWhiteSpace(TxtCageCode.Text))
            {
                MessageBox.Show("请输入笼编码", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(TxtSpace.Text, out _))
            {
                MessageBox.Show("请输入有效的间距", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(TxtLayerLength.Text, out _))
            {
                MessageBox.Show("请输入有效的长度", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(TxtLayerWidth.Text, out _))
            {
                MessageBox.Show("请输入有效的宽度", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(TxtLayerStartSeq.Text))
            {
                if (!int.TryParse(TxtLayerStartSeq.Text.Trim(), out int startSeq) || startSeq < 0)
                {
                    MessageBox.Show("请输入有效的起始序号（非负整数）", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(TxtLayerBatchCount.Text))
            {
                if (!int.TryParse(TxtLayerCount.Text?.Trim(), out int layerCountFromCage) || layerCountFromCage <= 0)
                {
                    MessageBox.Show("数量未填写时，请在笼基本信息中填写有效的层数。", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            else if (!int.TryParse(TxtLayerBatchCount.Text.Trim(), out int bc) || bc <= 0)
            {
                MessageBox.Show("请输入有效的数量（正整数）", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(TxtLayerDefaultStartCoord.Text)
                && !decimal.TryParse(TxtLayerDefaultStartCoord.Text.Trim(), out _))
            {
                MessageBox.Show("请输入有效的起始坐标", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>保存笼：校验笼基本信息与层数等（含栅格起始坐标）。</summary>
        private bool ValidateInputsForSave()
        {
            if (string.IsNullOrWhiteSpace(TxtCageCode.Text))
            {
                MessageBox.Show("请输入笼编码", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!int.TryParse(TxtLayerCount.Text, out int layerCount) || layerCount <= 0)
            {
                MessageBox.Show("请输入有效的层数", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(TxtSpace.Text, out _))
            {
                MessageBox.Show("请输入有效的层间距", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(TxtLayerLength.Text, out _))
            {
                MessageBox.Show("请输入有效的长度", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(TxtLayerWidth.Text, out _))
            {
                MessageBox.Show("请输入有效的宽度", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(TxtGridStartCoord.Text, out _))
            {
                MessageBox.Show("请输入有效的栅格起始坐标", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputsForSave())
                return;

            if (_generatedLayers.Count == 0)
            {
                MessageBox.Show("请先初始化层数据", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var context = new WarehouseDbContext())
                {
                    var cage = new Cage
                    {
                        CageCode = TxtCageCode.Text.Trim(),
                        EquipmentName = string.IsNullOrWhiteSpace(TxtEquipmentName.Text) ? null : TxtEquipmentName.Text.Trim(),
                        CageType = ParseNullableInt(TxtCageType.Text),
                        LayerCount = int.Parse(TxtLayerCount.Text),
                        Length = decimal.Parse(TxtLength.Text),
                        Width = decimal.Parse(TxtWidth.Text),
                        Height = decimal.Parse(TxtHeight.Text),
                        ZeroCoordinate = ParseNullableDecimal(TxtZeroCoordinate.Text),
                        NegativeLimit = ParseNullableDecimal(TxtNegativeLimit.Text),
                        PositiveLimit = ParseNullableDecimal(TxtPositiveLimit.Text),
                        InboundGap = ParseNullableDecimal(TxtInboundGap.Text),
                        OutboundGap = ParseNullableDecimal(TxtOutboundGap.Text),
                        CageSequenceNo = ParseNullableInt(TxtCageSequenceNo.Text),
                        XCoordinate = ParseNullableDecimal(TxtXCoordinate.Text),
                        YCoordinate = ParseNullableDecimal(TxtYCoordinate.Text),
                        TransitionLayerNo = ParseNullableInt(TxtTransitionLayerNo.Text),
                        GridInitSeqNo = ParseNullableInt(TxtGridInitSeqNo.Text),
                        GridStartCoord = decimal.Parse(TxtGridStartCoord.Text),
                        Space = decimal.Parse(TxtSpace.Text),
                        LocationType = GetSelectedLocationTypeCode(),
                        IsOneWay = ChkIsOneWay.IsChecked,
                        UpdateTime = DateTime.Now,
                        Layers = _generatedLayers
                    };

                    context.Cages.Add(cage);
                    context.SaveChanges();

                    MessageBox.Show("保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadCages();
                    ClearInputs();
                    RightPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "无内部异常";
                MessageBox.Show($"保存失败: {ex.Message}\n\n内部异常: {innerMsg}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (CageListBox.SelectedItem is not Cage selectedCage)
            {
                MessageBox.Show("请先选择要删除的笼", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"确定要删除笼 {selectedCage.CageCode} 及其所有层吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using (var context = new WarehouseDbContext())
                {
                    var cage = context.Cages.FirstOrDefault(c => c.CageCode == selectedCage.CageCode);
                    if (cage != null)
                    {
                        context.Cages.Remove(cage);
                        context.SaveChanges();
                        MessageBox.Show("删除成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadCages();
                        ClearInputs();
                        RightPanel.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "无内部异常";
                MessageBox.Show($"删除失败: {ex.Message}\n\n内部异常: {innerMsg}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 界面为「A笼 / B笼」单选，写入数据库的 <see cref="Cages.LocationType"/> 为单字符 "A" 或 "B"。
        /// </summary>
        private string? GetSelectedLocationTypeCode()
        {
            if (RdoLocationA.IsChecked == true)
            {
                return "A";
            }

            if (RdoLocationB.IsChecked == true)
            {
                return "B";
            }

            return null;
        }

        /// <summary>
        /// 加载时兼容历史数据："A"/"B"、"A笼"/"B笼"（不区分大小写）。
        /// </summary>
        private void ApplyLocationTypeToRadio(string? locationType)
        {
            RdoLocationA.IsChecked = false;
            RdoLocationB.IsChecked = false;

            var raw = (locationType ?? string.Empty).Trim();
            if (raw.Length == 0)
            {
                return;
            }

            var head = raw[..1];
            if (string.Equals(head, "A", StringComparison.OrdinalIgnoreCase))
            {
                RdoLocationA.IsChecked = true;
            }
            else if (string.Equals(head, "B", StringComparison.OrdinalIgnoreCase))
            {
                RdoLocationB.IsChecked = true;
            }
        }

        private static decimal? ParseNullableDecimal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return decimal.TryParse(text.Trim(), out var v) ? v : null;
        }

        private static int? ParseNullableInt(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return int.TryParse(text.Trim(), out var v) ? v : null;
        }

        private void MarkLayersDamaged_Click(object sender, RoutedEventArgs e)
        {
            if (LayersDataGrid.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要标记为破损的层", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var selected = LayersDataGrid.SelectedItems.Cast<Layer>().ToList();
                using (var context = new WarehouseDbContext())
                {
                    foreach (var layer in selected)
                    {
                        var dbLayer = context.Layers.FirstOrDefault(x => x.LayerID == layer.LayerID);
                        if (dbLayer != null)
                        {
                            dbLayer.IsDamaged = true;
                        }

                        layer.IsDamaged = true;
                    }

                    context.SaveChanges();
                }

                LayersDataGrid.ItemsSource = null;
                LayersDataGrid.ItemsSource = _generatedLayers;
                MessageBox.Show($"已标记 {selected.Count} 个层为破损。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "无内部异常";
                MessageBox.Show($"标记失败: {ex.Message}\n\n内部异常: {innerMsg}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MarkLayersGood_Click(object sender, RoutedEventArgs e)
        {
            if (LayersDataGrid.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要标记为良好的层", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var selected = LayersDataGrid.SelectedItems.Cast<Layer>().ToList();
                using (var context = new WarehouseDbContext())
                {
                    foreach (var layer in selected)
                    {
                        var dbLayer = context.Layers.FirstOrDefault(x => x.LayerID == layer.LayerID);
                        if (dbLayer != null)
                        {
                            dbLayer.IsDamaged = false;
                        }

                        // 同步界面展示字段
                        layer.IsDamaged = false;
                    }

                    context.SaveChanges();
                }

                LayersDataGrid.ItemsSource = null;
                LayersDataGrid.ItemsSource = _generatedLayers;
                MessageBox.Show($"已标记 {selected.Count} 个层为良好。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "无内部异常";
                MessageBox.Show($"标记失败: {ex.Message}\n\n内部异常: {innerMsg}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BatchDelete_Click(object sender, RoutedEventArgs e)
        {
            if (LayersDataGrid.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要删除的层", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"确定要删除选中的 {LayersDataGrid.SelectedItems.Count} 个层吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using (var context = new WarehouseDbContext())
                {
                    var selectedLayers = LayersDataGrid.SelectedItems.Cast<Layer>().ToList();
                    foreach (var layer in selectedLayers)
                    {
                        var dbLayer = context.Layers.FirstOrDefault(x => x.LayerID == layer.LayerID);
                        if (dbLayer != null)
                        {
                            context.Layers.Remove(dbLayer);
                        }
                        _generatedLayers.Remove(layer);
                    }
                    context.SaveChanges();
                    LayersDataGrid.ItemsSource = null;
                    LayersDataGrid.ItemsSource = _generatedLayers;
                    MessageBox.Show("删除成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "无内部异常";
                MessageBox.Show($"删除失败: {ex.Message}\n\n内部异常: {innerMsg}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
