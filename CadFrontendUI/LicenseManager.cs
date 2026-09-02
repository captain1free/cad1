using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions; // 引入正则替代笨重的 JSON 解析库
using System.Windows.Forms;

namespace ZWCAD_Plugin
{
    // ==========================================
    // 1. C++ 底层桥接 (P/Invoke)
    // ==========================================
    internal static class NativeSecurity
    {
        // 动态加载 C++ 引擎
#if ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#else
        private const string DllName = "CadMultiPlatformProj.arx";
#endif

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern void GetHardwareId(StringBuilder outBuffer, int maxLen);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int CheckLicense(string base64Data, string base64Signature);

        // 【新增安全特性】：向 C++ 索要真实授权状态
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)] // 安全封送 C++ 的 bool 到 C#
        public static extern bool IsAuthorized();
    }

    // ==========================================
    // 2. 授权管理 (只负责网络和文件读写)
    // ==========================================
    public class LicenseManager
    {
        private const string API_URL = "http://122.51.18.87/api.php?action=activate";
        private static string LicenseFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZwCadPlugin", "license.lic");

        public static string GetMachineCode()
        {
            try
            {
                StringBuilder sb = new StringBuilder(64);
                NativeSecurity.GetHardwareId(sb, 64); // 直接向 C++ 索要绝对准确的机器码
                return sb.ToString();
            }
            catch
            {
                return "C++_ENGINE_NOT_LOADED"; // 如果 C++ 核心库没加载，直接返回错误
            }
        }

        // 【新增行为】：弹出状态 / 或者注册窗
        public static void ShowAuthStatus()
        {
            bool isAuth = false;
            try
            {
                isAuth = NativeSecurity.IsAuthorized();
            }
            catch
            {
                MessageBox.Show("C++ 安全引擎未加载或通讯被拦截！", "安全防御拦截", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string machineCode = GetMachineCode();

            if (isAuth)
            {
                MessageBox.Show($"安全引擎状态: \t运行中\n授权验证状态: \t已永久激活\n\n您的专属机器识别码:\n{machineCode}\n\n感谢您对正版核心算法的支持！",
                                "软件授权状态查询", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                using (var form = new RegisterForm(machineCode))
                {
                    form.ShowDialog();
                }
            }
        }

        public static bool CheckOrRegister()
        {
            string machineCode = GetMachineCode();

            // 1. 读本地文件，直接扔给 C++ 去验
            if (File.Exists(LicenseFilePath))
            {
                string localKey = File.ReadAllText(LicenseFilePath).Trim();
                if (PassToCppForVerify(localKey, out _)) return true;
            }

            // 2. 尝试静默联网
            if (TryAutoActivate(machineCode)) return true;

            // 3. 弹窗
            using (var form = new RegisterForm(machineCode))
            {
                return form.ShowDialog() == DialogResult.OK;
            }
        }

        public static bool PassToCppForVerify(string fullLicense, out string errorMsg)
        {
            errorMsg = "未知错误";
            try
            {
                string cleanLicense = fullLicense.Replace("\r", "").Replace("\n", "").Replace(" ", "");

                string[] parts = cleanLicense.Split('.');
                if (parts.Length != 2)
                {
                    errorMsg = "注册码格式错误 (缺少 . 分隔符)";
                    return false;
                }

                int resultCode = NativeSecurity.CheckLicense(parts[0], parts[1]);

                switch (resultCode)
                {
                    case 1: return true;
                    case -1: errorMsg = "底层错误: 业务数据 Base64 解码失败"; break;
                    case -2: errorMsg = "底层错误: 签名数据 Base64 解码失败"; break;
                    case -3: errorMsg = "授权无效: 机器码不匹配 (该注册码属于另一台电脑)"; break;
                    case -4: errorMsg = "授权无效: 授权数据损坏 (缺少 | 分隔符)"; break;
                    case -5: errorMsg = "授权无效: 日期格式解析失败"; break;
                    case -6: errorMsg = "授权拦截: 该注册码已过期！"; break;
                    case -7: errorMsg = "底层错误: C++ 内存导入公钥失败"; break;
                    case -8: errorMsg = "防伪拦截: RSA 签名校验未通过 (注册码被篡改或私钥不匹配)"; break;
                    default: errorMsg = $"底层未知错误: 错误码 {resultCode}"; break;
                }
                return false;
            }
            catch (Exception ex)
            {
                errorMsg = "系统异常: " + ex.Message;
                return false;
            }
        }

        private static bool TryAutoActivate(string machineCode)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    string jsonPayload = $"{{\"machine_code\":\"{machineCode}\"}}";
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var response = client.PostAsync(API_URL, content).Result;
                    if (!response.IsSuccessStatusCode) return false;

                    string respString = response.Content.ReadAsStringAsync().Result;

                    if (respString.Contains("\"code\":200") || respString.Contains("\"code\": 200"))
                    {
                        Match match = Regex.Match(respString, @"""license_key""\s*:\s*""([^""]+)""");
                        if (match.Success)
                        {
                            string serverKey = match.Groups[1].Value;
                            if (PassToCppForVerify(serverKey, out _))
                            {
                                SaveLicense(serverKey);
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static void SaveLicense(string key)
        {
            try
            {
                string dir = Path.GetDirectoryName(LicenseFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(LicenseFilePath, key);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存授权文件失败。\n" + ex.Message);
            }
        }
    }

    // ==========================================
    // 3. 注册窗口 (UI优化)
    // ==========================================
    public class RegisterForm : Form
    {
        private TextBox txtMachineCode;
        private TextBox txtLicense;
        private Label lblStatus;
        private string _machineCode;

        public RegisterForm(string machineCode)
        {
            _machineCode = machineCode;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "软件授权激活";
            this.Size = new Size(460, 360);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);

            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            this.Controls.Add(mainPanel);

            Label lblTitle = new Label { Text = "需要激活才能继续使用", Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true, Top = 10, Left = 15, ForeColor = Color.FromArgb(0, 102, 204) };
            mainPanel.Controls.Add(lblTitle);

            Label lblCode = new Label { Text = "您的机器码 (请发送给管理员):", Top = 50, Left = 15, AutoSize = true };
            txtMachineCode = new TextBox { Top = 75, Left = 15, Width = 320, ReadOnly = true, Text = _machineCode, BackColor = Color.White };
            Button btnCopy = new Button { Text = "复制", Top = 73, Left = 345, Width = 80, Height = 25, Cursor = Cursors.Hand };
            btnCopy.Click += (s, e) => { Clipboard.SetText(_machineCode); MessageBox.Show("机器码已复制到剪贴板"); };

            mainPanel.Controls.Add(lblCode);
            mainPanel.Controls.Add(txtMachineCode);
            mainPanel.Controls.Add(btnCopy);

            Label lblKey = new Label { Text = "输入注册码:", Top = 120, Left = 15, AutoSize = true };
            txtLicense = new TextBox { Top = 145, Left = 15, Width = 410, Height = 100, Multiline = true, ScrollBars = ScrollBars.Vertical };

            mainPanel.Controls.Add(lblKey);
            mainPanel.Controls.Add(txtLicense);

            Button btnRegister = new Button { Text = "验证并激活", Top = 260, Left = 15, Width = 410, Height = 40, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnRegister.Click += BtnRegister_Click;

            mainPanel.Controls.Add(btnRegister);

            lblStatus = new Label { Text = "提示: 未联网也可使用离线注册码激活", Top = 310, Left = 15, AutoSize = true, ForeColor = Color.Gray };
            mainPanel.Controls.Add(lblStatus);
        }

        private void BtnRegister_Click(object sender, EventArgs e)
        {
            string inputKey = txtLicense.Text.Trim();
            if (string.IsNullOrEmpty(inputKey))
            {
                MessageBox.Show("请输入注册码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (LicenseManager.PassToCppForVerify(inputKey, out string errorMsg))
            {
                LicenseManager.SaveLicense(inputKey);
                MessageBox.Show("激活成功！\n感谢您的支持。", "恭喜", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("激活失败:\n\n" + errorMsg, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}