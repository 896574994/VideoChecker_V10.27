/* -*- coding: utf-8 -*-
 * GpuInfo.cs — 显卡探测：这台机器有哪些 GPU 能用于硬件解码、各叫什么、编号是多少
 *
 * ★ 为什么要有它（2026-09-28 用户要求）
 *   「最开始先检查电脑有没有GPU，有几个GPU可以选择」
 *
 * ★★ 为什么不能只看注册表就下结论（本机实测抓到的坑）
 *   注册表枚举顺序：0000 = NVIDIA RTX 5070，0001 = AMD Radeon 610M
 *   而 ffmpeg 的 -hwaccel_device 编号：0 = **AMD 610M**，1 = **NVIDIA RTX 5070**
 *   —— **两边顺序正好相反** ✗
 *   如果拿注册表序号直接当 ffmpeg 编号用，用户选「NVIDIA」实际会把活派给核显 ✗
 *   所以这里的判据是**真的跑一次 ffmpeg**，读它自己报出来的设备：
 *       [AVHWDeviceContext @ ...] Using device 10de:2d58 (NVIDIA GeForce RTX 5070 Laptop GPU)
 *   用这行里的 PCI ID 和名字当唯一真相 ✓ 注册表**只用来补显存大小** ✓
 *
 * ★ 另一条实测结论：`-init_hw_device d3d11va:x:N` 的编号是**被忽略**的
 *   （0/1/2 都报同一块卡）✗ 要挑卡必须用 `-hwaccel d3d11va -hwaccel_device N`，
 *   而且**必须有真实输入文件**才能触发解码器初始化 ✓
 *
 * ★ 平台限制：d3d11va 要 Windows 8+。Win7 上探测会失败 → 退回 -hwaccel auto（走 dxva2）✓
 *
 * C# 5 兼容语法。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace VideoChecker
{
    /// <summary>一块可用于硬件解码的显卡。</summary>
    public class GpuCard
    {
        /// <summary>ffmpeg 的 -hwaccel_device 编号。</summary>
        public int Device = -1;
        /// <summary>显卡名（ffmpeg 报的，最可信）。</summary>
        public string Name = "";
        /// <summary>PCI 标识，形如 "10de:2d58"。</summary>
        public string PciId = "";
        /// <summary>显存 GB（注册表补的，读不到 = 0）。</summary>
        public double VramGB = 0;
    }

    /// <summary>显卡探测（本机有哪些 GPU、编号怎么对）。</summary>
    public static class GpuInfo
    {
        private static readonly object _lock = new object();
        private static List<GpuCard> _cards = null;      // null = 还没探测过
        private static volatile bool _probing = false;

        /// <summary>探测是否进行中。</summary>
        public static bool Probing { get { return _probing; } }
        /// <summary>探测过但没有可用显卡（比如 Win7、或机器上没有能硬解的卡）。</summary>
        public static bool Unavailable { get { return _cards != null && _cards.Count == 0; } }
        /// <summary>探测失败的原因（给人看的一句话）。</summary>
        public static string FailReason = "";

        /// <summary>已探测到的显卡列表（没探测过返回 null）。</summary>
        public static List<GpuCard> Cards
        {
            get
            {
                lock (_lock) { return _cards == null ? null : new List<GpuCard>(_cards); }
            }
        }

        /// <summary>是否已经探测过（不论成功失败）。</summary>
        public static bool Detected { get { lock (_lock) { return _cards != null; } } }

        /// <summary>
        /// **本机有没有可用于硬件解码的显卡**（探测完成后才算数）。
        /// ★ 激进模式的闸门就看它（2026-09-29 用户要求）：
        ///   「如果只有 CPU 的话不允许启用激进模式」✓
        ///   没探测完返回 false —— 宁可先判"没有"，等探测完再刷新 ✓
        /// </summary>
        public static bool HasUsableGpu
        {
            get { lock (_lock) { return _cards != null && _cards.Count > 0; } }
        }

        /// <summary>
        /// 后台探测（不卡界面）。done 在探测完成时回调（可能在别的线程上）。
        /// 已经探测过就直接回调返回。
        /// </summary>
        public static void DetectAsync(Action done)
        {
            lock (_lock)
            {
                if (_cards != null) { if (done != null) done(); return; }
                if (_probing) { if (done != null) done(); return; }
                _probing = true;
            }
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                try { Detect(); }
                catch (Exception ex) { Log.Warn("显卡探测失败：" + ex.Message); }
                finally { _probing = false; }
                if (done != null) { try { done(); } catch (Exception) { } }
            });
            th.IsBackground = true;
            th.Start();
        }

        /// <summary>同步等待探测完成（最多等 timeoutMs 毫秒；超时就先用"没探测到"继续）。</summary>
        public static void EnsureDetected(int timeoutMs)
        {
            if (Detected) return;
            DetectAsync(null);
            int waited = 0;
            while (!Detected && waited < timeoutMs)
            {
                System.Threading.Thread.Sleep(100);
                waited += 100;
            }
        }

        /// <summary>强制重新探测（用户点「重新检测」时用）。</summary>
        public static void RefreshAsync(Action done)
        {
            lock (_lock)
            {
                if (_probing) { if (done != null) done(); return; }
                _cards = null;
            }
            DetectAsync(done);
        }

        /// <summary>按 PCI 找 ffmpeg 编号；找不到返回 -1。</summary>
        public static int FindDevice(string pciId)
        {
            if (string.IsNullOrEmpty(pciId)) return -1;
            List<GpuCard> c = Cards;
            if (c == null) return -1;
            for (int i = 0; i < c.Count; i++)
                if (string.Equals(c[i].PciId, pciId, StringComparison.OrdinalIgnoreCase)) return c[i].Device;
            return -1;
        }

        /// <summary>按 PCI 拿显卡对象；没有返回 null。</summary>
        public static GpuCard Find(string pciId)
        {
            if (string.IsNullOrEmpty(pciId)) return null;
            List<GpuCard> c = Cards;
            if (c == null) return null;
            for (int i = 0; i < c.Count; i++)
                if (string.Equals(c[i].PciId, pciId, StringComparison.OrdinalIgnoreCase)) return c[i];
            return null;
        }

        /// <summary>一行话描述探测结果（给界面/日志用）。</summary>
        public static string Summary()
        {
            List<GpuCard> c = Cards;
            if (c == null) return "显卡尚未检测";
            if (c.Count == 0) return "未检测到可用于硬件解码的显卡（将只用 CPU；" + (FailReason.Length > 0 ? FailReason : "系统或显卡驱动不支持") + "）";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("检测到 ").Append(c.Count).Append(" 块可用显卡：");
            for (int i = 0; i < c.Count; i++)
            {
                if (i > 0) sb.Append("；");
                sb.Append((i + 1)).Append(". ").Append(c[i].Name);
                if (c[i].VramGB > 0) sb.Append("（显存 ").Append(c[i].VramGB.ToString("0.#", CultureInfo.InvariantCulture)).Append(" GB）");
                sb.Append(" [编号 ").Append(c[i].Device).Append("]");
            }
            return sb.ToString();
        }

        /// <summary>真的探测一次（同步）。</summary>
        public static void Detect()
        {
            List<GpuCard> found = new List<GpuCard>();
            FailReason = "";
            try
            {
                if (!D3d11Usable())
                {
                    FailReason = "当前系统不支持 d3d11va（Windows 7 只能走自动选择）";
                }
                else
                {
                    string clip = ProbeClip();
                    if (clip == null)
                    {
                        FailReason = "无法生成探测用的样本片段";
                    }
                    else
                    {
                        int miss = 0;
                        for (int dev = 0; dev <= 4; dev++)
                        {
                            GpuCard card = ProbeDevice(clip, dev);
                            if (card == null)
                            {
                                miss++;
                                if (miss >= 2 && found.Count > 0) break;   // 连续两次都没有 → 已经到头了
                                if (miss >= 3 && found.Count == 0) break;
                                continue;
                            }
                            miss = 0;
                            bool dup = false;
                            for (int i = 0; i < found.Count; i++)
                                if (found[i].PciId == card.PciId) { dup = true; break; }
                            if (!dup) found.Add(card);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FailReason = ex.Message;
            }

            if (found.Count > 0) EnrichVram(found);      // 注册表只用来补显存

            lock (_lock) { _cards = found; }
            Log.Info("显卡探测：" + Summary());
        }

        /// <summary>本机能不能用 d3d11va（Windows 8+）。</summary>
        private static bool D3d11Usable()
        {
            try
            {
                Version v = Environment.OSVersion.Version;
                if (v.Major > 6) return true;
                if (v.Major == 6 && v.Minor >= 2) return true;   // 6.2 = Windows 8
                return false;
            }
            catch (Exception) { return false; }
        }

        /// <summary>探测用的小片段（64x64、0.4 秒），生成一次就复用。</summary>
        private static string ProbeClip()
        {
            try
            {
                string p = Path.Combine(Path.GetTempPath(), "vc_gpuprobe.mp4");
                if (File.Exists(p) && new FileInfo(p).Length > 500) return p;
                string[] args = new string[] {
                    "-y", "-v", "error",
                    "-f", "lavfi", "-i", "color=c=black:s=64x64:d=0.4:r=5",
                    "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
                    p
                };
                Ffmpeg.Run(Ffmpeg.FfmpegBin(), args, 60, true);
                if (File.Exists(p) && new FileInfo(p).Length > 500) return p;
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("生成显卡探测片段失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 探一块卡：ffmpeg 用 -hwaccel_device N 解一帧，读它报出来的设备名和 PCI。
        /// 真实输出形如：
        ///   [AVHWDeviceContext @ ...] Using device 1002:164e (AMD Radeon(TM) 610M).
        /// ★ 名字里本身带括号（AMD Radeon(TM) 610M）—— 正则必须取到**行尾那个右括号**，
        ///   否则会把名字截成 "AMD Radeon(TM" ✗（实测踩到）
        /// </summary>
        private static GpuCard ProbeDevice(string clip, int dev)
        {
            try
            {
                string[] args = new string[] {
                    "-v", "verbose",
                    "-hwaccel", "d3d11va",
                    "-hwaccel_device", dev.ToString(CultureInfo.InvariantCulture),
                    "-i", clip,
                    "-frames:v", "1",
                    "-f", "null", "-"
                };
                ProcessResult r = Ffmpeg.Run(Ffmpeg.FfmpegBin(), args, 30, true);
                if (r == null || r.Stderr == null) return null;
                Match m = Regex.Match(r.Stderr,
                    "Using device\\s+([0-9a-fA-F]{4}:[0-9a-fA-F]{4})\\s*\\((.*?)\\)\\.?\\s*$",
                    RegexOptions.Multiline);
                if (!m.Success) return null;
                string name = m.Groups[2].Value.Trim();
                // 软渲染 / 虚拟显示适配器不是真显卡 —— 列出来只会让人选错 ✗
                if (IsVirtualOrSoftware(name)) return null;
                GpuCard c = new GpuCard();
                c.Device = dev;
                c.PciId = m.Groups[1].Value.ToLowerInvariant();
                c.Name = name;
                return c;
            }
            catch (Exception) { return null; }
        }

        private static string[] _virtualWords = new string[] {
            "basic render", "basic display", "remote", "idd", "indirect", "virtual", "mirror", "warp"
        };

        /// <summary>软渲染器 / 虚拟显示适配器（不是能用来解码的真显卡）。</summary>
        private static bool IsVirtualOrSoftware(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string low = name.ToLowerInvariant();
            for (int i = 0; i < _virtualWords.Length; i++)
                if (low.IndexOf(_virtualWords[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>从注册表补显存大小（读不到就算了，不影响功能）。</summary>
        private static void EnrichVram(List<GpuCard> cards)
        {
            try
            {
                const string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(cls))
                {
                    if (k == null) return;
                    string[] subs = k.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        if (!Regex.IsMatch(subs[i], "^[0-9]{4}$")) continue;
                        try
                        {
                            using (RegistryKey sk = k.OpenSubKey(subs[i]))
                            {
                                if (sk == null) continue;
                                object mid = sk.GetValue("MatchingDeviceId");
                                if (mid == null) continue;
                                // ★ 大小写不敏感 —— 有的厂商写 "PCI\VEN_..."，有的写 "pci\ven_..." ✗
                                //   （实测：NVIDIA 的键是小写 ven_10de，区分大小写就读不到显存了）
                                Match m = Regex.Match(mid.ToString(), "VEN_([0-9A-Fa-f]{4})&DEV_([0-9A-Fa-f]{4})",
                                    RegexOptions.IgnoreCase);
                                if (!m.Success) continue;
                                string pci = (m.Groups[1].Value + ":" + m.Groups[2].Value).ToLowerInvariant();

                                double gb = 0;
                                object qw = sk.GetValue("HardwareInformation.qwMemorySize");
                                if (qw is long) gb = (long)qw / 1073741824.0;
                                else
                                {
                                    object ms = sk.GetValue("HardwareInformation.MemorySize");
                                    byte[] b = ms as byte[];
                                    if (b != null && b.Length >= 4) gb = BitConverter.ToUInt32(b, 0) / 1073741824.0;
                                }
                                if (gb > 0 && gb < 1024)
                                    for (int j = 0; j < cards.Count; j++)
                                        if (cards[j].PciId == pci) cards[j].VramGB = Math.Round(gb, 1);
                            }
                        }
                        catch (Exception) { }        // 个别子键权限不足（如 Properties）——跳过就行
                    }
                }
            }
            catch (Exception ex) { Log.Warn("读显卡显存失败（不影响使用）：" + ex.Message); }
        }
    }
}