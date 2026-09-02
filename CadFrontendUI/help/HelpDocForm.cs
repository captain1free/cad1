#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
#elif ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
#endif

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Forms.Application;

namespace CadFrontendUI.Help
{
    /// <summary>
    /// 全栈架构：模块化分栏帮助文档窗体
    /// </summary>
    public class HelpDocForm : Form
    {
        private SplitContainer splitContainer;
        private ListBox lstModules;
        private RichTextBox rtbContent;
        private Button btnClose;

        // 存储各个模块的帮助字典
        private Dictionary<string, string> _helpContents;

        public HelpDocForm()
        {
            InitializeComponent();
            InitializeHelpData();

            // 默认选中第一项
            if (this.lstModules.Items.Count > 0)
            {
                this.lstModules.SelectedIndex = 0;
            }
        }

        private void InitializeComponent()
        {
            this.splitContainer = new SplitContainer();
            this.lstModules = new ListBox();
            this.rtbContent = new RichTextBox();
            this.btnClose = new Button();

            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.SuspendLayout();

            // 
            // splitContainer (分割面板，左侧导航，右侧内容)
            // 
            this.splitContainer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.splitContainer.Location = new Point(12, 12);
            this.splitContainer.Name = "splitContainer";

            // Panel1 (左侧导航)
            this.splitContainer.Panel1.Controls.Add(this.lstModules);
            // Panel2 (右侧内容)
            this.splitContainer.Panel2.Controls.Add(this.rtbContent);
            this.splitContainer.Size = new Size(760, 480);
            this.splitContainer.SplitterDistance = 200; // 左侧宽度
            this.splitContainer.TabIndex = 0;

            // 
            // lstModules (左侧列表)
            // 
            this.lstModules.Dock = DockStyle.Fill;
            this.lstModules.Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
            this.lstModules.FormattingEnabled = true;
            this.lstModules.ItemHeight = 20;
            this.lstModules.Name = "lstModules";
            this.lstModules.SelectedIndexChanged += new EventHandler(this.LstModules_SelectedIndexChanged);

            // 
            // rtbContent (右侧文本区)
            // 
            this.rtbContent.Dock = DockStyle.Fill;
            this.rtbContent.BackColor = Color.White;
            this.rtbContent.Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
            this.rtbContent.Name = "rtbContent";
            this.rtbContent.ReadOnly = true;
            this.rtbContent.Text = "";

            // 
            // btnClose (关闭按钮)
            // 
            this.btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.btnClose.Location = new Point(672, 505);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new Size(100, 35);
            this.btnClose.TabIndex = 1;
            this.btnClose.Text = "我知道了";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new EventHandler(this.BtnClose_Click);

            // 
            // HelpDocForm
            // 
            this.ClientSize = new Size(784, 552);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.splitContainer);
            this.MinimizeBox = false;
            this.Name = "HelpDocForm";
            this.ShowIcon = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "使用手册";

            this.splitContainer.Panel1.ResumeLayout(false);
            this.splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        /// <summary>
        /// 初始化帮助内容字典
        /// </summary>
        private void InitializeHelpData()
        {
            _helpContents = new Dictionary<string, string>();

            // 1. 通用说明
            _helpContents.Add("📝 首页与通用说明",
@"欢迎使用插件！

本插件基于 C# (.NET) 前端与 C++ 17 底层引擎打造，采用高安全性的数据封送与内存管理机制，为您提供极致的运行效率。

请在左侧选择对应模块查看详细帮助。");

            // 2. 坐标展点模块（基于你提供的源码提炼的硬核文档）
            _helpContents.Add("📍 坐标展点与提取",
@"【坐标展点与提取】
1.高程点导入（展点后可以是纯文字或 CASS 块格式）；
2.高程点分段设色导入（按高程范围自动赋色，生成图例）；
3.高程点导出（支持按图层、多边形区域等方式提取坐标）

1、高程点导入功能
  操作步骤
     点击坐标展点弹出 “坐标展点” 窗口；
     选择数据文件：点击 “...” 按钮，选择 .dat 或 .xyz 格式的坐标文件；
    (数据文件格式（.dat/.xyz）
            1,,100.000,200.000,50.123
            2,,105.000,200.000,52.456
            3,,100.000,205.000,48.789
            4,,105.000,205.000,51.234
            5,,102.500,202.500,50.567
                或者
            473937.355 2922779.46 18.22
            473938.676 2922781.142 17.734
            473939.989 2922782.759 17.685
            473941.466 2922784.403 17.585
            473942.945 2922785.947 17.651
            473944.341 2922787.461 17.619
     设置参数：
     文字高度：高程文字的大小，建议根据图纸比例设置（如 2.0）；
     最小间距：两点距离小于此值时自动抽稀（设为 0 则不抽稀）；
     小数位数：高程数值保留的小数位（0-6 位，默认 3 位）；
     点位格式：
        「展纯文字高程」：生成点 + 文字对象；
        「CASS 高程点」：生成 CASS 格式块参照（GC200）。
        点击 “确定”，等待导入完成，图纸会自动缩放至全图显示。

2、高程点分段设色导入功能
     操作步骤
     窗体中点击 展点颜色区分，选择 .dat 或 .xyz 文件；
     弹出 “高程点分段设色导入” 窗口，设置通用参数：
     文字高度、小数位、抽希间距（同普通导入）；
     设置高程分段与颜色：
     点击「添加分段」，新增一行；
     填写 “最小高程” 和 “最大高程”（如 0~50）；
     点击该行的「选择」按钮，在颜色框中挑选颜色；
     可重复添加多个分段（如 50~100、100~150 等）；
     选中分段行后点击「删除选中」可移除。
     点击 “极速导入”，等待完成后，图纸会生成带颜色的高程点和左上角图例。

3、高程点导出功能

操作步骤
    窗体中选择坐标导出；
    选择导出方式（命令行提示）：
    按图层 (L)：输入图层名，导出该图层下所有高程点；
    多边形区域 (S)：在图纸上绘制多边形，导出区域内的高程点；特别注意绘制完成后要 “F ”结束命令，否则会一直提示继续选择点；
    选择对象所在的图层 (O)：选中一个对象，导出其所在图层的高程点。
    按提示完成选择后，弹出 “保存文件” 窗口；
    选择保存位置，文件名默认 ExportedGeoElevPoints.dat，点击 “保存”；
    命令行提示导出完成，即可在保存位置找到 .dat 文件。
");

            // 3. 三角网模块 (预留)
            _helpContents.Add("📐 高程三角网 (TIN)",
@"【高程三角网】模块说明

核心功能一：地形边界自动提取
        
        ### 3.1 功能说明
        基于离散高程点云数据，通过Delaunay三角剖分（TIN不规则三角网）算法，自动生成精准的闭合地形边界线；支持自定义边长阈值，自动过滤边缘无效狭长三角面、剔除飞点/离群点，实现边界智能收缩，完美贴合实际地形轮廓。

        ### 3.2 详细操作步骤
        #### 步骤1：调用功能界面
        「地形边界提取」可视化操作窗口。

        #### 步骤2：选择高程点云数据文件
        1. 点击窗口中的「浏览(B)...」按钮；
        2. 在弹出的文件选择窗口中，选中你的离散高程点文件（支持格式：.csv`/`.dat`）；
        3. 点击「打开」，文件路径会自动填充到文本框中。

        #### 步骤3：设置核心参数
        窗口中唯一核心参数为**边界收缩最大边长阈值**，单位为「米」，默认值50.0米，参数说明如下：
        - 作用：三角网中任意一条边的长度超过该阈值，会被判定为「无效外部边」自动剔除，实现边界收缩；
        - 设置原则：**推荐设置为点云平均点间距的2~3倍**；
          - 数值越小：边界越贴合地形轮廓，过滤的外部三角网越多，收缩效果越强；
          - 数值越大：边界范围越大，保留的三角网越多，极端大值（如9999米）可生成最外围凸包边界；
          - 示例：点云平均间距10米，推荐设置20~30米；需剔除边缘稀疏点可设15米，需保留全范围可设100米。

        #### 步骤4：执行边界生成
        1. 确认文件路径和参数设置无误后，点击「极速生成边界」按钮；
        2. 按钮自动置灰，窗口下方进度条实时显示生成进度，CAD命令行同步输出运行日志；
        3. 等待进度条走到100%，命令行提示`[运算完成] 极速降维打击完毕，成功生成 X 条 3D 边界拓扑多段线！`，即完成操作。

        #### 步骤5：结果查看
        生成完成后，CAD会自动在当前图纸中生成闭合3D多段线地形边界；在命令行输入 **`Z` 回车 `E` 回车**，即可缩放至全图查看完整边界。
        ---
        ## 四、核心功能二：两期地形高程/土方差值分析
        **命令触发：`TINDIFF`**
        ### 4.1 功能说明
        用于对比两期地形的高程变化，精准计算土方填挖量、地形沉降/隆起值；通过TIN三角网空间插值算法，支持「点对点对比」「规则网格采样对比」两种计算模式，可按差值范围自动赋色，生成可视化差值文本、颜色图例，广泛用于土方验收、地形沉降监测、挖填方量计算。

        ### 4.2 详细操作步骤
        #### 步骤1：准备数据
        提前准备两份离散高程点文件：**一期基准地形文件**（如施工前地形）、**二期对比地形文件**（如施工后/验收地形），文件格式规范见本文第五章。

        #### 步骤2：调用功能界面
        在CAD命令行输入 **`TINDIFF`** 并回车，弹出「两期地形高程差值分析」窗口。

        #### 步骤3：选择数据文件
        分别点击「浏览」按钮，选择对应的「一期基准地形文件」和「二期对比地形文件」，确认路径无误。

        #### 步骤4：设置分析核心参数
        1. **最大边长阈值**：单位米，过滤两期TIN网中的超长无效三角面，推荐设置为点云平均点间距的2~3倍；
        2. **计算模式**：
           - 模式0：点云点对点对比：以二期点云的点位为基准，插值一期地形高程计算差值，保留原始点位精度；
           - 模式1：规则网格采样对比：按设置的网格步长生成均匀网格点，分别插值两期地形高程计算差值，适合生成标准化填挖分布图；
        3. **网格步长**：仅模式1生效，单位米，设置网格采样的间距；步长越小精度越高，耗时越长，推荐设置为原始点云平均点间距的1~2倍；
        4. **差值分段与颜色映射**：自定义高程差值范围与对应CAD颜色号，例如：
           | 差值范围（米） | 含义 | CAD颜色号 |
           |----------------|------|-----------|
           | -5 ~ -3        | 强挖方 | 1（红色） |
           | -3 ~ -1        | 弱挖方 | 2（黄色） |
           | -1 ~ 1         | 无变化 | 7（白色） |
           | 1 ~ 3          | 弱填方 | 5（蓝色） |
           | 3 ~ 5          | 强填方 | 6（洋红） |
           生成后差值文本会自动匹配对应颜色，直观区分填挖方。

        #### 步骤5：执行差值计算
        点击「开始差值分析」按钮，进度条实时显示计算进度，CAD命令行同步输出日志；等待计算完成，进度条走到100%即操作结束。

        #### 步骤6：结果查看
        计算完成后，CAD图纸中会自动生成3类结果：
        1. 点位高程差值文本：正数为填方、负数为挖方，按设置的颜色映射自动赋色；
        2. 差值范围颜色图例：位于图纸左上角，标注每个颜色对应的差值区间；
        3. 整体范围外包围盒：框选所有计算点位的完整范围。
        输入 **`Z` 回车 `E` 回车** 即可缩放全图查看完整结果。
        ---
        ## 五、数据格式规范与示例数据
        ### 5.1 通用格式要求
        - 文件类型：纯文本格式，支持后缀 `.txt`/`.csv`/`.dat`；
        - 编码格式：UTF-8 或 ANSI（推荐ANSI，兼容性最佳）；
        - 分隔符：支持**英文逗号、空格、Tab键**分隔，禁止使用中文逗号；
        - 内容要求：每行对应一个离散点，必须包含X平面坐标、Y平面坐标、Z高程值，均为数字格式，禁止字母、特殊字符；
        - 空行处理：程序自动过滤文件中的空行、无效行，无需手动删除。

        ### 5.2 标准示例数据
        #### 标准格式（测绘行业通用）：`点号,,X坐标,Y坐标,Z高程`
        ```
        1,,385200.123,2896000.456,125.32
        2,,385205.123,2896000.456,124.85
        3,,385200.123,2896005.456,125.16
        4,,385205.123,2896005.456,124.93
        5,,385202.500,2896002.500,125.05
        6,,385210.000,2896000.000,123.78
        7,,385210.000,2896010.000,124.22
        8,,385200.000,2896010.000,125.47
        ```
        #### 兼容简化格式
        程序可自动识别以下格式，无需手动转换：
        - 仅X,Y,Z三列：`385200.123,2896000.456,125.32`
        - 空格分隔：`1 385200.123 2896000.456 125.32`
        - Tab分隔：`1	385200.123	2896000.456	125.32`

        ### 5.3 错误格式示例（禁止使用）
        ```
        1，385200.123，2896000.456，125.32  // 使用中文逗号分隔
        1,,385200.123,2896000.456,高程125.32  // 包含非数字字符
        1,,385200.123,,125.32  // 缺少Y坐标
        ```
        ## 六、常见问题与解决方案（FAQ）
        ### Q1：输入命令后，CAD提示「未知命令」，怎么办？
        A1：核心原因是插件未成功加载，按以下步骤排查：
        1. 确认CAD平台与插件文件匹配：AutoCAD必须用`.arx`，中望CAD必须用`.zrx`，严禁混用；
        2. 重新执行`NETLOAD`命令加载插件，查看命令行是否有报错提示；
        3. 确认插件文件未被杀毒软件隔离/删除，可将插件文件夹加入杀毒软件白名单；
        4. 确认CAD为完整版，非绿色精简版（精简版可能缺少.NET运行环境），安装.NET Framework 4.5及以上版本。

        ### Q2：点击生成后，提示「数据解析异常，或无法构建有效拓扑网」，怎么办？
        A2：原因是点云数据无法构建有效TIN三角网，排查方案：
        1. 检查数据格式是否符合规范，是否存在中文逗号、非数字字符、坐标缺失；
        2. 确认点云数量≥3个，且3个点不共线（所有点X/Y坐标完全一致无法构建三角网）；
        3. 检查最大边长阈值是否设置过小（如0.001米），导致所有三角面被过滤，放大阈值重试。

        ### Q3：生成的边界范围过大，包含大量无点区域，怎么办？
        A3：原因是最大边长阈值设置过大，保留了边缘狭长无效三角面，解决方案：
        1. 减小「最大边长阈值」，推荐设置为点云平均点间距的2倍，重新生成；
        2. 提前删除数据中的飞点/离群点，避免生成无效外围三角面；
        3. 若需精准贴合地形，可逐步缩小阈值，多次测试找到最优值。

        ### Q4：生成结果后，CAD中看不到图形，怎么办？
        A4：解决方案：
        1. 输入 **`Z` 回车 `E` 回车**，CAD会自动缩放至全图范围，即可看到生成的图形；
        2. 检查当前图层是否被关闭/冻结，确保图层处于打开、解冻状态；
        3. 检查图形颜色是否与CAD背景色一致（如黑色背景+黑色线条），修改图形颜色即可。

       ");
            _helpContents.Add("不想写文档",
@"欢迎使用插件！
。");



            // 将字典的 Key 绑定到左侧 ListBox
            foreach (var key in _helpContents.Keys)
            {
                this.lstModules.Items.Add(key);
            }
        }

        private void LstModules_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.lstModules.SelectedItem != null)
            {
                string selectedModule = this.lstModules.SelectedItem.ToString();
                if (_helpContents.ContainsKey(selectedModule))
                {
                    this.rtbContent.Text = _helpContents[selectedModule];
                }
            }
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}