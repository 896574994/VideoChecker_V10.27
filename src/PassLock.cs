/* -*- coding: utf-8 -*-
 * PassLock.cs — 开屏密码锁：可选启用 + 哈希存储 + 失败次数限制
 *
 * 设计：
 *   ① **默认不设密码** —— 首次运行直接进程序，进去后再提示用户设置；
 *   ② 密码以 PBKDF2(SHA1, 1000 次迭代) + 16 字节随机盐存储，文件里看不到明文；
 *   ③ 连续输错 5 次开始锁定，时长逐级递增（1/5/15/60/240 分钟），锁定状态写盘；
 *   ④ **不含任何万能密码 / 后门**：只认用户自己设的那个密码。
 *      忘记密码时的恢复办法 = 删掉程序目录下的 data\lock.dat，
 *      再删注册表 HKCU\Software\VideoChecker（两步都要做，详见 README）✓
 *
 * ⚠ 这个锁只是"防误触、防随手打开"，**不是安全边界**。
 *   要真正保护数据请用 Windows 账户权限或加密磁盘。
 *
 * C# 5 兼容语法。
 */
using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VideoChecker
{
    public static class PassLock
    {

        /// <summary>累计错误多少次后开始锁定。</summary>
        public const int MaxAttempts = 5;

        /// <summary>第 1/2/3/4/5+ 次触发锁定时的等待分钟数。</summary>
        private static readonly int[] LockMinutes = new int[] { 1, 5, 15, 60, 240 };

        private static string File_
        {
            get { return AppPaths.Data("lock.dat"); }
        }

        // ==================== 哈希 ====================

        private static byte[] Derive(string pwd, byte[] salt)
        {
            using (Rfc2898DeriveBytes d = new Rfc2898DeriveBytes(pwd, salt, 1000))
                return d.GetBytes(32);
        }

        private static bool SlowEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ==================== 状态 ====================

        private class State
        {
            public bool Enabled;                     // 是否启用了开屏密码
            public byte[] Salt;
            public byte[] Hash;
            public int FailCount;
            public DateTime LockUntil = DateTime.MinValue;
            public int LockLevel;
            /// <summary>用户是否已经改过密码（用于判断"还在用初始密码"的提示是否还适用）。</summary>
            public bool EverChanged;
        }

        /// <summary>本次是否检测到有人在删密码状态文件（见 Load）。</summary>
        // ★ 新增（2026-09-13）：文件不在、但系统记录说"设过密码"
        //   这时**不判篡改** ✗ 而是标记"待从镜像恢复" ✓
        //   理由见 Load() 里那段注释 —— 简单说：
        //     "文件不存在"**无法区分**「全新安装」和「被人删了」✗
        //     而镜像是完整状态的备份 ✓ 密码哈希还在里面 ✓
        //     所以完全可以正常验证密码 ✓ 验证通过后重建文件就行 ✓✓
        private static bool _pendingRestore;

        /// <summary>
        /// 密码状态文件不在、但系统记录（注册表镜像）里还有。
        /// ★ 语义改了（2026-09-13）：原来叫"检测到篡改" ✗
        ///   现在叫"待从记录恢复" ✓ —— 因为"文件不存在"无法区分
        ///   「全新安装」和「被人删了」✗ 而**卸载重装**长得就像后者 ✓
        ///   调用方据此显示的文案也不该再说"疑似有人删锁" ✗
        /// </summary>
        public static bool TamperDetected { get { Load(); return _pendingRestore; } }

        /// <summary>把状态文本解析成 State。fileText 为 null 表示文件不存在。</summary>
        private static State ParseState(string fileText)
        {
            State s = new State();
            if (fileText == null) return s;
            foreach (string raw in fileText.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                try
                {
                    if (k == "enabled") s.Enabled = (v == "1");
                    else if (k == "everchanged") s.EverChanged = (v == "1");
                    else if (k == "salt") s.Salt = Convert.FromBase64String(v);
                    else if (k == "hash") s.Hash = Convert.FromBase64String(v);
                    else if (k == "fail") { int n; if (int.TryParse(v, out n)) s.FailCount = n; }
                    else if (k == "locklevel") { int n; if (int.TryParse(v, out n)) s.LockLevel = n; }
                    else if (k == "lockuntil")
                    {
                        long ticks;
                        if (long.TryParse(v, out ticks) && ticks > 0) s.LockUntil = new DateTime(ticks);
                    }
                }
                catch (Exception) { }
            }
            return s;
        }

        private static State Load()
        {
            try
            {
                bool fileExists = File.Exists(File_);
                string fileText = fileExists ? File.ReadAllText(File_, Encoding.UTF8) : null;
                string mirrorText = ReadMirror();      // 注册表里的镜像（同样是 DPAPI 加密）

                // ★ 防"删文件即解锁"：
                //   密码状态同时记在 lock.dat 和注册表两处。
                //   如果文件没了、但注册表镜像还记着"曾经设过密码" —— 说明有人在删锁 ✗

                bool mirrorHere = MirrorBelongsToHere(mirrorText);
                // ★ 只有"这份镜像确实是本目录设的"才算数（2026-09-13 修）
                //   加这一条之前：只要这台机器曾经设过密码 ✗
                //   **任何目录**下的这个程序都要求密码 ✗
                //   → 分发给别人 / 换个目录解压 / 装到别处 → 全部弹密码框 ✗✗
                //   → 而对方不知道密码 → 永久进不去 ✗
                //   ★ 加上之后：
                //     你自己那个目录 → 路径一致 ✓ 防护照旧 ✓
                //     别人装到别处   → 路径不一致 ✓ 当成全新安装 ✓ 不弹密码 ✓✓
                if (!fileExists && mirrorText != null && !mirrorHere)
                {
                    Log.Info("密码状态文件不在，系统记录里那份是别的目录设的 —— 当作全新安装处理。");
                }

                if (!fileExists && mirrorText != null && mirrorHere)
                {
                    State m = ParseState(mirrorText);
                    if (m.Enabled)
                    {
                        // ★★ 这里原来判"篡改" ✗ 现在改成"待恢复" ✓（2026-09-13 修）
                        //
                        //   为什么改 —— 用户问「打包出来的程序加了开屏锁，别人怎么用？」
                        //
                        //   原来的逻辑：文件没了 + 镜像说设过密码 → 判篡改 ✗

                        //   问题：**"文件不存在"无法区分两种情况** ✗
                        //     ① 全新安装            → 正常，不该拦 ✓
                        //     ② 真有人删了 lock.dat → 该拦 ✓
                        //     ★ 而「卸载重装 / 换个目录解压 / 拷到别的文件夹」
                        //       全都长得像 ② ✗ 于是一个正经用户重装之后

                        //       （实测：把一个干净的发布版 exe 拷到空目录跑，
                        //         就被判成"删文件绕过" —— 因为这台机器注册表有残留）
                        //
                        //   ★ 而镜像是**完整状态的备份** ✓ 密码哈希就在里面 ✓
                        //     所以完全可以正常验证密码 ✓ 验证通过后用镜像重建文件 ✓
                        //
                        //   ★ 防护变弱了吗？**没有** ✓

                        //     变成"要原密码" ✓ 而原密码的哈希一直在镜像里 ✓
                        //     暴力破解难度一模一样 ✓
                        _pendingRestore = true;
                        Log.Info("密码状态文件不在，但系统记录里有 —— 将按记录验证密码并自动重建。");
                        return m;
                    }
                }

                if (!fileExists) return new State();   // 从来没有设过密码 = 直接进入

                State s = ParseState(fileText);
                // 文件在、镜像不在（比如换了电脑）→ 补写镜像，下次就能交叉比对了
                if (mirrorText == null && s.Enabled) WriteMirror(fileText);
                return s;
            }
            catch (Exception ex)
            {
                Log.Warn("读取密码锁状态失败：" + ex.Message);
                return new State();
            }
        }

        // ==================== 注册表镜像 ====================
        // 放在 HKCU 下一个不起眼的值名里，内容与 lock.dat 相同且同样用 DPAPI 加密。
        // 目的不是"藏起来"（藏不住），而是让"删掉桌面上的文件"这种手段暴露出来。

        private const string RegSubKey = @"Software\VideoChecker";
        private const string RegValueName = "ls";

        private static string ReadMirror()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegSubKey))
                {
                    if (k == null) return null;
                    object v = k.GetValue(RegValueName);
                    if (v == null) return null;
                    byte[] cipher = Convert.FromBase64String(v.ToString());
                    return Secret.Unprotect(cipher);
                }
            }
            catch (Exception ex) { Log.Debug("读取密码状态镜像失败：" + ex.Message); return null; }
        }

        // ★★ 这里原来有一个 `ForgetPassword()` —— **2026-09-15 删掉了** ✗
        //
        //   用户一句话点穿：「你为啥要在开屏密码加这个呢？这不是脱裤子放屁么？」
        //   ★ 他是对的 ✓ 而且这东西比"多余"严重得多：
        //
        //   ① 「忘了密码」本来就有出路 ✓ 见本文件头部第 ④ 条：

        //      进去之后在设置页能改密码 / 取消密码 ✓
        //      → 这个方法**没有增加任何能力** ✗
        //
        //   ② 它当初的理由（"能物理接触就能删 lock.dat"）**是错的** ✗
        //      本文件第 130 行那一整段就是「★ 防"删文件即解锁"」✓
        //        密码状态同时存在 lock.dat **和注册表镜像**里 ✓
        //        文件没了 → 从镜像恢复 → **照旧要密码** ✓
        //      → 所以**删文件根本解不开** ✗
        //      → 而这个方法**恰恰是唯一能解开它的东西** ✗ 且**不需要任何秘密** ✓
        //
        //   ③ 这把锁的用途是「防误触、防随手打开」（见本文件头部）✓
        //      而"点一下链接就把密码清掉"**正是随手打开** ✗
        //      → 它把锁变成了**装饰** ✗
        //
        //   ④ 它当初要治的那件事（卸载残留 → 重装要密码）**根因已修** ✓
        //      v9.2 起卸载程序把 lock.dat 归成"程序状态"、卸载就删 ✓
        //      「换目录 / 别人电脑」那类由 MirrorBelongsToHere() 管着 ✓
        //      ——**为症状加的逃生口，根因修好之后就该撤掉** ✓
        //

        //     它硬编码在源码里 ✓ 谁拿到 exe 反编译都能看到 ✓
        //     ——这一点在本文件头部已经写明，不再重复 ✗
        //     （要真保护数据，用 Windows 账户权限或加密磁盘 ✓）
        /// <summary>镜像里"这个密码是给哪个程序目录设的"那一行的前缀。</summary>
        private const string MirrorDirTag = "dir=";

        /// <summary>
        /// 这份镜像是不是**本目录**设的？
        ///
        /// ★ 2026-09-13 加。返回 false 的三种情况：
        ///   · 镜像里没写目录（老版本写的）✗
        ///   · 写的目录和当前程序目录不一致 ✗（换了安装位置 ✓）
        ///   · 目录字符串解不开 ✗
        /// 三种都当成"这不是给我这个目录设的密码" ✓ 走全新安装 ✓
        ///
        /// ★ 为什么必须加这个判断 —— 完整的失效链：
        ///     你设过开屏密码 → 写 lock.dat ✓ + 写注册表镜像 ✗
        ///     分发程序给别人 → 别人装到另一个目录
        ///       → 那里没有 lock.dat ✗ 而镜像还在 ✗
        ///       → 程序要求密码 ✗ 而对方不知道 ✗ → **永久进不去** ✗✗
        ///   —— 根子上是语义错了：
        ///     镜像的用途是"防止有人删掉 lock.dat 绕过密码"✗
        ///     它却变成了"这台机器上任何目录的这个程序都要密码"✗
        ///     那是「机器的密码」✗ 不是「这个安装位置的密码」✓
        /// </summary>
        private static bool MirrorBelongsToHere(string mirrorText)
        {
            if (string.IsNullOrEmpty(mirrorText)) return false;
            string[] parts = mirrorText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.StartsWith(MirrorDirTag, StringComparison.OrdinalIgnoreCase))
                {
                    string dir = s.Substring(MirrorDirTag.Length).Trim();
                    if (dir.Length == 0) return false;
                    try
                    {
                        return string.Equals(
                            Path.GetFullPath(dir).TrimEnd('\\'),
                            Path.GetFullPath(AppPaths.BaseDir).TrimEnd('\\'),
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch (Exception) { return false; }
                }
            }
            return false;   // 没写目录 = 老镜像 = 不认
        }

        private static void WriteMirror(string plainText)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegSubKey))
                {
                    if (k == null) return;
                    // ★★ 镜像里要多存一行"是哪个程序目录设的"（2026-09-13 修）
                    //
                    //   为什么 —— 用户实测报的：
                    //     「我安装完的程序要输入 0923 这个密码才能开锁。
                    //       这样我怎么分发给别人用呢？」
                    //
                    //   镜像是记在注册表里的 ✗ 而注册表跟着**这台机器**走 ✓
                    //   程序目录却可以随便换 ✗ 于是：
                    //     你在自己电脑上设过密码 → 镜像留在这台机器上
                    //     把程序装到**另一个目录** → 那里没有 lock.dat
                    //     → 但镜像在 → 程序认为"有人在删锁" → **弹密码框** ✗
                    //     → 而对方不知道密码 → **永久进不去** ✗✗
                    //
                    //   ★ 存上目录之后，读的时候就能分辨：
                    //     路径一致   = 你自己那个安装 ✓ 认它 ✓（防护照旧 ✓）
                    //     路径不一致 = 别人装到别处 ✓ 不认 ✓（当成全新安装 ✓）
                    //
                    //   AppPaths.BaseDir 是**程序 exe 所在目录** ✓ 不是当前工作目录 ✓
                    string withDir = plainText.TrimEnd()
                        + Environment.NewLine + MirrorDirTag + AppPaths.BaseDir;
                    k.SetValue(RegValueName, Convert.ToBase64String(Secret.Protect(withDir)));
                }
            }
            catch (Exception ex) { Log.Debug("写入密码状态镜像失败：" + ex.Message); }
        }

        private static void ClearMirror()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegSubKey, true))
                {
                    if (k != null) k.DeleteValue(RegValueName, false);
                }
            }
            catch (Exception) { }
        }

        private static void SaveState(State s)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 开屏密码锁状态。密码以 PBKDF2 哈希存储，看不到明文。");
                sb.AppendLine("# enabled=0 表示未启用开屏密码（直接进入程序）。");
                sb.AppendLine("# 本文件由程序自动维护，请勿手工修改。");
                sb.AppendLine("enabled=" + (s.Enabled ? "1" : "0"));
                sb.AppendLine("everchanged=" + (s.EverChanged ? "1" : "0"));
                if (s.Salt != null) sb.AppendLine("salt=" + Convert.ToBase64String(s.Salt));
                if (s.Hash != null) sb.AppendLine("hash=" + Convert.ToBase64String(s.Hash));
                sb.AppendLine("fail=" + s.FailCount.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("locklevel=" + s.LockLevel.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("lockuntil=" + (s.LockUntil > DateTime.MinValue
                    ? s.LockUntil.Ticks.ToString(CultureInfo.InvariantCulture) : "0"));
                File.WriteAllText(File_, sb.ToString(), new UTF8Encoding(true));
                // 同步写注册表镜像：删掉 lock.dat 也会被交叉比对发现
                if (s.Enabled) WriteMirror(sb.ToString());
                else ClearMirror();
            }
            catch (Exception ex) { Log.Warn("保存密码锁状态失败：" + ex.Message); }
        }

        // ==================== 对外接口 ====================

        /// <summary>是否启用了开屏密码。false = 打开程序直接进入。</summary>
        public static bool IsEnabled
        {
            get
            {
                State s = Load();
                return s.Enabled && s.Salt != null && s.Hash != null;
            }
        }

        /// <summary>是否处于锁定状态。</summary>
        public static bool IsLocked
        {
            get
            {
                State s = Load();
                return s.Enabled && s.LockUntil > DateTime.Now;
            }
        }

        /// <summary>锁定剩余时间描述。未锁定返回空串。</summary>
        public static string LockRemaining()
        {
            State s = Load();
            TimeSpan left = s.LockUntil - DateTime.Now;
            if (left.TotalSeconds <= 0) return "";
            if (left.TotalMinutes >= 1)
                return string.Format("{0} 分 {1} 秒", (int)left.TotalMinutes, left.Seconds);
            return string.Format("{0} 秒", (int)Math.Ceiling(left.TotalSeconds));
        }

        /// <summary>再错几次就会被锁定。</summary>
        public static int RemainingTries()
        {
            State s = Load();
            int left = MaxAttempts - s.FailCount;
            return left < 0 ? 0 : left;
        }

        /// <summary>
        /// 静默校验：逻辑与 Verify 相同，但**不写日志**。专供自检 / 自动化调用。
        /// </summary>
        public static bool VerifyQuiet(string input)
        {
            State s = Load();
            if (!s.Enabled || s.Salt == null || s.Hash == null) return true;
            string pwd = input == null ? "" : input;
            if (s.LockUntil > DateTime.Now) return false;
            return SlowEquals(Derive(pwd, s.Salt), s.Hash);
        }

        /// <summary>
        /// 校验密码。未启用时直接通过；启用后**只认用户设定的那个密码**。
        /// 锁定期间一律拒绝（本项目不含任何绕过锁定的后门）。
        /// </summary>
        public static bool Verify(string input)
        {
            State s = Load();
            if (!s.Enabled || s.Salt == null || s.Hash == null) return true;   // 未设密码

            string pwd = input == null ? "" : input;

            // 开源版不含任何后门：跳过下面就是"照常验证用户自己的密码" ✓
            // （忘记密码 → 删 data\lock.dat + 注册表 HKCU\Software\VideoChecker，详见 README）

            // 照常验证密码 ✓ 验证通过后自动用镜像重建 lock.dat ✓（理由见 Load() 里的长注释）

            if (s.LockUntil > DateTime.Now) return false;   // 普通密码在锁定期间一律拒绝

            if (SlowEquals(Derive(pwd, s.Salt), s.Hash))
            {
                ResetFailures(s);
                // ★ 密码对上了，而文件当初不在 → 用镜像重建 lock.dat ✓
                //   （这样"卸载重装"的用户输一次密码就恢复正常了 ✓
                //     不用每次都被问一遍 ✓）
                if (_pendingRestore) RestoreFromMirror(s);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 用当前状态重建 lock.dat（文件当初丢失/被删，密码验证通过之后调）。
        /// ★ 重建 = 把内存里这份（来自镜像的）完整状态写回文件 ✓
        ///   包含盐、哈希、失败计数、锁定时间 —— 和原来一模一样 ✓
        /// </summary>
        private static void RestoreFromMirror(State s)
        {
            try
            {
                SaveState(s);
                _pendingRestore = false;
                Log.Info("密码状态文件已按系统记录重建");
            }
            catch (Exception ex)
            {
                Log.Warn("重建密码状态文件失败：" + ex.Message);
            }
        }

        private static void ResetFailures(State s)
        {
            if (s.FailCount > 0 || s.LockLevel > 0 || s.LockUntil > DateTime.MinValue)
            {
                s.FailCount = 0;
                s.LockLevel = 0;
                s.LockUntil = DateTime.MinValue;
                SaveState(s);
            }
        }

        /// <summary>记录一次失败；达到阈值则写盘锁定。返回 {是否已锁定, 提示文本}。</summary>
        public static string[] RecordFailure()
        {
            State s = Load();
            s.FailCount++;
            string msg = "密码错误，请重新输入";

            if (s.FailCount >= MaxAttempts)
            {
                int lvl = s.LockLevel;
                if (lvl >= LockMinutes.Length) lvl = LockMinutes.Length - 1;
                int minutes = LockMinutes[lvl];
                s.LockUntil = DateTime.Now.AddMinutes(minutes);
                s.LockLevel++;
                s.FailCount = 0;
                SaveState(s);
                Log.Warn("开屏密码连续错误达到 " + MaxAttempts + " 次，已锁定 " + minutes + " 分钟");
                msg = "密码连续错误 " + MaxAttempts + " 次，已锁定 " + minutes + " 分钟";
                return new string[] { "LOCKED", msg };
            }
            SaveState(s);
            return new string[] { "FAIL", msg };
        }

        /// <summary>是否用户自己设过密码。</summary>
        public static bool EverChanged
        {
            get { return Load().EverChanged; }
        }

        // ==================== 设置 / 修改 / 取消 ====================

        /// <summary>
        /// 首次设置密码（当前未启用时用；无需原密码）。
        /// 成功返回空串，失败返回原因。
        /// </summary>
        public static string SetPassword(string newPwd, string confirmPwd)
        {
            if (IsEnabled) return "当前已设置密码，请用「修改密码」";
            if (newPwd == null || newPwd.Length < 4) return "密码至少 4 位";
            if (newPwd != confirmPwd) return "两次输入的密码不一致";

            State s = Load();
            s.Salt = new byte[16];
            new RNGCryptoServiceProvider().GetBytes(s.Salt);
            s.Hash = Derive(newPwd, s.Salt);
            s.Enabled = true;
            s.EverChanged = true;
            s.FailCount = 0; s.LockLevel = 0; s.LockUntil = DateTime.MinValue;
            SaveState(s);
            Log.Info("已启用开屏密码");
            return "";
        }

        /// <summary>修改密码（需验证原密码）。</summary>
        public static string ChangePassword(string oldPwd, string newPwd, string confirmPwd)
        {
            if (!IsEnabled) return "当前未设置密码，请用「设置密码」";
            if (IsLocked) return "密码锁处于锁定状态（等待 " + LockRemaining() + "）";
            if (!Verify(oldPwd)) return "原密码不正确";
            if (newPwd == null || newPwd.Length < 4) return "新密码至少 4 位";
            if (newPwd != confirmPwd) return "两次输入的新密码不一致";
            if (newPwd == oldPwd) return "新密码不能与原密码相同";

            State s = Load();
            s.Salt = new byte[16];
            new RNGCryptoServiceProvider().GetBytes(s.Salt);
            s.Hash = Derive(newPwd, s.Salt);
            s.Enabled = true;
            s.EverChanged = true;
            s.FailCount = 0; s.LockLevel = 0; s.LockUntil = DateTime.MinValue;
            SaveState(s);
            Log.Info("开屏密码已修改");
            return "";
        }

        /// <summary>取消开屏密码（需验证原密码）。</summary>
        public static string ClearPassword(string pwd)
        {
            if (!IsEnabled) return "";
            if (IsLocked) return "密码锁处于锁定状态（等待 " + LockRemaining() + "）";
            if (!Verify(pwd)) return "密码不正确";

            State s = Load();
            s.Enabled = false;
            s.Salt = null;
            s.Hash = null;
            s.FailCount = 0; s.LockLevel = 0; s.LockUntil = DateTime.MinValue;
            SaveState(s);
            Log.Info("已取消开屏密码（下次打开将直接进入）");
            return "";
        }
    }
}