// smena.txt. AppHub.cs — связь с core.txt, лаунчером dev.txt™. Он держит на этом компьютере хаб (127.0.0.1:42080).
// Приложение отдаёт туда настройки — они появляются на общей странице настроек в core.txt — и принимает оттуда
// изменения. Оттуда же приходит общий вид: «Меньше движения» для всех приложений разом. Команда «show» без страницы
// один раз показывает переключатель, чтобы его можно было попробовать без клавиатуры; со страницей — окно (ShowRoute).
// Семья dev.txt (Family.cs): пока core.txt на связи и держит один значок в трее на всех, своего значка нет —
// «Открыть» и «Приостановить» smena.txt стоят в его меню (smena.tray), а оба сочетания клавиш — на его странице
// «Клавиши» (smena.keys). «Не вмешиваться в играх» core.txt показывает на своей странице «Игры». Пока smena.txt
// в стороне (идёт игра, Aside.cs), оба сочетания в smena.keys выключены, а «show» отвечает отказом с причиной.
// core.txt может быть не запущен — это нормально: приложение работает само и тихо пробует подключиться снова.
// Протокол описан в kit.txt (hub/PROTOCOL.md); HubLink.cs и Family.cs скопированы оттуда как есть.
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace DevTxt.Smena
{
    static class AppHub
    {
        public const string Topic = Program.Id + ".settings";
        public const string SettingsPage = "app:" + Program.Id;   // страница настроек smena.txt в core.txt
        const int AltMod = 1, Grave = 0xC0;                        // Alt — как у RegisterHotKey; клавиша над Tab (VK_OEM_3)

        static HubLink link;
        static Family family;
        static Dispatcher ui;
        static int published;

        public static bool FlipAvailable = true;    // есть ли на этом компьютере объёмные виды (Windows.Graphics.Capture); ставит Program
        public static Func<bool> Show;              // показать переключатель: команда «show» без страницы; true — показан
        public static Action Quit;                  // закрыть smena.txt: «Закрыть» в трее core.txt, пока своего значка нет
        public static Action Open;                  // открыть окно «О программе»: «Открыть» в трее core.txt, «show» со страницей
        public static event Action StatusChanged;   // связь появилась или пропала; вызывается в потоке окна
        public static event Action FamilyChanged;   // core.txt сообщил новое core.family или связь пропала; в потоке окна
        public static bool Online { get { return link != null && link.Online; } }

        // core.txt на связи и сказал, что сейчас у семьи (core.family)
        public static bool CoreKnown { get { return family != null && family.CoreOnline; } }

        // Свой значок в трее не нужен: core.txt на связи и показывает один значок на всех
        public static bool HideOwnTray { get { return family != null && family.HideOwnTray; } }

        // Для проверок: хаб на другом порту, рядом с настоящим core.txt
        public static int TestPort
        {
            get { int p; return int.TryParse(Environment.GetEnvironmentVariable("SMENA_HUB_PORT"), out p) && p > 0 ? p : 0; }
        }

        // Подсказка к «Не вмешиваться в играх»: что именно делает программа (Aside.cs и окна игр в Win.cs)
        public const string GamesHint = "Когда идёт игра с античитом, впереди игра или окно на весь экран, smena.txt полностью отходит в сторону — работает обычный Alt+Tab";

        // Описание настроек — в том виде, в каком его ждёт core.txt: всегда целиком.
        // «Не вмешиваться в играх» помечена section: "games" — core.txt показывает её на своей странице «Игры»,
        // рядом с тем, что делают в играх остальные приложения. Ключ и значение те же.
        public static Dictionary<string, object> Settings()
        {
            var keys = new object[]
            {
                HubLink.Msg("key", "replace", "type", "switch", "label", "Заменять Alt+Tab", "hint", "Вместо системного переключателя окон открывается этот", "value", Prefs.Replace),
                HubLink.Msg("key", "sameApp", "type", "switch", "label", "Alt+` — окна того же приложения", "hint", "Клавиша над Tab перебирает окна программы, в которой вы сейчас", "value", Prefs.SameApp),
                HubLink.Msg("key", "games", "type", "switch", "label", "Не вмешиваться в играх", "hint", GamesHint, "value", Prefs.Games, "section", "games")
            };
            var options = new object[Prefs.Views.Length];
            for (int i = 0; i < options.Length; i++) options[i] = HubLink.Msg("value", Prefs.Views[i], "label", Prefs.ViewLabels[i]);
            var view = new object[]
            {
                HubLink.Msg("key", "view", "type", "choice", "label", "Вид переключателя",
                    "hint", FlipAvailable ? "Все виды, кроме сетки, — объёмные, с живыми окнами" : "Объёмные виды на этом компьютере недоступны — будет сетка",
                    "value", Prefs.View, "options", options),
                HubLink.Msg("key", "allDesktops", "type", "switch", "label", "Показывать окна со всех рабочих столов", "hint", "Иначе — только с текущего", "value", Prefs.AllDesktops),
                HubLink.Msg("key", "size", "type", "slider", "label", "Размер превью", "hint", "Только для сетки; к объёмным видам не относится", "value", Prefs.Size, "min", Layout.MinPreview, "max", Layout.MaxPreview, "step", Layout.StepPreview, "unit", "px")
            };
            return HubLink.Msg("groups", new object[] { HubLink.Msg("title", "Клавиши", "items", keys), HubLink.Msg("title", "Вид", "items", view) });
        }

        // Быстрые пункты для общего меню в трее core.txt: самое частое, без имени приложения — оно стоит над ними.
        // Run зовётся в потоке хаба: окно и настройки трогаются только в потоке окна.
        public static List<TrayItem> Tray()
        {
            return new List<TrayItem>
            {
                new TrayItem("show", "Открыть", null, delegate { OnUi(delegate { if (Open != null) Open(); }); }),
                new TrayItem("pause", "Приостановить", Prefs.Paused, delegate { OnUi(delegate { Prefs.SetPaused(!Prefs.Paused); }); })
            };
        }

        // Сочетания для общей страницы «Клавиши» в core.txt. Работают, пока замена Alt+Tab включена, не приостановлена и
        // smena.txt не в стороне (идёт игра); сочетание с клавишей над Tab — ещё и пока включены окна того же приложения.
        public static List<KeyInfo> Keys()
        {
            bool on = Logic.KeysOn(Prefs.Replace, Prefs.Paused, Aside.On);
            return new List<KeyInfo>
            {
                new KeyInfo("switch", "Переключение окон", KeyNames.Text(AltMod, KeyMachine.Tab), on),
                new KeyInfo("sameApp", "Окна того же приложения", KeyNames.Text(AltMod, Grave), on && Prefs.SameApp)
            };
        }

        // Столько поток хаба ждёт поток окна. Взаимного ожидания нет (поток окна в хаб только кладёт сообщения в очередь),
        // но если поток окна завис, поток хаба без предела стоял бы вместе с ним — и связь с core.txt тоже.
        const int UiWaitMs = 5000;

        // Выполнить в потоке окна и дождаться: ответ на команду уходит, когда всё уже сделано и разослано.
        // Поток окна не ответил за UiWaitMs — просьба снимается, в ответ уходит «не получилось». Без окна (проверки) — прямо здесь.
        static void OnUi(Action a)
        {
            if (ui == null || ui.CheckAccess()) { a(); return; }
            Exception failed = null;
            DispatcherOperation op = ui.BeginInvoke(DispatcherPriority.Send, new Action(delegate { try { a(); } catch (Exception e) { failed = e; } }));
            // Не успел: снимаем просьбу; если она уже выполняется — даём доделать ещё столько же
            if (op.Wait(TimeSpan.FromMilliseconds(UiWaitMs)) != DispatcherOperationStatus.Completed
                && (op.Abort() || op.Wait(TimeSpan.FromMilliseconds(UiWaitMs)) != DispatcherOperationStatus.Completed))
                throw new Exception("smena.txt сейчас не отвечает, попробуйте ещё раз");
            if (failed != null) throw failed;
        }

        static bool Number(object value, out double n)
        {
            n = 0;
            if (value is int) n = (int)value;
            else if (value is long) n = (long)value;
            else if (value is double) n = (double)value;
            else if (value is decimal) n = (double)(decimal)value;
            else return false;
            return !double.IsNaN(n) && !double.IsInfinity(n);
        }

        // Меняет настройку по её ключу. Вызывается в потоке окна; незнакомый ключ или не то значение — исключение,
        // его текст уйдёт в core.txt ответом «не получилось».
        public static void Apply(string key, object value)
        {
            if (key == "size")
            {
                double n;
                if (!Number(value, out n)) throw new Exception("нужно число");
                Prefs.Set(key, Prefs.Snap(n));
                return;
            }
            if (key == "view")
            {
                string v = value as string;
                if (v == null || Prefs.KnownView(v) != v) throw new Exception("нужно одно из: " + string.Join(", ", Prefs.Views));
                Prefs.Set(key, v);
                return;
            }
            if (key == "replace" || key == "sameApp" || key == "games" || key == "allDesktops")
            {
                if (!(value is bool)) throw new Exception("нужно «да» или «нет»");
                Prefs.Set(key, (bool)value);
                return;
            }
            throw new Exception("такой настройки нет");
        }

        public static bool ReadReduced(object theme)
        {
            var d = theme as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue("reducedMotion", out v) && v is bool && (bool)v;
        }

        public static void Start(Dispatcher dispatcher)
        {
            if (link != null) return;
            ui = dispatcher;
            link = new HubLink(Program.Id, Program.Name, Program.VersionText, TestPort > 0 ? TestPort : HubLink.DefaultPort);
            link.Topics = new string[] { "hub.theme" };
            link.Commands = new string[] { "settings.set", "show" };
            link.OnCommand = Command;
            link.OnState = delegate(string topic, object data, string from)
            {
                if (topic != "hub.theme") return;
                bool reduced = ReadReduced(data);
                ui.BeginInvoke(new Action(delegate { Ui.ForceReducedMotion = reduced; }));
            };
            link.OnStatus = delegate(bool on) { ui.BeginInvoke(new Action(delegate { if (StatusChanged != null) StatusChanged(); })); };
            // Семья: core.family, hub.apps и tray.run. Встаёт поверх своих обработчиков — и до link.Start()
            family = new Family(link, Program.Id, delegate { ui.BeginInvoke(new Action(delegate { if (Quit != null) Quit(); })); });
            family.Changed = delegate
            {
                Aside.Recheck();   // core.family.game мог поменяться — не ждём опроса
                ui.BeginInvoke(new Action(delegate { if (FamilyChanged != null) FamilyChanged(); }));
            };
            Prefs.Changed += Publish;   // настройку поменяли — страница в core.txt тоже обновится
            link.Start();
            Publish();                  // хаба ещё нет — клиент запомнит и повторит после подключения
        }

        public static void Stop()
        {
            if (link != null) link.Stop();
        }

        public static void Publish()
        {
            published++;
            if (link != null) link.State(Topic, Settings());
            // Галочка «Приостановить» и включённость клавиш зависят от тех же настроек; одинаковое Family второй раз не шлёт
            if (family != null) { family.SetTray(Tray()); family.SetKeys(Keys()); }
        }

        // Просьба к core.txt показать своё окно; page — какую страницу (SettingsPage — настройки smena.txt)
        public static void ShowCore() { ShowCore(null); }

        public static void ShowCore(string page)
        {
            if (link != null) link.Command("core", "show", page == null ? null : HubLink.Msg("page", page));
        }

        // Команда «show»: что открыть, решает ShowRoute. Вызывается в потоке окна.
        // В ответе shown — показано ли что-нибудь, opened — что: switcher, about или core (страница smena.txt в core.txt).
        public static Dictionary<string, object> ShowCommand(Dictionary<string, object> args)
        {
            ShowTarget to = ShowRoute.For(args, family != null && family.Has("core", "show"));
            if (to == ShowTarget.CorePage)
            {
                ShowCore(SettingsPage);
                return HubLink.Msg("shown", true, "opened", "core");
            }
            if (to == ShowTarget.About)
            {
                bool can = Open != null;   // без окна (--headless) открыть нечего
                if (can) Open();
                return HubLink.Msg("shown", can, "opened", "about");
            }
            // В стороне переключатель не показывается: идёт игра (reason — почему, для подписи)
            if (Aside.On) return HubLink.Msg("shown", false, "opened", "switcher", "reason", Aside.Text + " — smena.txt в стороне, работает обычный Alt+Tab");
            return HubLink.Msg("shown", Show != null && Show(), "opened", "switcher");
        }

        // core.txt сказал, что идёт игра (core.family.game); из любого потока
        public static bool CoreGame { get { return family != null && family.Game; } }

        // Приходит в фоновом потоке
        static object Command(string name, Dictionary<string, object> args, string from)
        {
            object result = null;
            OnUi(delegate
            {
                if (name == "show") { result = ShowCommand(args); return; }
                object key, value;
                args.TryGetValue("key", out key);
                args.TryGetValue("value", out value);
                int before = published;
                try { Apply(key as string, value); }
                finally { if (published == before) Publish(); }   // core.txt ждёт полную картину, даже если ничего не изменилось
            });
            return result;
        }
    }
}
