// kit.txt — общие элементы интерфейса dev.txt™ для C# (WPF): Kapsula, core.txt и всё, что появится позже.
// Копируется в приложение как есть, рядом с Tokens.cs, и руками там не правится. Здесь нет ни одного своего
// цвета, радиуса или длительности — только токены. Те же элементы для веба — в ui/kit.css; менять их нужно вместе.
// Пишется на C# 5: его понимает компилятор, встроенный в Windows.
using System;
using System.Windows;
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

        public static readonly Brush Bg = Solid(Tokens.Dark.Bg), Label = Solid(Tokens.Dark.Label), Label2 = Solid(Tokens.Dark.Label2), Label3 = Solid(Tokens.Dark.Label3);
        public static readonly Brush Fill = Solid(Tokens.Dark.Fill), Fill2 = Solid(Tokens.Dark.Fill2);
        public static readonly Brush GlassFill = Glass(Tokens.GlassDark.FillTop, Tokens.GlassDark.FillBottom);
        public static readonly Brush GlassHover = Glass(Tokens.GlassDark.HoverTop, Tokens.GlassDark.HoverBottom);
        public static readonly Brush GlassStrong = Glass(Tokens.GlassDark.StrongTop, Tokens.GlassDark.StrongBottom);
        public static readonly Brush Rim = MakeRim();
        // Карточка — стекло потоньше кнопки: кнопка на ней должна читаться
        public static readonly Brush CardFill = Glass(Tokens.GlassDark.FillTop * 0.55, Tokens.GlassDark.FillBottom * 0.55);
        static readonly Brush SpriteBody = Solid(Tokens.Palette.Brand), SpriteEye = Solid(Color.FromRgb(0x1A, 0x1A, 0x1E));

        public static Brush Solid(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static Color Alpha(Color c, double a) { return Color.FromArgb((byte)Math.Round(255 * Math.Max(0, Math.Min(1, a))), c.R, c.G, c.B); }

        static Brush Glass(double top, double bottom)
        {
            var b = new LinearGradientBrush(Alpha(Colors.White, top), Alpha(Colors.White, bottom), 90);
            b.Freeze();
            return b;
        }

        static Brush MakeRim()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.7, 1) };
            for (int i = 0; i < Tokens.GlassDark.RimColors.Length; i++) b.GradientStops.Add(new GradientStop(Tokens.GlassDark.RimColors[i], Tokens.GlassDark.RimOffsets[i]));
            b.Freeze();
            return b;
        }

        // Главная кнопка: залита цветом, сверху чуть светлее
        public static Brush Tinted(Color c)
        {
            Func<byte, byte> up = delegate(byte v) { return (byte)Math.Min(255, v + 26); };
            var b = new LinearGradientBrush(Color.FromRgb(up(c.R), up(c.G), up(c.B)), c, 90);
            b.Freeze();
            return b;
        }

        public static IEasingFunction EaseOut { get { return new CubicEase { EasingMode = EasingMode.EaseOut }; } }
        // «Меньше движения»: системная настройка Windows; приложение может включить и само
        public static bool ForceReducedMotion = false;
        public static bool ReducedMotion { get { return ForceReducedMotion || !SystemParameters.ClientAreaAnimation; } }

        public static TextBlock T(string text, double size, Brush color, FontWeight weight)
        {
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
                CornerRadius = new CornerRadius(Tokens.Radius.Row), Background = GlassFill, BorderBrush = Rim, BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10), Child = child, SnapsToDevicePixels = true
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

        // Свечение за стеклом: три цветных пятна на тёмном фоне. Кладётся первым в корень окна.
        public static Canvas Backdrop() { return Backdrop(920, 640); }

        // То же для окна другого размера: пятна расставляются в тех же долях ширины и высоты
        public static Canvas Backdrop(double width, double height)
        {
            double kx = width / 920, ky = height / 640, k = Math.Max(kx, ky);
            var glow = new Canvas { IsHitTestVisible = false };
            Action<double, double, double, Color, double> blob = delegate(double x, double y, double size, Color c, double a)
            {
                var fill = new RadialGradientBrush();
                fill.GradientStops.Add(new GradientStop(Alpha(c, a), 0));
                fill.GradientStops.Add(new GradientStop(Alpha(c, a * 0.55), 0.45));
                fill.GradientStops.Add(new GradientStop(Alpha(c, 0), 1));
                fill.Freeze();
                // центр пятна — в той же доле окна, размер — по большей стороне
                double side = size * k;
                var e = new Ellipse { Width = side, Height = side, Fill = fill };
                Canvas.SetLeft(e, (x + size / 2) * kx - side / 2); Canvas.SetTop(e, (y + size / 2) * ky - side / 2);
                glow.Children.Add(e);
            };
            blob(-380, -420, 1000, Tokens.Palette.Brand, 0.42);
            blob(380, 240, 900, Tokens.Palette.Blue, 0.26);
            blob(520, -360, 760, Tokens.Palette.Purple, 0.2);
            return glow;
        }
    }

    // Кнопка-капсула: стеклянная или залитая цветом. Под курсором светлеет, при нажатии чуть сжимается.
    class Btn
    {
        public readonly Border Root;
        readonly TextBlock text;
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        Brush idle, hover;
        Action click;
        bool pressed;

        public Btn()
        {
            text = new TextBlock { FontSize = Tokens.Size.Body, FontWeight = FontWeights.SemiBold, FontFamily = Ui.TextFont, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Root = new Border
            {
                Height = 32, MinWidth = 96, CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 0, 16, 1), BorderThickness = new Thickness(1),
                Child = text, Cursor = Cursors.Hand, RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5), SnapsToDevicePixels = true
            };
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
            double to = on ? 0.965 : 1;
            if (Ui.ReducedMotion) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleX = scale.ScaleY = to; return; }
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(on ? Tokens.Motion.PressMs : Tokens.Motion.FastMs)) { EasingFunction = Ui.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // tint == null — стеклянная; иначе залита этим цветом
        public void Set(string label, Color? tint, bool enabled, Action onClick)
        {
            text.Text = label;
            click = onClick;
            if (tint.HasValue)
            {
                idle = Ui.Tinted(tint.Value);
                hover = Ui.Tinted(Color.FromRgb((byte)Math.Min(255, tint.Value.R + 14), (byte)Math.Min(255, tint.Value.G + 14), (byte)Math.Min(255, tint.Value.B + 14)));
                Root.BorderBrush = Ui.Solid(Ui.Alpha(Colors.White, 0.22));
                text.Foreground = Brushes.White;
            }
            else
            {
                idle = Ui.GlassFill; hover = Ui.GlassHover;
                Root.BorderBrush = Ui.Rim;
                text.Foreground = Ui.Label;
            }
            Root.Background = Root.IsMouseOver ? hover : idle;
            Root.IsHitTestVisible = enabled;
            Root.Opacity = enabled ? 1 : 0.45;
            Root.Visibility = Visibility.Visible;
        }

        public void Hide() { Root.Visibility = Visibility.Collapsed; }
    }

    // Переключатель: бегунок скользит, дорожка наливается зелёным. При «Меньше движения» — сразу на месте.
    class Switch
    {
        public readonly Border Root;
        readonly TranslateTransform knobX = new TranslateTransform();
        readonly SolidColorBrush track;
        readonly Action<bool> changed;
        bool on;
        const double W = 46, H = 28, Knob = 24;

        public Switch(bool state, Action<bool> changed)
        {
            on = state;
            this.changed = changed;
            track = new SolidColorBrush(on ? Tokens.Palette.Green : Tokens.Dark.Fill2);
            var knob = new Border
            {
                Width = Knob, Height = Knob, CornerRadius = new CornerRadius(Knob / 2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), RenderTransform = knobX,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Direction = 270, Opacity = 0.28, Color = Colors.Black }
            };
            knobX.X = on ? W - Knob - 4 : 0;
            Root = new Border { Width = W, Height = H, CornerRadius = new CornerRadius(H / 2), Background = track, Child = knob, Cursor = Cursors.Hand };
            Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Value = !on; if (this.changed != null) this.changed(on); };
        }

        public bool Value
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                double to = on ? W - Knob - 4 : 0;
                Color c = on ? Tokens.Palette.Green : Tokens.Dark.Fill2;
                if (Ui.ReducedMotion)
                {
                    knobX.BeginAnimation(TranslateTransform.XProperty, null); knobX.X = to;
                    track.BeginAnimation(SolidColorBrush.ColorProperty, null); track.Color = c;
                    return;
                }
                var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                var time = TimeSpan.FromMilliseconds(Tokens.Motion.BaseMs);
                knobX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, time) { EasingFunction = ease });
                track.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, time) { EasingFunction = ease });
            }
        }
    }

    // Полоса хода: тонкая дорожка и заливка цветом продукта
    class Progress
    {
        public readonly Border Root;
        readonly Border fill;
        double value;

        public Progress(Color tint)
        {
            fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Solid(tint), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            Root = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Fill2, Child = fill };
            Root.SizeChanged += delegate { fill.Width = Root.ActualWidth * value; };
        }

        public double Value
        {
            get { return value; }
            set { this.value = Math.Max(0, Math.Min(1, value)); fill.Width = Root.ActualWidth * this.value; }
        }
    }

    // Состояние: цветная точка и подпись
    class Status
    {
        public readonly DockPanel Root = new DockPanel();
        readonly Ellipse dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock text = Ui.T("", Tokens.Size.Caption, Ui.Label2, FontWeights.Normal);
        public enum Kind { None, Ok, Warn, Bad }

        public Status()
        {
            DockPanel.SetDock(dot, System.Windows.Controls.Dock.Left);   // полное имя: у приложения может быть своё пространство имён «Dock»
            Root.Children.Add(dot);
            Root.Children.Add(text);
        }

        public void Set(string label, Kind kind) { Set(label, kind == Kind.Ok ? Tokens.Palette.Green : kind == Kind.Warn ? Tokens.Palette.Orange : kind == Kind.Bad ? Tokens.Palette.Red : Tokens.Dark.Label3); }
        public void Set(string label, Color color) { text.Text = label; dot.Fill = Ui.Solid(color); }
    }

    // Переключатель из нескольких вариантов (вкладки, режимы): стеклянная дорожка, выбранный — плотное стекло с кромкой
    class Seg
    {
        public readonly Border Root;
        readonly System.Collections.Generic.List<Border> items = new System.Collections.Generic.List<Border>();
        readonly Action<int> changed;
        int index;

        public Seg(string[] labels, int selected, Action<int> changed)
        {
            this.changed = changed;
            index = selected;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < labels.Length; i++)
            {
                int n = i;
                var t = new TextBlock { Text = labels[i], FontSize = Tokens.Size.Body, FontFamily = Ui.TextFont, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var b = new Border { Height = 26, MinWidth = 44, CornerRadius = new CornerRadius(13), Padding = new Thickness(14, 0, 14, 1), BorderThickness = new Thickness(1), Child = t, Cursor = Cursors.Hand, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0) };
                b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (index == n) return;
                    Index = n;
                    if (this.changed != null) this.changed(n);
                };
                items.Add(b);
                row.Children.Add(b);
            }
            Root = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(3), Background = Ui.GlassFill, BorderBrush = Ui.Rim, BorderThickness = new Thickness(1), Child = row, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true };
            Paint();
        }

        public int Index
        {
            get { return index; }
            set { index = value; Paint(); }
        }

        void Paint()
        {
            for (int i = 0; i < items.Count; i++)
            {
                bool on = i == index;
                items[i].Background = on ? Ui.GlassStrong : Brushes.Transparent;
                items[i].BorderBrush = on ? Ui.Rim : Brushes.Transparent;
                var t = (TextBlock)items[i].Child;
                t.Foreground = on ? Ui.Label : Ui.Label2;
                t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
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
        const double W = 168, Knob = 18;

        public Slider(double min, double max, double step, double value, Color tint, Action<double> changed)
        {
            this.min = min; this.max = Math.Max(max, min + 1e-9); this.step = step; this.changed = changed;
            fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Solid(tint), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Knob / 2, 0, 0, 0) };
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Fill2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Knob / 2, 0, Knob / 2, 0) };
            knob = new Border
            {
                Width = Knob, Height = Knob, CornerRadius = new CornerRadius(Knob / 2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RenderTransform = knobX,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Direction = 270, Opacity = 0.28, Color = Colors.Black }
            };
            Root = new Grid { Width = W, Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { track, fill, knob } };
            Root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Root.CaptureMouse(); Move(e.GetPosition(Root).X); };
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

    // Движение — общие приёмы; те же, что в ui/kit.css. При «Меньше движения» всё сразу встаёт на место.
    static class Motion
    {
        static IEasingFunction Out() { return new CubicEase { EasingMode = EasingMode.EaseOut }; }

        // Появление по очереди: дети поднимаются и проявляются волной, сосед за соседом
        public static void Stagger(Panel panel)
        {
            if (Ui.ReducedMotion) return;
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
            if (Ui.ReducedMotion) return;
            var scale = new ScaleTransform(0.86, 0.86);
            element.RenderTransformOrigin = origin;
            element.RenderTransform = scale;
            var time = TimeSpan.FromMilliseconds(Tokens.Motion.BaseMs);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, time) { EasingFunction = Out() });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, time) { EasingFunction = Out() });
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, time) { EasingFunction = Out() });
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
