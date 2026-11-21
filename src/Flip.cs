// smena.txt. Flip.cs — объёмные виды переключателя: «Стопка» (как Flip 3D в Windows Vista), «Каскад», «Обложки» и другие
// (Flip.Views). На весь монитор размытый затемнённый фон, по центру — живые окна в перспективе, выбранное главное,
// под ними — значок и заголовок. Раскладки отличаются только местом карточек; всё остальное здесь общее.
// Окно сцены — своё, непрозрачное (прозрачное окно на весь экран WPF рисует через процессор, 3D с живыми картинками
// ему не по силам). Логикой по-прежнему владеет Switcher: список окон, выбор, клавиши, переключение. Здесь — только показ.
// Картинки окон — Windows.Graphics.Capture (Capture.cs): обычные ImageSource, их можно положить на грань в Viewport3D.
// Появление: один кадр монитора (наше окно в этот момент спрятано и в кадр не попадает) — и сцена в первый миг
// выглядит ровно как экран под ней; затем фон перетекает в размытые обои, а окна летят со своих мест в стопку.
// Захваты окон заводятся ещё до показа (Warm, по одному за кадр); окно, чьей живой картинки пока нет, летит с куском
// кадра монитора на своём месте. Полёт идёт по времени (Flip.Tick, Flip.Share): одна длительность на любой частоте
// монитора, подвисший кадр не съедает движение. Сразу после раскрытия сцена стоит, пока кадры не пойдут ровно (Flip.Go),
// движение — с места без рывка и с мягким оседанием (Flip.Glide); старт по глубине сдвинут плавно (Flip.Delay), садятся
// все разом. Стопка встаёт так, как окна лежат на экране (Flip.StartPos), и выбор доезжает до выбранного.
// Раскладки — чистые функции Flip в Model.cs (Flip.Place).
// Свёрнутые окна живой картинки не дают: у них на грани последняя картинка окна из миниатюры DWM (Iconic.cs), снятая
// до показа и запомненная, пока окно свёрнуто; летать им неоткуда — они проявляются на месте. Не вышло — плашка со значком.
// Выбранное свёрнутое при уходе летит туда, где окно встанет после разворачивания (Win.RestorePlace, Restore в Model.cs),
// и сцена ждёт, пока окно под ней развернётся; его живой захват подменяет неподвижную картинку уже в полёте.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DevTxt.Smena
{
    // Одна карточка стопки
    class FlipCard
    {
        public WinItem Item;
        public int Index;                   // место в списке окон
        public LiveWindow Live;             // живая картинка; может не быть
        public bool CanLive;                // окно настоящее и не свёрнуто
        public bool Dead;                   // захват не завёлся или умер: только плашка
        public bool ShowsLive;              // на грани живая картинка (иначе плашка или рисунок)
        public bool Cropped;                // пока живой нет: на грани окно, вырезанное из кадра монитора
        public bool Still;                  // свёрнутое окно: на грани его картинка из миниатюры DWM (Iconic.cs)
        public Win.RECT Rect;               // где окно на экране (в точках экрана), если HasHome
        public bool GotFrame;               // хоть один кадр пришёл (для --flipprobe)
        public double Aspect, FrontW, FrontH;
        public bool HasHome, FlyHome;       // есть место на этом мониторе; при появлении летит оттуда
        public Pose Home;
        // Свёрнутое окно, которое после разворачивания встанет на этом мониторе (Win.RestorePlace): где (видимая рамка,
        // в точках экрана), развернётся ли на весь экран и поза там — плоско, верхней (оно станет верхним окном)
        public bool Restore, RestoreMax;
        public Win.RECT RestoreRect;
        public Pose RestoreHome;
        public Pose Now;                    // где карточка стоит сейчас — с этого места начинается уход
        public double Delay;                // задержка старта при появлении, мс (лесенка)
        public ImageBrush Brush;
        public ModelVisual3D Visual;
        public ScaleTransform3D Size;
        public AxisAngleRotation3D Roll, Turn, Tilt;   // вокруг z, y и x — в таком порядке (как Pose)
        public TranslateTransform3D Move;
        public double Opacity;              // что сейчас на экране
        public double Z;
        public double Key;                  // порядок отрисовки при равной глубине: больше — выше
        public int ZRank;                   // место окна в настоящем порядке окон на экране: больше — выше (дома карточки лежат так же)
    }

    class FlipView : Window
    {
        const double Blur = 40;             // радиус размытия фона, в точках
        const double BlurCacheScale = 0.5;  // готовое размытие хранится в половину разрешения экрана: деталей в нём нет
        const int OpenAtOnce = 1;           // столько живых картинок заводится за раз (при показе, в кадре, при смене выбора)
        const double PlaqueW = 480;         // плашка (значок вместо картинки) рисуется такой ширины и растягивается на грань

        enum Phase { Idle, Shown, Leaving }

        public Action<int> Picked;          // щёлкнули карточку: выбрать её окно и переключиться
        public Action<int> CloseAsked;      // щелчок колёсиком по карточке: закрыть окно
        public Action<int> Step;            // колёсико мыши: +1 — следующее окно, −1 — предыдущее
        public Action Cancelled;            // щелчок мимо карточек или окно потеряло фокус при показе командой
        public Action<KeyAction> Keyed;     // клавиши, когда окно само в фокусе (показ командой)
        public Action Failed;               // ошибка в кадре или в обработчике: переключатель убирает всё
        public bool StayCloaked;            // --flipprobe: окно всё время остаётся спрятанным от глаз

        // Замеры для --flipprobe
        public int SceneFrames, LiveOpened, LiveDied, Errors, Flew, Homes;
        public double FrameSum, FrameWorst, OpenMs;
        public string MonitorNote = "не запрашивался", DesktopNote = "не запрашивался", PathNote = "";
        // Картинки свёрнутых окон за показ: снято сейчас (путь А — миниатюра DWM), взято из памяти, не вышло (и почему)
        public int StillShot, StillCached, StillFailed;
        public double StillMsMax;
        public string StillWhy = "";
        public bool MonitorGot, DesktopGot;
        // Замеры показа (Marks: --flipprobe и SMENA_TRACE): доля полёта на первом видимом кадре, сколько кадров шёл полёт,
        // худший промежуток между кадрами и худшая работа в кадре за первую секунду после показа; то же для ухода
        public double FirstT = -1, GapMax, WorkMax, LeaveFirstT = -1;
        public int FlightFrames, LeaveFrames, IdleFrames;
        public double IdleWorkMax;
        double seenAt = -1, seenShown, placedT;
        bool placedFlight;
        // Шаг кадров за показ (Marks): промежутки по времени показа кадра — при появлении, листании и уходе (после первого
        // видимого кадра); повторные вызовы Rendering; работа обработчика по частям (сумма и худшая, мс); кадры WGC,
        // принятые картинками окон за кадр; сборки мусора за показ
        public readonly Gaps OpenGaps = new Gaps(4096), BrowseGaps = new Gaps(16384), LeaveGaps = new Gaps(4096);
        public int Repeats, Counted, WgcSum, WgcMax, Gc0, Gc1, Gc2;
        public double PullSum, PullMax, BgSum, BgMax, PlaceSum, PlaceMax, WorkSum, WorkTop;
        int gc0, gc1, gc2;
        public int AfterWgc, LateAfterWgc, AfterNone, LateAfterNone;
        public double WorstGap, WorstAt;     // худший промежуток в движении и когда он был (мс после первого видимого кадра)
        public string WorstPhase = "";
        string lateTimes = "";
        int wgcBefore;
        // Уход к свёрнутому окну (для --flipprobe и --film): летела ли карточка на его будущее место, сколько кадров;
        // когда (мс ухода) окно развернулось под сценой, когда завёлся и пришёл его живой захват, сколько сцена ждала
        // после конца ухода; предсказанное и настоящее место, их расхождение и расхождение конечной позы карточки
        // с настоящей видимой рамкой (в точках экрана; −1 — не мерили)
        public bool RestoreFlew;
        public int RestoreFrames, RestoreMiss = -1, RestoreEndMiss = -1;
        public double RestoreKnownMs = -1, RestoreOpenMs = -1, RestoreLiveMs = -1, RestoreWaitedMs = -1;
        public Win.RECT RestoreGuess, RestoreReal;
        FlipCard restoring;                  // карточка свёрнутого окна, которая летит на его будущее место; null — нет такой
        double knownAt = -1;                 // на какой мс ухода окно развернулось (−1 — ещё нет)
        bool missed;                         // развернулось не там, где ждали: карточка доходит до настоящего места
        Pose realHome;
        double animMs;                       // анимация разворачивания DWM, которую сцена закрывает (Restore.AnimMs или 0)
        public int Reordered, AfterReorder, LateAfterReorder;   // сколько раз менялся состав или порядок карточек в Viewport3D
        int reorderedSeen;
        bool reorderedBefore;
        bool movedBefore, springing;
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        readonly Grid root = new Grid { ClipToBounds = true };
        readonly Grid fallback = new Grid(), layerB = new Grid();
        readonly Rectangle layerA = new Rectangle(), blurred = new Rectangle();
        readonly ImageBrush brushA = new ImageBrush { Stretch = Stretch.Fill }, brushB = new ImageBrush { Stretch = Stretch.Fill };
        readonly Viewport3D viewport = new Viewport3D { IsHitTestVisible = true };
        readonly ModelVisual3D light = new ModelVisual3D { Content = new AmbientLight(Colors.White) };
        readonly PerspectiveCamera camera = new PerspectiveCamera { LookDirection = new Vector3D(0, 0, -1), UpDirection = new Vector3D(0, 1, 0), FieldOfView = Flip.Fov, NearPlaneDistance = 1 };
        readonly StackPanel caption, title;
        readonly Border iconHost;
        readonly TextBlock name, hint, minimized;
        readonly List<FlipCard> cards = new List<FlipCard>();
        readonly List<FlipCard> drawn = new List<FlipCard>();
        readonly DispatcherTimer uncloakGuard, leaveGuard;
        readonly Stopwatch clock = new Stopwatch();
        // Уход идёт в кадрах. Если кадры перестали приходить (упал поток отрисовки WPF, пропала видеокарта), сцена
        // не должна остаться на экране поверх всего — через столько она убирается сразу. Это страховка, а не оформление:
        // уход длится Flip.LeaveMs, то есть доли секунды.
        const int LeaveLimitMs = 1500;

        Phase phase = Phase.Idle;
        string view = "flip";               // раскладка этого показа (Flip.Views); берётся из настроек при показе
        IntPtr hwnd;
        double scale = 1, w, h, pos, vel, target;
        int selected;
        bool sticky, snap, awaiting, ticking, appearing, leaving;
        int frames;
        double lastFrame;
        double shownAt = -1;                 // время показа прошлого кадра (RenderingTime), мс; −1 — кадров ещё не было
        double typical = Pace.MaxStep;       // обычный шаг кадра, с (Pace.Typical): первый шаг движения не длиннее его
        bool[] pickVisible = new bool[0], pickFresh = new bool[0], pick = new bool[0];   // Pace.LivePick, без выделений в кадре
        int liveCursor;
        int leaveTo = -1;                    // к какой карточке уходим (−1 — отмена: все по домам)
        bool leaveCommit;
        Action done;                         // позвать, когда сцена убрана
        Win.RECT mon;
        LiveWindow monLive, deskLive;
        ImageSource monImage;                // кадр монитора на слое A (у --film — нарисованный) и его размер в пикселях
        int monW, monH;
        readonly Dictionary<IntPtr, LiveWindow> early = new Dictionary<IntPtr, LiveWindow>();   // живые картинки, заведённые в Warm
        readonly List<WinItem> earlyQueue = new List<WinItem>();                               // кому ещё завести, по порядку
        readonly List<IconicShot> shots = new List<IconicShot>();                              // свёрнутые окна, чьи картинки снимаются
        readonly List<WinItem> shotQueue = new List<WinItem>();                                // чьи ещё снять, по порядку
        bool shotTurn;                       // до показа живые картинки и снимки свёрнутых заводятся через кадр
        double elapsed;                      // сколько миллисекунд появления или ухода прошло (Flip.Tick)
        bool started;                        // появление пошло (до этого сцена стоит неподвижно — Flip.Go)
        bool gliding;                        // выбор едет вместе с появлением (Flip.Glide), а не пружиной
        double posFrom;                      // откуда он едет
        double holdFrom = -1;                // время показа первого видимого кадра, мс; −1 — ещё не было
        double frameShown;                   // время показа текущего кадра, мс
        int leaveHeld;                       // сколько кадров уход уже стоит в конечном положении (LeaveOver)
        const int LeaveHold = 2;             // столько кадров конечного положения ухода попадает на экран до того, как сцена уберётся
        // Замеры показа: через сколько мс после первого видимого кадра пошло движение и худший промежуток до этого;
        // сколько кадров опоздало уже во время движения появления и когда первый из них (мс после первого видимого)
        public double GoAfter = -1, HoldGapMax, FirstLateAt = -1;
        public int LateMoving;
        double openEnd, leaveEnd;            // когда появление и уход заканчиваются, мс
        double bgFrom;                       // непрозрачность фона в начале ухода
        bool deskWanted;                     // обои ещё не начали захватываться (Warm откладывает их на кадр; объект захвата готовится)
        bool monWanted;                      // кадр монитора ещё не начал захватываться: объект захвата готовится
        Win.RECT deskRect;
        bool deskShown;
        FlipCard pressed;

        public bool Busy { get { return phase != Phase.Idle; } }
        public IntPtr Hwnd { get { return hwnd; } }   // для сторожа потока окна (StallGuard)

        public FlipView()
        {
            Title = Program.Id + ".txt";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = Ui.TextFont;
            Background = Ui.Bg;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            // Слои снизу вверх: запасной фон dev.txt; кадр экрана как есть (A); размытые затемнённые обои (B); стопка; подпись
            root.Background = Ui.Bg;
            fallback.Background = Ui.Bg;
            fallback.Children.Add(Ui.Backdrop(0, 0));
            root.Children.Add(fallback);
            layerA.Fill = brushA;
            layerA.Visibility = Visibility.Collapsed;
            root.Children.Add(layerA);
            // Размытый край тянет прозрачность снаружи — слой чуть больше экрана, лишнее обрезано
            blurred.Fill = brushB;
            blurred.Margin = new Thickness(-Blur);
            blurred.Effect = new BlurEffect { Radius = Blur, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
            // Размытие во весь экран дорогое: около 3 мс работы видеокарты на каждый кадр при 3440×1440 (--flipprobe),
            // больше половины кадра на 180 Гц. Поэтому оно считается один раз: слой с размытием кэшируется готовой
            // картинкой (BitmapCache на родителе — в кэш попадает и эффект), а проявление и угасание — только прозрачность
            // слоя B поверх этой картинки. Пересчёт — лишь когда меняется сама картинка фона (кадр монитора или обои).
            var blurHost = new Grid();
            blurHost.CacheMode = new BitmapCache(BlurCacheScale);
            blurHost.Children.Add(blurred);
            layerB.Children.Add(blurHost);
            // Затемнение: цвет фона dev.txt с прозрачностью третьего уровня текста
            layerB.Children.Add(new Border { Background = Ui.Solid(Color.FromArgb(Tokens.Dark.Label3.A, Tokens.Dark.Bg.R, Tokens.Dark.Bg.G, Tokens.Dark.Bg.B)) }); // audit:ok — тёмная сцена поверх экрана, от темы Windows не зависит
            layerB.Visibility = Visibility.Collapsed;
            root.Children.Add(layerB);

            viewport.Camera = camera;
            viewport.Children.Add(light);
            root.Children.Add(viewport);

            iconHost = new Border { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            name = Ui.T("", Tokens.Size.Body, Ui.Label, FontWeights.Normal);
            name.VerticalAlignment = VerticalAlignment.Center;
            // У свёрнутого окна с картинкой пометка — тихой подписью рядом с заголовком (у плашки она своя, на плашке)
            minimized = Ui.T("Свёрнуто", Tokens.Size.Body, Ui.Label3, FontWeights.Normal);
            minimized.VerticalAlignment = VerticalAlignment.Center;
            minimized.Margin = new Thickness(8, 0, 0, 0);
            minimized.Visibility = Visibility.Collapsed;
            title = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { iconHost, name, minimized } };
            hint = Ui.T(Switcher.StickyHint, Tokens.Size.Micro, Ui.Label3, FontWeights.Normal);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Margin = new Thickness(0, 10, 0, 0);
            caption = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Children = { title, hint } };
            root.Children.Add(caption);
            Content = root;

            root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Guard(delegate { Down(e); }); };
            root.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { Guard(delegate { Up(e); }); };
            root.MouseDown += delegate(object s, MouseButtonEventArgs e) { Guard(delegate { Middle(e); }); };
            root.MouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                if (phase != Phase.Shown || e.Delta == 0) return;
                e.Handled = true;
                Guard(delegate { if (Step != null) Step(e.Delta < 0 ? 1 : -1); });
            };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (phase != Phase.Shown || !sticky) return;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                KeyAction a = e.Key == Key.Tab ? (shift ? KeyAction.Prev : KeyAction.Next) : e.Key == Key.Left ? KeyAction.Left : e.Key == Key.Right ? KeyAction.Right
                    : e.Key == Key.Up ? KeyAction.Up : e.Key == Key.Down ? KeyAction.Down : e.Key == Key.Escape ? KeyAction.Cancel : e.Key == Key.Enter ? KeyAction.Commit
                    : e.Key == Key.Delete ? KeyAction.Close : KeyAction.None;
                if (a == KeyAction.None) return;
                e.Handled = true;
                Guard(delegate { if (Keyed != null) Keyed(a); });
            };
            Deactivated += delegate { if (phase == Phase.Shown && sticky && IsVisible) Guard(delegate { if (Cancelled != null) Cancelled(); }); };

            // Если кадры почему-то не идут, спрятанным окно не остаётся
            uncloakGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            uncloakGuard.Tick += delegate { uncloakGuard.Stop(); Guard(Uncloak); };
            leaveGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LeaveLimitMs) };
            leaveGuard.Tick += delegate { leaveGuard.Stop(); if (phase == Phase.Leaving) Guard(Hide); };
        }

        // Создаёт окно Windows, не показывая его
        public void Prepare()
        {
            hwnd = new WindowInteropHelper(this).EnsureHandle();
            Win.Style(hwnd, true);
            Win.NoTransitions(hwnd);
            var src = HwndSource.FromHwnd(hwnd);
            if (src != null && src.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
            if (scale <= 0) scale = 1;
        }

        // Обработчики мыши, клавиш и таймеров: ошибка — переключатель убирает всё, ошибка уходит в журнал
        void Guard(Action a)
        {
            try { a(); }
            catch { Errors++; Fail(); throw; }
        }

        void Fail()
        {
            try { if (Failed != null) Failed(); else Hide(); } catch { }
        }

        // ───────── Подготовка, показ, уход ─────────

        // Начало переключения (ещё до показа): кадр монитора и обои начинают захватываться сразу, чтобы успеть к показу.
        // Если прошлая сцена ещё доигрывает уход — он обрывается сразу.
        // Оба фона идут запасным путём, через память (WriteableBitmap): через D3DImage большая картинка в плоском слое
        // роняла поток отрисовки WPF (UCEERR_RENDERTHREADFAILURE, --flipprobe). Кадр монитора берётся один раз,
        // обои меняются редко; копия идёт без ожидания видеокарты (LiveWindow.Pull), поток окна её не ждёт.
        // Живые картинки окон тоже заводятся заранее, до показа — по одной за кадр (открытие захвата — около кадра работы),
        // ближайшие к выбранному первыми: к порогу показа у окон уже есть кадры, и сцена не ждёт их и не стоит на месте.
        public void Warm(Win.RECT monitor, List<WinItem> items, int selectedIndex)
        {
            Hide();
            mon = monitor;
            if (!Capture.Available) { MonitorNote = DesktopNote = "нет захвата: " + Capture.Problem; return; }
            monWanted = true;
            OpenMonitorNow();
            // Обои — со следующего кадра: за один раз поток окна делает одно тяжёлое дело
            deskWanted = true;
            DesktopNote = "ждёт кадра";
            if (items != null && items.Count > 0)
            {
                view = Prefs.Is3D(Prefs.View) ? Prefs.View : "flip";
                int n = items.Count;
                selected = Math.Max(0, Math.Min(selectedIndex, n - 1));
                var order = new List<int>();
                for (int i = 0; i < n; i++) order.Add(i);
                order.Sort(delegate(int a, int b) { return Rank(a, n).CompareTo(Rank(b, n)); });
                foreach (int i in order)
                    if (earlyQueue.Count < Flip.MaxLive && !items[i].Fake && !items[i].Minimized && Logic.MayTouch(items[i])) earlyQueue.Add(items[i]);
                StillShot = 0; StillCached = 0; StillFailed = 0; StillMsMax = 0; StillWhy = "";
                QueueStills(items);
            }
            StartTicking();
        }

        // Свёрнутые окна без запомненной картинки — в очередь на снимок миниатюры DWM (Iconic.cs). Запомненные картинки
        // окон, которых больше нет или которые уже не свёрнуты, забываются.
        void QueueStills(List<WinItem> items)
        {
            Stills.Keep(items);
            foreach (WinItem it in items)
            {
                if (!it.Minimized || it.Fake || !Logic.MayTouch(it) || Stills.Get(it.Hwnd) != null || shotQueue.Count + shots.Count >= Still.CacheCount) continue;
                IntPtr h = it.Hwnd;
                if (!shotQueue.Exists(delegate(WinItem q) { return q.Hwnd == h; }) && !shots.Exists(delegate(IconicShot s) { return s.Source == h; })) shotQueue.Add(it);
            }
        }

        // Снимки свёрнутых окон: open — завести одно новое; у начатых — кадры. Готовая картинка — в память и на карточку.
        // true — у карточки сменились пропорции.
        bool StepStills(bool open)
        {
            bool changed = false, shown = false;
            if (open && shotQueue.Count > 0)
            {
                WinItem it = shotQueue[0];
                shotQueue.RemoveAt(0);
                string why;
                IconicShot s = IconicShot.Open(it.Hwnd, mon, out why);
                if (s != null) shots.Add(s);
                else { StillFailed++; StillWhy = why; }
            }
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                IconicShot s = shots[i];
                if (s.Pull())
                {
                    Stills.Put(s.Source, s.Image);
                    StillShot++;
                    if (s.Ms > StillMsMax) StillMsMax = s.Ms;
                    foreach (FlipCard c in cards)
                        if (c.Item.Hwnd == s.Source && c.Item.Minimized && !c.ShowsLive) { shown = true; if (ShowStill(c, s.Image)) changed = true; }
                }
                if (!s.Done) continue;
                if (s.Image == null) { StillFailed++; StillWhy = s.Why; }
                s.Dispose();
                shots.RemoveAt(i);
            }
            if (shown) Caption();
            return changed;
        }

        void DropStills()
        {
            foreach (IconicShot s in shots) s.Dispose();
            shots.Clear();
            shotQueue.Clear();
        }

        // До показа: одно новое окно за кадр, у заведённых — свежие кадры. Объект захвата, который ещё готовится
        // в своём потоке (CaptureItems.Later), спрашивается снова в следующих кадрах.
        void WarmStep()
        {
            // Обои: первая попытка — единственное дело этого кадра; повторные почти ничего не стоят
            if (deskWanted) { bool first = DesktopNote != CaptureItems.Later; OpenDesktopNow(); if (first) return; }
            // Снимок свёрнутого окна — через кадр с живой картинкой (или каждый кадр, когда живые все заведены)
            bool still = shotQueue.Count > 0 && (earlyQueue.Count == 0 || (shotTurn = !shotTurn));
            StepStills(still);
            if (!still && earlyQueue.Count > 0)
            {
                WinItem it = earlyQueue[0];
                earlyQueue.RemoveAt(0);
                string why = "";
                LiveWindow l = early.ContainsKey(it.Hwnd) ? null : LiveWindow.Open(it.Hwnd, false, true, false, LiveWindow.QuickWait, out why);
                if (l != null) { early[it.Hwnd] = l; LiveOpened++; }
                else if (why == CaptureItems.Later) earlyQueue.Add(it);
            }
            foreach (LiveWindow l in early.Values) l.Pull();
        }

        // Кадр монитора: захват заводится, как только готов объект захвата (иначе — снова в следующем кадре)
        void OpenMonitorNow()
        {
            string why;
            monLive = LiveWindow.OpenMonitor(Monitor(mon), LiveWindow.QuickWait, out why);
            monWanted = monLive == null && why == CaptureItems.Later;
            MonitorNote = monLive != null ? "ждёт кадра" : monWanted ? CaptureItems.Later : "не завёлся: " + why;
        }

        void OpenDesktopNow()
        {
            string why;
            deskLive = OpenDesktop(out why);
            deskWanted = deskLive == null && why == CaptureItems.Later;
            DesktopNote = deskLive != null ? "ждёт кадра" : deskWanted ? CaptureItems.Later : "не завёлся: " + why;
        }

        void DropEarly()
        {
            foreach (LiveWindow l in early.Values) l.Dispose();
            early.Clear();
            earlyQueue.Clear();
        }

        // Запомненная картинка свёрнутого окна — на грань, пропорции карточки по ней. true — пропорции изменились.
        bool ShowStill(FlipCard c, BitmapSource img)
        {
            Paint(c, img);
            c.Still = true;
            c.Cropped = false;
            double a = img.PixelWidth / (double)Math.Max(1, img.PixelHeight);
            if (Math.Abs(a - c.Aspect) <= 0.01) return false;
            c.Aspect = a;
            Flip.Front(view, a, w, h, out c.FrontW, out c.FrontH);
            return true;
        }

        // Показ: items — окна по порядку, selected — выбранное, monitor — где показывать (в точках экрана),
        // stay — показ командой (окно берёт фокус, внизу подсказка про клавиши); fake — выдуманные окна, без захвата
        public void Open(List<WinItem> items, int selectedIndex, Win.RECT monitor, bool stay, bool fake)
        {
            var started = Stopwatch.StartNew();
            Marks.Hit("open");
            if (phase != Phase.Idle) Abandon();
            FirstT = -1; GapMax = 0; WorkMax = 0; FlightFrames = 0; LeaveFrames = 0; LeaveFirstT = -1; seenAt = -1;
            OpenGaps.Clear(); BrowseGaps.Clear(); LeaveGaps.Clear();
            Repeats = 0; Counted = 0; WgcSum = 0; WgcMax = 0; Gc0 = Gc1 = Gc2 = 0;
            PullSum = PullMax = BgSum = BgMax = PlaceSum = PlaceMax = WorkSum = WorkTop = 0;
            AfterWgc = LateAfterWgc = AfterNone = LateAfterNone = 0; wgcBefore = 0; movedBefore = false; springing = false;
            WorstGap = 0; WorstAt = 0; WorstPhase = "";
            Reordered = 0; reorderedSeen = 0; reorderedBefore = false; AfterReorder = 0; LateAfterReorder = 0;
            lateTimes = "";
            GoAfter = -1; HoldGapMax = 0; FirstLateAt = -1; LateMoving = 0;
            view = Prefs.Is3D(Prefs.View) ? Prefs.View : "flip";
            mon = monitor;
            sticky = stay;
            snap = Ui.ReducedMotion;
            w = (mon.Right - mon.Left) / scale; h = (mon.Bottom - mon.Top) / scale;
            Fit(w, h);
            hint.Visibility = sticky ? Visibility.Visible : Visibility.Collapsed;
            phase = Phase.Shown;
            leaving = false; leaveTo = -1;

            if (deskWanted) OpenDesktopNow();
            // Объект захвата монитора так и не успел: кадра монитора в этот раз не будет (снимать его после показа нельзя —
            // в кадр попала бы сама сцена)
            if (monWanted) { monWanted = false; MonitorNote = "объект захвата не успел к показу"; }
            // Фон: кадр монитора. Дальше он не нужен — и не должен захватывать уже показанную сцену. Если его копия
            // в память ещё идёт, он дойдёт в одном из ближайших кадров (пока сцена спрятана).
            Pull(monLive);
            if (monLive != null) monLive.Stop();
            MonitorGot = false;
            brushA.ImageSource = null;
            layerA.Visibility = Visibility.Collapsed;
            deskShown = false;
            if (!MonitorReady())
            {
                SetBackground(false);
                if (monLive != null) MonitorNote = "не успел к показу";
            }

            if (!fake) QueueStills(items);
            Build(items, fake);
            LiftHomes();
            Marks.Hit("cards");
            selected = Math.Max(0, Math.Min(selectedIndex, cards.Count - 1));
            // Стопка встаёт так, как окна лежат на экране, и выбор доезжает до выбранного (Flip.StartPos)
            pos = snap ? selected : Flip.StartPos(selected, cards.Count);
            target = Flip.Nearest(pos, selected, cards.Count); vel = 0;
            // Этот шаг выбора идёт той же кривой и за то же время, что и полёт: стопка встаёт и поворачивается одним движением
            posFrom = pos;
            gliding = pos != target;
            Caption();
            Rebalance(OpenAtOnce);
            Marks.Hit("captures");

            Win.Cloak(hwnd, true);
            awaiting = true;
            frames = 0;
            appearing = true;                // при «меньше движения» — без полёта, только проявление
            elapsed = 0;
            openEnd = snap ? Flip.FadeMs : Flip.OpenMs;
            Left = mon.Left / scale; Top = mon.Top / scale; Width = w; Height = h;
            Win.Style(hwnd, !sticky);
            Win.Place(hwnd, mon);
            Place();
            Show();
            Win.Place(hwnd, mon);
            clock.Restart();
            lastFrame = -1;
            StartTicking();
            uncloakGuard.Start();
            if (sticky) Activate();
            OpenMs = started.Elapsed.TotalMilliseconds;
            Marks.Hit("built");
        }

        // Выбор сменился; animate — доехать пружиной (иначе сразу)
        public void Select(int index, bool animate)
        {
            if (phase != Phase.Shown || cards.Count == 0) return;
            selected = Math.Max(0, Math.Min(index, cards.Count - 1));
            EndGlide();
            target = Flip.Nearest(pos, selected, cards.Count);
            if (!animate || snap) { pos = target; vel = 0; }
            Caption();
            Rebalance(OpenAtOnce);
            Place();
        }

        // Окно закрыли с карточки: карточка уходит, остальные остаются со своими картинками
        public void Remove(int index)
        {
            if (phase != Phase.Shown || index < 0 || index >= cards.Count) return;
            FlipCard c = cards[index];
            Release(c);
            cards.RemoveAt(index);
            for (int i = 0; i < cards.Count; i++) cards[i].Index = i;
            int n0 = cards.Count + 1, at = (int)((Math.Round(pos) % n0 + n0) % n0);
            if (index < at) { pos -= 1; target -= 1; posFrom -= 1; }
            selected = Math.Max(0, Math.Min(selected, cards.Count - 1));
            Place();
        }

        // Уход. target — окно, в которое переключились (null — отмена: все карточки по домам); done — когда сцена убрана
        public void Close(WinItem targetItem, Action whenDone)
        {
            if (phase != Phase.Shown)
            {
                // Показа не было (быстрый Alt+Tab) или уже уходим: убрать сразу
                Hide();
                if (whenDone != null) whenDone();
                return;
            }
            Marks.Hit("close");
            done = whenDone;
            leaveCommit = targetItem != null;
            leaveTo = targetItem == null ? -1 : cards.FindIndex(delegate(FlipCard c) { return c.Item == targetItem; });
            uncloakGuard.Stop();
            if (awaiting) { Hide(); return; }
            // Уход начинается оттуда, где карточки и фон сейчас, — даже если появление ещё не доиграло
            bgFrom = layerB.Opacity;
            EndGlide();
            phase = Phase.Leaving;
            appearing = false;
            leaving = true;
            elapsed = 0;
            leaveHeld = 0;
            leaveEnd = snap ? Flip.FadeMs : Flip.LeaveMs;
            // Выбрано свёрнутое окно, чьё будущее место на этом мониторе известно: карточка летит туда, как обычное окно
            // домой, а сцена не убирается, пока окно под ней не развернулось (Restore.Ready)
            restoring = !snap && leaveTo >= 0 && cards[leaveTo].Restore ? cards[leaveTo] : null;
            knownAt = -1; missed = false;
            RestoreFlew = restoring != null; RestoreFrames = 0; RestoreMiss = -1; RestoreEndMiss = -1;
            RestoreKnownMs = -1; RestoreOpenMs = -1; RestoreLiveMs = -1; RestoreWaitedMs = -1;
            RestoreGuess = restoring != null ? restoring.RestoreRect : new Win.RECT(); RestoreReal = new Win.RECT();
            animMs = restoring == null ? 0 : restoring.Item.Fake || SystemParameters.MinimizeAnimation ? Restore.AnimMs : 0;
            StartTicking();
            leaveGuard.Stop();
            leaveGuard.Start();
        }

        // Убрать сцену сейчас же: спрятать, освободить все захваты. Ждавший ухода получает done.
        public new void Hide()
        {
            if (Marks.On && phase != Phase.Idle)
            {
                Marks.Hit("hidden");
                Marks.Note("view", Array.IndexOf(Flip.Views, view));
                Marks.Note("cards", cards.Count);
                Marks.Note("firstT", FirstT); Marks.Note("flight", FlightFrames);
                Marks.Note("gapMax", GapMax); Marks.Note("workMax", WorkMax);
               
                Marks.Note("leaveFrames", LeaveFrames); Marks.Note("leaveFirstT", LeaveFirstT);
                // Неподвижная пауза после раскрытия: через сколько мс пошло движение, худший промежуток до этого;
                // опоздавшие кадры уже в полёте
                Marks.Note("goAfter", GoAfter); Marks.Note("holdGapMax", HoldGapMax);
                Marks.Note("lateMoving", LateMoving); Marks.Note("firstLateAt", FirstLateAt);
                // Шаг кадров: распределение промежутков (мс, по времени показа) при появлении, листании и уходе
                if (seenAt >= 0) { Gc0 = GC.CollectionCount(0) - gc0; Gc1 = GC.CollectionCount(1) - gc1; Gc2 = GC.CollectionCount(2) - gc2; }
                Marks.Text("gapsOpen", OpenGaps.Short()); Marks.Text("gapsBrowse", BrowseGaps.Short()); Marks.Text("gapsLeave", LeaveGaps.Short());
                Marks.Text("work", WorkLine()); Marks.Note("repeats", Repeats);
                Marks.Text("gc", Gc0 + "/" + Gc1 + "/" + Gc2);
                Marks.Note("tier", RenderCapability.Tier >> 16);
            }
            uncloakGuard.Stop();
            leaveGuard.Stop();
            StopTicking();
            if (hwnd != IntPtr.Zero && (phase != Phase.Idle || IsVisible)) Win.Cloak(hwnd, true);
            if (IsVisible) base.Hide();
            phase = Phase.Idle;
            awaiting = false; appearing = false; leaving = false;
            restoring = null;
            foreach (FlipCard c in cards) Release(c);
            cards.Clear();
            drawn.Clear();
            Rebuild();
            ReleaseBackground();
            DropEarly();
            DropStills();
            deskWanted = false; monWanted = false;
            Marks.Flush();
            Action d = done;
            done = null;
            if (d != null) d();
        }

        // Новый показ поверх ещё не убранного: то же, что Hide, но фоновые захваты (уже начатые в Warm) остаются
        void Abandon()
        {
            LiveWindow m = monLive, d = deskLive;
            var e = new Dictionary<IntPtr, LiveWindow>(early);
            var q = new List<WinItem>(earlyQueue);
            var ss = new List<IconicShot>(shots);
            var sq = new List<WinItem>(shotQueue);
            bool dw = deskWanted, mw = monWanted;
            monLive = null; deskLive = null; early.Clear(); earlyQueue.Clear(); shots.Clear(); shotQueue.Clear();
            Hide();
            monLive = m; deskLive = d;
            foreach (var kv in e) early[kv.Key] = kv.Value;
            earlyQueue.AddRange(q);
            shots.AddRange(ss);
            shotQueue.AddRange(sq);
            deskWanted = dw; monWanted = mw;
        }

        void ReleaseBackground()
        {
            if (monLive != null) { monLive.Dispose(); monLive = null; }
            if (deskLive != null) { deskLive.Dispose(); deskLive = null; }
            monImage = null; monW = monH = 0;
            brushA.ImageSource = null;
            brushB.ImageSource = null;
            layerA.Visibility = Visibility.Collapsed;
            layerB.Visibility = Visibility.Collapsed;
            deskShown = false;
        }

        void Release(FlipCard c)
        {
            if (c.Live != null) { c.Live.Dispose(); c.Live = null; }
            c.ShowsLive = false;
        }

        // Первый кадр показа нарисован — окно открывается глазам и начинается появление
        void Uncloak()
        {
            uncloakGuard.Stop();
            if (!awaiting) return;
            awaiting = false;
            Marks.Hit("uncloak");
            // Летят со своих мест окна, у которых есть картинка — живая или вырезанная из кадра монитора; без неё на месте
            // окна мелькнула бы плашка, и такие карточки просто проявляются в раскладке
            Flew = 0; Homes = 0;
            foreach (FlipCard c in cards) { c.FlyHome = Flies(c); if (c.FlyHome) Flew++; if (c.HasHome) Homes++; }
            // Сторож только что спрятал окно (поток окна зависал): глазам не открываем, сброс показа уже в очереди
            if (!StayCloaked && !StallGuard.Tripped) Win.Cloak(hwnd, false);
            // Этот кадр — первый, который видно. Сцена пока стоит неподвижно (неотличима от экрана); отсчёт появления
            // начнётся с первого кадра, пришедшего вовремя (Flip.Go в Motion). Задержки старта — по месту в раскладке сейчас.
            elapsed = 0;
            started = false;
            holdFrom = frameShown;
            foreach (FlipCard c in cards) c.Delay = snap ? 0 : Flip.Delay(view, Flip.Depth(c.Index, pos, cards.Count), cards.Count);
            openEnd = snap ? Flip.FadeMs : Flip.OpenMs;
            Place();
        }

        // ───────── Фон ─────────

        // Кадр монитора дошёл (true): он — фон первого мига, и из него же картинки окон, чьих живых ещё нет
        bool MonitorReady()
        {
            if (MonitorGot || monLive == null || monLive.Frames == 0) return false;
            MonitorGot = true;
            Marks.Hit("monitor");
            MonitorNote = "есть, путь " + monLive.Path;
            monImage = monLive.Image; monW = monLive.Width; monH = monLive.Height;
            brushA.ImageSource = monImage;
            layerA.Visibility = Visibility.Visible;
            SetBackground(true);
            foreach (FlipCard c in cards) if (!c.ShowsLive) Crop(c);
            return true;
        }

        // Слой B: размытые затемнённые обои; пока их нет — размытый кадр монитора; нет и его — общий фон dev.txt
        void SetBackground(bool haveA)
        {
            if (deskLive != null && deskLive.Frames > 0 && SetDesktopBrush()) { deskShown = true; layerB.Visibility = Visibility.Visible; Marks.Hit("desk"); return; }
            brushB.Viewbox = new Rect(0, 0, 1, 1);
            brushB.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
            brushB.ImageSource = haveA ? brushA.ImageSource : null;
            layerB.Visibility = haveA ? Visibility.Visible : Visibility.Collapsed;
        }

        // Обои — окно рабочего стола целиком (на нескольких мониторах — на весь виртуальный экран); из него берётся наш монитор
        bool SetDesktopBrush()
        {
            int pw = deskRect.Right - deskRect.Left, ph = deskRect.Bottom - deskRect.Top;
            if (pw <= 0 || ph <= 0 || deskLive.Width <= 0 || deskLive.Height <= 0) return false;
            double kx = deskLive.Width / (double)pw, ky = deskLive.Height / (double)ph;
            brushB.ViewboxUnits = BrushMappingMode.Absolute;
            brushB.Viewbox = new Rect((mon.Left - deskRect.Left) * kx, (mon.Top - deskRect.Top) * ky, (mon.Right - mon.Left) * kx, (mon.Bottom - mon.Top) * ky);
            brushB.ImageSource = deskLive.Image;
            DesktopGot = true;
            DesktopNote = "есть (Progman), путь " + deskLive.Path;
            return true;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Win.RECT r);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Win.POINT p, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Win.RECT value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int Size; public Win.RECT Monitor, Work; public uint Flags; }

        // Окно рабочего стола с обоями — Progman. В новых сборках Windows 11 обои и значки — его дочерние окна, а захват окна
        // берёт и дочерние; что отдаёт захват Progman, показывает --flipprobe (разброс яркости: обои — не ровная заливка).
        LiveWindow OpenDesktop(out string why)
        {
            why = "";
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) { why = "нет окна Progman"; return null; }
            if (!GetWindowRect(progman, out deskRect)) { why = "нет размера Progman"; return null; }
            return LiveWindow.Open(progman, false, true, true, LiveWindow.QuickWait, out why);
        }

        static IntPtr Monitor(Win.RECT r)
        {
            return MonitorFromPoint(new Win.POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 }, 2);   // MONITOR_DEFAULTTONEAREST
        }

        // Монитор, на котором окно (в точках экрана) — для --flipprobe
        public static Win.RECT MonitorOf(IntPtr window)
        {
            var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(MonitorFromWindow(window, 2), ref info);
            return info.Monitor;
        }

        // Видимые границы окна на экране (без невидимых полей для растягивания). false — окна не видно.
        static bool Bounds(IntPtr window, out Win.RECT r)
        {
            r = new Win.RECT();
            if (!IsWindowVisible(window) || IsIconic(window)) return false;
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, 4) == 0 && cloaked != 0 && (cloaked & 1) == 0) return false;   // спрятано системой (другой рабочий стол); своё (DWM_CLOAKED_APP) — только в проверке
            if (DwmGetWindowAttribute(window, 9, out r, Marshal.SizeOf(typeof(Win.RECT))) != 0 && !GetWindowRect(window, out r)) return false;   // DWMWA_EXTENDED_FRAME_BOUNDS
            return r.Right - r.Left >= 8 && r.Bottom - r.Top >= 8;
        }

        // ───────── Карточки ─────────

        void Fit(double width, double height)
        {
            root.Width = width; root.Height = height;
            camera.Position = new Point3D(0, 0, Flip.Distance(width));
            caption.Margin = new Thickness(0, 0, 0, height * 0.07);
        }

        void Build(List<WinItem> items, bool fake)
        {
            foreach (FlipCard old in cards) Release(old);
            cards.Clear();
            drawn.Clear();
            Dictionary<IntPtr, int> ranks = fake ? null : ZOrder();
            for (int i = 0; i < items.Count; i++)
            {
                WinItem it = items[i];
                // Окно игры (Logic.MayTouch): ни захвата, ни снимка свёрнутого, ни куска кадра монитора — плашка со значком
                bool touch = Logic.MayTouch(it);
                var c = new FlipCard { Item = it, Index = i, CanLive = !fake && !it.Fake && !it.Minimized && touch, Aspect = it.Fake ? it.Aspect : Flip.DefaultAspect };
                // Выдуманные окна лежат по порядку списка (первое сверху), настоящие — как на экране
                int zr;
                c.ZRank = ranks != null && ranks.TryGetValue(it.Hwnd, out zr) ? zr : items.Count - i;
                Win.RECT r;
                if (!it.Fake && touch && Bounds(it.Hwnd, out r))
                {
                    c.Aspect = (r.Right - r.Left) / (double)(r.Bottom - r.Top);
                    // Место есть, только если окно на этом мониторе
                    int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
                    if (cx >= mon.Left && cx < mon.Right && cy >= mon.Top && cy < mon.Bottom)
                    {
                        c.HasHome = true;
                        c.Rect = r;
                        c.Home = Flip.Home(r.Left, r.Top, r.Right, r.Bottom, mon.Left, mon.Top, scale, w, h);
                    }
                }
                // Свёрнутое окно: где оно встанет, если его выберут, — только чтение, окно не трогается
                Win.RECT fr;
                bool toMax;
                string how;
                if (!fake && !it.Fake && it.Minimized && touch && Win.RestorePlace(it.Hwnd, hwnd, out fr, out toMax, out how)) SetRestore(c, fr, toMax);
                Flip.Front(view, c.Aspect, w, h, out c.FrontW, out c.FrontH);
                MakeModel(c);
                if (!it.Fake && it.Icon == null) it.Icon = Win.Icon(it, Dispatcher, IconReady);
                // Живая картинка, заведённая ещё до показа (Warm), — сразу на грань
                LiveWindow l;
                if (!fake && early.TryGetValue(it.Hwnd, out l))
                {
                    early.Remove(it.Hwnd);
                    if (c.CanLive) { c.Live = l; if (l.Frames > 0) ShowLive(c); } else l.Dispose();
                }
                // Свёрнутое окно: его картинка из памяти (Iconic.cs), если уже снята; нет — плашка, пока не снимется
                BitmapSource still = !fake && it.Minimized && touch ? Stills.Get(it.Hwnd) : null;
                if (still != null) { ShowStill(c, still); StillCached++; }
                else if (!c.ShowsLive && !Crop(c)) Picture(c);
                cards.Add(c);
            }
            DropEarly();
        }

        // Будущее место свёрнутого окна (видимая рамка, в точках экрана) — карточке, если оно на этом мониторе.
        // На другом мониторе лететь некуда: карточка при выборе гаснет, как раньше.
        void SetRestore(FlipCard c, Win.RECT r, bool toMax)
        {
            int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
            if (cx < mon.Left || cx >= mon.Right || cy < mon.Top || cy >= mon.Bottom) return;
            c.Restore = true;
            c.RestoreMax = toMax;
            c.RestoreRect = r;
            c.RestoreHome = Flip.Home(r.Left, r.Top, r.Right, r.Bottom, mon.Left, mon.Top, scale, w, h);
        }

        // Уход к свёрнутому окну, раз за кадр: развернулось ли оно под сценой (видно, не свёрнуто, есть видимая рамка) —
        // тогда его настоящее место и живой захват (WGC теперь даёт кадры). Захват заводится через ту же очередь
        // объектов захвата (CaptureItems.Later — спросим в следующем кадре), первый кадр подменяет неподвижную картинку.
        void WatchRestore()
        {
            FlipCard c = restoring;
            RestoreFrames++;
            if (c.Item.Fake) return;
            Win.RECT r;
            if (knownAt < 0)
            {
                if (!Bounds(c.Item.Hwnd, out r)) return;
                Restored(c, r);
            }
            if (c.Live == null && !c.Dead)
            {
                string why;
                c.Live = LiveWindow.Open(c.Item.Hwnd, false, true, false, LiveWindow.QuickWait, out why);
                if (c.Live != null) { LiveOpened++; c.CanLive = true; RestoreOpenMs = elapsed; }
                else if (why != CaptureItems.Later) c.Dead = true;
            }
            if (c.ShowsLive && RestoreLiveMs < 0) RestoreLiveMs = elapsed;
        }

        // Окно развернулось на r (видимая рамка, в точках экрана): если не там, где ждали, и на этом же мониторе —
        // карточка плавно доходит до настоящего места (Restore.Correct)
        void Restored(FlipCard c, Win.RECT r)
        {
            knownAt = elapsed;
            RestoreKnownMs = elapsed;
            RestoreReal = r;
            RestoreMiss = Restore.Miss(r, c.RestoreRect);
            int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
            missed = RestoreMiss > 0 && cx >= mon.Left && cx < mon.Right && cy >= mon.Top && cy < mon.Bottom;
            if (missed) realHome = Flip.Home(r.Left, r.Top, r.Right, r.Bottom, mon.Left, mon.Top, scale, w, h);
        }

        // Куда летит карточка свёрнутого окна сейчас: на предсказанное место, а если окно встало иначе — к настоящему
        Pose RestoreTo(FlipCard c)
        {
            if (!missed || knownAt < 0) return c.RestoreHome;
            return Flip.Lerp(c.RestoreHome, realHome, Restore.Correct(elapsed, knownAt, leaveEnd));
        }

        // Сцена убирается: насколько конечная поза карточки разошлась с видимой рамкой окна (в точках экрана)
        void RestoreDone()
        {
            double l, t, r, b;
            Flip.Screen(restoring.Now, w, h, out l, out t, out r, out b);
            var at = new Win.RECT
            {
                Left = (int)Math.Round(l * scale + mon.Left), Top = (int)Math.Round(t * scale + mon.Top),
                Right = (int)Math.Round(r * scale + mon.Left), Bottom = (int)Math.Round(b * scale + mon.Top)
            };
            Win.RECT real;
            if (restoring.Item.Fake) real = knownAt >= 0 ? RestoreReal : restoring.RestoreRect;
            else if (!Bounds(restoring.Item.Hwnd, out real)) return;
            RestoreReal = real;
            RestoreEndMiss = Restore.Miss(at, real);
        }

        [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr parent);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

        // Настоящий порядок окон верхнего уровня: окно → место, чем выше на экране, тем больше. Числа заведомо больше
        // числа окон в списке, так что окно, которого в порядке почему-то нет, ляжет ниже всех. Только чтение.
        static Dictionary<IntPtr, int> ZOrder()
        {
            var all = new List<IntPtr>();
            for (IntPtr h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && all.Count < 10000; h = GetWindow(h, 2)) all.Add(h);   // GW_HWNDNEXT
            var rank = new Dictionary<IntPtr, int>();
            for (int i = 0; i < all.Count; i++) if (!rank.ContainsKey(all[i])) rank[all[i]] = 100000 + all.Count - i;
            return rank;
        }

        // Дома карточки стоят по глубине так же, как окна лежат на экране: верхнее — в плоскости экрана, каждое следующее
        // вниз — на Flip.LiftStep глубже (и во столько же раз крупнее: на экране то же место, Flip.Lift)
        void LiftHomes()
        {
            var homes = cards.FindAll(delegate(FlipCard c) { return c.HasHome; });
            homes.Sort(delegate(FlipCard a, FlipCard b) { return b.ZRank.CompareTo(a.ZRank); });
            for (int r = 0; r < homes.Count; r++) homes[r].Home = Flip.Lift(homes[r].Home, -r * Flip.LiftStep * w, w);
        }

        // Живая картинка пришла: на грань, пропорции карточки — по ней. true — пропорции изменились.
        bool ShowLive(FlipCard c)
        {
            c.GotFrame = true;
            if (PathNote.Length == 0) PathNote = c.Live.Path;
            Paint(c, c.Live.Image);
            c.ShowsLive = true;
            c.Cropped = false;
            double a = c.Live.Width / (double)Math.Max(1, c.Live.Height);
            if (Math.Abs(a - c.Aspect) <= 0.01) return false;
            c.Aspect = a;
            Flip.Front(view, a, w, h, out c.FrontW, out c.FrontH);
            return true;
        }

        void Paint(FlipCard c, ImageSource img)
        {
            if (c.Brush.ViewboxUnits != BrushMappingMode.RelativeToBoundingBox) c.Brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
            if (c.Brush.Viewbox != new Rect(0, 0, 1, 1)) c.Brush.Viewbox = new Rect(0, 0, 1, 1);
            if (c.Brush.ImageSource != img) c.Brush.ImageSource = img;
        }

        // Пока живой картинки нет, а окно на этом мониторе: на грань — его же место из кадра монитора. На своём месте
        // карточка тогда выглядит ровно как окно, и живая картинка потом подменяет её без скачка.
        bool Crop(FlipCard c)
        {
            double x, y, cw, ch;
            if (!c.HasHome || !MonitorGot || monImage == null
                || !Flip.CropBox(c.Rect.Left, c.Rect.Top, c.Rect.Right, c.Rect.Bottom, mon.Left, mon.Top, mon.Right, mon.Bottom, monW, monH, out x, out y, out cw, out ch)) return false;
            c.Brush.ViewboxUnits = BrushMappingMode.Absolute;
            c.Brush.Viewbox = new Rect(x, y, cw, ch);
            c.Brush.ImageSource = monImage;
            c.Cropped = true;
            return true;
        }

        // Летит ли карточка со своего места: место есть и есть чем его показать
        static bool Flies(FlipCard c) { return c.HasHome && (c.ShowsLive || c.Cropped); }

        // Четырёхугольник 1×1 с картинкой; размер, поворот и место задают преобразования
        void MakeModel(FlipCard c)
        {
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { new Point3D(-0.5, -0.5, 0), new Point3D(0.5, -0.5, 0), new Point3D(0.5, 0.5, 0), new Point3D(-0.5, 0.5, 0) },
                TextureCoordinates = new PointCollection { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
            };
            mesh.Freeze();
            c.Brush = new ImageBrush { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(c.Brush, BitmapScalingMode.Linear);
            c.Size = new ScaleTransform3D(1, 1, 1);
            c.Roll = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            c.Turn = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
            c.Tilt = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
            c.Move = new TranslateTransform3D();
            // Порядок фиксирован (его же повторяют Pose и Flip.Corner): размер, поворот в плоскости экрана (z),
            // вокруг вертикали (y), вокруг горизонтали (x), сдвиг
            var t = new Transform3DGroup();
            t.Children.Add(c.Size);
            t.Children.Add(new RotateTransform3D(c.Roll));
            t.Children.Add(new RotateTransform3D(c.Turn));
            t.Children.Add(new RotateTransform3D(c.Tilt));
            t.Children.Add(c.Move);
            var model = new GeometryModel3D { Geometry = mesh, Material = new DiffuseMaterial(c.Brush), Transform = t };
            c.Visual = new ModelVisual3D { Content = model };
        }

        // Картинка карточки, пока живой нет: рисунок выдуманного окна или плашка со значком
        void Picture(FlipCard c)
        {
            if (c.ShowsLive || c.Still) return;
            c.Cropped = false;
            FrameworkElement el;
            double pw, ph;
            if (c.Item.Fake && !c.Item.Minimized)
            {
                pw = c.FrontW; ph = c.FrontH;
                el = Demo.Window(c.Item, pw, ph);
            }
            else
            {
                pw = PlaqueW; ph = Math.Round(PlaqueW / c.Aspect);
                el = Plaque(c.Item, pw, ph);
            }
            Paint(c, Snapshot(el, pw, ph));
        }

        // Плашка: ровная подложка, крупный значок, у свёрнутого окна — подпись
        static FrameworkElement Plaque(WinItem item, double pw, double ph)
        {
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = Demo.IconOf(item, 72);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(icon);
            if (item.Minimized)
            {
                var note = Ui.T("Свёрнуто", Tokens.Size.Body, Ui.Label3, FontWeights.Normal);
                note.HorizontalAlignment = HorizontalAlignment.Center; note.Margin = new Thickness(0, 12, 0, 0);
                col.Children.Add(note);
            }
            return new Border { Width = pw, Height = ph, Background = Ui.Solid(Tokens.Dark.Raised), BorderBrush = Ui.Solid(Tokens.Dark.EdgeTop), BorderThickness = new Thickness(1), Child = col }; // audit:ok — тёмная сцена поверх экрана, от темы Windows не зависит
        }

        ImageSource Snapshot(FrameworkElement el, double pw, double ph)
        {
            el.Measure(new Size(pw, ph));
            el.Arrange(new Rect(0, 0, pw, ph));
            el.UpdateLayout();
            double k = Math.Max(1, scale);
            var bmp = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(pw * k)), Math.Max(1, (int)Math.Ceiling(ph * k)), 96 * k, 96 * k, PixelFormats.Pbgra32);
            bmp.Render(el);
            bmp.Freeze();
            return bmp;
        }

        // Значок добыт в фоне: в подпись и на плашку
        void IconReady(WinItem item, ImageSource img)
        {
            item.Icon = img;
            if (phase == Phase.Idle) return;
            foreach (FlipCard c in cards) if (c.Item == item && !c.Cropped) Picture(c);
            Caption();
        }

        void Caption()
        {
            if (cards.Count == 0) { name.Text = ""; iconHost.Child = null; minimized.Visibility = Visibility.Collapsed; return; }
            WinItem it = cards[selected].Item;
            name.Text = it.Title;
            minimized.Visibility = cards[selected].Still ? Visibility.Visible : Visibility.Collapsed;
            iconHost.Child = Demo.IconOf(it, 20);
        }

        // Живые картинки: не больше Flip.MaxLive сразу, ближайшие к выбранному — первыми (и вперёд, и на шаг назад);
        // дальние получают плашку. limit — сколько новых завести сейчас.
        void Rebalance(int limit)
        {
            int n = cards.Count;
            if (n == 0) return;
            var order = new List<FlipCard>(cards);
            order.Sort(delegate(FlipCard a, FlipCard b) { return Rank(a.Index, n).CompareTo(Rank(b.Index, n)); });
            int live = 0;
            foreach (FlipCard c in order)
            {
                bool want = c.CanLive && !c.Dead && live < Flip.MaxLive;
                if (want) live++;
                if (!want && c.Live != null) { Release(c); Picture(c); }
                if (want && c.Live == null && limit > 0)
                {
                    limit--;
                    string why;
                    c.Live = LiveWindow.Open(c.Item.Hwnd, false, true, false, LiveWindow.QuickWait, out why);
                    // Объект захвата ещё готовится в своём потоке (Later): место за карточкой остаётся, спросим в следующем кадре
                    if (c.Live != null) LiveOpened++;
                    else if (why != CaptureItems.Later) { c.Dead = true; LiveDied++; live--; }
                }
            }
        }

        // В колоде важнее те, что дальше по стопке; в симметричных раскладках — ближайшие с обеих сторон поровну
        int Rank(int i, int n)
        {
            int k = ((i - selected) % n + n) % n;
            return Flip.Symmetric(view) ? Math.Min(2 * k, 2 * (n - k) - 1) : Math.Min(2 * k, 4 * (n - k) - 1);
        }

        static void Pull(LiveWindow l) { if (l != null && !l.Dead) l.Pull(); }

        // ───────── Кадр ─────────

        void StartTicking()
        {
            if (ticking) return;
            shownAt = -1;
            CompositionTarget.Rendering += Frame;
            ticking = true;
        }

        void StopTicking()
        {
            if (!ticking) return;
            CompositionTarget.Rendering -= Frame;
            ticking = false;
        }

        // Кадр: свежие картинки окон, пружина выбора, появление и уход. Подписка живёт, пока сцена на экране (окна живые),
        // но если ничего не пришло и ничего не едет — сцена не трогается и не перерисовывается.
        void Frame(object sender, EventArgs e)
        {
            try
            {
                if (!clock.IsRunning) clock.Start();
                long t0 = Stopwatch.GetTimestamp();
                double now = clock.Elapsed.TotalSeconds, dt;
                // Шаг — по времени показа кадра (а не по часам в обработчике: их зовут неровно, и позиции дрожали бы);
                // повторный вызов с тем же временем показа — не кадр
                var re = e as RenderingEventArgs;
                double shown = re != null ? re.RenderingTime.TotalMilliseconds : t0 * TickMs;
                if (!Pace.Next(shown, ref shownAt, out dt)) { Repeats++; return; }
                typical = Pace.Typical(typical, dt);
                if (phase == Phase.Idle)
                {
                    // До показа: копим кадры фона и заводим живые картинки окон. Кадру монитора хватит одного — захват сразу
                    // останавливается, картинка остаётся
                    if (monWanted) OpenMonitorNow();
                    if (monLive != null && !monLive.Dead && monLive.Pull()) { monLive.Stop(); Marks.Hit("monitor"); }
                    Pull(deskLive);
                    WarmStep();
                    if (Marks.On) { IdleFrames++; double wk = (clock.Elapsed.TotalSeconds - now) * 1000; if (wk > IdleWorkMax) IdleWorkMax = wk; }
                    return;
                }
                double gap = lastFrame >= 0 ? shown - lastFrame : -1;
                if (lastFrame >= 0)
                {
                    SceneFrames++; FrameSum += gap; if (gap > FrameWorst) FrameWorst = gap;
                }
                lastFrame = shown;
                Marks.Hit("frame1");
                bool moved = Motion(dt, shown, gap);
                if (leaving && restoring != null) WatchRestore();
                // Кадр монитора, чья копия в память ещё шла при показе
                if (awaiting && !MonitorGot && monLive != null && monLive.Pull()) MonitorReady();
                // Окно открывается глазам, когда кадр нарисован и у окон, что полетят со своих мест, есть картинка
                // (не дольше 100 мс — дальше его откроет uncloakGuard)
                if (awaiting && ++frames >= 2 && (MonitorGot || monLive == null || !monLive.Staged)
                    && cards.TrueForAll(delegate(FlipCard c) { return !c.HasHome || c.Live == null || Flies(c); })) Uncloak();

                bool changed = false;
                long t1 = Stopwatch.GetTimestamp();
                if (deskWanted) OpenDesktopNow();   // объект захвата обоев был ещё не готов
                if (deskLive != null && !deskLive.Dead && deskLive.Pull() && !deskShown && SetDesktopBrush())
                {
                    deskShown = true;
                    layerB.Visibility = Visibility.Visible;
                    Marks.Hit("desk");
                }
                // Обои на экране — захват дальше не нужен: каждый новый кадр обоев — это копия во весь экран через память
                // и новое размытие. Картинка остаётся (как у кадра монитора).
                if (deskShown && deskLive != null) deskLive.Stop();
                long t2 = Stopwatch.GetTimestamp();
                int wgc = 0;
                // Чьи живые картинки обновить: в движении — по две за кадр по кругу (Pace.LivePick), невидимые — никогда
                bool moving = !awaiting && (appearing || leaving || pos != target);
                int n = cards.Count;
                if (pickVisible.Length < n) { pickVisible = new bool[n]; pickFresh = new bool[n]; pick = new bool[n]; }
                for (int i = 0; i < n; i++) { FlipCard c = cards[i]; pickVisible[i] = c.Opacity > 0.002; pickFresh[i] = c.Live != null && !c.ShowsLive; }
                Pace.LivePick(pickVisible, pickFresh, n, moving, ref liveCursor, pick);
                for (int i = 0; i < n; i++)
                {
                    FlipCard c = cards[i];
                    if (c.Live == null) continue;
                    bool got = pick[i] && c.Live.Pull();
                    if (got) wgc++;
                    if (got && ShowLive(c)) changed = true;
                    if (c.Live.Dead)
                    {
                        // Захват умер (окно закрылось, видеокарта сбросилась): плашка, без исключений наружу
                        c.Dead = true; LiveDied++;
                        Release(c);
                        Picture(c);
                    }
                }
                // При уходе новые живые картинки не нужны: карточки гаснут или летят домой с тем, что у них есть
                if (!leaving) Rebalance(OpenAtOnce);
                if ((shots.Count > 0 || shotQueue.Count > 0) && StepStills(shotQueue.Count > 0)) changed = true;

                if (moved || appearing || leaving || awaiting) changed = true;
                long t3 = Stopwatch.GetTimestamp();
                if (changed) Place();
                long t4 = Stopwatch.GetTimestamp();
                // Долетели: окнам, чьей живой картинки так и не будет, — плашка вместо куска кадра монитора
                if (!appearing && !awaiting && !leaving)
                    foreach (FlipCard c in cards) if (c.Cropped && c.Live == null) Picture(c);
                if (Marks.On) Track(now, shown, gap, t0, t1, t2, t3, t4, wgc);
                if (LeaveOver()) Hide();
            }
            catch { Errors++; Fail(); }
        }

        // Выбор ехал вместе с появлением, а теперь его ведёт пружина (новый выбор, уход): пружина подхватывает его
        // с той же скоростью, без остановки
        void EndGlide()
        {
            if (!gliding) return;
            gliding = false;
            double share = Flip.Share(elapsed, 0, Flip.OpenMs), k = 0.001;
            vel = share >= 1 ? 0 : (target - posFrom) * (Flip.Glide(share + k) - Flip.Glide(share)) / k / (Flip.OpenMs / 1000.0);
        }

        // Уход доигран: сцену можно убирать. Зовётся раз за кадр, после Place: конечное положение (фон — ровно кадр
        // монитора, выбранное окно на своём месте) сначала LeaveHold кадров стоит на экране — и только потом сцена убирается,
        // так что последнее, что видно, совпадает с экраном под ней.
        // Уход к свёрнутому окну: конечное положение стоит, пока окно под сценой не развернулось и не доиграло анимацию
        // (и пока карточка не дошла до настоящего места), но не дольше Restore.WaitMs после конца ухода.
        bool LeaveOver()
        {
            if (!leaving || elapsed < leaveEnd) return false;
            if (restoring != null && !Restore.Ready(elapsed, leaveEnd, knownAt, missed, animMs)) return false;
            if (restoring != null && leaveHeld == 0) RestoreWaitedMs = Math.Max(0, elapsed - leaveEnd);
            if (++leaveHeld <= LeaveHold) return false;
            if (restoring != null) RestoreDone();
            return true;
        }

        // Движение за кадр (его же шагает --film): время появления или ухода и пружина выбора. dt — шаг кадра, с;
        // shownMs — время показа этого кадра, gapMs — промежуток от прошлого (−1 — прошлого не было). true — выбор едет.
        bool Motion(double dt, double shownMs, double gapMs)
        {
            frameShown = shownMs;
            // Сразу после раскрытия сцена стоит, пока не придёт кадр вовремя (или не выйдет Flip.HoldMs): пауза DWM
            // проходит на неподвижной картинке. Кадр, в котором движение решено начать, ещё показывает начало.
            if (appearing && !awaiting && !started)
            {
                double since = holdFrom >= 0 ? shownMs - holdFrom : 0;
                if (since > 0 && gapMs > HoldGapMax) HoldGapMax = gapMs;
                if (!Flip.Go(since, gapMs, typical * 1000)) return false;
                started = true;
                GoAfter = since;
                Marks.Hit("go");
                return false;
            }
            // Появление и уход идут по времени; подвисший кадр засчитывается не длиннее Flip.MaxDt, а первый шаг
            // движения — не длиннее обычного кадра
            if ((appearing && !awaiting) || leaving) elapsed = Flip.Tick(elapsed, elapsed <= 0 ? Pace.Start(dt, typical) : dt);
            // Пока появление не пошло, стоит и выбор
            if (appearing && (awaiting || !started)) return false;
            if (gliding && appearing)
            {
                double share = Flip.Share(elapsed, 0, Flip.OpenMs);
                pos = posFrom + (target - posFrom) * Flip.Glide(share);
                springing = true;
                if (share < 1) return true;
                gliding = false;
                double wrap = cards.Count > 0 ? Math.Floor(target / cards.Count) * cards.Count : 0;
                pos = target - wrap; target = pos; vel = 0;
                return true;
            }
            else if (gliding) EndGlide();
            bool wasSpringing = springing;
            springing = pos != target;
            if (pos == target) return false;
            if (snap) { pos = target; vel = 0; }
            else
            {
                // Пружина шагает мелкими равными шагами внутри кадра (Layout.Spring); первый шаг — не длиннее обычного кадра
                Layout.Spring(ref pos, ref vel, target, Tokens.Motion.SpringShapeStiffness, Tokens.Motion.SpringShapeDamping, wasSpringing ? dt : Pace.Start(dt, typical));
                if (Math.Abs(pos - target) < 0.002 && Math.Abs(vel) < 0.02) { pos = target; vel = 0; }
            }
            if (pos == target && cards.Count > 0)
            {
                // Доехали: выбор снова в пределах круга
                double wrap = Math.Floor(pos / cards.Count) * cards.Count;
                pos -= wrap; target -= wrap;
            }
            return true;
        }

        // Работа обработчика кадра по частям (средняя/худшая, мс): всё, фон, картинки окон (кадры WGC, лишние захваты,
        // снимки свёрнутых, пружина), позы и порядок; кадры WGC за кадр (среднее/больше всего)
        public string WorkLine()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int k = Math.Max(1, Counted);
            Func<double, string> f = delegate(double v) { return v.ToString("0.00", inv); };
            return "всё " + f(WorkSum / k) + "/" + f(WorkTop) + ", фон " + f(BgSum / k) + "/" + f(BgMax) + ", окна " + f(PullSum / k) + "/" + f(PullMax)
                + ", позы " + f(PlaceSum / k) + "/" + f(PlaceMax) + ", кадров WGC " + (WgcSum / (double)k).ToString("0.0", inv) + "/" + WgcMax
                + ", опоздал после кадра с WGC " + LateAfterWgc + " из " + AfterWgc + ", без WGC " + LateAfterNone + " из " + AfterNone
                + ", после смены карточек в сцене " + LateAfterReorder + " из " + AfterReorder
                + (WorstGap > 0 ? ", худший промежуток " + WorstGap.ToString("0.0", inv) + " (" + WorstPhase + ", через " + WorstAt.ToString("0", inv) + " мс после первого видимого)" : "")
                + (lateTimes.Length > 0 ? ", опоздавшие кадры — мс после первого видимого (промежуток): " + lateTimes.Trim() : "");
        }

        // Замеры кадра (только при Marks.On): первый видимый кадр, кадры полёта, подвисания за первую секунду, уход
        void Track(double now, double shownNow, double gap, long t0, long t1, long t2, long t3, long t4, int wgc)
        {
            double work = (clock.Elapsed.TotalSeconds - now) * 1000;
            // После первого видимого кадра: промежуток — в распределение своей части показа, работа — по частям
            // Промежуток считается, только если движение шло и в прошлом кадре, и в этом: когда ничего не едет, WPF сам
            // реже зовёт кадры, и это не рывок
            bool moving = leaving || placedFlight || springing;
            bool count = seenAt >= 0 && gap >= 0 && (phase == Phase.Shown || leaving) && moving && movedBefore;
            movedBefore = moving;
            if (count)
            {
                (leaving ? LeaveGaps : placedFlight ? OpenGaps : BrowseGaps).Add(gap);
                if (gap > WorstGap) { WorstGap = gap; WorstAt = shownNow - seenShown; WorstPhase = (leaving ? "уход" : placedFlight ? "появление" : "листание") + (reorderedBefore ? ", после смены карточек" : "") + (wgcBefore > 0 ? ", после WGC" : ""); }
                // Опоздал ли кадр после кадра, в котором картинки окон обновлялись (D3DImage), и после кадра без обновлений
                if (wgcBefore > 0) { AfterWgc++; if (gap > Gaps.Monitor) LateAfterWgc++; } else { AfterNone++; if (gap > Gaps.Monitor) LateAfterNone++; }
                if (reorderedBefore) { AfterReorder++; if (gap > Gaps.Monitor) LateAfterReorder++; }
                // Когда опаздывали кадры (первые несколько десятков): по ним видно, кучно или вразброс
                if (gap > Gaps.Monitor && lateTimes.Length < 400) lateTimes += ((int)(shownNow - seenShown)) + "(" + ((int)Math.Round(gap)) + ") ";
                // Опоздавший кадр уже во время полёта появления (порог — тот же, что у Flip.Go)
                if (placedFlight && !leaving && gap > Flip.OnTime * typical * 1000) { LateMoving++; if (FirstLateAt < 0) FirstLateAt = shownNow - seenShown; }
                double all = (Stopwatch.GetTimestamp() - t0) * TickMs, bg = (t2 - t1) * TickMs, pull = (t3 - t2) * TickMs, place = (t4 - t3) * TickMs;
                Counted++;
                WorkSum += all; if (all > WorkTop) WorkTop = all;
                BgSum += bg; if (bg > BgMax) BgMax = bg;
                PullSum += pull; if (pull > PullMax) PullMax = pull;
                PlaceSum += place; if (place > PlaceMax) PlaceMax = place;
                WgcSum += wgc; if (wgc > WgcMax) WgcMax = wgc;
            }
            wgcBefore = wgc;
            reorderedBefore = Reordered != reorderedSeen;
            reorderedSeen = Reordered;
            if (phase == Phase.Shown && !awaiting)
            {
                if (seenAt < 0) { seenAt = now; seenShown = shownNow; FirstT = placedT; Marks.Hit("seen"); gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2); }
                else if (now - seenAt <= 1 && gap > GapMax) GapMax = gap;
                if (now - seenAt <= 1 && work > WorkMax) WorkMax = work;
                if (placedFlight) FlightFrames++;
                if (!appearing) Marks.Hit("landed");
            }
            if (leaving) { LeaveFrames++; if (LeaveFirstT < 0) LeaveFirstT = placedT; }
            if (selected < cards.Count && cards[selected].ShowsLive) Marks.Hit("live-sel");
            if (cards.TrueForAll(delegate(FlipCard c) { return !c.CanLive || c.Dead || c.ShowsLive; })) Marks.Hit("live-all");
        }

        // Ставит каждую карточку на место и раскладывает порядок отрисовки: от дальних к ближним.
        // Появление: карточки летят со своих мест в раскладку с плавным замедлением, лесенкой; фон проявляется за то же время.
        // Уход: выбранная (при отмене — все, у кого есть место) летит домой с разгоном и замедлением, остальные гаснут
        // и чуть отъезжают вглубь. «Меньше движения»: никто не летит, всё только проявляется и гаснет.
        void Place()
        {
            int n = cards.Count;
            if (appearing && !awaiting && started && elapsed >= openEnd) appearing = false;
            double flight = snap ? Flip.FadeMs : Flip.OpenMs;
            double s = leaving ? Flip.Share(elapsed, 0, leaveEnd) : 0;
            // Для замеров: доля появления (у первой в раскладке карточки задержки нет)
            placedT = awaiting ? 0 : leaving ? s : appearing ? Flip.Share(elapsed, 0, flight) : 1;
            placedFlight = appearing && !awaiting && started;
            foreach (FlipCard c in cards)
            {
                double d = Flip.Depth(c.Index, pos, n);
                Pose stack = Flip.Place(view, d, n, c.FrontW, c.FrontH, w, h);
                c.Key = Flip.Order(view, d, n);
                Pose p = stack;
                if (leaving)
                {
                    Pose from = c.Now;
                    // Выбрали окно — домой летит только оно, остальные гаснут; отмена — домой все, у кого дом есть.
                    // Выбранное свёрнутое летит туда, где окно встанет после разворачивания.
                    bool restore = c == restoring;
                    bool home = !snap && (restore || (c.HasHome && (!leaveCommit || c.Index == leaveTo)));
                    if (home)
                    {
                        double e = Flip.Glide(s);
                        p = Flip.Lerp(from, restore ? RestoreTo(c) : c.Home, e);
                        p.Opacity = from.Opacity + (1 - from.Opacity) * e;
                    }
                    else
                    {
                        double e = snap ? s : Flip.Glide(s);
                        p = from;
                        if (!snap) p.Z = from.Z - e * Flip.Recede * w;
                        p.Opacity = from.Opacity * (1 - e);
                    }
                }
                else if (awaiting || appearing)
                {
                    if (snap) p.Opacity = awaiting ? 0 : stack.Opacity * Flip.Share(elapsed, 0, flight);
                    else
                    {
                        double e = awaiting ? 0 : Flip.Glide(Flip.Share(elapsed, c.Delay, Flip.Flight(c.Delay)));
                        bool fly = awaiting ? Flies(c) : c.FlyHome;
                        if (fly) { p = Flip.Lerp(c.Home, stack, e); p.Opacity = Flip.Blend(stack.Opacity, Flip.Edge(view, d, n), e); }
                        else p.Opacity = stack.Opacity * e;
                    }
                }
                Put(c, p);
            }
            // Фон: кадр экрана → размытые обои при появлении (одной кривой на всё появление, с нуля) и обратно при уходе
            // (до нуля к концу ухода, раньше, чем сцена уберётся)
            double bg = 1;
            if (leaving) bg = bgFrom * (1 - (snap ? s : Flip.Glide(s)));
            else if (awaiting) bg = 0;
            else if (appearing) bg = snap ? Flip.Share(elapsed, 0, flight) : Flip.Glide(Flip.Share(elapsed, 0, flight));
            layerB.Opacity = bg;
            caption.Opacity = bg;
            Rebuild();
        }

        void Put(FlipCard c, Pose p)
        {
            c.Size.ScaleX = Math.Max(1, p.W); c.Size.ScaleY = Math.Max(1, p.H);
            c.Roll.Angle = p.Roll;
            c.Turn.Angle = p.Angle;
            c.Tilt.Angle = p.Pitch;
            c.Move.OffsetX = p.X; c.Move.OffsetY = p.Y; c.Move.OffsetZ = p.Z;
            c.Opacity = Math.Max(0, Math.Min(1, p.Opacity));
            c.Z = p.Z;
            c.Now = p;
            if (c.Brush.Opacity != c.Opacity) c.Brush.Opacity = c.Opacity;
        }

        // Полупрозрачные грани WPF надо добавлять сзади наперёд; невидимые не добавляются вовсе.
        // Карточки в одной плоскости (дома, на своих окнах) чуть разведены по глубине — иначе они мерцали бы друг сквозь друга.
        // Список и сравнение — одни на все кадры: Rebuild зовётся каждый кадр движения, и выделять в нём нечего
        readonly List<FlipCard> want = new List<FlipCard>();
        Comparison<FlipCard> backToFront;

        int BackToFront(FlipCard a, FlipCard b)
        {
            int z = a.Z.CompareTo(b.Z);
            if (z != 0 && Math.Abs(a.Z - b.Z) > 0.5) return z;
            // Одна плоскость: выбранное при уходе — сверху; дома (пока появление не пошло и при отмене) — как окна
            // лежат на экране; иначе ближнее по стопке — сверху
            if (leaving && leaveCommit) { if (a.Index == leaveTo) return 1; if (b.Index == leaveTo) return -1; }
            if (awaiting || (appearing && !started) || (leaving && !leaveCommit)) { int r = a.ZRank.CompareTo(b.ZRank); if (r != 0) return r; }
            return a.Key.CompareTo(b.Key);
        }

        void Rebuild()
        {
            if (backToFront == null) backToFront = BackToFront;
            want.Clear();
            foreach (FlipCard c in cards) if (c.Opacity > 0.002) want.Add(c);
            want.Sort(backToFront);
            for (int i = 0; i < want.Count; i++) want[i].Move.OffsetZ = want[i].Z + i * 0.05;
            bool same = want.Count == drawn.Count;
            for (int i = 0; same && i < want.Count; i++) same = want[i] == drawn[i];
            if (same) return;
            if (Marks.On) Reordered++;
            viewport.Children.Clear();
            viewport.Children.Add(light);
            foreach (FlipCard c in want) viewport.Children.Add(c.Visual);
            drawn.Clear();
            drawn.AddRange(want);
        }

        // ───────── Мышь ─────────

        FlipCard Hit(MouseEventArgs e)
        {
            var res = VisualTreeHelper.HitTest(viewport, e.GetPosition(viewport)) as RayMeshGeometry3DHitTestResult;
            if (res == null) return null;
            foreach (FlipCard c in cards) if (c.Visual == res.VisualHit) return c;
            return null;
        }

        void Down(MouseButtonEventArgs e)
        {
            if (phase != Phase.Shown) return;
            e.Handled = true;
            pressed = Hit(e);
            // Щелчок мимо карточек — отмена, как щелчок мимо панели в сетке
            if (pressed == null && Cancelled != null) Cancelled();
        }

        void Up(MouseButtonEventArgs e)
        {
            if (phase != Phase.Shown) return;
            e.Handled = true;
            FlipCard c = Hit(e);
            if (c != null && c == pressed && Picked != null) Picked(c.Index);
            pressed = null;
        }

        void Middle(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || phase != Phase.Shown) return;
            e.Handled = true;
            FlipCard c = Hit(e);
            if (c != null && CloseAsked != null) CloseAsked(c.Index);
        }

        // ───────── Снимки ─────────

        // Сцена с выдуманными окнами в конечном виде — в PNG; на экране ничего
        public void Shot(string file, double width, double height, int select, bool stay)
        {
            var items = Demo.Windows(Switcher.DemoCount);
            view = Prefs.Is3D(Prefs.View) ? Prefs.View : "flip";
            mon = new Win.RECT { Right = (int)width, Bottom = (int)height };
            scale = 1;
            w = width; h = height;
            sticky = stay;
            snap = true;
            Fit(w, h);
            hint.Visibility = sticky ? Visibility.Visible : Visibility.Collapsed;
            phase = Phase.Shown;
            Build(items, true);
            selected = select >= 0 && select < cards.Count ? select : Logic.StartIndex(cards.Count, true, false);
            pos = target = selected;
            Caption();
            Place();
            Content = null;
            Save(file);
        }

        // ───────── Раскадровка: --demo --view ВИД --film папка ─────────
        // Та же сцена с выдуманными окнами, но по шагам времени, в PNG: появление от первого видимого кадра до покоя,
        // затем уход выбранного окна. У окон — «домашние» места на условном рабочем столе (с перекрытиями), под ними —
        // условный кадр монитора с этими окнами и условные обои: виден и полёт, и переход фона. На экране ничего.
        // Шагает тот же Motion и тот же Place, что и настоящий кадр; второй видимый кадр опаздывает на FilmLate шагов,
        // как на настоящем экране сразу после раскрытия. Возвращает строку итога: где наибольшие смещения за кадр
        // и насколько первый и последний кадры отличаются от «рабочего стола».
        const double FilmBar = 48;          // высота условной панели задач, точек
        const double FilmLate = 2;          // второй видимый кадр приходит через столько обычных шагов
        const int FilmThumb = 400;          // ширина кадра на контактном листе, точек
        const double FilmSheetMs = 1000.0 / 30;   // на контактном листе — кадр примерно через столько мс

        // pickMin: 0 — выбрано обычное окно; 1 — выбрано свёрнутое (у выдуманных окон свёрнуто каждое десятое, начиная
        // с четвёртого), его будущее место предсказано точно; 2 — предсказано мимо (сдвиг и другой размер), и окно
        // «развернувшись» встаёт на настоящее место — карточка доводится до него
        const double FilmRestoreMs = 30;    // «окно развернулось» через столько мс ухода (настоящее чужое окно — за 1–3 кадра)
        const int FilmLiveSteps = 2;        // и ещё через столько шагов кадра пришёл его живой захват

        public string Film(string folder, double width, double height, double fps, int pickMin)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            System.IO.Directory.CreateDirectory(folder);
            foreach (string mask in new string[] { "open_*.png", "leave_*.png" })
                foreach (string old in System.IO.Directory.GetFiles(folder, mask)) System.IO.File.Delete(old);
            var items = Demo.Windows(Switcher.DemoCount);
            int n = items.Count;
            view = Prefs.Is3D(Prefs.View) ? Prefs.View : "flip";
            mon = new Win.RECT { Right = (int)width, Bottom = (int)height };
            scale = 1;
            w = width; h = height;
            sticky = false;
            snap = false;
            StayCloaked = true;
            Fit(w, h);
            hint.Visibility = Visibility.Collapsed;
            phase = Phase.Shown;
            leaving = false; leaveTo = -1;

            Win.RECT[] homes = FilmHomes(items, w, h);
            int sel = pickMin > 0 ? items.FindIndex(delegate(WinItem it) { return it.Minimized; }) : Logic.StartIndex(n, true, false);
            if (sel < 0) { Hide(); return "film: свёрнутых окон в наборе нет (нужно --count 4 и больше)"; }
            // Свёрнутое окно после разворачивания стоит там, где стояло бы обычное окно с тем же номером, — поверх всех
            Win.RECT future = pickMin > 0 ? FilmSpot(items[sel], sel, w, h) : new Win.RECT();
            Win.RECT[] homesAfter = (Win.RECT[])homes.Clone();
            if (pickMin > 0) homesAfter[sel] = future;
            BitmapSource wall = Render(FilmDesk(items, homes, -1, false), w, h);
            BitmapSource before = Render(FilmDesk(items, homes, 0, true), w, h);
            BitmapSource after = Render(FilmDesk(items, homesAfter, sel, true), w, h);

            // Фон — как у настоящего показа: слой A — кадр монитора, слой B — размытые затемнённые обои
            monImage = before; monW = before.PixelWidth; monH = before.PixelHeight;
            MonitorGot = true;
            brushA.ImageSource = before;
            layerA.Visibility = Visibility.Visible;
            brushB.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
            brushB.Viewbox = new Rect(0, 0, 1, 1);
            brushB.ImageSource = wall;
            layerB.Visibility = Visibility.Visible;
            deskShown = true;

            Build(items, true);
            foreach (FlipCard c in cards)
            {
                Win.RECT r = homes[c.Index];
                if (r.Right <= r.Left) continue;
                // Окно на своём месте: «живая картинка» — то же окно, нарисованное в его размер
                c.HasHome = true;
                c.Rect = r;
                c.Home = Flip.Home(r.Left, r.Top, r.Right, r.Bottom, 0, 0, 1, w, h);
                c.Aspect = (r.Right - r.Left) / (double)(r.Bottom - r.Top);
                Flip.Front(view, c.Aspect, w, h, out c.FrontW, out c.FrontH);
                FrameworkElement el = Demo.Window(c.Item, r.Right - r.Left, r.Bottom - r.Top);
                el.Width = r.Right - r.Left; el.Height = r.Bottom - r.Top;
                Paint(c, Snapshot(el, r.Right - r.Left, r.Bottom - r.Top));
                c.ShowsLive = true;
                c.Cropped = false;
            }
            LiftHomes();
            if (pickMin > 0)
            {
                // Свёрнутое окно: на грани его неподвижная картинка, как из миниатюры DWM (не больше Still.MaxSide), и
                // предсказанное место — точное или мимо
                FlipCard c = cards[sel];
                int fw = future.Right - future.Left, fh = future.Bottom - future.Top, sw, sh;
                if (!Still.Fit(fw, fh, (int)w, (int)h, out sw, out sh)) { sw = fw; sh = fh; }
                FrameworkElement el = Demo.Window(c.Item, fw, fh);
                el.Width = fw; el.Height = fh;
                var still = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                el.Measure(new Size(fw, fh)); el.Arrange(new Rect(0, 0, fw, fh)); el.UpdateLayout();
                var box = new Border { Width = sw, Height = sh, Child = new Rectangle { Fill = new VisualBrush(el) { Stretch = Stretch.Fill } } };
                box.Measure(new Size(sw, sh)); box.Arrange(new Rect(0, 0, sw, sh)); box.UpdateLayout();
                still.Render(box);
                still.Freeze();
                ShowStill(c, still);
                Win.RECT guess = future;
                if (pickMin == 2)
                {
                    int gw = (int)Math.Round(fw * 0.9), gh = (int)Math.Round(fh * 0.9);
                    guess = new Win.RECT { Left = future.Left + 40, Top = future.Top - 30, Right = future.Left + 40 + gw, Bottom = future.Top - 30 + gh };
                }
                SetRestore(c, guess, false);
            }
            selected = sel;
            pos = Flip.StartPos(selected, n);
            target = Flip.Nearest(pos, selected, n); vel = 0;
            posFrom = pos;
            gliding = pos != target;
            Caption();
            awaiting = true;
            appearing = true;
            elapsed = 0;
            openEnd = Flip.OpenMs;
            typical = 1 / fps;
            Content = null;
            Place();
            Uncloak();

            var openFiles = new List<string>(); var openTimes = new List<double>();
            var leaveFiles = new List<string>(); var leaveTimes = new List<double>();
            var log = new System.Text.StringBuilder();
            double step = 1000 / fps, t = 0;
            FilmTrack track = new FilmTrack(this);
            FilmSave(folder, "open", openFiles, openTimes, t);
            track.Frame(t, layerB.Opacity);
            double first = Difference(FilmFrame(openFiles[0]), before);
            int settled = 0;
            for (int k = 1; k < 600 && settled < 3; k++)
            {
                double gap = k == 1 ? FilmLate * step : step;
                t += gap;
                typical = Pace.Typical(typical, gap / 1000);
                Motion(gap / 1000, t, gap);
                Place();
                FilmSave(folder, "open", openFiles, openTimes, t);
                track.Frame(t, layerB.Opacity);
                if (!appearing && pos == target) settled++;
            }
            log.Append("появление: кадров ").Append(openFiles.Count).Append(", ").Append(track.Line(w));

            // Уход: выбор сделан, выбранное окно летит на своё место
            Close(items[selected], null);
            var leave = new FilmTrack(this);
            double t0 = t;
            FilmSave(folder, "leave", leaveFiles, leaveTimes, 0);
            leave.Frame(0, layerB.Opacity);
            double liveAt = -1;
            for (int k = 1; k < 600 && phase == Phase.Leaving; k++)
            {
                t += step;
                typical = Pace.Typical(typical, step / 1000);
                Motion(step / 1000, t, step);
                if (restoring != null)
                {
                    // Окно «развернулось» под сценой на своём настоящем месте; чуть позже — его «живой захват»: то же окно
                    // в полный размер вместо неподвижной картинки
                    WatchRestore();
                    if (knownAt < 0 && t - t0 >= FilmRestoreMs) { Restored(restoring, future); liveAt = t - t0 + FilmLiveSteps * step; }
                    if (liveAt >= 0 && t - t0 >= liveAt - 1e-6 && !restoring.ShowsLive)
                    {
                        int fw = future.Right - future.Left, fh = future.Bottom - future.Top;
                        FrameworkElement el = Demo.Window(restoring.Item, fw, fh);
                        el.Width = fw; el.Height = fh;
                        Paint(restoring, Snapshot(el, fw, fh));
                        restoring.ShowsLive = true;
                        RestoreOpenMs = RestoreLiveMs = elapsed;
                    }
                }
                Place();
                // Кадр, в котором сцена убирается, на экран уже не попадает
                if (LeaveOver()) break;
                FilmSave(folder, "leave", leaveFiles, leaveTimes, t - t0);
                leave.Frame(t - t0, layerB.Opacity);
            }
            double last = Difference(FilmFrame(leaveFiles[leaveFiles.Count - 1]), after);
            log.Append("; уход: кадров ").Append(leaveFiles.Count).Append(", ").Append(leave.Line(w));
            if (pickMin > 0)
                log.Append("; свёрнутое окно: ").Append(RestoreFlew ? "летело на будущее место" : "не летело")
                    .Append(", предсказано ").Append(pickMin == 2 ? "мимо на " + Restore.Miss(RestoreGuess, future) + " точек" : "точно")
                    .Append(", «развернулось» на ").Append(RestoreKnownMs.ToString("0", inv)).Append(" мс ухода, живая картинка с ").Append(RestoreLiveMs.ToString("0", inv))
                    .Append(" мс, ждали после конца ухода ").Append(RestoreWaitedMs.ToString("0", inv)).Append(" мс, конечная поза против места окна ").Append(RestoreEndMiss).Append(" точек");
            log.Append("; первый кадр против кадра монитора: ").Append(first.ToString("0.000", inv))
                .Append(" (средняя разница по каналу, 0–255); последний кадр ухода против рабочего стола после перехода: ").Append(last.ToString("0.000", inv));
            FilmSheet(System.IO.Path.Combine(folder, "open.png"), openFiles, openTimes, "Появление — " + view + ", шаг " + step.ToString("0.0", inv) + " мс");
            FilmSheet(System.IO.Path.Combine(folder, "leave.png"), leaveFiles, leaveTimes, "Уход — " + view + ", шаг " + step.ToString("0.0", inv) + " мс");
            Hide();
            return "film " + view + ": " + log.ToString();
        }

        // Смещения карточек между соседними кадрами раскадровки: углы карточек на экране, наибольший сдвиг любого угла
        sealed class FilmTrack
        {
            readonly FlipView f;
            readonly Dictionary<FlipCard, double[]> prev = new Dictionary<FlipCard, double[]>();
            readonly Dictionary<FlipCard, double> prevOp = new Dictionary<FlipCard, double>();
            double prevBg = -1, prevT;
            public double Max, MaxAt, StartAt = -1, FirstMove, Early, LastMove, Bg, FirstBg = -1, Op, OpAt;
            public string MaxWho = "";

            public FilmTrack(FlipView f) { this.f = f; }

            public void Frame(double t, double bg)
            {
                double most = 0;
                string who = "";
                foreach (FlipCard c in f.cards)
                {
                    // Сдвиг почти невидимой карточки (непрозрачность меньше 5%, например перескок через шов круга) не в счёт
                    double[] now = null;
                    if (c.Opacity > 0.05)
                    {
                        now = new double[8];
                        for (int i = 0; i < 4; i++)
                        {
                            double z;
                            Flip.Corner(c.Now, i % 2 == 0 ? -0.5 : 0.5, i < 2 ? -0.5 : 0.5, f.w, f.h, out now[2 * i], out now[2 * i + 1], out z);
                        }
                    }
                    double[] was;
                    if (now != null && prev.TryGetValue(c, out was) && was != null)
                        for (int i = 0; i < 4; i++)
                        {
                            double d = Math.Sqrt((now[2 * i] - was[2 * i]) * (now[2 * i] - was[2 * i]) + (now[2 * i + 1] - was[2 * i + 1]) * (now[2 * i + 1] - was[2 * i + 1]));
                            if (d > most) { most = d; who = c.Item.Title; }
                        }
                    prev[c] = now;
                    double op;
                    if (prevOp.TryGetValue(c, out op) && Math.Abs(c.Opacity - op) > Op) { Op = Math.Abs(c.Opacity - op); OpAt = t; }
                    prevOp[c] = c.Opacity;
                }
                if (FirstBg < 0) FirstBg = bg;
                if (prevBg >= 0) Bg = Math.Max(Bg, Math.Abs(bg - prevBg));
                prevBg = bg;
                most /= f.w;
                if (most > 1e-5 && StartAt < 0) { StartAt = t; FirstMove = most; }
                if (StartAt >= 0 && t - StartAt <= 50 + 1e-6) Early = Math.Max(Early, most);
                if (most > Max) { Max = most; MaxAt = t; MaxWho = who; }
                LastMove = most;
                prevT = t;
            }

            public string Line(double w)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                Func<double, string> pc = delegate(double v) { return (v * 100).ToString("0.00", inv) + "%"; };
                return "движение с " + (StartAt < 0 ? "—" : StartAt.ToString("0", inv) + " мс") + ", первый сдвиг " + pc(FirstMove)
                    + " ширины, наибольший за первые 50 мс " + pc(Early) + ", наибольший вообще " + pc(Max) + " на " + MaxAt.ToString("0", inv) + " мс (" + MaxWho + ")"
                    + ", последний " + pc(LastMove) + ", фон на первом кадре " + FirstBg.ToString("0.000", inv) + ", наибольший шаг фона за кадр " + Bg.ToString("0.000", inv)
                    + ", наибольший шаг непрозрачности карточки за кадр " + Op.ToString("0.000", inv) + " на " + OpAt.ToString("0", inv) + " мс";
            }
        }

        void FilmSave(string folder, string name, List<string> files, List<double> times, double t)
        {
            string file = System.IO.Path.Combine(folder, name + "_" + files.Count.ToString("000") + ".png");
            Save(file);
            files.Add(file);
            times.Add(t);
        }

        static BitmapSource FilmFrame(string file)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(System.IO.Path.GetFullPath(file));
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }

        // Средняя разница двух картинок одного размера по каналу (0–255); размеры разные — −1
        static double Difference(BitmapSource a, BitmapSource b)
        {
            if (a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight) return -1;
            var fa = new FormatConvertedBitmap(a, PixelFormats.Bgra32, null, 0);
            var fb = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
            int stride = a.PixelWidth * 4;
            var pa = new byte[stride * a.PixelHeight];
            var pb = new byte[stride * a.PixelHeight];
            fa.CopyPixels(pa, stride, 0);
            fb.CopyPixels(pb, stride, 0);
            double sum = 0;
            for (int i = 0; i < pa.Length; i++) if (i % 4 != 3) sum += Math.Abs(pa[i] - pb[i]);
            return sum / (pa.Length / 4 * 3);
        }

        static BitmapSource Render(FrameworkElement el, double width, double height)
        {
            el.Measure(new Size(width, height));
            el.Arrange(new Rect(0, 0, width, height));
            el.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Math.Round(width), (int)Math.Round(height), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(el);
            bmp.Freeze();
            return bmp;
        }

        // «Домашние» места выдуманных окон: как обычные окна на рабочем столе, внахлёст; самое недавнее — крупнее всех
        // и по центру. Свёрнутым места нет. Прямоугольники — в целых точках.
        static Win.RECT[] FilmHomes(List<WinItem> items, double w, double h)
        {
            var r = new Win.RECT[items.Count];
            for (int i = 0; i < items.Count; i++) if (!items[i].Minimized) r[i] = FilmSpot(items[i], i, w, h);
            return r;
        }

        // Место выдуманного окна номер i (и будущее место свёрнутого — так же)
        static Win.RECT FilmSpot(WinItem item, int i, double w, double h)
        {
            double[,] spots = { { 0.50, 0.45, 0.50 }, { 0.30, 0.40, 0.40 }, { 0.73, 0.36, 0.38 }, { 0.40, 0.62, 0.34 }, { 0.68, 0.62, 0.36 },
                                { 0.22, 0.62, 0.30 }, { 0.82, 0.55, 0.28 }, { 0.56, 0.28, 0.32 }, { 0.36, 0.28, 0.28 } };
            int k = i % spots.GetLength(0);
            double a = item.Aspect > 0 ? item.Aspect : Flip.DefaultAspect;
            double ww = spots[k, 2] * w, hh = ww / a, room = (h - FilmBar) * 0.8;
            if (hh > room) { hh = room; ww = hh * a; }
            double cx = spots[k, 0] * w, cy = spots[k, 1] * (h - FilmBar);
            int l = (int)Math.Round(cx - ww / 2), t = (int)Math.Round(Math.Max(0, cy - hh / 2));
            int b = Math.Min((int)(h - FilmBar), t + (int)Math.Round(hh));
            return new Win.RECT { Left = l, Top = b - (int)Math.Round(hh), Right = l + (int)Math.Round(ww), Bottom = b };
        }

        // Условный рабочий стол: обои (цветные пятна, чтобы было видно размытие), окна по местам — снизу вверх, top — поверх
        // всех (−1 — по порядку списка: первое сверху), и панель задач. withWindows = false — одни обои, как кадр Progman.
        FrameworkElement FilmDesk(List<WinItem> items, Win.RECT[] homes, int top, bool withWindows)
        {
            var desk = new Canvas { Width = w, Height = h, ClipToBounds = true, Background = Ui.Solid(Tokens.Dark.Bg) }; // audit:ok — тёмная сцена поверх экрана, от темы Windows не зависит
            var sky = new LinearGradientBrush(Ui.Alpha(Tokens.Palette.Blue, 0.6), Ui.Alpha(Tokens.Palette.Purple, 0.45), 90);
            var fill = new Rectangle { Width = w, Height = h, Fill = sky };
            desk.Children.Add(fill);
            Color[] spots = { Tokens.Palette.Orange, Tokens.Palette.Pink, Tokens.Palette.Green, Tokens.Palette.Yellow };
            double[,] at = { { 0.12, 0.70, 0.30 }, { 0.80, 0.15, 0.26 }, { 0.55, 0.85, 0.22 }, { 0.35, 0.10, 0.12 } };
            for (int i = 0; i < spots.Length; i++)
            {
                double d = at[i, 2] * w;
                var e = new Ellipse { Width = d, Height = d, Fill = Ui.Solid(Ui.Alpha(spots[i], 0.55)) };
                Canvas.SetLeft(e, at[i, 0] * w - d / 2); Canvas.SetTop(e, at[i, 1] * h - d / 2);
                desk.Children.Add(e);
            }
            if (!withWindows) return desk;
            var order = new List<int>();
            for (int i = items.Count - 1; i >= 0; i--) if (i != top) order.Add(i);
            if (top >= 0) order.Add(top);
            foreach (int i in order)
            {
                Win.RECT r = homes[i];
                if (r.Right <= r.Left) continue;
                FrameworkElement el = Demo.Window(items[i], r.Right - r.Left, r.Bottom - r.Top);
                el.Width = r.Right - r.Left; el.Height = r.Bottom - r.Top;
                Canvas.SetLeft(el, r.Left); Canvas.SetTop(el, r.Top);
                desk.Children.Add(el);
            }
            var bar = new Border { Width = w, Height = FilmBar, Background = Ui.Solid(Tokens.Dark.Surface), BorderBrush = Ui.Solid(Tokens.Dark.EdgeTop), BorderThickness = new Thickness(0, 1, 0, 0) }; // audit:ok — тёмная сцена поверх экрана, от темы Windows не зависит
            Canvas.SetTop(bar, h - FilmBar);
            desk.Children.Add(bar);
            return desk;
        }

        // Контактный лист: кадры примерно через FilmSheetMs, под каждым — время от начала, мс
        static void FilmSheet(string file, List<string> files, List<double> times, string title)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var pickList = new List<int>();
            double next = 0;
            for (int i = 0; i < files.Count; i++)
                if (times[i] >= next - 1e-6 || i == files.Count - 1) { pickList.Add(i); next = Math.Max(next + FilmSheetMs, times[i] + FilmSheetMs * 0.5); }
            const int cols = 6;
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = cols };
            foreach (int i in pickList)
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(System.IO.Path.GetFullPath(files[i]));
                img.DecodePixelWidth = FilmThumb;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                var label = Ui.T(times[i].ToString("0", inv) + " мс · кадр " + i, Tokens.Size.Caption, Ui.Label2, FontWeights.Normal);
                label.Margin = new Thickness(0, 4, 0, 0);
                grid.Children.Add(new StackPanel { Margin = new Thickness(6), Children = { new Image { Source = img, Width = FilmThumb, Stretch = Stretch.Uniform }, label } });
            }
            var head = Ui.T(title, Tokens.Size.Body, Ui.Label, FontWeights.Normal);
            head.Margin = new Thickness(6, 6, 6, 2);
            var page = new StackPanel { Background = Ui.Bg, Children = { head, grid } };
            page.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size s = page.DesiredSize;
            page.Arrange(new Rect(0, 0, s.Width, s.Height));
            page.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(s.Width), (int)Math.Ceiling(s.Height), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(page);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = System.IO.File.Create(file)) enc.Save(f);
        }

        // --flipprobe: снимок сцены в середине прогона. Вместо фона с экрана — общий фон dev.txt: содержимое экрана
        // в файл не попадает.
        public void ProbeShot(string file)
        {
            var a = layerA.Visibility;
            var b = layerB.Visibility;
            layerA.Visibility = Visibility.Collapsed;
            layerB.Visibility = Visibility.Collapsed;
            try { Save(file); }
            finally { layerA.Visibility = a; layerB.Visibility = b; lastFrame = -1; }   // сам снимок долгий — в замер кадров он не идёт
        }

        void Save(string file)
        {
            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Math.Round(w), (int)Math.Round(h), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = System.IO.File.Create(file)) enc.Save(f);
        }

        // Для --flipprobe: разброс яркости кадра обоев (0–255) по редкой сетке точек; −1 — кадра нет. Сам кадр никуда не пишется.
        public double DesktopSpread()
        {
            var bmp = deskLive == null ? null : deskLive.Image as BitmapSource;
            if (bmp == null || deskLive.Frames == 0 || bmp.PixelWidth < 2 || bmp.PixelHeight < 2) return -1;
            var row = new byte[bmp.PixelWidth * 4];
            double sum = 0, sq = 0;
            int n = 0;
            for (int j = 0; j < 32; j++)
            {
                int y = (int)((j + 0.5) * bmp.PixelHeight / 32);
                bmp.CopyPixels(new Int32Rect(0, y, bmp.PixelWidth, 1), row, row.Length, 0);
                for (int i = 0; i < 32; i++)
                {
                    int o = (int)((i + 0.5) * bmp.PixelWidth / 32) * 4;
                    double l = 0.299 * row[o + 2] + 0.587 * row[o + 1] + 0.114 * row[o];
                    sum += l; sq += l * l; n++;
                }
            }
            double mean = sum / n;
            return Math.Sqrt(Math.Max(0, sq / n - mean * mean));
        }

        // Для --flipprobe: сколько карточек получили хоть один кадр
        public int CardsWithFrames
        {
            get { int k = 0; foreach (FlipCard c in cards) if (c.GotFrame) k++; return k; }
        }

        // Для --flipprobe: сколько карточек свёрнутых окон показывают картинку окна (а не плашку)
        public int StillCards
        {
            get { int k = 0; foreach (FlipCard c in cards) if (c.Item.Minimized && c.Still) k++; return k; }
        }

        // Для --flipprobe: картинка на карточке свёрнутого окна; null — на карточке плашка
        public BitmapSource StillOf(IntPtr hwnd)
        {
            foreach (FlipCard c in cards) if (c.Item.Hwnd == hwnd && c.Still) return c.Brush.ImageSource as BitmapSource;
            return null;
        }

        // Для --flipprobe --real: у скольких карточек с живой картинкой и местом на экране размер картинки совпал с видимой
        // рамкой окна (DWMWA_EXTENDED_FRAME_BOUNDS) с точностью до точки — тогда дома карточка ложится ровно на окно
        public void SizeMatch(out int same, out int total, out string odd)
        {
            same = 0; total = 0; odd = "";
            foreach (FlipCard c in cards)
            {
                if (!c.HasHome || c.Live == null || !c.ShowsLive) continue;
                total++;
                int rw = c.Rect.Right - c.Rect.Left, rh = c.Rect.Bottom - c.Rect.Top;
                if (Math.Abs(c.Live.Width - rw) <= 1 && Math.Abs(c.Live.Height - rh) <= 1) same++;
                else if (odd.Length < 200) odd += " " + c.Live.Width + "x" + c.Live.Height + "/" + rw + "x" + rh;
            }
        }

        // Для --flipprobe: какое будущее место предсказано у карточки свёрнутого окна (false — места нет: на другом
        // мониторе или не вышло)
        public bool RestoreOf(IntPtr window, out Win.RECT r, out bool toMax)
        {
            r = new Win.RECT(); toMax = false;
            foreach (FlipCard c in cards) if (c.Item.Hwnd == window && c.Restore) { r = c.RestoreRect; toMax = c.RestoreMax; return true; }
            return false;
        }

        // Для --flipprobe: сколько живых картинок открыто сейчас и сколько весит один их кадр (ширина × высота × 4 байта).
        // На каждый захват видеопамяти уходит несколько таких кадров: пул захвата (2), своя текстура и копия у WPF.
        public void LiveNow(out int count, out long bytes)
        {
            count = 0; bytes = 0;
            foreach (FlipCard c in cards)
                if (c.Live != null && !c.Live.Dead) { count++; bytes += (long)c.Live.Width * c.Live.Height * 4; }
        }
    }

    // --flipprobe: свои спрятанные цветные окна, сцена на них (тоже спрятанная), 3 секунды листания, замеры одной строкой
    static class FlipProbe
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);

        static Color Pixel(BitmapSource img, int x, int y)
        {
            var px = new byte[4];
            img.CopyPixels(new Int32Rect(Math.Max(0, Math.Min(x, img.PixelWidth - 1)), Math.Max(0, Math.Min(y, img.PixelHeight - 1)), 1, 1), px, 4, 0);
            return Color.FromRgb(px[2], px[1], px[0]);
        }

        static void Wait(double ms)
        {
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }

        // Порог показа — как у Switcher: таймер или проверка в каждом кадре, что раньше
        static void WaitDue(double ms)
        {
            var frame = new DispatcherFrame();
            var sw = Stopwatch.StartNew();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            EventHandler tick = null;
            Action due = delegate { t.Stop(); CompositionTarget.Rendering -= tick; frame.Continue = false; };
            tick = delegate { if (sw.Elapsed.TotalMilliseconds >= ms) due(); };
            t.Tick += delegate { due(); };
            t.Start();
            CompositionTarget.Rendering += tick;
            Dispatcher.PushFrame(frame);
        }

        // Шаг кадров показа одной строкой: промежутки (по времени показа кадра) при появлении, листании и уходе, работа
        // обработчика по частям, повторные вызовы Rendering, сборки мусора за показ, уровень отрисовки WPF
        static string Pacing(FlipView f)
        {
            int tier = RenderCapability.Tier >> 16;
            return "; шаг кадров, мс: появление [" + f.OpenGaps.Line() + "], листание [" + f.BrowseGaps.Line() + "], уход [" + f.LeaveGaps.Line()
                + "]; работа в кадре, мс средняя/худшая: " + f.WorkLine() + "; повторов Rendering " + f.Repeats
                + ", сборок мусора 0/1/2: " + f.Gc0 + "/" + f.Gc1 + "/" + f.Gc2 + ", уровень отрисовки " + tier
                + (RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly ? " (программно!)" : "");
        }

        static string Ms(string mark)
        {
            double v = Marks.Get(mark);
            return v < 0 ? "—" : v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        // --flipprobe --real: та же спрятанная сцена, но на настоящих окнах — список собирается так же, как у Switcher.
        // Окна только захватываются (без жёлтой рамки): ничего не активируется, не двигается и не сворачивается.
        // Показы подряд (первый — холодный), с задержкой показа, как у Alt+Tab; чётные закрываются выбором, нечётные — отменой,
        // четвёртый — отменой посреди полёта. Пишется и сколько окон свёрнуто, у скольких из них картинка из миниатюры DWM
        // (Iconic.cs; чужие окна при этом только читаются), и сколько весит кадр живых.
        public static int RunReal(int runs, out string line)
        {
            if (!Capture.Available) { line = "flipprobe real: нет — " + Capture.Problem; return 1; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Marks.Probe = true;
            FlipView flip = null;
            var sb = new System.Text.StringBuilder();
            try
            {
                flip = new FlipView { StayCloaked = true };
                bool failed = false;
                flip.Failed = delegate { failed = true; flip.Hide(); };
                flip.Prepare();
                // Где встанут свёрнутые окна, если их выбрать, — только чтение (GetWindowPlacement и размер миниатюры DWM):
                // ни одно окно не разворачивается
                {
                    bool fg0;
                    List<WinItem> all = Win.List(Prefs.AllDesktops, true, out fg0);
                    Win.RECT am = Win.ActiveMonitor();
                    int k = 0;
                    double spent = 0;
                    var pred = new System.Text.StringBuilder();
                    // Окна, сочтённые играми (Logic.GameWindow): их не захватываем и не открываем; видимые — сцена не показывается
                    var games = new System.Text.StringBuilder();
                    foreach (WinItem it in all)
                        if (it.Game) games.Append(games.Length > 0 ? ", " : "").Append(it.Proc).Append(it.Minimized ? " (свёрнуто)" : "")
                            .Append(Games.IsGame(it.Proc, Prefs.Extra) ? " — по имени процесса" : " — на весь экран");
                    sb.Append(" | окна-игры: ").Append(games.Length > 0 ? games.ToString() : "нет");
                    foreach (WinItem it in all)
                    {
                        if (!it.Minimized || it.Game) continue;
                        k++;
                        Win.RECT r;
                        bool toMax;
                        string how;
                        var pw = Stopwatch.StartNew();
                        bool got = Win.RestorePlace(it.Hwnd, flip.Hwnd, out r, out toMax, out how);
                        spent += pw.Elapsed.TotalMilliseconds;
                        int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
                        bool here = got && cx >= am.Left && cx < am.Right && cy >= am.Top && cy < am.Bottom;
                        pred.Append(" | свёрнутое ").Append(k).Append(" (").Append(Win.Describe(it.Hwnd)).Append("): ")
                            .Append(got ? r.Left + "," + r.Top + "–" + r.Right + "," + r.Bottom + " (" + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + ")"
                                + (toMax ? ", развернётся на весь экран" : ", обычным окном") + (here ? ", на мониторе показа — полетит" : ", на другом мониторе — погаснет") + "; " + how
                                : "места нет (" + how + ")");
                    }
                    sb.Append(" | предсказание места свёрнутых: ").Append(k).Append(" окон за ").Append(spent.ToString("0.00", inv)).Append(" мс").Append(pred);
                }
                bool allClosed = true;
                for (int k = 0; k < runs && !failed; k++)
                {
                    bool fgFirst;
                    Marks.Begin("probe");
                    List<WinItem> items = Win.List(Prefs.AllDesktops, true, out fgFirst);
                    Marks.Hit("list");
                    // Видимое окно игры в настоящем показе включило бы сетку вместо сцены (Logic.UseFlip); в пробе оно просто
                    // не попадает в сцену — и его картинка не попадает никуда
                    if (!Logic.UseFlip(true, items)) { items.RemoveAll(delegate(WinItem it) { return it.Game && !it.Minimized; }); fgFirst = false; }
                    if (items.Count == 0) { line = "flipprobe real: нет — настоящих окон нет"; return 1; }
                    Win.RECT mon = Win.ActiveMonitor();
                    flip.LiveOpened = 0; flip.LiveDied = 0; flip.SceneFrames = 0; flip.FrameSum = 0; flip.IdleFrames = 0; flip.IdleWorkMax = 0;
                    flip.Warm(mon, items, Logic.StartIndex(items.Count, fgFirst, false));
                    Marks.Hit("warm");
                    WaitDue(Switcher.RevealMs);
                    Marks.Hit("reveal");
                    int idleFrames = flip.IdleFrames; double idleWork = flip.IdleWorkMax;
                    int sel = Logic.StartIndex(items.Count, fgFirst, false);
                    flip.Open(items, sel, mon, false, false);
                    // Четвёртый показ закрывается посреди полёта: уход начинается с того места, где карточки сейчас
                    bool early = k == 3;
                    Wait(early ? 200 : 1200);
                    bool cancel = k % 2 == 1, closed = false;
                    int flew = flip.Flew, homes = flip.Homes, opened = flip.LiveOpened, liveNow, minimized = items.FindAll(delegate(WinItem it) { return it.Minimized; }).Count;
                    long liveBytes;
                    flip.LiveNow(out liveNow, out liveBytes);
                    int stillCards = flip.StillCards, stillShot = flip.StillShot, stillCached = flip.StillCached, stillFailed = flip.StillFailed;
                    double stillMs = flip.StillMsMax;
                    string stillWhy = flip.StillWhy;
                    int sizeSame, sizeAll;
                    string sizeOdd;
                    flip.SizeMatch(out sizeSame, out sizeAll, out sizeOdd);
                    double goAfter = flip.GoAfter, holdGap = flip.HoldGapMax, firstLate = flip.FirstLateAt;
                    int lateMoving = flip.LateMoving;
                    flip.Close(cancel ? null : items[sel], delegate { closed = true; });
                    var limit = Stopwatch.StartNew();
                    while (!closed && limit.ElapsedMilliseconds < 2000) Wait(10);
                    allClosed &= closed;
                    sb.Append(" | показ ").Append(k + 1).Append(k == 0 ? " (холодный" : " (тёплый").Append(cancel ? ", отмена" : ", выбор").Append(early ? " посреди полёта)" : ")")
                        .Append(": окон ").Append(items.Count).Append(" (свёрнутых ").Append(minimized).Append(", из них с картинкой ").Append(stillCards)
                        .Append(": путь А — миниатюра DWM, снято сейчас ").Append(stillShot).Append(stillShot > 0 ? " (дольше всего " + stillMs.ToString("0", inv) + " мс)" : "")
                        .Append(", готовых уже при построении сцены ").Append(stillCached).Append(", не вышло ").Append(stillFailed).Append(stillFailed > 0 ? " — " + stillWhy : "").Append(")")
                        .Append(", живых открыто ").Append(opened)
                        .Append(", живых к уходу ").Append(liveNow).Append(" (кадр ").Append((liveBytes / 1048576.0).ToString("0", inv)).Append(" МБ)")
                        .Append(", со своих мест ").Append(flew).Append(" из ").Append(homes)
                        .Append("; мс от нажатия: список ").Append(Ms("list")).Append(", захват фона начат ").Append(Ms("warm"))
                        .Append(", порог показа ").Append(Ms("reveal")).Append(" (кадров до него ").Append(idleFrames).Append(", худшая работа ").Append(idleWork.ToString("0", inv)).Append(" мс)").Append(", карточки ").Append(Ms("cards")).Append(", захваты ").Append(Ms("captures"))
                        .Append(", сцена построена ").Append(Ms("built")).Append(", первый кадр сцены ").Append(Ms("frame1"))
                        .Append(", раскрыта бы ").Append(Ms("uncloak")).Append(", первый видимый кадр ").Append(Ms("seen"))
                        .Append(", живой у выбранного ").Append(Ms("live-sel")).Append(", живые у всех ").Append(Ms("live-all"))
                        .Append(", кадр монитора ").Append(Ms("monitor")).Append(", размытые обои ").Append(Ms("desk")).Append(", полёт закончен ").Append(Ms("landed"))
                        .Append("; движение пошло через ").Append(goAfter < 0 ? "—" : goAfter.ToString("0", inv)).Append(" мс после первого видимого кадра (худший промежуток до этого ")
                        .Append(holdGap.ToString("0", inv)).Append(" мс), опоздавших кадров уже в полёте появления ").Append(lateMoving)
                        .Append(firstLate >= 0 ? " (первый через " + firstLate.ToString("0", inv) + " мс после первого видимого)" : "")
                        .Append("; живая картинка по размеру совпала с видимой рамкой окна у ").Append(sizeSame).Append(" из ").Append(sizeAll).Append(sizeOdd.Length > 0 ? " (картинка/рамка:" + sizeOdd + ")" : "")
                        .Append("; доля полёта на первом видимом кадре ").Append(flip.FirstT.ToString("0.00", inv))
                        .Append(", кадров полёта ").Append(flip.FlightFrames).Append(", средний промежуток ").Append((flip.SceneFrames > 0 ? flip.FrameSum / flip.SceneFrames : 0).ToString("0.0", inv)).Append(" мс")
                        .Append(", худший промежуток за 1 с ").Append(flip.GapMax.ToString("0", inv)).Append(" мс, худшая работа в кадре ").Append(flip.WorkMax.ToString("0", inv))
                        .Append(" мс")
                        .Append("; уход: кадров ").Append(flip.LeaveFrames).Append(", доля на первом ").Append(flip.LeaveFirstT.ToString("0.00", inv))
                        .Append(", убрано через ").Append(Marks.Get("hidden") >= 0 && Marks.Get("close") >= 0 ? (Marks.Get("hidden") - Marks.Get("close")).ToString("0", inv) : "—").Append(" мс")
                        .Append(Pacing(flip));
                    Wait(300);
                }
                int left = LiveWindow.Count, helpers = IconicShot.Count;
                bool ok = !failed && flip.Errors == 0 && left == 0 && helpers == 0 && allClosed;
                line = (ok ? "flipprobe real: ok" : "flipprobe real: нет") + ", ошибок " + flip.Errors + ", захватов после закрытия " + left + ", вспомогательных окон " + helpers
                    + ", картинок свёрнутых в памяти " + Stills.Count + " (" + (Stills.Bytes / 1048576.0).ToString("0.0", inv) + " МБ)" + sb.ToString();
                return ok ? 0 : 1;
            }
            catch (Exception e) { line = "flipprobe real: нет — " + e.GetType().Name + ": " + e.Message + sb.ToString(); return 1; }
            finally
            {
                try { if (flip != null) { flip.Hide(); flip.Close(); } } catch { }
            }
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPLACEMENT { public int Length, Flags, ShowCmd; public Win.POINT MinPos, MaxPos; public Win.RECT Normal; }
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Win.RECT value, int size);

        // Своё проверочное окно для разворачивания, спрятанное DWM ещё до показа, без фокуса и не в Alt+Tab: framed — с обычной
        // рамкой (невидимые поля для растягивания), tool — WS_EX_TOOLWINDOW (место окна тогда в координатах экрана, а не
        // рабочей области), max — свёрнуто из развёрнутого (развернётся на весь экран). Показано и свёрнуто; null — не спряталось или не свернулось (тогда не показываем).
        static Window MinimizedProbe(string title, Color color, Rect at, bool framed, bool tool, bool max, List<Window> wins, out IntPtr hwnd)
        {
            var bar = new Border { Background = new SolidColorBrush(Tokens.Palette.Yellow), Height = 40, VerticalAlignment = VerticalAlignment.Top };
            var win = new Window
            {
                Left = at.Left, Top = at.Top, Width = at.Width, Height = at.Height, WindowStartupLocation = WindowStartupLocation.Manual,
                WindowStyle = framed ? WindowStyle.SingleBorderWindow : WindowStyle.None, ResizeMode = framed ? ResizeMode.CanResize : ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Topmost = false, Title = title,
                Content = new Grid { Background = new SolidColorBrush(color), Children = { bar } }
            };
            hwnd = new WindowInteropHelper(win).EnsureHandle();
            long ex = GetWindowLongPtr(hwnd, -20).ToInt64() | Logic.WS_EX_NOACTIVATE;
            if (tool) ex |= Logic.WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, -20, new IntPtr(ex));
            int on = 1, cloaked;
            if (DwmSetWindowAttribute(hwnd, 13, ref on, 4) != 0 || DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) != 0 || cloaked == 0) return null;
            win.Show();
            wins.Add(win);
            Wait(300);   // окно нарисовалось: эту поверхность DWM и покажет в миниатюре
            if (!max) ShowWindow(hwnd, 7);   // SW_SHOWMINNOACTIVE
            else
            {
                // Свернуть с пометкой «разворачивать на весь экран» (WPF_RESTORETOMAXIMIZED), ни разу не выводя окно вперёд:
                // развернуть его без фокуса нельзя
                var wp = new WINDOWPLACEMENT { Length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
                if (!GetWindowPlacement(hwnd, ref wp)) return null;
                wp.Flags |= 2; wp.ShowCmd = 7;
                if (!SetWindowPlacement(hwnd, ref wp)) return null;
            }
            return IsIconic(hwnd) ? win : null;
        }

        static string R(Win.RECT r) { return r.Left + "," + r.Top + "–" + r.Right + "," + r.Bottom; }

        // Показы с выбором своего свёрнутого окна — как Alt+Tab к нему: окно разворачивается (как в Win.Activate, только без
        // вывода вперёд — SW_SHOWNOACTIVATE), сцена уходит. Окна: с рамкой; без рамки (свёрнутое из основного прогона), которое
        // «приложение» разворачивает с опозданием; с рамкой, свёрнутое из развёрнутого; без рамки, которое не разворачивается
        // вовсе (сцена ждёт не дольше Restore.WaitMs). Все окна остаются спрятанными; фокус ни у кого не отбирается.
        static bool RestoreRuns(FlipView flip, List<WinItem> items, List<Window> wins, Win.RECT mon, int mi, out string line)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder("; разворачивание свёрнутого:");
            line = "";
            var area = SystemParameters.WorkArea;
            IntPtr ha, hc, hd;
            if (MinimizedProbe("smena.txt flipprobe restore A", Tokens.Palette.Blue, new Rect(area.Left + 120, area.Top + 90, 720, 460), true, false, false, wins, out ha) == null
                || MinimizedProbe("smena.txt flipprobe restore C", Tokens.Palette.Pink, new Rect(area.Left + 60, area.Top + 60, 640, 420), true, false, true, wins, out hc) == null
                || MinimizedProbe("smena.txt flipprobe restore D", Tokens.Palette.Purple, new Rect(area.Left + 300, area.Top + 200, 500, 360), false, true, false, wins, out hd) == null)
            { line = sb.Append(" нет — проверочное окно не спряталось или не свернулось").ToString(); return false; }
            var show = new List<WinItem>(items);
            show.Add(new WinItem { Hwnd = ha, Title = "Проверочное окно с рамкой — smena.txt", Key = "flipprobe", Minimized = true });
            show.Add(new WinItem { Hwnd = hc, Title = "Проверочное развёрнутое окно — smena.txt", Key = "flipprobe", Minimized = true });
            show.Add(new WinItem { Hwnd = hd, Title = "Проверочное окно, которое не разворачивается — smena.txt", Key = "flipprobe", Minimized = true });
            string[] names = { "с рамкой", "без рамки, разворачивается через 200 мс", "с рамкой, на весь экран", "не разворачивается" };
            int[] at = { items.Count, mi, items.Count + 1, items.Count + 2 };
            double[] lateMs = { 0, 200, 0, -1 };
            bool ok = true;
            IntPtr fg0 = GetForegroundWindow();
            for (int k = 0; k < at.Length; k++)
            {
                foreach (WinItem it in show) it.Minimized = IsIconic(it.Hwnd);
                int sel = at[k];
                IntPtr h = show[sel].Hwnd;
                flip.Warm(mon, show, sel);
                Wait(200);
                flip.Open(show, sel, mon, false, false);
                Wait(700);
                Win.RECT guess;
                bool toMax, known = flip.RestoreOf(h, out guess, out toMax);
                var sw = Stopwatch.StartNew();
                double restoredAt = -1;
                DispatcherTimer late = null;
                if (lateMs[k] == 0) { ShowWindow(h, 4); restoredAt = 0; }   // SW_SHOWNOACTIVATE
                else if (lateMs[k] > 0)
                {
                    late = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(lateMs[k]) };
                    late.Tick += delegate { late.Stop(); ShowWindow(h, 4); restoredAt = sw.Elapsed.TotalMilliseconds; };
                    late.Start();
                }
                bool closed = false;
                double hiddenAt = -1;
                flip.Close(show[sel], delegate { closed = true; hiddenAt = sw.Elapsed.TotalMilliseconds; });
                while (!closed && sw.ElapsedMilliseconds < 2000) Wait(10);
                if (late != null) { late.Stop(); while (restoredAt < 0 && sw.ElapsedMilliseconds < 2000) Wait(10); }
                int cloaked;
                bool hidden = DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0;
                Win.RECT real = new Win.RECT();
                bool got = !IsIconic(h) && IsWindowVisible(h) && DwmGetWindowAttribute(h, 9, out real, Marshal.SizeOf(typeof(Win.RECT))) == 0;
                if (!got) real = new Win.RECT();
                int miss = got && known ? Restore.Miss(guess, real) : -1;
                bool zoomed = IsZoomed(h);
                bool never = lateMs[k] < 0;
                // Разворачивалось: карточка летела, сцена скрылась после разворачивания (и не раньше конца анимации),
                // предсказание и конечная поза — с точностью до 2 точек. Не разворачивалось: сцена ждала Restore.WaitMs
                // и убралась сама.
                bool fine = closed && known && flip.RestoreFlew && hidden && flip.Errors == 0
                    && (never ? flip.RestoreKnownMs < 0 && flip.RestoreWaitedMs >= Restore.WaitMs - 20 && flip.RestoreWaitedMs <= Restore.WaitMs + 20
                              : got && flip.RestoreKnownMs >= 0 && hiddenAt > restoredAt && miss >= 0 && miss <= 2 && flip.RestoreEndMiss >= 0 && flip.RestoreEndMiss <= 2
                                && flip.RestoreFrames >= 10 && zoomed == toMax && flip.RestoreWaitedMs <= Restore.WaitMs + 1);
                ok &= fine;
                sb.Append(" [").Append(names[k]).Append(fine ? ": ok" : ": нет")
                    .Append(", предсказано ").Append(known ? R(guess) + (toMax ? " (на весь экран)" : "") : "—")
                    .Append(never ? "" : ", встало " + (got ? R(real) + (zoomed ? " (развёрнуто)" : "") : "—") + ", расхождение " + miss + " точ.")
                    .Append(", кадров ухода ").Append(flip.RestoreFrames)
                    .Append(", конечная поза против рамки окна ").Append(flip.RestoreEndMiss).Append(" точ.")
                    .Append(never ? "" : ", развернулось на " + restoredAt.ToString("0", inv) + " мс, сцена узнала на " + flip.RestoreKnownMs.ToString("0", inv) + " мс ухода")
                    .Append(", скрыта на ").Append(hiddenAt.ToString("0", inv)).Append(" мс, ждали после конца ухода ").Append(flip.RestoreWaitedMs.ToString("0", inv)).Append(" мс")
                    .Append(never ? "" : ", живой захват заведён на " + flip.RestoreOpenMs.ToString("0", inv) + " мс, кадр на карточке с " + flip.RestoreLiveMs.ToString("0", inv) + " мс")
                    .Append(hidden ? ", окно спрятано" : ", окно НЕ спрятано").Append("]");
                Wait(150);
            }
            bool fgSame = GetForegroundWindow() == fg0;
            ok &= fgSame;
            sb.Append(fgSame ? ", фокус не тронут" : ", фокус сменился!");
            line = sb.ToString();
            return ok;
        }

        // count — сколько проверочных окон (по умолчанию 5 небольших); больше пяти — окна почти во весь экран, как развёрнутые
        // настоящие: так видно, сколько живых картинок сцена тянет без рывков и сколько они весят
        public static int Run(string png, int count, out string line)
        {
            if (!Capture.Available) { line = "flipprobe: нет — " + Capture.Problem; return 1; }
            Color[] colors = { Tokens.Palette.Orange, Tokens.Palette.Green, Tokens.Palette.Blue, Tokens.Palette.Pink, Tokens.Palette.Purple };
            double[,] sizes = { { 640, 400 }, { 520, 520 }, { 800, 450 }, { 420, 600 }, { 700, 380 } };
            bool big = count > colors.Length;
            if (count <= 0) count = colors.Length;
            var wins = new List<Window>();
            var bars = new List<Border>();
            var items = new List<WinItem>();
            FlipView flip = null;
            DispatcherTimer blink = null;
            try
            {
                var area = SystemParameters.WorkArea;
                for (int i = 0; i < count; i++)
                {
                    var bar = new Border { Background = new SolidColorBrush(Tokens.Palette.Yellow), Height = 40, VerticalAlignment = VerticalAlignment.Top };
                    var body = new Grid { Background = new SolidColorBrush(colors[i % colors.Length]), Children = { bar } };
                    double ww = big ? area.Width * (0.7 + 0.3 * (i % 4) / 3) : sizes[i, 0], wh = big ? area.Height * (0.7 + 0.3 * ((i + 1) % 3) / 2) : sizes[i, 1];
                    var win = new Window
                    {
                        Left = big ? area.Left + (area.Width - ww) * (i % 5) / 4 : area.Left + 40 + i * 90, Top = big ? area.Top + (area.Height - wh) * (i % 3) / 2 : area.Top + 40 + i * 60, Width = ww, Height = wh,
                        WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false, ShowActivated = false, Topmost = false, Content = body, Title = "smena.txt flipprobe " + i
                    };
                    // Как в --capture: окно стоит на экране, но спрятано DWM ещё до показа; не спряталось — не показываем
                    IntPtr hwnd = new WindowInteropHelper(win).EnsureHandle();
                    long ex = GetWindowLongPtr(hwnd, -20).ToInt64();
                    SetWindowLongPtr(hwnd, -20, new IntPtr(ex | Logic.WS_EX_TOOLWINDOW | Logic.WS_EX_NOACTIVATE));
                    int on = 1, cloaked;
                    if (DwmSetWindowAttribute(hwnd, 13, ref on, 4) != 0 || DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) != 0 || cloaked == 0)
                    { line = "flipprobe: нет — не удалось спрятать проверочное окно, показывать его не стали"; return 1; }
                    win.Show();
                    wins.Add(win);
                    bars.Add(bar);
                    items.Add(new WinItem { Hwnd = hwnd, Title = "Проверочное окно " + (i + 1) + " — smena.txt", Key = "flipprobe" });
                }
                // Рисунок меняется раз в 100 мс — кадры идут
                int ticks = 0;
                blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                blink.Tick += delegate
                {
                    ticks++;
                    for (int i = 0; i < bars.Count; i++) bars[i].Width = 40 + (ticks * 23 + i * 50) % 300;
                };
                blink.Start();

                Win.RECT mon = FlipView.MonitorOf(items[0].Hwnd);
                // Одно своё окно сворачивается (спрятанным, без анимации на экране и без фокуса): у его карточки должна быть
                // картинка окна, а не плашка. Чужие окна проверка не трогает.
                int mi = items.Count >= 3 ? 2 : -1;
                if (mi >= 0)
                {
                    Wait(300);   // окно успело нарисоваться: эту поверхность DWM и запомнит
                    ShowWindow(items[mi].Hwnd, 7);   // SW_SHOWMINNOACTIVE
                    if (!IsIconic(items[mi].Hwnd)) { line = "flipprobe: нет — своё проверочное окно не свернулось"; return 1; }
                    items[mi].Minimized = true;
                }
                flip = new FlipView { StayCloaked = true };
                bool failed = false;
                flip.Failed = delegate { failed = true; flip.Hide(); };
                flip.Prepare();

                // Несколько коротких показов подряд: захваты не должны копиться; картинка свёрнутого со второго — из памяти
                int stillQuick = 0;
                for (int k = 0; k < 3; k++)
                {
                    flip.Warm(mon, items, 0);
                    Wait(60);
                    flip.Open(items, 0, mon, false, false);
                    Wait(120);
                    if (k == 2) stillQuick = flip.StillCached;
                    flip.Hide();
                }
                int leftAfterQuick = LiveWindow.Count, helpersAfterQuick = IconicShot.Count;
                // Основной показ снимает свёрнутое окно заново: так в строке — время самого снимка
                if (mi >= 0) Stills.Forget(items[mi].Hwnd);
                flip.SceneFrames = 0; flip.FrameSum = 0; flip.FrameWorst = 0; flip.LiveOpened = 0; flip.LiveDied = 0;

                // Основной прогон: 3 секунды, выбор каждые 400 мс. Отметки времени — от начала подготовки (как от нажатия)
                Marks.Probe = true;
                Marks.Begin("probe");
                flip.IdleFrames = 0; flip.IdleWorkMax = 0;
                flip.Warm(mon, items, 0);
                Wait(200);
                int idleFrames = flip.IdleFrames;
                double idleWork = flip.IdleWorkMax;
                flip.Open(items, 0, mon, false, false);
                double openMs = flip.OpenMs;
                Wait(150);
                int flew = flip.Flew, homes = flip.Homes;
                bool monitor = flip.MonitorGot;
                string monNote = flip.MonitorNote;
                var sw = Stopwatch.StartNew();
                int sel = 0;
                bool saved = false;
                while (sw.ElapsedMilliseconds < 3000 && !failed)
                {
                    Wait(400);
                    sel = (sel + 1) % items.Count;
                    flip.Select(sel, true);
                    if (png != null && !saved && sw.ElapsedMilliseconds > 1500) { Wait(250); flip.ProbeShot(png); saved = true; }
                }
                int withFrames = flip.CardsWithFrames, liveNow;
                long liveBytes;
                flip.LiveNow(out liveNow, out liveBytes);
                // Свёрнутое окно: на карточке его картинка — середина цвета окна, полоса сверху посередине жёлтая
                bool stillOk = mi < 0;
                string still = "";
                if (mi >= 0)
                {
                    BitmapSource img = flip.StillOf(items[mi].Hwnd);
                    if (img == null) still = ", свёрнутое окно: картинки нет (" + (flip.StillWhy.Length > 0 ? flip.StillWhy : "плашка") + ")";
                    else
                    {
                        // Полоса — верхние 40 точек окна: середина полосы (20 точек) в долях высоты окна, а не одной высоты 450
                        double winH = wins[mi].ActualHeight > 0 ? wins[mi].ActualHeight : wins[mi].Height;
                        Color mid = Pixel(img, img.PixelWidth / 2, img.PixelHeight / 2), bar = Pixel(img, img.PixelWidth / 2, (int)(img.PixelHeight * 20 / winH));
                        stillOk = CaptureMath.Near(mid, colors[mi % colors.Length], 12) && CaptureMath.Near(bar, Tokens.Palette.Yellow, 12);
                        still = ", свёрнутое окно: картинка есть, путь А (миниатюра DWM во вспомогательном окне), снята за "
                            + flip.StillMsMax.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " мс, " + img.PixelWidth + "x" + img.PixelHeight
                            + (stillOk ? ", цвета совпали" : ", цвета не совпали (" + mid + ", " + bar + ")")
                            + ", в повторных показах из памяти " + stillQuick;
                    }
                }
                string first = ", мс от подготовки: показ " + Ms("open") + " (до него кадров " + idleFrames + ", худшая работа " + idleWork.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " мс)"
                    + ", раскрыта бы " + Ms("uncloak") + ", первый видимый кадр " + Ms("seen") + ", живые у всех " + Ms("live-all") + ", полёт закончен " + Ms("landed")
                    + "; за первую секунду: худший промежуток " + flip.GapMax.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                    + " мс, худшая работа в кадре " + flip.WorkMax.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " мс"
                    + "; живых " + liveNow + ", кадр " + (liveBytes / 1048576.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " МБ";
                int frames = flip.SceneFrames, opened = flip.LiveOpened, died = flip.LiveDied;
                double avg = frames > 0 ? flip.FrameSum / frames : 0, worst = flip.FrameWorst;
                double spread = flip.DesktopSpread();
                string desk = flip.DesktopNote + (spread >= 0 ? ", разброс яркости " + spread.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : ""), path = flip.PathNote;
                bool closed = false;
                flip.Close(null, delegate { closed = true; });
                var limit = Stopwatch.StartNew();
                while (!closed && limit.ElapsedMilliseconds < 2000) Wait(20);
                int leftAfter = LiveWindow.Count, helpersAfter = IconicShot.Count, liveWanted = items.Count - (mi >= 0 ? 1 : 0);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                first += Pacing(flip);
                // Выбор свёрнутого окна: карточка летит на его будущее место, сцена ждёт разворачивания
                string restoreLine = "";
                bool restoreOk = mi < 0 || failed || RestoreRuns(flip, items, wins, mon, mi, out restoreLine);
                first += restoreLine;
                string info = "кадров сцены " + frames + ", интервал средний " + avg.ToString("0.0", inv) + " мс, худший " + worst.ToString("0.0", inv) + " мс"
                    + ", показ собран за " + openMs.ToString("0.0", inv) + " мс, со своих мест полетели " + flew + " из " + homes
                    + ", живых открыто " + opened + ", умерло " + died + ", кадры у своих окон " + withFrames + " из " + liveWanted + still
                    + ", кадр монитора: " + monNote + ", рабочий стол: " + desk + ", путь живых окон " + (path.Length > 0 ? path : "—")
                    + ", захватов после быстрых показов " + leftAfterQuick + ", после закрытия " + leftAfter
                    + ", вспомогательных окон после быстрых показов " + helpersAfterQuick + ", после закрытия " + helpersAfter + (closed ? "" : ", уход не закончился") + first;
                bool ok = !failed && flip.Errors == 0 && withFrames == liveWanted && stillOk && leftAfter == 0 && leftAfterQuick == 0 && helpersAfter == 0 && helpersAfterQuick == 0 && closed
                    && restoreOk && LiveWindow.Count == 0 && IconicShot.Count == 0;
                line = (ok ? "flipprobe: ok, " : "flipprobe: нет — ") + info;
                return ok ? 0 : 1;
            }
            catch (Exception e) { line = "flipprobe: нет — " + e.GetType().Name + ": " + e.Message; return 1; }
            finally
            {
                if (blink != null) blink.Stop();
                try { if (flip != null) { flip.Hide(); flip.Close(); } } catch { }
                foreach (Window win in wins) { try { win.Close(); } catch { } }
            }
        }
    }
}
