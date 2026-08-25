# 实验性功能

这是 PCL Nex 的可选 PCLX 插件包。安装后在“已安装插件”中展开“实验功能”，按需勾选功能并重启启动器；所有功能默认关闭。

当前实际实现的功能：

- 滑块键盘精细调整，来源 [Meloong-Git/PCL #9168](https://github.com/Meloong-Git/PCL/pull/9168)。
- 打开网页时自动补全 HTTPS，来源 [Meloong-Git/PCL #9274](https://github.com/Meloong-Git/PCL/pull/9274)，仅覆盖打开网页入口。

在此目录运行以下命令生成可导入的 `dist/实验性功能.pclx`：

```powershell
.\Build-Package.ps1 -Configuration Release
```

未合并 PR 的完整候选分类在仓库根目录的 `EXPERIMENTAL_FEATURES_CANDIDATES.md`。只有完成独立移植和验证的功能才应加入这个包。
