// smena.txt. Aside.cs — «отойти в сторону», пока идёт игра. Когда настройка «Не вмешиваться в играх» включена и
// запущена игра под античитом (Games.Shielded), впереди окно на весь экран или core.txt сказал, что идёт игра
// (core.family.game), smena.txt полностью бездействует: перехватчик клавиатуры снят, Alt+Tab системный, все захваты
// и вспомогательные окна закрыты, картинки окон забыты, слежение за окнами (SetWinEventHook) снято, ни одно чужое окно
// и ни один чужой процесс не трогаются. Остаются связь с core.txt (локальный сокет) и опрос раз в PollMs: какие
// процессы запущены (только имена из снимка списка процессов, без открытия самих процессов) и что впереди.
// Поводов нет AsideWatch.QuietMs подряд — всё возвращается само. Логика — AsideWatch и Games в Model.cs.
// Здесь же счётчики обращений к Windows (Calls) — по ним --asideprobe проверяет, что в стороне их нет.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace DevTxt.Smena
{
    // Сколько раз программа обращалась к чужим окнам и процессам и к своим перехватчикам — для --asideprobe
    static class Calls
    {
        public static int OpenProcess, OpenToken, Attach, SendInput, Captures, Thumbs, HookOn, HookOff, WatchOn, WatchOff, Snapshots;

        public static void Add(ref int counter) { Interlocked.Increment(ref counter); }

        // Обращения к чужому — то, чего в стороне быть не должно
        public static string Touches()
        {
            return "OpenProcess " + OpenProcess + ", OpenProcessToken " + OpenToken + ", AttachThreadInput " + Attach + ", SendInput " + SendInput
                + ", захватов " + Captures + ", миниатюр DWM " + Thumbs;
        }

        public static long Sum() { return (long)OpenProcess + OpenToken + Attach + SendInput + Captures + Thumbs; }
    }

    // Какие процессы запущены — только имена из снимка списка процессов (CreateToolhelp32Snapshot). Снимок не открывает
    // ни одного процесса: Windows отдаёт копию своего списка, хэндл — только у самого снимка, и он сразу закрывается.
    static class Procs
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint Size, Usage, Pid;
            public IntPtr Heap;
            public uint Module, Threads, Parent;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] static extern bool First(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] static extern bool Next(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        const uint SnapProcess = 2;   // TH32CS_SNAPPROCESS
        const int FreshMs = 250;      // чаще снимок не делается, даже если имени окна в нём нет

        static readonly object gate = new object();
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static Dictionary<uint, string> map = new Dictionary<uint, string>();
        static double mapAt = -1e9;

        // Подмена списка запущенных процессов — только в --asideprobe и --headless (ставит Program): иначе не действует.
        // FakeList задаёт сама проба; SMENA_FAKE_PROCS — имена через запятую или путь к файлу с ними (читается при каждом опросе).
        public static bool AllowFake;
        public static volatile string[] FakeList;

        // Снимок: номер процесса → имя (Games.Norm). Пустой — снимок не удался.
        public static Dictionary<uint, string> Take()
        {
            var d = new Dictionary<uint, string>();
            IntPtr snap = CreateToolhelp32Snapshot(SnapProcess, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return d;
            Calls.Add(ref Calls.Snapshots);
            try
            {
                var e = new PROCESSENTRY32 { Size = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                for (bool ok = First(snap, ref e); ok; ok = Next(snap, ref e)) d[e.Pid] = Games.Norm(e.Exe);
            }
            finally { CloseHandle(snap); }
            lock (gate) { map = d; mapAt = clock.Elapsed.TotalMilliseconds; }
            return d;
        }

        // Имена запущенных процессов: подмена (если разрешена и задана) или настоящий снимок
        public static List<string> Running()
        {
            string[] fake = Fake();
            if (fake != null) return new List<string>(fake);
            return new List<string>(Take().Values);
        }

        static string[] Fake()
        {
            if (!AllowFake) return null;
            string[] list = FakeList;
            if (list != null) return list;
            string v = Environment.GetEnvironmentVariable("SMENA_FAKE_PROCS");
            if (string.IsNullOrEmpty(v)) return null;
            try { if (File.Exists(v)) v = File.ReadAllText(v); } catch { return new string[0]; }
            return v.Split(new char[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // Имя процесса по номеру — из последнего снимка; нет в нём — снимок заново (не чаще FreshMs). "" — не узнать.
        public static string NameOf(uint pid)
        {
            string name;
            lock (gate)
            {
                if (map.TryGetValue(pid, out name)) return name;
                if (clock.Elapsed.TotalMilliseconds - mapAt < FreshMs) return "";
            }
            try { Take().TryGetValue(pid, out name); } catch { }
            return name ?? "";
        }
    }

    static class Aside
    {
        public const int PollMs = 3000;   // как часто смотрим список процессов и окно впереди

        static readonly AsideWatch watch = new AsideWatch();
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static Dispatcher ui;
        static Timer timer;
        static int polling;

        public static volatile bool On;               // smena.txt в стороне (читается из любого потока)
        public static Func<bool> CoreGame;            // core.txt сказал, что идёт игра (core.family.game); в потоке опроса
        public static Func<bool> Fullscreen = Win.GameFrontAhead;   // впереди игра или окно на весь экран; в потоке опроса
        public static Func<IntPtr, bool> FullscreenWindow = Win.GameFront;   // это окно — игра или на весь экран; в потоке окна
        public static event Action Changed;           // вошли, вышли или сменился повод; в потоке окна
        public static int Polls;

        public static AsideReason Reason { get { return watch.Reason; } }
        public static string What { get { return watch.What; } }

        // Почему в стороне — одной строкой; "" — не в стороне
        public static string Text { get { return On ? Logic.AsideText(watch.Reason) : ""; } }

        // Опрос — лёгкий таймер в пуле потоков, не в потоке окна; первый — сразу
        public static void Start(Dispatcher dispatcher)
        {
            if (timer != null) return;
            ui = dispatcher;
            Prefs.Changed += Recheck;
            timer = new Timer(Poll, null, 0, PollMs);
        }

        public static void Stop()
        {
            if (timer != null) { timer.Dispose(); timer = null; }
            Prefs.Changed -= Recheck;
        }

        // Посмотреть сейчас, не дожидаясь таймера (поменялись настройки, core.family)
        public static void Recheck() { if (timer != null) ThreadPool.QueueUserWorkItem(Poll); }

        static void Poll(object state)
        {
            if (Interlocked.Exchange(ref polling, 1) == 1) return;
            try
            {
                bool games = Prefs.Games;
                string running = null;
                bool fs = false, core = false;
                // Настройка выключена — ни снимков, ни вопросов: всё это нужно только ради неё
                if (games)
                {
                    running = Games.Running(Procs.Running(), Prefs.Extra);
                    try { fs = Fullscreen != null && Fullscreen(); } catch { }
                    try { core = CoreGame != null && CoreGame(); } catch { }
                }
                Interlocked.Increment(ref Polls);
                Dispatcher d = ui;
                if (d != null) d.BeginInvoke(new Action(delegate { Apply(running, fs, core); }));
            }
            catch { }
            finally { Interlocked.Exchange(ref polling, 0); }
        }

        // В потоке окна
        static void Apply(string running, bool fullscreen, bool core)
        {
            bool was = watch.On;
            AsideReason before = watch.Reason;
            bool now = watch.Update(Prefs.Games, running, fullscreen, core, clock.Elapsed.TotalMilliseconds);
            if (now == was && watch.Reason == before) return;
            On = now;
            if (now != was) Journal.Write(now ? "В стороне: " + Logic.AsideText(watch.Reason) + (watch.What.Length > 0 ? " (" + watch.What + ")" : "") + ". Alt+Tab системный" : "Игра закончилась: Alt+Tab снова открывает smena.txt");
            Action h = Changed;
            if (h != null) h();
        }

        // Впереди новое окно (Win.Watch, в потоке окна): на весь экран — в сторону сразу, не дожидаясь опроса
        public static void Foreground(IntPtr hwnd)
        {
            if (On || !Prefs.Games || ui == null) return;
            Func<IntPtr, bool> f = FullscreenWindow;
            if (f != null && f(hwnd)) Apply(null, true, false);
        }

        // Отойти в сторону: всё, что держит захваты и картинки чужих окон, закрывается; перехватчики снимает вызывающий
        public static void Release()
        {
            Stills.Clear();
            LiveWindow.CloseAll();
            IconicShot.CloseAll();
            CaptureItems.Clear();
            LiveWindow.ForgetMonitors();
        }

        // Что делается при входе и выходе — одно для обычного запуска, --headless и --asideprobe.
        // hook — поставить или снять перехватчик клавиш по текущему состоянию.
        public static void Wire(Switcher switcher, Action hook)
        {
            Changed += delegate
            {
                if (On)
                {
                    // Открытый показ закрывается так же, как при любой ошибке: на экране ничего не остаётся
                    if (switcher != null) switcher.Abort();
                    Win.Unwatch();
                    Release();
                }
                else Win.Rewatch();
                if (hook != null) hook();
                AppHub.Publish();   // сочетания на странице «Клавиши» — выключены, пока в стороне
            };
        }
    }

    // --asideprobe: проверка «отойти в сторону» без экрана и без настоящего перехвата клавиатуры. Перехватчик — заглушка
    // с тем же порядком установки и снятия (Hook.Stub), список процессов подменён (Procs.FakeList), «окно-игра» — своё
    // спрятанное DWM вспомогательное окно, названное игрой через Prefs.Extra. Одна строка итога; код выхода 0 — всё так.
    static class AsideProbe
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        const int HoldMs = 6000;   // столько в стороне следим, что обращений нет

        static int failed;
        static readonly StringBuilder notes = new StringBuilder();
        static void Check(bool ok, string what) { if (!ok) { failed++; notes.Append(" | НЕТ: ").Append(what); } }

        // Своё окно 160×100 в углу экрана, спрятанное DWM ещё до показа, без фокуса, не в Alt+Tab; не спряталось — не показываем
        static IntPtr Helper()
        {
            IntPtr h = CreateWindowEx((uint)(Logic.WS_EX_TOOLWINDOW | Logic.WS_EX_NOACTIVATE), "STATIC", "smena.txt aside probe", 0x80000000, 0, 0, 160, 100, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (h == IntPtr.Zero) return h;
            int on = 1, cloaked;
            if (DwmSetWindowAttribute(h, 13, ref on, 4) != 0 || DwmGetWindowAttribute(h, 14, out cloaked, 4) != 0 || cloaked == 0) { DestroyWindow(h); return IntPtr.Zero; }
            ShowWindow(h, 4);   // SW_SHOWNOACTIVATE
            return h;
        }

        public static int Run(out string line)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Prefs.Persist = false;
            Prefs.Reset();
            Procs.AllowFake = true;
            Procs.FakeList = new string[0];
            // Окно на весь экран впереди и core.txt в пробе не в счёт: её итог не должен зависеть от того, что сейчас на экране
            Aside.Fullscreen = delegate { return false; };
            Aside.FullscreenWindow = delegate(IntPtr h) { return false; };
            Aside.CoreGame = null;
            Hook.Stub = true;
            Hook.Allowed = true;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Dispatcher ui = app.Dispatcher;
            var switcher = new Switcher(false, true);
            switcher.Prepare();
            var machine = new KeyMachine();
            switcher.Machine = machine;
            Hook.Setup(machine, ui, switcher.Handle);
            Action sync = delegate { Hook.Sync(Program.HookWanted); };
            Prefs.Changed += sync;
            AppHub.Show = switcher.OpenSticky;
            Aside.Wire(switcher, sync);
            IntPtr a = IntPtr.Zero, b = IntPtr.Zero;
            LiveWindow live = null;
            string result = "";
            var script = new Thread(delegate()
            {
                Func<Func<bool>, int, bool> waitFor = delegate(Func<bool> cond, int ms)
                {
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < ms) { bool ok = false; ui.Invoke(new Action(delegate { ok = cond(); })); if (ok) return true; Thread.Sleep(50); }
                    return false;
                };
                Action<Action> onUi = delegate(Action act) { ui.Invoke(act); };
                try
                {
                    // ── Обычная работа ──
                    onUi(delegate
                    {
                        sync();
                        Win.Watch(null);
                        Aside.Start(ui);
                        a = Helper(); b = Helper();
                    });
                    Check(a != IntPtr.Zero && b != IntPtr.Zero, "свои спрятанные окна");
                    bool normal = false;
                    onUi(delegate
                    {
                        string why;
                        if (Capture.Available) live = LiveWindow.Open(a, false, true, true, 3000, out why);
                        IntPtr t = Win.ThumbRegister(b, a);
                        Win.ThumbRelease(t);
                        bool fg;
                        Win.List(false, false, out fg);
                        normal = Hook.Installed && Win.Watching;
                    });
                    Check(normal, "в обычной работе перехватчик (заглушка) стоит и окна отслеживаются");
                    Check(!Capture.Available || live != null, "в обычной работе захват своего окна заводится");
                    long before = Calls.Sum();
                    string touchedNormal = Calls.Touches();
                    Check(before > 0, "в обычной работе обращения считаются");

                    // ── Появился EasyAntiCheat_EOS ──
                    var enter = Stopwatch.StartNew();
                    Procs.FakeList = new string[] { "EasyAntiCheat_EOS.exe" };
                    bool inAside = waitFor(delegate { return Aside.On; }, Aside.PollMs + 2000);
                    double enterMs = enter.Elapsed.TotalMilliseconds;
                    Check(inAside, "EasyAntiCheat_EOS — в сторону");
                    bool cleared = false, keysOff = false, refused = false;
                    string reason = "";
                    onUi(delegate
                    {
                        cleared = !Hook.Installed && !Win.Watching && LiveWindow.Count == 0 && IconicShot.Count == 0 && Stills.Count == 0;
                        var keys = AppHub.Keys();
                        keysOff = !keys[0].On && !keys[1].On;
                        var shown = AppHub.ShowCommand(null);
                        refused = !(bool)shown["shown"] && shown.ContainsKey("reason");
                        reason = shown.ContainsKey("reason") ? shown["reason"] as string : "";
                    });
                    Check(cleared, "в стороне: перехватчик снят, слежение за окнами снято, захватов и вспомогательных окон 0, картинки забыты");
                    Check(keysOff, "в стороне: оба сочетания выключены");
                    Check(refused, "в стороне: «show» отвечает отказом с причиной");
                    long held = Calls.Sum();
                    int hookOn = Calls.HookOn, watchOn = Calls.WatchOn;
                    // Всё, что в обычной работе обращается к чужому, — по таймерам и командам — в стороне молчит
                    var hold = Stopwatch.StartNew();
                    int tries = 0;
                    while (hold.ElapsedMilliseconds < HoldMs)
                    {
                        onUi(delegate
                        {
                            bool fg;
                            Win.List(true, true, out fg);
                            Win.WarmIcons();
                            Win.Elevated(GetForegroundWindow());
                            Win.SendMask();
                            switcher.OpenSticky();
                        });
                        tries++;
                        Thread.Sleep(500);
                    }
                    Thread.Sleep(200);   // значки — в пуле потоков
                    bool still = Aside.On;
                    long heldDelta = Calls.Sum() - held;
                    Check(still, "в стороне всё время удержания");
                    Check(heldDelta == 0 &&Calls.HookOn == hookOn && Calls.WatchOn == watchOn, "в стороне " + HoldMs / 1000 + " с обращений нет (" + Calls.Touches() + ")");

                    // ── Процесс исчез ──
                    var leave = Stopwatch.StartNew();
                    Procs.FakeList = new string[0];
                    bool back = waitFor(delegate { return !Aside.On; }, (int)AsideWatch.QuietMs + 2 * Aside.PollMs + 2000);
                    double leaveMs = leave.Elapsed.TotalMilliseconds;
                    Check(back, "без античита — назад");
                    Check(leaveMs >= AsideWatch.QuietMs - 50, "назад не раньше, чем через " + AsideWatch.QuietMs / 1000 + " с");
                    bool restored = false, keysOn = false;
                    onUi(delegate { restored = Hook.Installed && Win.Watching; var keys = AppHub.Keys(); keysOn = keys[0].On && keys[1].On; });
                    Check(restored, "назад: перехватчик (заглушка) и слежение за окнами снова стоят");
                    Check(keysOn, "назад: сочетания снова включены");

                    // ── Окно-игра в списке (своё окно, названное игрой) ──
                    string self = Procs.NameOf(Win.Self);
                    Prefs.Extra = new string[] { self.ToUpperInvariant() + ".EXE" };
                    long g0 = Calls.Sum();
                    bool isGame = false, gentle = false, noIcon = false, grid = false, noTouch = false, notElevated = false;
                    WinItem item = null;
                    onUi(delegate
                    {
                        isGame = Win.IsGame(a);
                        gentle = Win.GentleFor(a);
                        notElevated = !Win.Elevated(a);
                        item = Win.Identify(a, Win.Self, "игра", false, isGame);
                        noIcon = Win.Icon(item, ui, null) == null;
                        var items = new List<WinItem> { item, new WinItem { Hwnd = b, Title = "окно", Key = "x" } };
                        grid = !Logic.UseFlip(true, items);
                        noTouch = !Logic.MayTouch(item);
                    });
                    Thread.Sleep(200);
                    long g1 = Calls.Sum();
                    Check(isGame && item != null && item.Game && item.Path == null, "своё окно с именем из asideProcesses — игра, путь к файлу не берётся");
                    Check(gentle && notElevated && noIcon && noTouch, "окно-игра: только обычная активация, права и значок не спрашиваются, захвата нет");
                    Check(grid, "видимое окно-игра в списке — сетка вместо объёмного вида");
                    Check(g1 == g0, "окно-игра: обращений 0 (" + (g1 - g0) + ")");
                    // То же окно не игрой — путь к файлу берётся (OpenProcess): счётчик действительно считает
                    int op = Calls.OpenProcess;
                    onUi(delegate { Win.Identify(a, Win.Self, "окно", false, false); });
                    Check(Calls.OpenProcess == op + 1, "не игра — OpenProcess считается");
                    Prefs.Extra = new string[0];

                    // ── Снимок списка процессов не открывает процессов (косвенно: число хэндлов программы) ──
                    int h0 = Process.GetCurrentProcess().HandleCount;
                    for (int i = 0; i < 20; i++) Procs.Take();
                    int h1 = Process.GetCurrentProcess().HandleCount;
                    Check(Math.Abs(h1 - h0) <= 8, "20 снимков списка процессов: хэндлов было " + h0 + ", стало " + h1);

                    result = "в сторону через " + enterMs.ToString("0", inv) + " мс после появления EasyAntiCheat_EOS (опрос раз в " + Aside.PollMs + " мс), "
                        + "назад через " + leaveMs.ToString("0", inv) + " мс после исчезновения (тишина " + AsideWatch.QuietMs.ToString("0", inv) + " мс); "
                        + "в обычной работе: " + touchedNormal + "; в стороне " + HoldMs / 1000 + " с, " + tries + " попыток обратиться: прибавилось " + heldDelta + "; "
                        + "перехватчик (заглушка) ставился " + Calls.HookOn + ", снимался " + Calls.HookOff + "; слежение за окнами ставилось " + Calls.WatchOn + ", снималось " + Calls.WatchOff + "; "
                        + "причина отказа «show»: «" + reason + "»; окно-игра: обращений " + (g1 - g0) + ", вид — " + (grid ? "сетка" : "объёмный") + "; "
                        + "хэндлов до и после 20 снимков: " + h0 + " → " + h1 + " (косвенно); опросов " + Aside.Polls + ", снимков " + Calls.Snapshots;
                }
                catch (Exception e) { failed++; notes.Append(" | ошибка: ").Append(e.GetType().Name).Append(": ").Append(e.Message); }
                finally
                {
                    try
                    {
                        onUi(delegate
                        {
                            Aside.Stop();
                            if (live != null) live.Dispose();
                            if (a != IntPtr.Zero) DestroyWindow(a);
                            if (b != IntPtr.Zero) DestroyWindow(b);
                            Win.Unwatch();
                            Hook.Stop();
                        });
                    }
                    catch { }
                    ui.BeginInvokeShutdown(DispatcherPriority.Normal);
                }
            }) { IsBackground = true, Name = "smena aside probe" };
            script.Start();
            app.Run();
            script.Join(5000);
            line = (failed == 0 ? "asideprobe: ok, " : "asideprobe: нет, ") + result + notes.ToString();
            return failed == 0 ? 0 : 1;
        }
    }
}
