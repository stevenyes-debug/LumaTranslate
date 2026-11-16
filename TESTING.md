# 验证记录

验证日期：2026-09-20。Windows x64；.NET Framework 4.8；C# 5；WPF。没有依赖 npm、NuGet 或 Python 运行桌面应用。

## 已完成

| 检查 | 结果与范围 |
| --- | --- |
| 完整构建 | 成功生成 x64 Windows GUI EXE。剩余一条 Windows SDK WinRT / System.Runtime facade 版本统一警告；实际 OCR 已能运行。 |
| 发行包结构 | x64 PE、GUI 子系统、DPI / asInvoker manifest、.NET 4.8 配置及多尺寸图标均通过。 |
| 真实界面渲染 | 主窗口、设置页、可编辑双语结果窗离屏渲染并目视检查；预览使用示例文字。 |
| 浮窗状态 | 原文编辑、旧译文清除、语言切换、请求取消身份检查、错误／加载／完成状态通过；标题栏拖动仍需实际鼠标验收。 |
| 核心逻辑 | 25 组全部通过：Unicode 边界、段落保留、最多 3 路并发、原序重建、重复／在途请求合并、独立取消、HTTP / JSON / 额度错误、超时、缓存、原子保存与损坏文件恢复等。 |
| 本地 OCR | 系统返回 `en-US`、`zh-Hans-CN`；真实生成的图片识别得到 `HELLO WORLD` 与 `WINDOWS TRANSLATION`。 |
| 全局快捷键 | 注册、交换、重复与非法组合、F12、冲突回滚、临时注册清理及 Dispose 释放通过。 |
| 非激活截图 | 隐藏遮罩 HWND 具有 `WS_EX_NOACTIVATE`，`WM_MOUSEACTIVATE` 不激活，保持当前前台窗口；源码没有激活、隐藏或最小化外部窗口的调用。 |
| 截图性能 | 1920 × 1080、7 次预热后，直接 BGRA 图像准备中位 3.00 ms；原 PNG 中转为 17.54 ms。固定 35 + 90 ms 等待已移除。数据不含 OCR／网络。 |
| 窗口与托盘生命周期 | 主窗 / 历史 / 设置 / 浮窗创建通过；关闭到托盘关闭时的延后退出无重入；托盘和快捷键创建、重复 Dispose 清理通过。 |
| 真实公共翻译服务 | 独立使用 Python 标准库 HTTPS 客户端访问同一 MyMemory 接口，英→中、中→英均 HTTP 200、业务状态 200。Python 只用于验证，不是应用运行依赖。 |

真实服务样例：

- 原文：`Hello world. A window to understanding.`
- 返回：`你好，世界。理解的窗口。`
- 原文：`让理解，没有边界。`
- 返回：`Let it be understood that there are no boundaries.`

这些是服务实际返回值，不代表人工润色质量；UI 预览图中使用的演示译文与上述联网样例分开。

## 未完成或受限的检查

当前受限执行环境里，应用同款 .NET `HttpClient` 的 HTTPS 握手返回 Windows 错误「安全包中没有可用的凭证」；系统 WinHTTP 对同一地址也返回客户端凭据错误。应用所用的证书校验未被关闭，HTTPS 没有降级为 HTTP。

因此，**应用自身在普通用户桌面会话的真实联网请求仍需验收**。独立 Python 客户端成功仅验证接口与网络可达，不替代 .NET 传输链路验证。核心自动测试使用可注入 HTTP Handler 测试真实分段、编码、解析和请求状态，未将模拟响应当成线上完成结果。

没有自动操纵用户当前浏览器选区或剪贴板，也没有进行可见的屏幕框选。以下需要运行成品后手动检查：

1. 打开 EXE，输入 `Hello world`，原文选英语，目标选简体中文，点击翻译。
2. 关闭主窗口，在托盘找到映译图标，双击可重开；右键「退出映译」应移除托盘图标。
3. 在浏览器或记事本选中一句文字，按 `Ctrl + Alt + D`，检查中央浮窗和两个复制按钮。拖动浮窗标题；修改原文、语言和译文，按 `Ctrl + Enter` 重新翻译。
4. 按 `Ctrl + Alt + A`，框选一段清晰文字，检查遮罩、裁剪、OCR 和译文。再测试 Esc / 右键取消，并确认原应用没有被最小化或强制带到前台。
5. 保存新的快捷键并确认旧组合释放、新组合生效；尝试已被其他软件占用的组合，确认原组合仍能用。
6. 浮窗中测试置顶、调整大小、Esc 关闭、网络失败后的重试。请求进行中修改原文，确认旧请求不会覆盖新输入。
7. 在游戏或视频的独占／无边框全屏模式中触发截图，确认遮罩与浮窗出现时程序没有自行最小化；此项尚未实机完成。
8. 如有副屏，分别在 100% / 150% / 200% 缩放及负坐标副屏上框选，核对选区对应。
9. 关闭网络后应显示连接错误；恢复网络后可重试。用超过 500 UTF-8 字节的长文本检查分段并发与最终顺序。

## 运行自动检查

从发行包目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\build-test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\core-test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\native-test.ps1
```

主程序也包含离屏自检入口。请为报告传入完整路径；该模式不发送联网翻译请求：

```powershell
$report = Join-Path $env:TEMP 'LumaTranslate-self-test\report.json'
$test = Start-Process .\LumaTranslate.exe -ArgumentList @('--self-test', ('"' + $report + '"')) -PassThru -WindowStyle Hidden
$test.WaitForExit()
Get-Content -LiteralPath $report
```

重新生成实际界面演示：

```powershell
$preview = Join-Path (Get-Location) 'preview'
$render = Start-Process .\LumaTranslate.exe -ArgumentList @('--render-preview', ('"' + $preview + '"')) -PassThru -WindowStyle Hidden
$render.WaitForExit()
```

自检 / 测试产生的图片、JSON 和临时设置仅为测试资料。普通运行不会保存屏幕截图，也不会写入演示文本。
