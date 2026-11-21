// smena.txt. Iconic.cs — картинка свёрнутого окна для объёмных видов.
// Windows.Graphics.Capture у свёрнутого окна кадров не даёт, но DWM хранит его последнюю поверхность — её показывает
// панель задач. Путь: своё вспомогательное окно (на экране, но спрятанное DWM, без фокуса, не в Alt+Tab и не на панели
// задач) → в нём миниатюра DWM свёрнутого окна во всё окно → кадр этого вспомогательного окна через WGC (запасной путь,
// через память) → замороженная картинка. После первого годного кадра захват, миниатюра и окно сразу закрываются.
// Пока окно свёрнуто, его картинка не меняется — она запоминается (Stills) до разворачивания или закрытия окна.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevTxt.Smena
{
    sealed class IconicShot : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential)]
        struct THUMBPROPS { public uint Flags; public Win.RECT Dest, Source; public byte Opacity; public bool Visible, ClientOnly; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr source, out IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMBPROPS props);

        public static int Count;            // сколько вспомогательных окон сейчас есть — для проверки утечек

        IntPtr helper, thumb;
        LiveWindow live;
        bool later;                          // объект захвата ещё готовится (CaptureItems.Later): спросим в следующем кадре
        readonly Stopwatch age = Stopwatch.StartNew();
        public readonly IntPtr Source;
        public int SourceW, SourceH, W, H;   // размер свёрнутого окна по DWM и размер вспомогательного окна
        public BitmapSource Image;           // готовая картинка (заморожена) или null
        public bool Done;                    // больше ждать нечего: картинка есть или её не будет
        public string Why = "";              // почему картинки нет
        public double Ms;                    // сколько мс от открытия до картинки

        IconicShot(IntPtr source) { Source = source; }

        // Вспомогательное окно в левом верхнем углу монитора mon (в точках экрана), миниатюра, захват.
        // null — не вышло (why — почему). Объект захвата, который ещё готовится, спрашивается снова в Pull.
        public static IconicShot Open(IntPtr source, Win.RECT mon, out string why)
        {
            if (Aside.On) { why = "smena.txt в стороне: идёт игра"; return null; }
            var s = new IconicShot(source);
            lock (open) open.Add(s);
            try { why = s.Start(mon); }
            catch (Exception e) { why = e.Message; }
            if (why.Length == 0) return s;
            s.Dispose();
            return null;
        }

        string Start(Win.RECT mon)
        {
            // WS_POPUP без рамки; WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE — не в Alt+Tab, не на панели задач, без фокуса.
            // Класс STATIC — готовый системный: своя оконная процедура не нужна, рисовать окну нечего (всё закрывает миниатюра).
            helper = CreateWindowEx((uint)(Logic.WS_EX_TOOLWINDOW | Logic.WS_EX_NOACTIVATE),"STATIC", "smena.txt", 0x80000000, mon.Left, mon.Top, Still.MinSide, Still.MinSide, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (helper == IntPtr.Zero) return "вспомогательное окно не создалось";
            Count++;
            // Спрятать ещё до показа; не спряталось — не показываем. За экраном окно держать нельзя: такое WGC снимает пустым.
            int on = 1, cloaked;
            if (DwmSetWindowAttribute(helper, 13, ref on, 4) != 0 || DwmGetWindowAttribute(helper, 14, out cloaked, 4) != 0 || cloaked == 0)
                return "вспомогательное окно не спряталось";
            Win.NoTransitions(helper);
            Calls.Add(ref Calls.Thumbs);
            if (DwmRegisterThumbnail(helper, Source, out thumb) != 0 || thumb == IntPtr.Zero) { thumb = IntPtr.Zero; return "миниатюра DWM не завелась"; }
            SIZE size;
            if (DwmQueryThumbnailSourceSize(thumb, out size) != 0) return "у миниатюры нет размера";
            SourceW = size.W; SourceH = size.H;
            int w, h;
            if (!Still.Fit(size.W, size.H, mon.Right - mon.Left, mon.Bottom - mon.Top, out w, out h))
                return "у DWM нет картинки окна (" + size.W + "×" + size.H + ")";
            W = w; H = h;
            Win.Move(helper, mon.Left, mon.Top, w, h);
            // Окно целиком, без указания части: так DWM сам отбрасывает невидимые края для растягивания
            var p = new THUMBPROPS { Flags = 0x1 | 0x4 | 0x8 | 0x10, Dest = new Win.RECT { Right = w, Bottom = h }, Opacity = 255, Visible = true, ClientOnly = false };
            if (DwmUpdateThumbnailProperties(thumb, ref p) != 0) return "миниатюра DWM не встала";
            ShowWindow(helper, 4);   // SW_SHOWNOACTIVATE
            return Capture(LiveWindow.QuickWait);
        }

        string Capture(int waitMs)
        {
            string why;
            live = LiveWindow.Open(helper, false, true, true, waitMs, out why);
            later = live == null && why == CaptureItems.Later;
            if (live == null && !later) return why.Length > 0 ? why : "захват не завёлся";
            return "";
        }

        // Забрать кадр, если пришёл. true — картинка готова (Image); захват и окно к этому времени уже закрыты.
        public bool Pull()
        {
            if (Done) return Image != null;
            if (later) { string why = Capture(0); if (why.Length > 0) { Finish(why); return false; } }
            if (live != null)
            {
                bool got = live.Pull();
                if (live.Dead) { Finish("захват умер: " + live.Error); return false; }
                var bmp = live.Image as BitmapSource;
                if (got && bmp != null && !Still.Flat(Sample(bmp), Still.Tolerance))
                {
                    var copy = new WriteableBitmap(bmp);
                    copy.Freeze();
                    Image = copy;
                    Ms = age.Elapsed.TotalMilliseconds;
                    Finish("");
                    return true;
                }
            }
            if (age.ElapsedMilliseconds > Still.GiveUpMs) Finish(live == null ? "объект захвата не успел" : live.Frames == 0 ? "кадров нет" : "миниатюра пустая");
            return false;
        }

        // Редкая сетка точек кадра (BGRA): по ней видно, нарисовал ли DWM что-то, кроме ровной заливки
        static byte[] Sample(BitmapSource bmp)
        {
            int n = Still.Grid, w = bmp.PixelWidth, h = bmp.PixelHeight;
            var px = new byte[n * n * 4];
            var row = new byte[w * 4];
            try
            {
                for (int j = 0; j < n; j++)
                {
                    bmp.CopyPixels(new Int32Rect(0, (int)((j + 0.5) * h / n), w, 1), row, row.Length, 0);
                    for (int i = 0; i < n; i++) Buffer.BlockCopy(row, (int)((i + 0.5) * w / n) * 4, px, (j * n + i) * 4, 4);
                }
                return px;
            }
            catch { return null; }
        }

        void Finish(string why)
        {
            Why = why;
            Done = true;
            Close();
        }

        void Close()
        {
            later = false;
            if (live != null) { live.Dispose(); live = null; }
            if (thumb != IntPtr.Zero) { try { DwmUnregisterThumbnail(thumb); } catch { } thumb = IntPtr.Zero; }
            if (helper != IntPtr.Zero) { try { DestroyWindow(helper); } catch { } helper = IntPtr.Zero; Count--; }
        }

        public void Dispose()
        {
            if (!Done) { Done = true; if (Why.Length == 0) Why = "закрыто"; }
            Close();
            lock (open) open.Remove(this);
        }

        static readonly List<IconicShot> open = new List<IconicShot>();   // ещё не закрытые — для CloseAll

        // Закрыть все снимки разом (smena.txt отходит в сторону): окна, миниатюры и захваты
        public static void CloseAll()
        {
            IconicShot[] all;
            lock (open) all = open.ToArray();
            foreach (IconicShot s in all) { try { s.Dispose(); } catch { } }
        }
    }

    // Запомненные картинки свёрнутых окон, только в памяти. Запись живёт, пока окно свёрнуто: разворачивание и новое
    // сворачивание (Win.Watch) и пропажа окна из списка (Keep) её убирают; давние вытесняются (StillCache).
    static class Stills
    {
        static readonly StillCache<BitmapSource> cache = new StillCache<BitmapSource>(Still.CacheCount, Still.CacheBytes);

        public static BitmapSource Get(IntPtr hwnd) { return cache.Get(hwnd); }

        public static void Put(IntPtr hwnd, BitmapSource img)
        {
            if (img != null) cache.Put(hwnd, img, (long)img.PixelWidth * img.PixelHeight * 4);
        }

        public static void Forget(IntPtr hwnd) { cache.Forget(hwnd); }

        // Остаются только окна, которые сейчас в списке и свёрнуты
        public static void Keep(List<WinItem> items)
        {
            var minimized = new HashSet<IntPtr>();
            foreach (WinItem it in items) if (it.Minimized && !it.Fake) minimized.Add(it.Hwnd);
            cache.Keep(delegate(IntPtr h) { return minimized.Contains(h); });
        }

        // Забыть все картинки (smena.txt отходит в сторону)
        public static void Clear() { cache.Keep(delegate(IntPtr h) { return false; }); }

        public static int Count { get { return cache.Count; } }
        public static long Bytes { get { return cache.Bytes; } }
    }
}
