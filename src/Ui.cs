// kit.txt — общие элементы интерфейса dev.txt™ для C# (WPF): Kapsula, core.txt и всё, что появится позже.
// Выглядят и ведут себя как Windows 11: фон Mica, светлая и тёмная тема по Windows, скругления 8 и 4,
// элементы управления и движение по правилам Fluent, переход по Tab с рамкой фокуса.
// Копируется в приложение как есть, рядом с Tokens.cs, и руками там не правится. Здесь нет ни одного своего
// цвета, радиуса или длительности — только токены. Те же элементы для веба — в ui/kit.css; менять их нужно вместе.
// Пишется на C# 5: его понимает компилятор, встроенный в Windows.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DevTxt
{
    static class Ui
    {
        public static readonly FontFamily TextFont = new FontFamily(Tokens.Font.Text), DisplayFont = new FontFamily(Tokens.Font.Display);
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        // Плавность: WPF по умолчанию считает анимации на 60 кадрах, даже если монитор быстрее, — на 144 Гц движение
        // рвётся. Поднимаем до частоты монитора, один раз на всё приложение, до первой анимации.
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
        static bool smooth;
        // false — оставить 60 кадров. Нужно приложениям с большими прозрачными окнами (переключатель smena на весь
        // экран): такие окна Windows перерисовывает целиком через процессор, и 180 раз в секунду он не успевает — мерцает.
        public static bool AllowSmooth = true;
        public static void Smooth()
        {
            if (smooth || !AllowSmooth) return;
            smooth = true;
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                int hz = GetDeviceCaps(dc, 116);   // VREFRESH
                ReleaseDC(IntPtr.Zero, dc);
                if (hz < 60 || hz > 360) hz = 60;
                Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(hz));
            }
            catch { }
        }

        // Цвета темы. Кисти общие и не заморожены: когда Windows переключает светлую и тёмную тему,
        // ApplyTheme перекрашивает их на месте — и всё, что ими нарисовано, меняется само.
        public static readonly Brush Bg = Live(Tokens.Dark.Bg), Label = Live(Tokens.Dark.Label), Label2 = Live(Tokens.Dark.Label2), Label3 = Live(Tokens.Dark.Label3);
        public static readonly Brush Fill = Live(Tokens.Dark.Fill), Fill2 = Live(Tokens.Dark.Fill2), Press = Live(Tokens.Dark.FillPress), Separator = Live(Tokens.Dark.Separator);
        public static readonly Brush GlassFill = Live(Alpha(Colors.White, Tokens.GlassDark.FillTop));      // поле кнопки
        public static readonly Brush GlassHover = Live(Alpha(Colors.White, Tokens.GlassDark.HoverTop));    // оно же под курсором
        public static readonly Brush GlassStrong = Live(Alpha(Colors.White, Tokens.GlassDark.StrongTop));
        public static readonly Brush Rim = Live(Tokens.GlassDark.RimColors[0]);                            // тонкая обводка
        public static readonly Brush CardFill = Live(Alpha(Colors.White, Tokens.GlassDark.StrongTop));     // карточка, строка настроек
        public static readonly Brush Thumb = Live(Color.FromRgb(0x45, 0x45, 0x45));                        // бегунок ползунка
        public static readonly SolidColorBrush AccentBrush = new SolidColorBrush(Tokens.Palette.Brand);     // акцент: по умолчанию оранжевый dev.txt
        public static readonly SolidColorBrush AccentText = new SolidColorBrush(OnColor(Tokens.Palette.Brand));
        static readonly Brush SpriteBody = Solid(Tokens.Palette.Brand), SpriteEye = Solid(Color.FromRgb(0x1A, 0x1A, 0x1E));

        public static bool Light;                 // сейчас светлая тема
        public static bool ThemeFollows;          // приложение следует теме Windows (окна; оверлеи вроде дока остаются тёмными)
        public static event Action ThemeChanged;

        public static Brush Solid(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static SolidColorBrush Live(Color c) { return new SolidColorBrush(c); }
        public static Color Alpha(Color c, double a) { return Color.FromArgb((byte)Math.Round(255 * Math.Max(0, Math.Min(1, a))), c.R, c.G, c.B); }
        static void Paint(Brush b, Color c) { var s = b as SolidColorBrush; if (s != null && !s.IsFrozen) s.Color = c; }

        // Текст на цветной заливке: тёмный на светлом цвете, белый на тёмном — как у кнопок Windows
        public static Color OnColor(Color c)
        {
            double l = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
            return l > 0.55 ? Color.FromArgb(0xE4, 0, 0, 0) : Colors.White;
        }

        public static Color Accent { get { return AccentBrush.Color; } }
        public static void SetAccent(Color c) { AccentBrush.Color = c; AccentText.Color = OnColor(c); }

        // Цвет акцента Windows («Параметры → Персонализация → Цвета»); null — не удалось узнать
        public static Color? SystemAccent()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object v = k != null ? k.GetValue("AccentColor") : null;
                    if (!(v is int)) return null;
                    uint abgr = unchecked((uint)(int)v);
                    return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                }
            }
            catch { return null; }
        }

        // Тема приложений Windows: true — светлая
        public static bool SystemLight()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        public static void ApplyTheme(bool light)
        {
            Light = light;
            Paint(Bg, light ? Tokens.Light.Bg : Tokens.Dark.Bg);
            Paint(Label, light ? Tokens.Light.Label : Tokens.Dark.Label);
            Paint(Label2, light ? Tokens.Light.Label2 : Tokens.Dark.Label2);
            Paint(Label3, light ? Tokens.Light.Label3 : Tokens.Dark.Label3);
            Paint(Fill, light ? Tokens.Light.Fill : Tokens.Dark.Fill);
            Paint(Fill2, light ? Tokens.Light.Fill2 : Tokens.Dark.Fill2);
            Paint(Press, light ? Tokens.Light.FillPress : Tokens.Dark.FillPress);
            Paint(Separator, light ? Tokens.Light.Separator : Tokens.Dark.Separator);
            Paint(GlassFill, Alpha(Colors.White, light ? Tokens.GlassLight.FillTop : Tokens.GlassDark.FillTop));
            Paint(GlassHover, light ? Alpha(Color.FromRgb(0xF9, 0xF9, 0xF9), Tokens.GlassLight.HoverTop) : Alpha(Colors.White, Tokens.GlassDark.HoverTop));
            Paint(GlassStrong, Alpha(Colors.White, light ? Tokens.GlassLight.StrongTop : Tokens.GlassDark.StrongTop));
            Paint(Rim, light ? Tokens.GlassLight.RimColors[0] : Tokens.GlassDark.RimColors[0]);
            Paint(CardFill, light ? Alpha(Colors.White, Tokens.GlassLight.FillTop) : Alpha(Colors.White, Tokens.GlassDark.StrongTop));
            Paint(Thumb, light ? Colors.White : Color.FromRgb(0x45, 0x45, 0x45));
            if (ThemeChanged != null) ThemeChanged();
        }

        static bool following;
        // Следить за темой Windows: сразу и каждый раз, когда её переключат
        public static void FollowSystemTheme()
        {
            ApplyTheme(SystemLight());
            if (following) return;
            following = true;
            var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += delegate(object s, Microsoft.Win32.UserPreferenceChangedEventArgs e)
            {
                if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
                ui.BeginInvoke(new Action(delegate { bool l = SystemLight(); if (l != Light) ApplyTheme(l); }));
            };
        }

        // Заливка кнопки цветом: ровная, как у кнопок Windows 11
        public static Brush Tinted(Color c) { return Solid(c); }

        // Рамка фокуса с клавиатуры: двойная обводка вокруг элемента, как в Windows
        public static readonly Style Focus = MakeFocus();
        static Style MakeFocus()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            border.SetValue(Border.BorderBrushProperty, Label);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Tokens.Radius.Control + 3));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(-3));
            var style = new Style();
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate { VisualTree = border }));
            return style;
        }

        // Элемент можно выбрать клавишей Tab и нажать Enter или пробелом
        public static void Keyboard(FrameworkElement e, Action press)
        {
            e.Focusable = true;
            e.FocusVisualStyle = Focus;
            e.KeyDown += delegate(object s, KeyEventArgs k)
            {
                if (k.Key != Key.Enter && k.Key != Key.Space) return;
                k.Handled = true;
                if (press != null) press();
            };
        }

        // «Быстрый вход» Windows: cubic-bezier(0, 0, 0, 1) — резкий старт и долгое мягкое торможение
        public static IEasingFunction EaseOut { get { return new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 7 }; } }
        // «Меньше движения»: системная настройка Windows; приложение может включить и само
        public static bool ForceReducedMotion = false;
        public static bool ReducedMotion { get { return ForceReducedMotion || !SystemParameters.ClientAreaAnimation; } }
        // Можно ли двигать элемент: он в настоящем окне (а не в снимке) и движение не выключено
        public static bool Live(Visual v) { return !ReducedMotion && v != null && PresentationSource.FromVisual(v) != null; }

        public static TextBlock T(string text, double size, Brush color, FontWeight weight)
        {
            if (weight == FontWeights.Bold) weight = FontWeights.SemiBold;   // жирного в шкале Windows нет
            return new TextBlock { Text = text, FontSize = size, Foreground = color, FontWeight = weight, FontFamily = size >= Tokens.Size.Title ? DisplayFont : TextFont, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        // Название продукта dev.txt: «kapsula.txt™» — имя яркое, подпись .txt™ приглушена
        public static TextBlock Brand(string name, double size, FontWeight weight)
        {
            var t = T("", size, Label, weight);
            t.Inlines.Add(new System.Windows.Documents.Run(name.ToLowerInvariant()));
            t.Inlines.Add(new System.Windows.Documents.Run(".txt") { Foreground = Label2 });
            t.Inlines.Add(new System.Windows.Documents.Run("™") { Foreground = Label3, FontSize = Math.Max(8, size * 0.5), FontWeight = FontWeights.Normal, BaselineAlignment = BaselineAlignment.TextTop });
            return t;
        }

        // Стекло: блок (углы 18) или строка списка (углы 14)
        public static Border Block(UIElement child, bool card)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(Tokens.Radius.Block), Background = card ? CardFill : GlassFill, BorderBrush = Rim, BorderThickness = new Thickness(1),
                Padding = new Thickness(18), Child = child, SnapsToDevicePixels = true
            };
        }

        public static Border Row(UIElement child)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(Tokens.Radius.Row), Background = CardFill, BorderBrush = Rim, BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12), Child = child, SnapsToDevicePixels = true
            };
        }

        // Плитка значка: пиксельный значок (или что угодно) на стекле
        public static Border Tile(UIElement child)
        {
            return new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(Tokens.Radius.Row), Background = Fill,
                BorderBrush = Solid(Alpha(Colors.White, 0.2)), BorderThickness = new Thickness(1), Child = child
            };
        }

        // Пиксельный значок в общем стиле: одно оранжевое тело и тёмный глаз, без фона.
        // rows — строки из «.» (пусто), «#» (тело) и «E» (глаз), как в icons/sprites.json.
        public static FrameworkElement Sprite(string[] rows, double box)
        {
            var body = new System.Text.StringBuilder("F1");
            var eye = new System.Text.StringBuilder("F1");
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; )
                {
                    char c = rows[y][x];
                    int end = x;
                    while (end < rows[y].Length && rows[y][end] == c) end++;
                    if (c != '.') (c == 'E' ? eye : body).Append(" M" + x + "," + y + "h" + (end - x) + "v1h-" + (end - x) + "z");
                    x = end;
                }
            var canvas = new Canvas { Width = rows[0].Length, Height = rows.Length };
            canvas.Children.Add(new Path { Data = Geometry.Parse(body.ToString()), Fill = SpriteBody });
            canvas.Children.Add(new Path { Data = Geometry.Parse(eye.ToString()), Fill = SpriteEye });
            return new Viewbox { Width = box, Height = box, Child = canvas, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        }

        // Фон окна. Под Windows 11 его заменяет системный Mica (см. Native) — этот слой тогда прячется;
        // в Windows 10 и на снимках остаётся ровный цвет темы. Кладётся первым в корень окна.
        public static Canvas Backdrop() { return Backdrop(0, 0); }
        public static Canvas Backdrop(double width, double height) { return new Canvas { IsHitTestVisible = false, Background = Bg, Tag = "dt-backdrop" }; }

        // Подпись dev.txt поверх Mica: тёплое оранжевое свечение из левого верхнего угла — одно во всех окнах экосистемы
        public static Canvas Signature()
        {
            var glow = new Canvas { IsHitTestVisible = false };
            var fill = new RadialGradientBrush();
            fill.GradientStops.Add(new GradientStop(Alpha(Tokens.Palette.Brand, 0.20), 0));
            fill.GradientStops.Add(new GradientStop(Alpha(Tokens.Palette.Brand, 0.07), 0.5));
            fill.GradientStops.Add(new GradientStop(Alpha(Tokens.Palette.Brand, 0), 1));
            fill.Freeze();
            var e = new Ellipse { Width = 1100, Height = 900, Fill = fill };
            Canvas.SetLeft(e, -520); Canvas.SetTop(e, -520);
            glow.Children.Add(e);
            return glow;
        }

        // Общие стили окна: подсказки и полосы прокрутки как в Windows 11. Вызывается один раз: Ui.Styles(app.Resources).
        //   • подсказка — тёмная панель с углами 4 и обводкой, длинный текст переносится;
        //   • полоса прокрутки — тонкая, без стрелок, видна только когда есть что листать.
        public static void Styles(ResourceDictionary res)
        {
            try
            {
                string ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";
                res[typeof(ToolTip)] = System.Windows.Markup.XamlReader.Parse(
                    "<Style " + ns + " TargetType='ToolTip'><Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='HasDropShadow' Value='False'/>" +
                    "<Setter Property='Foreground' Value='White'/><Setter Property='FontSize' Value='" + Tokens.Size.Caption + "'/><Setter Property='FontFamily' Value='" + Tokens.Font.Text + "'/>" +
                    "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'><Grid Margin='8,4,8,12'>" +
                    "<Border CornerRadius='" + Tokens.Radius.Tip + "' Background='#FF2C2C2C'><Border.Effect><DropShadowEffect BlurRadius='8' ShadowDepth='2' Direction='270' Opacity='0.3' Color='Black'/></Border.Effect></Border>" +
                    "<Border CornerRadius='" + Tokens.Radius.Tip + "' BorderThickness='1' BorderBrush='#18FFFFFF' Padding='9,5,9,6' MaxWidth='320'>" +
                    "<ContentPresenter><ContentPresenter.Resources><Style TargetType='TextBlock'><Setter Property='TextWrapping' Value='Wrap'/></Style></ContentPresenter.Resources></ContentPresenter>" +
                    "</Border></Grid></ControlTemplate></Setter.Value></Setter></Style>");
                string thumb = "<Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='b' CornerRadius='1.5' Background='#80909090' Margin='{0}'/>" +
                    "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#C0909090'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb>";
                res[typeof(System.Windows.Controls.Primitives.ScrollBar)] = System.Windows.Markup.XamlReader.Parse(
                    "<Style " + ns + " TargetType='ScrollBar'><Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='Background' Value='Transparent'/>" +
                    "<Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/>" +
                    "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True'><Track.Thumb>" +
                    string.Format(thumb, "4,2,3,2") + "</Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter>" +
                    "<Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/><Setter Property='Height' Value='10'/><Setter Property='MinHeight' Value='10'/>" +
                    "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'><Track x:Name='PART_Track'><Track.Thumb>" +
                    string.Format(thumb, "2,4,2,3") + "</Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter></Trigger></Style.Triggers></Style>");
            }
            catch { }
        }

        // Значок из Segoe Fluent Icons — как в самой Windows 11
        public static TextBlock Glyph(string code)
        {
            return new TextBlock { Text = code, FontFamily = IconFont, FontSize = 16, Foreground = Label, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        // ───────── Окно как в Windows 11 ─────────
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        static int build = -1;
        static int Build
        {
            get
            {
                if (build >= 0) return build;
                try { using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) build = int.Parse((string)k.GetValue("CurrentBuildNumber")); }
                catch { build = 0; }
                return build;
            }
        }
        // Mica за всем окном — Windows 11 22H2 и новее
        public static bool MicaAvailable { get { return Build >= 22621; } }

        // Делает окно родным для Windows 11: системная рамка с тенью и скруглёнными углами, фон Mica,
        // тёмная или светлая рамка по теме, прилипание к краям экрана и (если resizable) изменение размера.
        // Шапку окно рисует само; перетаскивать — DragMove, кнопки — CaptionButtons.
        public static void Native(Window w, bool resizable)
        {
            Smooth();
            w.WindowStyle = WindowStyle.SingleBorderWindow;
            w.ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.CanMinimize;
            System.Windows.Shell.WindowChrome.SetWindowChrome(w, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(resizable ? 6 : 0), GlassFrameThickness = new Thickness(-1),
                UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0)
            });
            if (ThemeFollows) FollowSystemTheme();
            w.SourceInitialized += delegate
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                var src = HwndSource.FromHwnd(h);
                if (MicaAvailable && src != null && src.CompositionTarget != null)
                {
                    src.CompositionTarget.BackgroundColor = Colors.Transparent;
                    w.Background = Brushes.Transparent;
                    var root = w.Content as Panel;
                    if (root != null)
                    {
                        root.Background = Brushes.Transparent;
                        foreach (UIElement c in root.Children) { var fe = c as FrameworkElement; if (fe != null && fe.Tag as string == "dt-backdrop") fe.Visibility = Visibility.Collapsed; }
                    }
                }
                Frame(h);
                Action again = delegate { Frame(h); };
                ThemeChanged += again;
                w.Closed += delegate { ThemeChanged -= again; };
            };
            // Развёрнутое окно с такой рамкой заходит за край экрана на толщину рамки — отступаем
            w.StateChanged += delegate { var root = w.Content as FrameworkElement; if (root != null) root.Margin = w.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0); };
        }

        static void Frame(IntPtr h)
        {
            int dark = Light ? 0 : 1, round = 2, backdrop = 2;
            DwmSetWindowAttribute(h, 20, ref dark, 4);     // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(h, 33, ref round, 4);    // DWMWA_WINDOW_CORNER_PREFERENCE: скруглять
            if (MicaAvailable) DwmSetWindowAttribute(h, 38, ref backdrop, 4);   // DWMWA_SYSTEMBACKDROP_TYPE: Mica
        }

        // Кнопки окна Windows 11: «Свернуть», «Развернуть» (если окно меняет размер) и «Закрыть» — 46×32,
        // значки Segoe Fluent Icons; «Закрыть» под курсором красная. close — что делать вместо обычного закрытия.
        public static StackPanel CaptionButtons(Window w, Action close)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            Func<string, string, bool, Action, Border> make = delegate(string glyph, string tip, bool danger, Action click)
            {
                var t = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 10, Foreground = Label, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Width = 46, Height = 32, Background = Brushes.Transparent, Child = t, ToolTip = tip };
                b.MouseEnter += delegate { b.Background = danger ? Solid(Tokens.Palette.Red) : Fill2; if (danger) t.Foreground = Brushes.White; };
                b.MouseLeave += delegate { b.Background = Brushes.Transparent; t.Foreground = Label; };
                b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; if (!danger) b.Background = Press; };
                b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
                return b;
            };
            row.Children.Add(make("\uE921", "Свернуть", false, delegate { w.WindowState = WindowState.Minimized; }));
            if (w.ResizeMode == ResizeMode.CanResize)
            {
                Border max = null;
                max = make("\uE922", "Развернуть", false, delegate { w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; });
                w.StateChanged += delegate { ((TextBlock)max.Child).Text = w.WindowState == WindowState.Maximized ? "\uE923" : "\uE922"; max.ToolTip = w.WindowState == WindowState.Maximized ? "Свернуть в окно" : "Развернуть"; };
                row.Children.Add(max);
            }
            row.Children.Add(make("\uE8BB", "Закрыть", true, close ?? new Action(w.Close)));
            return row;
        }
    }

    // Кнопка Windows 11: углы 4, высота 32. Обычная — поле с тонкой обводкой; главная — залита цветом акцента,
    // текст на ней тёмный или белый по яркости цвета. Под курсором светлеет, при нажатии тускнеет — без «прыжков».
    class Btn
    {
        public readonly Border Root;
        readonly TextBlock text;
        Brush idle, hover, press;
        Action click;
        bool pressed;

        public Btn()
        {
            text = new TextBlock { FontSize = Tokens.Size.Body, FontFamily = Ui.TextFont, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Root = new Border
            {
                Height = 32, MinWidth = 96, CornerRadius = new CornerRadius(Tokens.Radius.Control), Padding = new Thickness(12, 0, 12, 1), BorderThickness = new Thickness(1),
                Child = text, SnapsToDevicePixels = true
            };
            Ui.Keyboard(Root, delegate { if (click != null && Root.IsHitTestVisible) click(); });
            Root.MouseEnter += delegate { Root.Background = hover; };
            Root.MouseLeave += delegate { Root.Background = idle; Press(false); };
            Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Press(true); };
            Root.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                bool was = pressed;
                Press(false);
                if (was && click != null) click();
            };
        }

        void Press(bool on)
        {
            if (pressed == on) return;
            pressed = on;
            Root.Background = on ? press : Root.IsMouseOver ? hover : idle;
            text.Opacity = on ? 0.786 : 1;   // нажатая кнопка Windows: текст приглушается
        }

        // tint == null — обычная; иначе залита этим цветом
        public void Set(string label, Color? tint, bool enabled, Action onClick)
        {
            Motion.Swap(text, label);
            click = onClick;
            if (tint.HasValue)
            {
                idle = Ui.Solid(tint.Value);
                hover = Ui.Solid(Ui.Alpha(tint.Value, 0.9));
                press = Ui.Solid(Ui.Alpha(tint.Value, 0.8));
                Root.BorderBrush = Ui.Solid(Ui.Alpha(Colors.White, 0.08));
                text.Foreground = Ui.Solid(Ui.OnColor(tint.Value));
            }
            else
            {
                idle = Ui.GlassFill; hover = Ui.GlassHover; press = Ui.Press;
                Root.BorderBrush = Ui.Rim;
                text.Foreground = Ui.Label;
            }
            Root.Background = Root.IsMouseOver ? hover : idle;
            Root.IsHitTestVisible = enabled;
            Root.Focusable = enabled;
            Root.Opacity = enabled ? 1 : 0.5;
            Root.Visibility = Visibility.Visible;
        }

        public void Hide() { Root.Visibility = Visibility.Collapsed; }
    }

    // Переключатель Windows 11: 40×20. Выключен — пустая дорожка с обводкой и серым бегунком;
    // включён — дорожка цвета акцента. Под курсором бегунок чуть крупнее. Пробел и Enter тоже переключают.
    class Switch
    {
        public readonly Border Root;
        readonly Border knob;
        readonly TranslateTransform knobX = new TranslateTransform();
        readonly ScaleTransform knobScale = new ScaleTransform(1, 1);
        readonly Action<bool> changed;
        bool on;
        const double W = 40, H = 20, Knob = 12;

        public Switch(bool state, Action<bool> changed)
        {
            on = state;
            this.changed = changed;
            var move = new TransformGroup();
            move.Children.Add(knobScale);
            move.Children.Add(knobX);
            knob = new Border
            {
                Width = Knob, Height = Knob, CornerRadius = new CornerRadius(Knob / 2), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), RenderTransform = move, RenderTransformOrigin = new Point(0.5, 0.5)
            };
            Root = new Border { Width = W, Height = H, CornerRadius = new CornerRadius(H / 2), BorderThickness = new Thickness(1), Child = knob, SnapsToDevicePixels = true };
            knobX.X = on ? Travel : 0;
            Paint();
            Action toggle = delegate { Value = !on; if (this.changed != null) this.changed(on); };
            Ui.Keyboard(Root, toggle);
            Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; toggle(); };
            Root.MouseEnter += delegate { Grow(1.17); };
            Root.MouseLeave += delegate { Grow(1); };
        }

        static double Travel { get { return W - 2 - 6 - Knob; } }

        void Paint()
        {
            Root.Background = on ? (Brush)Ui.AccentBrush : Ui.Fill;
            Root.BorderBrush = on ? (Brush)Ui.AccentBrush : Ui.Label2;
            knob.Background = on ? (Brush)Ui.AccentText : Ui.Label2;
        }

        void Grow(double k)
        {
            if (!Ui.Live(Root)) { knobScale.ScaleX = knobScale.ScaleY = k; return; }
            var a = new DoubleAnimation(k, TimeSpan.FromMilliseconds(Tokens.Motion.FastMs)) { EasingFunction = Ui.EaseOut };
            knobScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            knobScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        public bool Value
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Paint();
                double to = on ? Travel : 0;
                if (!Ui.Live(Root)) { knobX.BeginAnimation(TranslateTransform.XProperty, null); knobX.X = to; return; }
                knobX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(Tokens.Motion.FastMs)) { EasingFunction = Ui.EaseOut });
            }
        }
    }

    // Поле ввода Windows 11 (поиск): высота 32, углы 4, значок лупы, подсказка в пустом поле, крестик для очистки;
    // в фокусе снизу полоска цвета акцента. Changed приходит не на каждую букву, а когда человек остановился (120 мс).
    // Esc очищает поле.
    class Field
    {
        public readonly Border Root;
        public readonly TextBox Box;
        public Action<string> Changed;
        readonly System.Windows.Threading.DispatcherTimer wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };

        public Field(string hint)
        {
            Box = new TextBox { Background = Brushes.Transparent, Foreground = Ui.Label, CaretBrush = Ui.Label, BorderThickness = new Thickness(0), FontSize = Tokens.Size.Body, FontFamily = Ui.TextFont, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(32, 0, 30, 1) };
            var icon = Ui.Glyph("\uE721");
            icon.FontSize = Tokens.Size.Caption; icon.Foreground = Ui.Label2; icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(11, 0, 0, 0); icon.IsHitTestVisible = false;
            var ghost = Ui.T(hint, Tokens.Size.Body, Ui.Label3, FontWeights.Normal);
            ghost.Margin = new Thickness(34, 0, 0, 1); ghost.VerticalAlignment = VerticalAlignment.Center; ghost.IsHitTestVisible = false;
            var clear = Ui.Glyph("\uE711");
            clear.FontSize = Tokens.Size.Micro; clear.Foreground = Ui.Label2; clear.HorizontalAlignment = HorizontalAlignment.Right; clear.Margin = new Thickness(0, 0, 4, 0);
            clear.Width = 24; clear.Height = 24; clear.Padding = new Thickness(0, 6, 0, 0); clear.Background = Brushes.Transparent; clear.Visibility = Visibility.Collapsed; clear.ToolTip = "Очистить";
            var line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Background = Ui.AccentBrush, CornerRadius = new CornerRadius(0, 0, Tokens.Radius.Control, Tokens.Radius.Control), Visibility = Visibility.Hidden, IsHitTestVisible = false };
            Root = new Border { Height = 32, CornerRadius = new CornerRadius(Tokens.Radius.Control), Background = Ui.GlassFill, BorderBrush = Ui.Rim, BorderThickness = new Thickness(1), Child = new Grid { Children = { Box, icon, ghost, clear, line } }, ClipToBounds = true };
            wait.Tick += delegate { wait.Stop(); if (Changed != null) Changed(Box.Text); };
            Box.TextChanged += delegate
            {
                ghost.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                clear.Visibility = Box.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                wait.Stop(); wait.Start();
            };
            Box.GotKeyboardFocus += delegate { line.Visibility = Visibility.Visible; };
            Box.LostKeyboardFocus += delegate { line.Visibility = Visibility.Hidden; };
            Box.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape && Box.Text.Length > 0) { e.Handled = true; Box.Text = ""; } };
            clear.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Box.Text = ""; Box.Focus(); };
        }

        public string Text { get { return Box.Text; } set { Box.Text = value ?? ""; } }
    }

    // Полоса хода: тонкая дорожка и заливка цветом продукта
    class Progress
    {
        public readonly Border Root;
        readonly Border fill;
        readonly TranslateTransform slide = new TranslateTransform();
        double value;
        bool busy;

        public Progress(Color tint)
        {
            // Как у Windows 11: дорожка в 1 точку и полоса в 3 точки цвета акцента (tint оставлен для совместимости)
            fill = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, RenderTransform = slide };
            var track = new Border { Height = 1, Background = Ui.Label3, VerticalAlignment = VerticalAlignment.Center };
            Root = new Border { Height = 3, Background = Brushes.Transparent, Child = new Grid { Children = { track, fill } }, ClipToBounds = true };
            Root.SizeChanged += delegate { if (busy) Run(); else Apply(false); };
        }

        // Заливка доезжает до нового значения плавно; назад (новая загрузка) — сразу
        void Apply(bool glide)
        {
            double to = Root.ActualWidth * value;
            if (!glide || !Ui.Live(Root) || to < fill.Width)
            {
                fill.BeginAnimation(FrameworkElement.WidthProperty, null);
                fill.Width = to;
                return;
            }
            fill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(Tokens.Motion.BaseMs)) { EasingFunction = Ui.EaseOut });
        }

        void Run()
        {
            double w = Root.ActualWidth;
            fill.BeginAnimation(FrameworkElement.WidthProperty, null);
            if (!Ui.Live(Root) || w <= 0) { slide.BeginAnimation(TranslateTransform.XProperty, null); slide.X = 0; fill.Width = w; return; }
            fill.Width = w * 0.3;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-w * 0.3, w, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }

        public double Value
        {
            get { return value; }
            set { this.value = Math.Max(0, Math.Min(1, value)); if (!busy) Apply(true); }
        }

        // Сколько осталось — неизвестно (распаковка, установщик): по дорожке бегает полоска
        public bool Busy
        {
            get { return busy; }
            set
            {
                if (busy == value) return;
                busy = value;
                if (busy) { Run(); return; }
                slide.BeginAnimation(TranslateTransform.XProperty, null);
                slide.X = 0;
                Apply(false);
            }
        }
    }

    // Состояние: цветная точка и подпись
    class Status
    {
        public readonly DockPanel Root = new DockPanel();
        readonly Ellipse dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly SolidColorBrush paint = new SolidColorBrush(Tokens.Dark.Label3);
        bool busy;
        readonly TextBlock text = Ui.T("", Tokens.Size.Caption, Ui.Label2, FontWeights.Normal);
        public enum Kind { None, Ok, Warn, Bad }

        public Status()
        {
            dot.Fill = paint;
            DockPanel.SetDock(dot, System.Windows.Controls.Dock.Left);   // полное имя: у приложения может быть своё пространство имён «Dock»
            Root.Children.Add(dot);
            Root.Children.Add(text);
        }

        public void Set(string label, Kind kind) { Set(label, kind == Kind.Ok ? Tokens.Palette.Green : kind == Kind.Warn ? Tokens.Palette.Orange : kind == Kind.Bad ? Tokens.Palette.Red : Tokens.Dark.Label3); }
        public void Set(string label, Color color)
        {
            Motion.Swap(text, label);
            if (Ui.Live(dot)) paint.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(Tokens.Motion.BaseMs)));
            else { paint.BeginAnimation(SolidColorBrush.ColorProperty, null); paint.Color = color; }
        }

        // Идёт работа — точка мерцает
        public bool Busy
        {
            get { return busy; }
            set { if (busy == value) return; busy = value; Motion.Blink(dot, value); }
        }
    }

    // Вкладки или режимы — как SelectorBar в Windows 11: подписи без рамок, под выбранной — короткая
    // полоска цвета акцента, которая переезжает при смене. Стрелки влево и вправо тоже переключают.
    class Seg
    {
        public readonly Border Root;
        readonly System.Collections.Generic.List<Border> items = new System.Collections.Generic.List<Border>();
        readonly Action<int> changed;
        readonly Border pill;
        readonly TranslateTransform pillX = new TranslateTransform();
        readonly StackPanel strip;
        int index;
        const double PillW = 16;

        public Seg(string[] labels, int selected, Action<int> changed)
        {
            this.changed = changed;
            index = selected;
            strip = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < labels.Length; i++)
            {
                int n = i;
                var t = new TextBlock { Text = labels[i], FontSize = Tokens.Size.Body, FontFamily = Ui.TextFont, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var b = new Border { Height = 34, MinWidth = 44, CornerRadius = new CornerRadius(Tokens.Radius.Control), Padding = new Thickness(12, 0, 12, 2), Child = t, Background = Brushes.Transparent, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0) };
                b.MouseEnter += delegate { b.Background = Ui.Fill; };
                b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
                b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; b.Background = Ui.Press; Pick(n); };
                b.MouseLeftButtonUp += delegate { b.Background = b.IsMouseOver ? Ui.Fill : Brushes.Transparent; };
                items.Add(b);
                strip.Children.Add(b);
            }
            pill = new Border { Width = PillW, Height = 3, CornerRadius = new CornerRadius(1.5), Background = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, RenderTransform = pillX, IsHitTestVisible = false, Margin = new Thickness(0, 0, 0, 2) };
            Root = new Border { Background = Brushes.Transparent, Child = new Grid { Children = { strip, pill } }, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true };
            Root.Focusable = true;
            Root.FocusVisualStyle = Ui.Focus;
            Root.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Left && index > 0) { e.Handled = true; Pick(index - 1); }
                else if (e.Key == Key.Right && index < items.Count - 1) { e.Handled = true; Pick(index + 1); }
            };
            strip.SizeChanged += delegate { Place(false); };
            Paint();
        }

        void Pick(int n)
        {
            if (index == n) return;
            Index = n;
            if (changed != null) changed(n);
        }

        public int Index
        {
            get { return index; }
            set { if (index == value) return; index = value; Paint(); Place(true); }
        }

        void Place(bool glide)
        {
            if (index < 0 || index >= items.Count || items[index].ActualWidth <= 0) return;
            double x = items[index].TranslatePoint(new Point(0, 0), strip).X + (items[index].ActualWidth - PillW) / 2;
            if (!glide || !Ui.Live(Root)) { pillX.BeginAnimation(TranslateTransform.XProperty, null); pillX.X = x; return; }
            pillX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(Tokens.Motion.BaseMs)) { EasingFunction = Ui.EaseOut });
        }

        void Paint()
        {
            for (int i = 0; i < items.Count; i++)
            {
                var t = (TextBlock)items[i].Child;
                t.Foreground = i == index ? Ui.Label : Ui.Label2;
            }
        }
    }

    // Ползунок: тонкая дорожка, заливка цветом и белый бегунок. О новом значении сообщает, когда бегунок отпустили.
    class Slider
    {
        public readonly Grid Root;
        readonly Border fill, knob;
        readonly TranslateTransform knobX = new TranslateTransform();
        readonly double min, max, step;
        readonly Action<double> changed;
        double value;
        const double W = 168, Knob = 20;

        public Slider(double min, double max, double step, double value, Color tint, Action<double> changed)
        {
            this.min = min; this.max = Math.Max(max, min + 1e-9); this.step = step; this.changed = changed;
            // Как у Windows 11: дорожка 4, заливка цветом акцента (tint оставлен для совместимости), бегунок — диск с точкой акцента
            fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Knob / 2, 0, 0, 0) };
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Label3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Knob / 2, 0, Knob / 2, 0) };
            var dot = new Ellipse { Width = 12, Height = 12, Fill = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            knob = new Border
            {
                Width = Knob, Height = Knob, CornerRadius = new CornerRadius(Knob / 2), Background = Ui.Thumb, BorderBrush = Ui.Rim, BorderThickness = new Thickness(1), Child = dot,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RenderTransform = knobX
            };
            knob.MouseEnter += delegate { dot.Width = dot.Height = 14; };
            knob.MouseLeave += delegate { dot.Width = dot.Height = 12; };
            Root = new Grid { Width = W, Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { track, fill, knob } };
            Root.Focusable = true;
            Root.FocusVisualStyle = Ui.Focus;
            // С клавиатуры: стрелки — на шаг, Home и End — в края; о новом значении сообщаем сразу
            Root.KeyDown += delegate(object s, KeyEventArgs e)
            {
                double d = this.step > 0 ? this.step : (this.max - this.min) / 20, to = this.value;
                if (e.Key == Key.Left || e.Key == Key.Down) to -= d;
                else if (e.Key == Key.Right || e.Key == Key.Up) to += d;
                else if (e.Key == Key.Home) to = this.min;
                else if (e.Key == Key.End) to = this.max;
                else return;
                e.Handled = true;
                double was = this.value;
                Value = to;
                if (this.value != was && this.changed != null) this.changed(this.value);
            };
            Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Root.Focus(); Root.CaptureMouse(); Move(e.GetPosition(Root).X); };
            Root.MouseMove += delegate(object s, MouseEventArgs e) { if (Root.IsMouseCaptured) Move(e.GetPosition(Root).X); };
            Root.MouseLeftButtonUp += delegate
            {
                if (!Root.IsMouseCaptured) return;
                Root.ReleaseMouseCapture();
                if (this.changed != null) this.changed(this.value);
            };
            Value = value;
        }

        void Move(double x)
        {
            double v = min + (max - min) * Math.Max(0, Math.Min(1, (x - Knob / 2) / (W - Knob)));
            if (step > 0) v = min + Math.Round((v - min) / step) * step;
            Value = v;
        }

        public double Value
        {
            get { return value; }
            set
            {
                this.value = Math.Max(min, Math.Min(max, value));
                double x = (this.value - min) / (max - min) * (W - Knob);
                knobX.X = x;
                fill.Width = x;
            }
        }
    }

    // Каркас окна любого приложения dev.txt — один для всех, чтобы они читались как одна семья.
    // Как «Параметры» Windows 11: слева значок и имя продукта, под ними разделы; справа заголовок раздела,
    // содержимое и нижняя строка. Поверх Mica — общая подпись dev.txt: тёплое свечение в левом верхнем углу.
    //   var shell = new NavShell(window, Sprite, "core", "версия 0.3.0", Close);
    //   int apps = shell.Add("Приложения", Ui.Glyph(""));   shell.Changed = delegate(int i) { … };
    //   shell.Body.Children.Add(page);   Content = shell.Root;
    class NavShell
    {
        public const double PaneW = 260;
        public readonly Grid Root;
        public readonly Grid Body = new Grid();                       // страницы — сюда
        public readonly DockPanel Bottom = new DockPanel { LastChildFill = true, Height = 64, Margin = new Thickness(32, 0, 32, 4) };   // строка состояния и кнопки
        public readonly TextBlock Status = Ui.T("", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);   // внизу слева, под разделами
        public Action<int> Changed;
        readonly FrameworkElement pane;
        readonly ColumnDefinition paneCol = new ColumnDefinition { Width = new GridLength(PaneW) };
        readonly StackPanel nav = new StackPanel();
        readonly System.Collections.Generic.List<Border> items = new System.Collections.Generic.List<Border>();
        readonly System.Collections.Generic.List<Border> pills = new System.Collections.Generic.List<Border>();
        readonly TextBlock title = Ui.T("", Tokens.Size.Display, Ui.Label, FontWeights.SemiBold), subtitle = Ui.T("", Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
        readonly FrameworkElement titleBox;
        int selected = -1;

        public NavShell(Window w, string[] sprite, string name, string version, Action close)
        {
            Root = new Grid { Background = Ui.Bg, ClipToBounds = true };
            Root.Children.Add(Ui.Backdrop());
            Root.Children.Add(Ui.Signature());
            var main = new Grid();
            main.ColumnDefinitions.Add(paneCol);
            main.ColumnDefinitions.Add(new ColumnDefinition());
            Root.Children.Add(main);
            MouseButtonEventHandler drag = delegate(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed && e.ClickCount == 1) try { w.DragMove(); } catch { } };

            // Слева: значок и имя продукта, версия, разделы, строка состояния
            var mark = Ui.Sprite(sprite, 30);
            mark.VerticalAlignment = VerticalAlignment.Center;
            var brand = Ui.Brand(name, Tokens.Size.Title, FontWeights.SemiBold);
            brand.Margin = new Thickness(12, 0, 0, 1); brand.VerticalAlignment = VerticalAlignment.Center;
            var ver = Ui.T(version ?? "", Tokens.Size.Caption, Ui.Label3, FontWeights.Normal);
            ver.Margin = new Thickness(42, 2, 0, 0);
            var head = new StackPanel { Margin = new Thickness(20, 22, 16, 18), Background = Brushes.Transparent, Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { mark, brand } }, ver } };
            head.MouseLeftButtonDown += drag;
            Status.TextWrapping = TextWrapping.Wrap; Status.Margin = new Thickness(20, 8, 20, 18);
            var navView = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav, Focusable = false, Padding = new Thickness(8, 0, 8, 0) };
            var left = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(head, Dock.Top); left.Children.Add(head);
            DockPanel.SetDock(Status, Dock.Bottom); left.Children.Add(Status);
            left.Children.Add(navView);
            pane = left;
            main.Children.Add(left);

            // Справа: заголовок раздела (за него окно перетаскивается), кнопки окна, содержимое, нижняя строка
            var top = new Grid { Background = Brushes.Transparent, Height = 104 };
            top.MouseLeftButtonDown += drag;
            subtitle.Margin = new Thickness(0, 2, 0, 0);
            titleBox = new StackPanel { Margin = new Thickness(32, 34, 160, 0), Children = { title, subtitle } };
            top.Children.Add(titleBox);
            top.Children.Add(Ui.CaptionButtons(w, close));
            var right = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(top, Dock.Top); right.Children.Add(top);
            DockPanel.SetDock(Bottom, Dock.Bottom); right.Children.Add(Bottom);
            right.Children.Add(Body);
            Grid.SetColumn(right, 1);
            main.Children.Add(right);
        }

        // Заголовок группы разделов («Настройки», «Приложения»)
        public void Header(string text)
        {
            var t = Ui.T(text, Tokens.Size.Caption, Ui.Label3, FontWeights.SemiBold);
            t.Margin = new Thickness(12, nav.Children.Count == 0 ? 4 : 18, 0, 6);
            nav.Children.Add(t);
        }

        // Раздел: значок (пиксельный или Segoe Fluent Icons) и подпись. Возвращает номер раздела.
        public int Add(string label, FrameworkElement icon)
        {
            int n = items.Count;
            var pill = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), Background = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden };
            var text = Ui.T(label, Tokens.Size.Body, Ui.Label, FontWeights.Normal);
            text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new Thickness(14, 0, 0, 1);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            if (icon != null) { icon.Width = 20; icon.Height = 20; icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon); }
            else text.Margin = new Thickness(0, 0, 0, 1);
            row.Children.Add(text);
            var b = new Border { Height = 38, CornerRadius = new CornerRadius(Tokens.Radius.Control), Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Child = new Grid { Children = { row, pill } } };
            b.MouseEnter += delegate { b.Background = n == selected ? Ui.Fill2 : Ui.Fill; };
            b.MouseLeave += delegate { b.Background = n == selected ? Ui.Fill : Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate { b.Background = Ui.Press; };   // нажатие видно сразу, как в навигации Windows
            b.MouseLeftButtonUp += delegate { Pick(n); b.Background = n == selected ? Ui.Fill : Brushes.Transparent; };
            Ui.Keyboard(b, delegate { Pick(n); });
            items.Add(b); pills.Add(pill);
            nav.Children.Add(b);
            return n;
        }

        public void Clear() { nav.Children.Clear(); items.Clear(); pills.Clear(); selected = -1; }

        void Pick(int n)
        {
            if (n == selected) return;
            Selected = n;
            if (Changed != null) Changed(n);
        }

        // Выбранный раздел: заливка и полоска цвета акцента слева, как в навигации Windows 11
        public int Selected
        {
            get { return selected; }
            set
            {
                selected = value;
                for (int i = 0; i < items.Count; i++)
                {
                    bool on = i == selected;
                    items[i].Background = on ? (items[i].IsMouseOver ? Ui.Fill2 : Ui.Fill) : (items[i].IsMouseOver ? Ui.Fill : Brushes.Transparent);
                    bool was = pills[i].Visibility == Visibility.Visible;
                    pills[i].Visibility = on ? Visibility.Visible : Visibility.Hidden;
                    if (on && !was) Motion.Pop(pills[i]);
                }
            }
        }

        public void Title(string text, string sub)
        {
            Motion.Swap(title, text ?? "");
            Motion.Swap(subtitle, sub ?? "");
            subtitle.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;
        }

        // Без левой панели и заголовка — например, установщик на первом запуске
        public void Chrome(bool on)
        {
            paneCol.Width = new GridLength(on ? PaneW : 0);
            pane.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            titleBox.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            Bottom.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Меню dev.txt — одно для значка в трее и для правого щелчка во всех приложениях: панель Windows 11 (углы 8,
    // строки 32, подсветка с углами 4, галочка слева), со значком и именем продукта в шапке. Закрывается щелчком
    // мимо и клавишей Esc.
    class PopMenu : Window
    {
        const double Shadow = 18, RowH = 32;
        readonly StackPanel list = new StackPanel();
        readonly Border box;
        bool closing;

        public PopMenu()
        {
            Title = "dev.txt";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = Ui.TextFont;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            box = new Border
            {
                Margin = new Thickness(Shadow), MinWidth = 196, CornerRadius = new CornerRadius(Tokens.Radius.Block), Background = Ui.Solid(Tokens.Dark.Menu),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = Shadow, ShadowDepth = 4, Direction = 270, Opacity = 0.4, Color = Colors.Black },
                Child = new Border { CornerRadius = new CornerRadius(Tokens.Radius.Block), Background = Brushes.Transparent, BorderBrush = Ui.Rim, BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = list }
            };
            Content = box;
            Deactivated += delegate { Shut(); };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Shut(); return; }
                if (rows.Count == 0) return;
                if (e.Key == Key.Down || e.Key == Key.Tab) { e.Handled = true; Mark((marked + 1) % rows.Count); }
                else if (e.Key == Key.Up) { e.Handled = true; Mark((marked + rows.Count - 1) % rows.Count); }
                else if (e.Key == Key.Home) { e.Handled = true; Mark(0); }
                else if (e.Key == Key.End) { e.Handled = true; Mark(rows.Count - 1); }
                else if ((e.Key == Key.Enter || e.Key == Key.Space) && marked >= 0) { e.Handled = true; Action a = acts[marked]; Shut(); a(); }
            };
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { closing = true; };
        }

        // Доступные строки и их действия — для стрелок и Enter
        readonly System.Collections.Generic.List<Border> rows = new System.Collections.Generic.List<Border>();
        readonly System.Collections.Generic.List<Action> acts = new System.Collections.Generic.List<Action>();
        int marked = -1;

        void Mark(int n)
        {
            if (marked >= 0 && marked < rows.Count) rows[marked].Background = Brushes.Transparent;
            marked = n;
            if (marked >= 0 && marked < rows.Count) rows[marked].Background = Ui.Fill;
        }

        // Закрытие: меню быстро гаснет (83 мс, как у Windows) и только потом исчезает
        void Shut()
        {
            if (closing) return;
            closing = true;
            if (!Ui.Live(box)) { Close(); return; }
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(Tokens.Motion.PressMs));
            fade.Completed += delegate { try { Close(); } catch { } };
            box.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // Заголовок: название приложения или что угодно ещё
        public void Header(UIElement what)
        {
            list.Children.Add(new Border { Padding = new Thickness(10, 5, 10, 6), Child = what });
        }

        public void Line()
        {
            list.Children.Add(new Border { Height = 1, Margin = new Thickness(-4, 4, -4, 4), Background = Ui.Separator });
        }

        // Строка меню. check — показать галочку справа; action == null — строка недоступна.
        public void Item(string label, bool check, Action action)
        {
            bool enabled = action != null;
            var text = Ui.T(label, Tokens.Size.Body, enabled ? Ui.Label : Ui.Label3, FontWeights.Normal);
            text.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel { LastChildFill = true };
            // галочка — слева, как в меню Windows; место под неё есть у всех строк, чтобы подписи стояли ровно
            var mark = new TextBlock { Text = check ? "\uE73E" : "", FontFamily = Ui.IconFont, FontSize = Tokens.Size.Caption, Foreground = Ui.Label, VerticalAlignment = VerticalAlignment.Center, Width = 28 };
            DockPanel.SetDock(mark, System.Windows.Controls.Dock.Left);
            row.Children.Add(mark);
            row.Children.Add(text);
            var b = new Border { Height = RowH, CornerRadius = new CornerRadius(Tokens.Radius.Tip), Padding = new Thickness(10, 0, 10, 1), Background = Brushes.Transparent, Child = row };
            if (enabled)
            {
                int slot = rows.Count;
                rows.Add(b); acts.Add(action);
                b.MouseEnter += delegate { Mark(slot); };   // курсор и стрелки двигают одну и ту же подсветку
                b.MouseLeave += delegate { if (marked == slot) Mark(-1); };
                b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    Shut();
                    action();
                };
            }
            list.Children.Add(b);
        }

        // Показывает меню над точкой (cx, bottom) — в единицах WPF, в координатах экрана; за края рабочей области не вылезает
        public void ShowAbove(double cx, double bottom, Rect work)
        {
            box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = box.DesiredSize.Width, h = box.DesiredSize.Height;
            Left = Math.Max(work.Left - Shadow + 4, Math.Min(work.Right + Shadow - 4 - w, cx - w / 2));
            Top = Math.Max(work.Top, bottom - h + Shadow);
            Show();
            Motion.GrowFrom(box, new Point(0.5, 1));   // меню вырастает из того, по чему щёлкнули
            Activate();
        }

        // Шапка меню: значок и имя продукта — одна у всех приложений dev.txt
        public void Brand(string[] sprite, string name)
        {
            var mark = Ui.Sprite(sprite, 18);
            var brand = Ui.Brand(name, Tokens.Size.Body, FontWeights.SemiBold);
            brand.Margin = new Thickness(8, 0, 0, 1); brand.VerticalAlignment = VerticalAlignment.Center;
            Header(new StackPanel { Orientation = Orientation.Horizontal, Children = { mark, brand } });
            Line();
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

        // Показывает меню у курсора — для значка в трее: над курсором, не вылезая за края экрана
        public void ShowAtCursor()
        {
            POINT p;
            GetCursorPos(out p);
            IntPtr dc = GetDC(IntPtr.Zero);
            double k = GetDeviceCaps(dc, 88) / 96.0;   // LOGPIXELSX
            ReleaseDC(IntPtr.Zero, dc);
            if (k <= 0) k = 1;
            double x = p.X / k, y = p.Y / k;
            box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = box.DesiredSize.Width, h = box.DesiredSize.Height;
            double left = SystemParameters.VirtualScreenLeft, top = SystemParameters.VirtualScreenTop;
            double right = left + SystemParameters.VirtualScreenWidth;
            Left = Math.Max(left - Shadow + 4, Math.Min(right + Shadow - 4 - w, x - w + Shadow + 8));
            Top = y - h + Shadow - 4 >= top ? y - h + Shadow - 4 : y - Shadow + 4;
            Show();
            Motion.GrowFrom(box, new Point(1, 1));   // вырастает от курсора
            Activate();
            try { SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle); } catch { }   // иначе меню трея не закроется щелчком мимо
        }
    }

    // Меню Windows 11 — настоящее системное: скруглённое, на матовом стекле, светлое или тёмное по теме.
    // Для значка в трее и любых других контекстных меню вместо меню WinForms (те выглядят как в Windows 7).
    static class NativeMenu
    {
        public class Item
        {
            public string Label; public Action Act; public bool Check, Line, Disabled;
            public Item(string label, Action act) { Label = label; Act = act; }
            public static Item Separator { get { return new Item(null, null) { Line = true }; } }
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        // Тёмные системные меню: так делают сами приложения Windows (Блокнот, Проводник); функция есть с Windows 10 1903
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();

        static HwndSource owner;   // невидимое окно-хозяин: без него меню не закрывается щелчком мимо

        public static void Show(params Item[] items)
        {
            if (owner == null) owner = new HwndSource(new HwndSourceParameters("dev.txt menu") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80 });
            try { SetPreferredAppMode(Ui.Light ? 3 : 2); FlushMenuThemes(); } catch { }   // 2 — всегда тёмные, 3 — всегда светлые
            IntPtr m = CreatePopupMenu();
            try
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].Line) { AppendMenu(m, 0x800, UIntPtr.Zero, null); continue; }   // MF_SEPARATOR
                    uint flags = (items[i].Check ? 0x8u : 0) | (items[i].Disabled ? 0x1u : 0);   // MF_CHECKED, MF_GRAYED
                    AppendMenu(m, flags, new UIntPtr((uint)(i + 1)), items[i].Label);
                }
                POINT p;
                GetCursorPos(out p);
                SetForegroundWindow(owner.Handle);
                int cmd = TrackPopupMenuEx(m, 0x100 | 0x2 | 0x20, p.X, p.Y, owner.Handle, IntPtr.Zero);   // RETURNCMD, RIGHTBUTTON, BOTTOMALIGN
                PostMessage(owner.Handle, 0, IntPtr.Zero, IntPtr.Zero);   // WM_NULL: так меню трея закрывается правильно
                if (cmd > 0 && cmd <= items.Length && items[cmd - 1].Act != null) items[cmd - 1].Act();
            }
            finally { DestroyMenu(m); }
        }
    }

    // Движение — общие приёмы; те же, что в ui/kit.css. При «Меньше движения» всё сразу встаёт на место.
    static class Motion
    {
        static IEasingFunction Out() { return Ui.EaseOut; }

        // Появление по очереди: дети поднимаются и проявляются волной, сосед за соседом
        public static void Stagger(Panel panel)
        {
            if (!Ui.Live(panel)) return;
            int i = 0;
            foreach (UIElement child in panel.Children)
            {
                if (child.Visibility != Visibility.Visible) continue;
                var shift = new TranslateTransform(0, 12);
                child.RenderTransform = shift;
                child.Opacity = 0;
                var begin = TimeSpan.FromMilliseconds(Math.Min(i, 11) * Tokens.Motion.StaggerMs);
                var time = TimeSpan.FromMilliseconds(Tokens.Motion.SlowMs);
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, time) { BeginTime = begin, EasingFunction = Out() });
                child.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, time) { BeginTime = begin, EasingFunction = Out() });
                i++;
            }
        }

        // Вырастание из точки: окно или меню появляется из того места, по которому щёлкнули.
        // origin — в долях размера элемента (0.5, 0.5 — из середины).
        public static void GrowFrom(FrameworkElement element, Point origin)
        {
            if (!Ui.Live(element)) return;
            var scale = new ScaleTransform(0.86, 0.86);
            element.RenderTransformOrigin = origin;
            element.RenderTransform = scale;
            var time = TimeSpan.FromMilliseconds(Tokens.Motion.BaseMs);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, time) { EasingFunction = Out() });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, time) { EasingFunction = Out() });
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, time) { EasingFunction = Out() });
        }

        static TimeSpan Ms(double ms) { return TimeSpan.FromMilliseconds(ms); }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, object> leaving = new System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, object>();

        static string NoDigits(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (char c in s ?? "") if (!char.IsDigit(c)) b.Append(c);
            return b.ToString();
        }

        // Смена надписи: новая проявляется, чуть поднимаясь. Если поменялись только цифры (идёт счёт) — без движения.
        public static void Swap(TextBlock text, string value)
        {
            if (text.Text == value) return;
            bool counting = NoDigits(text.Text) == NoDigits(value);
            bool was = text.Text != "";
            text.Text = value;
            if (counting || !was || !Ui.Live(text)) return;
            var shift = new TranslateTransform(0, 5);
            text.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, Ms(Tokens.Motion.FastMs)) { EasingFunction = Out() });
            text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(Tokens.Motion.FastMs)) { EasingFunction = Out() });
        }

        // Хлопок: элемент выскакивает с лёгким перелётом — «готово», «получилось»
        public static void Pop(FrameworkElement element)
        {
            if (!Ui.Live(element)) return;
            var scale = new ScaleTransform(1, 1);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = scale;
            var a = new DoubleAnimation(0.8, 1, Ms(Tokens.Motion.SlowMs)) { EasingFunction = Out() };   // без перелёта: Windows не «прыгает»
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // Идёт работа: элемент мягко приглушается и возвращается. Включать и выключать — по одному разу.
        public static void Breathe(FrameworkElement element, bool on)
        {
            if (!on || !Ui.Live(element)) { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1; return; }
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.55, Ms(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }

        // Мерцание: точка состояния, пока что-то делается
        public static void Blink(UIElement element, bool on)
        {
            if (!on || !Ui.Live(element)) { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1; return; }
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.3, Ms(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }

        // Показать или спрятать: появляется, съезжая сверху; уходит, растворяясь, и только потом освобождает место
        public static void Show(FrameworkElement element, bool on)
        {
            object mark;
            bool going = leaving.TryGetValue(element, out mark);
            bool visible = element.Visibility == Visibility.Visible && !going;
            if (visible == on) return;
            if (going) leaving.Remove(element);
            if (!Ui.Live(element))
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                element.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            if (on)
            {
                element.Visibility = Visibility.Visible;
                var shift = new TranslateTransform(0, -6);
                element.RenderTransform = shift;
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, Ms(Tokens.Motion.BaseMs)) { EasingFunction = Out() });
                element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(Tokens.Motion.BaseMs)) { EasingFunction = Out() });
                return;
            }
            leaving.Add(element, new object());
            var fade = new DoubleAnimation(0, Ms(Tokens.Motion.FastMs)) { EasingFunction = Out() };
            fade.Completed += delegate
            {
                object m;
                if (!leaving.TryGetValue(element, out m)) return;   // за это время снова попросили показать
                leaving.Remove(element);
                element.Visibility = Visibility.Collapsed;
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
            };
            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // Вход сбоку: dx > 0 — справа (вперёд), dx < 0 — слева (назад)
        public static void SlideIn(FrameworkElement element, double dx)
        {
            if (!Ui.Live(element)) return;
            var shift = new TranslateTransform(dx, 0);
            element.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, Ms(Tokens.Motion.SlowMs)) { EasingFunction = Out() });
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(Tokens.Motion.SlowMs)) { EasingFunction = Out() });
        }

        // Вход страницы, как в «Параметрах» Windows 11: новая страница поднимается снизу и проявляется
        public static void Enter(FrameworkElement element)
        {
            if (!Ui.Live(element)) return;
            var shift = new TranslateTransform(0, 32);
            element.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(32, 0, Ms(Tokens.Motion.SlowMs)) { EasingFunction = Out() });
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(Tokens.Motion.BaseMs)) { EasingFunction = Out() });
        }

        // «Не получилось»: Windows не трясёт окна — кромка вспыхивает красным, а текст ошибки говорит остальное
        public static void Shake(FrameworkElement element)
        {
            var b = element as Border;
            if (b != null) Glow(b, Tokens.Palette.Red);
        }

        // Вспышка кромки: рамка загорается цветом и гаснет — «готово»
        public static void Glow(Border border, Color color)
        {
            if (!Ui.Live(border)) return;
            Brush rest = border.BorderBrush;
            var lit = new SolidColorBrush(color);
            border.BorderBrush = lit;
            var a = new ColorAnimation(color, Ui.Alpha(color, 0.15), Ms(Tokens.Motion.RevealMs * 2)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += delegate { if (border.BorderBrush == lit) border.BorderBrush = rest; };
            lit.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }

        // Заливка по шагам: полоска шага наливается слева направо
        public static void FillIn(FrameworkElement element)
        {
            if (!Ui.Live(element)) return;
            var scale = new ScaleTransform(0, 1);
            element.RenderTransformOrigin = new Point(0, 0.5);
            element.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, Ms(Tokens.Motion.SlowMs)) { EasingFunction = Out() });
        }

        // Набегающее число: текст считает от from до to; show превращает число в надпись («66,0 МБ»)
        public static void Count(TextBlock text, double from, double to, Func<double, string> show)
        {
            if (Ui.ReducedMotion || from == to) { text.Text = show(to); return; }
            var started = DateTime.UtcNow;
            double length = Tokens.Motion.RevealMs * 2;
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += delegate
            {
                double t = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / length);
                double eased = 1 - Math.Pow(1 - t, 3);
                text.Text = show(from + (to - from) * eased);
                if (t >= 1) timer.Stop();
            };
            text.Text = show(from);
            timer.Start();
        }
    }
}
