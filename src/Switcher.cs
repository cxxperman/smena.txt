// smena.txt. Switcher.cs — сам переключатель: прозрачное окно на весь монитор, стеклянная панель по центру
// и на ней карточки окон — живое превью (его рисует сама Windows поверх нашего окна), значок и заголовок.
// Окно создаётся один раз при запуске и дальше только показывается и прячется; тяжёлого при показе нет:
// значки берутся из памяти, превью Windows отдаёт сразу.
// Чтобы ничего не мигало: затемнения экрана нет; окно показывается спрятанным (Win.Cloak) и открывается глазам только
// с готовой картинкой; превью ставятся сразу на свои места и вместе с карточками не ездят и не растут — панель только
// проявляется; покадровая работа идёт лишь пока что-то движется.
// Быстрый Alt+Tab окна не показывает вовсе: выбор сделан сразу, панель появляется, только если Alt всё ещё держат.
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
        public Grid Root, Slot;                 // Slot — место карточки в ряду
        public Border Box, Hover, IconHost;     // Box — место превью: ровная подложка, на ней — превью
        public Border BigIconHost;              // крупный значок вместо превью (свёрнутое окно); может не быть
        public TextBlock Title;
        public IntPtr Thumb;
        public Win.RECT Source;                 // видимая часть окна-источника
        public Win.RECT Dest, Part;             // где превью стоит на экране и какая часть окна в нём видна
        public bool Placed, InView;             // место сосчитано; превью не за краем панели
        public Win.RECT LastDest, LastSource;   // что уже сказано Windows
        public int LastOpacity = -1;
        public bool LastVisible;
    }

    class Switcher : Window
    {
        const double PanelPad = 14, Footer = 30, Shadow = 40;
        public const int RevealMs = 150;   // столько держат Alt после Tab, прежде чем панель появится; быстрее — переключение без панели
        public const string StickyHint = "Щёлкните окно или нажмите Enter  ·  Delete — закрыть окно  ·  Esc — отмена";

        readonly bool demo, headless;
        readonly Session session = new Session();
        readonly List<Card> cards = new List<Card>();
        List<WinItem> items = new List<WinItem>();
        bool sticky, snap;
        bool revealed;                      // панель построена и показана; false — окна только выбраны (быстрый Alt+Tab)
        bool awaiting;                      // окно показано, но спрятано от глаз, пока не нарисован первый кадр
        int gen;                            // номер переключения у KeyMachine, к которому относится этот показ
        int cols, rows, visibleRows, firstRow, frames;
        double preview, cardW, cardH, thumbH, panelW, panelH, viewW, viewH, scale = 1;
        Win.RECT mon;
        IntPtr hwnd;

        readonly Grid root = new Grid();
        readonly Border dim, shadow, glass, highlight, scrollMark;
        readonly Grid panelHost = new Grid(), glow = new Grid(), foot;
        readonly Canvas viewport = new Canvas { ClipToBounds = true }, strip = new Canvas(), deck = new Canvas();
        readonly TranslateTransform highlightAt = new TranslateTransform();
        readonly TextBlock hint;
        readonly DispatcherTimer altWatch, revealTimer, uncloakGuard;
        readonly Stopwatch clock = new Stopwatch();
        double hx, hy, hvx, hvy, tx, ty;   // подсветка: где она, скорость, куда едет
        double sy, svy, tsy;               // прокрутка рядов
        double last, fadeEnd;
        bool ticking;
        FlipView flip;                      // объёмные виды: своё окно сцены; логика показа остаётся здесь
        bool flipping;                      // этот показ идёт стопкой, а не сеткой
        readonly System.Diagnostics.Stopwatch revealClock = new System.Diagnostics.Stopwatch();   // от начала показа до порога
        bool revealWatch;                   // порог показа проверяется и в кадрах (RevealTick)

        KeyMachine machine;
        public KeyMachine Machine           // клавиши; может не быть (--demo, снимки)
        {
            get { return machine; }
            set { machine = value; if (Guard != null) Guard.Machine = value; }
        }
        public StallGuard Guard;            // сторож потока окна (Stall.cs); нет в --headless и в снимках
        int stale = -1;                     // действия перехватчика с таким номером переключения и раньше — из времени, когда поток окна висел
        bool opening;                       // показ командой собирается: сторож не трогает «открыто командой» у KeyMachine
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

            // Затемнения экрана нет: оно мигало при каждом Alt+Tab. Остался невидимый глазу слой на весь монитор
            // (совсем прозрачное место Windows отдаёт окну под нами): щелчок мимо панели — отмена
            dim = new Border { Background = Ui.Solid(Color.FromArgb(1, 0, 0, 0)) };
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
            // Подсказка про клавиши — только когда переключатель открыт командой: при Alt+Tab клавиши и так под пальцами
            hint = Ui.T("", Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            foot = new Grid { Height = Footer, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Layout.Margin + 2, 0, Layout.Margin + 2, 0), Visibility = Visibility.Collapsed };
            foot.Children.Add(hint);
            viewport.VerticalAlignment = VerticalAlignment.Top;
            viewport.HorizontalAlignment = HorizontalAlignment.Left;
            viewport.Children.Add(strip);
            scrollMark = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = Ui.Fill2, Visibility = Visibility.Collapsed };
            viewport.Children.Add(scrollMark);
            glass = new Border
            {
                CornerRadius = new CornerRadius(Tokens.Radius.Block), Background = Ui.CardFill, BorderBrush = Ui.Rim, BorderThickness = new Thickness(1),
                Padding = new Thickness(PanelPad - 1), Child = new Grid { Children = { viewport, foot } }, SnapsToDevicePixels = true
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
                // Как в Alt+Tab Windows 11: выбранное окно обведено светлой рамкой в 2 точки, под ней — лёгкая заливка
                CornerRadius = new CornerRadius(Tokens.Radius.Block), Background = Ui.Fill, BorderBrush = Ui.Label, BorderThickness = new Thickness(2),
                RenderTransform = highlightAt, IsHitTestVisible = false, SnapsToDevicePixels = true
            };
            Content = root;

            // Если Alt отпустили, а мы этого не увидели (экран блокировки, окно с правами администратора) — не висим, а переключаемся
            altWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            altWatch.Tick += delegate { if (session.Open && !sticky && !Win.KeyDown(KeyMachine.Alt)) Commit(); };

            // Панель появляется не сразу: быстрый Alt+Tab успевает закончиться раньше и обходится без неё
            revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RevealMs) };
            revealTimer.Tick += delegate { RevealDue(); };

            // Если кадры почему-то не идут, спрятанным окно не остаётся
            uncloakGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            uncloakGuard.Tick += delegate { uncloakGuard.Stop(); Uncloak(); };

            // Клавиши, когда окно само в фокусе (показ командой, без перехватчика)
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (!session.Open || !sticky) return;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                KeyAction a = e.Key == Key.Tab ? (shift ? KeyAction.Prev : KeyAction.Next) : e.Key == Key.Left ? KeyAction.Left : e.Key == Key.Right ? KeyAction.Right
                    : e.Key == Key.Up ? KeyAction.Up : e.Key == Key.Down ? KeyAction.Down : e.Key == Key.Escape ? KeyAction.Cancel : e.Key == Key.Enter ? KeyAction.Commit
                    : e.Key == Key.Delete ? KeyAction.Close : KeyAction.None;
                if (a == KeyAction.None) return;
                e.Handled = true;
                Handle(a, gen);
            };
            Deactivated += delegate { if (session.Open && sticky && IsVisible) Cancel(); };
        }

        // Создаёт окно Windows, не показывая его: к первому Alt+Tab всё готово
        public void Prepare()
        {
            hwnd = new WindowInteropHelper(this).EnsureHandle();
            Win.Style(hwnd, true);
            Win.NoTransitions(hwnd);
            var src = HwndSource.FromHwnd(hwnd);
            if (src != null && src.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
            if (scale <= 0) scale = 1;
            if (headless) return;
            // Окно объёмной сцены тоже готово заранее; Switcher — её хозяин: всё, что делают мышь и клавиши, решается здесь
            flip = new FlipView();
            flip.Prepare();
            // Сторож: если поток окна зависнет посреди показа, оба окна (сетка и сцена — во весь монитор, поверх всех)
            // прячутся без него, а Alt+Tab отдаётся Windows
            Guard = new StallGuard(Dispatcher) { Machine = machine, Busy = Busy, Recovered = Recover };
            Guard.Watch(hwnd);
            Guard.Watch(flip.Hwnd);
            flip.Picked = delegate(int at) { if (!session.Open || at < 0 || at >= items.Count) return; session.Selected = at; Commit(); };
            flip.CloseAsked = delegate(int at) { CloseCard(at); };
            flip.Step = delegate(int d) { Handle(d > 0 ? KeyAction.Next : KeyAction.Prev, gen); };
            flip.Cancelled = delegate { Cancel(); };
            flip.Keyed = delegate(KeyAction a) { Handle(a, gen); };
            flip.Failed = Abort;
            // Устройства видеокарты заводятся заранее, если объёмный вид уже выбран: первый Alt+Tab их не ждёт
            if (!demo && Prefs.Is3D(Prefs.View) && !Aside.On) WarmGpu();
            Prefs.Changed += delegate { if (!demo && Prefs.Is3D(Prefs.View) && !Aside.On) WarmGpu(); };
        }

        // Порог показа прошёл: Alt ещё держат — показ, отпустили — просто переключение
        void RevealDue()
        {
            revealTimer.Stop();
            StopRevealWatch();
            try
            {
                if (!session.Open || revealed) return;
                if (!sticky && !Win.KeyDown(KeyMachine.Alt)) Commit(); else Reveal();
            }
            catch { Abort(); throw; }
        }

        // Объёмный вид: порог показа проверяется ещё и в каждом кадре. Пока сцена готовится, кадры идут без перерыва,
        // и сообщение таймера Windows может опоздать на сотни мс (--flipprobe --real: 150 мс превращались в 490).
        void RevealTick(object s, EventArgs e) { if (revealClock.Elapsed.TotalMilliseconds >= RevealMs) RevealDue(); }

        void StopRevealWatch()
        {
            if (!revealWatch) return;
            CompositionTarget.Rendering -= RevealTick;
            revealWatch = false;
        }

        // Идёт ли показ — для сторожа (в потоке окна; не идёт — сторож засыпает). Заодно: KeyMachine считает, что
        // переключатель открыт командой, а показа нет, — такое состояние глотало бы Tab, Enter, Esc и стрелки во всей
        // системе (и Alt+Tab не работал бы совсем); оно заканчивается здесь.
        bool Busy()
        {
            if (opening) return true;
            bool shown = session.Open || revealed || awaiting || (flip != null && flip.Busy) || IsVisible;
            if (!shown && machine != null && machine.Sticky) machine.End();
            return shown || (machine != null && machine.Active);
        }

        // Поток окна висел дольше StallWatch.TripMs и очнулся. Сторож уже спрятал окна и пропускал клавиши мимо нас;
        // здесь — всё в исходное состояние, чтобы следующий Alt+Tab работал как обычно. Действия перехватчика,
        // скопившиеся в очереди за время зависания, устарели и не выполняются.
        void Recover(double silentMs)
        {
            stale = machine != null ? machine.Generation : -1;
            Abort();
            flipping = false; awaiting = false; revealed = false;
            if (machine != null) { machine.End(); machine.Resume(); }
        }

        // Есть ли захват на самом деле (с видеокартой); если не так, как думали, — страница настроек узнает
        bool WarmGpu()
        {
            bool ok = Capture.Available;
            if (ok) CaptureItems.Start();   // поток объектов захвата и фабрика — тоже заранее
            if (AppHub.FlipAvailable != ok) { AppHub.FlipAvailable = ok; AppHub.Publish(); }
            return ok;
        }

        // ───────── Действия: от клавиатуры (через KeyMachine), от мыши, от команды ─────────

        // g — номер переключения, к которому действие относится (его дал KeyMachine)
        public void Handle(KeyAction a, int g)
        {
            if (g <= stale) return;   // из очереди времён зависания: показ уже сброшен (Recover)
            try
            {
                if (a == KeyAction.Open || a == KeyAction.OpenBack || a == KeyAction.OpenSame)
                {
                    // Показ ещё открыт, а перехватчик начал новое переключение: дальше этот показ — его
                    if (session.Open) gen = g;
                    else if (!OpenView(false, a, g) && Machine != null) Machine.End(g);
                }
                else if ((a == KeyAction.Next || a == KeyAction.Prev) && !session.Open && Machine != null && Machine.Active && !Machine.Sticky)
                {
                    // Перехватчик уже считает, что переключение идёт, а показа нет (прошлый закрылся раньше времени):
                    // не глотаем Tab впустую — открываем показ
                    if (!OpenView(false, a == KeyAction.Prev ? KeyAction.OpenBack : KeyAction.Open, g)) Machine.End(g);
                }
                else if (a == KeyAction.Commit) Commit();
                else if (a == KeyAction.Cancel) Cancel();
                else if (a == KeyAction.Close) { if (session.Open) { if (!revealed) Reveal(); CloseCard(session.Selected); } }
                else if (session.Move(flipping ? Flat(a) : a))
                {
                    // Tab нажали ещё раз — человек выбирает, а не просто возвращается к прошлому окну: панель нужна сейчас
                    if (!revealed) Reveal(); else Select(true);
                }
            }
            catch { Abort(); throw; }
        }

        // В стопке один ряд: вверх и вниз — то же, что предыдущее и следующее окно
        static KeyAction Flat(KeyAction a)
        {
            return a == KeyAction.Up ? KeyAction.Prev : a == KeyAction.Down ? KeyAction.Next : a;
        }

        // Показ без клавиатуры: остаётся, пока не щёлкнут окно или не нажмут Esc. false — показывать нечего или уже показан.
        public bool OpenSticky()
        {
            if (session.Open || (Aside.On && !demo)) return false;   // в стороне (идёт игра) переключатель не показывается
            // Номер показа берётся до показа: если показ закроется, ещё не успев открыться (окно сразу потеряло фокус),
            // его «закончено» дойдёт до KeyMachine с тем же номером, и «открыто командой» не останется висеть
            int g = !headless && machine != null ? machine.BeginSticky() : 0;
            opening = true;
            try
            {
                if (!OpenView(true, KeyAction.Open, g)) { if (g != 0) machine.End(g); return false; }
                return true;
            }
            catch { if (g != 0) machine.End(g); Abort(); throw; }
            finally { opening = false; }
        }

        // Начало показа: окна перечислены, выбор сделан. Панель при Alt+Tab появляется чуть позже (Reveal), если Alt
        // ещё держат; отпустили раньше — Commit просто выводит выбранное окно вперёд, и на экране ничего не мелькает.
        bool OpenView(bool stay, KeyAction how, int g)
        {
            bool fgFirst = true;
            if (Aside.On && !demo) return false;
            // Прошлая стопка ещё доигрывает уход — обрываем его сразу, новый показ важнее
            if (flip != null && flip.Busy) flip.Hide();
            if (!demo && !headless) Marks.Begin(stay ? "sticky" : "alttab");
            items = demo ? Demo.Windows(DemoCount) : Win.List(Prefs.AllDesktops, true, out fgFirst);
            Marks.Hit("list");
            if (how == KeyAction.OpenSame) items = Logic.SameApp(items, fgFirst && items.Count > 0 ? items[0].Key : null);
            LastCount = items.Count;
            if (items.Count == 0) return false;
            if (headless) return true;   // проверки без экрана: окна сосчитаны, показывать нечего
            if (Guard != null) Guard.Arm();   // с этого мига поток окна сторожат

            sticky = stay;
            gen = g;
            revealed = false;
            firstRow = 0;
            mon = Win.ActiveMonitor();
            viewW = (mon.Right - mon.Left) / scale; viewH = (mon.Bottom - mon.Top) / scale;
            Measure(viewW, viewH);
            // Объёмный вид — если выбран, захват окон есть (в --demo окна выдуманные, захват не нужен) и среди окон нет видимой
            // игры: в объёмный вид попал бы кадр монитора с её картинкой (Logic.UseFlip). Иначе молча сетка
            flipping = flip != null && Logic.UseFlip(Prefs.Is3D(Prefs.View), items) && (demo || WarmGpu());
            if (flipping) cols = items.Count;
            session.Begin(items.Count, cols, fgFirst, how == KeyAction.OpenBack);
            if (flipping && !demo) { flip.Warm(mon, items, session.Selected); Marks.Hit("warm"); }   // кадр экрана и обои начинают захватываться сразу, ещё до показа
            if (sticky) Reveal();
            else
            {
                // Чтобы программа под нами не открыла меню, когда Alt отпустят. Игре своих нажатий не посылаем
                if (demo || !Win.GameAhead()) Win.SendMask();
                altWatch.Start();
                revealTimer.Start();
                if (flipping)
                {
                    revealClock.Restart();
                    if (!revealWatch) { CompositionTarget.Rendering += RevealTick; revealWatch = true; }
                }
            }
            return true;
        }

        // Строит панель и показывает её. Окно ставится на место и раскладывается ещё невидимым, показывается спрятанным
        // от глаз (Win.Cloak) и открывается, когда первый кадр готов: кадра от прошлого показа на экране не бывает.
        void Reveal()
        {
            revealTimer.Stop();
            StopRevealWatch();
            if (revealed || !session.Open) return;
            revealed = true;
            Marks.Hit("reveal");
            if (flipping) { flip.Open(items, session.Selected, mon, sticky, demo); return; }
            Build();
            foreach (Card c in cards)
            {
                if (c.Item.Minimized || c.Item.Fake) continue;
                // Окну игры миниатюры DWM не заводим: плашка со значком, как у окна без картинки
                if (!Logic.MayTouch(c.Item)) { NoPreview(c); continue; }
                c.Thumb = Win.ThumbRegister(hwnd, c.Item.Hwnd);
                if (c.Thumb != IntPtr.Zero && !Win.ThumbSource(c.Thumb, c.Item.Hwnd, out c.Source)) { Win.ThumbRelease(c.Thumb); c.Thumb = IntPtr.Zero; }
                if (c.Thumb == IntPtr.Zero) NoPreview(c);
            }

            snap = Ui.ReducedMotion;
            panelHost.BeginAnimation(OpacityProperty, null);
            panelHost.RenderTransform = null;
            panelHost.Opacity = snap ? 1 : 0;
            Win.Cloak(hwnd, true);
            awaiting = true;
            frames = 0;
            Left = mon.Left / scale; Top = mon.Top / scale; Width = viewW; Height = viewH;
            Win.Style(hwnd, !sticky);
            Win.Place(hwnd, mon);
            Select(false);
            root.UpdateLayout();
            Show();
            Win.Place(hwnd, mon);
            root.UpdateLayout();
            SyncThumbs(true);
            clock.Restart();
            last = 0; fadeEnd = 0;
            StartTicking();
            uncloakGuard.Start();
            if (sticky) Activate();
        }

        // Первый кадр нового показа готов — открываем окно глазам; панель проявляется (и только: ничего не едет и не растёт)
        void Uncloak()
        {
            uncloakGuard.Stop();
            if (!awaiting) return;
            awaiting = false;
            if (!StallGuard.Tripped) Win.Cloak(hwnd, false);   // сторож только что спрятал окно: сброс показа уже в очереди
            if (!snap)
            {
                panelHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Tokens.Motion.FastMs)) { EasingFunction = Ui.EaseOut });
                fadeEnd = clock.Elapsed.TotalSeconds + Tokens.Motion.FastMs / 1000.0 + 0.05;
                StartTicking();
            }
            SyncThumbs(false);
        }

        void Commit()
        {
            int chosen = session.Close();
            if (Machine != null) Machine.End(gen);
            if (chosen < 0) return;
            WinItem target = chosen < items.Count ? items[chosen] : null;
            if (!demo && !headless && target != null) Win.Activate(target.Hwnd);
            CloseView(target, true);
        }

        void Cancel()
        {
            bool was = session.Open;
            session.Close();
            if (Machine != null) Machine.End(gen);
            if (was) CloseView(null, true);
        }

        // Что-то пошло не так — на экране ничего не оставляем ни при каких условиях
        public void Abort()
        {
            try { session.Close(); if (Machine != null) Machine.End(gen); CloseView(null, false); } catch { }
            try { if (flip != null) flip.Hide(); } catch { }
        }

        // target — окно, в которое переключились (null — отмена); animate — стопке можно доиграть уход
        void CloseView(WinItem target, bool animate)
        {
            altWatch.Stop();
            revealTimer.Stop();
            StopRevealWatch();
            uncloakGuard.Stop();
            StopTicking();
            if (flipping)
            {
                // Логика закрыта сразу (session, Machine.End — как в сетке); сцена доигрывает уход и потом зовёт Finished
                flipping = false;
                awaiting = false;
                revealed = false;
                if (animate) flip.Close(target, Done);
                else { flip.Hide(); Done(); }
                return;
            }
            // Сначала прячем окно от глаз — панель и превью исчезают разом; спрятанным оно и остаётся до следующего показа
            if (hwnd != IntPtr.Zero && (revealed || IsVisible)) Win.Cloak(hwnd, true);
            awaiting = false;
            revealed = false;
            foreach (Card c in cards) { Win.ThumbRelease(c.Thumb); c.Thumb = IntPtr.Zero; }
            if (IsVisible) Hide();
            cards.Clear();
            deck.Children.Clear();
            strip.Children.Clear();
            Done();
        }

        void Done() { if (Finished != null) Finished(); }

        // Закрывает окно с карточки (Delete или щелчок колёсиком) и убирает карточку; показ продолжается
        void CloseCard(int at)
        {
            if (!session.Open || at < 0 || at >= items.Count) return;
            WinItem w = items[at];
            if (!w.Fake && !demo && !headless) Win.Close(w.Hwnd);
            items.RemoveAt(at);
            if (items.Count > 0) Measure(viewW, viewH);
            if (flipping) cols = Math.Max(1, items.Count);
            if (!session.Remove(at, cols))
            {
                // Окон не осталось
                if (Machine != null) Machine.End(gen);
                CloseView(null, true);
                return;
            }
            if (!revealed) return;
            if (flipping) { flip.Remove(at); flip.Select(session.Selected, true); return; }
            // Карточки строятся заново, а живые превью оставшихся окон остаются теми же — они не мигают
            var old = new List<Card>(cards);
            Build();
            foreach (Card c in cards)
            {
                Card was = old.Find(delegate(Card x) { return x.Item == c.Item; });
                if (was == null) continue;
                c.Thumb = was.Thumb; c.Source = was.Source;
                was.Thumb = IntPtr.Zero;
            }
            foreach (Card c in old) { Win.ThumbRelease(c.Thumb); c.Thumb = IntPtr.Zero; }
            foreach (Card c in cards) if (c.Thumb == IntPtr.Zero && !c.Item.Fake) NoPreview(c);
            Select(false);
        }

        // ───────── Раскладка ─────────

        // Считает раскладку для окон items на экране w×h: размер карточки, сколько колонок и рядов, размер панели.
        // Карточек не строит — это быстро, и этого хватает, чтобы выбрать окно.
        void Measure(double w, double h)
        {
            root.Width = w; root.Height = h;
            preview = Math.Max(Layout.MinPreview, Math.Min(Layout.MaxPreview, Prefs.Size));
            // Маленький экран: карточка не шире, чем он позволяет
            preview = Math.Min(preview, Math.Max(80, w * 0.9 - 2 * (PanelPad + Layout.Margin + Layout.Pad)));
            cardW = Layout.CardW(preview); cardH = Layout.CardH(preview); thumbH = Layout.ThumbH(preview);
            // Строка с подсказкой есть только при показе командой
            double footer = sticky ? Footer : 0, bottom = sticky ? PanelPad / 2 : PanelPad;
            Layout.Rows(items.Count, cardW, cardH, w * 0.92 - 2 * (PanelPad + Layout.Margin), h * 0.84 - PanelPad - bottom - footer - 2 * Layout.Margin, out cols, out rows, out visibleRows);
            firstRow = Math.Max(0, Math.Min(firstRow, rows - visibleRows));

            double vw = cols * cardW + (cols - 1) * Layout.Gap + 2 * Layout.Margin, vh = visibleRows * cardH + (visibleRows - 1) * Layout.Gap + 2 * Layout.Margin;
            hint.Text = sticky ? StickyHint : "";
            // Узкая панель: подсказке про клавиши нужно место
            double minW = sticky ? 2 * (Layout.Margin + 2) + hint.FontSize * 0.55 * hint.Text.Length : 0;
            panelW = Math.Max(vw, minW) + 2 * PanelPad; panelH = PanelPad + vh + footer + bottom;
            foot.Visibility = sticky ? Visibility.Visible : Visibility.Collapsed;
            glass.Padding = new Thickness(PanelPad - 1, PanelPad - 1, PanelPad - 1, bottom - 1);
            viewport.Width = vw; viewport.Height = vh;
            viewport.Margin = new Thickness((panelW - 2 * PanelPad - vw) / 2, 0, 0, 0);
            panelHost.Width = panelW; panelHost.Height = panelH;
        }

        // Строит карточки по сосчитанной раскладке
        void Build()
        {
            glow.Children.Clear();
            glow.Children.Add(Ui.Backdrop(panelW, panelH));
            glow.Clip = new RectangleGeometry(new Rect(0, 0, panelW, panelH), Tokens.Radius.Block, Tokens.Radius.Block);

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
                Card c = MakeCard(items[i]);
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
                scrollMark.Height = (viewport.Height - 2 * Layout.Margin) * visibleRows / rows;
                Canvas.SetLeft(scrollMark, viewport.Width - 5);
            }
        }

        Card MakeCard(WinItem item)
        {
            var c = new Card { Item = item };
            c.Hover = new Border { CornerRadius = new CornerRadius(Tokens.Radius.Row), Background = Brushes.Transparent };
            // Под каждым превью — ровная подложка: окно другой формы (узкое, широкое) стоит на ней по центру,
            // и карточки выглядят одинаково
            c.Box = new Border
            {
                Width = preview, Height = thumbH, Margin = new Thickness(Layout.Pad, Layout.Pad, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Background = Ui.Fill, CornerRadius = new CornerRadius(Tokens.Radius.Tip)
            };
            if (item.Fake && !item.Minimized) c.Box.Child = Demo.Window(item, preview, thumbH);

            c.IconHost = new Border { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            c.Title = Ui.T(item.Title, Tokens.Size.Body, Ui.Label2, FontWeights.Normal);
            c.Title.VerticalAlignment = VerticalAlignment.Center;
            var caption = new DockPanel { Height = Layout.Caption, Margin = new Thickness(Layout.Pad + 3, Layout.Pad + thumbH, Layout.Pad + 3, 0), VerticalAlignment = VerticalAlignment.Top, LastChildFill = true };
            DockPanel.SetDock(c.IconHost, System.Windows.Controls.Dock.Left);
            caption.Children.Add(c.IconHost);
            caption.Children.Add(c.Title);

            // Карточка не растёт и не ездит: на ней живое превью, а его Windows двигает отдельно от нашей картинки
            c.Root = new Grid { Width = cardW, Height = cardH, Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { c.Hover, c.Box, caption } };
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
            // Щелчок колёсиком закрывает окно, как на панели задач и во вкладках
            c.Root.MouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                try { CloseCard(cards.IndexOf(c)); } catch { Abort(); throw; }
            };
            if (item.Minimized) NoPreview(c);
            SetIcon(c, item.Fake ? null : item.Icon ?? Win.Icon(item, Dispatcher, IconReady));
            return c;
        }

        // Вместо превью — крупный значок на той же подложке: окно свёрнуто или Windows не дала его картинку
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
            c.Box.Child = col;
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

        // Приводит картинку к выбранному окну: рамка подсветки и прокрутка рядов. Сама карточка остаётся как была.
        void Select(bool animate)
        {
            int sel = session.Selected;
            if (flipping) { if (revealed) flip.Select(sel, animate); return; }
            for (int i = 0; i < cards.Count; i++)
            {
                bool on = i == sel;
                Card c = cards[i];
                c.Title.Foreground = on ? Ui.Label : Ui.Label2;
                if (on) c.Hover.Background = Brushes.Transparent;
            }
            firstRow = Layout.Scroll(firstRow, sel / cols, visibleRows, rows);
            tx = (sel % cols) * (cardW + Layout.Gap) - Layout.Grow;
            ty = (sel / cols) * (cardH + Layout.Gap) - Layout.Grow;
            tsy = firstRow * (cardH + Layout.Gap);
            if (animate && !snap)
            {
                // Подсветка и ряды доедут пружиной; кадры нужны, только пока они едут
                if (hx != tx || hy != ty || sy != tsy) StartTicking();
                return;
            }
            hx = tx; hy = ty; sy = tsy; hvx = hvy = svy = 0;
            Place();
            if (revealed && IsVisible)
            {
                root.UpdateLayout();
                SyncThumbs(true);
            }
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

        void StartTicking()
        {
            if (ticking) return;
            last = clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += Frame;
            ticking = true;
        }

        void StopTicking()
        {
            if (!ticking) return;
            CompositionTarget.Rendering -= Frame;
            ticking = false;
        }

        // Кадр. Вызывается, только пока что-то меняется: окно ждёт первого кадра, панель проявляется, едет подсветка
        // или ряды. Всё встало — подписка снимается, и переключатель на экране не стоит процессору ничего.
        void Frame(object sender, EventArgs e)
        {
            try
            {
                double now = clock.Elapsed.TotalSeconds, dt = now - last;
                last = now;
                // Первый кадр нового показа уже нарисован — окно можно открывать
                if (awaiting && ++frames >= 2) Uncloak();
                bool moving = hx != tx || hy != ty || sy != tsy, scrolling = sy != tsy;
                if (moving)
                {
                    Layout.Spring(ref hx, ref hvx, tx, Tokens.Motion.SpringFadeStiffness, Tokens.Motion.SpringFadeDamping, dt);
                    Layout.Spring(ref hy, ref hvy, ty, Tokens.Motion.SpringFadeStiffness, Tokens.Motion.SpringFadeDamping, dt);
                    Layout.Spring(ref sy, ref svy, tsy, Tokens.Motion.SpringShapeStiffness, Tokens.Motion.SpringShapeDamping, dt);
                    if (Math.Abs(hx - tx) < 0.3 && Math.Abs(hvx) < 2) { hx = tx; hvx = 0; }
                    if (Math.Abs(hy - ty) < 0.3 && Math.Abs(hvy) < 2) { hy = ty; hvy = 0; }
                    if (Math.Abs(sy - tsy) < 0.3 && Math.Abs(svy) < 2) { sy = tsy; svy = 0; }
                    Place();
                    if (scrolling) strip.UpdateLayout();
                }
                // Места превью пересчитываются, только когда едут ряды; иначе меняется разве что прозрачность
                SyncThumbs(scrolling);
                if (!moving && !awaiting && now >= fadeEnd) StopTicking();
            }
            catch { Abort(); }
        }

        // Говорит Windows, где стоит каждое живое превью и насколько оно прозрачно (как панель). Windows рисует превью сама,
        // поверх нашего окна. geometry — пересчитать места (после раскладки или прокрутки рядов); иначе берутся сосчитанные.
        void SyncThumbs(bool geometry)
        {
            if (hwnd == IntPtr.Zero || cards.Count == 0) return;
            int opacity = (int)Math.Round(255 * Math.Max(0, Math.Min(1, panelHost.Opacity)));
            Rect vp = new Rect();
            bool measured = false;
            foreach (Card c in cards)
            {
                if (c.Thumb == IntPtr.Zero) continue;
                if (geometry || !c.Placed)
                {
                    if (!measured) { vp = viewport.TransformToAncestor(root).TransformBounds(new Rect(0, 0, viewport.Width, viewport.Height)); measured = true; }
                    Rect b = c.Box.TransformToAncestor(root).TransformBounds(new Rect(0, 0, preview, thumbH));
                    double x, y, w, h;
                    Layout.Fit(c.Source.Right - c.Source.Left, c.Source.Bottom - c.Source.Top, b.X * scale, b.Y * scale, b.Width * scale, b.Height * scale, 1, out x, out y, out w, out h);
                    double dl = x, dt = y, dr = x + w, db = y + h, sl = c.Source.Left, st = c.Source.Top, sr = c.Source.Right, sb = c.Source.Bottom;
                    c.InView = Layout.Clip(ref dl, ref dt, ref dr, ref db, ref sl, ref st, ref sr, ref sb, vp.Left * scale, vp.Top * scale, vp.Right * scale, vp.Bottom * scale);
                    c.Dest = new Win.RECT { Left = (int)Math.Round(dl), Top = (int)Math.Round(dt), Right = (int)Math.Round(dr), Bottom = (int)Math.Round(db) };
                    c.Part = new Win.RECT { Left = (int)Math.Round(sl), Top = (int)Math.Round(st), Right = (int)Math.Round(sr), Bottom = (int)Math.Round(sb) };
                    c.Placed = true;
                }
                bool visible = c.InView && opacity > 0;
                if (visible == c.LastVisible && opacity == c.LastOpacity && Same(c.Dest, c.LastDest) && Same(c.Part, c.LastSource)) continue;
                Win.ThumbUpdate(c.Thumb, c.Dest, c.Part, (byte)opacity, visible);
                c.LastVisible = visible; c.LastOpacity = opacity; c.LastDest = c.Dest; c.LastSource = c.Part;
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
            firstRow = 0;
            Measure(w, h);
            Build();
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
                WinItem w = current.Find(delegate(WinItem x) { return !x.Minimized && !x.Game; }) ?? current.Find(delegate(WinItem x) { return !x.Game; });
                IntPtr t = w == null ? IntPtr.Zero : Win.ThumbRegister(hwnd, w.Hwnd);
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
