<div align="center">

<img src="PCL2-Nex.png" alt="Logo" width="80" height="80">

# PCL2-Nex

**基于 PCL2 与 PCL2-CE 二次开发的 Minecraft 启动器**
<br>
专注于联机体验增强与可扩展架构设计

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
<br>
[![Base](https://img.shields.io/badge/Base-PCL2-green.svg)](https://github.com/Meloong-Git/PCL)
[![Base](https://img.shields.io/badge/Base-PCL2--CE-green.svg)](https://github.com/PCL-Community/PCL2-CE)

</div>

---

## 📖 简介

**PCL2-Nex** 是一款基于  
[Plain Craft Launcher 2 (PCL2)](https://github.com/Meloong-Git/PCL)  
及其社区版  
[PCL2-CE](https://github.com/PCL-Community/PCL2-CE)  
进行二次开发的 Minecraft 启动器项目。

在保留 PCL2 原有简洁、稳定体验的基础上，  
PCL2-Nex 重点探索 **联机互动功能的扩展**，  
并引入了 **可插拔的扩展机制**，以提升整体灵活性与可维护性。

> **说明**  
> PCL2-Nex 本体遵循 PCL2 / PCL2-CE 的相关许可与使用约定。  
> 本项目所集成的插件系统基于独立的插件框架实现，  
> **插件框架及其插件本身为独立作品，其许可与责任由各自项目单独承担。**

---

## ✨ 核心特性

* **插件扩展支持**  
  PCL2-Nex 集成并使用独立的插件框架  
  **[nex-plugin-framework](https://github.com/Nex-Devloper/nex-plugin-framework)**  
  以实现功能扩展。  
  该框架为 **通用插件系统**，在设计上与 PCL2 的具体实现解耦，  
  可被其他项目独立使用。

* **联机体验增强**  
  针对多人游戏与联机场景进行优化，  
  提供更便捷的联机交互支持。

* **模块化设计**  
  启动器核心与扩展功能解耦，  
  便于后续维护与功能演进。

* **开源协作**  
  项目遵循开源原则，欢迎社区参与改进与贡献。

---

## 🧩 插件与扩展

* **nex-plugin-framework**  
  <https://github.com/Nex-Devloper/nex-plugin-framework>

  nex-plugin-framework 是一个 **独立开源项目**，  
  负责为 PCL2-Nex 提供插件加载与生命周期管理能力。

  - 该框架 **不包含 PCL2 或 PCL2-CE 的源码**
  - 不属于 PCL2 及其衍生项目的一部分
  - 插件及插件框架的许可方式以其各自仓库声明为准

---

## 🔗 致谢与上游项目

PCL2-Nex 的实现离不开以下优秀开源项目的支持：

### 启动器上游

* **[PCL2-CE (Community Edition)](https://github.com/PCL-Community/PCL2-CE)**  
  社区维护的 PCL2 改进版本。
* **[PCL2 (Meloong-Git)](https://github.com/Meloong-Git/PCL)**  
  由龙腾猫跃开发的 PCL2 原始项目。

### 扩展与插件相关

* **[nex-plugin-framework](https://github.com/Nex-Devloper/nex-plugin-framework)**  
  独立的通用插件框架项目。

---

## 📦 安装与使用

1. 前往 [Releases](链接) 页面下载最新版本。
2. 解压并运行 `PCL2-Nex.exe`。
3. 按提示完成 Minecraft 环境配置。

---

## ⚖️ 许可证说明

* **PCL2-Nex（本仓库）**：  
  采用 **Apache License 2.0** 许可证发布。

* **上游项目**：  
  PCL2 与 PCL2-CE 的版权与使用约定归其各自作者与项目所有。

* **插件框架与插件**：  
  nex-plugin-framework 及基于其开发的插件为独立作品，  
  **不受 PCL2 使用指南的直接约束**，  
  其许可证以对应仓库的 `LICENSE` 文件为准。

---

<div align="center">
  <strong>PCL-Nex-Developer</strong>
</div>
