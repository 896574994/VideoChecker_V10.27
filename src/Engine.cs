/* -*- coding: utf-8 -*-
 * Engine.cs — 检测调度引擎（单文件/目录/RTSP）
 * 与 Python 版 video_checker.py 主流程等价。
 * C# 5 兼容语法。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VideoChecker
{
    /// <summary>用户点了「停止」导致检测中断 —— 这是正常停止，不是错误（MainForm 会接住并保留部分结果）。</summary>
    public class StoppedException : Exception
    {
        public StoppedException() : base("检测已停止") { }
    }

    public static class Engine
    {
        /// <summary>检测 RTSP/网络流（实时拉流）。</summary>
        public static MediaReport CheckRtsp(string url, double duration, CheckOptions opts)
        {
            return CheckRtsp(url, duration, opts, null);
        }

        /// <summary>检测 RTSP/网络流。isCancelled 供「停止」按钮中断（每个步骤边界检查）。</summary>
        public static MediaReport CheckRtsp(string url, double duration, CheckOptions opts, Func<bool> isCancelled)
        {
            // 报告里用打码地址：报告会被打开、导出 PDF（PDF 是整页栅格化的图片，
            // 文字无法被替换），明文密码印在图上就收不回来了。
            MediaReport rep = new MediaReport(Secret.MaskUrl(url));
            Ffmpeg.ClearPacketCache();   // 每路流重新取包，避免沿用上一条流的缓存
            if (opts.RtspBizMode) RtspBiz.Check(url, duration, opts, rep, isCancelled);   // 拉流业务检测（专用判定）
            else RtspCheck.CheckRtsp(url, duration, opts, rep);                            // 旧路径：把流当文件
            return rep;
        }

        private static string[] _videoExt = new string[] {
            ".mp4", ".mov", ".mkv", ".avi", ".flv", ".ts", ".m2ts", ".mts",
            ".webm", ".wmv", ".mpg", ".mpeg", ".m4v", ".m4a", ".3gp", ".ogv",
            ".vob", ".aac", ".mp3", ".wav", ".flac"
        };

        public static bool IsVideoFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            for (int i = 0; i < _videoExt.Length; i++)
                if (ext == _videoExt[i]) return true;
            return false;
        }

        /// <summary>收集待检测文件：文件直接返回，目录递归扫描。</summary>
        public static List<string> CollectFiles(string path)
        {
            List<string> files = new List<string>();
            if (File.Exists(path)) { files.Add(path); return files; }
            if (Directory.Exists(path))
            {
                Stack<string> stack = new Stack<string>();
                stack.Push(path);
                while (stack.Count > 0)
                {
                    string dir = stack.Pop();
                    try
                    {
                        foreach (string d in Directory.GetDirectories(dir)) stack.Push(d);
                        string[] names = Directory.GetFiles(dir);
                        Array.Sort(names, StringComparer.Ordinal);
                        foreach (string f in names)
                            if (IsVideoFile(f)) files.Add(f);
                    }
                    catch { }
                }
            }
            return files;
        }

        /// <summary>检测单个文件，返回报告。</summary>
        public static MediaReport CheckOneFile(string path, CheckOptions opts,
            Action<int, int> mageProgress = null, Func<bool> isCancelled = null, Action<string> onStage = null)
        {
            return CheckOneFileEx(path, opts, mageProgress, isCancelled, onStage, null);
        }

        /// <summary>
        /// 分析命令的超时随视频时长自适应。
        ///
        /// ★ 为什么（2026-09-28）：以前写死 1800 秒（30 分钟）——
        ///   3 小时的片子软件解码普遍要 1~2 小时，直接被当成「超时」杀掉 ✗
        ///   用户只看到一个 ERROR，以为程序坏了 ✗
        ///   现在：时长已知时给「30 分钟 + 一个时长」，封顶 6 小时；
        ///   界面有实时进度（time= 解析），用户随时可以点「停止」。
        /// </summary>
        private static int TimeoutFor(double? knownDur)
        {
            if (!knownDur.HasValue || knownDur.Value <= 0) return 1800;
            double t = 1800 + knownDur.Value;
            if (t > 21600) t = 21600;
            return (int)t;
        }

        /// <summary>多长的视频才尝试硬件解码（10 分钟）。回归样本都远小于它，路径零变化。</summary>
        private const double HwAccelMinDur = 600.0;

        /// <summary>激进模式下的硬件解码门槛（1 分钟）—— CPU+GPU 一起上，短一点也值得。</summary>
        private const double AggressiveHwMinDur = 60.0;

        /// <summary>分段并行的门槛：普通 10 分钟，激进模式 2 分钟。</summary>
        private const double ParallelMinDur = 600.0;
        private const double AggressiveParallelMinDur = 120.0;

        /// <summary>
        /// 硬件解码的候选顺序：**指定显卡 → 自动 → 软件**。
        /// 逐个试，前一个失败（退出码非 0 或产生解码错误行）就退到下一个 ✓
        /// ★ 最后一个永远是 null（软件解码）—— 保证一定能跑出结果，不会因为硬解不行就整片失败 ✗
        /// </summary>
        internal static List<string[]> HwCandidates(CheckOptions opts)
        {
            List<string[]> list = new List<string[]>();
            if (opts != null && !string.IsNullOrEmpty(opts.GpuPci))
            {
                int dev = GpuInfo.FindDevice(opts.GpuPci);
                if (dev >= 0)
                    list.Add(new string[] { "-hwaccel", "d3d11va", "-hwaccel_device", dev.ToString() });
                else
                    Log.Warn("指定的显卡（" + opts.GpuPci + "）没在检测结果里 —— 本次改用自动选卡");
            }
            list.Add(new string[] { "-hwaccel", "auto" });
            list.Add(null);          // 软件解码兜底
            return list;
        }

        /// <summary>把硬件选项翻译成人话（日志/报告用）。</summary>
        internal static string DescribeHw(string[] hw)
        {
            if (hw == null) return "软件解码";
            for (int i = 0; i < hw.Length; i++)
                if (hw[i] == "-hwaccel_device" && i + 1 < hw.Length)
                    return "硬件解码（指定显卡 编号 " + hw[i + 1] + "）";
            return "硬件解码（自动选卡）";
        }

        /// <summary>
        /// 按候选顺序解码一遍视频分析：第一个成功的候选就是结果。
        /// usedHw 输出真正用上的那一档（null = 软件解码）。
        /// </summary>
        private static AnalyzeResult AnalyzeVideoWithFallback(string path, double? tSec, int timeout,
            Action<double> onProgress, List<string[]> hwCands, Func<bool> isCancelled, out string[] usedHw)
        {
            usedHw = null;
            AnalyzeResult r = null;
            for (int i = 0; i < hwCands.Count; i++)
            {
                r = Ffmpeg.AnalyzeEx(path, Ffmpeg.VF_CHAIN, null, tSec, timeout, null, onProgress, hwCands[i]);
                if (isCancelled != null && isCancelled()) throw new StoppedException();
                bool lastOne = (hwCands[i] == null);
                if (lastOne || (r.Code == 0 && r.ErrorLines.Count == 0))
                {
                    usedHw = hwCands[i];
                    break;
                }
                Log.Info(DescribeHw(hwCands[i]) + "不可用或报错，回退下一档（" + Path.GetFileName(path) + "）");
            }
            return r;
        }

        /// <summary>
        /// 检测单个文件，返回报告（完整版）。
        ///
        /// onFileProgress(stage, doneSec, totalSec)：阶段进度回调。
        ///   stage ∈ 探测媒体 / 视频分析 / 音频分析 / 音频质量分析 / 判定结果；
        ///   视频、音频分析会按 ffmpeg 进度行实时回调已处理秒数 —— 长视频界面上能看见真实进度。
        /// </summary>
        public static MediaReport CheckOneFileEx(string path, CheckOptions opts,
            Action<int, int> mageProgress, Func<bool> isCancelled, Action<string> onStage,
            Action<string, double, double> onFileProgress)
        {
            MediaReport rep = new MediaReport(path);
            Ffmpeg.ClearPacketCache();   // 每个文件重新取包，避免缓存无上限增长、也避免跨文件误用
            Dictionary<string, object> probe = null;
            if (onFileProgress != null) onFileProgress("探测媒体", 0, 0);
            try
            {
                probe = Ffmpeg.ProbeMedia(path, 120);
            }
            catch (FfmpegError e)
            {
                rep.Error = "无法探测媒体: " + e.Message;
                return rep;
            }

            MediaInfo info = Checks.BuildMediaInfo(probe);
            rep.MediaInfo = InfoToDict(info);

            try
            {
                string vStderr = "", aStderr = "", aStdout = "";
                List<string> vErr = new List<string>(), aErr = new List<string>();
                bool isAudioFile = Checks.IsAudioOnlyFile(path);
                bool needV = (opts.CheckVideo || opts.CheckSync) && !isAudioFile;
                bool needA = opts.CheckAudio || opts.CheckSync;
                double sampling = opts.EffectiveSampling();
                double? knownDur = info.Duration;
                // 分析的实际跨度：采样模式取 min(采样, 时长)，全片取整段时长（进度条分母用）
                double span = sampling > 0
                    ? (knownDur.HasValue ? Math.Min(sampling, knownDur.Value) : sampling)
                    : (knownDur.HasValue ? knownDur.Value : 0);
                int timeout = TimeoutFor(knownDur);
                // 激进模式把硬件解码门槛从 10 分钟降到 1 分钟；并行分段门槛 10 分钟 → 2 分钟
                bool tryHw = knownDur.HasValue && knownDur.Value > (opts.Aggressive ? AggressiveHwMinDur : HwAccelMinDur);
                List<string[]> hwCands = tryHw ? HwCandidates(opts) : null;
                double parMin = opts.Aggressive ? AggressiveParallelMinDur : ParallelMinDur;

                // ★ 视频与音频分析同时启动（2026-09-28）：
                //   音频解码很轻、但也得完整过一遍文件（demux），
                //   和视频解码并行跑省掉这段纯等待；视频侧自己再按 CPU+GPU 分段并行。
                //   并发期间进度条只跟视频走（音频快得多，来回切阶段会让进度条乱跳 ✗）。
                bool videoActive = needV;
                System.Threading.Tasks.Task<AnalyzeResult> vt = null;
                System.Threading.Tasks.Task<AnalyzeResult> at = null;
                if (needV)
                {
                    if (onFileProgress != null) onFileProgress("视频分析", 0, span);
                    vt = System.Threading.Tasks.Task<AnalyzeResult>.Factory.StartNew(delegate()
                    {
                        Action<double> vProg = null;
                        if (onFileProgress != null)
                            vProg = delegate(double sec) { onFileProgress("视频分析", sec, span); };
                        bool longFull = tryHw && sampling <= 0 && knownDur.HasValue && knownDur.Value > parMin;
                        if (longFull)
                        {
                            // 长视频全片：CPU + GPU 分段并行解码（内部已合并去重）
                            return AnalyzeVideoParallel(path, knownDur.Value, isCancelled, vProg, hwCands, opts.Aggressive);
                        }
                        if (!tryHw)
                        {
                            // 短视频 / 快速模式：软件解码（保持老路径，回归基线靠它不变）
                            return Ffmpeg.AnalyzeEx(path, Ffmpeg.VF_CHAIN, null,
                                sampling > 0 ? (double?)sampling : null, timeout, null, vProg, null);
                        }
                        // 单段：指定卡 → 自动 → 软件，逐个试（宁可慢，不能把「硬解不吃」误判成「片子坏」✓）
                        string[] usedHw;
                        AnalyzeResult r = AnalyzeVideoWithFallback(path, sampling > 0 ? (double?)sampling : null,
                            timeout, vProg, hwCands, isCancelled, out usedHw);
                        Log.Info("视频分析使用" + DescribeHw(usedHw) + "（" + Path.GetFileName(path) + "）");
                        return r;
                    });
                }
                if (needA)
                {
                    if (onFileProgress != null) onFileProgress("音频分析", 0, span);
                    at = System.Threading.Tasks.Task<AnalyzeResult>.Factory.StartNew(delegate()
                    {
                        Action<double> aProg = null;
                        if (onFileProgress != null)
                            aProg = delegate(double sec)
                            {
                                // 视频还没跑完时音频不打扰进度条（避免阶段来回跳）
                                if (!videoActive) onFileProgress("音频分析", sec, span);
                            };
                        return Ffmpeg.AnalyzeEx(path, null, Ffmpeg.AF_CHAIN,
                            sampling > 0 ? (double?)sampling : null, timeout, null, aProg, null);
                    });
                }

                List<System.Threading.Tasks.Task<AnalyzeResult>> all = new List<System.Threading.Tasks.Task<AnalyzeResult>>();
                if (vt != null) all.Add(vt);
                if (at != null) all.Add(at);
                try
                {
                    System.Threading.Tasks.Task.WaitAll(all.ToArray());
                }
                catch (AggregateException ae)
                {
                    Exception baseEx = ae.GetBaseException();
                    if (baseEx is FfmpegError) throw (FfmpegError)baseEx;
                    if (baseEx is StoppedException) throw (StoppedException)baseEx;
                    throw baseEx;
                }
                if (isCancelled != null && isCancelled()) throw new StoppedException();

                if (vt != null)
                {
                    AnalyzeResult vr = vt.Result;
                    vStderr = vr.Stderr;
                    vErr = vr.ErrorLines;
                }
                if (at != null)
                {
                    AnalyzeResult ar = at.Result;
                    aStderr = ar.Stderr;
                    aStdout = ar.Stdout;
                    aErr = ar.ErrorLines;
                }

                List<Seg> silenceSegs = Ffmpeg.ParseSilence(aStderr);
                List<Seg3> blackSegs = Ffmpeg.ParseBlack(vStderr);

                if (onFileProgress != null) onFileProgress("判定结果", 0, 0);
                if (opts.CheckAudio)
                    Checks.CheckAudioIntegrity(path, probe, aStderr, aStdout, aErr, opts.AudioTh, rep);
                if (opts.CheckQuality)
                {
                    if (onFileProgress != null) onFileProgress("音频质量分析", 0, 0);
                    Checks.CheckAudioQuality(path, probe, sampling, opts.QualityTh, rep);
                }
                if (opts.CheckVideo)
                {
                    if (isAudioFile)
                        rep.Add("视频完整性", "SKIP", "纯音频文件，无视频流可检测", null);
                    else
                        Checks.CheckVideoIntegrity(path, probe, vStderr, vErr, opts.VideoTh, rep);
                }
                if (opts.CheckSync)
                {
                    if (isAudioFile)
                        rep.Add("音画同步", "SKIP", "纯音频文件，无法进行音画同步检测", null);
                    else
                        Checks.CheckAvSync(path, probe, opts.SyncTh, silenceSegs, blackSegs, rep);
                }
                if (opts.CheckMage)
                {
                    if (isAudioFile)
                        rep.Add("AI 画面理解", "SKIP", "纯音频文件，无画面可分析", null);
                    else
                        MageCheck.CheckMage(path, probe, opts.Mage, sampling, rep, mageProgress, isCancelled, onStage);
                }
            }
            catch (FfmpegError e)
            {
                rep.Error = e.Message;
            }
            return rep;
        }

        /// <summary>
        /// 长视频的视频分析：CPU 和 GPU 一起干活。
        ///
        /// ★ 为什么（2026-09-28 用户提）：以前要么整段 GPU、要么整段 CPU，另一侧闲着 ✗
        ///   现在把视频切成 K 段：第 0 段走硬件解码（指定的显卡，或 -hwaccel auto），其余走软件解码，
        ///   K 个 ffmpeg 同时跑，最后把 stderr 合并（黑帧/冻结按区间并集去重，解码错误按归一化去重）。
        ///
        /// 设计要点：
        ///   · 相邻段重叠 60 秒 —— 覆盖「冻结帧 30 秒失败线」的判定粒度，
        ///     跨边界的长黑帧/长冻结在并集合并后长度不丢 ✓
        ///   · 每段带 -copyts（绝对时间戳）—— 去重和报告时间轴都靠它 ✓
        ///   · K = 处理器核心数的一半，普通模式夹在 2~4，激进模式放宽到 6
        ///   · 回归样本都小于 10 分钟，走不到这里 → 基线路径零变化 ✓
        /// </summary>
        internal static AnalyzeResult AnalyzeVideoParallel(string path, double knownDur,
            Func<bool> isCancelled, Action<double> onProgress, List<string[]> hwCands, bool aggressive)
        {
            int segs = Environment.ProcessorCount / 2;
            if (segs < 2) segs = 2;
            int segCap = aggressive ? 6 : 4;
            if (segs > segCap) segs = segCap;
            const double overlap = 60.0;
            if (knownDur <= overlap * 2) segs = 1;

            if (segs <= 1)
            {
                // 太短不值得切：仍按候选链试硬件（激进模式下 2 分钟以内也会走到这里）
                List<string[]> cands = (hwCands != null && hwCands.Count > 0)
                    ? hwCands : new List<string[]>(new string[][] { null });
                string[] usedSingle;
                AnalyzeResult one = AnalyzeVideoWithFallback(path, null, TimeoutFor(knownDur),
                    onProgress, cands, isCancelled, out usedSingle);
                Log.Info("视频分析使用" + DescribeHw(usedSingle) + "（" + Path.GetFileName(path) + "）");
                return one;
            }

            double baseLen = knownDur / segs;
            double[] starts = new double[segs];
            double[] ends = new double[segs];
            for (int i = 0; i < segs; i++)
            {
                starts[i] = (i == 0) ? 0 : ends[i - 1] - overlap;
                ends[i] = (i == segs - 1) ? knownDur : (i + 1) * baseLen;
            }

            Log.Info("长视频并行分析：切成 " + segs + " 段（1 段硬件 + " + (segs - 1)
                + " 段软件）同时解码（" + Path.GetFileName(path) + "）");

            double[] segDone = new double[segs];
            object progLock = new object();
            System.Func<int, Action<double>> mkProg = delegate(int wi)
            {
                if (onProgress == null) return null;   // ★ 没给进度回调就别造一个会空引用的委托 ✗
                return delegate(double sec)
                {
                    lock (progLock)
                    {
                        // time= 进度行是**相对分段起点**的秒数（实测：-ss 102.5 时 time 从 0 涨），
                        // 而 blackdetect/freezedetect/showinfo 的时间戳才是绝对时间 —— 两边不一样，别混 ✗
                        segDone[wi] = sec;
                        double total = 0;
                        for (int j = 0; j < segs; j++) total += segDone[j];
                        if (total > knownDur) total = knownDur;
                        if (total < 0) total = 0;
                        onProgress(total);
                    }
                };
            };

            // 第 0 段走硬件（指定卡 → 自动 → 软件，候选链在段内自己回退）；
            // 其余段走软件 —— CPU 和 GPU 同时出力 ✓
            List<string[]> cands0 = (hwCands != null && hwCands.Count > 0)
                ? hwCands : new List<string[]>(new string[][] { null });
            string[] usedHw = null;
            System.Threading.Tasks.Task<AnalyzeResult>[] tasks = new System.Threading.Tasks.Task<AnalyzeResult>[segs];
            for (int i = 0; i < segs; i++)
            {
                int wi = i;
                tasks[i] = System.Threading.Tasks.Task<AnalyzeResult>.Factory.StartNew(delegate()
                {
                    double st = starts[wi];
                    double len = ends[wi] - st;
                    if (wi == 0)
                    {
                        string[] used;
                        AnalyzeResult r0 = AnalyzeVideoWithFallback(path, len, TimeoutForSlice(len),
                            mkProg(wi), cands0, isCancelled, out used);
                        usedHw = used;
                        return r0;
                    }
                    return Ffmpeg.AnalyzeEx(path, Ffmpeg.VF_CHAIN, null, len,
                        TimeoutForSlice(len), null, mkProg(wi), null, st);
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);
            if (isCancelled != null && isCancelled()) throw new StoppedException();

            AnalyzeResult hw = tasks[0].Result;
            Log.Info("并行分析：第 1 段用" + DescribeHw(usedHw) + "，其余 " + (segs - 1) + " 段用软件解码"
                + "（" + Path.GetFileName(path) + "）");

            StringBuilder sb = new StringBuilder();
            List<string> errLines = new List<string>();
            List<Seg3> allBlack = new List<Seg3>();
            List<Seg> allFreeze = new List<Seg>();
            for (int i = 0; i < segs; i++)
            {
                AnalyzeResult r = (i == 0) ? hw : tasks[i].Result;
                // 每个 worker 在自己的 stderr 里配对（单 worker 事件流良构），
                // 检测行剥掉、跨 worker 段级并集后再统一写回 ✓
                sb.Append(Ffmpeg.StripDetectLines(r.Stderr)).Append('\n');
                for (int j = 0; j < r.ErrorLines.Count; j++) errLines.Add(r.ErrorLines[j]);
                List<Seg3> blk = Ffmpeg.ParseBlack(r.Stderr);
                for (int j = 0; j < blk.Count; j++) allBlack.Add(blk[j]);
                List<Seg> frz = Ffmpeg.ParseFreeze(r.Stderr);
                for (int j = 0; j < frz.Count; j++) allFreeze.Add(frz[j]);
            }
            List<Seg3> mBlack = Ffmpeg.MergeBlackSegs(allBlack);
            List<Seg> mFreeze = Ffmpeg.MergeFreezeSegs(allFreeze);
            Ffmpeg.EmitDetectLines(sb, mBlack, mFreeze);
            List<string> deduped = Ffmpeg.DedupeErrorLines(errLines);
            return new AnalyzeResult(sb.ToString(), "", deduped, 0);
        }

        /// <summary>单个分段的超时：30 分钟底 + 段长的两倍，封顶 6 小时。</summary>
        private static int TimeoutForSlice(double sliceLen)
        {
            double t = 1800 + sliceLen * 2;
            if (t > 21600) t = 21600;
            return (int)t;
        }

        internal static Dictionary<string, object> InfoToDict(MediaInfo info)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["container"] = info.Container;
            d["duration"] = info.Duration.HasValue ? (object)info.Duration.Value : null;
            d["size"] = info.Size.HasValue ? (object)info.Size.Value : null;
            d["bit_rate"] = info.BitRate.HasValue ? (object)info.BitRate.Value : null;
            if (info.Video != null) d["video"] = info.Video;
            if (info.Audio != null) d["audio"] = info.Audio;
            return d;
        }

        /// <summary>格式化时长 mm:ss。</summary>
        public static string FmtSec(double? sec)
        {
            if (!sec.HasValue) return "?";
            int total = (int)sec.Value;
            int m = total / 60, s = total % 60;
            return m.ToString("00") + ":" + s.ToString("00");
        }

        /// <summary>格式化文件大小。</summary>
        public static string FmtSize(double? size)
        {
            if (!size.HasValue || size.Value <= 0) return "?";
            double n = size.Value;
            string[] units = new string[] { "B", "KB", "MB", "GB" };
            int u = 0;
            while (n >= 1024 && u < units.Length - 1) { n /= 1024; u++; }
            if (u == 0) return ((long)n).ToString() + "B";
            return n.ToString("0.0") + units[u];
        }
    }
}