// smena.txt. Switcher.cs — сам переключатель: прозрачное окно на весь монитор, затемнение, стеклянная панель по центру
// и на ней карточки окон — живое превью (его рисует сама Windows поверх нашего окна), значок и заголовок.
// Окно создаётся один раз при запуске и дальше только показывается и прячется; тяжёлого при показе нет:
// значки берутся из памяти, превью Windows отдаёт сразу.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DevTxt.Smena
{
    // Карточка одного окна
    class Card
    {
        public WinItem Item;
        public Grid Root;
        public Border Box, Hover, IconHost;     // Box — место превью
        public Border BigIconHost;              // крупный значок вместо превью (свёрнутое окно); может не быть
        public TextBlock Title;
        public readonly ScaleTransform Scale = new ScaleTransform(1, 1);
        public Grid Slot;                       // место карточки в ряду
        public Canvas Row;                      // её ряд
        public IntPtr Thumb;
        public Win.RECT Source, LastDest, LastSource;
        public int LastOpacity = -1;
        public bool LastVisible;
    }

    class Switcher : Window
    {
        const double PanelPad = 14, Footer = 30, SelectedScale = 1.03, Shadow = 40;

        readonly bool demo, headless;
        readonly Session session = new Session();
        readonly List<Card> cards = new List<Card>();
        List<WinItem> items = new List<WinItem>();
        bool sticky, snap;
        int cols, rows, visibleRows, firstRow;
        double preview, cardW, cardH, thumbH, scale = 1;
        IntPtr hwnd;

        readonly Grid root = new Grid();
        readonly Border dim, shadow, glass, highlight, scrollMark;
        readonly Grid panelHost = new Grid(), glow = new Grid();
        readonly Canvas viewport = new Canvas { ClipToBounds = true }, strip = new Canvas(), deck = new Canvas();
        readonly TranslateTransform highlightAt = new TranslateTransform();
        readonly TextBlock hint;
        readonly DispatcherTimer altWatch;
        readonly Stopwatch clock = new Stopwatch();
        double hx, hy, hvx, hvy, tx, ty;   // подсветка: где она, скорость, куда едет
        double sy, svy, tsy;               // прокрутка рядов
        double last;
        bool ticking;

        public KeyMachine Machine;          // клавиши; может не быть (--demo, снимки)
        public Action Finished;             // переключатель закрылся
        public static int DemoCount = 9;
        public int LastCount;               // сколько окон было в последнем показе
        public bool IsOpen { get { return session.Open; } }

        public Switcher(bool demo, bool headless)
        {
            this.demo = demo; this.headless = headless;
            Title = Program.Id + ".txt";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = Ui.TextFont;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            // Затемнение: щелчок мимо панели — отмена
            dim = new Border { Background = Ui.Solid(Ui.Alpha(Tokens.Dark.Bg, 0.5)) };
            dim.MouseLeftButtonDown += delegate { Cancel(); };
            root.Children.Add(dim);

            // Панель: тёмная подложка с тенью, свечение, стекло с кромкой
            shadow = new Border
            {
                CornerRadius = new CornerRadius(Tokens.Radius.Block), Background = Ui.Solid(Tokens.Dark.Menu),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = Shadow, ShadowDepth = 10, Direction = 270, Opacity = 0.45, Color = Colors.Black }
            };
            glow.Opacity = 0.6;
            glow.IsHitTestVisible = false;
            hint = Ui.T("", Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.HorizontalAlignment = HorizontalAlignment.Right;
            var mark = Ui.Sprite(Program.Sprite, 16);
            mark.VerticalAlignment = VerticalAlignment.Center;
            var brand = Ui.Brand(Program.Id, Tokens.Size.Caption, FontWeights.SemiBold);
            brand.Margin = new Thickness(7, 0, 0, 1); brand.VerticalAlignment = VerticalAlignment.Center;
            var foot = new Grid { Height = Footer, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Layout.Margin + 2, 0, Layout.Margin + 2, 0) };
            foot.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Children = { mark, brand } });
            foot.Children.Add(hint);
            viewport.VerticalAlignment = VerticalAlignment.Top;
            viewport.HorizontalAlignment = HorizontalAlignment.Left;
            viewport.Children.Add(strip);
            scrollMark = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = Ui.Fill2, Visibility = Visibility.Collapsed };
            viewport.Children.Add(scrollMark);
            glass = new Border
            {
                CornerRadius = new CornerRadius(Tokens.Radius.Block), Background = Ui.CardFill, BorderBrush = Ui.Rim, BorderThickness = new Thickness(1),
                Padding = new Thickness(PanelPad - 1, PanelPad - 1, PanelPad - 1, PanelPad / 2 - 1), Child = new Grid { Children = { viewport, foot } }, SnapsToDevicePixels = true
            };
            panelHost.HorizontalAlignment = HorizontalAlignment.Center;
            panelHost.VerticalAlignment = VerticalAlignment.Center;
            panelHost.Children.Add(shadow);
            panelHost.Children.Add(glow);
            panelHost.Children.Add(glass);
            panelHost.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };   // щелчок по панели мимо карточек ничего не делает
            root.Children.Add(panelHost);

            highlight = new Border
            {
                CornerRadius = new CornerRadius(Tokens.Radius.Row), Background = Ui.GlassStrong, BorderBrush = Ui.Rim, BorderThickness = new Thickness(1),
                RenderTransform = highlightAt, IsHitTestVisible = false, SnapsToDevicePixels = true
            };
            Content = root;

            // Если Alt отпустили, а мы этого не увидели (экран блокировки, окно с правами администратора) — не висим, а переключаемся
            altWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            altWatch.Tick += delegate { if (session.Open && !sticky && !Win.KeyDown(KeyMachine.Alt)) Commit(); };

            // Клавиши, когда окно само в фокусе (показ командой, без перехватчика)
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (!session.Open || !sticky) return;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                KeyAction a = e.Key == Key.Tab ? (shift ? KeyAction.Prev : KeyAction.Next) : e.Key == Key.Left ? KeyAction.Left : e.Key == Key.Right ? KeyAction.Right
                    : e.Key == Key.Up ? KeyAction.Up : e.Key == Key.Down ? KeyAction.Down : e.Key == Key.Escape ? KeyAction.Cancel : e.Key == Key.Enter ? KeyAction.Commit : KeyAction.None;
                if (a == KeyAction.None) return;
                e.Handled = true;
                Handle(a);
            };
            Deactivated += delegate { if (session.Open && sticky && IsVisible) Cancel(); };
        }

        // Создаёт окно Windows, не показывая его: к первому Alt+Tab всё готово
        public void Prepare()
        {
            hwnd = new WindowInteropHelper(this).EnsureHandle();
            Win.Style(hwnd, true);
            var src = HwndSource.FromHwnd(hwnd);
            if (src != null && src.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
            if (scale <= 0) scale = 1;
        }

        // ───────── Действия: от клавиатуры (через KeyMachine), от мыши, от команды ─────────
        public void Handle(KeyAction a)
        {
            try
            {
                if (a == KeyAction.Open || a == KeyAction.OpenBack || a == KeyAction.OpenSame)
                {
                    if (!session.Open && !OpenView(false, a) && Machine != null) Machine.End();
                }
                else if (a == KeyAction.Commit) Commit();
                else if (a == KeyAction.Cancel) Cancel();
                else if (session.Move(a)) Select(true);
            }
            catch { Abort(); throw; }
        }

        // Показ без клавиатуры: остаётся, пока не щёлкнут окно или не нажмут Esc. false — показывать нечего или уже показан.
        public bool OpenSticky()
        {
            if (session.Open) return false;
            try
            {
                if (!OpenView(true, KeyAction.Open)) return false;
                if (headless) return true;
                if (Machine != null) Machine.BeginSticky();
                return true;
            }
            catch { Abort(); throw; }
        }

        bool OpenView(bool stay, KeyAction how)
        {
            bool fgFirst = true;
            items = demo ? Demo.Windows(DemoCount) : Win.List(Prefs.AllDesktops, true, out fgFirst);
            if (how == KeyAction.OpenSame) items = Logic.SameApp(items, fgFirst && items.Count > 0 ? items[0].Key : null);
            LastCount = items.Count;
            if (items.Count == 0) return false;
            if (headless) return true;   // проверки без экрана: окна сосчитаны, показывать нечего

            sticky = stay;
            Win.RECT mon = Win.ActiveMonitor();
            double w = (mon.Right - mon.Left) / scale, h = (mon.Bottom - mon.Top) / scale;
            Arrange(w, h);
            session.Begin(items.Count, cols, fgFirst, how == KeyAction.OpenBack);
            foreach (Card c in cards)
            {
                if (c.Item.Minimized) continue;
                c.Thumb = Win.ThumbRegister(hwnd, c.Item.Hwnd);
                if (c.Thumb != IntPtr.Zero && !Win.ThumbSource(c.Thumb, c.Item.Hwnd, out c.Source)) { Win.ThumbRelease(c.Thumb); c.Thumb = IntPtr.Zero; }
            }
            foreach (Card c in cards) if (c.Thumb == IntPtr.Zero) NoPreview(c);

            Left = mon.Left / scale; Top = mon.Top / scale; Width = w; Height = h;
            Win.Style(hwnd, !sticky);
            snap = Ui.ReducedMotion;
            Select(false);
            Enter();
            Show();
            Win.Place(hwnd, mon);
            root.UpdateLayout();
            SyncThumbs();
            clock.Restart();
            last = 0;
            if (!ticking) { CompositionTarget.Rendering += Frame; ticking = true; }
            if (sticky) Activate();
            else
            {
                Win.SendMask();   // чтобы программа под нами не открыла меню, когда Alt отпустят
                altWatch.Start();
            }
            return true;
        }

        void Commit()
        {
            int chosen = session.Close();
            if (Machine != null) Machine.End();
            if (chosen < 0) return;
            WinItem target = chosen < items.Count ? items[chosen] : null;
            if (!demo && !headless && target != null) Win.Activate(target.Hwnd);
            CloseView();
        }

        void Cancel()
        {
            bool was = session.Open;
            session.Close();
            if (Machine != null) Machine.End();
            if (was) CloseView();
        }

        // Что-то пошло не так — затемнение на экране не оставляем ни при каких условиях
        public void Abort()
        {
            try { session.Close(); if (Machine != null) Machine.End(); CloseView(); } catch { }
        }

        void CloseView()
        {
            altWatch.Stop();
            if (ticking) { CompositionTarget.Rendering -= Frame; ticking = false; }
            foreach (Card c in cards) { Win.ThumbRelease(c.Thumb); c.Thumb = IntPtr.Zero; }
            if (IsVisible) Hide();
            cards.Clear();
            deck.Children.Clear();
            strip.Children.Clear();
            if (Finished != null) Finished();
        }

        // ───────── Раскладка ─────────

        // Строит карточки для окон items на экране w×h
        void Arrange(double w, double h)
        {
            root.Width = w; root.Height = h;
            preview = Math.Max(Layout.MinPreview, Math.Min(Layout.MaxPreview, Prefs.Size));
            // Маленький экран: карточка не шире, чем он позволяет
            preview = Math.Min(preview, Math.Max(80, w * 0.9 - 2 * (PanelPad + Layout.Margin + Layout.Pad)));
            cardW = Layout.CardW(preview); cardH = Layout.CardH(preview); thumbH = Layout.ThumbH(preview);
            Layout.Rows(items.Count, cardW, cardH, w * 0.92 - 2 * (PanelPad + Layout.Margin), h * 0.84 - PanelPad * 1.5 - Footer - 2 * Layout.Margin, out cols, out rows, out visibleRows);
            firstRow = 0;

            double vw = cols * cardW + (cols - 1) * Layout.Gap + 2 * Layout.Margin, vh = visibleRows * cardH + (visibleRows - 1) * Layout.Gap + 2 * Layout.Margin;
            // Узкая панель: подсказке про клавиши нужно место рядом с названием
            double minW = 2 * (Layout.Margin + 2) + 120 + hint.FontSize * 0.5 * HintText().Length;
            double pw = Math.Max(vw, minW) + 2 * PanelPad, ph = PanelPad + vh + Footer + PanelPad / 2;
            viewport.Width = vw; viewport.Height = vh;
            viewport.Margin = new Thickness((pw - 2 * PanelPad - vw) / 2, 0, 0, 0);
            panelHost.Width = pw; panelHost.Height = ph;
            glow.Children.Clear();
            glow.Children.Add(Ui.Backdrop(pw, ph));
            glow.Clip = new RectangleGeometry(new Rect(0, 0, pw, ph), Tokens.Radius.Block, Tokens.Radius.Block);
            hint.Text = HintText();

            strip.Children.Clear();
            cards.Clear();
            highlight.Width = cardW + 2 * Layout.Grow; highlight.Height = cardH + 2 * Layout.Grow;
            deck.Children.Clear();
            strip.Children.Add(highlight);
            strip.Children.Add(deck);
            Canvas row = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (i % cols == 0)
                {
                    row = new Canvas { Width = cols * (cardW + Layout.Gap), Height = cardH };
                    Canvas.SetTop(row, (i / cols) * (cardH + Layout.Gap));
                    deck.Children.Add(row);
                }
                Card c = MakeCard(items[i], i);
                c.Row = row;
                c.Slot = new Grid { Width = cardW, Height = cardH, Children = { c.Root } };
                Canvas.SetLeft(c.Slot, (i % cols) * (cardW + Layout.Gap));
                row.Children.Add(c.Slot);
                cards.Add(c);
            }
            Canvas.SetLeft(strip, Layout.Margin);
            bool scrolls = rows > visibleRows;
            scrollMark.Visibility = scrolls ? Visibility.Visible : Visibility.Collapsed;
            if (scrolls)
            {
                scrollMark.Height = (vh - 2 * Layout.Margin) * visibleRows / rows;
                Canvas.SetLeft(scrollMark, vw - 5);
            }
        }

        string HintText()
        {
            return sticky ? "Щёлкните окно или нажмите Enter  ·  Esc — отмена" : "Tab — дальше  ·  Shift+Tab — назад  ·  стрелки — выбор  ·  Esc — отмена";
        }

        Card MakeCard(WinItem item, int index)
        {
            var c = new Card { Item = item };
            c.Hover = new Border { CornerRadius = new CornerRadius(Tokens.Radius.Row), Background = Brushes.Transparent };
            c.Box = new Border { Width = preview, Height = thumbH, Margin = new Thickness(Layout.Pad, Layout.Pad, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            if (item.Fake && !item.Minimized) c.Box.Child = Demo.Window(item, preview, thumbH);

            c.IconHost = new Border { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            c.Title = Ui.T(item.Title, Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
            c.Title.VerticalAlignment = VerticalAlignment.Center;
            var caption = new DockPanel { Height = Layout.Caption, Margin = new Thickness(Layout.Pad + 3, Layout.Pad + thumbH, Layout.Pad + 3, 0), VerticalAlignment = VerticalAlignment.Top, LastChildFill = true };
            DockPanel.SetDock(c.IconHost, System.Windows.Controls.Dock.Left);
            caption.Children.Add(c.IconHost);
            caption.Children.Add(c.Title);

            c.Root = new Grid { Width = cardW, Height = cardH, Background = Brushes.Transparent, Cursor = Cursors.Hand, RenderTransform = c.Scale, RenderTransformOrigin = new Point(0.5, 0.5), Children = { c.Hover, c.Box, caption } };
            c.Root.MouseEnter += delegate { if (cards.IndexOf(c) != session.Selected) c.Hover.Background = Ui.Fill; };
            c.Root.MouseLeave += delegate { c.Hover.Background = Brushes.Transparent; };
            c.Root.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                int at = cards.IndexOf(c);
                if (at < 0 || !session.Open) return;
                session.Selected = at;
                Commit();
            };
            if (item.Minimized) NoPreview(c);
            SetIcon(c, item.Fake ? null : item.Icon ?? Win.Icon(item, Dispatcher, IconReady));
            return c;
        }

        // Вместо превью — плитка с крупным значком: окно свёрнуто или Windows не дала его картинку
        void NoPreview(Card c)
        {
            if (c.BigIconHost != null) return;
            c.BigIconHost = new Border { Width = 56, Height = 56 };
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { c.BigIconHost } };
            if (c.Item.Minimized)
            {
                var note = Ui.T("Свёрнуто", Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
                note.HorizontalAlignment = HorizontalAlignment.Center; note.Margin = new Thickness(0, 8, 0, 0);
                col.Children.Add(note);
            }
            c.Box.Child = new Border { CornerRadius = new CornerRadius(Tokens.Radius.Tip), Background = Ui.Fill, Child = col };
            SetIcon(c, c.Item.Icon);
        }

        void SetIcon(Card c, ImageSource img)
        {
            if (img != null) c.Item.Icon = img;
            c.IconHost.Child = Demo.IconOf(c.Item, 18);
            if (c.BigIconHost != null) c.BigIconHost.Child = Demo.IconOf(c.Item, 56);
        }

        // Значок добыт в фоне — ставим, если карточка ещё на экране
        void IconReady(WinItem item, ImageSource img)
        {
            item.Icon = img;
            foreach (Card c in cards) if (c.Item == item) SetIcon(c, img);
        }

        // ───────── Выбор, прокрутка, появление ─────────

        // Приводит картинку к выбранному окну: подсветка, размер карточки, прокрутка рядов
        void Select(bool animate)
        {
            int sel = session.Selected;
            for (int i = 0; i < cards.Count; i++)
            {
                bool on = i == sel;
                Card c = cards[i];
                c.Title.Foreground = on ? Ui.Label : Ui.Label2;
                if (on) c.Hover.Background = Brushes.Transparent;
                double to = on ? SelectedScale : 1;
                if (!animate || snap)
                {
                    c.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); c.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    c.Scale.ScaleX = c.Scale.ScaleY = to;
                }
                else if (Math.Abs(c.Scale.ScaleX - to) > 0.0005 || on)
                {
                    var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(Tokens.Motion.FastMs)) { EasingFunction = Ui.EaseOut };
                    c.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                    c.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
            }
            firstRow = Layout.Scroll(firstRow, sel / cols, visibleRows, rows);
            tx = (sel % cols) * (cardW + Layout.Gap) - Layout.Grow;
            ty = (sel / cols) * (cardH + Layout.Gap) - Layout.Grow;
            tsy = firstRow * (cardH + Layout.Gap);
            if (!animate || snap) { hx = tx; hy = ty; sy = tsy; hvx = hvy = svy = 0; }
            Place();
        }

        void Place()
        {
            highlightAt.X = hx; highlightAt.Y = hy;
            Canvas.SetTop(strip, Layout.Margin - sy);
            if (rows > visibleRows)
            {
                double track = viewport.Height - 2 * Layout.Margin, max = (rows - visibleRows) * (cardH + Layout.Gap);
                Canvas.SetTop(scrollMark, Layout.Margin + (track - scrollMark.Height) * Math.Max(0, Math.Min(1, sy / max)));
            }
        }

        // Появление — общими приёмами dev.txt (Ui.cs, Motion): панель вырастает из середины и проявляется,
        // карточки поднимаются волной: в одном ряду — одна за другой, в нескольких — ряд за рядом.
        // При «Меньше движения» всё сразу на месте.
        void Enter()
        {
            panelHost.BeginAnimation(OpacityProperty, null);
            dim.BeginAnimation(OpacityProperty, null);
            panelHost.RenderTransform = null;
            panelHost.Opacity = 1; dim.Opacity = 1;
            if (snap) return;
            dim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Tokens.Motion.FastMs)) { EasingFunction = Ui.EaseOut });
            Motion.GrowFrom(panelHost, new Point(0.5, 0.5));
            Motion.Stagger(deck.Children.Count == 1 ? (Panel)deck.Children[0] : deck);
        }

        // Каждый кадр, пока переключатель на экране: пружины подсветки и прокрутки, затем превью встают на свои места
        void Frame(object sender, EventArgs e)
        {
            try
            {
                double now = clock.Elapsed.TotalSeconds, dt = now - last;
                last = now;
                if (hx != tx || hy != ty || sy != tsy)
                {
                    Layout.Spring(ref hx, ref hvx, tx, Tokens.Motion.SpringFadeStiffness, Tokens.Motion.SpringFadeDamping, dt);
                    Layout.Spring(ref hy, ref hvy, ty, Tokens.Motion.SpringFadeStiffness, Tokens.Motion.SpringFadeDamping, dt);
                    Layout.Spring(ref sy, ref svy, tsy, Tokens.Motion.SpringShapeStiffness, Tokens.Motion.SpringShapeDamping, dt);
                    if (Math.Abs(hx - tx) < 0.3 && Math.Abs(hvx) < 2) { hx = tx; hvx = 0; }
                    if (Math.Abs(hy - ty) < 0.3 && Math.Abs(hvy) < 2) { hy = ty; hvy = 0; }
                    if (Math.Abs(sy - tsy) < 0.3 && Math.Abs(svy) < 2) { sy = tsy; svy = 0; }
                    Place();
                    strip.UpdateLayout();
                }
                SyncThumbs();
            }
            catch { Abort(); }
        }

        // Ставит каждое живое превью туда, где сейчас его карточка: с учётом увеличения, подъёма, прокрутки и прозрачности.
        // Windows рисует превью сама, поверх нашего окна; мы только говорим, где и какую часть.
        void SyncThumbs()
        {
            if (hwnd == IntPtr.Zero || cards.Count == 0) return;
            Rect vp = viewport.TransformToAncestor(root).TransformBounds(new Rect(0, 0, viewport.Width, viewport.Height));
            double shown = panelHost.Opacity;
            foreach (Card c in cards)
            {
                if (c.Thumb == IntPtr.Zero) continue;
                Rect b = c.Box.TransformToAncestor(root).TransformBounds(new Rect(0, 0, preview, thumbH));
                double x, y, w, h;
                Layout.Fit(c.Source.Right - c.Source.Left, c.Source.Bottom - c.Source.Top, b.X * scale, b.Y * scale, b.Width * scale, b.Height * scale, 1, out x, out y, out w, out h);
                double dl = x, dt = y, dr = x + w, db = y + h, sl = c.Source.Left, st = c.Source.Top, sr = c.Source.Right, sb = c.Source.Bottom;
                bool visible = Layout.Clip(ref dl, ref dt, ref dr, ref db, ref sl, ref st, ref sr, ref sb, vp.Left * scale, vp.Top * scale, vp.Right * scale, vp.Bottom * scale);
                int opacity = (int)Math.Round(255 * Math.Max(0, Math.Min(1, shown * c.Slot.Opacity * c.Row.Opacity)));
                var d = new Win.RECT { Left = (int)Math.Round(dl), Top = (int)Math.Round(dt), Right = (int)Math.Round(dr), Bottom = (int)Math.Round(db) };
                var src = new Win.RECT { Left = (int)Math.Round(sl), Top = (int)Math.Round(st), Right = (int)Math.Round(sr), Bottom = (int)Math.Round(sb) };
                visible = visible && opacity > 0;
                if (visible == c.LastVisible && opacity == c.LastOpacity && Same(d, c.LastDest) && Same(src, c.LastSource)) continue;
                Win.ThumbUpdate(c.Thumb, d, src, (byte)opacity, visible);
                c.LastVisible = visible; c.LastOpacity = opacity; c.LastDest = d; c.LastSource = src;
            }
        }

        static bool Same(Win.RECT a, Win.RECT b) { return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom; }

        // ───────── Снимок и проверка без экрана ─────────

        // Рисует переключатель с выдуманными окнами в файл PNG. Живых превью в файле быть не может (их рисует Windows
        // прямо на экране), поэтому вместо них — условные «окна».
        public void Shot(string file, double w, double h, int select, bool stay)
        {
            items = Demo.Windows(DemoCount);
            sticky = stay;
            snap = true;
            Arrange(w, h);
            session.Begin(items.Count, cols, true, false);
            if (select >= 0 && select < items.Count) session.Selected = select;
            Select(false);
            Content = null;
            // Вместо рабочего стола — общий фон dev.txt
            var desk = new Grid { Background = Ui.Bg, ClipToBounds = true };
            desk.Children.Add(Ui.Backdrop(w, h));
            root.Children.Insert(0, desk);
            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = System.IO.File.Create(file)) enc.Save(f);
        }

        // Настоящие окна, но ничего не показывая: сколько их и удалось ли завести живое превью первого на нашем спрятанном окне
        public string Probe()
        {
            bool first;
            var current = Win.List(false, true, out first);
            var all = Win.List(true, true, out first);
            string thumb = "нет окон";
            if (current.Count > 0)
            {
                WinItem w = current.Find(delegate(WinItem x) { return !x.Minimized; }) ?? current[0];
                IntPtr t = Win.ThumbRegister(hwnd, w.Hwnd);
                Win.RECT part;
                bool sized = t != IntPtr.Zero && Win.ThumbSource(t, w.Hwnd, out part);
                bool updated = sized && Win.ThumbUpdate(t, new Win.RECT { Right = 100, Bottom = 62 }, new Win.RECT { Right = 100, Bottom = 62 }, 255, true);
                thumb = t == IntPtr.Zero ? "не зарегистрировано" : "зарегистрировано, размер " + (sized ? "получен" : "не получен") + ", положение " + (updated ? "принято" : "не принято");
                Win.ThumbRelease(t);
            }
            return "окон: " + current.Count + "; со всех рабочих столов: " + all.Count + "; превью первого: " + thumb + "; масштаб " + scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // Выдуманные окна для --demo и снимков, а ещё плитка с буквой — значок для тех, у кого своего нет
    static class Demo
    {
        static readonly string[] Titles =
        {
            "Отчёт за ноябрь — Документы", "Входящие — Почта", "Switcher.cs — Редактор", "Калькулятор", "dev.txt — Браузер", "Терминал", "Плейлист «В дорогу» — Музыка",
            "Заметки", "Бюджет — Таблицы", "Загрузки — Проводник", "Чат команды", "Календарь", "Отпуск 2026 — Фото", "Карты", "Настройки", "Фильм — Видео",
            "Диспетчер задач", "Словарь", "Погода", "Эскиз — Рисунок", "Доклад — Презентации", "Архив", "Переводчик", "Часы", "Справка"
        };
        static readonly double[] Aspects = { 1.6, 1.78, 1.45, 1, 1.78, 1.3, 2.1, 0.8, 1.6, 1.5 };

        static Color[] Tints
        {
            get { return new Color[] { Tokens.Palette.Blue, Tokens.Palette.Green, Tokens.Palette.Purple, Tokens.Palette.Orange, Tokens.Palette.Brand, Tokens.Palette.Pink, Tokens.Palette.Red, Tokens.Palette.Yellow }; }
        }

        public static List<WinItem> Windows(int count)
        {
            var list = new List<WinItem>();
            Color[] tints = Tints;
            for (int i = 0; i < count; i++)
            {
                string title = Titles[i % Titles.Length];
                int dash = title.LastIndexOf(" — ");
                string app = dash >= 0 ? title.Substring(dash + 3) : title;
                list.Add(new WinItem
                {
                    Hwnd = new IntPtr(1000 + i), Title = title, Key = app.ToLowerInvariant(), Fake = true, Minimized = i % 10 == 3,
                    Tint = tints[i % tints.Length], Aspect = Aspects[i % Aspects.Length]
                });
            }
            return list;
        }

        // Значок окна: настоящий, а если его нет — плитка с первой буквой
        public static FrameworkElement IconOf(WinItem w, double size)
        {
            if (w.Icon != null)
            {
                var img = new Image { Source = w.Icon, Width = size, Height = size, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
            string name = w.Fake ? w.Key : w.Title;
            string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            var t = new TextBlock { Text = letter, FontSize = Tokens.Size.Title, FontWeight = FontWeights.Bold, FontFamily = Ui.DisplayFont, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var tile = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(Tokens.Radius.Tip), Background = w.Fake ? Ui.Tinted(w.Tint) : Ui.Solid(Tokens.Dark.Raised), Child = t };
            return new Viewbox { Width = size, Height = size, Child = tile, Stretch = Stretch.Uniform };
        }

        // Условное «окно» вместо живого превью: полоса заголовка, боковая панель, строки. Пропорции — как у окна.
        public static FrameworkElement Window(WinItem w, double boxW, double boxH)
        {
            double x, y, ww, wh;
            Layout.Fit(w.Aspect * 100, 100, 0, 0, boxW, boxH, 100, out x, out y, out ww, out wh);
            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            body.RowDefinitions.Add(new RowDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.28, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.72, GridUnitType.Star) });
            var bar = new Border { Background = Ui.Fill2 };
            bar.Child = new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(2.5), Background = Ui.Solid(w.Tint), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumnSpan(bar, 2);
            body.Children.Add(bar);
            var side = new Border { Background = Ui.Solid(Ui.Alpha(w.Tint, 0.75)) };
            Grid.SetRow(side, 1);
            body.Children.Add(side);
            var lines = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
            double[] widths = { 0.7, 1, 0.85, 0.5, 0.9, 0.6 };
            for (int i = 0; i < widths.Length; i++)
                lines.Children.Add(new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = i == 0 ? Ui.Solid(Ui.Alpha(w.Tint, 0.9)) : Ui.Fill2, Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(8, (ww * 0.72 - 20) * widths[i]) });
            var content = new Border { Child = lines, ClipToBounds = true };
            Grid.SetRow(content, 1); Grid.SetColumn(content, 1);
            body.Children.Add(content);
            return new Border
            {
                Width = Math.Round(ww), Height = Math.Round(wh), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = Ui.Solid(Tokens.Dark.Raised), BorderBrush = Ui.Solid(Tokens.Dark.EdgeTop), BorderThickness = new Thickness(1), Child = body, ClipToBounds = true
            };
        }
    }
}
