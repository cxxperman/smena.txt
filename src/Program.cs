// smena.txt — переключатель окон dev.txt™ вместо системного Alt+Tab: стеклянная панель с живыми превью окон.
// Живёт в трее; окна в обычной работе нет.
// Ключи для проверки:
//   --demo              выдуманные окна; перехватчик клавиш, хаб, диск и настоящие окна не трогаются
//   --shot файл.png     нарисовать переключатель с выдуманными окнами в файл и выйти; на экране ничего не появляется.
//                       --count N — сколько окон, --select N — какое выбрано, --sticky — подсказка как при показе командой,
//                       --size ШxВ — размер «экрана», --about — нарисовать окно «О программе»
//   --selftest          проверить отбор и порядок окон, клавиши, раскладку, настройки; код выхода 0 — всё в порядке
//   --headless          без трея и без окон: только связь с хабом (для проверок протокола). Перехватчик не ставится.
//   --no-hook           работать как обычно, но перехватчик клавиш не ставить; переключатель открывается только командой «show»
//   --probe [файл]      сосчитать настоящие окна, попробовать завести живое превью на спрятанном окне, написать одну строку и выйти
// Для проверок же: SMENA_HUB_PORT — хаб на другом порту, SMENA_DATA — другая папка для настроек.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace DevTxt.Smena
{
    static class Program
    {
        // Имя в хабе, в каталоге core.txt и в папке настроек — строчными латинскими; Name — имя файла и процесса
        public const string Id = "smena", Name = "Smena";

        // Значок: две карточки окон, одна поверх другой; у передней — глаза. Те же строки — в sprite.json.
        internal static readonly string[] Sprite =
        {
            "#######.....",
            "#######.....",
            "##..........",
            "##.#########",
            "##.##E###E##",
            "##.##E###E##",
            "...#########",
            "...#########"
        };

        public static bool Quitting;

        public static string VersionText
        {
            get { Version v = typeof(Program).Assembly.GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        static string Arg(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name && !args[i + 1].StartsWith("--")) return args[i + 1];
            return null;
        }

        static bool Has(string[] args, string name) { return Array.IndexOf(args, name) >= 0; }

        [STAThread]
        static int Main(string[] args)
        {
            bool demo = Has(args, "--demo"), headless = Has(args, "--headless"), probe = Has(args, "--probe");
            string shot = Arg(args, "--shot");
            if (Has(args, "--selftest")) return SelfTest.Run();

            bool created = true;
            Mutex mutex = null;
            if (shot == null && !demo && !probe && !headless) mutex = new Mutex(true, "devtxt_" + Id + "_single_instance", out created);
            if (!created)
            {
                // Уже запущено: просим показать окно «О программе» и выходим
                try { EventWaitHandle.OpenExisting("devtxt_" + Id + "_show").Set(); } catch { }
                return 0;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (demo || shot != null || probe) Prefs.Persist = false; else Prefs.Load();
            int n;
            if (int.TryParse(Arg(args, "--count"), out n) && n > 0 && n <= 60) Switcher.DemoCount = n;

            if (shot != null)
            {
                if (Has(args, "--about")) { new AboutWindow(true).Shot(shot); return 0; }
                double w = 1600, h = 900;
                string[] size = (Arg(args, "--size") ?? "").Split('x');
                if (size.Length == 2) { double.TryParse(size[0], out w); double.TryParse(size[1], out h); }
                int select = -1;
                int.TryParse(Arg(args, "--select") ?? "-1", out select);
                new Switcher(true, false).Shot(shot, Math.Max(640, w), Math.Max(400, h), select, Has(args, "--sticky"));
                return 0;
            }

            var switcher = new Switcher(demo, headless);
            switcher.Prepare();
            if (probe)
            {
                string line = switcher.Probe();
                string file = Arg(args, "--probe");
                try { if (file != null) File.WriteAllText(file, line + "\r\n"); else Console.WriteLine(line); } catch { }
                return 0;
            }

            // Приложение живёт весь день: ошибка в одном показе не должна ни ронять его, ни оставлять затемнение на экране
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                e.Handled = true;
                switcher.Abort();
                try { if (Prefs.Persist) { Directory.CreateDirectory(Prefs.Dir); File.AppendAllText(Path.Combine(Prefs.Dir, "smena.log"), DateTime.Now.ToString("s") + " " + e.Exception + "\r\n"); } } catch { }
            };

            if (demo)
            {
                // Выдуманные окна на экране; щелчок или Esc — и приложение закрывается
                switcher.Finished = delegate { app.Shutdown(); };
                switcher.OpenSticky();
                app.Run();
                return 0;
            }

            var machine = new KeyMachine();
            machine.LeaveAlone = delegate { return Logic.LeaveAlone(Prefs.Paused, Prefs.Replace, Prefs.Games, Prefs.Games && Win.FullscreenAhead()); };
            machine.SameApp = delegate { return Prefs.SameApp; };
            switcher.Machine = machine;

            // Перехватчик клавиш: только в обычном запуске и только пока он нужен
            Hook.Allowed = !headless && !Has(args, "--no-hook");
            Hook.Setup(machine, app.Dispatcher, switcher.Handle);
            Action sync = delegate { Hook.Sync(Prefs.Replace && !Prefs.Paused); };
            Prefs.Changed += sync;
            sync();

            AppHub.Show = switcher.OpenSticky;
            AppHub.Start(app.Dispatcher);

            if (!headless)
            {
                var about = new AboutWindow(false);
                about.Try = delegate { about.Hide(); switcher.OpenSticky(); };
                Tray.Start(about, delegate
                {
                    Quitting = true;
                    Hook.Stop();
                    AppHub.Stop();
                    Tray.Stop();
                    app.Shutdown();
                });
                var show = new EventWaitHandle(false, EventResetMode.AutoReset, "devtxt_" + Id + "_show");
                new Thread(delegate() { while (true) { show.WaitOne(); app.Dispatcher.BeginInvoke(new Action(about.ShowWindow)); } }) { IsBackground = true }.Start();
                if (Hook.Allowed)
                {
                    // После сна и разблокировки перехватчик ставится заново: Windows могла его снять
                    Microsoft.Win32.SystemEvents.PowerModeChanged += delegate(object s, Microsoft.Win32.PowerModeChangedEventArgs e) { if (e.Mode == Microsoft.Win32.PowerModes.Resume) Hook.Restart(); };
                    Microsoft.Win32.SystemEvents.SessionSwitch += delegate(object s, Microsoft.Win32.SessionSwitchEventArgs e) { if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) Hook.Restart(); };
                }
                // Значки открытых окон добываются заранее и понемногу обновляются — показ их не ждёт
                Win.WarmIcons();
                var warm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                warm.Tick += delegate { if (!switcher.IsOpen) Win.WarmIcons(); };
                warm.Start();
            }
            app.Run();
            GC.KeepAlive(mutex);
            return 0;
        }
    }

    // Значок в трее и его меню
    static class Tray
    {
        static System.Windows.Forms.NotifyIcon icon;
        static System.Windows.Forms.ToolStripMenuItem pause;

        public static void Start(AboutWindow about, Action quit)
        {
            try
            {
                var menu = new System.Windows.Forms.ContextMenuStrip();
                // Настройки лежат на общей странице в core.txt; если он не запущен — окно «О программе» так и скажет
                menu.Items.Add("Настройки", null, delegate { if (AppHub.Online) AppHub.ShowCore(); else about.ShowWindow(); });
                pause = new System.Windows.Forms.ToolStripMenuItem("Приостановить", null, delegate { Prefs.SetPaused(!Prefs.Paused); });
                menu.Items.Add(pause);
                menu.Items.Add("О программе", null, delegate { about.ShowWindow(); });
                var coreItem = new System.Windows.Forms.ToolStripMenuItem("core.txt", null, delegate { CoreApp.Go(AppHub.Online, AppHub.ShowCore); });
                menu.Items.Add(coreItem);
                menu.Opening += delegate { coreItem.Text = CoreApp.Label(AppHub.Online); };
                menu.Items.Add("Выход", null, delegate { quit(); });
                icon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    ContextMenuStrip = menu, Visible = true
                };
                icon.MouseClick += delegate(object s, System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left) about.ShowWindow(); };
                Prefs.Changed += Render;
                Render();
            }
            catch { icon = null; }
        }

        static void Render()
        {
            if (icon == null) return;
            pause.Checked = Prefs.Paused;
            icon.Text = Program.Id + ".txt" + (Prefs.Paused ? " — приостановлено" : "");
        }

        public static void Stop()
        {
            try { if (icon != null) { icon.Visible = false; icon.Dispose(); } } catch { }
            icon = null;
        }
    }

    // Настройки приложения. Меняются со страницы настроек в core.txt; «Приостановить» — из трея и до выхода.
    static class Prefs
    {
        public static volatile bool Replace = true;        // заменять Alt+Tab
        public static volatile bool SameApp = true;        // Alt+` — окна того же приложения
        public static volatile bool Games = true;          // не вмешиваться, когда впереди окно на весь экран
        public static volatile bool AllDesktops = false;   // показывать окна со всех рабочих столов
        public static volatile bool Paused = false;        // приостановлено из трея; на диск не пишется
        public static double Size = Layout.DefaultPreview; // ширина превью
        public static bool Persist = true;                 // false — ничего не писать на диск (--demo)
        public static event Action Changed;                // вызывается в потоке окна

        public static string Dir
        {
            get
            {
                string test = Environment.GetEnvironmentVariable("SMENA_DATA");
                if (!string.IsNullOrEmpty(test)) return test;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dev.txt", Program.Id);
            }
        }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static void Reset()
        {
            Replace = true; SameApp = true; Games = true; AllDesktops = false; Paused = false; Size = Layout.DefaultPreview;
        }

        // Размер превью — в пределах и по шагу ползунка
        public static double Snap(double n)
        {
            n = Layout.MinPreview + Math.Round((n - Layout.MinPreview) / Layout.StepPreview) * Layout.StepPreview;
            return Math.Max(Layout.MinPreview, Math.Min(Layout.MaxPreview, n));
        }

        static bool Flag(Dictionary<string, object> d, string key, bool fallback)
        {
            object v;
            return d.TryGetValue(key, out v) && v is bool ? (bool)v : fallback;
        }

        public static void Load()
        {
            try
            {
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath)) as Dictionary<string, object>;
                if (d == null) return;
                Replace = Flag(d, "replace", Replace);
                SameApp = Flag(d, "sameApp", SameApp);
                Games = Flag(d, "games", Games);
                AllDesktops = Flag(d, "allDesktops", AllDesktops);
                object v;
                if (d.TryGetValue("size", out v) && (v is int || v is decimal || v is double)) Size = Snap(Convert.ToDouble(v));
            }
            catch { }   // файла ещё нет или он испорчен — остаются значения по умолчанию
        }

        public static void Save()
        {
            if (!Persist) return;
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    { "replace", Replace }, { "sameApp", SameApp }, { "games", Games }, { "allDesktops", AllDesktops }, { "size", Size }
                }));
            }
            catch { }
        }

        static void Announce(bool save)
        {
            if (save) Save();
            if (Changed != null) Changed();
        }

        // true — значение действительно поменялось
        public static bool Set(string key, bool on)
        {
            if (key == "replace") { if (Replace == on) return false; Replace = on; }
            else if (key == "sameApp") { if (SameApp == on) return false; SameApp = on; }
            else if (key == "games") { if (Games == on) return false; Games = on; }
            else if (key == "allDesktops") { if (AllDesktops == on) return false; AllDesktops = on; }
            else return false;
            Announce(true);
            return true;
        }

        public static bool Set(string key, double n)
        {
            if (key != "size" || Size == n) return false;
            Size = n;
            Announce(true);
            return true;
        }

        public static bool SetPaused(bool on)
        {
            if (Paused == on) return false;
            Paused = on;
            Announce(false);
            return true;
        }
    }
}
