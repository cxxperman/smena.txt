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
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
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
        const long WS_CAPTION = 0x00C00000;
        const uint GW_OWNER = 4;
        const int SW_RESTORE = 9, DWMWA_FRAME = 9, DWMWA_CLOAKED = 14, CLOAKED_SHELL = 2;
        const string FrameClass = "ApplicationFrameWindow";
        public const int MaskKey = 0xE8;   // клавиша, которой нет ни на одной клавиатуре
        public static readonly IntPtr OurInput = new IntPtr(0x534D454E);   // пометка «это нажатие послали мы сами»

        public static readonly uint Self = (uint)Process.GetCurrentProcess().Id;

        class Known { public uint Pid; public string Path, Key; public bool Frame; }
        static readonly Dictionary<IntPtr, Known> known = new Dictionary<IntPtr, Known>();   // чьё окно — узнаём один раз
        static IVirtualDesktopManager desktops;
        static bool desktopsFailed;

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

        // Окна для переключателя: отобраны, с путём к программе; окно, в котором человек сейчас, — первое.
        public static List<WinItem> List(bool allDesktops, bool desktops, out bool foregroundFirst)
        {
            var raw = Logic.Filter(Enumerate(desktops && allDesktops), Self, allDesktops);
            var list = new List<WinItem>();
            lock (known)
            {
                var next = new Dictionary<IntPtr, Known>();
                foreach (RawWin w in raw)
                {
                    Known k;
                    if (!known.TryGetValue(w.Hwnd, out k) || k.Pid != w.Pid || k.Path == null || (k.Frame && k.Key.StartsWith("frame|")))
                    {
                        k = new Known { Pid = w.Pid, Path = PathOf(w.Pid) };
                        k.Frame = Class(w.Hwnd) == FrameClass;
                        if (k.Frame)
                        {
                            string inner = InnerPath(w.Hwnd, w.Pid);
                            if (inner != null) k.Path = inner;
                            else k.Key = "frame|" + w.Title.ToLowerInvariant();   // свёрнуто: приложения внутри нет, узнаём по заголовку
                        }
                        if (k.Key == null) k.Key = k.Path != null ? k.Path.ToLowerInvariant() : "pid|" + w.Pid;
                    }
                    next[w.Hwnd] = k;
                    list.Add(new WinItem { Hwnd = w.Hwnd, Title = w.Title, Path = k.Key.StartsWith("frame|") ? null : k.Path, Key = k.Key, Minimized = w.Minimized });
                }
                known.Clear();
                foreach (var kv in next) known[kv.Key] = kv.Value;
            }
            foregroundFirst = Logic.ForegroundFirst(list, GetForegroundWindow());
            return list;
        }

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

        // Впереди окно на весь свой монитор и без заголовка (игра, видео)? Рабочий стол и панель задач не в счёт.
        // Вызывается из потока перехватчика: только быстрые вопросы к Windows, ни одного — к самой программе.
        public static bool FullscreenAhead() { return Fullscreen(GetForegroundWindow()); }

        // Окно на весь экран без заголовка — игра или видео. Невидимые слои поверх экрана (оверлей NVIDIA, ввод Windows,
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
                bool caption = (GetWindowLongPtr(fg, GWL_STYLE).ToInt64() & WS_CAPTION) == WS_CAPTION;
                RECT m = info.Monitor;
                return Logic.IsFullscreen(r.Left, r.Top, r.Right, r.Bottom, m.Left, m.Top, m.Right, m.Bottom, caption);
            }
            catch { return false; }
        }

        public static bool KeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        // ───────── Сторож: системный Alt+Tab открылся, хотя должен был наш ─────────
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token, int cls, out int value, int size, out int length);
        static WinEventProc watchProc;   // ссылка хранится, чтобы сборщик мусора не убрал её из-под Windows

        // Вызывает seen(окно, что было впереди), когда на экране появился системный переключатель окон при зажатом Alt.
        // Работает в потоке окна: события приходят через его очередь сообщений и ничего не тормозят.
        public static void WatchSystemSwitcher(Action<IntPtr> seen)
        {
            IntPtr before = IntPtr.Zero;
            DateTime last = DateTime.MinValue;
            watchProc = delegate(IntPtr h, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
            {
                try
                {
                    if (obj != 0) return;   // OBJID_WINDOW: само окно, а не что-то внутри него
                    bool system = ev == 0x0014;   // EVENT_SYSTEM_SWITCHSTART
                    if (ev == 0x0003)             // EVENT_SYSTEM_FOREGROUND
                    {
                        string cls = Class(hwnd);
                        system = cls == "XamlExplorerHostIslandWindow" || cls == "MultitaskingViewFrame" || cls == "TaskSwitcherWnd";
                        if (!system) { before = hwnd; return; }
                    }
                    if (!system || !KeyDown(KeyMachine.Alt)) return;   // Win+Tab и прочее — не наше дело
                    if ((DateTime.UtcNow - last).TotalMilliseconds < 800) return;   // одно открытие — одно сообщение
                    last = DateTime.UtcNow;
                    seen(before);
                }
                catch { }
            };
            SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, watchProc, 0, 0, 2);   // WINEVENT_OUTOFCONTEXT | SKIPOWNPROCESS
            SetWinEventHook(0x0014, 0x0014, IntPtr.Zero, watchProc, 0, 0, 2);
        }

        // Окно программы, запущенной от администратора. Пока такое окно впереди, Windows не показывает обычным
        // программам нажатия клавиш — и Alt+Tab там всегда системный. Не смогли спросить — значит, права выше наших.
        public static bool Elevated(IntPtr hwnd)
        {
            uint pid;
            if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out pid) == 0 || pid == 0) return false;
            IntPtr p = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (p == IntPtr.Zero) return true;
            try
            {
                IntPtr token;
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
            try { uint pid; GetWindowThreadProcessId(hwnd, out pid); name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
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

        // ───────── Вывести окно вперёд ─────────

        // Нажатие и отпускание клавиши, которой не существует. Нужно дважды:
        // 1) пока Alt зажат — чтобы программа под нами не открыла своё меню, когда Alt отпустят («Alt был с чем-то»);
        // 2) перед переключением — Windows разрешает менять переднее окно тому, от кого пришёл последний ввод.
        public static void SendMask()
        {
            try
            {
                var down = new INPUT { Type = 1 };
                down.Key.Vk = MaskKey; down.Key.Extra = OurInput;
                INPUT up = down;
                up.Key.Flags = 2;   // KEYEVENTF_KEYUP
                SendInput(2, new INPUT[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
            }
            catch { }
        }

        // Выводит окно вперёд; свёрнутое разворачивает. true — окно действительно стало передним.
        // На что опираемся, по порядку:
        //  1. обычный SetForegroundWindow — Windows разрешает его, пока человек держит или только что отпустил Alt+Tab;
        //  2. не вышло — посылаем пустое нажатие (последний ввод теперь наш) и пробуем снова;
        //  3. не вышло — на время присоединяемся к очереди ввода переднего окна (AttachThreadInput) и пробуем ещё раз.
        public static bool Activate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
            if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);
            if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;
            SendMask();
            if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;
            uint pid, me = GetCurrentThreadId(), other = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            bool attached = other != 0 && other != me && AttachThreadInput(me, other, true);
            try
            {
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally { if (attached) AttachThreadInput(me, other, false); }
            return GetForegroundWindow() == hwnd;
        }

        // ───────── Живые превью ─────────

        public static IntPtr ThumbRegister(IntPtr dest, IntPtr source)
        {
            IntPtr thumb;
            try { return DwmRegisterThumbnail(dest, source, out thumb) == 0 ? thumb : IntPtr.Zero; } catch { return IntPtr.Zero; }
        }

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
        public static ImageSource Icon(WinItem w, Dispatcher ui, Action<WinItem, ImageSource> ready)
        {
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
