<div align="center">

<img src="PCL2-Nex.png" alt="Logo" width="80" height="80">

# PCL2-Nex

**基于 PCL2 与 PCL2-CE 二次开发的 Minecraft 启动器**
<br>
专注于联机功能扩展与规范化扩展接口设计

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

在继承 PCL2 原有设计理念与核心体验的基础上，  
PCL2-Nex 主要聚焦于 **联机相关功能的扩展**，  
并整理、抽象出一套用于扩展启动器能力的 **统一接口规范**，  
以便后续功能演进与生态协作。

---

## ✨ 核心特性

* **联机功能增强**  
  针对多人游戏与联机场景进行优化，  
  提供更友好的联机相关功能支持。

* **规范化扩展接口（NexAPI）**  
  PCL2-Nex 在现有架构基础上整理并定义了 **NexAPI**，  
  用于描述扩展模块在生命周期、能力声明与交互方式上的行为约定。

  NexAPI 仅作为接口规范存在，  
  不包含具体实现逻辑，  
  其设计目标是保持启动器核心与扩展功能之间的清晰边界。

* **模块化设计**  
  启动器核心逻辑与扩展能力通过接口进行解耦，  
  有助于提升可维护性与长期演进能力。

---

## 🧩 关于 NexAPI

**NexAPI** 是 PCL2-Nex 在 PCL2 原有结构基础上整理形成的扩展接口规范，  
用于约定扩展模块如何与启动器进行交互。

* NexAPI 属于 PCL2-Nex 项目的一部分
* 受 PCL2 及其相关使用指南的约束
* 仅定义接口、数据结构与行为约定
* 不限定接口的具体实现方式

通过 NexAPI 开发的扩展内容，  
其具体实现、分发方式与许可证选择，  
由对应的实现项目自行决定并承担相应责任。

---

## 🔗 致谢与上游项目

PCL2-Nex 的实现离不开以下优秀项目的支持与贡献：

* **[PCL2 (Meloong-Git)](https://github.com/Meloong-Git/PCL)**  
  由龙腾猫跃开发的 Plain Craft Launcher 2 原始项目。
* **[PCL2-CE (Community Edition)](https://github.com/PCL-Community/PCL2-CE)**  
  社区维护的 PCL2 改进版本。

---

## 📦 安装与使用

1. 前往 [Releases](链接) 页面下载最新版本。
2. 解压并运行 `PCL2-Nex.exe`。
3. 按提示完成 Minecraft 环境配置。

---

## ⚖️ 许可证说明

* **PCL2-Nex（本仓库）**  
  采用 **Apache License 2.0** 许可证发布。

* **上游项目**  
  PCL2 与 PCL2-CE 的版权与使用约定归其原作者及项目所有。

* **NexAPI**  
  NexAPI 作为 PCL2-Nex 的扩展接口规范，来源于对 PCL2-CE 架构与行为的抽象与整理，其使用与分发遵循 PCL2-CE 的相关许可与使用约定。

* **接口实现与扩展内容**  
  基于 NexAPI 的具体实现项目与扩展内容，  
  在不包含 PCL2 / PCL2-Nex 源码的前提下，  
  其许可证与法律责任由对应项目自行决定并承担。

---

<div align="center">
  <strong>PCL-Nex-Developer</strong>
</div>
