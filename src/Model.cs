// smena.txt. Model.cs — всё, что решается без экрана и без клавиатуры: какие окна показывать и в каком порядке,
// куда сдвигается выбор, что значит каждая клавиша, когда Alt+Tab остаётся системным, сколько рядов и что видно.
// Здесь нет обращений к Windows — поэтому всё это проверяется в --selftest.
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace DevTxt.Smena
{
    // Окно, как его отдала Windows, до отбора
    class RawWin
    {
        public IntPtr Hwnd;
        public bool Visible, Owned, Minimized;
        public long ExStyle;
        public int Cloaked;                 // 0 — не спрятано системой
        public bool OtherDesktop, Ghost;    // спрятано, потому что на другом рабочем столе; пустая рамка приложения из Магазина
        public string Title;
        public uint Pid;
    }

    // Окно в переключателе
    class WinItem
    {
        public IntPtr Hwnd;
        public string Title, Path, Key;     // Key — по нему окна одного приложения узнают друг друга
        public bool Minimized;
        public ImageSource Icon;
        // Только для выдуманных окон (--demo, снимки)
        public bool Fake;
        public Color Tint;
        public double Aspect;
    }

    static class Logic
    {
        public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;

        // Окно попало бы в системный Alt+Tab: видно, не служебное, с заголовком, не наше.
        // Спрятанные системой окна (другой рабочий стол, приложение из Магазина в фоне) не берём;
        // при allDesktops берём те из них, что лежат на другом рабочем столе.
        public static bool Eligible(RawWin w, uint self, bool allDesktops)
        {
            if (w == null || !w.Visible || w.Pid == self) return false;
            bool app = (w.ExStyle & WS_EX_APPWINDOW) != 0;
            if (w.Owned && !app) return false;
            if ((w.ExStyle & WS_EX_TOOLWINDOW) != 0) return false;
            if (!app && (w.ExStyle & WS_EX_NOACTIVATE) != 0) return false;
            if (w.Cloaked != 0 && !(allDesktops && w.OtherDesktop && !w.Ghost)) return false;
            return !string.IsNullOrEmpty(w.Title);
        }

        // Отбор с сохранением порядка: Windows перечисляет окна сверху вниз, то есть от недавнего к давнему
        public static List<RawWin> Filter(IEnumerable<RawWin> zorder, uint self, bool allDesktops)
        {
            var list = new List<RawWin>();
            foreach (RawWin w in zorder) if (Eligible(w, self, allDesktops)) list.Add(w);
            return list;
        }

        // Окно, в котором человек сейчас, — всегда первое: окна «поверх всех» стоят в порядке Windows выше него,
        // хотя ими давно не пользовались. true — оно в списке есть.
        public static bool ForegroundFirst(List<WinItem> list, IntPtr foreground)
        {
            int at = list.FindIndex(delegate(WinItem w) { return w.Hwnd == foreground; });
            if (at < 0 || foreground == IntPtr.Zero) return false;
            WinItem fg = list[at];
            list.RemoveAt(at);
            list.Insert(0, fg);
            return true;
        }

        // Впереди может быть диалог или всплывающее окно программы, которого в списке нет: тогда «текущее окно» — его
        // владелец. chain — переднее окно и его владельцы по порядку; возвращает первое из них, что есть в списке.
        public static IntPtr Listed(List<WinItem> list, IList<IntPtr> chain)
        {
            if (list == null || chain == null) return IntPtr.Zero;
            foreach (IntPtr h in chain)
                if (h != IntPtr.Zero && list.Exists(delegate(WinItem w) { return w.Hwnd == h; })) return h;
            return IntPtr.Zero;
        }

        // Окна того же приложения, в том же порядке
        public static List<WinItem> SameApp(List<WinItem> list, string key)
        {
            var same = new List<WinItem>();
            foreach (WinItem w in list) if (key != null && string.Equals(w.Key, key, StringComparison.OrdinalIgnoreCase)) same.Add(w);
            return same;
        }

        // Сдвиг выбора по кругу
        public static int Move(int index, int count, int delta)
        {
            if (count <= 0) return 0;
            return ((index + delta) % count + count) % count;
        }

        // Вверх и вниз по рядам: за край не уходит; вниз с предпоследнего ряда на неполный последний — к его последнему окну
        public static int MoveRow(int index, int count, int cols, int dir)
        {
            if (count <= 0 || cols <= 0) return 0;
            int to = index + dir * cols;
            if (to < 0) return index;
            if (to >= count) return index / cols < (count - 1) / cols ? count - 1 : index;
            return to;
        }

        // С чего начинать: вперёд — с предыдущего окна (текущее стоит первым), назад — с самого давнего
        public static int StartIndex(int count, bool foregroundFirst, bool backward)
        {
            if (count <= 1) return 0;
            if (backward) return count - 1;
            return foregroundFirst ? 1 : 0;
        }

        // Окно на весь экран (игра, видео): без заголовка, ровно по границам монитора, не развёрнутое и без рамки для
        // растягивания. Развёрнутое окно без своего заголовка (Steam, программы на Electron, окна WindowStyle.None) —
        // обычное окно: оно развёрнуто (maximized), у него есть рамка (thickFrame) или оно выходит за края монитора.
        public static bool IsFullscreen(int left, int top, int right, int bottom, int monLeft, int monTop, int monRight, int monBottom, bool caption, bool maximized, bool thickFrame)
        {
            return !caption && !maximized && !thickFrame && left == monLeft && top == monTop && right == monRight && bottom == monBottom;
        }

        // Когда Alt+Tab остаётся системным
        public static bool LeaveAlone(bool paused, bool replace, bool notInGames, bool fullscreen)
        {
            return paused || !replace || (notInGames && fullscreen);
        }
    }

    // Какими окнами пользовались недавно. Порядок Windows (сверху вниз) для этого не годится: окна «поверх всех» в нём
    // всегда первые, хотя к ним могли не подходить весь день. Список пополняется, когда впереди оказывается новое окно.
    class Recent
    {
        const int Max = 256;
        readonly List<IntPtr> order = new List<IntPtr>();   // от недавнего к давнему

        // Окно вышло вперёд
        public void Touch(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            lock (order)
            {
                order.Remove(hwnd);
                order.Insert(0, hwnd);
                if (order.Count > Max) order.RemoveAt(order.Count - 1);
            }
        }

        // Окно свернули или закрыли: оно больше не «недавнее» и встаёт туда, куда его поставила Windows
        public void Forget(IntPtr hwnd)
        {
            lock (order) order.Remove(hwnd);
        }

        // Расставляет окна: сначала те, которыми пользовались, от недавнего к давнему; остальные — следом, в прежнем порядке
        public void Sort(List<WinItem> list)
        {
            var seen = new List<WinItem>();
            lock (order)
            {
                foreach (IntPtr h in order)
                {
                    WinItem w = list.Find(delegate(WinItem x) { return x.Hwnd == h; });
                    if (w != null && !seen.Contains(w)) seen.Add(w);
                }
            }
            if (seen.Count == 0) return;
            list.RemoveAll(delegate(WinItem x) { return seen.Contains(x); });
            list.InsertRange(0, seen);
        }
    }

    enum KeyAction { None, Open, OpenBack, OpenSame, Next, Prev, Left, Right, Up, Down, Commit, Cancel, Close }

    // Что значит каждая клавиша. Этот же код вызывает перехватчик клавиатуры (Hook.cs) — и больше он не делает ничего:
    // спросил, передал действие окну, вернул «проглотить или нет».
    class KeyMachine
    {
        public const int Tab = 0x09, Enter = 0x0D, Shift = 0x10, Ctrl = 0x11, Alt = 0x12, Esc = 0x1B, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Delete = 0x2E;
        public const int LWin = 0x5B, RWin = 0x5C, LShift = 0xA0, RAlt = 0xA5;   // 0xA0…0xA5 — левые и правые Shift, Ctrl, Alt
        public const int GraveScan = 0x29;                                          // клавиша над Tab: «`» или «ё», в любой раскладке

        readonly object gate = new object();
        readonly bool[] eaten = new bool[256];   // нажатие проглочено — отпускание проглатывается тоже
        bool active, sticky;
        int generation;                          // номер переключения: растёт с каждым новым Alt+Tab и показом командой

        public Func<bool> LeaveAlone = delegate { return false; };   // спрашивается только в момент первого Alt+Tab
        public Func<bool> SameApp = delegate { return true; };       // включено ли Alt+`

        public bool Active { get { lock (gate) return active; } }
        public bool Sticky { get { lock (gate) return sticky; } }

        public int Generation { get { lock (gate) return generation; } }

        // Перехватчик снят: что бы ни шло, оно закончено
        public void End() { lock (gate) { active = false; sticky = false; } }

        // Показ номер gen закрыт не клавишей: щелчком, потерей Alt, ошибкой. Окно узнаёт об этом позже перехватчика,
        // и к тому времени человек мог уже начать следующий Alt+Tab — опоздавшее «закончено» его не трогает.
        public void End(int gen) { lock (gate) { if (gen != generation) return; active = false; sticky = false; } }

        // Переключатель открыт командой и ждёт без удержания Alt. Возвращает номер этого показа.
        public int BeginSticky() { lock (gate) { active = true; sticky = true; return ++generation; } }

        static bool IsAlt(int vk) { return vk == Alt || vk == RAlt - 1 || vk == RAlt; }
        static bool IsModifier(int vk) { return vk == Shift || vk == Ctrl || vk == Alt || vk == LWin || vk == RWin || (vk >= LShift && vk <= RAlt); }

        static KeyAction Arrow(int vk)
        {
            return vk == Left ? KeyAction.Left : vk == Right ? KeyAction.Right : vk == Up ? KeyAction.Up : vk == Down ? KeyAction.Down : KeyAction.None;
        }

        // vk, scan — клавиша; down — нажата или отпущена; alt — зажат ли Alt в этот момент;
        // shift — зажат ли Shift; other — зажат Ctrl или Win (тогда сочетание не наше).
        // Возвращает true — клавишу проглотить; action — что сделать переключателю; gen — к какому переключению
        // это действие относится (с этим номером окно потом зовёт End).
        public bool Key(int vk, int scan, bool down, bool alt, bool shift, bool other, out KeyAction action, out int gen)
        {
            action = KeyAction.None;
            vk &= 0xFF;
            lock (gate)
            {
                gen = generation;
                if (!down)
                {
                    bool was = eaten[vk];
                    eaten[vk] = false;
                    // Alt отпущен — переключаемся. Само отпускание уходит дальше, как обычно.
                    if (IsAlt(vk) && active && !sticky) { active = false; action = KeyAction.Commit; return false; }
                    return was;
                }

                bool tab = vk == Tab, grave = scan == GraveScan && !IsModifier(vk) && !tab;
                if (sticky)
                {
                    if (tab || grave) action = shift ? KeyAction.Prev : KeyAction.Next;
                    else if (vk == Esc) action = KeyAction.Cancel;
                    else if (vk == Enter) action = KeyAction.Commit;
                    else if (vk == Delete) action = KeyAction.Close;
                    else action = Arrow(vk);
                    if (action == KeyAction.Cancel || action == KeyAction.Commit) { active = false; sticky = false; }
                }
                else if (active)
                {
                    if (IsModifier(vk)) return false;
                    // Клавиша пришла без Alt: его отпускание прошло мимо нас. Заканчиваем, клавишу не трогаем.
                    if (!alt) { active = false; action = KeyAction.Commit; return false; }
                    if (tab || grave) action = shift ? KeyAction.Prev : KeyAction.Next;
                    else if (vk == Esc) action = KeyAction.Cancel;
                    else if (vk == Enter) action = KeyAction.Commit;
                    else if (vk == Delete) action = KeyAction.Close;
                    else action = Arrow(vk);
                    if (action == KeyAction.Cancel || action == KeyAction.Commit) active = false;
                    // Пока переключатель открыт, все клавиши с Alt — наши, даже те, что ничего не значат:
                    // иначе Alt+буква открывает меню в программе под нами
                    eaten[vk] = true;
                    return true;
                }
                else
                {
                    if (!alt || other || !(tab || grave)) return false;
                    if (grave && !SameApp()) return false;
                    if (LeaveAlone()) return false;
                    active = true;
                    gen = ++generation;
                    action = grave ? KeyAction.OpenSame : shift ? KeyAction.OpenBack : KeyAction.Open;
                }
                if (action == KeyAction.None) return false;
                eaten[vk] = true;
                return true;
            }
        }
    }

    // Один показ переключателя: сколько окон и какое выбрано. Им пользуется и окно, и проверки.
    class Session
    {
        public bool Open;
        public int Count, Cols = 1, Selected;

        public void Begin(int count, int cols, bool foregroundFirst, bool backward)
        {
            Count = count;
            Cols = Math.Max(1, cols);
            Selected = Logic.StartIndex(count, foregroundFirst, backward);
            Open = count > 0;
        }

        // true — выбор сдвинулся
        public bool Move(KeyAction a)
        {
            if (!Open) return false;
            int to = Selected;
            if (a == KeyAction.Next || a == KeyAction.Right) to = Logic.Move(Selected, Count, 1);
            else if (a == KeyAction.Prev || a == KeyAction.Left) to = Logic.Move(Selected, Count, -1);
            else if (a == KeyAction.Up) to = Logic.MoveRow(Selected, Count, Cols, -1);
            else if (a == KeyAction.Down) to = Logic.MoveRow(Selected, Count, Cols, 1);
            if (to == Selected) return false;
            Selected = to;
            return true;
        }

        // Окно номер index убрали из показа (его закрыли). Выбор остаётся на том же окне, а если убрали выбранное —
        // переходит на занявшее его место. cols — сколько теперь колонок. false — окон не осталось, показ закрыт.
        public bool Remove(int index, int cols)
        {
            if (!Open || index < 0 || index >= Count) return Open;
            Count--;
            Cols = Math.Max(1, cols);
            if (index < Selected) Selected--;
            if (Selected >= Count) Selected = Math.Max(0, Count - 1);
            if (Count == 0) Open = false;
            return Open;
        }

        // Закрывает показ. Возвращает номер выбранного окна или -1, если показа не было.
        public int Close()
        {
            int chosen = Open ? Selected : -1;
            Open = false;
            return chosen;
        }
    }

    // Раскладка карточек: ряды, прокрутка, место превью внутри карточки
    static class Layout
    {
        public const double Gap = 10, Pad = 8, Caption = 30, Grow = 4, Margin = 10;
        public const double ThumbRatio = 0.62;   // высота превью к ширине
        public const double MinPreview = 160, MaxPreview = 360, StepPreview = 20, DefaultPreview = 240;

        public static double CardW(double preview) { return preview + Pad * 2; }
        public static double ThumbH(double preview) { return Math.Round(preview * ThumbRatio); }
        public static double CardH(double preview) { return Pad + ThumbH(preview) + Caption + Pad / 2; }

        // Сколько колонок и рядов: ряды поровну (9 окон — это 5 и 4, а не 7 и 2); visible — сколько рядов помещается по высоте
        public static void Rows(int count, double cardW, double cardH, double maxW, double maxH, out int cols, out int rows, out int visible)
        {
            if (count < 1) count = 1;
            int maxCols = Math.Max(1, (int)Math.Floor((maxW + Gap) / (cardW + Gap)));
            rows = (count + maxCols - 1) / maxCols;
            cols = (count + rows - 1) / rows;
            int maxRows = Math.Max(1, (int)Math.Floor((maxH + Gap) / (cardH + Gap)));
            visible = Math.Min(rows, maxRows);
        }

        // Первый видимый ряд — такой, чтобы выбранный был виден; двигается не больше, чем нужно
        public static int Scroll(int first, int selectedRow, int visible, int rows)
        {
            if (selectedRow < first) first = selectedRow;
            if (selectedRow >= first + visible) first = selectedRow - visible + 1;
            return Math.Max(0, Math.Min(first, Math.Max(0, rows - visible)));
        }

        // Вписывает окно sw×sh в прямоугольник, сохраняя пропорции; больше, чем maxScale, не растягивает
        public static void Fit(double sw, double sh, double bx, double by, double bw, double bh, double maxScale, out double x, out double y, out double w, out double h)
        {
            if (sw <= 0 || sh <= 0) { x = bx; y = by; w = bw; h = bh; return; }
            double k = Math.Min(Math.Min(bw / sw, bh / sh), maxScale);
            w = sw * k; h = sh * k;
            x = bx + (bw - w) / 2; y = by + (bh - h) / 2;
        }

        // Обрезает превью по видимой области: место на экране (d…) и соответствующая часть окна-источника (s…).
        // false — превью целиком за краем.
        public static bool Clip(ref double dl, ref double dt, ref double dr, ref double db, ref double sl, ref double st, ref double sr, ref double sb,
                                double cl, double ct, double cr, double cb)
        {
            double w = dr - dl, h = db - dt;
            if (w <= 0 || h <= 0) return false;
            double nl = Math.Max(dl, cl), nt = Math.Max(dt, ct), nr = Math.Min(dr, cr), nb = Math.Min(db, cb);
            if (nr - nl < 1 || nb - nt < 1) return false;
            double kx = (sr - sl) / w, ky = (sb - st) / h;
            double l2 = sl + (nl - dl) * kx, t2 = st + (nt - dt) * ky, r2 = sr - (dr - nr) * kx, b2 = sb - (db - nb) * ky;
            dl = nl; dt = nt; dr = nr; db = nb;
            sl = l2; st = t2; sr = r2; sb = b2;
            return true;
        }

        // Пружина: x тянется к target. Шаги мелкие, чтобы редкий кадр её не разболтал.
        public static void Spring(ref double x, ref double v, double target, double stiffness, double damping, double dt)
        {
            dt = Math.Max(0, Math.Min(dt, 0.05));
            int n = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                v += (stiffness * (target - x) - damping * v) * h;
                x += v * h;
            }
        }
    }

    // Значок приложения из Магазина лежит не в файле программы, а рядом, картинкой; какой именно — написано в AppxManifest.xml
    static class StoreLogo
    {
        // Имя картинки из манифеста: «Assets\Logo.png»
        public static string FromManifest(string xml)
        {
            if (xml == null) return null;
            var m = System.Text.RegularExpressions.Regex.Match(xml, "Square44x44Logo\\s*=\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Из файлов «Logo.scale-200.png», «Logo.targetsize-48_altform-unplated.png»… выбирает лучший для значка
        public static string Pick(string logo, string[] files)
        {
            if (logo == null || files == null) return null;
            string stem = System.IO.Path.GetFileNameWithoutExtension(logo).ToLowerInvariant();
            string best = null;
            int bestScore = -1;
            foreach (string f in files)
            {
                string name = System.IO.Path.GetFileName(f).ToLowerInvariant();
                if (!name.StartsWith(stem + ".") || name.Contains("contrast-")) continue;
                int score = 1;
                var size = System.Text.RegularExpressions.Regex.Match(name, "targetsize-(\\d+)");
                var zoom = System.Text.RegularExpressions.Regex.Match(name, "scale-(\\d+)");
                if (size.Success) { int s = int.Parse(size.Groups[1].Value); score = s >= 48 && s <= 96 ? 300 - Math.Abs(64 - s) : s > 96 ? 150 : s; }
                else if (zoom.Success) score = 100 + Math.Min(99, int.Parse(zoom.Groups[1].Value) / 4);
                if (name.Contains("unplated")) score += 400;
                if (score > bestScore) { bestScore = score; best = f; }
            }
            return best;
        }
    }
}
