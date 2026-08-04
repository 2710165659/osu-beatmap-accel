# Changelog

All notable changes to BeatmapAccel will be documented in this file.

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
