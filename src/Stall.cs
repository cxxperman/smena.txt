// smena.txt. Stall.cs — сторож потока окна: страховка на случай, когда поток окна завис, а окно переключателя
// (сетка или объёмная сцена — оба во весь монитор и поверх всех) осталось на экране и глотает щелчки, а перехватчик
// (он в своём потоке) продолжает глотать Alt+Tab. Причина зависания здесь не важна.
// Сторож — свой фоновый поток. Пока идёт показ, он раз в StallWatch.CheckMs спрашивает поток окна «жив?» (сообщением
// в очередь Dispatcher); логика — StallWatch в Model.cs. Ответа нет дольше StallWatch.TripMs — сторож сам, без потока окна:
//   — прячет окна переключателя через DWM (DWMWA_CLOAK). Это работает из чужого потока при зависшем владельце и сразу
//     снимает окно и с экрана, и из-под щелчков (--flipprobe --stall). SetWindowPos, ShowWindow, SetWindowRgn,
//     SetWindowLong ждут поток-владелец, а ShowWindowAsync и SWP_ASYNCWINDOWPOS лишь встают в его очередь — не годятся;
//   — переводит KeyMachine в «пропускать всё» (Pass): Alt+Tab снова достаётся Windows;
//   — пишет строку в журнал и в трассу (Marks.Stall).
// Курсор мыши программа не прячет и не меняет нигде, поэтому возвращать его не нужно.
// Поток окна очнулся — первым делом получает ответ сторожа и сбрасывает показ (Recovered: Switcher.Recover).
// Не очнулся и за StallWatch.KillMs — программа завершает себя (Kill): лучше системный Alt+Tab, чем мёртвый перехват.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DevTxt.Smena
{
    sealed class StallGuard
    {
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static volatile bool down;

        // Какой-то сторож сработал, а поток окна ещё не сбросил показ: окна переключателя не открываются глазам
        public static bool Tripped { get { return down; } }

        readonly object gate = new object();
        readonly StallWatch watch = new StallWatch();
        readonly Dispatcher ui;
        readonly List<IntPtr> windows = new List<IntPtr>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool stopped;

        public KeyMachine Machine;          // перехватчик спрашивает её; при срабатывании — Pass
        public Func<bool> Busy;             // в потоке окна: идёт ли показ (нет — сторож засыпает до следующего Arm)
        public Action<double> Recovered;    // в потоке окна: он очнулся после срабатывания; сколько мс молчал
        public Action<double> Kill;         // в потоке сторожа: молчит дольше KillMs. Не задано — программа завершается
        public volatile int Trips, Kills;   // для --flipprobe --stall
        public volatile int TripAfter = -1; // сколько мс молчал поток окна, когда сработал сторож

        static double Now { get { return clock.Elapsed.TotalMilliseconds; } }

        public StallGuard(Dispatcher dispatcher)
        {
            ui = dispatcher;
            new Thread(Loop) { IsBackground = true, Name = "smena stall guard", Priority = ThreadPriority.AboveNormal }.Start();
        }

        // Окно, которое прятать при срабатывании
        public void Watch(IntPtr hwnd)
        {
            lock (gate) if (hwnd != IntPtr.Zero && !windows.Contains(hwnd)) windows.Add(hwnd);
        }

        // Показ начался (или перехватчик отдал окну действие): поток окна должен отвечать. Из любого потока, без ожиданий.
        public void Arm()
        {
            lock (gate) watch.Arm();
            wake.Set();
        }

        public bool Idle { get { lock (gate) return watch.Idle; } }

        public void Stop() { stopped = true; wake.Set(); }

        void Loop()
        {
            while (!stopped)
            {
                bool idle;
                lock (gate) idle = watch.Idle;
                if (idle) { wake.WaitOne(); continue; }
                Thread.Sleep(StallWatch.CheckMs);
                bool ask;
                StallStep step;
                double silent;
                lock (gate) { double now = Now; step = watch.Tick(now, out ask); silent = watch.Silent(now); }
                try
                {
                    if (ask) ui.BeginInvoke(DispatcherPriority.Send, new Action(Answer));
                    if (step == StallStep.Trip) Trip(silent);
                    else if (step == StallStep.Kill) Die(silent);
                }
                catch { }
            }
        }

        // Ответ потока окна (в потоке окна)
        void Answer()
        {
            bool was;
            double silent;
            lock (gate) { silent = watch.Silent(Now); was = watch.Answer(); }
            if (was)
            {
                string note = "поток окна очнулся через " + Math.Round(silent) + " мс; показ сброшен";
                Marks.Stall(note);
                Journal.Write("Сторож: " + note);
                try { if (Recovered != null) Recovered(silent); } catch { }
                down = false;
            }
            bool busy = true;
            try { busy = Busy == null || Busy(); } catch { }
            if (!busy) lock (gate) if (!watch.Tripped) watch.Disarm();
        }

        // В потоке сторожа: поток окна молчит дольше TripMs
        void Trip(double silent)
        {
            down = true;
            TripAfter = (int)Math.Round(silent);
            Trips++;
            IntPtr[] list;
            lock (gate) list = windows.ToArray();
            foreach (IntPtr h in list) Win.Cloak(h, true);
            if (Machine != null) Machine.Pass();
            string note = "поток окна не отвечает " + TripAfter + " мс: окна переключателя спрятаны, клавиши идут мимо smena.txt";
            Marks.Stall(note);
            Journal.Write("Сторож: " + note);
        }

        // В потоке сторожа: поток окна молчит дольше KillMs
        void Die(double silent)
        {
            Kills++;
            string note = "поток окна не отвечает " + Math.Round(silent) + " мс: smena.txt завершается, Alt+Tab остаётся системным";
            Marks.Stall(note);
            Journal.Write("Сторож: " + note);
            if (Kill != null) { Kill(silent); return; }
            // Под отладчиком остановка в точке останова — не зависание
            if (Debugger.IsAttached) return;
            try { Process.GetCurrentProcess().Kill(); } catch { Environment.Exit(3); }
        }
    }

    // --flipprobe --stall <мс>: проверка сторожа без экрана. Два своих окна 1×1 в левом верхнем углу экрана, поверх всех,
    // в своём потоке — как сцена (непрозрачное) и как сетка (прозрачное, alpha 1/255). Поток окон замирает на заданное
    // время; из третьего потока смотрим, что сторож сам спрятал окна (DWM), точка под ними больше не их (WindowFromPoint),
    // Alt+Tab идёт мимо нас, а очнувшийся поток сбросил показ и клавиши снова наши. Выход программы подменён: только отметка.
    static class StallProbe
    {
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        static Window Tiny(bool layered, int x, out IntPtr hwnd)
        {
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = x, Top = 0, Width = 1, Height = 1, Title = "smena.txt stall probe",
                // Кисти — свои: общие кисти Ui принадлежат главному потоку, а эти окна живут в своём
                AllowsTransparency = layered, Background = new SolidColorBrush(layered ? Color.FromArgb(1, 0, 0, 0) : Tokens.Dark.Bg) // audit:ok — тёмная сцена поверх экрана, от темы Windows не зависит
            };
            hwnd = new WindowInteropHelper(w).EnsureHandle();
            long ex = GetWindowLongPtr(hwnd, -20).ToInt64();
            SetWindowLongPtr(hwnd, -20, new IntPtr(ex | Logic.WS_EX_TOOLWINDOW | Logic.WS_EX_NOACTIVATE));
            w.Show();
            SetWindowPos(hwnd, new IntPtr(-1), x, 0, 1, 1, 0x0010);   // HWND_TOPMOST, SWP_NOACTIVATE
            return w;
        }

        static bool Hit(IntPtr hwnd, int x) { return GetAncestor(WindowFromPoint(new POINT { X = x, Y = 0 }), 2) == hwnd; }   // GA_ROOT

        static int Cloaked(IntPtr hwnd) { int c; return DwmGetWindowAttribute(hwnd, 14, out c, 4) == 0 ? c : -1; }   // DWMWA_CLOAKED

        // Как перехватчик: нажатие с зажатым Alt; true — проглочено
        static bool Press(KeyMachine m, int vk, bool down, out KeyAction a)
        {
            int g;
            return m.Key(vk, 0, down, true, false, false, out a, out g);
        }

        public static int Run(int stallMs, out string line)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (stallMs < StallWatch.TripMs + 800) { line = "flipprobe stall: нет — замирание должно быть не короче " + (StallWatch.TripMs + 800).ToString(inv) + " мс"; return 1; }
            Dispatcher owner = null;
            Window solid = null, glass = null;
            IntPtr hs = IntPtr.Zero, hg = IntPtr.Zero;
            var ready = new ManualResetEvent(false);
            string failure = "";
            var thread = new Thread(delegate()
            {
                try
                {
                    solid = Tiny(false, 0, out hs);
                    glass = Tiny(true, 1, out hg);
                    owner = Dispatcher.CurrentDispatcher;
                }
                catch (Exception e) { failure = e.GetType().Name + ": " + e.Message; }
                finally { ready.Set(); }
                if (owner != null) Dispatcher.Run();
            }) { IsBackground = true, Name = "stall probe windows" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.WaitOne(5000);
            StallGuard guard = null;
            try
            {
                if (owner == null) { line = "flipprobe stall: нет — проверочные окна не завелись " + failure; return 1; }
                Thread.Sleep(150);
                var machine = new KeyMachine();
                int recovered = 0;
                double wokeAfter = -1, killAfter = -1;
                bool hiddenByReset = false;
                guard = new StallGuard(owner) { Machine = machine };
                guard.Watch(hs); guard.Watch(hg);
                guard.Busy = delegate { return recovered == 0; };
                guard.Recovered = delegate(double ms)
                {
                    // То же, что Switcher.Recover: окна убраны, переключение закончено, клавиши снова наши
                    wokeAfter = ms;
                    solid.Hide(); glass.Hide();
                    hiddenByReset = !IsWindowVisible(hs) && !IsWindowVisible(hg);
                    machine.End(); machine.Resume();
                    recovered++;
                };
                guard.Kill = delegate(double ms) { killAfter = ms; };

                var wrong = new List<string>();
                // До замирания: окна видны, точка под ними — их, Alt+Tab наш
                if (!Hit(hs, 0) || !Hit(hg, 1)) wrong.Add("до замирания точка под окнами не их — проверка ничего не докажет");
                if (Cloaked(hs) != 0 || Cloaked(hg) != 0) wrong.Add("окна спрятаны ещё до замирания");
                KeyAction a;
                Press(machine, KeyMachine.Alt, true, out a);
                if (!Press(machine, KeyMachine.Tab, true, out a) || a != KeyAction.Open) wrong.Add("Alt+Tab до замирания не наш");
                guard.Arm();   // так делает перехватчик, отдавая окну действие
                Thread.Sleep(3 * StallWatch.CheckMs);
                if (guard.Trips != 0) wrong.Add("сторож сработал на живом потоке");

                // Замирание
                var sw = Stopwatch.StartNew();
                owner.BeginInvoke(DispatcherPriority.Send, new Action(delegate { Thread.Sleep(stallMs); }));
                while (guard.Trips == 0 && sw.ElapsedMilliseconds < stallMs - 300) Thread.Sleep(10);
                double tripAt = sw.Elapsed.TotalMilliseconds;
                bool tripped = guard.Trips > 0;
                if (!tripped) wrong.Add("сторож не сработал за " + Math.Round(tripAt) + " мс");
                else
                {
                    if (Cloaked(hs) == 0 || Cloaked(hg) == 0) wrong.Add("окна не спрятаны (cloaked " + Cloaked(hs) + "/" + Cloaked(hg) + ")");
                    if (Hit(hs, 0) || Hit(hg, 1)) wrong.Add("точка под окнами всё ещё их");
                    if (!machine.Passing || machine.Active) wrong.Add("KeyMachine не пропускает клавиши");
                    if (Press(machine, KeyMachine.Tab, true, out a) || a != KeyAction.None) wrong.Add("Alt+Tab при зависшем окне всё ещё глотается");
                    Press(machine, KeyMachine.Tab, false, out a);
                    if (!StallGuard.Tripped) wrong.Add("общий признак срабатывания не стоит");
                }
                bool cloakedDuring = tripped && Cloaked(hs) != 0;
                // Долгое замирание: программа вышла бы сама (здесь — только отметка)
                if (stallMs > StallWatch.KillMs + 500)
                {
                    while (guard.Kills == 0 && sw.ElapsedMilliseconds < stallMs - 200) Thread.Sleep(20);
                    if (guard.Kills != 1) wrong.Add("выход программы после " + StallWatch.KillMs.ToString(inv) + " мс не сработал");
                }
                // Поток очнулся: показ сброшен
                while (recovered == 0 && sw.ElapsedMilliseconds < stallMs + 3000) Thread.Sleep(10);
                if (recovered != 1) wrong.Add("очнувшийся поток не сбросил показ");
                else
                {
                    if (!hiddenByReset) wrong.Add("после сброса окна не убраны");
                    if (machine.Passing || machine.Active) wrong.Add("после сброса KeyMachine не в исходном состоянии");
                    Press(machine, KeyMachine.Alt, true, out a);
                    if (!Press(machine, KeyMachine.Tab, true, out a) || a != KeyAction.Open) wrong.Add("после сброса Alt+Tab не наш");
                    if (StallGuard.Tripped) wrong.Add("общий признак срабатывания не снят");
                }
                Thread.Sleep(3 * StallWatch.CheckMs);
                if (!guard.Idle) wrong.Add("сторож не уснул после сброса");
                if (stallMs <= StallWatch.KillMs && guard.Kills != 0) wrong.Add("выход программы сработал раньше времени");

                string info = "замирание " + stallMs + " мс; сторож сработал через " + guard.TripAfter + " мс молчания (порог " + StallWatch.TripMs.ToString(inv) + ", замечено пробой через " + Math.Round(tripAt) + " мс)"
                    + (cloakedDuring ? ": окна спрятаны из потока сторожа (DWMWA_CLOAK), точка под ними — уже не их, Alt+Tab шёл мимо" : "")
                    + "; поток очнулся, молчал " + Math.Round(wokeAfter) + " мс, показ сброшен"
                    + "; выход программы: " + (guard.Kills > 0 ? "сработал бы через " + Math.Round(killAfter) + " мс (в пробе подменён)" : "не понадобился")
                    + "; трасса: " + Marks.LastStall;
                line = wrong.Count == 0 ? "flipprobe stall: ok, " + info : "flipprobe stall: нет — " + string.Join("; ", wrong) + " | " + info;
                return wrong.Count == 0 ? 0 : 1;
            }
            catch (Exception e) { line = "flipprobe stall: нет — " + e.GetType().Name + ": " + e.Message; return 1; }
            finally
            {
                if (guard != null) guard.Stop();
                try
                {
                    if (owner != null)
                        owner.Invoke(new Action(delegate
                        {
                            try { if (solid != null) solid.Close(); } catch { }
                            try { if (glass != null) glass.Close(); } catch { }
                        }), TimeSpan.FromSeconds(3));
                    if (owner != null) owner.InvokeShutdown();
                }
                catch { }
            }
        }
    }
}
