### 代理下载规则集

`ProxyAccel` 是一个参照 `LLin` 的**下载加速部分**开发的精简规则集（vibe coding）。

它刻意只保留了一个功能：

* 在点击 **谱面预览（beatmap preview）** 后弹出代理下载窗口
* 点击弹窗里的下载按钮后，通过自定义 worker 转发 `osu!lazer direct download`

![示例图](image.png)
### 功能

* 代理转发 `osu!lazer direct download`
* 设置中可填写 `加速 URL`
* 设置中可点击 `Test` 测试 worker 是否可用
* 弹窗显示在**右上角**


### 使用说明

#### 1. 部署 worker

* 部署 [worker/osu_proxy_worker.js](/worker/osu_proxy_worker.js) 到你的 worker 平台
* 部署完成后，访问 `http://example.yourdomain.workers.dev/healthz`
* 返回 `200` 和 JSON 即表示 worker 可用
* 如果一直不可用可能是以下原因
  * url地址出错
  * worker分配域名由于dns污染导致不可用，解决办法：自定义域名（自行网上搜索）



#### 2. 使用 ruleset

1. 设置配置 `加速 URL`，例如：`http://example.yourdomain.workers.dev`
2. 测试是否可用，多次点击Test看是否可用
3. 进入在线谱面列表，播放音频预览后点击右上角弹出的下载按钮


### 本地构建

```powershell
dotnet build .\osu.Game.Rulesets.ProxyAccel\osu.Game.Rulesets.ProxyAccel.csproj -c Release
```

### 输出文件

* `osu.Game.Rulesets.ProxyAccel\bin\Release\net8.0\osu.Game.Rulesets.ProxyAccel.dll`

</details>
