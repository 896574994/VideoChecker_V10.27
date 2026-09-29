/* -*- coding: utf-8 -*-
 * SplashForm.cs — 启动页（制作人信息 + 页面开启锁）
 *
 * 密码不再写死：由 PassLock 管理（PBKDF2 哈希存储、可修改、连续错误 5 次锁定）。
 * 注意这是**客户端锁**，作用是防误触/防随手打开，不是防有心人的安全边界。
 * C# 5 兼容语法。
 */
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VideoChecker
{
    public class SplashForm : Form
    {
        /// <summary>密码校验（转发到 PassLock，保留此方法便于自检）。</summary>
        public static bool CheckPassword(string pwd)
        {
            return PassLock.Verify(pwd);
        }

        private TextBox _pwdBox;
        private Button _unlockBtn;
        private Label _msgLabel;
        private System.Windows.Forms.Timer _lockTimer;

        public SplashForm()
        {

            AppInfo.SetFormIcon(this);
            Text = "视频核对工具";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 460);
            Theme th = Themes.Current;
            BackColor = th.SplashBg1;
            DoubleBuffered = true;

            Paint += OnPaintBg;

            // 标题
            Label title = new Label();
            title.Text = AppInfo.Title;
            title.Font = new Font("Microsoft YaHei", 22F, FontStyle.Bold);
            title.ForeColor = th.SplashFg;
            title.AutoSize = true;
            title.Location = new Point(60, 60);
            Controls.Add(title);

            // 版本号统一从 AppInfo 取 —— 以前这里写死 "v1.7"，加了十几个功能都没人记得改
            Label sub = new Label();
            // 只放版本与日期：这一行宽度有限，加功能列表会被截断
            sub.Text = "C# 图形版  " + AppInfo.Version + "  ｜  " + AppInfo.BuildDate;
            sub.Font = new Font("Microsoft YaHei", 9F);
            sub.ForeColor = th.SplashSub;
            sub.AutoSize = true;
            sub.Location = new Point(62, 104);
            Controls.Add(sub);

            // 分隔线
            Label line = new Label();
            line.Text = "──────────────────────────────";
            line.ForeColor = th.SplashBg2;
            // ★ AutoSize 改 false + 定宽（2026-09-15）
            //   原来 AutoSize = true ✗ 30 个「─」按字体量出来约 360px ✓
            //   而它的起点是 x=58、窗体宽 420 → 右边只剩 362px ✗
            //   → **正好卡在边界上**（换个字体/DPI 就会伸出去）✓
            //   实测：check_layout.ps1 把开屏页纳进来之后当场报
            //     「✗ 跑出窗体: Label [────…]」✓
            //   这不只是"难看一点"✗ —— 它会**把窗体撑宽**，
            //   或者在边缘留一条被切断的线 ✓ 定宽之后由标签自己裁 ✓ 稳定 ✓
            line.AutoSize = false;
            line.Size = new Size(346, 12);
            line.Location = new Point(58, 140);
            Controls.Add(line);

            // 制作人
            Label maker = new Label();
            maker.Text = "制 作 人：" + AppInfo.Author;
            maker.Font = new Font("Microsoft YaHei", 13F, FontStyle.Bold);
            maker.ForeColor = th.SplashFg;
            maker.AutoSize = true;
            maker.Location = new Point(70, 175);
            Controls.Add(maker);

            Label qq = new Label();
            qq.Text = AppInfo.QQ.Length == 0 ? "" : "联系 QQ：" + AppInfo.QQ;   // 开源版留空 = 不显示
            qq.Font = new Font("Microsoft YaHei", 13F, FontStyle.Bold);
            qq.ForeColor = th.SplashAccent;
            qq.AutoSize = true;
            qq.Location = new Point(70, 210);
            Controls.Add(qq);

            // 密码区
            Label lockTip = new Label();
            lockTip.Text = "页面开启锁：请输入密码后进入";
            lockTip.Font = new Font("Microsoft YaHei", 10F);
            lockTip.ForeColor = th.SplashSub;
            lockTip.AutoSize = true;
            lockTip.Location = new Point(70, 270);
            Controls.Add(lockTip);

            _pwdBox = new TextBox();
            _pwdBox.Font = new Font("Microsoft YaHei", 12F);
            _pwdBox.Location = new Point(70, 300);
            _pwdBox.Size = new Size(200, 28);
            _pwdBox.UseSystemPasswordChar = true;
            _pwdBox.KeyDown += OnPwdKeyDown;
            Controls.Add(_pwdBox);

            _unlockBtn = new RoundButton();
            _unlockBtn.Text = "进 入";
            _unlockBtn.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
            _unlockBtn.BackColor = th.Accent;
            _unlockBtn.ForeColor = th.AccentFg;
            _unlockBtn.FlatStyle = FlatStyle.Flat;
            _unlockBtn.FlatAppearance.BorderSize = 0;
            _unlockBtn.Location = new Point(280, 297);
            _unlockBtn.Size = new Size(80, 34);
            _unlockBtn.Click += OnUnlock;
            Controls.Add(_unlockBtn);

            _msgLabel = new Label();
            _msgLabel.Font = new Font("Microsoft YaHei", 9F);
            _msgLabel.ForeColor = th.Danger;
            _msgLabel.AutoSize = true;
            _msgLabel.Location = new Point(70, 338);
            Controls.Add(_msgLabel);

            // ★★ 这里原来有个「不记得密码了？」链接 —— **2026-09-15 删掉了** ✗
            //
            //   用户一句话点穿：**「你为啥要在开屏密码加这个呢？这不是脱裤子放屁么？」**
            //   ★ 他说得对 ✓ 而且比"多余"更严重 —— 它是**唯一的真旁路** ✗
            //
            //   ① 「忘了密码」本来就有出路 ✓ 不需要它：
            //        PassLock 的头部设计第 ④ 条写着

            //        进去之后在设置页就能改密码 / 取消密码 ✓
            //      → 这个按钮**没有增加任何能力** ✓
            //
            //   ② 而它当初的理由是**错的** ✗：
            //        旧注释写「能物理接触电脑的人本来就能删掉 lock.dat，所以不降低安全性」
            //      ★ 实测代码（PassLock.cs 第 130 行那段「防'删文件即解锁'」）：
            //        密码状态同时存在 lock.dat **和注册表镜像**里 ✓
            //        文件没了 → 从镜像恢复 → **照样要密码**（要原密码，哈希就在镜像里）✓
            //        → **删文件根本解不开** ✗
            //      → 所以这个按钮**恰恰是唯一能解开它的东西** ✗
            //        而且**不需要任何秘密** ✓ 点两下就能清掉 ✓
            //
            //   ③ 这把锁的用途是「防误触、防随手打开」（见 PassLock.cs 头部）✓
            //      而"点一个下划线链接"**正是随手打开** ✗
            //      → 它把这个锁变成了**装饰** ✗
            //
            //   ④ 至于它当初要解决的那件事（卸载残留导致重装要密码）✓
            //      **根因已经在 v9.2 修掉了** ✓
            //      （卸载程序把 lock.dat 归成"程序状态"，卸载时删掉 ✓）
            //      另外「换目录 / 别人电脑」那类由 MirrorBelongsToHere 管着 ✓
            //      ——**为症状加的逃生口，根因修好之后就该撤掉** ✓
            //


            Label foot = new Label();
            foot.Text = "适配 Windows 7 / 10 / 11";
            foot.Font = new Font("Microsoft YaHei", 9F);
            foot.ForeColor = th.SplashSub;
            foot.AutoSize = true;
            foot.Location = new Point(120, 420);
            Controls.Add(foot);

            Shown += (s, e) => ApplyLockOnShown();
        }

        private void OnPaintBg(object sender, PaintEventArgs e)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                ClientRectangle, Themes.Current.SplashBg1, Themes.Current.SplashBg2, 45F))
            {
                e.Graphics.FillRectangle(b, ClientRectangle);
            }
        }

        private void OnPwdKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) TryUnlock();
        }

        /// <summary>
        private void OnUnlock(object sender, EventArgs e)
        {
            TryUnlock();
        }

        private void TryUnlock()
        {
            // 锁定期间不接受输入
            if (PassLock.IsLocked)
            {
                ShowLocked();
                return;
            }

            if (CheckPassword(_pwdBox.Text))
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            // 记一次失败：达到阈值会写盘锁定（重启程序也躲不过）
            string[] r = PassLock.RecordFailure();
            if (r[0] == "LOCKED")
            {
                _pwdBox.Text = "";
                _pwdBox.Enabled = false;
                _unlockBtn.Enabled = false;
                ShowLocked();
                return;
            }

            int left = PassLock.RemainingTries();
            _msgLabel.Text = "密码错误，请重新输入（再错 " + left + " 次将锁定）";
            _pwdBox.SelectAll();
            _pwdBox.Focus();
        }

        /// <summary>显示锁定状态并每秒刷新倒计时，到点自动恢复输入。</summary>
        private void ShowLocked()
        {
            string left = PassLock.LockRemaining();
            if (left.Length == 0)
            {
                // 锁定已结束，恢复输入
                if (_lockTimer != null) { _lockTimer.Stop(); _lockTimer.Dispose(); _lockTimer = null; }
                _pwdBox.Enabled = true;
                _unlockBtn.Enabled = true;
                _msgLabel.Text = "可以重新输入密码了";
                _pwdBox.Focus();
                return;
            }

            _msgLabel.Text = "密码连续错误次数过多，请等待 " + left + " 后重试";
            if (_lockTimer == null)
            {
                _lockTimer = new System.Windows.Forms.Timer();
                _lockTimer.Interval = 1000;
                _lockTimer.Tick += delegate(object s, EventArgs e) { ShowLocked(); };
                _lockTimer.Start();
            }
        }

        /// <summary>启动时若处于锁定状态，直接进入锁定显示。</summary>
        private void ApplyLockOnShown()
        {
            if (PassLock.IsLocked)
            {
                _pwdBox.Enabled = false;
                _unlockBtn.Enabled = false;
                ShowLocked();
            }
            else
            {
                _pwdBox.Focus();
                if (!PassLock.EverChanged)
                    _msgLabel.Text = "提示：这是你自己设置的密码吗？可在「设置」页修改";
            }
        }
    }
}