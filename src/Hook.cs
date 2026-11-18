// smena.txt. Hook.cs — перехват клавиатуры (WH_KEYBOARD_LL), чтобы Alt+Tab доставался нам, а не системе.
// Перехватчик живёт в собственном потоке и на каждую клавишу делает самое малое: спрашивает KeyMachine,
// что она значит, отдаёт действие потоку окна (BeginInvoke, без ожидания) и возвращается. Ни окон, ни диска, ни
// ожиданий здесь нет: медленный перехватчик тормозит клавиатуру во всей системе, и Windows молча его снимает.
// Пока переключатель приостановлен или выключен в настройках, перехватчика нет вовсе.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace DevTxt.Smena
{
    static class Hook
    {
        delegate IntPtr Proc(int code, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr Hwnd; public uint Message; public IntPtr W, L; public uint Time; public int X, Y; }

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, Proc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_QUIT = 0x12;
        const int LLKHF_ALTDOWN = 0x20;

        static readonly object gate = new object();
        static readonly Proc proc = Callback;   // ссылка хранится, чтобы сборщик мусора не убрал перехватчик из-под Windows
        static Thread thread;
        static uint threadId;
        static volatile IntPtr handle;
        static KeyMachine machine;
        static Dispatcher ui;
        static Action<KeyAction> act;

        public static bool Allowed;   // false — перехватчик не ставится никогда (--no-hook, --headless, --demo, проверки)
        public static bool Installed { get { return handle != IntPtr.Zero; } }

        public static void Setup(KeyMachine m, Dispatcher dispatcher, Action<KeyAction> handler)
        {
            machine = m; ui = dispatcher; act = handler;
        }

        // Ставит или снимает перехватчик — смотря нужен ли он сейчас
        public static void Sync(bool wanted)
        {
            if (wanted && Allowed && machine != null) Start(); else Stop();
        }

        // После сна и разблокировки компьютера ставим заново: Windows могла снять перехватчик, не сказав об этом
        public static void Restart()
        {
            bool was;
            lock (gate) was = thread != null;
            if (!was) return;
            Stop();
            Start();
        }

        static void Start()
        {
            lock (gate)
            {
                if (thread != null) return;
                var ready = new ManualResetEvent(false);
                thread = new Thread(delegate()
                {
                    threadId = GetCurrentThreadId();
                    handle = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
                    ready.Set();
                    if (handle == IntPtr.Zero) return;
                    MSG msg;
                    while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }   // перехватчик вызывается, пока поток ждёт сообщений
                    UnhookWindowsHookEx(handle);
                    handle = IntPtr.Zero;
                }) { IsBackground = true, Name = "smena hook", Priority = ThreadPriority.AboveNormal };
                thread.Start();
                ready.WaitOne(2000);
            }
        }

        public static void Stop()
        {
            lock (gate)
            {
                if (thread == null) return;
                if (handle != IntPtr.Zero) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                thread.Join(1000);
                thread = null;
            }
            if (machine != null) machine.End();
        }

        static IntPtr Callback(int code, IntPtr w, IntPtr l)
        {
            try
            {
                if (code == 0)
                {
                    // KBDLLHOOKSTRUCT: vkCode, scanCode, flags, time, dwExtraInfo
                    int vk = Marshal.ReadInt32(l, 0), scan = Marshal.ReadInt32(l, 4), flags = Marshal.ReadInt32(l, 8);
                    if (Marshal.ReadIntPtr(l, 16) != Win.OurInput)
                    {
                        int m = w.ToInt32();
                        bool down = m == WM_KEYDOWN || m == WM_SYSKEYDOWN, shift = false, other = false;
                        if (down && (vk == KeyMachine.Tab || scan == KeyMachine.GraveScan))
                        {
                            shift = (GetAsyncKeyState(KeyMachine.Shift) & 0x8000) != 0;
                            other = (GetAsyncKeyState(KeyMachine.Ctrl) & 0x8000) != 0 || (GetAsyncKeyState(KeyMachine.LWin) & 0x8000) != 0 || (GetAsyncKeyState(KeyMachine.RWin) & 0x8000) != 0;
                        }
                        KeyAction action;
                        bool eat = machine.Key(vk, scan, down, (flags & LLKHF_ALTDOWN) != 0, shift, other, out action);
                        if (action != KeyAction.None) ui.BeginInvoke(DispatcherPriority.Send, act, action);
                        if (eat) return new IntPtr(1);
                    }
                }
            }
            catch { }   // что бы ни случилось, клавиша уходит дальше
            return CallNextHookEx(IntPtr.Zero, code, w, l);
        }
    }
}
