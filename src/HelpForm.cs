/* -*- coding: utf-8 -*-
 * HelpForm.cs — 使用说明窗体（首次启动自动弹出，也可从左侧导航栏「使用说明」随时查看）
 *
 * 内容随功能更新。版本号/制作人从 AppInfo 取，不再写死 ——
 * 以前这里写死"RTSP 实时框"之类的旧说法，功能改了说明书没跟上。
 * C# 5 兼容语法。
 */
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VideoChecker
{
    public class HelpForm : Form
    {
        public HelpForm(bool firstRun)
        {
            AppInfo.SetFormIcon(this);
            Text = "使用说明 — " + AppInfo.Title;
            Font = new Font("Microsoft YaHei", 9F);
            ClientSize = new Size(820, 660);
            MinimumSize = new Size(700, 520);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = true;
            MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 250);

            Label head = new Label();
            head.Text = firstRun ? "使用说明（首次使用请先读一遍）" : "使用说明";
            head.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
            head.AutoSize = true;
            head.Location = new Point(16, 12);
            Controls.Add(head);

            RichTextBox box = new RichTextBox();
            box.ReadOnly = true;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Color.White;
            box.Font = new Font("Microsoft YaHei", 9.5F);
            box.WordWrap = true;
            box.ScrollBars = RichTextBoxScrollBars.Vertical;
            box.Text = BuildText();
            box.Select(0, 0);
            box.Location = new Point(16, 44);
            box.Size = new Size(ClientSize.Width - 32, ClientSize.Height - 44 - 58);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(box);

            CheckBox chk = null;
            if (firstRun)
            {
                chk = new CheckBox();
                chk.Text = "下次启动不再显示";
                chk.Font = new Font("Microsoft YaHei", 8.5F);
                chk.AutoSize = true;
                chk.Location = new Point(16, ClientSize.Height - 42);
                chk.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
                Controls.Add(chk);
            }

            Button ok = new RoundButton();
            ok.Text = firstRun ? "开始使用" : "关闭";
            ok.Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold);
            ok.Size = new Size(110, 32);
            ok.Location = new Point(ClientSize.Width - 126, ClientSize.Height - 44);
            ok.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            ok.Click += delegate(object s, EventArgs e)
            {
                if (chk != null && chk.Checked)
                {
                    try
                    {
                        File.WriteAllText(AppPaths.Data(".help_shown"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), new System.Text.UTF8Encoding(false));
                    }
                    catch (Exception) { }
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);
            AcceptButton = ok;

            Load += delegate(object s, EventArgs e)
            {
                Themes.ApplyTo(this);
                Themes.StylePrimary(ok);

                // RichTextBox 的 ForeColor 只对「之后写入」的文字生效 ——
                // 上面是先赋 Text、后应用主题，所以深色主题下会黑字黑底、完全看不见内容。
                // 必须改完颜色再把文本重新赋一遍。
                box.ForeColor = Themes.Current.Fg;
                box.BackColor = Themes.Current.Card;
                string body = box.Text;
                box.Text = body;
                box.Select(0, 0);
            };
        }

        /// <summary>说明书正文。功能有增删时记得同步改这里。</summary>
        private static string BuildText()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("【" + AppInfo.Title + "  " + AppInfo.Version + " — 使用说明】");
            // ★ 这一行**故意写成内联的 QQ**：开源版靠 tools\make_opensource.ps1 把它
            //   替换成 AppInfo.ContactTail()（QQ 留空时就不显示那一段）✓
            //   所以别改成 ContactTail() —— 私有版的 AppInfo 里没有那个方法 ✗
            sb.AppendLine("制作人：" + AppInfo.Author + AppInfo.ContactTail()
                + "    更新日期：" + AppInfo.BuildDate);
            sb.AppendLine("============================================================");
            sb.AppendLine();
            sb.AppendLine("一、界面总览");
            sb.AppendLine("  窗口左侧是一列图标导航，点一下就切换功能：");
            // ★ 从 MainForm.NavItems 生成（唯一来源 ✓）
            //   原来是手写的 4 行 ✗ 我加了「AI 实时巡检」图标却没改这里 ✓
            //   说明里从"第 3 个"直接跳到"后 3 个" ✓ 用户一眼看出来了 ✓
            for (int i = 0; i < MainForm.NavItems.Length; i++)
            {
                sb.AppendLine("    第 " + (i + 1) + " 个图标 → " + MainForm.NavItems[i].Title
                            + "（" + MainForm.NavItems[i].HelpDesc + "）");
            }
            sb.AppendLine("  右上角可随时切换界面主题（共 " + Themes.All.Length + " 套，见第六节）。");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine("二、本地视频检测（最常用）");
            sb.AppendLine();
            sb.AppendLine("  2.1 操作步骤");
            sb.AppendLine("   ① 点「浏览...」选视频文件或整个文件夹（也可直接把文件拖进窗口）；");
            sb.AppendLine("   ② 勾选要检查的内容（见 2.2）；");
            sb.AppendLine("   ③ 选检测模式（见 2.3）；");
            sb.AppendLine("   ④ 点「开始检测」，等进度条跑完；");
            sb.AppendLine("   ⑤ 点「打开报告」看结果；点「归档分类」会按结果自动复制文件，");
            sb.AppendLine("      方便分别处理。");
            sb.AppendLine();
            sb.AppendLine("  2.2 检测内容（可任意组合）");
            sb.AppendLine("       音频完整性 —— 有无静音段、音量异常");
            sb.AppendLine("       视频完整性 —— 有无黑屏、画面冻结、花屏、帧率异常");
            sb.AppendLine("       音画同步   —— 声音与画面是否对得上");
            sb.AppendLine("       音频质量   —— 电流麦 / 底噪 / 爆音（较慢，按需勾选）");
            sb.AppendLine("       AI 画面理解 —— 让 AI 描述画面内容（需 Ollama，见第四节）");
            sb.AppendLine();
            sb.AppendLine("  2.3 检测模式");
            sb.AppendLine("       「全片分析」—— 整片分析，最准确。长视频慢，但会实时显示阶段、");
            sb.AppendLine("                     已处理时长和预计剩余时间，随时能点「停止」；");
            sb.AppendLine("       「快速」    —— 只取前 60 秒采样，用于快速摸底。");
            sb.AppendLine();
            sb.AppendLine("  2.4 判定方式");
            sb.AppendLine("       「关键项保护」默认勾选 —— 黑屏 / 静音 / 花屏这类致命问题会被");
            sb.AppendLine("       「一票否决」直接判失败，不会被及格率冲淡。取消勾选则改为纯");
            sb.AppendLine("       及格率制（≥90% 通过 / ≥70% 一般）。");
            sb.AppendLine();
            sb.AppendLine("  2.5 长视频为什么不慢");
            sb.AppendLine("       · CPU + GPU 一起解码：长视频会自动切成若干段，一段走显卡、");
            sb.AppendLine("         其余走 CPU，同时跑，最后合并结果（比纯软件解码快 2 倍以上）；");
            sb.AppendLine("       · 状态栏全程显示「阶段 + 已处理 / 总时长 + 预计剩余」；");
            sb.AppendLine("       · 想再快可以开「激进模式」（见 5.2）—— 代价是 AI 功能会被停用。");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine("三、RTSP 实时检测（监控摄像头）");
            sb.AppendLine("  从左侧导航栏第 2 个图标进入。这个页面专门用于网络摄像头拉流检测，");
            sb.AppendLine("  与本地文件检测是两套独立的判定逻辑（实时流没有时长，很多文件检测");
            sb.AppendLine("  项在这里没有意义）。");
            sb.AppendLine();
            sb.AppendLine("  3.1 单路快速检测");
            sb.AppendLine("      填「流地址」（例：rtsp://admin:密码@192.168.1.64:554/Streaming/");
            sb.AppendLine("      Channels/101），填「备注」起个机位名，点「开始分析」。");
            sb.AppendLine("      「预览一帧」可直接抓一帧看摄像头通不通、画面正不正常。");
            sb.AppendLine();
            sb.AppendLine("  3.2 流列表与备注");
            sb.AppendLine("      点「添加/更新」把当前地址存进列表，以后不用再手敲长地址。");
            sb.AppendLine("      列表存在程序目录的 data\\rtsp_streams.dat，下次启动自动载入。");
            sb.AppendLine("      ★ 这个文件是**加密**的（Windows DPAPI，绑定本机当前账号）：");
            sb.AppendLine("        界面和报告里密码一律打码，文件里也看不到明文；");
            sb.AppendLine("        换个 Windows 账号或换台机器就解不开（这是正常的，不是坏了）。");
            sb.AppendLine("      双击列表某一行，可把该路地址载入上方做单路细查。");
            sb.AppendLine();
            sb.AppendLine("  3.3 CSV 批量导入");
            sb.AppendLine("      点「导入 CSV」一次导入多个机位。格式：每行一路「备注,流地址」；");
            sb.AppendLine("      地址列可放在任意位置；以 # 开头的行会被忽略（可写注释）；");
            sb.AppendLine("      重复地址自动跳过。不确定格式就点「导出模板」照着填。");
            sb.AppendLine("      注：Excel 文件请先「另存为 CSV UTF-8」再导入。");
            sb.AppendLine();
            sb.AppendLine("  3.4 多路轮巡");
            sb.AppendLine("      在列表「轮巡」列勾选要检的机位，点「开始轮巡」即逐路自动检测，");
            sb.AppendLine("      结果实时回填列表的「总评 / 合格率 / 摘要」列，最后汇总成一份报告。");
            sb.AppendLine();
            sb.AppendLine("  3.5 检测项说明（可自由勾选组合）");
            sb.AppendLine("      帧率丢帧 —— 标称帧率 vs 实际收到多少帧，反映丢包 / 卡顿");
            sb.AppendLine("      码率     —— 每秒码率是否稳定，反映带宽是否够用");
            sb.AppendLine("      关键帧   —— 关键帧间隔，太大则录像回放拖动难定位");
            sb.AppendLine("      画面动态 —— 长时间无变化 = 疑似卡死或镜头被遮挡");
            sb.AppendLine("      音频     —— 音量与静音分布");
            sb.AppendLine("      拾音器   —— 识别拾音器故障（典型特征是持续交流嗡鸣）");
            sb.AppendLine("      时间水印 —— 读画面时间水印，验证「确实是实时在录」（需 AI）");
            sb.AppendLine();
            sb.AppendLine("      关于「仅安静段判定」（拾音器检测的子选项）：");
            sb.AppendLine("        勾上（默认）—— 只在没人说话时才用市电成分判故障，结论可靠；");
            sb.AppendLine("        取消       —— 任何时刻都检查，但只给提示、不给结论。");
            sb.AppendLine("                      因为说话本身也带低频能量，容易把说话声当成");
            sb.AppendLine("                      交流声（假故障）。");
            sb.AppendLine();
            sb.AppendLine("  3.6 网络搜索摄像头（不用记各家不同的取流地址）");
            sb.AppendLine("      不同品牌的 RTSP 路径完全不一样，填错就报 Stream Not Found：");
            sb.AppendLine("        海康     /Streaming/Channels/101");
            sb.AppendLine("        TP-Link  /stream1");
            sb.AppendLine("        大华     /cam/realmonitor?channel=1&subtype=0");
            sb.AppendLine("      点「网络搜索」按钮就不用管这些了：");
            sb.AppendLine("        · 同时用 ONVIF 和 SSDP/UPnP 搜局域网里的摄像头；");
            sb.AppendLine("        · 自动挨个试出能用的取流地址，直接列在列表里；");
            sb.AppendLine("        · 选中一台 → 填账号密码 → 点「填进地址栏」即可。");
            sb.AppendLine("        （地址栏里已有的账号密码会自动预填进对话框）");
            sb.AppendLine("      ★ 注意：连续输错密码，有些摄像头会把账号临时锁掉");
            sb.AppendLine("        （需等 10~30 分钟，或把摄像头断电重启）。");
            sb.AppendLine("        程序检测到 401 会立刻停止尝试，不会反复试。");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine("四、AI 功能（可选；不用 AI 的整节跳过）");
            sb.AppendLine();
            sb.AppendLine("  4.1 准备（做一次就行）");
            sb.AppendLine("   ① 安装并启动 Ollama（https://ollama.com）；");
            sb.AppendLine("   ② 拉一个带 vl 的视觉模型：   ollama pull qwen2.5vl:7b");
            sb.AppendLine("      推荐 qwen2.5vl:7b：速度快、读水印稳；qwen3-vl:8b 更细但慢很多；");
            sb.AppendLine("   ③ 到「设置」页填 Ollama 地址与模型（几个 AI 页面共用这一份配置）。");
            sb.AppendLine();
            sb.AppendLine("  4.2 内容理解与文搜（第 3 个图标）");
            sb.AppendLine("      作用：AI 逐帧理解全片内容并读时间水印；输入关键词（如「红色 汽车」）");
            sb.AppendLine("            即可搜到对应画面，双击列表行或时间轴一键定位。");
            sb.AppendLine("      版面：上半部分左右分栏 —— 左栏画面（按视频真实比例定宽，不留黑边）、");
            sb.AppendLine("            右栏 AI 内容列表（位置 / 画面时间 / 判定 / AI 理解内容四列）；");
            sb.AppendLine("            中间竖线可左右拖动调两栏比例；下半部分保持整宽。");
            sb.AppendLine("      时间轴上有两条互不干扰的游标：");
            sb.AppendLine("        · 橙色手柄 = AI 游标：拖动它只浏览 AI 结果，底部实时显示");
            sb.AppendLine("          「那一刻 AI 看到了什么」，不会动视频；");
            sb.AppendLine("        · 青色竖线 = 视频当前位置：只读，跟着播放走。");
            sb.AppendLine("      真正跳过去：双击时间轴 / 点「跳到这一帧」→ 视频跳到 AI 游标处；");
            sb.AppendLine("                  点「预告片」→ 从游标位置起依次跳到后面的内容点");
            sb.AppendLine("                  （每个停 1.4 秒，用来快速过一遍片子）。");
            sb.AppendLine("      播放方式（程序自动选）：从开头播 → 系统播放器（最流畅）；");
            sb.AppendLine("      从中间播 → 关键帧逐帧推进（约 25 帧/秒）。原因：监控录像多是 H.265、");
            sb.AppendLine("      关键帧间隔大，系统播放器「跳到指定位置」并不可靠 —— 实测它会跳回");
            sb.AppendLine("      开头；关键帧推进的起点是精确的，代价只是画面略不如原生播放顺滑。");
            sb.AppendLine("      播放时状态栏显示「起点 xx:xx / xx:xx」，可据此确认起点是否正确。");
            sb.AppendLine("      结果可「导出列表」存成 CSV，方便溯源。");
            sb.AppendLine();
            sb.AppendLine("  4.3 AI 实时巡检（第 4 个图标，眼睛形状）");
            sb.AppendLine("      作用：让摄像头自己盯着 —— 画面有变化时自动抓图分析，发现异常就");
            sb.AppendLine("            记进表格和文件，不用人一直看着。");
            sb.AppendLine("      三种巡检方式（页面上选，选错程序会提示你换）：");
            sb.AppendLine("        · 报警接收（推荐）");
            sb.AppendLine("            摄像头检测到画面变化时，主动把现场图片推给本程序；");
            sb.AppendLine("            不轮询、不建 RTSP 会话，负载最低，短事件也不会漏。");
            sb.AppendLine("            适用于海康、大华等支持「报警上传 / 上传中心」的机型。");
            sb.AppendLine("            程序启动时会自动把本机地址填进摄像头的报警上传设置。");
            sb.AppendLine("            前提：事件 → 移动侦测 → 布防时间要覆盖你想监控的时段，");
            sb.AppendLine("                  联动方式里勾上「上传中心」。");
            sb.AppendLine("        · 事件订阅");
            sb.AppendLine("            挂摄像头的事件流（alertStream）等推送。");
            sb.AppendLine("            注意：实测部分机型只会通过它推异常类事件，移动侦测收不到");
            sb.AppendLine("                  —— 那种情况请改用「报警接收」。");
            sb.AppendLine("        · 定时轮询");
            sb.AppendLine("            每 N 秒抓一帧分析。所有摄像头都能用，但有轮询开销，两次");
            sb.AppendLine("            采样之间的动作会漏。间隔建议不小于 10 秒。");
            sb.AppendLine("      输出：每轮结果写进 data\\live_alerts.csv（跨天会自动分卷成");
            sb.AppendLine("            live_alerts_日期.csv；地址已打码，密码不落明文）。");
            sb.AppendLine("      心跳信息（比如视频信号丢失）不占主记录 —— 点「事件流水」按钮");
            sb.AppendLine("      可以看小窗里的全部原始事件。");
            sb.AppendLine();
            sb.AppendLine("  4.4 RTSP 页的「时间水印」");
            sb.AppendLine("      读拉流开头与结尾两帧的水印并对比：");
            sb.AppendLine("      水印在走 = 确实在实时录制；两次读数相同 = 疑似在放静止录像。");
            sb.AppendLine("      注意：AI 读水印的「年份」不可靠（实测两个模型都会读错），");
            sb.AppendLine("      但「有没有在走」这个判断是可靠的。");
            sb.AppendLine();
            sb.AppendLine("  4.5 两条要记住的规则");
            sb.AppendLine("      · AI 任务全局互斥：同一时间只允许一个 AI 任务运行（同时跑会把");
            sb.AppendLine("        显存和推理队列挤满，两边都变慢甚至超时）；");
            sb.AppendLine("      · 开了「激进模式」时 AI 全部停用（见 5.2）—— 要用 AI 先关掉它。");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine("五、性能与显卡（想让长视频更快就看这节）");
            sb.AppendLine();
            sb.AppendLine("  5.1 显卡检测与选择");
            sb.AppendLine("      程序启动时会在后台自动检测：本机有哪些显卡能用于硬件解码。");
            sb.AppendLine("      结果在「设置 → 性能」里能看到，也可以在那里指定用哪一块：");
            sb.AppendLine("        · 「自动（推荐）」 —— 由程序自己挑一块；");
            sb.AppendLine("        · 指定某一块     —— 例如同时有核显和独显时，可指定独显来解码；");
            sb.AppendLine("        · 「重新检测」   —— 换了显卡 / 驱动之后点一下重新探测。");
            sb.AppendLine("      ★ 某块卡啃不动这个片子时，程序会自动退到「自动」，再不行退到");
            sb.AppendLine("        「纯 CPU 解码」—— 宁可慢一点，也不会因此把好片子判成坏片子。");
            sb.AppendLine();
            sb.AppendLine("  5.2 激进模式（CPU + GPU 全力加速）");
            sb.AppendLine("      开启后：硬件解码门槛从 10 分钟降到 1 分钟、并行分段门槛从 10 分钟");
            sb.AppendLine("      降到 2 分钟、分段上限从 4 段提到 6 段 —— 长视频明显更快。");
            sb.AppendLine("      代价：**全部 AI 理解会被停用**。因为 AI 推理和硬件解码会抢同一块");
            sb.AppendLine("      显卡的显存，一起上容易爆显存；要用 AI 就到设置页关掉激进模式。");
            sb.AppendLine("      ★ 没检测到可用显卡时，这个选项是**灰的、勾不上** ——");
            sb.AppendLine("        只有 CPU 可用时，激进模式没有意义（它靠的就是 CPU 和 GPU 一起出力）。");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine("六、设置与主题");
            sb.AppendLine();
            sb.AppendLine("  6.1 设置页包含（左侧导航栏最后一个图标）");
            sb.AppendLine("      · AI   —— Ollama 服务地址、视觉模型、抽帧帧数、抽帧策略；");
            sb.AppendLine("      · 性能 —— 显卡检测与选择、激进模式（见第五节）；");
            sb.AppendLine("      · 界面 —— 主题（也可在主窗体右上角直接切换）；");
            sb.AppendLine("      · 安全 —— 设置 / 修改 / 取消开屏密码；");
            sb.AppendLine("      · 存储 —— 查看报告 / 归档 / 日志各占多少，按保留数量清理。");
            sb.AppendLine("      配置存在程序目录的 data\\ 子目录里（settings.ini 与 .theme），");
            sb.AppendLine("      也可以用记事本手工编辑。");
            sb.AppendLine();
            sb.AppendLine("  6.2 界面主题");
            sb.AppendLine("      共 " + Themes.All.Length + " 套：");
            {
                string line = "      ";
                for (int i = 0; i < Themes.All.Length; i++)
                {
                    if (i > 0 && i % 5 == 0)
                    {
                        sb.AppendLine(line);
                        line = "      ";
                    }
                    if (i % 5 > 0) line += " / ";
                    line += Themes.All[i].Name;
                }
                if (line.Trim().Length > 0) sb.AppendLine(line);
            }
            sb.AppendLine("      网页报告里还能单独换配色（10 套），与界面主题互不影响。");
            sb.AppendLine();
            sb.AppendLine("  6.3 开屏密码（默认不设）");
            sb.AppendLine("      程序默认不需要密码 —— 打开就直接进入。");
            sb.AppendLine("      首次进入时会问你要不要设一个；也可以随时在「设置」页里设置。");
            sb.AppendLine("      · 密码以哈希形式存储（PBKDF2 + 随机盐），文件里看不到明文；");
            sb.AppendLine("      · 连续输错 5 次会锁定，等待时间逐级递增（1/5/15/60/240 分钟），");
            sb.AppendLine("        锁定状态写在文件里，重启程序也躲不过去；");
            sb.AppendLine("      · 设置密码后，命令行 / 批量模式也需要提供密码；");
            sb.AppendLine("      · 密码状态同时记在文件和系统里，删掉其中一个无法绕过解锁。");
            sb.AppendLine();
            sb.AppendLine("      说明：这是「客户端锁」，作用是防误触、防随手打开，不是防有心人");
            sb.AppendLine("      的安全边界。要真正保护数据请用 Windows 账户权限或加密磁盘。");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine("七、报告与归档");
            sb.AppendLine("  · 每次检测自动生成网页报告(HTML) + PDF，独立编号不覆盖，放在");
            sb.AppendLine("    reports\\ 下；网页报告支持按「通过 / 一般 / 失败 / 无法分析」筛选，");
            sb.AppendLine("    并可在报告内切换 10 套配色；");
            sb.AppendLine("  · 「归档分类」按结果把视频复制到 archive\\ 下，方便分别处理；");
            sb.AppendLine("  · RTSP 页可「导出列表」把多路结果存成 CSV；");
            sb.AppendLine("  · 所有过程写入「日志」窗口：排障时可切到 Debug 级别看细节，");
            sb.AppendLine("    也可以点「导出诊断包」把现场信息收成一个文件发出去；");
            sb.AppendLine("  · ★ 报告 / 归档视频 / 日志都是**明文文件**，密码锁不保护它们 ——");
            sb.AppendLine("    介意外借的话请自行用 NTFS 权限或加密盘。");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine("八、常见问题");
            sb.AppendLine("  1. H.265(HEVC) 监控视频（海康常见）画面用关键帧显示 ——");
            sb.AppendLine("     拖动进度条定位画面，不影响检测结果；");
            sb.AppendLine("  2. AI 没反应：确认 Ollama 已启动、设置页地址正确、模型已拉取；");
            sb.AppendLine("     提示「AI 正忙」= 另一个窗口的 AI 任务还在跑；");
            sb.AppendLine("     提示「激进模式已开启」= 到设置页把激进模式关掉；");
            sb.AppendLine("  3. 报 ffmpeg 错误：确认程序目录下 ffmpeg\\bin\\ 里有 ffmpeg.exe");
            sb.AppendLine("     和 ffprobe.exe；");
            sb.AppendLine("  4. RTSP 连不上：确认地址、账号密码、端口正确，且电脑与摄像头在同一");
            sb.AppendLine("     网段；程序默认用 TCP 拉流（比 UDP 稳）；");
            sb.AppendLine("  5. 摄像头一直报「无有效音频信号」：多半是该摄像头本就没接拾音器，");
            sb.AppendLine("     属正常现象，可取消勾选「音频」「拾音器」两项；");
            sb.AppendLine("  6. 报告打不开：用浏览器打开 .html；PDF 在同目录留档；");
            sb.AppendLine("  7. 程序异常：日志在程序目录 logs\\ 下，未处理异常记在 error.log，");
            sb.AppendLine("     点「日志」窗口里的「导出诊断包」可把现场收成一个文件；");
            sb.AppendLine("  8. RTSP 报「认证失败(401)」：账号或密码不对，或者之前连续认证失败");
            sb.AppendLine("     被摄像头临时锁号了。等 10~30 分钟，或把摄像头断电重启；");
            sb.AppendLine("  9. 摄像头能用哪种巡检方式：海康/大华支持「报警接收」；TP-Link、");
            sb.AppendLine("     小米、萤石这类一般只能用「定时轮询」。选错时程序会提示你换；");
            sb.AppendLine(" 10. 取流地址不知道填什么：点「网络搜索」按钮，让它自己找；");
            sb.AppendLine(" 11. 中途想停：点「停止」即可 —— 已经检完的文件结果会保留并出报告；");
            sb.AppendLine(" 12. 长视频还要跑多久：看状态栏的「已处理 / 总时长 + 预计剩余」，实时刷新。");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            // 免责声明与第三方组件：文本统一放在 AppInfo 里，避免多处写法不一致
            sb.AppendLine("九、免责声明");
            sb.AppendLine();
            foreach (string line in AppInfo.Disclaimer.Split('\n'))
                sb.AppendLine(line.Length > 0 ? "  " + line : "");
            sb.AppendLine();
            sb.AppendLine("十、第三方组件");
            sb.AppendLine();
            foreach (string line in AppInfo.ThirdParty.Split('\n'))
                sb.AppendLine(line.Length > 0 ? "  " + line : "");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------");
            // ★ 这是**累计**的功能清单（AppInfo.Highlights），不是"本版更新" ✗
            //   以前标题写「vX.Y 版主要更新」而内容是全历史清单 → 名不副实 ✓ 已改
            sb.AppendLine("附录、主要功能（累计清单）");
            for (int i = 0; i < AppInfo.Highlights.Length; i++)
                sb.AppendLine("  " + (i + 1) + ". " + AppInfo.Highlights[i]);
            sb.AppendLine();
            sb.AppendLine("============================================================");
            sb.AppendLine(AppInfo.Full());
            return sb.ToString();
        }
    }
}