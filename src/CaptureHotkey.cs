using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class HotkeyMessageWindow : Forms.NativeWindow, IDisposable
    {
        internal event Action<int> Hotkey;
        internal HotkeyMessageWindow()
        {
            CreateHandle(new Forms.CreateParams { Caption = "Snapline Hotkeys", Parent = new IntPtr(-3) });
        }
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x0312 && Hotkey != null) Hotkey(message.WParam.ToInt32());
            else base.WndProc(ref message);
        }
        public void Dispose() { DestroyHandle(); }
    }

    internal sealed class CaptureHotkey : IDisposable
    {
        private readonly IntPtr handle;
        private readonly HwndSource source;
        private readonly HotkeyMessageWindow messageWindow;
        private readonly Action capture;
        private readonly int firstId;
        private int registeredId;
        private bool configuring;
        internal string Gesture { get; private set; }
        internal bool Registered { get { return registeredId != 0; } }
        internal static HotkeyMessageWindow CreateMessageSource()
        {
            return new HotkeyMessageWindow();
        }
        internal CaptureHotkey(Window owner, Action capture, int firstId = 2)
            : this(HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle), capture, firstId) { }

        private CaptureHotkey(HwndSource source, Action capture, int firstId)
        {
            this.capture = capture; this.firstId = firstId; this.source = source;
            handle = source.Handle; source.AddHook(Hook);
        }
        internal CaptureHotkey(HotkeyMessageWindow source, Action capture, int firstId = 2)
        {
            this.capture = capture; this.firstId = firstId; messageWindow = source;
            handle = source.Handle; source.Hotkey += OnHotkey;
        }
        internal bool Register(string value)
        {
            KeyGesture gesture;
            try { gesture = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(value); }
            catch (NotSupportedException) { return false; }
            catch (ArgumentException) { return false; }
            if (gesture == null || gesture.Key == Key.None || gesture.Key == Key.System) return false;
            if (gesture.Modifiers == ModifierKeys.None && (gesture.Key < Key.F1 || gesture.Key > Key.F24)) return false;
            if (registeredId != 0 && Gesture == value) return true;
            int id = registeredId == firstId ? firstId + 1 : firstId;
            if (!Native.RegisterHotKey(handle, id, 0x4000 | (uint)gesture.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key))) return false;
            if (registeredId != 0) Native.UnregisterHotKey(handle, registeredId);
            registeredId = id; Gesture = value; return true;
        }
        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message == 0x0312 && registeredId != 0 && wparam.ToInt32() == registeredId)
            {
                handled = true; OnHotkey(registeredId);
            }
            return IntPtr.Zero;
        }
        private void OnHotkey(int id)
        {
            if (registeredId != 0 && id == registeredId && !configuring) capture();
        }
        internal void Configure(Store store, bool paste = false, bool toggle = false)
        {
            var dialog = new Window { Title = toggle ? "隐藏 / 显示贴图快捷键" : paste ? "贴图快捷键" : "截图快捷键", Width = 360, Height = 260, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowInTaskbar = false, Background = Ui.Surface, Foreground = Ui.Text, FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI") };
            var content = new StackPanel { Margin = new Thickness(20) };
            content.Children.Add(new TextBlock { Text = toggle ? "按下快捷键或输入名称，例如 Shift+F3。" : paste ? "按下快捷键或输入名称，例如 F3、Ctrl+Alt+P。" : "按下快捷键或输入名称，例如 F1、Ctrl+Alt+A。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            string pending = toggle ? store.Settings.TogglePinsHotkey : paste ? store.Settings.PinHotkey : store.Settings.CaptureHotkey;
            var input = new TextBox { Text = pending, FontSize = 18, Padding = new Thickness(8) };
            var status = new TextBlock { Text = Registered ? "当前快捷键已启用" : "快捷键未启用，请选择一个未被占用的组合", Margin = new Thickness(0, 8, 0, 6), TextWrapping = TextWrapping.Wrap };
            input.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) { e.Handled = true; return; }
                // An already registered system shortcut may never reach the editor; allow typing its name.
                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0 && (key < Key.F1 || key > Key.F24)) return;
                e.Handled = true;
                try { pending = new KeyGestureConverter().ConvertToInvariantString(new KeyGesture(key, Keyboard.Modifiers)); input.Text = pending; status.Text = "点击保存后生效"; }
                catch (NotSupportedException) { status.Text = "请使用 F1–F24，或带 Ctrl / Alt 的组合"; }
            };
            input.TextChanged += delegate { pending = input.Text; status.Text = "点击保存后生效"; };
            content.Children.Add(input); content.Children.Add(status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Ui.Button("取消", "保留当前快捷键", delegate { dialog.Close(); }));
            buttons.Children.Add(Ui.Button("保存", "启用并记住截图快捷键", delegate
            {
                if (!Register(pending)) { status.Text = "该快捷键无效或已被占用，原设置仍保留"; return; }
                if (toggle) store.Settings.TogglePinsHotkey = pending; else if (paste) store.Settings.PinHotkey = pending; else store.Settings.CaptureHotkey = pending;
                store.SaveSettings(); dialog.Close();
            }));
            content.Children.Add(buttons); dialog.Content = content; dialog.Loaded += delegate { input.Focus(); input.SelectAll(); };
            configuring = true;
            try { dialog.ShowDialog(); }
            finally { configuring = false; }
        }
        public void Dispose()
        {
            if (registeredId != 0) Native.UnregisterHotKey(handle, registeredId);
            registeredId = 0;
            if (messageWindow != null) messageWindow.Hotkey -= OnHotkey;
            else source.RemoveHook(Hook);
        }
    }
}
