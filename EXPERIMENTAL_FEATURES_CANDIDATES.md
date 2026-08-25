# 实验性功能候选清单

统计窗口：2026-07-28 至 2026-08-26。收录两个上游所有未合并 PR（开启和关闭未合并），排除已单独处理的 PCL-CE [#3544](https://github.com/PCL-Community/PCL-CE/pull/3544)。共 38 个：PCL 17 个，PCL-CE 21 个。

关闭未合并的 PR 仅作需求参考；实验性功能默认关闭。

## 可选实验功能

| 来源 | PR | 功能 |
| --- | --- | --- |
| PCL | [#5656](https://github.com/Meloong-Git/PCL/pull/5656) | 深色模式 |
| PCL | [#6364](https://github.com/Meloong-Git/PCL/pull/6364) | 微软 CDK 兑换跳转 |
| PCL | [#9092](https://github.com/Meloong-Git/PCL/pull/9092) | 自动代理检测与自定义代理 |
| PCL | [#9133](https://github.com/Meloong-Git/PCL/pull/9133) | SSL 验证选项位置调整 |
| PCL | [#9149](https://github.com/Meloong-Git/PCL/pull/9149) | 杂志主页 |
| PCL | [#9168](https://github.com/Meloong-Git/PCL/pull/9168) | 滑块键盘微调 |
| PCL | [#9171](https://github.com/Meloong-Git/PCL/pull/9171) | 启动日志选择与复制 |
| PCL | [#9198](https://github.com/Meloong-Git/PCL/pull/9198) | Esc 取消启动 |
| PCL | [#9220](https://github.com/Meloong-Git/PCL/pull/9220) | 拖入压缩包检测提示 |
| PCL | [#9236](https://github.com/Meloong-Git/PCL/pull/9236) | 打开自定义 XAML 文件夹 |
| CE | [#3500](https://github.com/PCL-Community/PCL-CE/pull/3500) | 快速下载多选菜单 |
| CE | [#3505](https://github.com/PCL-Community/PCL-CE/pull/3505) | Debug UI 测试页 |
| CE | [#3518](https://github.com/PCL-Community/PCL-CE/pull/3518) | 离线自定义皮肤 |
| CE | [#3536](https://github.com/PCL-Community/PCL-CE/pull/3536) | 跨实例导入资源包与光影包 |
| CE | [#3537](https://github.com/PCL-Community/PCL-CE/pull/3537) | 删除实例前备份截图与投影原理图 |
| CE | [#3565](https://github.com/PCL-Community/PCL-CE/pull/3565) | 内存初始值与最大值 |
| CE | [#3566](https://github.com/PCL-Community/PCL-CE/pull/3566) | ARM64 JDK、LWJGL 自动下载 |

## 修复候选

| 来源 | PR | 修复 |
| --- | --- | --- |
| PCL | [#6087](https://github.com/Meloong-Git/PCL/pull/6087) | 版本名大小写同名目录验证 |
| PCL | [#9240](https://github.com/Meloong-Git/PCL/pull/9240) | 旧版 Fabric 避免 Java 25 |
| PCL | [#9262](https://github.com/Meloong-Git/PCL/pull/9262) | 旧版自动进服 IPv6 地址解析 |
| PCL | [#9267](https://github.com/Meloong-Git/PCL/pull/9267) | 窗口大小切换边框 |
| PCL | [#9274](https://github.com/Meloong-Git/PCL/pull/9274) | 无协议链接自动补全 |
| CE | [#3464](https://github.com/PCL-Community/PCL-CE/pull/3464) | 皮肤修复 |
| CE | [#3465](https://github.com/PCL-Community/PCL-CE/pull/3465) | 正版验证改皮肤后头像显示 |
| CE | [#3495](https://github.com/PCL-Community/PCL-CE/pull/3495) | 代理字段解析（已落地） |
| CE | [#3497](https://github.com/PCL-Community/PCL-CE/pull/3497) | 游戏运行时禁用实例修改与重置 |
| CE | [#3508](https://github.com/PCL-Community/PCL-CE/pull/3508) | ToolTip 显示与偏移（已落地） |
| CE | [#3515](https://github.com/PCL-Community/PCL-CE/pull/3515) | Toast 宽度限制 |
| CE | [#3528](https://github.com/PCL-Community/PCL-CE/pull/3528) | 自定义主页跳转 |

## 大型重构或高风险

| 来源 | PR | 变更 |
| --- | --- | --- |
| PCL | [#9156](https://github.com/Meloong-Git/PCL/pull/9156) | 滑块键盘微调的关闭重复 PR |
| PCL | [#9226](https://github.com/Meloong-Git/PCL/pull/9226) | AccessToken 更新策略 |
| CE | [#3454](https://github.com/PCL-Community/PCL-CE/pull/3454) | `ModModpack.cs` 重构 |
| CE | [#3490](https://github.com/PCL-Community/PCL-CE/pull/3490) | `mcmod.buf` 更新 |
| CE | [#3498](https://github.com/PCL-Community/PCL-CE/pull/3498) | Mod Loader 合并安装重构 |
| CE | [#3506](https://github.com/PCL-Community/PCL-CE/pull/3506) | 下载器分片与资源分配重构 |
| CE | [#3507](https://github.com/PCL-Community/PCL-CE/pull/3507) | 杂志主页地址的关闭重复 PR |
| CE | [#3523](https://github.com/PCL-Community/PCL-CE/pull/3523) | 整合包安装重写 |
| CE | [#3561](https://github.com/PCL-Community/PCL-CE/pull/3561) | 档案系统重构 |

关闭未合并：PCL #5656、#6087、#6364、#9156、#9171、#9226、#9274；CE #3454、#3464、#3465、#3490、#3507、#3566。其他条目仍开启。
