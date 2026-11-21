// smena.txt. Win.cs — разговор с Windows: какие окна открыты и чьи они, значки, живые превью (DWM),
// вывести окно вперёд, полноэкранное ли окно впереди, на каком мониторе показывать.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DevTxt.Smena
{
    static class Win
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)]
        struct THUMBPROPS { public uint Flags; public RECT Dest, Source; public byte Opacity; public bool Visible, ClientOnly; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        struct INPUT { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KEYBDINPUT Key; }
        delegate bool EnumProc(IntPtr hwnd, IntPtr l);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetLastActivePopup(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint ms, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr source, out IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMBPROPS props);

        // Рабочие столы Windows: описанный в документации способ узнать, на текущем ли столе окно
        [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr window, out int onCurrent);
            [PreserveSig] int GetWindowDesktopId(IntPtr window, out Guid desktop);
            [PreserveSig] int MoveWindowToDesktop(IntPtr window, ref Guid desktop);
        }
        [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
        class VirtualDesktopManager { }

        const int GWL_STYLE = -16, GWL_EXSTYLE = -20, GCLP_HICON = -14;
        const long WS_CAPTION = 0x00C00000, WS_MAXIMIZE = 0x01000000, WS_THICKFRAME = 0x00040000;
        const uint GW_OWNER = 4, WM_SYSCOMMAND = 0x0112;
        const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        const int SW_RESTORE = 9, DWMWA_FRAME = 9, DWMWA_CLOAK = 13, DWMWA_CLOAKED = 14, CLOAKED_SHELL = 2, SC_CLOSE = 0xF060;
        const string FrameClass = "ApplicationFrameWindow";
        public const int MaskKey = 0xE8;   // клавиша, которой нет ни на одной клавиатуре
        public static readonly IntPtr OurInput = new IntPtr(0x534D454E);   // пометка «это нажатие послали мы сами»

        public static readonly uint Self = (uint)Process.GetCurrentProcess().Id;

        class Known { public uint Pid; public string Path, Key; public bool Frame; }
        static readonly Dictionary<IntPtr, Known> known = new Dictionary<IntPtr, Known>();   // чьё окно — узнаём один раз
        static IVirtualDesktopManager desktops;
        static bool desktopsFailed;

        // Какими окнами пользовались недавно; пополняется в Watch, когда впереди оказывается новое окно
        public static readonly Recent Used = new Recent();

        // Окно и его владельцы по порядку: диалог, окно, которое его открыло, и так далее
        static List<IntPtr> OwnerChain(IntPtr hwnd)
        {
            var chain = new List<IntPtr>();
            for (int i = 0; i < 8 && hwnd != IntPtr.Zero && !chain.Contains(hwnd); i++)
            {
                chain.Add(hwnd);
                hwnd = GetWindow(hwnd, GW_OWNER);
            }
            return chain;
        }

        // Окно вышло вперёд: оно само — самое недавнее, сразу за ним его владельцы
        static void Seen(IntPtr hwnd)
        {
            List<IntPtr> chain = OwnerChain(hwnd);
            for (int i = chain.Count - 1; i >= 0; i--) Used.Touch(chain[i]);
        }

        static string Text(IntPtr hwnd)
        {
            // Для чужих окон GetWindowText не ждёт ответа программы — зависшее окно нас не остановит
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        static string Class(IntPtr hwnd)
        {
            var sb = new StringBuilder(128);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        static string PathOf(uint pid)
        {
            if (Aside.On) return null;   // в стороне чужие процессы не открываются
            Calls.Add(ref Calls.OpenProcess);
            IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION: хватает и для чужих программ
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally { CloseHandle(h); }
        }

        // Приложение из Магазина: окно держит общая программа-рамка, само приложение — в дочернем окне другого процесса
        static string InnerPath(IntPtr frame, uint framePid)
        {
            string inner = null;
            EnumChildWindows(frame, delegate(IntPtr child, IntPtr l)
            {
                uint cp;
                GetWindowThreadProcessId(child, out cp);
                if (cp == framePid) return true;
                inner = PathOf(cp);
                return inner == null;
            }, IntPtr.Zero);
            return inner;
        }

        static bool OnOtherDesktop(IntPtr hwnd)
        {
            if (desktopsFailed) return false;
            try
            {
                if (desktops == null) desktops = (IVirtualDesktopManager)new VirtualDesktopManager();
                int here;
                Guid id;
                if (desktops.IsWindowOnCurrentVirtualDesktop(hwnd, out here) != 0 || here != 0) return false;
                return desktops.GetWindowDesktopId(hwnd, out id) == 0 && id != Guid.Empty;
            }
            catch { desktopsFailed = true; return false; }
        }

        // Все видимые окна верхнего уровня, сверху вниз — то есть от недавнего к давнему.
        // desktops — выяснять ли про спрятанные окна, на другом ли они рабочем столе (только из потока окна).
        public static List<RawWin> Enumerate(bool desktops)
        {
            var list = new List<RawWin>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr l)
            {
                try
                {
                    if (!IsWindowVisible(hwnd)) return true;
                    var w = new RawWin { Hwnd = hwnd, Visible = true };
                    w.Owned = GetWindow(hwnd, GW_OWNER) != IntPtr.Zero;
                    w.ExStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                    int cloaked;
                    if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0) w.Cloaked = cloaked;
                    GetWindowThreadProcessId(hwnd, out w.Pid);
                    w.Title = Text(hwnd);
                    w.Minimized = IsIconic(hwnd);
                    if (w.Cloaked != 0 && desktops && (w.Cloaked & CLOAKED_SHELL) != 0 && w.Title.Length > 0 && w.Pid != Self)
                    {
                        w.OtherDesktop = OnOtherDesktop(hwnd);
                        // Рамка без приложения внутри — след закрытого или спящего приложения из Магазина, а не окно
                        if (w.OtherDesktop && Class(hwnd) == FrameClass) w.Ghost = InnerPath(hwnd, w.Pid) == null;
                    }
                    list.Add(w);
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        // Окна для переключателя: отобраны, с путём к программе, от недавнего к давнему; окно, в котором человек сейчас, —
        // первое. Если впереди диалог или всплывающее окно, которого в списке нет, «текущим» считается его владелец.
        // Окно игры (Logic.GameWindow): при включённой настройке «Не вмешиваться в играх» его процесс не открывается —
        // имя берётся из снимка списка процессов (Procs), путь к файлу и значок не берутся (в карточке — плитка с буквой).
        public static List<WinItem> List(bool allDesktops, bool desktops, out bool foregroundFirst)
        {
            foregroundFirst = false;
            var list = new List<WinItem>();
            if (Aside.On) return list;   // в стороне чужие окна не перечисляются
            var raw = Logic.Filter(Enumerate(desktops && allDesktops), Self, allDesktops);
            bool games = Prefs.Games;
            string[] extra = Prefs.Extra;
            lock (known)
            {
                var next = new Dictionary<IntPtr, Known>();
                foreach (RawWin w in raw)
                {
                    string proc = games ? Procs.NameOf(w.Pid) : "";
                    bool game = games && Logic.GameWindow(true, FullscreenAny(w.Hwnd, w.Minimized), proc, extra);
                    Known k;
                    if (game) k = Untouched(w.Pid, proc);
                    else if (!known.TryGetValue(w.Hwnd, out k) || k.Pid != w.Pid || k.Path == null || (k.Frame && k.Key.StartsWith("frame|"))) k = Resolve(w.Hwnd, w.Pid, w.Title);
                    next[w.Hwnd] = k;
                    list.Add(new WinItem { Hwnd = w.Hwnd, Title = w.Title, Path = game || k.Key.StartsWith("frame|") ? null : k.Path, Key = k.Key, Minimized = w.Minimized, Game = game, Proc = proc });
                }
                known.Clear();
                foreach (var kv in next) known[kv.Key] = kv.Value;
            }
            Used.Sort(list);
            foregroundFirst = Logic.ForegroundFirst(list, Logic.Listed(list, OwnerChain(GetForegroundWindow())));
            return list;
        }

        // Чьё окно: путь к файлу программы (OpenProcess), у приложения из Магазина — того, что внутри рамки
        static Known Resolve(IntPtr hwnd, uint pid, string title)
        {
            var k = new Known { Pid = pid, Path = PathOf(pid) };
            k.Frame = Class(hwnd) == FrameClass;
            if (k.Frame)
            {
                string inner = InnerPath(hwnd, pid);
                if (inner != null) k.Path = inner;
                else k.Key = "frame|" + title.ToLowerInvariant();   // свёрнуто: приложения внутри нет, узнаём по заголовку
            }
            if (k.Key == null) k.Key = k.Path != null ? k.Path.ToLowerInvariant() : "pid|" + pid;
            return k;
        }

        // Окно игры: только имя процесса из снимка, без пути
        static Known Untouched(uint pid, string proc)
        {
            return new Known { Pid = pid, Key = "proc|" + (string.IsNullOrEmpty(proc) ? "pid " + pid : proc) };
        }

        // Одно окно так же, как в List, без запоминания — для --asideprobe
        public static WinItem Identify(IntPtr hwnd, uint pid, string title, bool minimized, bool game)
        {
            string proc = Procs.NameOf(pid);
            Known k = game ? Untouched(pid, proc) : Resolve(hwnd, pid, title);
            return new WinItem { Hwnd = hwnd, Title = title, Path = game || k.Key.StartsWith("frame|") ? null : k.Path, Key = k.Key, Minimized = minimized, Game = game, Proc = proc };
        }

        // ───────── Окна игр ─────────

        // На весь экран: обычное окно — Fullscreen; свёрнутое — развернётся ровно на свой монитор без рамки
        static bool FullscreenAny(IntPtr hwnd, bool minimized)
        {
            return minimized ? FullscreenMinimized(hwnd) : Fullscreen(hwnd);
        }

        static bool FullscreenMinimized(IntPtr hwnd)
        {
            try
            {
                var wp = new WINDOWPLACEMENT { Length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
                if (!GetWindowPlacement(hwnd, ref wp)) return false;
                RECT n = wp.Normal;
                var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(MonitorFromRect(ref n, 2), ref info)) return false;   // MONITOR_DEFAULTTONEAREST
                long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
                RECT m = info.Monitor;
                return Logic.FullscreenMinimized(n.Right - n.Left, n.Bottom - n.Top, m.Right - m.Left, m.Bottom - m.Top, (style & WS_CAPTION) == WS_CAPTION, (style & WS_THICKFRAME) != 0);
            }
            catch { return false; }
        }

        // Окно игры — по тем же правилам, что в List (настройка, весь экран, имя процесса из снимка)
        public static bool IsGame(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Prefs.Games) return false;
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                return Logic.GameWindow(true, FullscreenAny(hwnd, IsIconic(hwnd)), Procs.NameOf(pid), Prefs.Extra);
            }
            catch { return false; }
        }

        // Впереди окно игры
        public static bool GameAhead() { return IsGame(GetForegroundWindow()); }

        // Переключение к target — только обычной просьбой к Windows (Logic.Gentle)
        public static bool GentleFor(IntPtr target) { return Logic.Gentle(Aside.On, GameAhead(), IsGame(target)); }

        // ───────── Монитор и полный экран ─────────

        // Монитор, на котором окно, с которым человек работает; если такого нет — тот, где курсор. В точках экрана.
        public static RECT ActiveMonitor()
        {
            IntPtr fg = GetForegroundWindow(), mon;
            if (fg != IntPtr.Zero) mon = MonitorFromWindow(fg, 1);
            else { POINT p; GetCursorPos(out p); mon = MonitorFromPoint(p, 1); }
            var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (GetMonitorInfo(mon, ref info)) return info.Monitor;
            return new RECT { Right = (int)SystemParameters.PrimaryScreenWidth, Bottom = (int)SystemParameters.PrimaryScreenHeight };
        }

        // Впереди окно ровно на весь свой монитор, без заголовка и рамки, не развёрнутое (игра, видео)?
        // Рабочий стол и панель задач не в счёт.
        // Вызывается из потока перехватчика: только быстрые вопросы к Windows, ни одного — к самой программе.
        public static bool FullscreenAhead() { return Fullscreen(GetForegroundWindow()); }

        // Окно на весь экран — игра или видео. Развёрнутое окно без заголовка (Steam, Electron) игрой не считается:
        // из-за таких окон Alt+Tab отдавался системе. Невидимые слои поверх экрана (оверлей NVIDIA, ввод Windows,
        // прозрачные окна, которые не берут фокус) игрой не считаются: из-за них Alt+Tab отдавался системе через раз.
        public static bool Fullscreen(IntPtr fg)
        {
            try
            {
                if (fg == IntPtr.Zero) return false;
                long ex = GetWindowLongPtr(fg, -20).ToInt64();   // GWL_EXSTYLE
                if ((ex & (0x80 | 0x08000000 | 0x20)) != 0) return false;   // WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE, WS_EX_TRANSPARENT
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (pid == Self) return false;
                string cls = Class(fg);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "XamlExplorerHostIslandWindow" || cls == "Windows.UI.Core.CoreWindow" || cls == "CEF-OSC-WIDGET" || cls == "MultitaskingViewFrame") return false;
                RECT r;
                if (!GetWindowRect(fg, out r)) return false;
                var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(MonitorFromWindow(fg, 2), ref info)) return false;
                long style = GetWindowLongPtr(fg, GWL_STYLE).ToInt64();
                bool caption = (style & WS_CAPTION) == WS_CAPTION;
                RECT m = info.Monitor;
                return Logic.IsFullscreen(r.Left, r.Top, r.Right, r.Bottom, m.Left, m.Top, m.Right, m.Bottom, caption, (style & WS_MAXIMIZE) != 0, (style & WS_THICKFRAME) != 0);
            }
            catch { return false; }
        }

        public static bool KeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        // ───────── Сторож: какое окно впереди; системный Alt+Tab открылся, хотя должен был наш ─────────
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token, int cls, out int value, int size, out int length);
        static WinEventProc watchProc;   // ссылка хранится, чтобы сборщик мусора не убрал её из-под Windows

        // Следит за тем, какое окно впереди: так складывается порядок «от недавнего к давнему» (Used).
        // Заодно зовёт systemSwitcher(окно, что было впереди), когда на экране появился системный переключатель окон
        // при зажатом Alt; systemSwitcher может не быть.
        // Работает в потоке окна: события приходят через его очередь сообщений и ничего не тормозят.
        // Пока smena.txt в стороне (Aside), слежения нет (Unwatch): вернулась — оно ставится снова (Rewatch).
        public static void Watch(Action<IntPtr> systemSwitcher)
        {
            if (watchProc != null) return;
            IntPtr before = IntPtr.Zero;
            DateTime last = DateTime.MinValue;
            watchWanted = true;
            watchProc = delegate(IntPtr h, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
            {
                try
                {
                    if (obj != 0) return;   // OBJID_WINDOW: само окно, а не что-то внутри него
                    // EVENT_SYSTEM_MINIMIZESTART: свёрнутое уходит в конец, как в Windows; его прежняя картинка (Stills) устарела
                    if (ev == 0x0016) { Used.Forget(hwnd); Stills.Forget(hwnd); return; }
                    if (ev == 0x0017) { Stills.Forget(hwnd); return; }   // EVENT_SYSTEM_MINIMIZEEND: окно развернули
                    bool system = ev == 0x0014;   // EVENT_SYSTEM_SWITCHSTART
                    if (ev == 0x0003)             // EVENT_SYSTEM_FOREGROUND
                    {
                        string cls = Class(hwnd);
                        system = cls == "XamlExplorerHostIslandWindow" || cls == "MultitaskingViewFrame" || cls == "TaskSwitcherWnd";
                        if (!system) { before = hwnd; Seen(hwnd); Aside.Foreground(hwnd); return; }
                    }
                    if (systemSwitcher == null || !system || !KeyDown(KeyMachine.Alt)) return;   // Win+Tab и прочее — не наше дело
                    if ((DateTime.UtcNow - last).TotalMilliseconds < 800) return;   // одно открытие — одно сообщение
                    last = DateTime.UtcNow;
                    systemSwitcher(before);
                }
                catch { }
            };
            Rewatch();
        }

        static bool watchWanted;
        static readonly List<IntPtr> watchHooks = new List<IntPtr>();
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

        // Слежение сейчас стоит
        public static bool Watching { get { return watchHooks.Count > 0; } }

        // Поставить слежение (если его просили через Watch и smena.txt не в стороне). В потоке окна.
        public static void Rewatch()
        {
            if (!watchWanted || watchProc == null || watchHooks.Count > 0 || Aside.On) return;
            Calls.Add(ref Calls.WatchOn);
            foreach (uint[] r in new uint[][] { new uint[] { 0x0003, 0x0003 }, new uint[] { 0x0014, 0x0014 }, new uint[] { 0x0016, 0x0017 } })   // FOREGROUND; SWITCHSTART; MINIMIZESTART и MINIMIZEEND
            {
                IntPtr h = SetWinEventHook(r[0], r[1], IntPtr.Zero, watchProc, 0, 0, 2);   // WINEVENT_OUTOFCONTEXT | SKIPOWNPROCESS
                if (h != IntPtr.Zero) watchHooks.Add(h);
            }
            Seen(GetForegroundWindow());
        }

        // Снять слежение (smena.txt отходит в сторону). В потоке окна.
        public static void Unwatch()
        {
            if (watchHooks.Count == 0) return;
            Calls.Add(ref Calls.WatchOff);
            foreach (IntPtr h in watchHooks) { try { UnhookWinEvent(h); } catch { } }
            watchHooks.Clear();
        }

        // Окно программы, запущенной от администратора. Пока такое окно впереди, Windows не показывает обычным
        // программам нажатия клавиш — и Alt+Tab там всегда системный. Не смогли спросить — значит, права выше наших.
        public static bool Elevated(IntPtr hwnd)
        {
            uint pid;
            if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out pid) == 0 || pid == 0) return false;
            // В стороне и у игры права не спрашиваем: это открыло бы её процесс
            if (Aside.On || IsGame(hwnd)) return false;
            Calls.Add(ref Calls.OpenProcess);
            IntPtr p = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (p == IntPtr.Zero) return true;
            try
            {
                IntPtr token;
                Calls.Add(ref Calls.OpenToken);
                if (!OpenProcessToken(p, 0x0008, out token)) return true;   // TOKEN_QUERY
                try { int value, length; return GetTokenInformation(token, 20, out value, 4, out length) && value != 0; }   // TokenElevation
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(p); }
        }

        // Для журнала: «программа — заголовок»
        public static string Describe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "неизвестное окно";
            string name = "?";
            // Имя — из снимка списка процессов: Process.GetProcessById открывал бы сам процесс
            try { uint pid; GetWindowThreadProcessId(hwnd, out pid); string n = Procs.NameOf(pid); if (n.Length > 0) name = n; } catch { }
            return name + " (" + Class(hwnd) + ")";
        }

        // ───────── Окно переключателя ─────────

        // Не попадает в Alt+Tab и на панель задач; noActivate — не забирает фокус у окна, с которым работают
        public static void Style(IntPtr hwnd, bool noActivate)
        {
            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | Logic.WS_EX_TOOLWINDOW;
            ex = noActivate ? ex | Logic.WS_EX_NOACTIVATE : ex & ~Logic.WS_EX_NOACTIVATE;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        }

        // Ставит окно поверх всех, ровно на монитор (в точках экрана), не показывая и не активируя
        public static void Place(IntPtr hwnd, RECT r)
        {
            SetWindowPos(hwnd, new IntPtr(-1), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x0010);   // SWP_NOACTIVATE
        }

        // Своё окно: новое место и размер (в точках экрана), не активируя и не меняя порядок окон
        public static void Move(IntPtr hwnd, int x, int y, int w, int h)
        {
            SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 0x0010 | 0x0004);   // SWP_NOACTIVATE | SWP_NOZORDER
        }

        // Без системной анимации появления и исчезновения окна (DWMWA_TRANSITIONS_FORCEDISABLED): окно открывается
        // и убирается ровно тем кадром, который в нём нарисован, — DWM не дорисовывает своё растворение или сжатие
        public static void NoTransitions(IntPtr hwnd)
        {
            try { int value = 1; DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int)); } catch { }
        }

        // Прячет окно от глаз, не трогая его самого (и обратно). Спрятанное так окно рисуется как обычно, поэтому показать
        // его можно уже с готовой картинкой — без кадра, оставшегося с прошлого раза.
        public static void Cloak(IntPtr hwnd, bool on)
        {
            try { int value = on ? 1 : 0; DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref value, sizeof(int)); } catch { }
        }

        // ───────── Куда встанет свёрнутое окно ─────────

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPLACEMENT { public int Length, Flags, ShowCmd; public POINT MinPos, MaxPos; public RECT Normal; }
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT r, uint flags);

        // Видимая рамка свёрнутого окна после разворачивания (Restore.Place), только чтение: окно не трогается.
        // dest — своё окно верхнего уровня, к которому на миг заводится миниатюра DWM ради её размера (видимая рамка
        // без невидимых полей); Zero — без неё. toMax — развернётся на весь экран; how — из чего посчитано (для проверок).
        // false — окно не свёрнуто или места нет.
        public static bool RestorePlace(IntPtr hwnd, IntPtr dest, out RECT visible, out bool toMax, out string how)
        {
            visible = new RECT(); toMax = false; how = "";
            try
            {
                if (hwnd == IntPtr.Zero || !IsIconic(hwnd) || Aside.On) return false;
                var wp = new WINDOWPLACEMENT { Length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
                if (!GetWindowPlacement(hwnd, ref wp)) return false;
                toMax = (wp.Flags & 2) != 0;   // WPF_RESTORETOMAXIMIZED
                bool tool = (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & Logic.WS_EX_TOOLWINDOW) != 0;
                RECT n = wp.Normal;
                var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(MonitorFromRect(ref n, 2), ref info)) return false;   // MONITOR_DEFAULTTONEAREST
                int tw = 0, th = 0;
                IntPtr thumb;
                if (!toMax && dest != IntPtr.Zero && Counted(ref Calls.Thumbs) && DwmRegisterThumbnail(dest, hwnd, out thumb) == 0 && thumb != IntPtr.Zero)
                {
                    SIZE size;
                    if (DwmQueryThumbnailSourceSize(thumb, out size) == 0) { tw = size.W; th = size.H; }
                    DwmUnregisterThumbnail(thumb);
                }
                how = "окно " + (n.Right - n.Left) + "x" + (n.Bottom - n.Top) + (tool ? "" : " (в координатах рабочей области)") + ", миниатюра " + (tw > 0 ? tw + "x" + th : "—");
                return Restore.Place(n, tool, toMax, info.Monitor, info.Work, tw, th, out visible);
            }
            catch { return false; }
        }

        // ───────── Вывести окно вперёд, закрыть окно ─────────

        // Нажатие и отпускание клавиши, которой не существует. Нужно дважды:
        // 1) пока Alt зажат — чтобы программа под нами не открыла своё меню, когда Alt отпустят («Alt был с чем-то»);
        // 2) перед переключением — Windows разрешает менять переднее окно тому, от кого пришёл последний ввод.
        public static void SendMask()
        {
            if (Aside.On) return;   // в стороне своих нажатий не посылаем
            try
            {
                Calls.Add(ref Calls.SendInput);
                var down = new INPUT { Type = 1 };
                down.Key.Vk = MaskKey; down.Key.Extra = OurInput;
                INPUT up = down;
                up.Key.Flags = 2;   // KEYEVENTF_KEYUP
                SendInput(2, new INPUT[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
            }
            catch { }
        }

        // Выводит окно вперёд; свёрнутое разворачивает. true — окно действительно стало передним.
        // Если у окна открыт диалог, вперёд выходит он: само окно под ним всё равно не отвечает.
        // На что опираемся, по порядку:
        //  1. посылаем пустое нажатие (последний ввод теперь наш — Windows разрешает менять переднее окно тому, от кого
        //     он пришёл) и зовём SetForegroundWindow;
        //  2. не вышло — на время присоединяемся к очереди ввода переднего окна (AttachThreadInput) и пробуем ещё раз.
        // Впереди игра, переключаемся в игру или smena.txt в стороне (Logic.Gentle) — только SetForegroundWindow: ни пустого
        // нажатия, ни чужой очереди ввода. Windows не пустила — так и остаётся (окно мигнёт на панели задач), не дожимаем.
        public static bool Activate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
            bool gentle = GentleFor(hwnd);
            if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);
            IntPtr to = hwnd;
            try
            {
                IntPtr popup = GetLastActivePopup(hwnd);
                if (popup != IntPtr.Zero && popup != hwnd && IsWindow(popup) && IsWindowVisible(popup)) to = popup;
            }
            catch { }
            if (gentle)
            {
                bool ok = SetForegroundWindow(to) && GetForegroundWindow() == to;
                if (ok) Seen(to);
                return ok;
            }
            SendMask();
            if (SetForegroundWindow(to) && GetForegroundWindow() == to) { Seen(to); return true; }
            // Поток окна здесь ничего не ждёт: показ переключателя ещё на экране. К очереди ввода зависшего переднего
            // окна не присоединяемся — общая очередь с зависшим потоком подвесила бы и нас; окно поднимается наверх
            // просьбой без ожидания (SWP_ASYNCWINDOWPOS): BringWindowToTop ждал бы ответа программы, а она может висеть.
            IntPtr fg = GetForegroundWindow();
            uint pid, me = GetCurrentThreadId(), other = GetWindowThreadProcessId(fg, out pid);
            bool attached = other != 0 && other != me && !IsHungAppWindow(fg) && Counted(ref Calls.Attach) && AttachThreadInput(me, other, true);
            try
            {
                SetWindowPos(to, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x4000);   // HWND_TOP; SWP_NOSIZE | SWP_NOMOVE | SWP_ASYNCWINDOWPOS
                SetForegroundWindow(to);
            }
            finally { if (attached) AttachThreadInput(me, other, false); }
            bool front = GetForegroundWindow() == to;
            if (front) Seen(to);
            return front;
        }

        // Просит окно закрыться — так же, как крестик в заголовке: программа может переспросить про несохранённое.
        // Если окно ждёт ответа в своём диалоге, просьба уходит диалогу.
        public static void Close(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
                IntPtr to = hwnd;
                if (!IsWindowEnabled(hwnd))
                {
                    IntPtr popup = GetLastActivePopup(hwnd);
                    if (popup != IntPtr.Zero && IsWindow(popup) && IsWindowVisible(popup)) to = popup;
                }
                PostMessage(to, WM_SYSCOMMAND, new IntPtr(SC_CLOSE), IntPtr.Zero);
                Used.Forget(hwnd);
            }
            catch { }
        }

        // ───────── Живые превью ─────────

        public static IntPtr ThumbRegister(IntPtr dest, IntPtr source)
        {
            if (Aside.On) return IntPtr.Zero;
            IntPtr thumb;
            Calls.Add(ref Calls.Thumbs);
            try { return DwmRegisterThumbnail(dest, source, out thumb) == 0 ? thumb : IntPtr.Zero; } catch { return IntPtr.Zero; }
        }

        // Посчитать обращение прямо в условии (для --asideprobe); всегда true
        static bool Counted(ref int counter) { Calls.Add(ref counter); return true; }

        public static void ThumbRelease(IntPtr thumb)
        {
            try { if (thumb != IntPtr.Zero) DwmUnregisterThumbnail(thumb); } catch { }
        }

        // Какую часть окна показывать: видимая рамка без невидимых полей для растягивания. false — показывать нечего.
        public static bool ThumbSource(IntPtr thumb, IntPtr source, out RECT part)
        {
            part = new RECT();
            SIZE size;
            if (DwmQueryThumbnailSourceSize(thumb, out size) != 0 || size.W < 8 || size.H < 8) return false;
            part.Right = size.W; part.Bottom = size.H;
            RECT win, frame;
            if (GetWindowRect(source, out win) && win.Right - win.Left == size.W && win.Bottom - win.Top == size.H
                && DwmGetWindowAttribute(source, DWMWA_FRAME, out frame, Marshal.SizeOf(typeof(RECT))) == 0
                && frame.Left >= win.Left && frame.Top >= win.Top && frame.Right <= win.Right && frame.Bottom <= win.Bottom && frame.Right - frame.Left >= 8 && frame.Bottom - frame.Top >= 8)
            {
                part.Left = frame.Left - win.Left; part.Top = frame.Top - win.Top;
                part.Right = frame.Right - win.Left; part.Bottom = frame.Bottom - win.Top;
            }
            return true;
        }

        public static bool ThumbUpdate(IntPtr thumb, RECT dest, RECT source, byte opacity, bool visible)
        {
            var p = new THUMBPROPS { Flags = 0x1 | 0x2 | 0x4 | 0x8 | 0x10, Dest = dest, Source = source, Opacity = opacity, Visible = visible, ClientOnly = false };
            try { return DwmUpdateThumbnailProperties(thumb, ref p) == 0; } catch { return false; }
        }

        // ───────── Значки ─────────

        static readonly Dictionary<string, ImageSource> icons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Значок окна. Есть в памяти — отдаёт сразу; нет — возвращает null, добывает его в фоне и зовёт ready в потоке окна.
        // Открытие переключателя из-за значков не ждёт никогда.
        // У окна игры значок не спрашивается ни у окна, ни у файла программы: в карточке — плитка с буквой (Demo.IconOf).
        public static ImageSource Icon(WinItem w, Dispatcher ui, Action<WinItem, ImageSource> ready)
        {
            if (w == null || !Logic.MayTouch(w) || Aside.On) return null;
            string key = w.Path ?? "hwnd|" + w.Hwnd;
            lock (icons)
            {
                ImageSource img;
                if (icons.TryGetValue(key, out img)) return img;
                if (!loading.Add(key)) return null;   // уже добывается; появится при следующем показе
            }
            string path = w.Path;
            IntPtr hwnd = w.Hwnd;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ImageSource img = null;
                try { img = Load(path, hwnd); } catch { }
                lock (icons)
                {
                    loading.Remove(key);
                    if (img != null || path != null) icons[key] = img;   // значок, взятый у самого окна, запоминаем только удачный
                }
                if (img != null && ui != null && ready != null) ui.BeginInvoke(new Action(delegate { ready(w, img); }));
            });
            return null;
        }

        static ImageSource Load(string path, IntPtr hwnd)
        {
            ImageSource img = null;
            if (path != null)
            {
                img = StoreIcon(path);
                if (img == null) img = FileIcon(path);
            }
            return img ?? WindowIcon(hwnd);
        }

        static ImageSource FromHandle(IntPtr icon)
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }

        static ImageSource FileIcon(string path)
        {
            var h = new IntPtr[1];
            if (!File.Exists(path) || PrivateExtractIcons(path, 0, 64, 64, h, new uint[1], 1, 0) == 0 || h[0] == IntPtr.Zero) return null;
            try { return FromHandle(h[0]); }
            finally { DestroyIcon(h[0]); }
        }

        // Значок приложения из Магазина: картинка из его папки, названная в AppxManifest.xml
        static ImageSource StoreIcon(string path)
        {
            string dir = Path.GetDirectoryName(path);
            for (int up = 0; up < 3 && dir != null; up++, dir = Path.GetDirectoryName(dir))
            {
                string manifest = Path.Combine(dir, "AppxManifest.xml");
                if (!File.Exists(manifest)) continue;
                string logo = StoreLogo.FromManifest(File.ReadAllText(manifest));
                if (logo == null) return null;
                string folder = Path.GetDirectoryName(Path.Combine(dir, logo));
                if (!Directory.Exists(folder)) return null;
                string file = StoreLogo.Pick(logo, Directory.GetFiles(folder, Path.GetFileNameWithoutExtension(logo) + ".*"));
                if (file == null && File.Exists(Path.Combine(dir, logo))) file = Path.Combine(dir, logo);
                if (file == null) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(file);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            return null;
        }

        // Значок, который окно показывает в заголовке: мелкий, но он есть почти у всех
        static ImageSource WindowIcon(IntPtr hwnd)
        {
            IntPtr icon;
            if (SendMessageTimeout(hwnd, 0x007F, new IntPtr(1), IntPtr.Zero, 0x0002, 100, out icon) == IntPtr.Zero) icon = IntPtr.Zero;   // WM_GETICON, большой; зависшее окно не ждём
            if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, GCLP_HICON);
            return icon == IntPtr.Zero ? null : FromHandle(icon);   // значок принадлежит окну — не уничтожаем
        }

        // Заранее, в фоне, добывает значки открытых сейчас окон — к первому Alt+Tab они уже в памяти
        public static void WarmIcons()
        {
            if (Aside.On) return;   // в стороне чужие окна не перечисляются
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    bool first;
                    foreach (WinItem w in List(false, false, out first)) Icon(w, null, null);
                }
                catch { }
            });
        }
    }
}
