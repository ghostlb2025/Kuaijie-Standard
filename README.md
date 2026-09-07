# 快截标准版

![快截图标](assets/MiashotIcon.svg)

快截是一款小巧、便携、完全本地运行的 Windows 截图工具。它保留完整的区域截图、标注、全屏截图、延迟截图和保存能力，不包含 OCR 引擎、模型、上传或遥测。

## 下载

在 GitHub 的 **Releases** 页面下载 `Kuaijie-Standard-Windows-x64.zip`，解压后直接运行 `快截-标准版.exe`，无需安装。

## 功能

- 快速区域截图：框选后立即复制到剪贴板。
- 高级区域截图：可调整选区，并添加矩形、箭头、画笔、马赛克和文字标注。
- 当前显示器全屏截图：包含任务栏，不包含鼠标指针。
- 3 秒延迟截图：适合截取系统托盘、右键菜单和下拉菜单。
- 保存最近截图和截图后自动保存 PNG。
- 托盘提供独立的“截图保存设置”，可设置保存位置和文件名模板，并实时预览 PNG 文件名。
- 文件名模板支持 `{日期}`、`{时间}`、`{序号}`；遇到同名文件时自动递增，绝不覆盖。
- 五组快捷键均可修改或设为“未设置”。
- 打开快捷键设置时会临时暂停全局热键，方便直接互换组合；取消后恢复原设置。

## 默认快捷键

| 功能 | 快捷键 |
| --- | --- |
| 快速区域截图 | `Alt+A` |
| 高级区域截图 | `Alt+D` |
| 当前显示器全屏截图 | `Alt+S` |
| 保存最近截图 | `Alt+W` |
| 3 秒后区域截图 | `Alt+E` |

截图默认只进入剪贴板，回到 Word、画图或聊天窗口后按 `Ctrl+V` 即可粘贴。自动保存开启后，PNG 默认写入 Windows“图片”文件夹，默认文件名类似 `快截_20260907_001.png`。保存设置同时作用于自动保存、高级工具栏保存和“保存最近截图”。

## 隐私与系统要求

- 截图和标注完全在本机处理。
- 不上传截图，不联网，不收集遥测。
- 支持 64 位 Windows 10/11。
- 单文件运行，不需要安装 .NET SDK 或其他运行时。
- 设置保存在 `%LocalAppData%\Kuaijie-Standard\settings.ini`。

## 从源码构建

Windows 自带的 .NET Framework C# 编译器即可完成构建：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成文件位于 `dist/`：`快截-标准版.exe` 和 `快截-标准版.zip`。

运行自动验证：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1
```

交互式桌面验证会真实移动鼠标和触发全局快捷键，只应在空闲桌面运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1 -Ui
```

## 许可

第一方源码使用 [MIT License](LICENSE)。
