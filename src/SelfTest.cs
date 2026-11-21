// smena.txt. SelfTest.cs — проверки без экрана, без клавиатуры и без настоящих окон (--selftest).
// Клавиши проверяются через тот же KeyMachine.Key, который вызывает перехватчик; сам перехватчик здесь не ставится.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace DevTxt.Smena
{
    static class SelfTest
    {
        static int failed, checks;
        static void Check(bool ok, string what) { checks++; if (!ok) { failed++; Console.Error.WriteLine("FAIL  " + what); } }
        static bool Throws(Action a) { try { a(); return false; } catch { return true; } }
        static bool Near(double a, double b) { return Math.Abs(a - b) < 1e-6; }

        // Площадь карточки на экране со знаком: больше нуля — к зрителю лицом, меньше — спиной
        static double Winding(Pose p, double w, double h)
        {
            double[] u = { -0.5, 0.5, 0.5, -0.5 }, v = { -0.5, -0.5, 0.5, 0.5 }, x = new double[4], y = new double[4];
            for (int i = 0; i < 4; i++) { double z; Flip.Corner(p, u[i], v[i], w, h, out x[i], out y[i], out z); }
            double sum = 0;
            for (int i = 0; i < 4; i++) sum += x[i] * y[(i + 1) % 4] - x[(i + 1) % 4] * y[i];
            return -sum / 2;
        }

        static RawWin Raw(int hwnd, string title)
        {
            return new RawWin { Hwnd = new IntPtr(hwnd), Visible = true, Title = title, Pid = 100 + (uint)hwnd };
        }

        static Dictionary<string, object> Item(Dictionary<string, object> settings, string key)
        {
            foreach (object g in (object[])settings["groups"])
                foreach (object i in (object[])((Dictionary<string, object>)g)["items"])
                    if ((string)((Dictionary<string, object>)i)["key"] == key) return (Dictionary<string, object>)i;
            return null;
        }

        // Клавиатура для проверок: помнит, какие клавиши зажаты, и подаёт события так же, как их подаёт перехватчик
        class Keys
        {
            public readonly KeyMachine M = new KeyMachine();
            public readonly Session S = new Session();
            public readonly List<KeyAction> Log = new List<KeyAction>();
            public int Count = 5, Cols = 5, Chosen = -2;   // -2 — ещё ничего не выбрано, -1 — отмена
            public bool ForegroundFirst = true;
            bool alt, shift, ctrl;

            public bool Send(int vk, bool down) { return Send(vk, vk == 0xC0 ? KeyMachine.GraveScan : 0, down); }

            public bool Send(int vk, int scan, bool down)
            {
                if (vk == KeyMachine.Alt) alt = down;
                KeyAction a;
                int g;
                // Как в Windows: у отпускания самого Alt признака «Alt зажат» уже нет
                bool eaten = M.Key(vk, scan, down, alt, shift, ctrl, out a, out g);
                if (vk == KeyMachine.Shift) shift = down;
                if (vk == KeyMachine.Ctrl) ctrl = down;
                if (a != KeyAction.None) { Log.Add(a); Do(a, g); }
                return eaten;
            }

            public bool Tap(int vk) { bool e = Send(vk, true); Send(vk, false); return e; }

            // То же, что делает окно переключателя, только без окна
            void Do(KeyAction a, int g)
            {
                if (a == KeyAction.Open || a == KeyAction.OpenBack || a == KeyAction.OpenSame)
                {
                    if (Count == 0) { M.End(g); return; }
                    S.Begin(Count, Cols, ForegroundFirst, a == KeyAction.OpenBack);
                }
                else if (a == KeyAction.Close) { if (S.Open && !S.Remove(S.Selected, Cols)) M.End(g); }
                else if (a == KeyAction.Commit) { int c = S.Close(); if (c >= 0) Chosen = c; }
                else if (a == KeyAction.Cancel) { if (S.Open) Chosen = -1; S.Close(); }
                else S.Move(a);
            }
        }

        public static int Run()
        {
            Prefs.Persist = false;
            Prefs.Reset();
            const int Tab = KeyMachine.Tab, Alt = KeyMachine.Alt, Shift = KeyMachine.Shift, Esc = KeyMachine.Esc;

            Check(System.Text.RegularExpressions.Regex.IsMatch(Program.Id, "^[a-z][a-z0-9-]{0,31}$") && Program.Id != "hub", "app id fits the hub rule");
            // Сторож Alt+Tab: без окна и без перехватчика ничего не ломается и ничего не ставится
            Check(!Win.Fullscreen(IntPtr.Zero) && !Win.Elevated(IntPtr.Zero) && Win.Describe(IntPtr.Zero) == "неизвестное окно", "the Alt+Tab watchdog copes with no window");
            Check(!Ui.AllowSmooth, "the full-screen switcher keeps WPF at 60 frames: at the monitor's 180 Hz the layered window flickers");
            int before = Hook.Refreshes;
            Hook.Refresh();
            Check(!Hook.Installed && Hook.Refreshes == before, "refreshing the hook does nothing while it is not installed");
            foreach (string row in Program.Sprite) Check(row.Length == Program.Sprite[0].Length && row.Trim('.', '#', 'E').Length == 0, "sprite rows are of equal length and use only . # E");

            // ── Отбор окон и порядок ──
            var tool = Raw(3, "palette"); tool.ExStyle = Logic.WS_EX_TOOLWINDOW;
            var hidden = Raw(4, "hidden"); hidden.Visible = false;
            var untitled = Raw(5, "");
            var owned = Raw(6, "dialog"); owned.Owned = true;
            var ownedApp = Raw(7, "owned app window"); ownedApp.Owned = true; ownedApp.ExStyle = Logic.WS_EX_APPWINDOW;
            var cloaked = Raw(8, "background store app"); cloaked.Cloaked = 2;
            var elsewhere = Raw(9, "on another desktop"); elsewhere.Cloaked = 2; elsewhere.OtherDesktop = true;
            var ghost = Raw(10, "ghost frame"); ghost.Cloaked = 2; ghost.OtherDesktop = true; ghost.Ghost = true;
            var mine = Raw(11, "smena.txt"); mine.Pid = 1;
            var noAct = Raw(12, "overlay"); noAct.ExStyle = Logic.WS_EX_NOACTIVATE;
            var noActApp = Raw(13, "bar that asks to be listed"); noActApp.ExStyle = Logic.WS_EX_NOACTIVATE | Logic.WS_EX_APPWINDOW;
            var min = Raw(14, "minimised"); min.Minimized = true;
            var z = new List<RawWin> { Raw(1, "a"), tool, Raw(2, "b"), hidden, untitled, owned, ownedApp, cloaked, elsewhere, ghost, mine, noAct, noActApp, min };
            Func<List<RawWin>, string> ids = delegate(List<RawWin> l) { return string.Join(",", l.ConvertAll(delegate(RawWin w) { return w.Hwnd.ToString(); }).ToArray()); };
            Check(ids(Logic.Filter(z, 1, false)) == "1,2,7,13,14", "filter keeps switchable windows in z-order, got " + ids(Logic.Filter(z, 1, false)));
            Check(ids(Logic.Filter(z, 1, true)) == "1,2,7,9,13,14", "all desktops adds windows of other desktops but not ghosts, got " + ids(Logic.Filter(z, 1, true)));

            Func<int, string, WinItem> item = delegate(int hwnd, string key) { return new WinItem { Hwnd = new IntPtr(hwnd), Title = "t" + hwnd, Key = key }; };
            Func<List<WinItem>, string> order = delegate(List<WinItem> l) { return string.Join(",", l.ConvertAll(delegate(WinItem w) { return w.Hwnd.ToString(); }).ToArray()); };
            // Порядок «от недавнего к давнему»: не по тому, как окна лежат в Windows (там «поверх всех» всегда первые),
            // а по тому, какими пользовались; окна, которых ещё не видели впереди, — следом, как лежат
            var used = new Recent();
            var zlist = new List<WinItem> { item(1, "top"), item(2, "edit"), item(3, "web"), item(4, "mail"), item(5, "chat") };
            used.Sort(zlist);
            Check(order(zlist) == "1,2,3,4,5", "nothing used yet: the list stays in z-order");
            used.Touch(new IntPtr(4)); used.Touch(new IntPtr(2)); used.Touch(new IntPtr(99)); used.Touch(IntPtr.Zero);
            used.Sort(zlist);
            Check(order(zlist) == "2,4,1,3,5", "used windows go first, most recent first; never-seen ones keep z-order (the topmost one is no longer first), got " + order(zlist));
            used.Touch(new IntPtr(4));
            used.Sort(zlist);
            Check(order(zlist) == "4,2,1,3,5", "using a window again moves it to the front once, got " + order(zlist));
            used.Forget(new IntPtr(4));
            zlist = new List<WinItem> { item(1, "top"), item(2, "edit"), item(3, "web"), item(5, "chat"), item(4, "mail") };
            used.Sort(zlist);
            Check(order(zlist) == "2,1,3,5,4", "a minimised window is forgotten and falls back to its z-order place, got " + order(zlist));
            for (int i = 1000; i < 1400; i++) used.Touch(new IntPtr(i));
            used.Sort(zlist);
            Check(order(zlist) == "1,2,3,5,4" || order(zlist) == "2,1,3,5,4", "the recent list does not grow without end");

            // Впереди диалог, которого в списке нет: текущим считается его владелец
            var owners = new List<WinItem> { item(1, "top"), item(2, "edit"), item(3, "web") };
            Check(Logic.Listed(owners, new IntPtr[] { new IntPtr(50), new IntPtr(51), new IntPtr(3), new IntPtr(1) }) == new IntPtr(3), "a dialog in front maps to its nearest listed owner");
            Check(Logic.Listed(owners, new IntPtr[] { new IntPtr(2), new IntPtr(1) }) == new IntPtr(2), "a listed foreground window stays itself");
            Check(Logic.Listed(owners, new IntPtr[] { new IntPtr(50), IntPtr.Zero }) == IntPtr.Zero && Logic.Listed(owners, null) == IntPtr.Zero && Logic.Listed(owners, new IntPtr[0]) == IntPtr.Zero, "no listed owner: nothing is in front");
            Check(Logic.ForegroundFirst(owners, Logic.Listed(owners, new IntPtr[] { new IntPtr(50), new IntPtr(3) })) && order(owners) == "3,1,2" && Logic.StartIndex(owners.Count, true, false) == 1,
                "with a dialog in front, Alt+Tab starts from the window used before its owner, got " + order(owners));

            var mru = new List<WinItem> { item(1, "top"), item(2, "edit"), item(3, "web"), item(4, "EDIT") };
            Check(Logic.ForegroundFirst(mru, new IntPtr(3)) && order(mru) == "3,1,2,4", "the foreground window goes first, the rest keep their order, got " + order(mru));
            Check(!Logic.ForegroundFirst(mru, new IntPtr(99)) && !Logic.ForegroundFirst(mru, IntPtr.Zero) && order(mru) == "3,1,2,4", "an unknown foreground window changes nothing");
            Check(order(Logic.SameApp(mru, "edit")) == "2,4" && Logic.SameApp(mru, null).Count == 0, "same-app list: by key, case-insensitive, in order");

            // ── Движение выбора ──
            Check(Logic.Move(0, 5, 1) == 1 && Logic.Move(4, 5, 1) == 0, "forward wraps from the last to the first");
            Check(Logic.Move(0, 5, -1) == 4 && Logic.Move(3, 5, -1) == 2, "backward wraps from the first to the last");
            Check(Logic.Move(0, 1, 1) == 0 && Logic.Move(0, 0, 1) == 0, "one window or none: nowhere to move");
            Check(Logic.StartIndex(5, true, false) == 1 && Logic.StartIndex(5, true, true) == 4 && Logic.StartIndex(5, false, false) == 0 && Logic.StartIndex(1, true, false) == 0, "start: previous window, last when backward, first when the desktop is in front");
            Check(Logic.MoveRow(1, 9, 5, 1) == 6 && Logic.MoveRow(6, 9, 5, -1) == 1, "down and up move by a row");
            Check(Logic.MoveRow(4, 9, 5, 1) == 8 && Logic.MoveRow(7, 9, 5, 1) == 7 && Logic.MoveRow(2, 9, 5, -1) == 2, "down onto a short last row lands on its last window; edges stop");

            // ── Клавиши: тот же код, что вызывает перехватчик ──
            var k = new Keys();
            Check(!k.Send(Alt, true), "Alt down is passed through");
            Check(k.Send(Tab, true) && k.Send(Tab, false) && k.S.Open && k.S.Selected == 1, "Alt+Tab is swallowed (down and up), opens on the previous window");
            Check(!k.Send(Alt, false) && k.Chosen == 1 && !k.M.Active, "releasing Alt switches to it and is passed through");

            k = new Keys();
            k.Send(Alt, true); k.Tap(Tab); k.Tap(Tab); k.Tap(Tab);
            Check(k.S.Selected == 3, "three Tabs: third window from the front");
            k.Send(Shift, true); k.Tap(Tab); k.Send(Shift, false);
            Check(k.S.Selected == 2, "Shift+Tab goes back");
            Check(k.Tap(KeyMachine.Right) && k.S.Selected == 3 && k.Tap(KeyMachine.Left) && k.Tap(KeyMachine.Left) && k.S.Selected == 1, "arrows move while Alt is held and are swallowed");
            k.Send(Alt, false);
            Check(k.Chosen == 1, "Alt up after arrows: the selected window is chosen");

            k = new Keys();
            k.Send(Alt, true); k.Send(Shift, true); k.Tap(Tab);
            Check(k.Log[0] == KeyAction.OpenBack && k.S.Selected == 4, "Alt+Shift+Tab opens on the last window");
            k.Tap(Tab); k.Send(Shift, false); k.Tap(Tab); k.Tap(Tab);
            k.Send(Alt, false);
            Check(k.Chosen == 0, "wrap-around both ways ends where the keys say, chose " + k.Chosen);

            k = new Keys { Count = 9 };
            k.Send(Alt, true); k.Tap(Tab); k.Tap(KeyMachine.Down); k.Send(Alt, false);
            Check(k.Chosen == 6, "Down moves a row down, chose " + k.Chosen);

            k = new Keys();
            k.Send(Alt, true); k.Tap(Tab); k.Tap(Tab);
            Check(k.Send(Esc, true) && k.Send(Esc, false) && k.Chosen == -1 && !k.S.Open && !k.M.Active, "Esc cancels and is swallowed");
            Check(!k.Send(Alt, false) && k.Chosen == -1, "Alt up after Esc switches nowhere");
            Check(!k.Tap(Esc) && !k.Tap(KeyMachine.Left) && !k.Tap(Tab), "with the switcher closed and Alt up, Esc, arrows and Tab are left alone");

            k = new Keys();
            k.Send(Alt, true); k.Tap(Tab); k.Send(Esc, true); k.Send(Esc, false);
            Check(k.Tap(Tab) && k.S.Open && k.S.Selected == 1, "Tab again while Alt is still held opens the switcher again");
            k.Send(Alt, false);
            Check(k.Chosen == 1, "and the second round ends with a switch");

            k = new Keys();
            k.Send(Alt, true); k.Send(Tab, true); k.Send(Tab, true); k.Send(Tab, true); k.Send(Tab, false); k.Send(Alt, false);
            Check(k.Chosen == 3, "a held Tab repeats: each repeat moves on, chose " + k.Chosen);

            k = new Keys();
            k.Send(Alt, true); k.Tap(Tab);
            // Пока переключатель открыт, Alt+буква до программы под ним не доходит (иначе она открывает меню)
            Check(k.Send(0x41, true) && k.Send(0x41, false) && k.M.Active && k.S.Open && k.S.Selected == 1 && k.Log.Count == 1, "other keys with Alt held are swallowed (down and up), do nothing and do not end the session");
            Check(k.Tap(0x73) && k.Tap(0x20) && k.M.Active, "Alt+F4 and Alt+Space do not reach the app underneath either");
            Check(!k.Send(Shift, true) && !k.Send(Shift, false) && !k.Send(KeyMachine.LWin, true) && !k.Send(KeyMachine.LWin, false), "modifiers are never swallowed");
            Check(k.Tap(KeyMachine.Enter) && k.Chosen == 1 && !k.M.Active, "Enter with Alt held switches at once");
            Check(!k.Tap(0x41), "after the switch letters are ordinary letters again");
            k.Send(Alt, false);

            // Delete закрывает выбранное окно: карточка уходит, показ продолжается
            k = new Keys();
            k.Send(Alt, true); k.Tap(Tab); k.Tap(Tab);
            Check(k.Tap(KeyMachine.Delete) && k.Log[k.Log.Count - 1] == KeyAction.Close && k.S.Open && k.S.Count == 4 && k.S.Selected == 2 && k.M.Active, "Delete with Alt held closes the selected window and keeps the switcher open");
            k.Send(Alt, false);
            Check(k.Chosen == 2, "after Delete, Alt up switches to the window that took its place");
            k = new Keys { Count = 1 };
            k.Send(Alt, true); k.Tap(Tab); k.Tap(KeyMachine.Delete);
            Check(!k.S.Open && !k.M.Active, "closing the last window ends the switcher");
            k.Send(Alt, false);
            Check(!k.Tap(KeyMachine.Delete) && k.Chosen == -2, "with the switcher closed Delete is an ordinary key");
            var rs = new Session();
            rs.Begin(9, 5, true, false);
            rs.Selected = 8;
            Check(rs.Remove(8, 4) && rs.Count == 8 && rs.Selected == 7 && rs.Cols == 4, "removing the last selected card selects the new last one");
            Check(rs.Remove(2, 4) && rs.Count == 7 && rs.Selected == 6, "removing a card before the selected one keeps the same window selected");
            Check(rs.Remove(6, 4) && rs.Selected == 5 && rs.Remove(99, 4) && rs.Remove(-1, 4) && rs.Count == 6, "a card that is not there is not removed");
            rs.Selected = 0;
            Check(rs.Remove(0, 4) && rs.Selected == 0 && rs.Count == 5, "removing the first selected card selects the next one");
            rs.Close();
            Check(!rs.Remove(0, 4) && rs.Count == 5, "a closed session removes nothing");

            // Быстрый второй Alt+Tab: окно узнаёт о конце первого переключения позже перехватчика и говорит «закончено»
            // уже во время второго — опоздавшее «закончено» второе переключение не трогает
            k = new Keys();
            k.Send(Alt, true); k.Tap(Tab);
            int g1 = k.M.Generation;
            k.Send(Alt, false);
            k.Send(Alt, true); k.Tap(Tab);
            int g2 = k.M.Generation;
            k.M.End(g1);
            Check(g2 != g1 && k.M.Active, "a late End of the first Alt+Tab does not end the second one");
            Check(k.Tap(Tab) && k.S.Selected == 2, "…Tab still moves in the second one");
            k.Chosen = -2;
            Check(!k.Send(Alt, false) && k.Chosen == 2 && k.Log[k.Log.Count - 1] == KeyAction.Commit, "…and releasing Alt still switches, chose " + k.Chosen);
            k.Send(Alt, true); k.Tap(Tab);
            k.M.End(k.M.Generation);
            Check(!k.M.Active && !k.Tap(KeyMachine.Left), "End with the current generation does end the session (click, lost Alt)");
            k.Send(Alt, false);
            k = new Keys();
            int gs = k.M.BeginSticky();
            Check(gs == k.M.Generation && gs != 0 && k.M.Sticky, "a sticky show gets its own generation");
            k.M.End(gs - 1);
            Check(k.M.Sticky, "…and an older End does not close it");
            k.M.End(gs);
            Check(!k.M.Active && !k.M.Sticky, "…while its own End does");
            k = new Keys();
            KeyAction ga; int gk1, gk2, gk3;
            k.M.Key(Alt, 0x38, true, true, false, false, out ga, out gk1);
            k.M.Key(Tab, 0x0F, true, true, false, false, out ga, out gk1);
            k.M.Key(Tab, 0x0F, true, true, false, false, out ga, out gk2);
            k.M.Key(Alt, 0x38, false, false, false, false, out ga, out gk3);
            Check(gk1 == gk2 && gk2 == gk3 && ga == KeyAction.Commit && gk1 == k.M.Generation, "every action of one Alt+Tab carries the same generation");
            k.M.Key(Alt, 0x38, true, true, false, false, out ga, out gk2);
            k.M.Key(Tab, 0x0F, true, true, false, false, out ga, out gk2);
            Check(gk2 == gk1 + 1 && ga == KeyAction.Open, "the next Alt+Tab gets the next generation");

            k = new Keys();
            k.Send(Alt, true); k.Send(KeyMachine.Ctrl, true);
            Check(!k.Tap(Tab) && !k.S.Open, "Ctrl+Alt+Tab is left to Windows");
            k.Send(KeyMachine.Ctrl, false); k.Send(Alt, false);
            Check(!k.Tap(Tab) && k.Log.Count == 0, "Tab without Alt is an ordinary Tab");

            // Alt отпустили, а мы этого не увидели: следующая клавиша без Alt заканчивает показ и не проглатывается
            k = new Keys();
            k.Send(Alt, true); k.Tap(Tab); k.Tap(Tab);
            KeyAction stale;
            int staleGen;
            Check(!k.M.Key(0x41, 0x1E, true, false, false, false, out stale, out staleGen) && stale == KeyAction.Commit && !k.M.Active, "a key without Alt during a session ends it and goes through");

            // Нажатия, помеченные как проглоченные, не «залипают»: отпускание без нажатия не трогаем
            k = new Keys();
            Check(!k.Send(Tab, false) && !k.Send(Esc, false), "a key-up that was never swallowed on the way down goes through");

            // Alt+` — окна того же приложения
            k = new Keys();
            k.Send(Alt, true);
            Check(k.Tap(0xC0) && k.Log[0] == KeyAction.OpenSame && k.S.Selected == 1, "Alt+` opens the same-app list");
            k.Tap(0xC0); k.Send(Alt, false);
            Check(k.Chosen == 2, "` again moves on");
            k = new Keys();
            k.M.SameApp = delegate { return false; };
            k.Send(Alt, true);
            Check(!k.Tap(0xC0) && !k.S.Open, "Alt+` switched off: the key is left alone");
            Check(k.Tap(Tab) && k.S.Open, "…while Alt+Tab still works");

            // Нет окон — нечего показывать: клавиши снова ничьи
            k = new Keys { Count = 0 };
            k.Send(Alt, true); k.Tap(Tab);
            Check(!k.S.Open && !k.M.Active && !k.Tap(KeyMachine.Left), "no windows: nothing opens, nothing is held");

            // Показ командой: без Alt, до щелчка, Enter или Esc
            k = new Keys();
            k.M.BeginSticky(); k.S.Begin(5, 5, true, false);
            Check(k.Tap(Tab) && k.Tap(KeyMachine.Right) && k.S.Selected == 3, "sticky: Tab and arrows move without Alt");
            k.Send(Alt, true); k.Send(Alt, false);
            Check(k.S.Open && k.Chosen == -2, "sticky: pressing and releasing Alt does not switch");
            Check(k.Tap(KeyMachine.Enter) && k.Chosen == 3 && !k.M.Sticky, "sticky: Enter switches");
            k = new Keys();
            k.M.BeginSticky(); k.S.Begin(5, 5, true, false);
            Check(k.Tap(Esc) && k.Chosen == -1 && !k.M.Active, "sticky: Esc cancels");
            k = new Keys();
            k.M.BeginSticky(); k.S.Begin(5, 5, true, false);
            k.M.End();
            Check(!k.Tap(Tab) && !k.Tap(Esc), "closed by a click: keys are released");

            // ── Когда Alt+Tab остаётся системным ──
            Check(!Logic.LeaveAlone(false, true, true, false), "normally Alt+Tab is ours");
            Check(Logic.LeaveAlone(true, true, true, false), "paused: left alone");
            Check(Logic.LeaveAlone(false, false, true, false), "switched off in settings: left alone");
            Check(Logic.LeaveAlone(false, true, true, true) && !Logic.LeaveAlone(false, true, false, true), "fullscreen app: left alone only while the setting is on");
            k = new Keys();
            bool leave = true;
            k.M.LeaveAlone = delegate { return leave; };
            k.Send(Alt, true);
            Check(!k.Send(Tab, true) && !k.Send(Tab, false) && !k.S.Open && k.Log.Count == 0, "left alone: Alt+Tab is not swallowed and nothing opens");
            k.Send(Alt, false);
            Check(k.Chosen == -2, "left alone: Alt up does nothing either");
            leave = false;
            k.Send(Alt, true);
            Check(k.Tap(Tab) && k.S.Open, "the rule is asked again at the next Alt+Tab");
            leave = true;
            Check(k.Tap(Tab) && k.S.Selected == 2, "a session that has begun is not dropped halfway");
            k.Send(Alt, false);
            Check(Logic.IsFullscreen(0, 0, 1920, 1080, 0, 0, 1920, 1080, false, false, false), "borderless window over the whole monitor is fullscreen");
            Check(Logic.IsFullscreen(1920, 0, 4480, 1440, 1920, 0, 4480, 1440, false, false, false), "…on the second monitor too");
            Check(!Logic.IsFullscreen(-8, -8, 1928, 1088, 0, 0, 1920, 1080, true, true, true), "a maximised window with a caption is not");
            Check(!Logic.IsFullscreen(0, 0, 1920, 1040, 0, 0, 1920, 1080, false, false, false), "a window that leaves the taskbar visible is not");
            // Развёрнутое окно без заголовка (Steam, Electron, WindowStyle.None) — не игра: из-за него Alt+Tab уходил системе
            Check(!Logic.IsFullscreen(0, 0, 1920, 1080, 0, 0, 1920, 1080, false, true, false), "a maximised frameless window is not fullscreen");
            Check(!Logic.IsFullscreen(0, 0, 1920, 1080, 0, 0, 1920, 1080, false, false, true), "a frameless window with a sizing border is not fullscreen");
            Check(!Logic.IsFullscreen(-8, -8, 1928, 1088, 0, 0, 1920, 1080, false, false, false) && !Logic.IsFullscreen(-1, 0, 1920, 1080, 0, 0, 1920, 1080, false, false, false), "a window that sticks out past the monitor is not fullscreen: the rectangle must match exactly");
            Check(!Logic.IsFullscreen(0, 0, 3840, 1080, 0, 0, 1920, 1080, false, false, false), "a window spanning two monitors is not fullscreen");

            // ── Раскладка и прокрутка ──
            int cols, rows, visible;
            double cw = Layout.CardW(240), ch = Layout.CardH(240);
            Layout.Rows(3, cw, ch, 1400, 640, out cols, out rows, out visible);
            Check(cols == 3 && rows == 1 && visible == 1, "3 windows: one row");
            Layout.Rows(9, cw, ch, 1400, 640, out cols, out rows, out visible);
            Check(cols == 5 && rows == 2 && visible == 2, "9 windows: two even rows, 5 and 4, got " + cols + "x" + rows);
            Layout.Rows(25, cw, ch, 1400, 640, out cols, out rows, out visible);
            Check(cols == 5 && rows == 5 && visible == 3, "25 windows: 5 rows of 5, three fit, got " + cols + "x" + rows + " visible " + visible);
            Layout.Rows(4, cw, ch, 200, 100, out cols, out rows, out visible);
            Check(cols == 1 && rows == 4 && visible == 1, "tiny screen: still one card per row and one row visible");
            Check(Layout.Scroll(0, 0, 3, 5) == 0 && Layout.Scroll(0, 2, 3, 5) == 0, "selection inside the visible rows: no scroll");
            Check(Layout.Scroll(0, 3, 3, 5) == 1 && Layout.Scroll(1, 4, 3, 5) == 2, "selection below: scroll just enough");
            Check(Layout.Scroll(2, 1, 3, 5) == 1 && Layout.Scroll(2, 0, 3, 5) == 0, "selection above: scroll back just enough");
            Check(Layout.Scroll(0, 4, 3, 5) == 2 && Layout.Scroll(2, 0, 3, 5) == 0, "wrap-around jumps to the far end");
            Check(Layout.Scroll(4, 1, 3, 2) == 0 && Layout.Scroll(0, 0, 1, 1) == 0, "scroll never leaves the rows");

            double fx, fy, fw, fh;
            Layout.Fit(1920, 1080, 10, 20, 240, 150, 1, out fx, out fy, out fw, out fh);
            Check(Near(fw, 240) && Near(fh, 135) && Near(fx, 10) && Near(fy, 27.5), "a wide window fits by width, centred");
            Layout.Fit(600, 1200, 0, 0, 240, 150, 1, out fx, out fy, out fw, out fh);
            Check(Near(fh, 150) && Near(fw, 75) && Near(fx, 82.5), "a tall window fits by height, centred");
            Layout.Fit(100, 50, 0, 0, 240, 150, 1, out fx, out fy, out fw, out fh);
            Check(Near(fw, 100) && Near(fh, 50) && Near(fx, 70) && Near(fy, 50), "a small window is not stretched");
            double dl = 0, dt = 100, dr = 200, db = 200, sl = 0, st = 0, sr = 1000, sb = 500;
            Check(Layout.Clip(ref dl, ref dt, ref dr, ref db, ref sl, ref st, ref sr, ref sb, 0, 150, 500, 400) && Near(dt, 150) && Near(db, 200) && Near(st, 250) && Near(sb, 500) && Near(sr, 1000),
                "a preview half above the edge shows the lower half of its window");
            dl = 0; dt = 0; dr = 200; db = 100; sl = 7; st = 0; sr = 1007; sb = 500;
            Check(Layout.Clip(ref dl, ref dt, ref dr, ref db, ref sl, ref st, ref sr, ref sb, -50, -50, 500, 400) && Near(dr, 200) && Near(sl, 7) && Near(sr, 1007), "a preview fully inside is untouched");
            dl = 0; dt = 500; dr = 200; db = 600;
            Check(!Layout.Clip(ref dl, ref dt, ref dr, ref db, ref sl, ref st, ref sr, ref sb, 0, 0, 500, 400), "a preview outside is hidden");

            double sx = 0, sv = 0;
            for (int i = 0; i < 60; i++) Layout.Spring(ref sx, ref sv, 100, Tokens.Motion.SpringFadeStiffness, Tokens.Motion.SpringFadeDamping, 1.0 / 60);
            Check(Math.Abs(sx - 100) < 0.5 && Math.Abs(sv) < 5, "the highlight spring settles within a second, at " + sx);
            sx = 0; sv = 0;
            Layout.Spring(ref sx, ref sv, 100, Tokens.Motion.SpringFadeStiffness, Tokens.Motion.SpringFadeDamping, 5);
            Check(sx > 0 && sx < 130, "a long frame does not throw the spring away, at " + sx);

            // ── Значок приложения из Магазина ──
            Check(StoreLogo.FromManifest("<VisualElements DisplayName=\"x\" Square44x44Logo=\"Assets\\AppList.png\" />") == "Assets\\AppList.png" && StoreLogo.FromManifest("<x/>") == null, "logo name is read from the manifest");
            Check(StoreLogo.Pick("Assets\\AppList.png", new string[] { @"C:\a\AppList.scale-100.png", @"C:\a\AppList.scale-200.png", @"C:\a\AppList.targetsize-16.png", @"C:\a\AppList.targetsize-48_altform-unplated.png", @"C:\a\AppList.targetsize-256_altform-unplated.png", @"C:\a\AppList.targetsize-48_contrast-black.png", @"C:\a\Other.scale-400.png" }) == @"C:\a\AppList.targetsize-48_altform-unplated.png", "the unplated 48 px logo wins");
            Check(StoreLogo.Pick("Assets\\AppList.png", new string[] { @"C:\a\AppList.scale-100.png", @"C:\a\AppList.scale-200.png" }) == @"C:\a\AppList.scale-200.png" && StoreLogo.Pick("Assets\\AppList.png", new string[0]) == null, "otherwise the largest scale; nothing if there is nothing");

            // ── Выдуманные окна для снимков ──
            var demo = Demo.Windows(25);
            Check(demo.Count == 25 && demo[3].Minimized && !demo[0].Minimized && demo.TrueForAll(delegate(WinItem d) { return d.Fake && d.Title.Length > 0 && d.Aspect > 0; }), "demo windows are complete");

            // ── Описание настроек — в том виде, в каком его ждёт core.txt ──
            var s = AppHub.Settings();
            Check((string)Item(s, "replace")["type"] == "switch" && (bool)Item(s, "replace")["value"], "«Заменять Alt+Tab» is a switch, on by default");
            Check((string)Item(s, "games")["type"] == "switch" && (bool)Item(s, "games")["value"], "«Не вмешиваться в играх» is on by default");
            Check((string)Item(s, "allDesktops")["type"] == "switch" && !(bool)Item(s, "allDesktops")["value"], "«Со всех рабочих столов» is off by default");
            Check((bool)Item(s, "sameApp")["value"], "Alt+` is on by default");
            var size = Item(s, "size");
            Check((string)size["type"] == "slider" && Near((double)size["value"], Layout.DefaultPreview) && (double)size["min"] < (double)size["value"] && (double)size["max"] > (double)size["value"] && (double)size["step"] > 0, "«Размер превью» is a slider within its range");
            foreach (string key in new string[] { "replace", "sameApp", "games", "allDesktops", "size" }) Check(!string.IsNullOrEmpty(Item(s, key)["label"] as string), key + " has a label");
            string json = new JavaScriptSerializer().Serialize(HubLink.Msg("type", "state", "topic", AppHub.Topic, "data", AppHub.Settings()));
            var back = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            Check(back != null && (string)back["topic"] == "smena.settings" && Item((Dictionary<string, object>)back["data"], "games")["value"] is bool, "settings survive JSON");

            // settings.set: значение меняется, о нём сообщают один раз, чужой ключ и не то значение отклоняются
            int fired = 0;
            Action count = delegate { fired++; };
            Prefs.Changed += count;
            AppHub.Apply("games", false);
            Check(!Prefs.Games && !(bool)Item(AppHub.Settings(), "games")["value"], "settings.set changes a switch");
            AppHub.Apply("games", false);
            Check(fired == 1, "an unchanged value is not announced again, announced " + fired);
            AppHub.Apply("replace", false); AppHub.Apply("allDesktops", true); AppHub.Apply("sameApp", false);
            Check(!Prefs.Replace && Prefs.AllDesktops && !Prefs.SameApp && fired == 4, "every switch is applied");
            AppHub.Apply("size", 300);
            Check(Near(Prefs.Size, 300) && Near((double)Item(AppHub.Settings(), "size")["value"], 300), "settings.set changes the slider (whole number, as JSON gives it)");
            AppHub.Apply("size", 311.0m);
            Check(Near(Prefs.Size, 320), "the slider value snaps to its step, got " + Prefs.Size);
            AppHub.Apply("size", 5000.0);
            Check(Near(Prefs.Size, Layout.MaxPreview), "…and stays within its range");
            Check(Throws(delegate { AppHub.Apply("noSuchKey", true); }), "unknown key is refused");
            Check(Throws(delegate { AppHub.Apply("games", "yes"); }) && Throws(delegate { AppHub.Apply("size", "big"); }) && Throws(delegate { AppHub.Apply("size", true); }), "a value of the wrong kind is refused");
            Check(Throws(delegate { AppHub.Apply(null, null); }), "empty arguments are refused");
            Check(!Prefs.Games && Near(Prefs.Size, Layout.MaxPreview), "refused commands change nothing");
            fired = 0;
            Prefs.SetPaused(true);
            Check(Prefs.Paused && fired == 1 && Logic.LeaveAlone(Prefs.Paused, true, Prefs.Games, false), "pause is announced and leaves Alt+Tab alone");
            Prefs.Changed -= count;

            // hub.theme: «Меньше движения» читается только из настоящего «да»
            Check(AppHub.ReadReduced(HubLink.Msg("mode", "dark", "accent", null, "reducedMotion", true)), "reducedMotion: true is read");
            Check(!AppHub.ReadReduced(HubLink.Msg("mode", "dark", "reducedMotion", false)) && !AppHub.ReadReduced(HubLink.Msg("reducedMotion", "yes")) && !AppHub.ReadReduced(null) && !AppHub.ReadReduced("x"), "anything else is not reduced motion");

            // ── Семья dev.txt: страница «Игры» в core.txt, общий трей, клавиши, свой значок ──
            Prefs.Reset();
            s = AppHub.Settings();
            Check(Item(s, "games")["section"] as string == "games", "«Не вмешиваться в играх» goes to the «Игры» page of core.txt");
            foreach (string key in new string[] { "replace", "sameApp", "allDesktops", "size" }) Check(!Item(s, key).ContainsKey("section"), key + " stays on the smena page of core.txt");
            back = new JavaScriptSerializer().DeserializeObject(new JavaScriptSerializer().Serialize(s)) as Dictionary<string, object>;
            Check(back != null && Item(back, "games")["section"] as string == "games" && Item(back, "games")["key"] as string == "games", "the section survives JSON, the key stays the same");

            var tray = AppHub.Tray();
            Check(tray.Count == 2 && tray.Count <= Family.MaxTray, "two quick items for the core.txt tray, no more than four");
            var showItem = tray[0].ToData();
            var pauseItem = tray[1].ToData();
            Check(showItem["key"] as string == "show" && showItem["label"] as string == "Открыть" && !showItem.ContainsKey("check") && !showItem.ContainsKey("enabled"), "the first item is «Открыть», a plain action");
            Check(pauseItem["key"] as string == "pause" && pauseItem["label"] as string == "Приостановить" && pauseItem["check"] is bool && !(bool)pauseItem["check"], "«Приостановить» is a check item, unchecked while not paused");
            foreach (TrayItem t in tray)
                Check(t.Run != null && t.Enabled && t.Label.IndexOf(Program.Id, StringComparison.OrdinalIgnoreCase) < 0 && Array.IndexOf(new string[] { "autostart", "startup", "update", "updates", "focus", "dnd", "core", "exit", "quit" }, t.Key) < 0,
                    "tray item «" + t.Label + "»: no app name and nothing the shared menu already has");
            fired = 0;
            Prefs.Changed += count;
            tray[1].Run();
            Check(Prefs.Paused && fired == 1 && (bool)AppHub.Tray()[1].ToData()["check"], "running «pause» pauses, announces it, and the next tray state carries the check");
            tray[1].Run();
            Check(!Prefs.Paused && fired == 2 && !(bool)AppHub.Tray()[1].ToData()["check"], "…and running it again resumes");
            Prefs.Changed -= count;
            int opened = 0;
            AppHub.Open = delegate { opened++; };
            tray[0].Run();
            AppHub.Open = null;
            Check(opened == 1 && !Throws(delegate { tray[0].Run(); }), "«Открыть» opens the window; without a window it does nothing");

            var keyList = AppHub.Keys();
            Check(keyList.Count == 2 && keyList[0].Id == "switch" && keyList[0].Label == "Переключение окон" && keyList[0].Keys == "Alt+Tab"
                && keyList[1].Id == "sameApp" && keyList[1].Label == "Окна того же приложения" && keyList[1].Keys == "Alt+`", "keys for core.txt: Alt+Tab and Alt+`, got " + keyList[0].Keys + " and " + keyList[1].Keys);
            Check(KeyNames.Normal(keyList[0].Keys) == keyList[0].Keys && KeyNames.Normal(keyList[1].Keys) == keyList[1].Keys && KeyNames.Normal("tab + ALT") == "Alt+Tab", "the combinations are written the family way");
            Check(keyList[0].On && keyList[1].On, "both keys are on by default");
            Prefs.SameApp = false;
            Check(AppHub.Keys()[0].On && !AppHub.Keys()[1].On, "Alt+` switched off: only it is off");
            Prefs.SameApp = true; Prefs.Replace = false;
            Check(!AppHub.Keys()[0].On && !AppHub.Keys()[1].On, "replacement switched off: both keys are off");
            Prefs.Replace = true; Prefs.Paused = true;
            Check(!AppHub.Keys()[0].On && !AppHub.Keys()[1].On, "paused: both keys are off");
            Prefs.Reset();
            Check((string)keyList[0].ToData()["keys"] == "Alt+Tab" && keyList[0].ToData()["on"] is bool, "a key goes to the hub as id, label, keys, on");

            // Семья на клиенте без хаба: своё остаётся своим, tray.run попадает в пункты трея, свой значок не прячется
            var fl = new HubLink(Program.Id, Program.Name, Program.VersionText, 9);
            fl.Topics = new string[] { "hub.theme" };
            fl.Commands = new string[] { "settings.set", "show" };
            fl.OnCommand = delegate(string name, Dictionary<string, object> a, string from) { return "own " + name; };
            var fam = new Family(fl, Program.Id);
            Check(Array.IndexOf(fl.Topics, "hub.theme") >= 0 && Array.IndexOf(fl.Topics, Family.Topic) >= 0 && Array.IndexOf(fl.Topics, "hub.apps") >= 0, "the family listens to core.family and hub.apps on top of hub.theme");
            Check(Array.IndexOf(fl.Commands, "settings.set") >= 0 && Array.IndexOf(fl.Commands, "show") >= 0 && Array.IndexOf(fl.Commands, Family.TrayRun) >= 0, "tray.run is added to the own commands");
            Check(!fam.HideOwnTray && !fam.CoreOnline && !fam.IslandOnline && !fam.Notify("smena.txt", "проверка", false) && !fam.SetFocus(true, 0), "no hub: the own tray icon stays, nothing goes to core.txt or the island");
            fam.SetTray(AppHub.Tray());
            fam.SetKeys(AppHub.Keys());
            fl.OnCommand(Family.TrayRun, HubLink.Msg("key", "pause"), "core");
            Check(Prefs.Paused, "tray.run «pause» reaches the pause item");
            fl.OnCommand(Family.TrayRun, HubLink.Msg("key", "pause"), "core");
            Check(!Prefs.Paused, "…and runs it again");
            Check(Throws(delegate { fl.OnCommand(Family.TrayRun, HubLink.Msg("key", "exit"), "core"); }) && Throws(delegate { fl.OnCommand(Family.TrayRun, new Dictionary<string, object>(), "core"); }), "an unknown or missing tray key is refused");
            Check(fl.OnCommand("settings.set", HubLink.Msg("key", "games", "value", true), "core") as string == "own settings.set", "other commands still reach the own handler");
            Check(!AppHub.HideOwnTray && !AppHub.CoreKnown, "before the hub link starts nothing hides the own tray icon");

            // Свой значок в трее: core.txt держит общий — не нужен; связь пропала — сразу нужен; после запуска ждём core.txt недолго
            var own = new OwnTray(false);
            Check(own.Visible(false, false) && !own.Waiting, "core.txt not installed: the icon shows at once");
            Check(!own.Visible(true, true), "core.txt holds the one icon: own icon hidden");
            Check(own.Visible(false, false), "the link drops: own icon is back at once");
            Check(own.Visible(true, false), "core.txt online but the tray is not his: own icon stays");
            own = new OwnTray(true);
            Check(!own.Visible(false, false) && own.Waiting, "core.txt installed: right after start the icon waits for it");
            Check(!own.Visible(true, true) && !own.Waiting, "core.txt answers that it holds the icon: hidden, no more waiting");
            Check(own.Visible(false, false), "…and when the link drops the icon is back at once, without waiting again");
            own = new OwnTray(true);
            Check(!own.Visible(false, false), "core.txt silent: still waiting");
            own.Expire();
            Check(own.Visible(false, false) && !own.Waiting, "…the wait runs out: the icon shows, it never stays hidden");
            Check(!own.Visible(true, true), "core.txt that comes later still takes the icon");
            own = new OwnTray(true);
            Check(own.Visible(true, false), "core.txt answers that the tray is not his: the icon shows without waiting the rest");
            Check(OwnTray.Wait > TimeSpan.Zero && OwnTray.Wait <= TimeSpan.FromSeconds(3), "the start-up wait is short: about three seconds at most");

            // Команда «show»: без страницы — переключатель, как всегда; со страницей — окно, а не переключатель во весь экран.
            // core.txt со страницы «Клавиши» шлёт { page: "keys" } — это страница smena.txt в core.txt, где включаются оба сочетания.
            Check(ShowRoute.For(null, true) == ShowTarget.Switcher && ShowRoute.For(new Dictionary<string, object>(), true) == ShowTarget.Switcher
                && ShowRoute.For(HubLink.Msg("page", null), true) == ShowTarget.Switcher, "show without a page opens the switcher, as before");
            Check(ShowRoute.For(HubLink.Msg("page", "keys"), true) == ShowTarget.CorePage, "show {page: keys} with core.txt online: the smena page of core.txt, where both keys are switched");
            Check(ShowRoute.For(HubLink.Msg("page", "keys"), false) == ShowTarget.About, "…and without core.txt: the window");
            foreach (object page in new object[] { "games", "general", "app:smena", "", 5, true })
                Check(ShowRoute.For(HubLink.Msg("page", page), true) == ShowTarget.About && ShowRoute.For(HubLink.Msg("page", page), false) == ShowTarget.About,
                    "show with page «" + page + "»: the window, never the full-screen switcher");
            int switched = 0, windows = 0;
            AppHub.Show = delegate { switched++; return true; };
            AppHub.Open = delegate { windows++; };
            var shown = AppHub.ShowCommand(HubLink.Msg("page", "keys"));
            Check(windows == 1 && switched == 0 && (bool)shown["shown"] && shown["opened"] as string == "about", "show {page: keys} without a hub link opens the window, not the switcher");
            shown = AppHub.ShowCommand(new Dictionary<string, object>());
            Check(switched == 1 && windows == 1 && (bool)shown["shown"] && shown["opened"] as string == "switcher", "a bare show still opens the switcher");
            AppHub.Show = null; AppHub.Open = null;
            Check(!Throws(delegate { shown = AppHub.ShowCommand(HubLink.Msg("page", "games")); }) && !(bool)shown["shown"] && shown["opened"] as string == "about"
                && !(bool)AppHub.ShowCommand(null)["shown"], "no window and no switcher (--headless): nothing is shown and nothing throws");

            // Настройки переживают запись и чтение; «Приостановить» на диск не попадает
            string dir = Path.Combine(Path.GetTempPath(), Program.Id + "-test-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            Environment.SetEnvironmentVariable("SMENA_DATA", dir);
            try
            {
                Prefs.Persist = true;
                Prefs.Reset();
                Prefs.Replace = false; Prefs.Games = false; Prefs.SameApp = false; Prefs.AllDesktops = true; Prefs.Size = 200; Prefs.Paused = true;
                Prefs.Save();
                Prefs.Reset();
                Prefs.Load();
                Check(!Prefs.Replace && !Prefs.Games && !Prefs.SameApp && Prefs.AllDesktops && Near(Prefs.Size, 200), "settings survive saving and loading");
                Check(!Prefs.Paused, "pause is not saved");
                File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json");
                Prefs.Reset();
                Prefs.Load();
                Check(Prefs.Replace && Prefs.Games && Near(Prefs.Size, Layout.DefaultPreview), "a broken settings file is ignored");
                File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"size\": 99999, \"games\": \"no\"}");
                Prefs.Load();
                Check(Near(Prefs.Size, Layout.MaxPreview) && Prefs.Games, "odd values in the file are brought into range or ignored");
            }
            finally
            {
                Prefs.Persist = false;
                Prefs.Reset();
                try { Directory.Delete(dir, true); } catch { }
            }

            // Перехватчик в проверках не ставится никогда
            Hook.Sync(true);
            Check(!Hook.Installed && !Hook.Allowed, "the keyboard hook is never installed by the self-test");

            // Без хаба: клиент ничего не бросает и остаётся «не на связи». Порт 9 никто не слушает.
            var link = new HubLink(Program.Id, Program.Name, Program.VersionText, 9);
            Check(!Throws(delegate { link.Start(); link.State(AppHub.Topic, AppHub.Settings()); link.Command("core", "show", null); Thread.Sleep(300); link.Stop(); }), "no hub — nothing throws");
            Check(!link.Online, "no hub — the app is simply offline");

            // Живая картинка окна: какую часть кадра копировать и когда пересоздавать пул (без видеокарты)
            int capW, capH;
            Check(!CaptureMath.Fit(640, 360, 640, 360, out capW, out capH) && capW == 640 && capH == 360, "capture: same size — copy the whole frame, keep the pool");
            Check(CaptureMath.Fit(500, 300, 640, 360, out capW, out capH) && capW == 500 && capH == 300, "capture: window shrank — copy only the content, recreate the pool");
            Check(CaptureMath.Fit(800, 600, 640, 360, out capW, out capH) && capW == 640 && capH == 360, "capture: window grew — copy what the pool holds, recreate the pool");
            Check(CaptureMath.Fit(800, 200, 640, 360, out capW, out capH) && capW == 640 && capH == 200, "capture: wider but lower — each side is clamped on its own");
            Check(!CaptureMath.Fit(0, 0, 640, 360, out capW, out capH) && capW == 0 && capH == 0 && !CaptureMath.Fit(640, 360, 0, 0, out capW, out capH) && capW == 0, "capture: empty frame or pool — nothing to copy, nothing to recreate");
            Check(CaptureMath.Near(System.Windows.Media.Color.FromRgb(250, 160, 12), Tokens.Palette.Orange, 12) && !CaptureMath.Near(Tokens.Palette.Blue, Tokens.Palette.Orange, 12), "capture: colour check has a tolerance but tells colours apart");

            // ── «Стопка»: раскладка по глубине ──
            double frontW, frontH;
            Flip.Front(16.0 / 9, 1600, 900, out frontW, out frontH);
            Check(frontW <= 1600 * Flip.FrontW + 1e-9 && frontH <= 900 * Flip.FrontH + 1e-9 && Math.Abs(frontW / frontH - 16.0 / 9) < 1e-9 && (Near(frontW, 1600 * Flip.FrontW) || Near(frontH, 900 * Flip.FrontH)), "flip: the front card fits its share of the screen, keeping the window's proportions");
            Flip.Front(0.5, 1600, 900, out frontW, out frontH);
            Check(Near(frontH, 900 * Flip.FrontH) && Near(frontW, frontH * 0.5), "flip: a tall window is fitted by height");
            Flip.Front(double.NaN, 1600, 900, out frontW, out frontH);
            Check(Math.Abs(frontW / frontH - Flip.DefaultAspect) < 1e-9, "flip: an unknown size gets the default proportions");
            Flip.Front(16.0 / 9, 1600, 900, out frontW, out frontH);
            Pose front = Flip.At(0, 9, frontW, frontH, 1600, 900);
            Check(Near(front.Opacity, 1) && Near(front.Z, 0) && Near(front.W, frontW) && Near(front.H, frontH) && front.Angle != 0, "flip: depth 0 is in front, fully opaque, at full size, turned like the rest");
            bool deeper = true, fading = true;
            for (double d = 0; d < Flip.Seen; d += 0.25)
            {
                Pose a = Flip.At(d, 20, frontW, frontH, 1600, 900), b = Flip.At(d + 0.25, 20, frontW, frontH, 1600, 900);
                if (!(b.Z < a.Z && b.X < a.X && b.Y > a.Y && b.Angle == a.Angle)) deeper = false;
                if (b.Opacity > a.Opacity + 1e-9) fading = false;
            }
            Check(deeper, "flip: further in the stack is deeper, to the left and up, at the same angle");
            Check(fading && Near(Flip.At(1, 20, frontW, frontH, 1600, 900).Opacity, 1) && Near(Flip.At(Flip.Seen - 0.5, 20, frontW, frontH, 1600, 900).Opacity, 0) && Flip.At(Flip.Seen - 1.5, 20, frontW, frontH, 1600, 900).Opacity > 0, "flip: cards fade towards the limit of sight and are gone at it");
            Check(Near(Flip.Fade(-0.05, 9), 1), "flip: the spring's small overshoot past the front does not make the front card see-through");
            Check(Flip.Fade(-0.25, 9) > 0 && Flip.Fade(-0.25, 9) < 1 && Near(Flip.Fade(-0.5, 9), 0) && Flip.At(-0.5, 9, frontW, frontH, 1600, 900).Z > 0, "flip: a card leaving the front flies towards you and fades out");
            Check(Near(Flip.Fade(3.5, 4), 0) && Near(Flip.Fade(3, 4), 1) && Flip.Fade(3.25, 4) > 0 && Flip.Fade(3.25, 4) < 1, "flip: at the end of the circle a card appears out of nothing");
            Check(Near(Flip.Depth(2, 2, 5), 0) && Near(Flip.Depth(3, 2, 5), 1) && Near(Flip.Depth(1, 2, 5), 4) && Near(Flip.Depth(0, 4, 5), 1), "flip: depth goes round the circle");
            Check(Near(Flip.Depth(4, 4.3, 5), -0.3) && Near(Flip.Depth(4, 4.6, 5), 4.4) && Near(Flip.Depth(0, 4.6, 5), 0.4), "flip: past half a step in front a card moves to the back of the stack");
            bool inRange = true;
            for (double p = -7; p < 7; p += 0.13) for (int i = 0; i < 5; i++) { double d = Flip.Depth(i, p, 5); if (d < -0.5 - 1e-9 || d >= 4.5) inRange = false; }
            Check(inRange && Near(Flip.Depth(0, 0, 1), 0), "flip: depth always stays within [−0.5; n−0.5)");
            Check(Near(Flip.Nearest(4, 0, 5), 5) && Near(Flip.Nearest(0, 4, 5), -1) && Near(Flip.Nearest(2, 3, 5), 3) && Near(Flip.Nearest(9, 0, 5), 10), "flip: the selection takes the short way round (last → first goes forward, first → last goes back)");
            Check(Near(Flip.Depth(0, Flip.Nearest(4, 0, 5), 5), 0) && Near(Flip.Depth(4, Flip.Nearest(0, 4, 5), 5), 0), "flip: …and arrives with the chosen window in front");

            // Дом карточки: проекция плоской карточки совпадает с прямоугольником окна на экране, при любом масштабе
            foreach (double sc in new double[] { 1, 1.25, 1.5 })
            {
                double monL = -1920, monT = 120, sw = 2560 / sc, sh = 1440 / sc;   // монитор слева от основного, в физических пикселях
                double l = -1700, t = 300, r = -900, b = 900;
                Pose home = Flip.Home(l, t, r, b, monL, monT, sc, sw, sh);
                double x0, y0, x1, y1;
                Flip.Project(home.X - home.W / 2, home.Y + home.H / 2, home.Z, sw, sh, out x0, out y0);
                Flip.Project(home.X + home.W / 2, home.Y - home.H / 2, home.Z, sw, sh, out x1, out y1);
                bool same = Math.Abs(x0 * sc + monL - l) < 1e-6 && Math.Abs(y0 * sc + monT - t) < 1e-6 && Math.Abs(x1 * sc + monL - r) < 1e-6 && Math.Abs(y1 * sc + monT - b) < 1e-6;
                Check(same && Near(home.Angle, 0) && Near(home.Opacity, 1), "flip: the home pose projects exactly onto the window at scale " + sc);
                // Та же поза, отодвинутая вглубь (Flip.Lift), на экране — ровно там же
                Pose deep = Flip.Lift(home, -5 * Flip.LiftStep * sw, sw);
                double lx0, ly0, lx1, ly1;
                Flip.Project(deep.X - deep.W / 2, deep.Y + deep.H / 2, deep.Z, sw, sh, out lx0, out ly0);
                Flip.Project(deep.X + deep.W / 2, deep.Y - deep.H / 2, deep.Z, sw, sh, out lx1, out ly1);
                Check(Math.Abs(lx0 - x0) < 1e-6 && Math.Abs(ly0 - y0) < 1e-6 && Math.Abs(lx1 - x1) < 1e-6 && Math.Abs(ly1 - y1) < 1e-6 && deep.Z < 0 && deep.W > home.W,
                    "flip: a home pose pushed deeper (lower windows) still projects exactly onto the window at scale " + sc);
            }
            double px, py;
            Flip.Project(100, 50, -Flip.Distance(1600), 1600, 900, out px, out py);
            Check(Near(px, 850) && Near(py, 425), "flip: twice as far is half as large (perspective)");
            // ── Свёрнутое окно: где оно встанет после разворачивания и как карточка доходит до настоящего места ──
            {
                int bl, bt, br, bb;
                Restore.Borders(14, 7, out bl, out bt, out br, out bb);
                bool usual = bl == 7 && bt == 0 && br == 7 && bb == 7;
                Restore.Borders(14, 14, out bl, out bt, out br, out bb);
                bool round = bl == 7 && bt == 7 && br == 7 && bb == 7;
                Restore.Borders(15, 7, out bl, out bt, out br, out bb);
                bool odd = bl + br == 15 && bt == 0 && bb == 7;
                Restore.Borders(0, 0, out bl, out bt, out br, out bb);
                Check(usual && round && odd && bl == 0 && bt == 0 && br == 0 && bb == 0, "restore: invisible borders are split left/right, the top gets only what the bottom cannot take");
                var monR = new Win.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
                var workTop = new Win.RECT { Left = 0, Top = 48, Right = 1920, Bottom = 1080 };   // панель задач сверху
                var normal = new Win.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 };
                Win.RECT v;
                bool okWs = Restore.Place(normal, false, false, monR, workTop, 786, 593, out v) && v.Left == 107 && v.Top == 148 && v.Right == 893 && v.Bottom == 741;
                bool okTool = Restore.Place(normal, true, false, monR, workTop, 786, 593, out v) && v.Left == 107 && v.Top == 100 && v.Right == 893 && v.Bottom == 693;
                Check(okWs && okTool, "restore: the normal place is shifted from workspace to screen coordinates (not for tool windows) and loses its invisible borders");
                bool okMax = Restore.Place(normal, false, true, monR, workTop, 786, 593, out v) && v.Left == 0 && v.Top == 48 && v.Right == 1920 && v.Bottom == 1080;
                Check(okMax, "restore: a window that restores maximized takes the work area of its monitor");
                bool noThumb = Restore.Place(normal, true, false, monR, workTop, 0, 0, out v) && v.Left == 100 && v.Right == 900 && v.Bottom == 700;
                bool bigThumb = Restore.Place(normal, true, false, monR, workTop, 1600, 593, out v) && v.Left == 100 && v.Right == 900;
                bool farThumb = Restore.Place(normal, true, false, monR, workTop, 400, 300, out v) && v.Left == 100 && v.Right == 900;
                Check(noThumb && bigThumb && farThumb, "restore: without a believable thumbnail size the borders are taken as zero");
                Check(!Restore.Place(new Win.RECT { Left = 10, Top = 10, Right = 12, Bottom = 400 }, true, false, monR, workTop, 0, 0, out v), "restore: an empty place is no place");
                Check(Restore.Miss(normal, normal) == 0 && Restore.Miss(normal, new Win.RECT { Left = 103, Top = 100, Right = 890, Bottom = 700 }) == 10, "restore: the miss is the largest side difference");

                // Доводка: с нуля, плавно, кончается к концу ухода или не быстрее короткого движения
                double L = Flip.LeaveMs;
                Check(Near(Restore.Correct(100, 100, L), 0) && Near(Restore.Correct(L, 100, L), 1) && Near(Restore.CorrectEnd(20, L), L)
                    && Near(Restore.CorrectEnd(L - 10, L), L - 10 + Tokens.Motion.FastMs) && Near(Restore.CorrectEnd(L + 100, L), L + 100 + Tokens.Motion.FastMs),
                    "restore: the correction starts at zero when the real place is known and ends with the flight, never faster than a short motion");
                // Полёт к месту, которое сменилось посреди пути, — без скачка: шаг за кадр не больше, чем у полёта без смены
                double fromX = 0, guessX = 1000, realX = 1040, prev = fromX, worst = 0, worstPlain = 0, prevPlain = fromX;
                for (double e = 1000 / 60.0; e <= L + 1e-9; e += 1000 / 60.0)
                {
                    double g = Flip.Glide(Flip.Share(e, 0, L));
                    double to = e < 100 ? guessX : guessX + (realX - guessX) * Restore.Correct(e, 100, L);
                    double x = fromX + (to - fromX) * g, plain = fromX + (realX - fromX) * g;
                    worst = Math.Max(worst, Math.Abs(x - prev)); worstPlain = Math.Max(worstPlain, Math.Abs(plain - prevPlain));
                    prev = x; prevPlain = plain;
                }
                double endX = fromX + (guessX + (realX - guessX) * Restore.Correct(L, 100, L) - fromX) * Flip.Glide(1);
                Check(Near(endX, realX) && worst <= worstPlain * 1.02, "restore: a card whose place changes mid-flight lands on the real place without a jump");
                // Когда сцену можно убирать
                double A = Restore.AnimMs, W = Restore.WaitMs;
                Check(!Restore.Ready(L - 1, L, 20, false, A) && Restore.Ready(L, L, 20, false, A) && !Restore.Ready(L, L, -1, false, A) && !Restore.Ready(L + W - 1, L, -1, false, A)
                    && Restore.Ready(L + W, L, -1, false, A), "restore: the scene waits for the window to restore, but no longer than the limit");
                Check(!Restore.Ready(200 + A - 1, L, 200, false, A) && Restore.Ready(200 + A, L, 200, false, A) && Restore.Ready(L, L, 200, false, 0),
                    "restore: the scene covers the window's restore animation to its end");
                Check(!Restore.Ready(Restore.CorrectEnd(L - 10, L) - 1, L, L - 10, true, 0) && Restore.Ready(Restore.CorrectEnd(L - 10, L), L, L - 10, true, 0),
                    "restore: the scene stays until the card has reached the real place");
            }
            Pose mid = Flip.Lerp(Flip.Home(0, 0, 100, 100, 0, 0, 1, 1600, 900), front, 0.5);
            Check(Near(mid.Opacity, 1) && Near(mid.Angle, front.Angle / 2) && Near(Flip.Lerp(front, front, 0.3).X, front.X), "flip: poses blend in between");

            // ── Появление и уход по времени: одна длительность на любой частоте, подвисший кадр не съедает полёт ──
            Check(Flip.OpenMs >= 450 && Flip.OpenMs <= 500 && Flip.LeaveMs >= 400 && Flip.LeaveMs <= 450 && Flip.FadeMs < Flip.LeaveMs,
                "flip: the flight out takes 450–500 ms, the way home 400–450 ms, the reduced-motion fade is shorter, got " + Flip.OpenMs + " / " + Flip.LeaveMs + " / " + Flip.FadeMs);
            foreach (int ms in new int[] { Flip.OpenMs, Flip.LeaveMs })
            {
                double t60 = 0, t240 = 0, e = 0;
                int f60 = 0, f240 = 0;
                for (e = 0; Flip.Share(e, 0, ms) < 1 && f60 < 1000; f60++) { e = Flip.Tick(e, 1.0 / 60); t60 += 1.0 / 60; }
                for (e = 0; Flip.Share(e, 0, ms) < 1 && f240 < 1000; f240++) { e = Flip.Tick(e, 1.0 / 240); t240 += 1.0 / 240; }
                Check(Math.Abs(t60 - t240) <= 1.0 / 60 + 1e-9 && Math.Abs(t240 - ms / 1000.0) <= 1.0 / 240 + 1e-9,
                    "flip: a " + ms + " ms motion lasts the same at 60 and 240 Hz, got " + (t60 * 1000).ToString("0") + " and " + (t240 * 1000).ToString("0") + " ms");
            }
            Check(Near(Flip.Share(0, 0, Flip.OpenMs), 0) && Near(Flip.Glide(0), 0) && Near(Flip.Glide(Flip.Share(Flip.Tick(0, 0), 0, Flip.OpenMs)), 0),
                "flip: on the first visible frame nothing has moved yet");
            Check(Near(Flip.Glide(1), 1) && Near(Flip.Glide(2), 1) && Near(Flip.Glide(-1), 0) && Near(Flip.Share(5000, 0, Flip.OpenMs), 1) && Near(Flip.Share(-5, 0, Flip.OpenMs), 0),
                "flip: motions end exactly at their place");
            {
                // Кривая: монотонна, без перелёта, скорость в начале и в конце — ноль, наибольшая — на 10–25% времени
                bool mono = true, inside = true;
                double vMax = 0, vAt = 0;
                for (int i = 0; i < 1000; i++)
                {
                    double a = Flip.Glide(i / 1000.0), b = Flip.Glide((i + 1) / 1000.0);
                    if (b < a - 1e-12) mono = false;
                    if (b < -1e-12 || b > 1 + 1e-12) inside = false;
                    if ((b - a) * 1000 > vMax) { vMax = (b - a) * 1000; vAt = i / 1000.0; }
                }
                Check(mono && inside, "flip: the curve only goes forward and never overshoots");
                double v0 = Flip.Glide(0.001) / 0.001, v1 = (1 - Flip.Glide(0.999)) / 0.001;
                Check(v0 < 0.05 && v1 < 0.05, "flip: the curve starts and ends at zero speed (no jerk off the mark, no bump on landing), got " + v0.ToString("0.000") + " / " + v1.ToString("0.000"));
                Check(vAt >= 0.10 && vAt <= 0.25 && vMax < 3.5, "flip: the curve speeds up over the first 10–25% and peaks below 3.5× the average speed, got " + (vAt * 100).ToString("0") + "% / " + vMax.ToString("0.00"));
                Check(Flip.Glide(0.05) < 0.03 && Flip.Glide(0.5) > 0.8 && Flip.Glide(0.9) > 0.99,
                    "flip: little motion in the first 5%, most of the way by half time, a long soft settle, got " + Flip.Glide(0.05).ToString("0.000") + " / " + Flip.Glide(0.5).ToString("0.000"));
                // Первый кадр движения на 180 Гц сдвигает меньше чем на 0,2% пути
                double firstStep = Flip.Glide(Flip.Share(Flip.Tick(0, 1.0 / 180), 0, Flip.OpenMs));
                Check(firstStep < 0.002, "flip: the first frame of motion at 180 Hz moves less than 0.2% of the way, got " + (firstStep * 100).ToString("0.000") + "%");
            }
            // Отложенный старт: первый видимый кадр — стоим; опоздавший кадр — стоим; первый вовремя — идём; не дольше HoldMs
            {
                double typ = 1000.0 / 180;
                Check(!Flip.Go(0, typ, typ) && !Flip.Go(0, 40, typ) && !Flip.Go(-1, typ, typ) && !Flip.Go(double.NaN, typ, typ),
                    "flip: the first visible frame never starts the motion");
                Check(!Flip.Go(39, 39, typ) && !Flip.Go(17, 17, typ) && Flip.Go(44, typ, typ) && Flip.Go(23, typ * 1.4, typ),
                    "flip: a late frame right after showing keeps the scene still; the first frame on time starts the motion");
                Check(Flip.Go(Flip.HoldMs, 50, typ) && Flip.Go(Flip.HoldMs + 30, 40, typ) && Flip.HoldMs >= 40 && Flip.HoldMs <= 80,
                    "flip: the scene waits no longer than " + Flip.HoldMs + " ms after the first visible frame, even if frames stay late");
                Check(!Flip.Go(10, -1, typ) && Flip.Go(10, 1.0, 0.5) == true,
                    "flip: no previous frame — no start; a very short usual step still counts as on time");
            }
            Check(Near(Flip.Blend(1, 1, 0.3), 1) && Near(Flip.Blend(0, 1, 0), 1) && Near(Flip.Blend(0.5, 1, 1), 0.5) && Near(Flip.Blend(0.5, 1, 0.5), 0.75) && Near(Flip.Blend(1, 0, 0.01), 0) && Near(Flip.Blend(1, 0.3, 0.01), 0.3),
                "flip: a card flying home→stack is fully visible at home, ends at its stack opacity and fades out towards a seam of the circle");
            {
                bool whole = true, seamless = true;
                foreach (string v in Flip.Views)
                    for (int n = 1; n <= 12; n++)
                    {
                        for (int i = 0; i < n; i++) if (!Near(Flip.Edge(v, i, n), 1)) whole = false;
                        // Там, где место карточки перескакивает с одного конца раскладки на другой (по глубине или по стороне),
                        // её не видно ни до, ни после
                        if (n < 2) continue;
                        for (double p = 0; p < n; p += 0.001)
                        {
                            double a = Flip.Depth(0, p, n), b = Flip.Depth(0, p + 0.001, n);
                            bool jump = Flip.Symmetric(v) ? Math.Abs(Flip.Signed(b, n) - Flip.Signed(a, n)) > 1 : Math.Abs(b - a) > 1;
                            if (jump && (Flip.Edge(v, a, n) > 0.01 || Flip.Edge(v, b, n) > 0.01)) seamless = false;
                        }
                    }
                Check(whole && seamless, "flip: cards in their places are not dimmed by the seam; at the seam of the circle a card is invisible on both sides");
            }
            Check(Near(Flip.StartPos(1, 9), 0) && Near(Flip.StartPos(8, 9), 0) && Near(Flip.StartPos(4, 9), 4) && Near(Flip.StartPos(0, 9), 0) && Near(Flip.StartPos(0, 1), 0) && Near(Flip.StartPos(1, 2), 0)
                && Near(Flip.Nearest(Flip.StartPos(1, 9), 1, 9), 1) && Near(Flip.Nearest(Flip.StartPos(8, 9), 8, 9), -1),
                "flip: the stack opens with the current window in front and the selection one step away from it");
            Check(Near(Flip.Tick(0, 5), Flip.MaxDt * 1000) && Near(Flip.Tick(100, 0.5), 100 + Flip.MaxDt * 1000) && Near(Flip.Tick(100, -1), 100) && Near(Flip.Tick(100, double.NaN), 100),
                "flip: a stalled frame counts for at most " + (Flip.MaxDt * 1000).ToString("0") + " ms, time never runs back");
            int late = 0;
            for (double x = 0; Flip.Share(x, 0, Flip.OpenMs) < 1 && late < 1000; late++) x = Flip.Tick(x, 0.5);
            Check(late >= (int)Math.Floor(Flip.OpenMs / (Flip.MaxDt * 1000)), "flip: even when every frame is late the flight is spread over at least " + Math.Floor(Flip.OpenMs / (Flip.MaxDt * 1000)) + " frames, got " + late);
            bool stairs = true, gradual = true, together = true;
            foreach (string v in Flip.Views)
                for (int n = 1; n <= 30; n++)
                {
                    if (!Near(Flip.Delay(v, 0, n), 0)) stairs = false;
                    for (double d = -0.5; d < n - 0.5; d += 0.25)
                    {
                        double wait = Flip.Delay(v, d, n);
                        if (wait < 0 || wait > Flip.LagMs + 1e-9) stairs = false;
                        if (Math.Abs(Flip.Delay(v, d + 0.25, n) - wait) > Flip.LagMs * 0.25 + 1e-9 && d + 0.25 < n - 0.5 && Math.Abs(Flip.Signed(d + 0.25, n) - Flip.Signed(d, n)) < 1) gradual = false;
                        if (!Near(wait + Flip.Flight(wait), Flip.OpenMs)) together = false;
                    }
                }
            Check(stairs && Flip.LagMs >= 30 && Flip.LagMs <= 90, "flip: the first card in the layout starts at once, the rest within " + Flip.LagMs + " ms after it");
            Check(gradual && Flip.Delay("flip", 1, 9) > 0 && Flip.Delay("flip", 2, 9) > Flip.Delay("flip", 1, 9) && Flip.Delay("flip", 3, 9) > Flip.Delay("flip", 2, 9)
                && Flip.Delay("flip", 2, 9) - Flip.Delay("flip", 1, 9) < Flip.Delay("flip", 1, 9) && Flip.Delay("flip", 8, 9) < Flip.LagMs,
                "flip: the start delay grows smoothly with depth, in ever smaller steps (no stairs), got " + Flip.Delay("flip", 1, 9).ToString("0") + "/" + Flip.Delay("flip", 2, 9).ToString("0") + "/" + Flip.Delay("flip", 3, 9).ToString("0") + " ms");
            Check(together && Near(Flip.Delay("cover", 1, 9), Flip.Delay("cover", 8, 9)) && Flip.Delay("cover", 1, 9) > 0,
                "flip: every card lands when the opening ends; in side-by-side layouts both neighbours start together");
            Check(Flip.MaxLive == 24, "flip: up to 24 windows get a live picture at once, got " + Flip.MaxLive);

            // ── Шаг кадров: время показа кадра, повторный вызов Rendering, первый шаг движения ──
            {
                double lastShown = -1, pdt;
                bool f1 = Pace.Next(1000, ref lastShown, out pdt);
                double pdt1 = pdt;
                bool f2 = Pace.Next(1000 + 1000.0 / 180, ref lastShown, out pdt);
                double pdt2 = pdt;
                bool f3 = Pace.Next(1000 + 1000.0 / 180, ref lastShown, out pdt);
                double pdt3 = pdt, kept = lastShown;
                bool f4 = Pace.Next(1000, ref lastShown, out pdt);
                Check(f1 && Near(pdt1, 0) && f2 && Math.Abs(pdt2 - 1.0 / 180) < 1e-9 && !f3 && Near(pdt3, 0) && !f4 && Near(lastShown, kept) && !Pace.Next(double.NaN, ref lastShown, out pdt),
                    "pace: the step comes from the time the frame is shown; a repeated Rendering call with the same time is not a frame, time never runs back");
                // Позиция зависит только от времени показа: неровные вызовы обработчика при ровном показе дают ровный шаг
                double a = -1, b = -1, ea = 0, eb = 0, d;
                for (int i = 1; i <= 60; i++)
                {
                    double show = i * 1000.0 / 180;
                    if (Pace.Next(show, ref a, out d)) ea = Flip.Tick(ea, d);
                    if (Pace.Next(show, ref b, out d)) eb = Flip.Tick(eb, d);
                    if (i % 3 == 0 && Pace.Next(show, ref b, out d)) eb = Flip.Tick(eb, d);   // лишний вызов с тем же временем
                }
                Check(Near(ea, eb) && Math.Abs(ea - 59 * 1000.0 / 180) < 1e-6, "pace: extra Rendering calls do not speed the motion up, got " + ea.ToString("0.00") + " and " + eb.ToString("0.00") + " ms");
                double typ = Pace.MaxStep;
                for (int i = 0; i < 10; i++) typ = Pace.Typical(typ, 1.0 / 180);
                double typBefore = typ;
                typ = Pace.Typical(typ, 0.050);
                Check(Math.Abs(typBefore - 1.0 / 180) < 1e-9 && typ < 1.0 / 180 + 0.001 && Pace.Typical(typ, 1.0 / 240) <= 1.0 / 240 + 1e-12
                    && Pace.Typical(double.NaN, 0.004) <= 0.004 + 1e-12 && Pace.Typical(0.005, 5) <= Pace.MaxStep && Pace.Typical(0.005, -1) >= Pace.MinStep,
                    "pace: the usual frame step follows shorter frames at once and a single stalled frame barely moves it, got " + (typ * 1000).ToString("0.00") + " ms");
                Check(Near(Pace.Start(0.050, 1.0 / 180), 1.0 / 180) && Near(Pace.Start(0.004, 1.0 / 180), 0.004) && Near(Pace.Start(-1, 0.005), 0) && Near(Pace.Start(double.NaN, 0.005), 0),
                    "pace: the first step of a motion is no longer than a usual frame, a stalled first frame does not jump a third of the way");
                double jump = Flip.Glide(Flip.Share(Flip.Tick(0, Pace.Start(0.050, 1.0 / 180)), 0, Flip.OpenMs));
                Check(jump < 0.002, "pace: after a 50 ms stall right after the motion starts the cards move by under 0.2% of the way, got " + (jump * 100).ToString("0.00") + "%");

                // Прореживание живых картинок: в движении — не больше двух за кадр по кругу, все по очереди, невидимые — никогда,
                // без первого кадра — всегда; в покое — все видимые
                int n = 12, cursor = 0;
                bool[] vis = new bool[n], fresh = new bool[n], pick = new bool[n];
                for (int i = 0; i < n; i++) vis[i] = i != 5;
                fresh[9] = true;
                int[] got = new int[n];
                bool budget = true;
                for (int f = 0; f < 30; f++)
                {
                    Pace.LivePick(vis, fresh, n, true, ref cursor, pick);
                    int spent = 0;
                    for (int i = 0; i < n; i++) if (pick[i]) { got[i]++; if (!fresh[i]) spent++; }
                    if (spent > Pace.LiveMoving) budget = false;
                }
                bool fair = true;
                for (int i = 0; i < n; i++) if (vis[i] && !fresh[i] && (got[i] < 4 || got[i] > 7)) fair = false;
                Check(budget && fair && got[5] == 0 && got[9] == 30, "pace: while moving at most " + Pace.LiveMoving + " live pictures update per frame, in turn; hidden ones never, ones without a first frame always");
                Pace.LivePick(vis, fresh, n, false, ref cursor, pick);
                bool all = true;
                for (int i = 0; i < n; i++) if (pick[i] != vis[i]) all = false;
                int c0 = 0;
                Pace.LivePick(vis, fresh, 0, true, ref c0, pick);
                Check(all && c0 == 0, "pace: at rest every visible live picture updates; no cards — nothing to do");

                // Распределение промежутков
                var g = new Gaps(8);
                foreach (double v in new double[] { 5.6, 5.6, 5.5, 11.1, 5.6, 16.7, 5.6, 5.6, 5.6, 33 }) g.Add(v);
                g.Add(-1); g.Add(double.NaN);
                Check(g.Count == 8 && Near(g.Worst, 33) && Near(g.Within(Gaps.Monitor), 6 / 8.0) && g.Over(Gaps.Long) == 1 && Near(g.Percentile(50), 5.6) && Near(g.Percentile(100), 16.7)
                    && Near(new Gaps(4).Within(6), 1) && Near(new Gaps(4).Percentile(95), 0),
                    "pace: frame-gap shares and percentiles are counted right, without growing past capacity; the worst one is kept");
            }
            double bx, by, bw, bh;
            Check(Flip.CropBox(1720, 0, 3440, 1440, 0, 0, 3440, 1440, 3440, 1440, out bx, out by, out bw, out bh) && Near(bx, 1720) && Near(by, 0) && Near(bw, 1720) && Near(bh, 1440),
                "flip: a window's place in the monitor frame, same size");
            Check(Flip.CropBox(2020, 100, 2420, 400, 1920, 0, 3840, 1080, 960, 540, out bx, out by, out bw, out bh) && Near(bx, 50) && Near(by, 50) && Near(bw, 200) && Near(bh, 150),
                "flip: a window's place in the frame of a second monitor, frame at half size");
            Check(!Flip.CropBox(0, 0, 100, 100, 0, 0, 0, 0, 100, 100, out bx, out by, out bw, out bh) && !Flip.CropBox(0, 0, 100, 100, 0, 0, 100, 100, 0, 0, out bx, out by, out bw, out bh)
                && !Flip.CropBox(50, 0, 50, 100, 0, 0, 100, 100, 100, 100, out bx, out by, out bw, out bh), "flip: no crop from an empty monitor, frame or window");

            // ── Картинка свёрнутого окна (Iconic.cs): размер вспомогательного окна, годен ли кадр, память ──
            int sw2, sh2;
            Check(Still.Fit(800, 450, 1920, 1080, out sw2, out sh2) && sw2 == 800 && sh2 == 450, "still: a small window keeps its size, no upscaling");
            Check(Still.Fit(2560, 1440, 3840, 2160, out sw2, out sh2) && sw2 == 1280 && sh2 == 720, "still: a big window is cut to 1280 on the long side, got " + sw2 + "x" + sh2);
            Check(Still.Fit(1000, 2000, 3840, 2160, out sw2, out sh2) && sw2 == 640 && sh2 == 1280, "still: a tall window is cut by height, got " + sw2 + "x" + sh2);
            Check(Still.Fit(2467, 1006, 1024, 768, out sw2, out sh2) && sw2 <= 1024 && sh2 <= 768 && Math.Abs(sw2 / (double)sh2 - 2467 / 1006.0) < 0.01, "still: never wider than the monitor, aspect kept, got " + sw2 + "x" + sh2);
            Check(!Still.Fit(160, 28, 1920, 1080, out sw2, out sh2) && sw2 == 0 && sh2 == 0, "still: the 160x28 caption strip of a minimized window is no picture");
            Check(!Still.Fit(0, 0, 1920, 1080, out sw2, out sh2) && !Still.Fit(800, 600, 0, 0, out sw2, out sh2), "still: no size, no monitor — no helper window");
            var flat = new byte[16 * 4];
            for (int i = 0; i < flat.Length; i += 4) { flat[i] = 30; flat[i + 1] = 30; flat[i + 2] = 30; flat[i + 3] = 255; }
            Check(Still.Flat(flat, Still.Tolerance), "still: an even fill is not a picture yet");
            flat[20] = 32;
            Check(Still.Flat(flat, Still.Tolerance), "still: noise within the tolerance is still an even fill");
            flat[20] = 200;
            Check(!Still.Flat(flat, Still.Tolerance), "still: one different point makes it a picture");
            Check(Still.Flat(null, 3) && Still.Flat(new byte[0], 3), "still: no points — no picture");
            var cache = new StillCache<string>(3, 100);
            IntPtr k1 = new IntPtr(1), k2 = new IntPtr(2), k3 = new IntPtr(3), k4 = new IntPtr(4);
            cache.Put(k1, "a", 10); cache.Put(k2, "b", 10); cache.Put(k3, "c", 10);
            Check(cache.Count == 3 && cache.Bytes == 30 && cache.Get(k1) == "a", "still cache: three fit");
            cache.Put(k4, "d", 10);   // k2 — самая давняя (k1 только что взята)
            Check(cache.Count == 3 && cache.Get(k2) == null && cache.Get(k1) == "a" && cache.Get(k4) == "d", "still cache: over the count the least recently used goes");
            cache.Put(k3, "c2", 70);  // замена: 10 + 10 + 70 = 90
            Check(cache.Count == 3 && cache.Bytes == 90 && cache.Get(k3) == "c2", "still cache: a new picture of the same window replaces the old one");
            cache.Put(k2, "b2", 30);  // 90 + 30 > 100: вытесняются давние, пока не влезет
            Check(cache.Bytes <= 100 && cache.Get(k2) == "b2" && cache.Get(k1) == null, "still cache: over the memory limit the oldest go, got " + cache.Bytes + " bytes");
            Check(!cache.Put(k1, "huge", 101) && cache.Get(k1) == null && cache.Bytes <= 100, "still cache: a picture bigger than the whole limit is not kept");
            cache.Keep(delegate(IntPtr key) { return key == k2; });
            Check(cache.Count == 1 && cache.Get(k2) == "b2" && cache.Bytes == 30, "still cache: windows gone or restored are forgotten");
            cache.Forget(k2); cache.Forget(k2);
            Check(cache.Count == 0 && cache.Bytes == 0, "still cache: forgetting twice is harmless");
            Check(Still.CacheCount >= 3 && Still.CacheBytes >= (long)Still.MaxSide * Still.MaxSide * 4, "still: the cache holds at least one picture of the largest size");

            // ── Остальные объёмные раскладки ──
            bool flipSame = true;
            for (double d = -0.5; d < 8.5; d += 0.1)
            {
                Pose a = Flip.At(d, 9, frontW, frontH, 1600, 900), b = Flip.Place("flip", d, 9, frontW, frontH, 1600, 900);
                if (!(a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.Angle == b.Angle && a.Opacity == b.Opacity && b.Pitch == 0 && b.Roll == 0)) flipSame = false;
            }
            Check(flipSame, "flip: the stack placed through Flip.Place is exactly the old stack");
            Check(Near(Flip.Signed(0, 5), 0) && Near(Flip.Signed(4, 5), -1) && Near(Flip.Signed(2, 5), 2) && Near(Flip.Signed(3, 5), -2) && Near(Flip.Signed(2, 4), 2) && Near(Flip.Signed(3, 4), -1) && Near(Flip.Signed(1, 2), 1),
                "layouts: the signed distance goes both ways round the circle; with an even count the seam falls between windows");
            Pose sum = Flip.Lerp(new Pose { Pitch = 10, Roll = -20 }, new Pose { Pitch = 30, Roll = 20 }, 0.5);
            Check(Near(sum.Pitch, 20) && Near(sum.Roll, 0) && Near(Flip.Home(0, 0, 100, 100, 0, 0, 1, 1600, 900).Pitch, 0) && Near(Flip.Home(0, 0, 100, 100, 0, 0, 1, 1600, 900).Roll, 0), "layouts: tilt and roll blend too, and are flat at home");
            double cx, cy, cz;
            Flip.Corner(new Pose { W = 100, H = 100, Angle = -32 }, -0.5, 0, 1600, 900, out cx, out cy, out cz);
            Check(cz < 0, "layouts: the corner maths turns the same way as the scene (negative angle — left edge further)");
            foreach (string v in Flip.Views)
            {
                if (v == "flip") continue;
                bool sym = Flip.Symmetric(v);
                foreach (double[] scr in new double[][] { new double[] { 1600, 900 }, new double[] { 3440, 1440 } })
                {
                    double sw = scr[0], sh = scr[1];
                    foreach (int n in new int[] { 1, 2, 3, 4, 9, 12, 20 })
                    {
                        double lw, lh;
                        Flip.Front(v, 16.0 / 9, sw, sh, out lw, out lh);
                        Pose sel = Flip.Place(v, 0, n, lw, lh, sw, sh);
                        double l, t, r, b;
                        Flip.Screen(sel, sw, sh, out l, out t, out r, out b);
                        bool inside = l >= 0 && t >= 0 && r <= sw && b <= sh;
                        bool clear = true;
                        for (int i = 1; i < n; i++)
                        {
                            Pose o = Flip.Place(v, i, n, lw, lh, sw, sh);
                            if (o.Opacity < 0.01 || o.Z < sel.Z - 0.5) continue;
                            double ol, ot, orr, ob;
                            Flip.Screen(o, sw, sh, out ol, out ot, out orr, out ob);
                            if (ol < r && orr > l && ot < b && ob > t) clear = false;
                        }
                        Check(Near(sel.Opacity, 1) && inside && clear, v + ": the chosen window is fully opaque, whole on a " + sw + "×" + sh + " screen and nothing nearer covers it (" + n + " windows)");
                    }
                    // Непрерывность: малый шаг — малое смещение, пока карточка видна; на шве круга — прозрачна
                    foreach (int n in new int[] { 4, 7, 12 })
                    {
                        double lw, lh;
                        Flip.Front(v, 1.6, sw, sh, out lw, out lh);
                        bool smooth = true, seamClear = true, facing = true;
                        Pose prev = Flip.Place(v, -0.5, n, lw, lh, sw, sh);
                        double prevS = Flip.Signed(-0.5, n);
                        for (double d = -0.49; d < n - 0.5; d += 0.01)
                        {
                            Pose p = Flip.Place(v, d, n, lw, lh, sw, sh);
                            if (p.Opacity > 0.01 && Winding(p, sw, sh) <= 0) facing = false;
                            double sd = sym ? Flip.Signed(d, n) : d;
                            if (Math.Abs(sd - prevS) > 0.5) { if (p.Opacity > 0.05 || prev.Opacity > 0.05) seamClear = false; }
                            else if (p.Opacity > 0.001 && prev.Opacity > 0.001)
                            {
                                double move = Math.Max(Math.Max(Math.Abs(p.X - prev.X), Math.Abs(p.Y - prev.Y)), Math.Abs(p.Z - prev.Z));
                                double turn = Math.Max(Math.Max(Math.Abs(p.Angle - prev.Angle), Math.Abs(p.Pitch - prev.Pitch)), Math.Abs(p.Roll - prev.Roll));
                                if (move > 0.02 * sw || turn > 3 || Math.Abs(p.Opacity - prev.Opacity) > 0.05) smooth = false;
                            }
                            prev = p; prevS = sd;
                        }
                        double seamAt = sym ? (n % 2 == 0 ? 0.5 : 0) + n / 2.0 : n - 0.5;
                        if (sym && Flip.Place(v, seamAt, n, lw, lh, sw, sh).Opacity > 1e-9) seamClear = false;
                        Check(facing, v + ": a visible card never shows its back (WPF does not draw it) (" + n + " windows, " + sw + "×" + sh + ")");
                        Check(smooth, v + ": poses change smoothly while the card is visible (" + n + " windows, " + sw + "×" + sh + ")");
                        Check(seamClear, v + ": crossing the seam of the circle the card is invisible (" + n + " windows, " + sw + "×" + sh + ")");
                    }
                }
                if (sym)
                {
                    // Зеркальность: окно на s и на −s стоят зеркально (барабан — сверху и снизу, спираль — ещё и ступенькой вверх)
                    double lw, lh;
                    Flip.Front(v, 1.6, 1600, 900, out lw, out lh);
                    Pose c0 = Flip.Place(v, 0, 41, lw, lh, 1600, 900);
                    bool mirror = true;
                    for (double q = 0.25; q <= 6; q += 0.25)
                    {
                        Pose a = Flip.Place(v, q, 41, lw, lh, 1600, 900), b = Flip.Place(v, 41 - q, 41, lw, lh, 1600, 900);
                        bool ok = Near(a.Z, b.Z) && Near(a.Opacity, b.Opacity) && Near(a.W, b.W);
                        if (v == "rolodex") ok = ok && Near(a.X, b.X) && Near(a.Y - c0.Y, c0.Y - b.Y) && Near(a.Pitch, -b.Pitch) && Near(a.Angle, b.Angle);
                        else if (v == "helix") ok = ok && Near(a.X, -b.X) && Near(a.Angle, -b.Angle) && Near(a.Roll, -b.Roll) && a.Y > b.Y;
                        else ok = ok && Near(a.X, -b.X) && Near(a.Y, b.Y) && Near(a.Angle, -b.Angle) && Near(a.Roll, -b.Roll) && Near(a.Pitch, b.Pitch);
                        if (!ok) mirror = false;
                    }
                    Check(mirror, v + ": the layout is mirror-symmetric around the chosen window");
                    Check(Flip.Order(v, 1, 9) > Flip.Order(v, 2, 9) && Near(Flip.Order(v, 1, 9), Flip.Order(v, 8, 9)) && Flip.Order(v, 0, 9) > Flip.Order(v, 1, 9), v + ": nearer to the chosen window is drawn on top, the same on both sides");
                }
                else Check(Flip.Order(v, 0, 9) > Flip.Order(v, 1, 9) && Flip.Order(v, 1, 9) > Flip.Order(v, 2, 9), v + ": the front of the deck is drawn on top");
            }

            // Настройка «Вид переключателя»
            Prefs.Reset();
            s = AppHub.Settings();
            var viewItem = Item(s, "view");
            var options = viewItem == null ? null : viewItem["options"] as object[];
            string[] wantViews = { "grid", "flip", "cascade", "cover", "carousel", "fan", "panorama", "helix", "rolodex", "tunnel" };
            string[] wantLabels = { "Сетка", "Стопка", "Каскад", "Обложки", "Карусель", "Веер", "Панорама", "Спираль", "Барабан", "Туннель" };
            bool listed = viewItem != null && (string)viewItem["type"] == "choice" && (string)viewItem["value"] == "grid" && options != null && options.Length == 10;
            for (int i = 0; listed && i < 10; i++)
                listed = (string)((Dictionary<string, object>)options[i])["value"] == wantViews[i] && (string)((Dictionary<string, object>)options[i])["label"] == wantLabels[i];
            Check(listed, "«Вид переключателя» is a choice of ten views, from «Сетка» and «Стопка» to «Туннель», grid by default");
            var viewGroup = (Dictionary<string, object>)((object[])s["groups"])[1];
            Check((string)viewGroup["title"] == "Вид" && (string)((Dictionary<string, object>)((object[])viewGroup["items"])[0])["key"] == "view", "…the first item of the «Вид» group");
            AppHub.FlipAvailable = true;
            string hintOn = Item(AppHub.Settings(), "view")["hint"] as string;
            AppHub.FlipAvailable = false;
            string hintOff = Item(AppHub.Settings(), "view")["hint"] as string;
            AppHub.FlipAvailable = true;
            Check(hintOn == "Все виды, кроме сетки, — объёмные, с живыми окнами" && hintOff == "Объёмные виды на этом компьютере недоступны — будет сетка", "the hint says honestly whether the 3D views are available here");
            back = new JavaScriptSerializer().DeserializeObject(new JavaScriptSerializer().Serialize(s)) as Dictionary<string, object>;
            Check(back != null && Item(back, "view")["value"] as string == "grid" && ((object[])Item(back, "view")["options"]).Length == 10, "the choice survives JSON");
            fired = 0;
            Prefs.Changed += count;
            AppHub.Apply("view", "flip");
            Check(Prefs.View == "flip" && (string)Item(AppHub.Settings(), "view")["value"] == "flip" && fired == 1, "settings.set view=flip is applied and announced");
            AppHub.Apply("view", "flip");
            Check(fired == 1, "the same view again is not announced");
            AppHub.Apply("view", "grid");
            Check(Prefs.View == "grid" && fired == 2, "settings.set view=grid switches back");
            Check(Throws(delegate { AppHub.Apply("view", "cube"); }) && Throws(delegate { AppHub.Apply("view", "Flip"); }) && Throws(delegate { AppHub.Apply("view", ""); }) && Throws(delegate { AppHub.Apply("view", true); }) && Throws(delegate { AppHub.Apply("view", null); }) && Throws(delegate { AppHub.Apply("view", 1); }) && Prefs.View == "grid" && fired == 2, "a view that is not one of the ten is refused and changes nothing");
            bool takesAll = true;
            int firedBefore = fired;
            foreach (string v in wantViews)
            {
                AppHub.Apply("view", v);
                if (Prefs.View != v || (string)Item(AppHub.Settings(), "view")["value"] != v || Prefs.KnownView(v) != v || Prefs.Is3D(v) != (v != "grid")) takesAll = false;
            }
            Check(takesAll && fired == firedBefore + wantViews.Length - 1, "settings.set takes every one of the ten views, each change announced; only the grid is not 3D");
            Check(!Prefs.Is3D("cube") && !Prefs.Is3D(null) && !Prefs.Is3D("grid"), "an unknown view is not 3D");
            Prefs.Changed -= count;
            Check(Prefs.KnownView("flip") == "flip" && Prefs.KnownView("grid") == "grid" && Prefs.KnownView("cube") == "grid" && Prefs.KnownView(null) == "grid", "an unknown view means the grid");
            dir = Path.Combine(Path.GetTempPath(), Program.Id + "-view-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            Environment.SetEnvironmentVariable("SMENA_DATA", dir);
            try
            {
                Prefs.Persist = true;
                Prefs.Reset();
                Prefs.View = "flip";
                Prefs.Save();
                Check(File.ReadAllText(Path.Combine(dir, "settings.json")).Contains("\"view\":\"flip\""), "the view is saved as \"view\"");
                Prefs.Reset();
                Prefs.Load();
                Check(Prefs.View == "flip", "…and read back");
                File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"view\": \"cube\"}");
                Prefs.Reset(); Prefs.View = "flip";
                Prefs.Load();
                Check(Prefs.View == "grid", "an unknown view in the file means the grid");
                File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"view\": 5}");
                Prefs.View = "flip";
                Prefs.Load();
                Check(Prefs.View == "grid", "…so does a view of the wrong kind");
            }
            finally
            {
                Prefs.Persist = false;
                Prefs.Reset();
                Environment.SetEnvironmentVariable("SMENA_DATA", null);
                try { Directory.Delete(dir, true); } catch { }
            }

            // ── Сторож потока окна (StallWatch, KeyMachine.Pass) ──
            var stall = new StallWatch();
            bool ask;
            Check(stall.Idle && stall.Tick(0, out ask) == StallStep.None && !ask, "the stall watch sleeps while nothing is shown");
            stall.Arm();
            Check(!stall.Idle && stall.Tick(100, out ask) == StallStep.None && ask && stall.Waiting, "armed: the first check asks the window thread");
            Check(stall.Tick(200, out ask) == StallStep.None && !ask, "one question at a time: no new one until the answer");
            Check(!stall.Answer() && !stall.Waiting && stall.Tick(300, out ask) == StallStep.None && ask, "an answer in time: nothing happens, the next check asks again");
            Check(stall.Tick(300 + StallWatch.TripMs - 1, out ask) == StallStep.None && !stall.Tripped, "silence just under the threshold is not a stall");
            Check(stall.Tick(300 + StallWatch.TripMs, out ask) == StallStep.Trip && stall.Tripped && Near(stall.Silent(300 + StallWatch.TripMs), StallWatch.TripMs), "silence of TripMs trips the guard");
            Check(stall.Tick(300 + StallWatch.TripMs + 100, out ask) == StallStep.None && !ask, "it trips once");
            stall.Disarm();
            Check(!stall.Idle && stall.Tick(300 + StallWatch.KillMs - 1, out ask) == StallStep.None, "a tripped guard keeps watching even when disarmed, and does not kill too early");
            Check(stall.Tick(300 + StallWatch.KillMs, out ask) == StallStep.Kill && stall.Tick(300 + StallWatch.KillMs + 1000, out ask) == StallStep.None, "silence of KillMs ends the program, once");
            Check(stall.Answer() && !stall.Tripped && !stall.Answer(), "the thread wakes up: the answer says the show must be reset, once");
            Check(stall.Idle && stall.Tick(99999, out ask) == StallStep.None && !ask, "reset and disarmed: the guard sleeps again");
            stall.Arm(); stall.Tick(0, out ask);
            Check(stall.Tick(StallWatch.TripMs, out ask) == StallStep.Trip && stall.Answer() && stall.Tick(StallWatch.TripMs + 1, out ask) == StallStep.None && ask, "after a reset the next stall is counted from a new question");
            Check(StallWatch.TripMs >= 1000 && StallWatch.TripMs <= 1500 && StallWatch.KillMs > StallWatch.TripMs * 5, "thresholds: about a second to hide, much longer to quit");

            k = new Keys();
            k.Send(Alt, true); k.Tap(Tab);
            Check(k.M.Active && k.S.Open, "a switch is going on");
            k.M.Pass();
            Check(k.M.Passing && !k.M.Active && !k.M.Sticky, "the window thread hangs: the switch is over for the hook");
            int logged = k.Log.Count;
            Check(!k.Send(Tab, true) && !k.Send(Tab, false) && k.Log.Count == logged, "while it hangs Alt+Tab goes to Windows and nothing is sent to the window");
            Check(!k.Send(Alt, false) && !k.Send(Esc, true) && !k.Send(KeyMachine.Enter, true) && k.Log.Count == logged, "Alt up, Esc and Enter go through too");
            k.M.End(); k.M.Resume(); k.S.Close();
            Check(!k.M.Passing && !k.M.Active, "after the reset the hook is idle");
            k.Send(Alt, true);
            Check(k.Send(Tab, true) && k.Log[k.Log.Count - 1] == KeyAction.Open && k.M.Active, "and the next Alt+Tab is ours again");
            k.Send(Tab, false); k.Send(Alt, false);
            k = new Keys();
            k.M.BeginSticky();
            k.M.Pass();
            Check(!k.Tap(Tab) && !k.Tap(Esc) && !k.Tap(KeyMachine.Left) && !k.M.Sticky, "a hang during a show opened by command lets Tab, Esc and arrows through");
            k.M.Resume();
            Check(!k.Tap(Tab) && !k.Tap(Esc), "after the reset, with no show open, Tab and Esc are ordinary keys");

            // ── Игры: имена процессов, «в стороне», окна игр ──
            Check(Games.Norm("EasyAntiCheat_EOS.exe") == "easyanticheat_eos" && Games.Norm(@"C:\Games\Dota2.EXE") == "dota2" && Games.Norm(" \"FortniteLauncher.exe\" ") == "fortnitelauncher" && Games.Norm(null) == "", "a process name is compared without path, quotes, .exe and case");
            Check(Games.Match("BEService_x64.EXE", "beservice_x64") && Games.Match("BEService_pubg", "BEService_*") && !Games.Match("BEServic", "BEService_*") && !Games.Match("BEService", "BEService_*"), "names match without case; a trailing star matches what starts the same");
            Check(!Games.Match("anything", "*") && !Games.Match("", "dota2") && !Games.Match("dota2", ""), "a lone star and empty names match nothing");
            Check(Games.Running(new string[] { "explorer", "FACEIT", "faceit.exe", "vgc", "vgtray", "dota2", "cs2", "steam", "RobloxPlayerBeta" }, null) == null, "FACEIT app, Riot Vanguard, Dota 2, CS2 and other everyday processes do not mean a game with anti-cheat");
            Check(Games.Running(new string[] { "explorer", "EasyAntiCheat_EOS.exe" }, null) == "easyanticheat_eos" && Games.Running(new string[] { "EasyAntiCheat" }, null) == "easyanticheat", "Easy Anti-Cheat running — a game with anti-cheat");
            Check(Games.Running(new string[] { "BEService" }, null) != null && Games.Running(new string[] { "BEService_pubg" }, null) != null && Games.Running(new string[] { "faceitclient" }, null) != null, "BattlEye and the FACEIT anti-cheat client — too");
            Check(Games.Running(new string[] { "FortniteClient-Win64-Shipping" }, null) != null && Games.Running(new string[] { "VALORANT-Win64-Shipping" }, null) != null, "Fortnite and VALORANT themselves — too");
            Check(Games.Running(new string[] { "mygame" }, new string[] { "MyGame.exe" }) == "mygame" && Games.Running(new string[] { "mygame" }, null) == null && Games.Running(null, null) == null, "own names (asideProcesses) are added to the built-in list");
            Check(Games.IsGame("dota2", null) && Games.IsGame("CS2.exe", null) && Games.IsGame("EasyAntiCheat_EOS", null) && Games.IsGame("x", new string[] { "X" }) && !Games.IsGame("explorer", null) && !Games.IsGame("", null), "games whose windows are not touched: Dota 2, CS2, everything from the lists");
            string[] extraNames = Games.Extra(new object[] { "a.exe", 5, null, "A", "", "*", new string('x', 300), "C:\\Games\\B.EXE" });
            Check(extraNames.Length == 2 && extraNames[0] == "a" && extraNames[1] == "b", "asideProcesses: strings only, normalised, without repeats and junk");
            Check(Games.Extra("a").Length == 0 && Games.Extra(null).Length == 0 && Games.Extra(5).Length == 0, "asideProcesses of the wrong kind is ignored");
            var many = new object[200];
            for (int i = 0; i < many.Length; i++) many[i] = "g" + i;
            Check(Games.Extra(many).Length == Games.MaxExtra, "asideProcesses is capped");

            var aw = new AsideWatch();
            Check(!aw.Update(false, "easyanticheat_eos", true, true, 0) && !aw.On, "the setting is off: never aside");
            Check(!aw.Update(true, null, false, false, 0), "nothing going on: not aside");
            Check(aw.Update(true, "easyanticheat_eos", false, false, 100) && aw.Reason == AsideReason.AntiCheat && aw.What == "easyanticheat_eos", "anti-cheat appears: aside at once");
            Check(aw.Update(true, null, false, false, 200) && aw.Update(true, null, false, false, 200 + AsideWatch.QuietMs - 1), "it is gone: still aside until QuietMs of quiet");
            Check(aw.Update(true, null, true, false, 200 + AsideWatch.QuietMs - 1) && aw.Reason == AsideReason.Fullscreen, "a new reason inside the quiet time keeps it aside");
            double q0 = 300 + AsideWatch.QuietMs;
            Check(aw.Update(true, null, false, false, q0) && aw.Update(true, null, false, false, q0 + AsideWatch.QuietMs - 1), "the quiet time starts again from the last reason (loading screen between matches)");
            Check(!aw.Update(true, null, false, false, q0 + AsideWatch.QuietMs) && aw.Reason == AsideReason.None, "QuietMs of quiet: back");
            Check(aw.Update(true, null, false, true, 0) && aw.Reason == AsideReason.Core, "core.txt says a game is on: aside");
            Check(!aw.Update(false, null, false, true, 1), "the setting switched off: back at once");
            Check(AsideWatch.QuietMs >= 5000 && AsideWatch.QuietMs <= 10000 && Aside.PollMs <= 3000, "quiet time 5–10 s; processes are looked at every 3 s or more often");

            Check(!Logic.GameWindow(false, true, "dota2", null) && Logic.GameWindow(true, true, "notepad", null) && Logic.GameWindow(true, false, "dota2", null) && !Logic.GameWindow(true, false, "notepad", null), "a game window: only with the setting; fullscreen or a game by name");
            Check(Logic.FullscreenMinimized(1920, 1080, 1920, 1080, false, false) && !Logic.FullscreenMinimized(1920, 1080, 1920, 1080, true, false) && !Logic.FullscreenMinimized(1920, 1080, 1920, 1080, false, true)
                && !Logic.FullscreenMinimized(1280, 720, 1920, 1080, false, false) && !Logic.FullscreenMinimized(0, 0, 0, 0, false, false), "a minimized window is fullscreen if it comes back exactly monitor-sized, without caption and frame");
            var gameShown = new WinItem { Title = "Dota 2", Game = true };
            var gameMin = new WinItem { Title = "Dota 2", Game = true, Minimized = true };
            var ordinary = new WinItem { Title = "Блокнот" };
            Check(!Logic.UseFlip(false, new List<WinItem> { ordinary }) && Logic.UseFlip(true, new List<WinItem> { ordinary }) && Logic.UseFlip(true, null), "the 3D view is used when chosen");
            Check(!Logic.UseFlip(true, new List<WinItem> { ordinary, gameShown }) && Logic.UseFlip(true, new List<WinItem> { ordinary, gameMin }), "a visible game window in the list — the grid; a minimized one does not matter");
            Check(!Logic.MayTouch(gameShown) && !Logic.MayTouch(gameMin) && Logic.MayTouch(ordinary) && !Logic.MayTouch(null), "a game window gets no capture, no thumbnail, no icon from its process");
            Check(!Logic.Gentle(false, false, false) && Logic.Gentle(true, false, false) && Logic.Gentle(false, true, false) && Logic.Gentle(false, false, true), "plain activation only: aside, from a game or into a game");
            Check(Logic.KeysOn(true, false, false) && !Logic.KeysOn(true, false, true) && !Logic.KeysOn(true, true, false) && !Logic.KeysOn(false, false, false), "keys are on only when replacing, not paused and not aside");
            Check(Logic.AsideText(AsideReason.AntiCheat).Length > 0 && Logic.AsideText(AsideReason.Fullscreen).Length > 0 && Logic.AsideText(AsideReason.Core).Length > 0 && Logic.AsideText(AsideReason.None) == "", "every reason has its words");

            Prefs.Reset();
            string gamesHint = Item(AppHub.Settings(), "games")["hint"] as string;
            Check(gamesHint == AppHub.GamesHint && gamesHint.Contains("античит") && gamesHint.Contains("на весь экран") && gamesHint.Contains("обычный Alt+Tab"), "the hint of «Не вмешиваться в играх» says what it does");
            Aside.On = true;
            try
            {
                Check(!AppHub.Keys()[0].On && !AppHub.Keys()[1].On, "aside: both keys are published off");
                var refused = AppHub.ShowCommand(null);
                Check(!(bool)refused["shown"] && (refused["reason"] as string ?? "").Contains("обычный Alt+Tab"), "aside: «show» says no and why");
                Check(!Program.HookWanted, "aside: the keyboard hook is not wanted");
                bool fgAside;
                Check(Win.List(false, false, out fgAside).Count == 0 && Win.Icon(ordinary, null, null) == null, "aside: windows are not listed and icons not asked for");
            }
            finally { Aside.On = false; }
            Check(AppHub.Keys()[0].On && Program.HookWanted, "back from aside: keys and hook are wanted again");

            // Подмена списка процессов действует только там, где её разрешили (--asideprobe, --headless)
            Environment.SetEnvironmentVariable("SMENA_FAKE_PROCS", "EasyAntiCheat_EOS");
            try
            {
                Procs.AllowFake = false;
                List<string> realProcs = Procs.Running();
                Check(realProcs.Count > 1 && !realProcs.Contains("EasyAntiCheat_EOS"), "SMENA_FAKE_PROCS is ignored in an ordinary run");
                Procs.AllowFake = true;
                Check(Procs.Running().Count == 1 && Games.Running(Procs.Running(), null) == "easyanticheat_eos", "SMENA_FAKE_PROCS replaces the list where allowed");
            }
            finally { Procs.AllowFake = false; Environment.SetEnvironmentVariable("SMENA_FAKE_PROCS", null); }
            Check(Procs.NameOf(Win.Self).Length > 0, "a process name comes from the process list snapshot");

            // Свои имена из settings.json сохраняются и не стираются при записи настроек
            string gdir = Path.Combine(Path.GetTempPath(), Program.Id + "-games-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            Environment.SetEnvironmentVariable("SMENA_DATA", gdir);
            try
            {
                Directory.CreateDirectory(gdir);
                Prefs.Persist = true;
                Prefs.Reset();
                File.WriteAllText(Path.Combine(gdir, "settings.json"), "{\"games\": true, \"asideProcesses\": [\"MyGame.exe\", 7]}");
                Prefs.Load();
                Check(Prefs.Extra.Length == 1 && Prefs.Extra[0] == "mygame", "asideProcesses is read from settings.json");
                Prefs.Set("sameApp", false);
                Prefs.Reset();
                Prefs.Load();
                Check(Prefs.Extra.Length == 1 && Prefs.Extra[0] == "mygame" && !Prefs.SameApp, "asideProcesses survives saving the settings");
            }
            finally
            {
                Prefs.Persist = false;
                Prefs.Reset();
                Environment.SetEnvironmentVariable("SMENA_DATA", null);
                try { Directory.Delete(gdir, true); } catch { }
            }

            // ── Пробная сборка из временной папки ──
            string tempDir = @"C:\Users\Someone\AppData\Local\Temp\";
            Check(Logic.InFolder(@"C:\Users\Someone\AppData\Local\Temp\claude\x\hang\SmenaTest.exe", tempDir), "an exe deep in the temp folder is in it");
            Check(Logic.InFolder(@"c:\users\someone\appdata\local\temp\Smena.exe", @"C:\Users\Someone\AppData\Local\Temp"), "case and the trailing slash do not matter");
            Check(Logic.InFolder("C:/Users/Someone/AppData/Local/Temp/a/Smena.exe", tempDir), "forward slashes are the same path");
            Check(!Logic.InFolder(@"C:\Users\Someone\AppData\Local\Temporary\Smena.exe", @"C:\Users\Someone\AppData\Local\Temp"), "a folder whose name only starts the same is another folder");
            Check(!Logic.InFolder(@"C:\Users\Someone\AppData\Local\Programs\smena\Smena.exe", tempDir) && !Logic.InFolder(tempDir, tempDir), "an installed copy is not in temp; the folder itself is not a file in it");
            Check(!Logic.InFolder(null, tempDir) && !Logic.InFolder(@"C:\a.exe", null) && !Logic.InFolder(@"C:\a.exe", "") && !Logic.InFolder("", tempDir), "no path — not in temp");
            Check(Logic.RefuseTempRun(true, null) && Logic.RefuseTempRun(true, "") && Logic.RefuseTempRun(true, "0") && Logic.RefuseTempRun(true, "yes"), "a plain run from temp is refused");
            Check(!Logic.RefuseTempRun(true, "1") && !Logic.RefuseTempRun(true, " 1 ") && !Logic.RefuseTempRun(false, null), "SMENA_ALLOW_TEMP=1 allows it; outside temp nothing is refused");

            Console.WriteLine(failed == 0 ? "all passed (" + checks + " checks)" : failed + " of " + checks + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
