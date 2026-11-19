// smena.txt. AppHub.cs — связь с core.txt, лаунчером dev.txt™. Он держит на этом компьютере хаб (127.0.0.1:42080).
// Приложение отдаёт туда настройки — они появляются на общей странице настроек в core.txt — и принимает оттуда
// изменения. Оттуда же приходит общий вид: «Меньше движения» для всех приложений разом. Команда «show» один раз
// показывает переключатель, чтобы его можно было попробовать без клавиатуры.
// core.txt может быть не запущен — это нормально: приложение работает само и тихо пробует подключиться снова.
// Протокол описан в kit.txt (hub/PROTOCOL.md); HubLink.cs скопирован оттуда как есть.
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace DevTxt.Smena
{
    static class AppHub
    {
        public const string Topic = Program.Id + ".settings";

        static HubLink link;
        static Dispatcher ui;
        static int published;

        public static Func<bool> Show;              // показать переключатель: команда «show»; true — показан
        public static event Action StatusChanged;   // связь появилась или пропала; вызывается в потоке окна
        public static bool Online { get { return link != null && link.Online; } }

        // Для проверок: хаб на другом порту, рядом с настоящим core.txt
        public static int TestPort
        {
            get { int p; return int.TryParse(Environment.GetEnvironmentVariable("SMENA_HUB_PORT"), out p) && p > 0 ? p : 0; }
        }

        // Описание настроек — в том виде, в каком его ждёт core.txt: всегда целиком.
        public static Dictionary<string, object> Settings()
        {
            var keys = new object[]
            {
                HubLink.Msg("key", "replace", "type", "switch", "label", "Заменять Alt+Tab", "hint", "Вместо системного переключателя окон открывается этот", "value", Prefs.Replace),
                HubLink.Msg("key", "sameApp", "type", "switch", "label", "Alt+` — окна того же приложения", "hint", "Клавиша над Tab перебирает окна программы, в которой вы сейчас", "value", Prefs.SameApp),
                HubLink.Msg("key", "games", "type", "switch", "label", "Не вмешиваться в играх", "hint", "Когда впереди игра или другое окно на весь экран, работает обычный Alt+Tab", "value", Prefs.Games)
            };
            var view = new object[]
            {
                HubLink.Msg("key", "allDesktops", "type", "switch", "label", "Показывать окна со всех рабочих столов", "hint", "Иначе — только с текущего", "value", Prefs.AllDesktops),
                HubLink.Msg("key", "size", "type", "slider", "label", "Размер превью", "value", Prefs.Size, "min", Layout.MinPreview, "max", Layout.MaxPreview, "step", Layout.StepPreview, "unit", "px")
            };
            return HubLink.Msg("groups", new object[] { HubLink.Msg("title", "Клавиши", "items", keys), HubLink.Msg("title", "Вид", "items", view) });
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
        }

        // Просьба к core.txt показать своё окно — там общая страница настроек
        public static void ShowCore()
        {
            if (link != null) link.Command("core", "show", null);
        }

        // Приходит в фоновом потоке
        static object Command(string name, Dictionary<string, object> args, string from)
        {
            Exception failed = null;
            object result = null;
            ui.Invoke(new Action(delegate
            {
                try
                {
                    if (name == "show") { result = HubLink.Msg("shown", Show != null && Show()); return; }
                    object key, value;
                    args.TryGetValue("key", out key);
                    args.TryGetValue("value", out value);
                    int before = published;
                    try { Apply(key as string, value); }
                    finally { if (published == before) Publish(); }   // core.txt ждёт полную картину, даже если ничего не изменилось
                }
                catch (Exception e) { failed = e; }
            }));
            if (failed != null) throw failed;
            return result;
        }
    }
}
