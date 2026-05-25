using System;
using System.Linq;
using System.Windows;
using GlassWarehouseSystem.Data;
// --- 关键：给数据库模型起个别名，防止和窗口类名 ControlPlcUser1 冲突 ---
using UserModel = GlassWarehouseSystem.Models.ControlPlcUser;

namespace GlassWarehouseSystem
{
    public partial class ControlPlcUser1 : Window
    {
        public ControlPlcUser1()
        {
            InitializeComponent();
            LoadUserListData();
        }

        private void LoadUserListData()
        {
            try
            {
                using (var db = new WarehouseDbContext())
                {
                    // 使用别名 UserModel 明确指定我们要找的是数据库里的用户实体
                    var users = db.ControlPlcUser.ToList();

                    cmbUser.ItemsSource = users;
                    cmbUpdateUser.ItemsSource = users;

                    // 这里的 u 就会自动识别为 UserModel 类型
                    var defaultUser = users.FirstOrDefault(u => u.Username == "Z") ?? users.FirstOrDefault();
                    if (defaultUser != null)
                    {
                        cmbUser.SelectedItem = defaultUser;
                        cmbUpdateUser.SelectedItem = defaultUser;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化用户列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region 登录逻辑
        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (cmbUser.SelectedValue == null)
            {
                MessageBox.Show("请选择账号！");
                return;
            }

            string selectedUser = cmbUser.SelectedValue.ToString();
            string password = txtPwd.Password;

            try
            {
                using (var db = new WarehouseDbContext())
                {
                    // 同样使用别名
                    var user = db.ControlPlcUser.FirstOrDefault(u => u.Username == selectedUser && u.Password == password);

                    if (user != null)
                    {
                        // --- 关键：设置 DialogResult 为 true，主窗口才能接收到解锁信号 ---
                        this.DialogResult = true;
                        this.Close();
                    }
                    else
                    {
                        MessageBox.Show("密码不正确，请重试。", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        txtPwd.Clear();
                        txtPwd.Focus();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库连接异常: {ex.Message}");
            }
        }
        #endregion

        #region 修改密码逻辑
        private void BtnUpdatePwd_Click(object sender, RoutedEventArgs e)
        {
            if (cmbUpdateUser.SelectedValue == null)
            {
                MessageBox.Show("请选择要修改的账号！");
                return;
            }

            string userName = cmbUpdateUser.SelectedValue.ToString();
            string oldPwd = txtOldPwd.Password;
            string newPwd = txtNewPwd.Password;

            if (string.IsNullOrWhiteSpace(newPwd))
            {
                MessageBox.Show("新密码不能为空！");
                return;
            }

            try
            {
                using (var db = new WarehouseDbContext())
                {
                    var dbUser = db.ControlPlcUser.FirstOrDefault(u => u.Username == userName && u.Password == oldPwd);

                    if (dbUser != null)
                    {
                        dbUser.Password = newPwd;
                        db.SaveChanges();

                        MessageBox.Show("密码修改成功！请使用新密码登录。");
                        txtOldPwd.Clear();
                        txtNewPwd.Clear();
                        BackToLogin_Click(null, null);
                    }
                    else
                    {
                        MessageBox.Show("旧密码验证失败，无法修改。", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"修改失败: {ex.Message}");
            }
        }
        #endregion

        #region 界面切换逻辑
        private void ShowUpdatePanel_Click(object sender, RoutedEventArgs e)
        {
            if (cmbUser.SelectedValue != null)
            {
                cmbUpdateUser.SelectedValue = cmbUser.SelectedValue;
            }
            LoginPanel.Visibility = Visibility.Collapsed;
            UpdatePanel.Visibility = Visibility.Visible;
        }

        private void BackToLogin_Click(object sender, RoutedEventArgs e)
        {
            txtPwd.Clear(); // 加上这一行
            UpdatePanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
        }
        #endregion
    }
}