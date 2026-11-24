## 中文更新

- 移除预览画布的阴影，放大时保留清晰的像素边缘。
- 减少拖动形状、连续缩放和应用局部马赛克时的重复绘制。
- 预览保存改为后台编码与写入，保存期间防止重复触发。

## English updates

- Removed the preview canvas shadow and kept pixel edges sharp when zooming in.
- Reduced rendering work while dragging shapes, scrolling to zoom, and applying a mosaic to part of an image.
- Moved preview encoding and file writes to the background and prevented repeat clicks during saves.

## 下载与安装

- **Snapline-Setup-1.0.5-x64.exe** 为 Windows 10/11 x64 安装包。双击安装，从开始菜单启动。
- **Snapline-Portable-1.0.5-x64.zip** 为免安装版。解压后运行 Snapline.exe。
- **SHA256SUMS.txt**：上述文件的 SHA-256 校验值。

安装到当前用户目录，无需管理员权限；可选桌面快捷方式，可从 Windows 设置卸载。卸载保留 `%LOCALAPPDATA%\Snapline` 中的截图和配置。

需要 .NET Framework 4.8 或更高版本；安装包会检查，不会自动下载安装运行时。安装包及程序暂未进行代码签名，Windows 可能显示未知发布者或 SmartScreen 提示。

## 主要功能

自动收集剪贴板图片、桌面透明晾衣绳、连续横向浏览、单击复制、预览缩放旋转、基础画笔标注、置顶/普通/置底配置。

运行后驻留系统托盘。用 Win+Shift+S 截图，按 Ctrl+Alt+T 展开晾衣绳。
