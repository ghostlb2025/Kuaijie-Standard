# 开发指南

快截标准版使用 Windows 自带的 .NET Framework 编译器直接构建，不需要 NuGet、.NET SDK、安装器或第三方运行时。

## 环境

- 64 位 Windows 10/11
- Windows PowerShell 5.1 或更新版本
- `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`

## 命令

```powershell
.\build.ps1
.\verify.ps1
.\verify.ps1 -Ui
```

构建输出写入 `dist/`，验证输出写入 `artifacts/`，二者都不会进入 Git。

## 设计边界

- 默认流程始终是截图后进入剪贴板，不创建悬浮截图窗口。
- 不加入 OCR、网络上传、遥测或云服务。
- 完整和高级截图共用同一套设置、热键与保存逻辑。
- 重复截图路径中的 GDI 和 WinForms 对象必须正确释放。
