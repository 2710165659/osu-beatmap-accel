### 代理下载规则集

`BeatmapAccel` 是一个参照 `LLin` 的**下载加速部分**开发的精简规则集（vibe coding）。

它刻意只保留了一个功能：

* 在点击 **谱面预览（beatmap preview）** 后弹出代理下载窗口
* 点击弹窗里的下载按钮后，通过内置测速器预选的最快 Cloudflare IP 直连 `osu!lazer direct download`

![示例图](image.png)
### 功能

* 使用内置测速器从预设 Cloudflare IPv4 段里选择当前最快 Cloudflare IP
* 支持手动填写当前优选 IP
* 游戏启动时可自动测速并切换
* 下载失败后可自动重新测速并切换
* 可选加入 IPv6 候选测速
* 设置中可手动点击 `测速并切换`
* 弹窗显示在**右上角**


### 使用说明

#### 使用 ruleset

1. 按需手动填写 `当前优选 IP`
2. 按需开启 `启动自动测速切换`
3. 按需开启 `下载失败后自动测速切换`
4. 按需开启 `启用 IPv6 候选测速`
5. 点击 `测速并切换`，切到当前测速结果中的最佳 IP
6. 进入在线谱面列表，播放音频预览后点击右上角弹出的下载按钮


### 本地构建

```powershell
dotnet build .\osu.Game.Rulesets.BeatmapAccel\osu.Game.Rulesets.BeatmapAccel.csproj -c Release
```

### 输出文件

* `osu.Game.Rulesets.BeatmapAccel\bin\Release\net8.0\osu.Game.Rulesets.BeatmapAccel.dll`

</details>
