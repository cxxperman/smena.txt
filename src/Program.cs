// smena.txt — переключатель окон dev.txt™ вместо системного Alt+Tab: стеклянная панель с живыми превью окон.
// Живёт в трее; окна в обычной работе нет.
// Ключи для проверки:
//   --demo              выдуманные окна; перехватчик клавиш, хаб, диск и настоящие окна не трогаются
//   --shot файл.png     нарисовать переключатель с выдуманными окнами в файл и выйти; на экране ничего не появляется.
//                       --count N — сколько окон, --select N — какое выбрано, --sticky — подсказка как при показе командой,
//                       --size ШxВ — размер «экрана», --about — нарисовать окно «О программе»
//   --selftest          проверить отбор и порядок окон, клавиши, раскладку, настройки; код выхода 0 — всё в порядке
//   --headless          без трея и без окон: только связь с хабом (для проверок протокола). Перехватчик не ставится.
//                       Что было бы со своим значком в трее (виден, ждёт core.txt, спрятан), пишется строкой в вывод.
//   --no-hook           работать как обычно, но перехватчик клавиш не ставить; переключатель открывается только командой «show»
//                       без страницы и кнопкой «Показать»
//   --probe [файл]      сосчитать настоящие окна, попробовать завести живое превью на спрятанном окне, написать одну строку и выйти
//   --capture [файл.png] проверить живую картинку окна (Windows.Graphics.Capture → WPF) на своём окне, спрятанном DWM:
//                       одна строка итога в вывод, код выхода 0 — картинка пришла и цвета совпали; в файл — полученный кадр.
//                       --copy — сразу запасной путь через память вместо D3DImage
//   --view ВИД          вид для --demo, --shot и --flipprobe (на настройки не влияет): grid — сетка; flip, cascade, cover,
//                       carousel, fan, panorama, helix, rolodex, tunnel — объёмные раскладки. --demo --view flip —
//                       сцена с выдуманными окнами без захвата; --shot файл.png --view flip — её конечный вид в файл
//   --demo --view ВИД --film ПАПКА  раскадровка объёмной сцены без экрана: появление и уход выбранного окна по кадрам
//                       (open_000.png…, leave_000.png…) и контактные листы open.png и leave.png; --fps N — шаг (по умолчанию 60),
//                       --size ШxВ — размер «экрана» (по умолчанию 1720x720). Одна строка итога: смещения за кадр, разница
//                       первого и последнего кадров с «рабочим столом». --pick-min — выбрано свёрнутое окно: его карточка
//                       летит на место, где окно встанет после разворачивания; --pick-min miss — место предсказано мимо,
//                       и карточка доводится до настоящего
//   --flipprobe [файл.png] проверить объёмную сцену живьём без экрана (раскладка — --view, по умолчанию «Стопка»): свои
//                       спрятанные цветные окна, спрятанная сцена на них, 3 секунды листания; одна строка замеров в вывод,
//                       код выхода 0 — сцена отработала без ошибок и все свои окна дали кадры. В файл — сцена в середине
//                       прогона, на общем фоне dev.txt вместо экрана
//   --flipprobe --real  та же спрятанная сцена на настоящих окнах (список — как при Alt+Tab), три показа подряд; окна только
//                       захватываются, ничего не выводится вперёд. Одна строка: время от нажатия до первого кадра, доля полёта на нём,
//                       кадры полёта и ухода, худший промежуток между кадрами
//   --flipprobe --stall МС  проверить сторожа потока окна (Stall.cs): два своих окна 1×1 в углу экрана, их поток замирает
//                       на МС (не меньше 2000); сторож должен сам спрятать окна и отдать Alt+Tab Windows, а очнувшийся
//                       поток — сбросить показ. Больше 10500 — проверяется и выход программы (в пробе он подменён)
//   --asideprobe        проверить «отойти в сторону» на время игры (Aside.cs) без экрана и без настоящего перехвата клавиатуры:
//                       перехватчик — заглушка, список процессов подменён. Одна строка итога, код выхода 0 — всё так
// Обычный запуск (без ключей проверки) из временной папки запрещён — это пробная сборка; SMENA_ALLOW_TEMP=1 — разрешить.
// Для проверок же: SMENA_HUB_PORT — хаб на другом порту, SMENA_DATA — другая папка для настроек,
// SMENA_TRACE=<файл> — каждый настоящий показ объёмного вида дописывает туда строку отметок времени (Marks.cs),
// SMENA_FAKE_PROCS — только с --headless: какие процессы «запущены» (имена через запятую или путь к файлу с ними;
// файл читается при каждом опросе) вместо настоящего списка — так «идёт игра» проверяется без игры.
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

        // Нужен ли сейчас перехватчик клавиш: замена Alt+Tab включена, не приостановлена и smena.txt не в стороне
        public static bool HookWanted { get { return Prefs.Replace && !Prefs.Paused && !Aside.On; } }

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

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint GetLongPathName(string shortPath, System.Text.StringBuilder longPath, uint size);

        // Полный путь без коротких имён 8.3 (временная папка бывает вида C:\Users\ABCDEF~1\...); не вышло — как есть
        static string LongPath(string path)
        {
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                uint n = GetLongPathName(Path.GetFullPath(path), sb, (uint)sb.Capacity);
                return n > 0 && n < sb.Capacity ? sb.ToString() : Path.GetFullPath(path);
            }
            catch { return path; }
        }

        [STAThread]
        static int Main(string[] args)
        {
            // Переключатель — прозрачное окно во весь экран: Windows перерисовывает его целиком через процессор.
            // На частоте монитора (180 Гц и выше) он не успевает и мерцает — остаёмся на 60 кадрах, как WPF задумал.
            Ui.AllowSmooth = false;
            bool demo = Has(args, "--demo"), headless = Has(args, "--headless"), probe = Has(args, "--probe");
            string shot = Arg(args, "--shot");
            if (Has(args, "--selftest")) return SelfTest.Run();
            if (Has(args, "--capture"))
            {
                // Без мьютекса, настроек и сети: своё невидимое окно, итог — одной строкой
                Prefs.Persist = false;
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                LiveWindow.ForceCopy = Has(args, "--copy");
                string line;
                int code = CaptureProbe.Run(Arg(args, "--capture"), out line);
                try { var o = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)); o.WriteLine(line); o.Flush(); } catch { }
                return code;
            }
            if (Has(args, "--asideprobe"))
            {
                // Без мьютекса, настроек, хаба и экрана; перехватчик — заглушка
                string line;
                int code = AsideProbe.Run(out line);
                try { var o = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)); o.WriteLine(line); o.Flush(); } catch { }
                return code;
            }
            if (Has(args, "--flipprobe"))
            {
                // Так же: без мьютекса, настроек и сети; все окна спрятаны
                Prefs.Persist = false;
                string probeView = Arg(args, "--view");
                if (probeView != null && Prefs.Is3D(probeView)) Prefs.View = probeView;
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                string line;
                int stall, count;
                if (!int.TryParse(Arg(args, "--count") ?? "", out count) || count < 1 || count > 60) count = 0;
                int code = Has(args, "--stall") ? (int.TryParse(Arg(args, "--stall") ?? "3000", out stall) ? StallProbe.Run(stall, out line) : StallProbe.Run(0, out line))
                    : Has(args, "--real") ? FlipProbe.RunReal(4, out line) : FlipProbe.Run(Arg(args, "--flipprobe"), count, out line);
                try { var o = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)); o.WriteLine(line); o.Flush(); } catch { }
                return code;
            }

            // Обычный запуск (без ключей проверки) пробной сборки из временной папки не встаёт настоящим перехватом
            // клавиатуры: такую копию мог подхватить и запустить core.txt, а в ней может быть недоделанное.
            // Нарочно — переменная SMENA_ALLOW_TEMP=1.
            if (shot == null && !demo && !probe && !headless && Logic.RefuseTempRun(Logic.InFolder(LongPath(typeof(Program).Assembly.Location), LongPath(Path.GetTempPath())), Environment.GetEnvironmentVariable("SMENA_ALLOW_TEMP")))
            {
                try { var o = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)); o.WriteLine("пробная сборка: запуск без ключей из временной папки запрещён"); o.Flush(); } catch { }
                return 3;
            }

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
            string view = Arg(args, "--view");
            if (view != null && (demo || shot != null)) Prefs.View = Prefs.KnownView(view);
            // Есть ли объёмные виды — для страницы настроек; видеокарта тут не трогается (это делает первый показ сцены)
            AppHub.FlipAvailable = Capture.SystemSupports;
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
                if (Prefs.Is3D(Prefs.View)) new FlipView().Shot(shot, Math.Max(640, w), Math.Max(400, h), select, Has(args, "--sticky"));
                else new Switcher(true, false).Shot(shot, Math.Max(640, w), Math.Max(400, h), select, Has(args, "--sticky"));
                return 0;
            }

            string film = Arg(args, "--film");
            if (film != null && demo)
            {
                // Раскадровка объёмной сцены в файлы; окон на экране нет
                string line;
                int code = 0;
                if (!Prefs.Is3D(Prefs.View)) { line = "film: нужен объёмный вид (--view flip, cover…)"; code = 1; }
                else
                {
                    double w = 1720, h = 720, fps = 60;
                    string[] size = (Arg(args, "--size") ?? "").Split('x');
                    if (size.Length == 2) { double.TryParse(size[0], out w); double.TryParse(size[1], out h); }
                    double f;
                    if (double.TryParse(Arg(args, "--fps") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) && f >= 10 && f <= 480) fps = f;
                    int pickMin = !Has(args, "--pick-min") ? 0 : Arg(args, "--pick-min") == "miss" ? 2 : 1;
                    line = new FlipView().Film(film, Math.Max(640, w), Math.Max(400, h), fps, pickMin);
                }
                try { var o = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)); o.WriteLine(line); o.Flush(); } catch { }
                return code;
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
            machine.LeaveAlone = delegate { return Aside.On || Logic.LeaveAlone(Prefs.Paused, Prefs.Replace, Prefs.Games, Prefs.Games && Win.FullscreenAhead()); };
            machine.SameApp = delegate { return Prefs.SameApp; };
            switcher.Machine = machine;

            // Перехватчик клавиш: только в обычном запуске и только пока он нужен
            Hook.Allowed = !headless && !Has(args, "--no-hook");
            Hook.Setup(machine, app.Dispatcher, switcher.Handle);
            // Перехватчик отдаёт окну действие — сторож ждёт ответа потока окна (завис — клавиши идут мимо нас)
            if (switcher.Guard != null) Hook.Posting = switcher.Guard.Arm;
            Action sync = delegate { Hook.Sync(HookWanted); };
            Prefs.Changed += sync;
            sync();
            // Сборщик мусора не останавливает программу надолго: долгая пауза — повод для Windows снять перехватчик
            try { System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency; } catch { }

            // Пока идёт игра — в стороне (Aside.cs): перехватчик снят, захваты закрыты, чужие окна не трогаются.
            // Подмена списка процессов (SMENA_FAKE_PROCS) — только без окон, для проверок
            Procs.AllowFake = headless;
            Aside.CoreGame = delegate { return AppHub.CoreGame; };
            Aside.Wire(switcher, sync);

            AppHub.Show = switcher.OpenSticky;
            AppHub.Start(app.Dispatcher);
            Aside.Start(app.Dispatcher);

            // Без трея решение о своём значке принимается так же — и пишется строкой: так связь с core.txt проверяется без экрана
            if (headless) Tray.Follow(Say);

            if (!headless)
            {
                var about = new AboutWindow(false);
                about.Try = delegate { about.Hide(); switcher.OpenSticky(); };
                AppHub.Open = about.ShowWindow;   // «Открыть» в меню core.txt, пока своего значка нет
                Action quit = delegate
                {
                    Quitting = true;
                    Aside.Stop();
                    Hook.Stop();
                    AppHub.Stop();
                    Tray.Stop();
                    app.Shutdown();
                };
                AppHub.Quit = quit;   // «Закрыть» в меню core.txt
                Tray.Start(about, quit);
                var show = new EventWaitHandle(false, EventResetMode.AutoReset, "devtxt_" + Id + "_show");
                new Thread(delegate() { while (true) { show.WaitOne(); app.Dispatcher.BeginInvoke(new Action(about.ShowWindow)); } }) { IsBackground = true }.Start();
                if (Hook.Allowed)
                {
                    // После сна и разблокировки перехватчик ставится заново: Windows могла его снять
                    Microsoft.Win32.SystemEvents.PowerModeChanged += delegate(object s, Microsoft.Win32.PowerModeChangedEventArgs e) { if (e.Mode == Microsoft.Win32.PowerModes.Resume) Hook.Restart(); };
                    Microsoft.Win32.SystemEvents.SessionSwitch += delegate(object s, Microsoft.Win32.SessionSwitchEventArgs e) { if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) Hook.Restart(); };
                }
                // Сторож следит, какое окно впереди, — из этого складывается порядок окон «от недавнего к давнему».
                // И ещё: если при зажатом Alt открылся системный переключатель, значит, мы клавишу не получили.
                // Записываем почему и, если перехватчик сняла Windows, сразу ставим его заново.
                Win.Watch(delegate(IntPtr before)
                {
                    if (!Hook.Allowed || !Prefs.Replace || Prefs.Paused || !Hook.Installed) return;
                    string who = Win.Describe(before);
                    if (Prefs.Games && Win.Fullscreen(before)) { Journal.Write("Alt+Tab отдан Windows: впереди окно на весь экран — " + who + ". Это настройка «Не вмешиваться в играх»"); return; }
                    if (Win.Elevated(before)) { Journal.Write("Alt+Tab не перехвачен: впереди окно с правами администратора — " + who + ". В таких окнах Windows не даёт перехватывать клавиши"); return; }
                    Journal.Write("Alt+Tab не перехвачен: Windows сняла перехватчик клавиш (впереди " + who + "). Поставлен заново");
                    Hook.Restart();
                });
                if (Hook.Allowed)
                {
                    // И без всякого сбоя перехватчик время от времени ставится заново — без перерыва, незаметно
                    var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    refresh.Tick += delegate { if (!switcher.IsOpen) Hook.Refresh(); };
                    refresh.Start();
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

        // --headless: что сейчас со своим значком в трее — строкой в стандартный вывод, в UTF-8
        static TextWriter said;
        static void Say(bool shown)
        {
            try
            {
                if (said == null) said = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                said.WriteLine("свой значок в трее: " + (shown ? "виден" : Tray.Waiting ? "ждёт ответа core.txt" : "спрятан — его держит core.txt"));
            }
            catch { }
        }
    }

    // Журнал: что пошло не так с Alt+Tab и почему. Лежит рядом с настройками, не больше ~200 КБ.
    static class Journal
    {
        public static string File { get { return Path.Combine(Prefs.Dir, "smena.log"); } }

        public static void Write(string line)
        {
            try
            {
                if (!Prefs.Persist) return;
                Directory.CreateDirectory(Prefs.Dir);
                var info = new FileInfo(File);
                if (info.Exists && info.Length > 200 * 1024) System.IO.File.Delete(File);
                System.IO.File.AppendAllText(File, DateTime.Now.ToString("s") + " " + line + "\r\n");
            }
            catch { }
        }
    }

    // Значок в трее и его меню. Пока core.txt на связи и держит один значок на всех, своего нет: «Открыть» и
    // «Приостановить» стоят в меню core.txt (AppHub.Tray). Связь пропала — значок сразу снова здесь, с полным меню.
    static class Tray
    {
        static System.Windows.Forms.NotifyIcon icon;
        static OwnTray rule;
        static DispatcherTimer wait;
        static Action<bool> apply;
        static int last = -1;   // что решили в прошлый раз: 0 — спрятан, 1 — ждёт core.txt, 2 — виден

        // Ещё ждём первого ответа core.txt после запуска
        public static bool Waiting { get { return rule != null && rule.Waiting; } }

        public static void Start(AboutWindow about, Action quit)
        {
            try
            {
                icon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    Visible = false   // покажет Follow, как только станет ясно, что значок не у core.txt
                };
                // Меню Windows 11, а не серое меню WinForms. Настройки лежат на общей странице в core.txt;
                // если он не запущен — окно «О программе» так и скажет
                icon.MouseUp += delegate(object s, System.Windows.Forms.MouseEventArgs e)
                {
                    if (e.Button != System.Windows.Forms.MouseButtons.Right) return;
                    var menu = new PopMenu();
                    menu.Brand(Program.Sprite, Program.Id);
                    menu.Item("Открыть smena.txt", false, about.ShowWindow);
                    menu.Item("Приостановить", Prefs.Paused, delegate { Prefs.SetPaused(!Prefs.Paused); });
                    menu.Item("Настройки", false, delegate { if (AppHub.Online) AppHub.ShowCore(AppHub.SettingsPage); else about.ShowWindow(); });
                    menu.Item(CoreApp.Label(AppHub.Online), false, delegate { CoreApp.Go(AppHub.Online, AppHub.ShowCore); });
                    menu.Line();
                    menu.Item("Выход", false, quit);
                    menu.ShowAtCursor();
                };
                icon.MouseClick += delegate(object s, System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left) about.ShowWindow(); };
                Prefs.Changed += Render;
                Aside.Changed += Render;
                Render();
            }
            catch { icon = null; }
            Follow(delegate(bool on) { if (icon != null && icon.Visible != on) icon.Visible = on; });
        }

        // Следит, нужен ли свой значок (правило — OwnTray), и зовёт show(виден) в потоке окна, когда решение меняется.
        // Сразу после запуска, если core.txt установлен, ждёт его ответа не дольше OwnTray.Wait — потом значок появляется.
        public static void Follow(Action<bool> show)
        {
            if (rule != null) return;
            apply = show;
            rule = new OwnTray(CoreApp.Installed);
            if (rule.Waiting)
            {
                wait = new DispatcherTimer { Interval = OwnTray.Wait };
                wait.Tick += delegate { rule.Expire(); Sync(); };
                wait.Start();
            }
            AppHub.FamilyChanged += Sync;
            Sync();
        }

        static void Sync()
        {
            if (rule == null) return;
            bool on = rule.Visible(AppHub.CoreKnown, AppHub.HideOwnTray);
            if (!rule.Waiting && wait != null) { wait.Stop(); wait = null; }
            int now = on ? 2 : rule.Waiting ? 1 : 0;
            if (now == last) return;
            last = now;
            try { if (apply != null) apply(on); } catch { }
        }

        static void Render()
        {
            if (icon == null) return;
            icon.Text = Program.Id + ".txt" + (Prefs.Paused ? " — приостановлено" : Aside.On ? " — в стороне: " + Aside.Text.ToLowerInvariant() : "");
        }

        public static void Stop()
        {
            if (wait != null) { wait.Stop(); wait = null; }
            AppHub.FamilyChanged -= Sync;
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
        public static double Size = Layout.DefaultPreview; // ширина превью (только для сетки)
        public static string View = "grid";               // вид переключателя: "grid" — сетка, иначе объёмная раскладка (Views)
        public static bool Persist = true;                 // false — ничего не писать на диск (--demo)
        // Свои имена процессов к спискам игр (Games): ключ asideProcesses в settings.json, в интерфейсе его нет.
        // Меняется только целиком — его читают и из других потоков.
        public static volatile string[] Extra = new string[0];
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
            Replace = true; SameApp = true; Games = true; AllDesktops = false; Paused = false; Size = Layout.DefaultPreview; View = "grid";
            Extra = new string[0];
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
                if (d.TryGetValue("view", out v)) View = KnownView(v as string);
                if (d.TryGetValue("asideProcesses", out v)) Extra = DevTxt.Smena.Games.Extra(v);
            }
            catch { }   // файла ещё нет или он испорчен — остаются значения по умолчанию
        }

        public static void Save()
        {
            if (!Persist) return;
            try
            {
                Directory.CreateDirectory(Dir);
                var d = new Dictionary<string, object>
                {
                    { "replace", Replace }, { "sameApp", SameApp }, { "games", Games }, { "allDesktops", AllDesktops }, { "size", Size }, { "view", View }
                };
                // Свои имена процессов пишутся обратно, иначе первое же изменение настроек их стёрло бы
                string[] extra = Extra;
                if (extra.Length > 0) d["asideProcesses"] = extra;
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(d));
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

        // Виды переключателя по порядку и их названия в настройках: сетка и объёмные раскладки (Flip.Views)
        public static readonly string[] Views = { "grid", "flip", "cascade", "cover", "carousel", "fan", "panorama", "helix", "rolodex", "tunnel" };
        public static readonly string[] ViewLabels = { "Сетка", "Стопка", "Каскад", "Обложки", "Карусель", "Веер", "Панорама", "Спираль", "Барабан", "Туннель" };

        // Вид переключателя: незнакомое значение — сетка
        public static string KnownView(string v) { return v != null && Array.IndexOf(Views, v) >= 0 ? v : "grid"; }

        // Объёмный вид (не сетка)
        public static bool Is3D(string v) { return v != "grid" && KnownView(v) == v; }

        public static bool Set(string key, string value)
        {
            if (key != "view" || View == value) return false;
            View = KnownView(value);
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
