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
                // Как в Windows: у отпускания самого Alt признака «Alt зажат» уже нет
                bool eaten = M.Key(vk, scan, down, alt, shift, ctrl, out a);
                if (vk == KeyMachine.Shift) shift = down;
                if (vk == KeyMachine.Ctrl) ctrl = down;
                if (a != KeyAction.None) { Log.Add(a); Do(a); }
                return eaten;
            }

            public bool Tap(int vk) { bool e = Send(vk, true); Send(vk, false); return e; }

            // То же, что делает окно переключателя, только без окна
            void Do(KeyAction a)
            {
                if (a == KeyAction.Open || a == KeyAction.OpenBack || a == KeyAction.OpenSame)
                {
                    if (Count == 0) { M.End(); return; }
                    S.Begin(Count, Cols, ForegroundFirst, a == KeyAction.OpenBack);
                }
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
            Check(!k.Tap(0x41) && k.M.Active, "other keys with Alt held are not swallowed and do not end the session");
            Check(k.Tap(KeyMachine.Enter) && k.Chosen == 1 && !k.M.Active, "Enter with Alt held switches at once");

            k = new Keys();
            k.Send(Alt, true); k.Send(KeyMachine.Ctrl, true);
            Check(!k.Tap(Tab) && !k.S.Open, "Ctrl+Alt+Tab is left to Windows");
            k.Send(KeyMachine.Ctrl, false); k.Send(Alt, false);
            Check(!k.Tap(Tab) && k.Log.Count == 0, "Tab without Alt is an ordinary Tab");

            // Alt отпустили, а мы этого не увидели: следующая клавиша без Alt заканчивает показ и не проглатывается
            k = new Keys();
            k.Send(Alt, true); k.Tap(Tab); k.Tap(Tab);
            KeyAction stale;
            Check(!k.M.Key(0x41, 0x1E, true, false, false, false, out stale) && stale == KeyAction.Commit && !k.M.Active, "a key without Alt during a session ends it and goes through");

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
            Check(Logic.IsFullscreen(0, 0, 1920, 1080, 0, 0, 1920, 1080, false), "borderless window over the whole monitor is fullscreen");
            Check(Logic.IsFullscreen(1920, 0, 4480, 1440, 1920, 0, 4480, 1440, false), "…on the second monitor too");
            Check(!Logic.IsFullscreen(-8, -8, 1928, 1088, 0, 0, 1920, 1080, true), "a maximised window with a caption is not");
            Check(!Logic.IsFullscreen(0, 0, 1920, 1040, 0, 0, 1920, 1080, false), "a window that leaves the taskbar visible is not");

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

            Console.WriteLine(failed == 0 ? "all passed (" + checks + " checks)" : failed + " of " + checks + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
