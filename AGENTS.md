# 快截标准版 repository guidance

## Product invariants

- 快截标准版是面向 64 位 Windows 10/11 的 WinForms 单文件截图工具。
- 保持 clipboard-first：截图完成后关闭覆盖层，由用户在目标应用中按 `Ctrl+V` 粘贴。
- 标准版不包含 OCR、模型、网络上传、遥测或云服务。
- 保持 Windows 自带 .NET Framework `csc.exe` 直接构建，不引入包管理器或安装器。
- `Miashot` 名称只用于历史命名空间、设置迁移和图标源文件名，不得作为当前产品名称展示。

## Commands

- Build: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`
- Verify: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1`
- Interactive UI: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1 -Ui`

## Change rules

- 保持源码和文档为 UTF-8；PowerShell 5.1 脚本保持 ASCII，或使用兼容 BOM。
- 不手工编辑 `dist/`、`artifacts/` 或已编译文件。
- 不终止用户已经运行的快截或历史 Miashot 进程。
- 剪贴板写入必须使用现有重试辅助逻辑。
- 重复路径上的 GDI 和 WinForms 资源必须显式释放。
