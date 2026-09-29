# FFmpeg 放这里（不进仓库）

本仓库**不包含** FFmpeg 二进制（两个 exe 加起来约 190 MB，不适合放进 Git）。

程序需要这两个文件：

```
vendor\ffmpeg\bin\ffmpeg.exe
vendor\ffmpeg\bin\ffprobe.exe
```

## 怎么准备

1. 到 <https://ffmpeg.org/download.html> 下载 **Windows 静态构建**（推荐 gyan.dev 或 BtbN 的 builds）
2. 解压后，把 `bin\ffmpeg.exe` 和 `bin\ffprobe.exe` 拷到本目录
3. 编译程序（`build.bat`），再把这两个 exe 拷一份到 `dist\ffmpeg\bin\`

程序在**自己所在目录的 `ffmpeg\bin\`** 下找它们；找不到会提示，不会静默失败。

## 版本建议

- Windows 7 用户：请用 **2022-06-29 之前**的构建。
  2024-05-31 之后的 FFmpeg 构建在 Win7 上会以 `0xC0000005`（访问冲突）启动即崩 —— 这是上游已知问题。
- Windows 10/11：用最新构建即可。

## 许可

FFmpeg 以 **GPLv3** 分发（部分构建为 LGPL），版权归 FFmpeg 项目所有。
本程序**只以独立进程方式调用** FFmpeg，未修改其源码，也未把它静态链接进本程序。
若你要**再分发**打包好的程序（含 FFmpeg），请自行遵守 GPL 的相关要求。
