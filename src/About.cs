// smena.txt. About.cs — маленькое окно «О программе» из значка в трее: что сейчас с Alt+Tab, кнопка «Показать»
// и связь с core.txt. Настроек здесь нет: они на общей странице в core.txt.
// Всё собрано из общих элементов (Ui.cs) и токенов (Tokens.cs).
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevTxt.Smena
{
    class AboutWindow : Window
    {
        const double W = 880, H = 560;

        readonly bool demo;
        readonly Status hook = new Status(), hub = new Status();
        readonly Btn tryBtn = new Btn(), coreBtn = new Btn();

        public Action Try;   // показать переключатель

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public AboutWindow(bool demo)
        {
            this.demo = demo;
            Title = Program.Id + ".txt";
            Width = W; Height = H;
            // Родное окно Windows 11: системная рамка и тень, Mica, тема Windows, прилипание к краям экрана
            Ui.Native(this, false);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Bg;
            FontFamily = Ui.TextFont;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            Content = Build();
            Render();

            Prefs.Changed += Render;
            AppHub.StatusChanged += Render;
            // Окно не закрывается, а прячется: приложение живёт в трее
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (!Program.Quitting) { e.Cancel = true; Hide(); } };

        }

        FrameworkElement Build()
        {
            // Общий каркас dev.txt: слева значок, имя и раздел, справа заголовок и содержимое
            var shell = new NavShell(this, Program.Sprite, Program.Id, "версия " + Program.VersionText, Hide);
            shell.Add("Главное", Ui.Glyph(""));
            shell.Selected = 0;
            shell.Title("Переключение окон", "Alt+Tab с живыми превью вместо системного");
            var footer = Ui.T("dev.txt™", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
            footer.VerticalAlignment = VerticalAlignment.Center;
            shell.Bottom.Children.Add(footer);

            // Блок: что с Alt+Tab и кнопка «Показать»; под чертой — связь с core.txt, где лежат настройки
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            text.Children.Add(hook.Root);
            var keys = Ui.T("Alt+Tab — вперёд, Shift — назад, стрелки — выбор, Esc — отмена. Отпустите Alt — и вы в выбранном окне.", Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
            keys.TextWrapping = TextWrapping.Wrap; keys.Margin = new Thickness(14, 4, 0, 0);
            text.Children.Add(keys);
            var first = new DockPanel { LastChildFill = true, MinHeight = 32 };
            tryBtn.Root.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(tryBtn.Root, System.Windows.Controls.Dock.Right);
            first.Children.Add(tryBtn.Root);
            first.Children.Add(text);

            var line = new Border { Height = 1, Background = Ui.Separator, Margin = new Thickness(0, 16, 0, 16) };

            var second = new DockPanel { LastChildFill = true };
            coreBtn.Root.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(coreBtn.Root, System.Windows.Controls.Dock.Right);
            second.Children.Add(coreBtn.Root);
            hub.Root.VerticalAlignment = VerticalAlignment.Center;
            hub.Root.Margin = new Thickness(0, 0, 16, 0);
            second.Children.Add(hub.Root);

            var block = Ui.Block(new StackPanel { Children = { first, line, second } }, true);
            block.Margin = new Thickness(32, 0, 32, 0);
            block.VerticalAlignment = VerticalAlignment.Top;
            shell.Body.Children.Add(block);
            return shell.Root;
        }

        FrameworkElement WindowButton(string glyph, string tip, Action click)
        {
            var t = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = Tokens.Size.Micro, Foreground = Ui.Label2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = 36, Height = 26, CornerRadius = new CornerRadius(Tokens.Radius.Tip), Background = Brushes.Transparent, Child = t, ToolTip = tip };
            b.MouseEnter += delegate { b.Background = Ui.Solid(Tokens.Palette.Red); t.Foreground = Ui.Label; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; t.Foreground = Ui.Label2; };
            b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        // Приводит строки состояния в соответствие с тем, что есть
        void Render()
        {
            if (demo || Hook.Installed) hook.Set("Alt+Tab открывает " + Program.Id + ".txt", Status.Kind.Ok);
            else if (Prefs.Paused) hook.Set("Приостановлено — работает обычный Alt+Tab", Status.Kind.Warn);
            else if (!Prefs.Replace) hook.Set("Замена Alt+Tab выключена в настройках", Status.Kind.None);
            else if (!Hook.Allowed) hook.Set("Запущено без перехвата клавиш", Status.Kind.None);
            else hook.Set("Не удалось перехватить Alt+Tab", Status.Kind.Bad);
            tryBtn.Set("Показать", null, true, delegate { if (Try != null) Try(); });

            bool online = demo || AppHub.Online;
            if (online) hub.Set("Настройки — в core.txt", Status.Kind.Ok);
            else if (CoreApp.Installed) hub.Set("core.txt не запущен — всё работает и без него", Status.Kind.None);
            else hub.Set(CoreApp.Pitch, Status.Kind.None);
            // Запущенный core.txt открывается сразу на странице настроек smena.txt
            coreBtn.Set(CoreApp.Label(online), online || CoreApp.Installed ? (Color?)null : Tokens.Palette.Brand, true, delegate { if (!demo) CoreApp.Go(AppHub.Online, delegate { AppHub.ShowCore(AppHub.SettingsPage); }); });
        }

        public void ShowWindow()
        {
            Render();
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            // По «Открыть» в меню core.txt или по команде хаба впереди чужая программа, и Windows может не пустить окно
            // вперёд: оно откроется позади других или только мигнёт на панели задач. Тогда — тем же способом, каким
            // переключатель выводит вперёд выбранное окно.
            if (!Activate() && !demo) Win.Activate(new WindowInteropHelper(this).Handle);
        }

        // Рисует окно в файл PNG, не показывая его на экране
        public void Shot(string file)
        {
            var root = (FrameworkElement)Content;
            Content = null;
            root.Measure(new Size(W, H));
            root.Arrange(new Rect(0, 0, W, H));
            root.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)(W * 1.5), (int)(H * 1.5), 144, 144, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = System.IO.File.Create(file)) enc.Save(f);
        }
    }
}
