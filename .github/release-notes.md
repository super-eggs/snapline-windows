## 中文更新

- 鼠标移到屏幕顶部时，晾衣绳缓缓滑下，移开后滑回顶部。收起途中移回鼠标，面板会反向滑回。
- 修复截图复制时与后台剪贴板收集争用，以及图片已可粘贴时仍提示复制失败的情况。
- 托盘启动时先准备快捷键，首次展开晾衣绳时再加载历史缩略图。
- 晾衣绳收起时按快捷键可以直接开始截图，抓屏和暗色背景在后台准备。
- 放大预览时保留清晰像素边缘，减少标注时的重复绘制，并在后台保存预览修改。

## English updates

- The shelf slides down when you hover near the top of the screen and slides back up when you move away. Move the pointer back during dismissal to reverse the motion.
- Fixed screenshot copying when background clipboard collection competes for access, and avoided failure messages when the image is already available to paste.
- Tray startup registers shortcuts immediately and loads history thumbnails when you first open the shelf.
- The capture shortcut starts immediately when Snapline's windows are hidden. Screen capture and backdrop preparation run in the background.
- Kept pixel edges sharp when zooming, reduced unnecessary redraws while annotating, and saved preview edits in the background.

## 下载与安装

- **Snapline-Setup-1.0.9-x64.exe** 为 Windows 10/11 x64 安装包。双击安装，从开始菜单启动。
- **Snapline-Portable-1.0.9-x64.zip** 为免安装版。解压后运行 Snapline.exe。
- **SHA256SUMS.txt**：上述文件的 SHA-256 校验值。

安装到当前用户目录，无需管理员权限；可选桌面快捷方式，可从 Windows 设置卸载。卸载保留 `%LOCALAPPDATA%\Snapline` 中的截图和配置。

需要 .NET Framework 4.8 或更高版本；安装包会检查，不会自动下载安装运行时。安装包及程序暂未进行代码签名，Windows 可能显示未知发布者或 SmartScreen 提示。

## 主要功能

自动收集剪贴板图片、桌面透明晾衣绳、连续横向浏览、单击复制、预览缩放旋转、基础画笔标注、置顶/普通/置底配置。

运行后驻留系统托盘。用 Win+Shift+S 截图，按 Ctrl+Alt+T 展开晾衣绳。
