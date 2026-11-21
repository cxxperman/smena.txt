// smena.txt. Model.cs — всё, что решается без экрана и без клавиатуры: какие окна показывать и в каком порядке,
// куда сдвигается выбор, что значит каждая клавиша, когда Alt+Tab остаётся системным, сколько рядов и что видно,
// нужен ли свой значок в трее, что открывает команда «show», где стоит каждая карточка в «Стопке 3D».
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
        public bool Game;                   // окно игры (Logic.GameWindow): его не захватываем, не открываем и не дожимаем
        public string Proc;                 // имя процесса из списка процессов (без .exe, строчными); может быть пустым
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

        // Лежит ли файл file в папке folder или глубже (пути полные; регистр букв и вид черты не важны).
        // Нужно, чтобы пробная сборка из временной папки не встала настоящим перехватом клавиатуры.
        public static bool InFolder(string file, string folder)
        {
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(folder)) return false;
            string f = file.Trim().Replace('/', '\\'), d = folder.Trim().Replace('/', '\\');
            if (d.Length == 0) return false;
            if (!d.EndsWith("\\")) d += "\\";
            return f.Length > d.Length && f.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }

        // Обычный запуск (без ключей проверки) из временной папки запрещён, если не разрешён переменной SMENA_ALLOW_TEMP=1
        public static bool RefuseTempRun(bool inTemp, string allow)
        {
            return inTemp && (allow == null || allow.Trim() != "1");
        }

        // ───────── Игры ─────────

        // Окно — игра: настройка «Не вмешиваться в играх» включена, а окно на весь экран (свёрнутое — развернётся на весь
        // экран без рамки) или его процесс — в списке игр (Games.IsGame). Такое окно не захватывается, его процесс
        // не открывается, а переключение к нему и от него — только обычной просьбой к Windows (Gentle).
        public static bool GameWindow(bool gamesOn, bool fullscreen, string proc, IList<string> extra)
        {
            return gamesOn && (fullscreen || Games.IsGame(proc, extra));
        }

        // Свёрнутое окно на весь экран: развернётся ровно в размер монитора, без заголовка и рамки (как у IsFullscreen).
        // Сравнивается только размер: место свёрнутого окна Windows хранит в координатах рабочей области, а не экрана.
        public static bool FullscreenMinimized(int normalW, int normalH, int monW, int monH, bool caption, bool thickFrame)
        {
            return !caption && !thickFrame && normalW > 0 && normalH > 0 && normalW == monW && normalH == monH;
        }

        // Объёмный вид при этом показе: выбран, и среди окон нет игры, которую видно. В объёмный вид попадает кадр монитора,
        // а в нём — картинка игры; поэтому при видимом окне игры — сетка. Свёрнутая игра не мешает (у неё плашка).
        public static bool UseFlip(bool want3D, IList<WinItem> items)
        {
            if (!want3D) return false;
            if (items != null) foreach (WinItem w in items) if (w != null && w.Game && !w.Minimized) return false;
            return true;
        }

        // Можно ли заводить для окна захват, миниатюру DWM, снимок свёрнутого и спрашивать значок у окна или у процесса
        public static bool MayTouch(WinItem w) { return w != null && !w.Game; }

        // Переключение — только обычной просьбой к Windows (без AttachThreadInput и SendInput): smena.txt в стороне,
        // впереди игра или переключаемся в игру
        public static bool Gentle(bool aside, bool foregroundGame, bool targetGame)
        {
            return aside || foregroundGame || targetGame;
        }

        // Работают ли сочетания клавиш (для страницы «Клавиши» в core.txt)
        public static bool KeysOn(bool replace, bool paused, bool aside) { return replace && !paused && !aside; }

        // Строка о том, почему smena.txt в стороне, — для окна «О программе», подсказки в трее и ответа на «show»
        public static string AsideText(AsideReason r)
        {
            if (r == AsideReason.AntiCheat) return "Идёт игра с античитом";
            if (r == AsideReason.Fullscreen) return "Впереди игра или окно на весь экран";
            if (r == AsideReason.Core) return "Идёт игра";
            return "";
        }
    }

    // Имена процессов игр и античитов. Имя — как в списке процессов Windows; .exe, путь и регистр не важны.
    // Запись со звёздочкой в конце — все имена, которые так начинаются.
    static class Games
    {
        // «Идёт игра под античитом»: служба античита, которая запущена, только пока идёт защищённая игра (её запускает
        // сама игра и останавливает после неё), или процесс самой такой игры. Пока любой из них запущен, smena.txt
        // отходит в сторону целиком (AsideWatch). Нарочно нет:
        //  — faceit: так зовутся и служба FACEIT AC, и приложение FACEIT, которое работает весь день; матч ловится по
        //    клиенту античита faceitclient;
        //  — vgc и vgtray (Riot Vanguard): работают с загрузки Windows; вместо них — процесс самой VALORANT;
        //  — dota2 и cs2: VAC — не античит ядра (их окна всё равно не трогаются — Untouched).
        public static readonly string[] Shielded =
        {
            "EasyAntiCheat", "EasyAntiCheat_EOS",            // Easy Anti-Cheat: служба (прежняя и Epic Online Services)
            "BEService", "BEService_x64", "BEService_*",     // BattlEye: служба; у некоторых игр своя — BEService_<игра>
            "faceitclient",                                  // FACEIT Anti-Cheat: клиент античита (не приложение FACEIT)
            "FortniteClient-Win64-Shipping", "FortniteClient-Win64-Shipping_EAC_EOS", "FortniteClient-Win64-Shipping_BE", "FortniteLauncher",
            "VALORANT-Win64-Shipping", "VALORANT"
        };

        // Игры, чьи окна не трогаются (при включённой настройке): ни захвата, ни миниатюры, ни открытия процесса.
        // Отходить в сторону из-за них не нужно — только не касаться их окон.
        public static readonly string[] Untouched = { "dota2", "cs2", "csgo", "aces", "RobloxPlayerBeta" };

        public const int MaxExtra = 64;   // столько своих имён (asideProcesses в settings.json) берётся, не больше

        // Имя процесса для сравнения: без пути, кавычек, пробелов по краям и .exe, строчными
        public static string Norm(string name)
        {
            if (name == null) return "";
            string n = name.Trim().Trim('"').Trim();
            int slash = Math.Max(n.LastIndexOf('\\'), n.LastIndexOf('/'));
            if (slash >= 0) n = n.Substring(slash + 1);
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return n.Trim().ToLowerInvariant();
        }

        // Подходит ли имя процесса под запись списка
        public static bool Match(string name, string entry)
        {
            string n = Norm(name), e = Norm(entry);
            if (n.Length == 0 || e.Length == 0) return false;
            if (e.EndsWith("*")) { e = e.Substring(0, e.Length - 1); return e.Length > 0 && n.StartsWith(e, StringComparison.Ordinal); }
            return n == e;
        }

        public static bool InList(string name, IList<string> list)
        {
            if (list != null) foreach (string e in list) if (Match(name, e)) return true;
            return false;
        }

        // Идёт ли игра под античитом: первое запущенное имя из Shielded или своих (extra); null — нет такого
        public static string Running(IEnumerable<string> running, IList<string> extra)
        {
            if (running == null) return null;
            foreach (string p in running) if (InList(p, Shielded) || InList(p, extra)) return Norm(p);
            return null;
        }

        // Процесс игры, чьих окон не касаемся: из любого списка
        public static bool IsGame(string proc, IList<string> extra)
        {
            return InList(proc, Shielded) || InList(proc, Untouched) || InList(proc, extra);
        }

        // Свои имена из settings.json (asideProcesses): массив строк; всё остальное пропускается
        public static string[] Extra(object value)
        {
            var list = new List<string>();
            var all = value as System.Collections.IEnumerable;
            if (all == null || value is string) return list.ToArray();
            foreach (object o in all)
            {
                string s = o as string;
                if (s == null || s.Length > 260) continue;
                string n = Norm(s);
                if (n.Length == 0 || n == "*" || list.Contains(n)) continue;
                list.Add(n);
                if (list.Count >= MaxExtra) break;
            }
            return list.ToArray();
        }
    }

    // Почему smena.txt в стороне
    enum AsideReason { None, AntiCheat, Fullscreen, Core }

    // Когда smena.txt «отходит в сторону» — полностью бездействует: настройка «Не вмешиваться в играх» включена и идёт
    // игра под античитом, впереди окно на весь экран или core.txt сказал, что идёт игра. Входит сразу, как увидит повод;
    // выходит, только когда поводов нет QuietMs подряд. Настройку выключили — выходит сразу.
    class AsideWatch
    {
        // Между матчами, на экране загрузки и при сворачивании игры повод на миг пропадает и тут же возвращается.
        // 8 с — дольше таких перерывов (и двух опросов процессов по 3 с), но не так долго, чтобы после игры ждать.
        public const double QuietMs = 8000;

        bool on;
        double quietSince = -1;
        AsideReason reason = AsideReason.None;
        string what = "";

        public bool On { get { return on; } }
        public AsideReason Reason { get { return reason; } }
        public string What { get { return what; } }   // имя процесса античита или игры, если известно

        // now — мс. antiCheat — запущенный процесс из Games.Running (null — нет). true — smena.txt в стороне.
        public bool Update(bool games, string antiCheat, bool fullscreen, bool coreGame, double now)
        {
            if (!games) { on = false; reason = AsideReason.None; what = ""; quietSince = -1; return false; }
            AsideReason r = antiCheat != null ? AsideReason.AntiCheat : fullscreen ? AsideReason.Fullscreen : coreGame ? AsideReason.Core : AsideReason.None;
            if (r != AsideReason.None)
            {
                on = true; reason = r; what = antiCheat ?? ""; quietSince = -1;
                return true;
            }
            if (!on) return false;
            if (quietSince < 0) quietSince = now;
            if (now - quietSince >= QuietMs) { on = false; reason = AsideReason.None; what = ""; quietSince = -1; }
            return on;
        }
    }

    // Что делает сторож потока окна (StallGuard в Stall.cs) после очередной проверки
    enum StallStep { None, Trip, Kill }

    // Сторож потока окна — чистая логика, без потоков и часов (их даёт StallGuard), поэтому её проверяет --selftest.
    // Пока идёт показ (Arm), сторож раз в CheckMs спрашивает поток окна «жив?» — одним сообщением; следующее уходит
    // только после ответа. Ответа нет дольше TripMs — Trip: окна переключателя прячутся, клавиши идут мимо нас.
    // Нет и дольше KillMs — Kill (один раз): лучше системный Alt+Tab, чем мёртвый перехват. Ответ после Trip значит,
    // что поток очнулся: Answer вернёт true, и поток окна сбросит показ в исходное состояние.
    class StallWatch
    {
        public const int CheckMs = 100;       // как часто сторож смотрит
        // Порог «поток окна завис». Не оформление, а страховка, поэтому не в Tokens. Худшие замеренные подвисания
        // потока окна при показе — сотни мс (--flipprobe --real); 1,2 с — заметно дольше их, но ещё до того, как
        // застывший экран начнёт казаться сломанным.
        public const double TripMs = 1200;
        public const double KillMs = 10000;   // столько без ответа — программа выходит сама

        bool armed, waiting, tripped, killed;
        double askedAt;

        public bool Armed { get { return armed; } }
        public bool Waiting { get { return waiting; } }
        public bool Tripped { get { return tripped; } }

        // Сторожить нечего: показа нет и ничего не сработало
        public bool Idle { get { return !armed && !tripped; } }

        public void Arm() { armed = true; }
        public void Disarm() { armed = false; }

        // Сколько поток окна молчит в ответ на последний вопрос
        public double Silent(double now) { return waiting ? Math.Max(0, now - askedAt) : 0; }

        // Проверка в момент now. ask — пора задать потоку окна новый вопрос.
        public StallStep Tick(double now, out bool ask)
        {
            ask = false;
            if (Idle) return StallStep.None;
            if (!waiting) { waiting = true; askedAt = now; ask = true; return StallStep.None; }
            double silent = now - askedAt;
            if (!tripped)
            {
                if (silent < TripMs) return StallStep.None;
                tripped = true;
                return StallStep.Trip;
            }
            if (killed || silent < KillMs) return StallStep.None;
            killed = true;
            return StallStep.Kill;
        }

        // Поток окна ответил. true — до этого сработал Trip: показ надо сбросить.
        public bool Answer()
        {
            waiting = false;
            bool was = tripped;
            tripped = false; killed = false;
            return was;
        }
    }

    // Нужен ли свой значок в трее. Пока core.txt на связи и держит один значок на всех (core.family.tray) — нет;
    // связь пропала или core.txt значок не держит — да, сразу. Сразу после запуска, если core.txt установлен, значок
    // ждёт его ответа не дольше Wait, чтобы не мигнуть при входе в Windows; не ответил — значок появляется.
    // Без входа приложение не остаётся никогда.
    class OwnTray
    {
        public static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);
        bool waiting;

        public OwnTray(bool coreInstalled) { waiting = coreInstalled; }

        // Ещё ждём первого ответа core.txt
        public bool Waiting { get { return waiting; } }

        // Время вышло, а core.txt так и не ответил
        public void Expire() { waiting = false; }

        // coreKnown — core.txt на связи и сказал, что у семьи (core.family); hideOwn — значок он держит за всех
        public bool Visible(bool coreKnown, bool hideOwn)
        {
            if (coreKnown) waiting = false;   // ответ пришёл: ждать больше нечего
            return !hideOwn && !waiting;
        }
    }

    // Что открывает команда «show»
    enum ShowTarget { Switcher, About, CorePage }

    // Без страницы «show» один раз показывает переключатель «до щелчка» — попробовать без клавиатуры, как было всегда.
    // Со страницей просят окно (core.txt со страницы «Клавиши» шлёт { page: "keys" }), а не переключатель во весь экран,
    // где Enter переводит в выбранное окно, а Delete его закрывает. «keys» ведёт на страницу smena.txt в core.txt:
    // оба сочетания включаются и выключаются там. Нет core.txt или страница другая — окно «О программе».
    static class ShowRoute
    {
        // coreShows — core.txt на связи и понимает «show»
        public static ShowTarget For(Dictionary<string, object> args, bool coreShows)
        {
            object page;
            if (args == null || !args.TryGetValue("page", out page) || page == null) return ShowTarget.Switcher;
            return page as string == "keys" && coreShows ? ShowTarget.CorePage : ShowTarget.About;
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
        bool passing;                            // поток окна завис (Pass): все клавиши идут мимо нас, до Resume
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

        // Поток окна не отвечает (сторож, StallGuard). Перехватчик живёт в своём потоке и без этого продолжал бы глотать
        // Alt+Tab, отдавая действия зависшему окну, — и Alt+Tab не работал бы совсем. Теперь всё идёт мимо нас,
        // начатое переключение закончено. Зовётся из потока сторожа.
        public void Pass() { lock (gate) { passing = true; active = false; sticky = false; Array.Clear(eaten, 0, eaten.Length); } }

        // Поток окна очнулся и сбросил показ: клавиши снова наши
        public void Resume() { lock (gate) passing = false; }

        public bool Passing { get { lock (gate) return passing; } }

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
                if (passing) return false;
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

    // Положение карточки в объёмной сцене. Начало — центр экрана, x вправо, y вверх, z — на зрителя; единица — точка
    // экрана (DIP) на плоскости z = 0. Повороты в градусах: Roll — в плоскости экрана (вокруг z), Angle — вокруг вертикали (y),
    // Pitch — вокруг горизонтали (x). Порядок всегда такой: сначала Roll, затем Angle, затем Pitch, затем сдвиг (так же в Flip.cs
    // и в Flip.Corner). W и H — размер карточки.
    struct Pose
    {
        public double X, Y, Z, Angle, Pitch, Roll, Opacity, W, H;
    }

    // Объёмные раскладки переключателя. Основная — «Стопка» (flip), как Flip 3D в Windows Vista: окна стоят одно за другим
    // и уходят вглубь влево-вверх, все повёрнуты на один угол; выбранное — впереди. Остальные — в Place.
    // Здесь только расчёт — его проверяет --selftest.
    static class Flip
    {
        // Объёмные виды по порядку, как в настройках (сетка — не здесь)
        public static readonly string[] Views = { "flip", "cascade", "cover", "carousel", "fan", "panorama", "helix", "rolodex", "tunnel" };

        public const double Fov = 40;                     // угол зрения камеры по ширине экрана, в градусах
        public const double Angle = -32;                  // поворот карточек: левый край дальше от зрителя
        public const double FrontW = 0.55, FrontH = 0.60; // передняя карточка вписана в такую долю экрана
        public const double ShiftX = 0.06, ShiftY = -0.02; // передняя карточка чуть правее и выше центра, в долях экрана
        public const double StepX = 0.08, StepY = 0.085, StepZ = 0.2;   // шаг стопки вглубь: по x и z — в долях ширины, по y — высоты
        public const double Seen = 8;                     // видно не больше стольких карточек, дальние гаснут
        public const int MaxLive = 24;                    // живых картинок одновременно не больше стольких
        public const double DefaultAspect = 1.6;          // пропорции карточки, пока о размере окна ничего не известно

        static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }

        // Расстояние от камеры до плоскости z = 0, при котором эта плоскость совпадает с экраном шириной w
        public static double Distance(double w) { return w / 2 / Math.Tan(Fov * Math.PI / 360); }

        // Где на экране (в точках, от левого верхнего угла) окажется точка сцены
        public static void Project(double x, double y, double z, double w, double h, out double sx, out double sy)
        {
            double d = Distance(w), k = d / Math.Max(1e-6, d - z);
            sx = w / 2 + x * k;
            sy = h / 2 - y * k;
        }

        // Глубина карточки i: 0 — впереди, 1, 2… — дальше в стопке. pos — где сейчас выбор (едет пружиной).
        // По кругу: всегда в [−0.5; n−0.5), так что уехавшая вперёд карточка проявляется в конце стопки.
        public static double Depth(int i, double pos, int n)
        {
            if (n <= 0) return 0;
            double d = (i - pos + 0.5) % n;
            if (d < 0) d += n;
            return d - 0.5;
        }

        // Куда ехать выбору, чтобы прийти к окну target кратчайшим путём по кругу: target плюс сколько-то полных кругов
        public static double Nearest(double pos, int target, int n)
        {
            if (n <= 0) return target;
            return target + n * Math.Round((pos - target) / n);
        }

        // Непрозрачность на глубине d: впереди целиком; уезжая на зрителя — гаснет; дальние гаснут к пределу видимости;
        // у конца круга карточка проявляется из ничего
        public static double Fade(double d, int n)
        {
            // Пружина выбора чуть перелетает: на десятую шага вперёд карточка ещё целиком непрозрачна
            double o = d < -0.1 ? Clamp((d + 0.5) / 0.4) : 1;
            o = Math.Min(o, Clamp((Seen - 0.5 - d) / 2));
            return Math.Min(o, Clamp((n - 0.5 - d) * 2));
        }

        // Размер карточки впереди: окно с такими пропорциями, вписанное в долю экрана w×h
        public static void Front(double aspect, double w, double h, out double cw, out double ch)
        {
            Front("flip", aspect, w, h, out cw, out ch);
        }

        // То же для любой раскладки: у каждой своя доля экрана под выбранное окно
        public static void Front(string view, double aspect, double w, double h, out double cw, out double ch)
        {
            if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) aspect = DefaultAspect;
            double fw, fh;
            Share(view, out fw, out fh);
            double bw = w * fw, bh = h * fh;
            cw = Math.Min(bw, bh * aspect);
            ch = cw / aspect;
        }

        // Доля экрана (по ширине и высоте), в которую вписано выбранное окно
        static void Share(string view, out double fw, out double fh)
        {
            switch (view)
            {
                case "cascade": fw = 0.50; fh = 0.50; break;
                case "cover": fw = 0.42; fh = 0.54; break;
                case "carousel": fw = 0.36; fh = 0.46; break;
                case "fan": fw = 0.36; fh = 0.46; break;
                case "panorama": fw = 0.34; fh = 0.50; break;
                case "helix": fw = 0.30; fh = 0.34; break;
                case "rolodex": fw = 0.46; fh = 0.32; break;
                case "tunnel": fw = 0.42; fh = 0.46; break;
                default: fw = FrontW; fh = FrontH; break;
            }
        }

        // Раскладка симметрична: соседи по обе стороны от выбранного (иначе — колода, уходящая вглубь)
        public static bool Symmetric(string view)
        {
            return view == "cover" || view == "carousel" || view == "fan" || view == "panorama" || view == "helix" || view == "rolodex";
        }

        // Середина круга для расстояния со знаком: при чётном числе окон шов сдвинут на полшага, чтобы не попасть на окно
        static double Middle(int n) { return n % 2 == 0 ? 0.5 : 0; }

        // Расстояние со знаком от выбранного по кругу: из глубины d ∈ [−0.5; n−0.5) — в (c − n/2; c + n/2], c = Middle(n).
        // Отрицательное — окна «перед» выбранным, положительное — «после».
        public static double Signed(double d, int n)
        {
            if (n <= 0) return 0;
            return d > Middle(n) + n / 2.0 ? d - n : d;
        }

        // Шов круга: там карточка перескакивает с одного края на другой — на шве она прозрачна, за полшага от него — целиком видна
        public static double Seam(double s, int n)
        {
            if (n <= 0) return 0;
            return Clamp((n / 2.0 - Math.Abs(s - Middle(n))) * 2);
        }

        // Порядок отрисовки при равной глубине: больше — выше (ближе к выбранному)
        public static double Order(string view, double d, int n)
        {
            return Symmetric(view) ? -Math.Abs(Signed(d, n)) : -d;
        }

        static double Rad(double deg) { return deg * Math.PI / 180; }
        static double Deg(double rad) { return rad * 180 / Math.PI; }

        // Место карточки с глубиной d (Depth) в раскладке view; cw×ch — её размер впереди, w×h — размер сцены.
        // Шаг соседей мерится по карточке обычных пропорций (uw×uh), чтобы не зависеть от размера самого окна.
        public static Pose Place(string view, double d, int n, double cw, double ch, double w, double h)
        {
            double uw, uh;
            Front(view, DefaultAspect, w, h, out uw, out uh);
            double s = Signed(d, n), a = Math.Abs(s), sg = s < 0 ? -1 : 1, t = Math.Min(a, 1), seam = Seam(s, n);
            var p = new Pose { W = cw, H = ch, Opacity = 1 };
            switch (view)
            {
                case "cascade":
                {
                    // Колода наклонена назад и уходит вверх и вглубь; выбранное — впереди внизу
                    p.Y = (-0.10 + d * 0.085) * h;
                    p.Z = -d * 0.16 * w;
                    p.Pitch = -24;
                    p.Opacity = Fade(d, n);
                    return p;
                }
                case "cover":
                {
                    // Cover Flow: выбранное лицом к зрителю, остальные плотно по бокам, повёрнуты к центру
                    double off = t * 0.72 * uw + Math.Max(0, a - 1) * 0.15 * uw;
                    p.X = sg * off;
                    p.Y = 0.01 * h;
                    p.Z = -(t * 0.9 + Math.Max(0, a - 1) * 0.03) * uw;
                    p.Angle = -sg * t * 65;
                    p.Opacity = Math.Min(Clamp((5.5 - a) / 1.5), seam);
                    return p;
                }
                case "carousel":
                {
                    // Кольцо лицом наружу, взгляд чуть сверху (всё кольцо наклонено на tilt); выбранное — ближе всех
                    int slots = Math.Max(6, Math.Min(n, 10));
                    double step = 360.0 / slots, phi = s * step, tilt = 10;
                    double r = uw * 1.1 / (2 * Math.Sin(Rad(step / 2)));
                    double z = r * Math.Cos(Rad(phi)) - r;
                    p.X = r * Math.Sin(Rad(phi));
                    p.Y = -0.03 * h - z * Math.Sin(Rad(tilt));
                    p.Z = z * Math.Cos(Rad(tilt));
                    p.Angle = phi;
                    p.Pitch = tilt;
                    // Обратную сторону WPF не рисует: карточка гаснет, пока поворачивается к зрителю ребром, а не пропадает скачком
                    // Дальше полукруга не видно: иначе при большом числе окон кольцо пошло бы на второй круг поверх выбранного
                    p.Opacity = Math.Min(Math.Min(Clamp((Facing(Math.Sin(Rad(phi)), 0, Math.Cos(Rad(phi)), p.X, 0, p.Z, w) - 0.05) / 0.25), Clamp((170 - Math.Abs(phi)) / 30)), seam);
                    return p;
                }
                case "helix":
                {
                    // Спираль: кольцо, как в карусели, но каждое следующее окно выше; карточки повёрнуты к зрителю сильнее,
                    // чтобы задние витки было видно
                    double step = 45, phi = s * step, tilt = 8;
                    double r = uw * 0.95 / (2 * Math.Sin(Rad(step / 2)));
                    double z = r * Math.Cos(Rad(phi)) - r;
                    p.X = r * Math.Sin(Rad(phi));
                    p.Y = 0.0 * h - z * Math.Sin(Rad(tilt)) + s * 0.085 * h;
                    p.Z = z * Math.Cos(Rad(tilt));
                    p.Angle = phi * 0.3;
                    p.Pitch = tilt;
                    p.Opacity = Math.Min(Clamp((4.5 - a) / 1.5), seam);
                    return p;
                }
                case "fan":
                {
                    // Веер, как карты в руке: поворот вокруг точки далеко под экраном; выбранное прямо, чуть выше и крупнее
                    double pivot = 2.2 * h, step = Deg(0.33 * uw / pivot), b = s * step, lift = (1 - t) * 0.03 * h, grow = 1 + (1 - t) * 0.06;
                    double r = pivot + lift;
                    p.X = r * Math.Sin(Rad(b));
                    p.Y = 0.01 * h + r * Math.Cos(Rad(b)) - pivot;
                    p.Z = -a * 0.02 * uw;
                    p.Roll = -b;
                    p.W = cw * grow; p.H = ch * grow;
                    p.Opacity = Math.Min(Clamp((4.5 - a) / 1.5), seam);
                    return p;
                }
                case "panorama":
                {
                    // Вогнутая стена вокруг зрителя: дуга с центром между экраном и зрителем, соседи стоят на ней и повёрнуты
                    // к центру; выбранное выходит из стены вперёд и потому крупнее
                    double r = 0.95 * Distance(w), center = r - 0.3 * w;
                    double g = sg * (t * 1.2 + Math.Max(0, a - 1) * 1.02) * uw / r;
                    p.X = r * Math.Sin(g);
                    p.Y = 0.01 * h;
                    p.Z = center - r * Math.Cos(g) + (1 - t) * (r - center);
                    p.Angle = -Deg(g);
                    p.Opacity = Math.Min(Clamp((4.5 - a) / 1.5), seam);
                    return p;
                }
                case "rolodex":
                {
                    // Барабан с горизонтальной осью: следующие уходят через верх назад, предыдущие — вниз
                    double step = 50, psi = s * step, r = uh * 1.0 / (2 * Math.Sin(Rad(step / 2)));
                    p.Y = 0.03 * h + r * Math.Sin(Rad(psi));
                    p.Z = r * (Math.Cos(Rad(psi)) - 1);
                    p.Pitch = -psi;
                    // Высота — от оси барабана, чтобы верх и низ гасли одинаково; запас 0.25 покрывает сдвиг оси от центра экрана
                    p.Opacity = Math.Min(Math.Min(Clamp((Facing(0, Math.Sin(Rad(psi)), Math.Cos(Rad(psi)), p.X, p.Y - 0.03 * h, p.Z, w) - 0.05) / 0.25), Clamp((170 - Math.Abs(psi)) / 30)), seam);
                    return p;
                }
                case "tunnel":
                {
                    // Закрученный туннель: окна уходят вглубь по центру, каждое следующее повёрнуто в плоскости экрана чуть больше,
                    // и их углы выглядывают из-за выбранного спиралью
                    p.Y = 0.02 * h;
                    p.Z = -d * 0.09 * w;
                    p.Roll = d * 9;
                    p.Opacity = Fade(d, n);
                    return p;
                }
                default:
                    return At(d, n, cw, ch, w, h);
            }
        }

        // Насколько карточка с лицевой стороной (nx, ny, nz) в точке (x, y, z) повёрнута к зрителю: 1 — прямо на него, 0 — ребром,
        // меньше нуля — спиной
        static double Facing(double nx, double ny, double nz, double x, double y, double z, double w)
        {
            double cx = -x, cy = -y, cz = Distance(w) - z, len = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            return len <= 0 ? 0 : (nx * cx + ny * cy + nz * cz) / len;
        }

        // Где на экране окажется угол карточки (u, v от −0.5 до 0.5 по её ширине и высоте), с теми же поворотами, что в Flip.cs;
        // z — глубина угла в сцене
        public static void Corner(Pose p, double u, double v, double w, double h, out double sx, out double sy, out double z)
        {
            double x = u * p.W, y = v * p.H, zz = 0, c, s, k;
            c = Math.Cos(Rad(p.Roll)); s = Math.Sin(Rad(p.Roll));
            k = x * c - y * s; y = x * s + y * c; x = k;
            c = Math.Cos(Rad(p.Angle)); s = Math.Sin(Rad(p.Angle));
            k = x * c + zz * s; zz = -x * s + zz * c; x = k;
            c = Math.Cos(Rad(p.Pitch)); s = Math.Sin(Rad(p.Pitch));
            k = y * c - zz * s; zz = y * s + zz * c; y = k;
            z = zz + p.Z;
            Project(x + p.X, y + p.Y, z, w, h, out sx, out sy);
        }

        // Прямоугольник на экране, в который попадает карточка целиком
        public static void Screen(Pose p, double w, double h, out double l, out double t, out double r, out double b)
        {
            l = t = double.MaxValue; r = b = double.MinValue;
            for (int i = 0; i < 4; i++)
            {
                double sx, sy, z;
                Corner(p, i % 2 == 0 ? -0.5 : 0.5, i < 2 ? -0.5 : 0.5, w, h, out sx, out sy, out z);
                l = Math.Min(l, sx); r = Math.Max(r, sx); t = Math.Min(t, sy); b = Math.Max(b, sy);
            }
        }

        // Место карточки на глубине d; cw×ch — её размер впереди, w×h — размер сцены
        public static Pose At(double d, int n, double cw, double ch, double w, double h)
        {
            return new Pose
            {
                X = (ShiftX - d * StepX) * w, Y = (ShiftY + d * StepY) * h, Z = -d * StepZ * w,
                Angle = Angle, Opacity = Fade(d, n), W = cw, H = ch
            };
        }

        // «Домашнее» место карточки: плоско, ровно поверх настоящего окна. Прямоугольник окна и монитора — в точках
        // экрана (физических пикселях), scale — масштаб экрана; w×h — размер сцены в DIP.
        public static Pose Home(double left, double top, double right, double bottom, double monLeft, double monTop, double scale, double w, double h)
        {
            if (scale <= 0) scale = 1;
            double l = (left - monLeft) / scale, t = (top - monTop) / scale, r = (right - monLeft) / scale, b = (bottom - monTop) / scale;
            return new Pose { X = (l + r) / 2 - w / 2, Y = h / 2 - (t + b) / 2, Z = 0, Angle = 0, Opacity = 1, W = r - l, H = b - t };
        }

        // Дома карточки не лежат в одной плоскости: каждая ниже по экрану стоит на LiftStep ширины экрана глубже предыдущей.
        // В одной плоскости первые же кадры поворота резали бы их друг о друга (глубина у них почти одна, а край
        // поворачивающейся карточки уходит вглубь и выходит вперёд). Чтобы на экране ничего не изменилось, поднятая
        // ближе к зрителю карточка во столько же раз меньше (Lift) — её проекция совпадает с окном точка в точку.
        public const double LiftStep = 0.02;

        // Та же поза на глубине z (ближе к зрителю — больше z), уменьшенная так, что на экране — ровно то же место
        public static Pose Lift(Pose p, double z, double w)
        {
            double d = Distance(w), k = (d - z) / d;
            p.X *= k; p.Y *= k; p.W *= k; p.H *= k;
            p.Z = z;
            return p;
        }

        // ── Появление и уход: по времени, а не по числу кадров ──
        // Длительность одна и та же на 60 и на 240 Гц. Подвисший кадр засчитывается не длиннее MaxDt: один долгий кадр
        // не съедает движение целиком, оно доигрывается следующими кадрами.

        public const double MaxDt = 1.0 / 30;            // самый длинный кадр, который идёт в зачёт движения, в секундах
        // Появление целиком: 333 + 167 = 500 мс. Все карточки садятся на места к этому сроку; фон перетекает за то же время.
        public const int OpenMs = Tokens.Motion.SlowMs + Tokens.Motion.FastMs;
        // Сдвиг старта по глубине: не ступеньками, а плавно растёт с удалённостью и не больше LagMs (2 × 30 = 60 мс).
        // Карточка, стартовавшая позже, летит настолько же короче: группа садится разом и читается одним движением.
        public const int LagMs = 2 * Tokens.Motion.StaggerMs;
        // Уход выбранного окна на место (и всех — при отмене): 333 + 83 = 416 мс. На 310 мс выбранное окно вставало резко.
        public const int LeaveMs = Tokens.Motion.SlowMs + Tokens.Motion.PressMs;
        // Сразу после раскрытия окна DWM часто задерживает следующий кадр на 17–40 мс (SMENA_TRACE). Это время сцена стоит
        // неподвижно — она неотличима от экрана, — и движение начинается с первого кадра, пришедшего вовремя
        // (промежуток не длиннее OnTime обычных шагов), но не позже HoldMs после первого видимого кадра.
        public const double HoldMs = 60;
        public const double OnTime = 1.5;
        // «Меньше движения»: без полёта, короткое проявление и угасание
        public const int FadeMs = Tokens.Motion.FastMs;
        public const double Recede = 0.06;               // при уходе невыбранные карточки отъезжают вглубь на столько, в долях ширины экрана

        // Сколько миллисекунд движения прошло после кадра длиной dt секунд
        public static double Tick(double elapsedMs, double dt)
        {
            if (double.IsNaN(dt) || dt < 0) dt = 0;
            return elapsedMs + Math.Min(dt, MaxDt) * 1000;
        }

        // Доля пройденного у движения длиной ms, начавшегося с задержкой delay: от 0 до 1
        public static double Share(double elapsedMs, double delayMs, double ms)
        {
            if (ms <= 0) return elapsedMs >= delayMs ? 1 : 0;
            return Clamp((elapsedMs - delayMs) / ms);
        }

        // Задержка старта карточки на глубине d (Depth): первая в раскладке — сразу, следующие — чуть позже, плавно
        // (d / (d + 1) от LagMs: 30, 40, 45 мс… и не больше LagMs), без ступенек
        public static double Delay(string view, double d, int n)
        {
            double step = Symmetric(view) ? Math.Abs(Signed(d, n)) : Math.Max(0, d);
            return LagMs * step / (step + 1);
        }

        // Сколько летит карточка, стартовавшая с задержкой delay: садится к концу появления вместе со всеми
        public static double Flight(double delayMs)
        {
            return Math.Max(Tokens.Motion.FastMs, OpenMs - Math.Max(0, delayMs));
        }

        // Пора ли начинать появление: прошло sinceMs от первого видимого кадра, этот кадр пришёл через gapMs после прошлого,
        // обычный шаг — typicalMs. Первый видимый кадр (sinceMs = 0) — всегда «нет».
        public static bool Go(double sinceMs, double gapMs, double typicalMs)
        {
            if (double.IsNaN(sinceMs) || sinceMs <= 0) return false;
            if (sinceMs >= HoldMs) return true;
            return gapMs > 0 && gapMs <= OnTime * Math.Max(1000 * Pace.MinStep, typicalMs);
        }

        // Кривая движения — cubic-bezier(0.25, 0, 0.1, 1): с места без рывка (скорость в начале — ноль), разгон за первые
        // ~18% времени, дальше долгое мягкое оседание, в конце скорость снова ноль. Монотонна, без перелёта.
        public const double GlideX1 = 0.25, GlideX2 = 0.1;

        public static double Glide(double t)
        {
            t = Clamp(t);
            if (t <= 0 || t >= 1) return t;
            // x(s) монотонна по s: ищем s, при котором x(s) = t, делением пополам — ровно и без сюрпризов
            double lo = 0, hi = 1, s = t;
            for (int i = 0; i < 40; i++)
            {
                s = (lo + hi) / 2;
                double u = 1 - s, x = 3 * u * u * s * GlideX1 + 3 * u * s * s * GlideX2 + s * s * s;
                if (x < t) lo = s; else hi = s;
            }
            s = (lo + hi) / 2;
            return 3 * (1 - s) * s * s + s * s * s;
        }

        // Шов круга на глубине d: там карточка перескакивает с одного конца раскладки на другой и потому должна быть
        // невидима. Колода (flip, cascade, tunnel): спереди, уезжая на зрителя, и в самом конце круга; симметричные — Seam.
        // В целых положениях — 1.
        public static double Edge(string view, double d, int n)
        {
            if (n <= 0) return 1;
            if (Symmetric(view)) return Seam(Signed(d, n), n);
            double o = d < -0.1 ? Clamp((d + 0.5) / 0.4) : 1;
            return Math.Min(o, Clamp((n - 0.5 - d) * 2));
        }

        // Непрозрачность карточки, летящей со своего места (там она целиком видна) к месту в раскладке с непрозрачностью
        // stack, пройдя долю e: плавно от 1 к stack, но не больше edge (Edge) — если по пути выбор уводит карточку через шов
        // круга, она гаснет к шву целиком и перескакивает невидимой, а не исчезает с полпути одним кадром.
        public static double Blend(double stack, double edge, double e)
        {
            e = Clamp(e);
            return Math.Min(1 + (Clamp(stack) - 1) * e, Clamp(edge));
        }

        // С какого места начинает выбор при появлении. Окно, в котором человек сейчас, — первое в списке и на экране
        // сверху; если выбрано соседнее с ним (Alt+Tab — следующее, Shift+Alt+Tab — последнее), стопка встаёт так же,
        // как окна лежат на экране, — текущее впереди, — а выбор доезжает до выбранного обычным шагом. Иначе выбранное
        // окно проходило бы сквозь текущее в первый же кадр движения.
        public static double StartPos(int selected, int n)
        {
            if (n > 1 && (selected == 1 || selected == n - 1)) return 0;
            return selected;
        }

        // Где окно в кадре монитора (в пикселях кадра): прямоугольник окна и монитора — в точках экрана, кадр — imgW×imgH.
        // false — монитор или кадр пустые.
        public static bool CropBox(double left, double top, double right, double bottom, double monLeft, double monTop, double monRight, double monBottom,
            int imgW, int imgH, out double x, out double y, out double cw, out double ch)
        {
            x = y = cw = ch = 0;
            double pw = monRight - monLeft, ph = monBottom - monTop;
            if (pw <= 0 || ph <= 0 || imgW <= 0 || imgH <= 0 || right <= left || bottom <= top) return false;
            double kx = imgW / pw, ky = imgH / ph;
            x = (left - monLeft) * kx; y = (top - monTop) * ky; cw = (right - left) * kx; ch = (bottom - top) * ky;
            return true;
        }

        public static Pose Lerp(Pose a, Pose b, double t)
        {
            return new Pose
            {
                X = a.X + (b.X - a.X) * t, Y = a.Y + (b.Y - a.Y) * t, Z = a.Z + (b.Z - a.Z) * t, Angle = a.Angle + (b.Angle - a.Angle) * t,
                Pitch = a.Pitch + (b.Pitch - a.Pitch) * t, Roll = a.Roll + (b.Roll - a.Roll) * t,
                Opacity = a.Opacity + (b.Opacity - a.Opacity) * t, W = a.W + (b.W - a.W) * t, H = a.H + (b.H - a.H) * t
            };
        }
    }

    // Куда встанет свёрнутое окно, когда его развернут, — заранее, ничего не разворачивая (Win.RestorePlace), и как
    // выбранная свёрнутая карточка доходит до настоящего места окна, если приложение решило иначе. Здесь только расчёт —
    // его проверяет --selftest. Прямоугольники — в точках экрана.
    static class Restore
    {
        // Невидимые поля для растягивания у окна с рамкой — несколько точек слева, справа и снизу. Больше этого разница
        // между оконным прямоугольником и видимой рамкой не бывает: иначе размер миниатюры — не о том окне.
        public const int MaxBorder = 48;
        // Окно разворачивается под сценой своей системной анимацией DWM, около 250 мс от того, как оно перестало быть
        // свёрнутым. Это оценка, а не наша длительность: сцена закрывает анимацию и не убирается раньше её конца.
        public const int AnimMs = 250;
        // Дольше этого после конца ухода сцена окно не ждёт (приложение тормозит с разворачиванием) — убирается как есть.
        // Страховка, а не оформление: сторож ухода (FlipView.LeaveLimitMs) куда дольше.
        public const int WaitMs = 300;

        // Невидимые поля по разнице размеров оконного прямоугольника и видимой рамки (dx по ширине, dy по высоте):
        // слева и справа поровну, сверху — только то, что не уместилось снизу (у обычного окна сверху полей нет)
        public static void Borders(int dx, int dy, out int left, out int top, out int right, out int bottom)
        {
            dx = Math.Max(0, dx); dy = Math.Max(0, dy);
            left = dx / 2; right = dx - left;
            bottom = Math.Min(dy, right);
            top = dy - bottom;
        }

        // Видимая рамка окна после разворачивания. normal — rcNormalPosition из GetWindowPlacement (у окон без
        // WS_EX_TOOLWINDOW — в координатах рабочей области: сдвинуты на панель задач), toMax — развернётся на весь экран
        // (WPF_RESTORETOMAXIMIZED); monitor и work — монитор этого прямоугольника и его рабочая область; thumbW×thumbH —
        // размер миниатюры DWM свёрнутого окна (уже без невидимых полей; 0 — не узнали: поля считаются нулевыми).
        // false — места нет (пустой прямоугольник).
        public static bool Place(Win.RECT normal, bool tool, bool toMax, Win.RECT monitor, Win.RECT work, int thumbW, int thumbH, out Win.RECT visible)
        {
            visible = new Win.RECT();
            if (toMax) visible = work;
            else
            {
                int dx = tool ? 0 : work.Left - monitor.Left, dy = tool ? 0 : work.Top - monitor.Top;
                Win.RECT r = new Win.RECT { Left = normal.Left + dx, Top = normal.Top + dy, Right = normal.Right + dx, Bottom = normal.Bottom + dy };
                int w = r.Right - r.Left, h = r.Bottom - r.Top, l = 0, t = 0, rr = 0, b = 0;
                if (thumbW > 0 && thumbH > 0 && thumbW <= w && thumbH <= h && w - thumbW <= 2 * MaxBorder && h - thumbH <= 2 * MaxBorder)
                    Borders(w - thumbW, h - thumbH, out l, out t, out rr, out b);
                visible = new Win.RECT { Left = r.Left + l, Top = r.Top + t, Right = r.Right - rr, Bottom = r.Bottom - b };
            }
            return visible.Right - visible.Left >= 8 && visible.Bottom - visible.Top >= 8;
        }

        // Наибольшее расхождение сторон двух прямоугольников, в точках
        public static int Miss(Win.RECT a, Win.RECT b)
        {
            return Math.Max(Math.Max(Math.Abs(a.Left - b.Left), Math.Abs(a.Top - b.Top)), Math.Max(Math.Abs(a.Right - b.Right), Math.Abs(a.Bottom - b.Bottom)));
        }

        // Доводка до настоящего места: окно развернулось (узнали на knownMs ухода) не там, где ждали. Доля доводки
        // плавно растёт от 0 до 1 до конца ухода leaveMs, но не быстрее, чем за короткое движение: карточка доходит
        // до настоящего места без скачка — и в середине полёта, и после него.
        public static double Correct(double elapsedMs, double knownMs, double leaveMs)
        {
            return Flip.Glide(Flip.Share(elapsedMs, knownMs, CorrectEnd(knownMs, leaveMs) - knownMs));
        }

        // Когда доводка кончается
        public static double CorrectEnd(double knownMs, double leaveMs)
        {
            return knownMs + Math.Max(Tokens.Motion.FastMs, leaveMs - knownMs);
        }

        // Можно ли убирать сцену после конца ухода (elapsedMs ≥ leaveMs): окно развернулось (knownMs ≥ 0), его анимация
        // (animMs) прошла и доводка кончилась (moved — место оказалось не тем). Или ждать уже хватит (WaitMs).
        public static bool Ready(double elapsedMs, double leaveMs, double knownMs, bool moved, double animMs)
        {
            if (elapsedMs < leaveMs) return false;
            if (elapsedMs >= leaveMs + WaitMs) return true;
            if (knownMs < 0) return false;
            return elapsedMs >= knownMs + animMs && (!moved || elapsedMs >= CorrectEnd(knownMs, leaveMs));
        }
    }

    // Шаг кадров объёмной сцены. Время движения берётся из времени показа кадра (RenderingEventArgs.RenderingTime),
    // а не из часов в момент обработчика: обработчик зовётся неровно, а показ идёт в такт монитору — позиции
    // должны соответствовать моменту, когда кадр увидят. Здесь только расчёт — его проверяет --selftest.
    static class Pace
    {
        // Новый кадр с временем показа renderingMs? last — время прошлого кадра (−1 — кадров ещё не было).
        // WPF иногда зовёт Rendering дважды с тем же временем показа: повтор — не кадр (false), время не идёт.
        // dt — сколько секунд прошло с прошлого кадра (у первого — 0).
        public static bool Next(double renderingMs, ref double last, out double dt)
        {
            dt = 0;
            if (double.IsNaN(renderingMs)) return false;
            if (last >= 0 && renderingMs <= last) return false;
            if (last >= 0) dt = (renderingMs - last) / 1000;
            last = renderingMs;
            return true;
        }

        public const double MinStep = 1.0 / 500, MaxStep = 1.0 / 30;   // пределы оценки обычного шага кадра, в секундах

        // Обычный шаг кадра (оценка, секунды): короче — сразу, длиннее — понемногу, так что один подвисший кадр её почти
        // не сдвигает. typical — прошлая оценка, dt — шаг этого кадра.
        public static double Typical(double typical, double dt)
        {
            if (double.IsNaN(typical) || typical <= 0) typical = MaxStep;
            if (double.IsNaN(dt) || dt <= 0) return Math.Max(MinStep, Math.Min(MaxStep, typical));
            double t = dt < typical ? dt : typical + (dt - typical) * 0.02;
            return Math.Max(MinStep, Math.Min(MaxStep, t));
        }

        // Первый шаг движения (появление, уход, пружина выбора) не длиннее обычного кадра: если кадр сразу после старта
        // подвис (на первом показе после запуска — около 50 мс, --flipprobe --real), движение начинается с этого кадра,
        // а не прыгает сразу на треть пути
        public static double Start(double dt, double typical)
        {
            if (double.IsNaN(dt) || dt < 0) return 0;
            return Math.Min(dt, Math.Max(MinStep, typical));
        }

        public const int LiveMoving = 2;   // в движении за кадр обновляется не больше стольких живых картинок (по кругу)

        // Чьи живые картинки обновить в этом кадре. В покое — у всех. В движении — не больше LiveMoving по кругу, начиная
        // с cursor (cursor сдвигается), — копия кадра окна во весь экран и её загрузка WPF стоят времени, а карточка
        // в полёте и 30 раз в секунду выглядит живой. Карточки без первого кадра (fresh) обновляются всегда — им нужна
        // картинка; невидимые (visible = false) не обновляются вовсе: их не рисуют. pick — ответ, ничего не выделяется.
        public static void LivePick(bool[] visible, bool[] fresh, int count, bool moving, ref int cursor, bool[] pick)
        {
            if (count <= 0) { cursor = 0; return; }
            cursor = ((cursor % count) + count) % count;
            int budget = moving ? LiveMoving : count, used = 0, last = -1;
            for (int i = 0; i < count; i++)
            {
                int k = (cursor + i) % count;
                pick[k] = false;
                if (fresh[k]) { pick[k] = true; continue; }
                if (!visible[k] || used >= budget) continue;
                pick[k] = true;
                used++;
                last = k;
            }
            // Следующий кадр начинает с карточки после последней обновлённой по бюджету
            if (last >= 0) cursor = (last + 1) % count;
        }
    }

    // Распределение промежутков между кадрами за показ (для --flipprobe и SMENA_TRACE): доли коротких и длинных,
    // процентили. Добавление ничего не выделяет: замер идёт внутри кадра.
    sealed class Gaps
    {
        public const double Monitor = 6, Near = 8.5, Long = 12;   // пороги: шаг монитора 180 Гц (5,6 мс), полтора шага, два
        readonly double[] all;
        int n;
        public double Worst { get; private set; }
        public int Count { get { return n; } }

        public Gaps(int capacity) { all = new double[Math.Max(1, capacity)]; }

        public void Clear() { n = 0; Worst = 0; }

        public void Add(double ms)
        {
            if (double.IsNaN(ms) || ms < 0) return;
            if (ms > Worst) Worst = ms;
            if (n < all.Length) all[n++] = ms;
        }

        // Доля промежутков не длиннее limit (0…1); нет промежутков — 1
        public double Within(double limit)
        {
            if (n == 0) return 1;
            int k = 0;
            for (int i = 0; i < n; i++) if (all[i] <= limit) k++;
            return k / (double)n;
        }

        // Сколько промежутков длиннее limit
        public int Over(double limit)
        {
            int k = 0;
            for (int i = 0; i < n; i++) if (all[i] > limit) k++;
            return k;
        }

        // p-й процентиль (0…100), ближайший ранг; нет промежутков — 0
        public double Percentile(double p)
        {
            if (n == 0) return 0;
            var s = new double[n];
            Array.Copy(all, s, n);
            Array.Sort(s);
            int r = (int)Math.Ceiling(Math.Max(0, Math.Min(100, p)) / 100 * n) - 1;
            return s[Math.Max(0, Math.Min(n - 1, r))];
        }

        // Одной строкой: «n 312, ≤6 мс 96%, ≤8,5 99%, ≤12 100%, >12 0, p50 5.6, p95 5.9, p99 8.1, худший 9.4»
        public string Line()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (n == 0) return "кадров нет";
            return "n " + n + ", ≤6 мс " + (Within(Monitor) * 100).ToString("0", inv) + "%, ≤8.5 " + (Within(Near) * 100).ToString("0", inv)
                + "%, ≤12 " + (Within(Long) * 100).ToString("0", inv) + "%, >12 " + Over(Long)
                + ", p50 " + Percentile(50).ToString("0.0", inv) + ", p95 " + Percentile(95).ToString("0.0", inv) + ", p99 " + Percentile(99).ToString("0.0", inv)
                + ", худший " + Worst.ToString("0.0", inv);
        }

        // Для строки трассы, без пробелов: «n:312,le6:96,le8.5:99,le12:100,gt12:0,p50:5.6,p95:5.9,p99:8.1,max:9.4»
        public string Short()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return "n:" + n + ",le6:" + (Within(Monitor) * 100).ToString("0", inv) + ",le8.5:" + (Within(Near) * 100).ToString("0", inv)
                + ",le12:" + (Within(Long) * 100).ToString("0", inv) + ",gt12:" + Over(Long)
                + ",p50:" + Percentile(50).ToString("0.0", inv) + ",p95:" + Percentile(95).ToString("0.0", inv) + ",p99:" + Percentile(99).ToString("0.0", inv)
                + ",max:" + Worst.ToString("0.0", inv);
        }
    }

    // Картинка свёрнутого окна (Iconic.cs): размер вспомогательного окна под миниатюру DWM, годен ли кадр, пределы памяти.
    // Здесь только расчёт — его проверяет --selftest.
    static class Still
    {
        public const int MaxSide = 1280;     // вспомогательное окно не больше стольких точек по длинной стороне
        public const int MinSide = 48;       // меньше — у DWM нет картинки окна, только полоска заголовка (160×28)
        public const int GiveUpMs = 700;     // столько ждём годный кадр миниатюры, дальше — значок
        public const int Tolerance = 3;      // допуск по каналу: ровная заливка — это ещё не картинка
        public const int Grid = 24;          // кадр проверяется по сетке Grid×Grid точек
        public const int CacheCount = 16;    // запомненных картинок не больше стольких
        public const long CacheBytes = 64L * 1024 * 1024;   // и не больше стольких байт (ширина × высота × 4)

        // Размер под миниатюру окна sw×sh: пропорции те же, не больше MaxSide по длинной стороне и не больше монитора
        // monW×monH, не увеличивая. false — у DWM нет картинки окна (полоска заголовка свёрнутого окна или пустота).
        public static bool Fit(int sw, int sh, int monW, int monH, out int w, out int h)
        {
            w = 0; h = 0;
            int maxW = Math.Min(MaxSide, monW), maxH = Math.Min(MaxSide, monH);
            if (sw < MinSide || sh < MinSide || maxW < MinSide || maxH < MinSide) return false;
            double k = Math.Min(1.0, Math.Min(maxW / (double)sw, maxH / (double)sh));
            w = Math.Max(1, Math.Min(maxW, (int)Math.Round(sw * k)));
            h = Math.Max(1, Math.Min(maxH, (int)Math.Round(sh * k)));
            return true;
        }

        // Точки кадра (BGRA подряд) одного цвета с допуском tolerance — или их нет: картинки в кадре нет
        public static bool Flat(byte[] px, int tolerance)
        {
            if (px == null || px.Length < 4) return true;
            for (int o = 4; o + 3 < px.Length; o += 4)
                if (Math.Abs(px[o] - px[0]) > tolerance || Math.Abs(px[o + 1] - px[1]) > tolerance || Math.Abs(px[o + 2] - px[2]) > tolerance) return false;
            return true;
        }
    }

    // Запомненное по окну с пределами по числу и по памяти: новое вытесняет самое давнее по использованию.
    // Запись больше всего предела не берётся вовсе.
    class StillCache<T> where T : class
    {
        class Entry { public T Value; public long Bytes, Used; }

        readonly Dictionary<IntPtr, Entry> map = new Dictionary<IntPtr, Entry>();
        readonly int maxCount;
        readonly long maxBytes;
        long clock;

        public long Bytes { get; private set; }
        public int Count { get { return map.Count; } }

        public StillCache(int maxCount, long maxBytes) { this.maxCount = maxCount; this.maxBytes = maxBytes; }

        public T Get(IntPtr key)
        {
            Entry e;
            if (!map.TryGetValue(key, out e)) return null;
            e.Used = ++clock;
            return e.Value;
        }

        // false — не поместилось бы даже одно
        public bool Put(IntPtr key, T value, long bytes)
        {
            Forget(key);
            if (value == null || bytes > maxBytes || maxCount <= 0) return false;
            while (map.Count > 0 && (map.Count + 1 > maxCount || Bytes + bytes > maxBytes)) Forget(Oldest());
            map[key] = new Entry { Value = value, Bytes = bytes, Used = ++clock };
            Bytes += bytes;
            return true;
        }

        public void Forget(IntPtr key)
        {
            Entry e;
            if (!map.TryGetValue(key, out e)) return;
            map.Remove(key);
            Bytes -= e.Bytes;
        }

        // Остаются только записи, для которых keep — true
        public void Keep(Predicate<IntPtr> keep)
        {
            var gone = new List<IntPtr>();
            foreach (var kv in map) if (!keep(kv.Key)) gone.Add(kv.Key);
            foreach (IntPtr k in gone) Forget(k);
        }

        IntPtr Oldest()
        {
            IntPtr best = IntPtr.Zero;
            long used = long.MaxValue;
            foreach (var kv in map) if (kv.Value.Used < used) { used = kv.Value.Used; best = kv.Key; }
            return best;
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
