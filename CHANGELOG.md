# Changelog

All notable changes to BeatmapAccel will be documented in this file.

## [v1.1.0]

- HTTP 测速返回 403 时不再判定为探测可用，下载失败后的恢复探测同步应用此规则。
- 新增“是否忽略代理”开关，默认关闭。开启后绕过系统代理直连优选 IP，并允许在代理环境下启用全局谱面下载接管；切换即时生效。
- 检测到代理且未开启忽略代理时，提示“检测到代理，接管所有谱面下载默认不生效，可开启忽略代理生效”。
- 项目升级至 .NET 10，同步更新构建、发布流程及本地构建说明。

## [v1.0.6]

- 适配 osu! `2026.819.0-tachyon`，修复设置页面 API 变更导致规则集无法加载的问题。

## [v1.0.5]

- 修复单人旁观时将谱面集 ID 当作难度谱面 ID，导致本地已有谱面仍重复下载的问题[#3](https://github.com/2710165659/osu-beatmap-accel/issues/3)。
- 修复同一 ID 混用导致谱面导入完成后本地可用性确认无效的问题。

## [v1.0.4]

- 修复好友观战自动下载不可用的问题
- 检测到开代理后自动不接管谱面下载

## [v1.0.3]

- 修复了tachyon709更新引起的报错
- 优化下载出错自动测速切换逻辑

## [V1.0.2]

- 修复了多人房间内下载歌曲无限卡死在导入环节的问题[#1](https://github.com/2710165659/osu-beatmap-accel/issues/1)。

## [v1.0.1]

### Fixed
- 适配 osu! `2026.618.0-tachyon`：修复 `BeatmapManager.IsAvailableLocally(BeatmapSetInfo)` 改为抛异常后导致的下载完成确认报错。改用 `IBeatmapInfo` 重载。

## [Previous]

### Changed
- 性能优化：轮询改事件驱动
- 构建发布流程优化
- 好友观战自动下载接管
- 修复 lazer 423 更新后 rank play 自动下载失效问题
- 兼容层增加、安卓兼容性改进
