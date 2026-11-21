// smena.txt. Capture.cs — живая картинка окна для WPF через Windows.Graphics.Capture (WGC).
// В отличие от миниатюр DWM, это обычный ImageSource: его можно наклонить, положить на грань в Viewport3D и т. п.
// Путь картинки: кадр WGC (текстура D3D11) → копия на видеокарте в свою общую текстуру → та же текстура,
// открытая в Direct3D9Ex → D3DImage. Через память процессора картинка не идёт.
// Запасной путь — чтение кадра через промежуточную текстуру в WriteableBitmap.
// Типы WinRT берутся из системных .winmd (ссылки в build.ps1); D3D11 и D3D9 — вызовами по таблице методов COM.
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace DevTxt.Smena
{
    // Расчёты без видеокарты: их проверяет --selftest
    static class CaptureMath
    {
        // Кадр пришёл с содержимым content (размер окна сейчас), а пул кадров заведён под pool.
        // Возвращает true, если пул пора пересоздать под новый размер; w и h — какую часть кадра копировать (0 — нечего).
        // Содержимое лежит в левом верхнем углу текстуры кадра и не бывает больше пула.
        public static bool Fit(int contentW, int contentH, int poolW, int poolH, out int w, out int h)
        {
            if (contentW <= 0 || contentH <= 0 || poolW <= 0 || poolH <= 0) { w = 0; h = 0; return false; }
            w = Math.Min(contentW, poolW);
            h = Math.Min(contentH, poolH);
            return contentW != poolW || contentH != poolH;
        }

        // Совпадает ли цвет с ожидаемым с допуском по каждому каналу
        public static bool Near(Color a, Color b, int tolerance)
        {
            return Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;
        }
    }

    // COM без описаний интерфейсов: метод берётся из таблицы объекта по номеру
    static class Com
    {
        public static T Fn<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, slot * IntPtr.Size), typeof(T)) as T;
        }

        public static IntPtr Query(IntPtr obj, Guid iid)
        {
            IntPtr r;
            return Marshal.QueryInterface(obj, ref iid, out r) == 0 ? r : IntPtr.Zero;
        }

        public static void Release(ref IntPtr p)
        {
            if (p != IntPtr.Zero) { try { Marshal.Release(p); } catch { } p = IntPtr.Zero; }
        }
    }

    // Общие на все окна устройства: D3D11 (для WGC) и D3D9Ex (для D3DImage). Создаются один раз, лениво.
    static class Gpu
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct TEXDESC
        {
            public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, Misc;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct BOX { public uint Left, Top, Front, Right, Bottom, Back; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MAPPED { public IntPtr Data; public uint RowPitch, DepthPitch; }
        [StructLayout(LayoutKind.Sequential)]
        struct PRESENT
        {
            public uint BackBufferWidth, BackBufferHeight, BackBufferFormat, BackBufferCount, MultiSampleType, MultiSampleQuality, SwapEffect;
            public IntPtr DeviceWindow;
            public int Windowed, AutoDepthStencil;
            public uint AutoDepthStencilFormat, Flags, RefreshRate, PresentationInterval;
        }

        [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out int level, out IntPtr context);
        [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
        [DllImport("d3d9.dll")] static extern int Direct3DCreate9Ex(uint sdk, out IntPtr d3d);
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();

        // ID3D11Device
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CreateTexture2DFn(IntPtr self, ref TEXDESC desc, IntPtr init, out IntPtr tex);
        // ID3D11DeviceContext
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int MapFn(IntPtr self, IntPtr res, uint sub, int type, uint flags, out MAPPED m);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void UnmapFn(IntPtr self, IntPtr res, uint sub);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void CopyRegionFn(IntPtr self, IntPtr dst, uint dstSub, uint x, uint y, uint z, IntPtr src, uint srcSub, ref BOX box);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void FlushFn(IntPtr self);
        // ID3D11Device::GetDeviceRemovedReason
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int RemovedFn(IntPtr self);
        // IDXGIResource::GetSharedHandle
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SharedHandleFn(IntPtr self, out IntPtr handle);
        // IDirect3D9Ex::CreateDeviceEx
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateDeviceExFn(IntPtr self, uint adapter, int type, IntPtr focus, uint flags, ref PRESENT pp, IntPtr mode, out IntPtr device);
        // IDirect3DDevice9Ex::CreateTexture
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTexture9Fn(IntPtr self, uint w, uint h, uint levels, uint usage, int format, int pool, out IntPtr tex, ref IntPtr shared);
        // IDirect3DTexture9::GetSurfaceLevel
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SurfaceLevelFn(IntPtr self, uint level, out IntPtr surface);

        public const uint FormatBgra = 87;   // DXGI_FORMAT_B8G8R8A8_UNORM
        static readonly Guid IidDxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        static readonly Guid IidDxgiResource = new Guid("035f3ab4-482e-4e50-b41f-8a7f8bd8960b");

        static bool tried;
        public static IntPtr Device, Context, D9;     // ID3D11Device, ID3D11DeviceContext, IDirect3DDevice9Ex
        public static IDirect3DDevice Winrt;          // то же устройство D3D11 в обёртке WinRT — для пула кадров
        public static string Problem = "";            // почему не завелось, если не завелось
        public static string Problem9 = "";           // почему не завёлся D3D9Ex (тогда только запасной путь)

        public static CreateTexture2DFn CreateTexture2D;
        public static MapFn Map;
        public static UnmapFn Unmap;
        public static CopyRegionFn CopyRegion;
        public static FlushFn Flush;

        public static bool Ready
        {
            get
            {
                if (!tried) { tried = true; try { Start(); } catch (Exception e) { Problem = "видеокарта: " + e.Message; Device = IntPtr.Zero; } }
                return Device != IntPtr.Zero && Winrt != null;
            }
        }

        static void Start()
        {
            IntPtr dev, ctx;
            int level;
            // D3D_DRIVER_TYPE_HARDWARE, D3D11_CREATE_DEVICE_BGRA_SUPPORT
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out dev, out level, out ctx);
            if (hr < 0) { Problem = "D3D11CreateDevice 0x" + hr.ToString("X8"); return; }
            IntPtr dxgi = Com.Query(dev, IidDxgiDevice);
            IntPtr insp = IntPtr.Zero;
            hr = dxgi == IntPtr.Zero ? -1 : CreateDirect3D11DeviceFromDXGIDevice(dxgi, out insp);
            Com.Release(ref dxgi);
            if (hr < 0 || insp == IntPtr.Zero) { Problem = "CreateDirect3D11DeviceFromDXGIDevice 0x" + hr.ToString("X8"); Com.Release(ref ctx); Com.Release(ref dev); return; }
            Winrt = (IDirect3DDevice)Marshal.GetObjectForIUnknown(insp);
            Com.Release(ref insp);
            CreateTexture2D = Com.Fn<CreateTexture2DFn>(dev, 5);
            Map = Com.Fn<MapFn>(ctx, 14);
            Unmap = Com.Fn<UnmapFn>(ctx, 15);
            CopyRegion = Com.Fn<CopyRegionFn>(ctx, 46);
            Flush = Com.Fn<FlushFn>(ctx, 111);
            Device = dev;
            Context = ctx;
            try { Start9(); } catch (Exception e) { Problem9 = "D3D9Ex: " + e.Message; Com.Release(ref D9); }
        }

        static void Start9()
        {
            IntPtr d3d;
            int hr = Direct3DCreate9Ex(32, out d3d);
            if (hr < 0 || d3d == IntPtr.Zero) { Problem9 = "Direct3DCreate9Ex 0x" + hr.ToString("X8"); return; }
            var pp = new PRESENT
            {
                BackBufferWidth = 1, BackBufferHeight = 1, BackBufferFormat = 0, BackBufferCount = 1,
                SwapEffect = 1, DeviceWindow = GetDesktopWindow(), Windowed = 1, PresentationInterval = 0x80000000
            };
            IntPtr d9;
            // D3DDEVTYPE_HAL; HARDWARE_VERTEXPROCESSING | MULTITHREADED | FPU_PRESERVE
            hr = Com.Fn<CreateDeviceExFn>(d3d, 20)(d3d, 0, 1, pp.DeviceWindow, 0x40 | 0x4 | 0x2, ref pp, IntPtr.Zero, out d9);
            Com.Release(ref d3d);
            if (hr < 0) { Problem9 = "CreateDeviceEx 0x" + hr.ToString("X8"); return; }
            D9 = d9;
        }

        // Видеокарта на месте? Если устройство потеряно — false
        public static bool Alive()
        {
            if (Device == IntPtr.Zero) return false;
            try { return Com.Fn<RemovedFn>(Device, 39)(Device) == 0; } catch { return false; }
        }

        public static IntPtr Texture(uint w, uint h, bool shared, bool staging)
        {
            var d = new TEXDESC { Width = w, Height = h, MipLevels = 1, ArraySize = 1, Format = FormatBgra, SampleCount = 1 };
            if (staging) { d.Usage = 3; d.CpuAccess = 0x20000; }          // STAGING, CPU_ACCESS_READ
            else { d.BindFlags = 0x20 | 0x8; if (shared) d.Misc = 0x2; }   // RENDER_TARGET | SHADER_RESOURCE; MISC_SHARED
            IntPtr t;
            int hr = CreateTexture2D(Device, ref d, IntPtr.Zero, out t);
            if (hr < 0) throw new COMException("CreateTexture2D", hr);
            return t;
        }

        // Общая текстура D3D11, открытая в D3D9Ex; возвращает поверхность уровня 0 для D3DImage
        public static IntPtr Open9(IntPtr tex11, uint w, uint h, out IntPtr tex9)
        {
            tex9 = IntPtr.Zero;
            IntPtr res = Com.Query(tex11, IidDxgiResource);
            if (res == IntPtr.Zero) throw new InvalidOperationException("нет IDXGIResource");
            IntPtr handle;
            int hr = Com.Fn<SharedHandleFn>(res, 8)(res, out handle);
            Com.Release(ref res);
            if (hr < 0 || handle == IntPtr.Zero) throw new COMException("GetSharedHandle", hr);
            // D3DUSAGE_RENDERTARGET, D3DFMT_A8R8G8B8, D3DPOOL_DEFAULT
            hr = Com.Fn<CreateTexture9Fn>(D9, 23)(D9, w, h, 1, 1, 21, 0, out tex9, ref handle);
            if (hr < 0) throw new COMException("CreateTexture (D3D9Ex)", hr);
            IntPtr surface;
            hr = Com.Fn<SurfaceLevelFn>(tex9, 18)(tex9, 0, out surface);
            if (hr < 0) { Com.Release(ref tex9); throw new COMException("GetSurfaceLevel", hr); }
            return surface;
        }
    }

    // Доступ к текстуре D3D11 за поверхностью WinRT
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig] int GetInterface(ref Guid iid, out IntPtr p);
    }

    // Создание GraphicsCaptureItem для HWND
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);
        [PreserveSig] int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }

    static class Capture
    {
        // Есть ли захват окон: система умеет WGC и устройство D3D11 заводится. На старой системе — просто false.
        public static bool Available
        {
            get { try { return Supported() && Gpu.Ready; } catch { return false; } }
        }

        // Умеет ли система WGC — без видеокарты: для описания настроек, устройства при этом не создаются
        public static bool SystemSupports
        {
            get { try { return Supported(); } catch { return false; } }
        }

        // Отдельный метод: на системе без WGC падает уже при подготовке вызова, а не в Available
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool Supported() { if (supported == 0) supported = GraphicsCaptureSession.IsSupported() ? 1 : -1; return supported > 0; }
        static int supported;   // 0 — ещё не спрашивали; ответ системы не меняется, а сам вызов WinRT изредка долгий

        // Почему захвата нет — для строки проверки
        public static string Problem
        {
            get
            {
                try { if (!Supported()) return "система не умеет Windows.Graphics.Capture"; }
                catch (Exception e) { return "нет Windows.Graphics.Capture: " + e.Message; }
                return Gpu.Ready ? "" : Gpu.Problem;
            }
        }
    }

    // Объекты захвата (GraphicsCaptureItem) окон и мониторов. Фабрика, CreateForWindow и CreateForMonitor — синхронные
    // вызовы WinRT, и изредка они отвечают только через 2 с (--flipprobe --real: 2 показа из 24); поток окна всё это время
    // стоял бы, а сцена — замирала на экране. Поэтому они идут в своём потоке, а поток окна ждёт ответа не дольше, чем
    // ему дали (и только на первый вопрос): не готово — Later, готовый объект он заберёт следующим вопросом.
    static class CaptureItems
    {
        public const string Later = "объект захвата ещё готовится";

        class Ask
        {
            public IntPtr Handle;
            public bool Monitor, Done;
            public GraphicsCaptureItem Item;
            public string Error;
            public DateTime At;
            public readonly ManualResetEvent Ready = new ManualResetEvent(false);
        }

        static readonly Guid IidItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");     // IGraphicsCaptureItem
        static readonly object gate = new object();
        static readonly System.Collections.Generic.Dictionary<long, Ask> asks = new System.Collections.Generic.Dictionary<long, Ask>();
        static readonly System.Collections.Generic.Queue<Ask> queue = new System.Collections.Generic.Queue<Ask>();
        static readonly AutoResetEvent signal = new AutoResetEvent(false);
        static Thread worker;
        static IGraphicsCaptureItemInterop interop;   // только в потоке worker

        static long Key(IntPtr handle, bool monitor) { return (handle.ToInt64() << 1) | (monitor ? 1L : 0L); }

        // Поток запускается заранее (вместе с устройствами видеокарты): к первому показу фабрика уже получена
        public static void Start()
        {
            lock (gate)
            {
                if (worker != null) return;
                worker = new Thread(Work) { IsBackground = true, Name = "smena capture items" };
                worker.SetApartmentState(ApartmentState.MTA);
                worker.Start();
            }
        }

        // Готовый объект (теперь он у вызывающего) или null: why — Later (спросите снова) или почему не вышло
        public static GraphicsCaptureItem Take(IntPtr handle, bool monitor, int waitMs, out string why)
        {
            Ask a;
            bool fresh = false;
            long key = Key(handle, monitor);
            lock (gate)
            {
                Sweep();
                if (!asks.TryGetValue(key, out a))
                {
                    a = new Ask { Handle = handle, Monitor = monitor };
                    asks[key] = a;
                    queue.Enqueue(a);
                    fresh = true;
                }
            }
            Start();
            if (fresh) signal.Set();
            if (fresh && waitMs > 0) a.Ready.WaitOne(waitMs);
            lock (gate)
            {
                if (!a.Done) { why = Later; return null; }
                asks.Remove(key);
            }
            a.Ready.Close();
            why = a.Error ?? "";
            return a.Item;
        }

        // Забыть все готовые и ещё не забранные объекты (smena.txt отходит в сторону). Тот, что сейчас готовится,
        // доготовится в своём потоке и уйдёт со следующей уборкой (Sweep).
        public static void Clear()
        {
            lock (gate)
            {
                var done = new System.Collections.Generic.List<long>();
                foreach (var kv in asks) if (kv.Value.Done) done.Add(kv.Key);
                foreach (long k in done)
                {
                    Ask a = asks[k];
                    asks.Remove(k);
                    try { if (a.Item != null) Marshal.ReleaseComObject(a.Item); } catch { }
                    a.Ready.Close();
                }
                // В очереди тоже не оставляем: объекты для чужих окон в стороне не нужны
                while (queue.Count > 0) { Ask a = queue.Dequeue(); asks.Remove(Key(a.Handle, a.Monitor)); try { a.Ready.Close(); } catch { } }
            }
        }

        // Готовые, но так и не забранные объекты (окно закрылось, показ кончился) не копятся
        static void Sweep()
        {
            var old = new System.Collections.Generic.List<long>();
            foreach (var kv in asks) if (kv.Value.Done && (DateTime.UtcNow - kv.Value.At).TotalSeconds > 30) old.Add(kv.Key);
            foreach (long k in old)
            {
                Ask a = asks[k];
                asks.Remove(k);
                try { if (a.Item != null) Marshal.ReleaseComObject(a.Item); } catch { }
                a.Ready.Close();
            }
        }

        static void Work()
        {
            while (true)
            {
                Ask a = null;
                lock (gate) if (queue.Count > 0) a = queue.Dequeue();
                if (a == null)
                {
                    // Фабрика — одна на всё время работы: её получение изредка стоит секунды (--flipprobe --real)
                    if (interop == null) { try { interop = (IGraphicsCaptureItemInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem)); } catch { } }
                    signal.WaitOne();
                    continue;
                }
                GraphicsCaptureItem item = null;
                string error = null;
                try
                {
                    if (interop == null) interop = (IGraphicsCaptureItemInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
                    Guid iid = IidItem;
                    IntPtr p;
                    int hr = a.Monitor ? interop.CreateForMonitor(a.Handle, ref iid, out p) : interop.CreateForWindow(a.Handle, ref iid, out p);
                    if (hr < 0 || p == IntPtr.Zero) error = (a.Monitor ? "монитор не захватывается (CreateForMonitor 0x" : "окно не захватывается (CreateForWindow 0x") + hr.ToString("X8") + ")";
                    else
                    {
                        try { item = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(p); }
                        finally { Marshal.Release(p); }
                    }
                }
                catch (Exception e) { error = e.Message; }
                lock (gate) { a.Item = item; a.Error = error; a.At = DateTime.UtcNow; a.Done = true; }
                try { a.Ready.Set(); } catch { }
            }
        }
    }

    // Живая картинка одного окна. Кадры забираются опросом Pull() из потока окна.
    sealed class LiveWindow : IDisposable
    {
        public static bool ForceCopy;   // только для проверок: сразу запасной путь
        public static int Count;        // сколько захватов сейчас открыто (не освобождено) — для проверки утечек
        public const int QuickWait = 15; // столько поток окна ждёт объект захвата при показе (обычно он готов за миллисекунды)
        const int ProbeWait = 3000;      // а проверка без экрана — столько
        const int LockWaitMs = 0;        // картинку D3DImage не ждём вовсе: занята потоком отрисовки — заводим новую или идём запасным путём
        static readonly System.Collections.Generic.Dictionary<IntPtr, GraphicsCaptureItem> monitors = new System.Collections.Generic.Dictionary<IntPtr, GraphicsCaptureItem>();
        bool shared;             // item — общий объект монитора, его не освобождаем
        bool lost;               // D3DImage потеряла видеокарту (сон, блокировка, UAC): вернётся — картинку привязать заново

        static readonly Guid IidTexture = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");  // ID3D11Texture2D

        GraphicsCaptureItem item;
        Direct3D11CaptureFramePool pool;
        GraphicsCaptureSession session;
        int poolW, poolH;

        IntPtr tex;              // своя текстура D3D11 (общая — для D3DImage, промежуточная — для копии)
        IntPtr tex9, surface9;   // та же текстура в D3D9Ex и её поверхность
        Direct3D11CaptureFrame pending;   // кадр, который картинка ещё не приняла (D3DImage занята потоком отрисовки)
        bool staged;             // запасной путь: кадр уже копируется в промежуточную текстуру, ждём, когда её можно прочесть
        const int StillDrawing = unchecked((int)0x887A000A);   // DXGI_ERROR_WAS_STILL_DRAWING
        D3DImage d3d;
        WriteableBitmap bitmap;
        bool copy, counted;

        public ImageSource Image { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Frames { get; private set; }       // сколько кадров дошло до картинки
        public int Recreated { get; private set; }    // сколько раз пул пересоздан из-за смены размера
        public bool Dead { get; private set; }
        public bool Staged { get { return staged; } }   // кадр уже снят, но копия в память ещё не готова
        public string Error { get; private set; }
        public bool Borderless { get; private set; }  // жёлтую рамку удалось снять
        public string BorderNote { get; private set; }

        // Каким путём картинка попадает в WPF
        public string Path { get { return copy ? "копия" : "d3dimage"; } }

        LiveWindow() { Error = ""; BorderNote = ""; counted = true; Count++; lock (open) open.Add(this); }

        static readonly System.Collections.Generic.List<LiveWindow> open = new System.Collections.Generic.List<LiveWindow>();   // не освобождённые — для CloseAll

        // Закрыть все захваты разом (smena.txt отходит в сторону): у кого они были, увидит Dead
        public static void CloseAll()
        {
            LiveWindow[] all;
            lock (open) all = open.ToArray();
            foreach (LiveWindow l in all) { try { l.Dispose(); } catch { } }
        }

        // Забыть сохранённые объекты захвата мониторов: при возврате они заводятся заново
        public static void ForgetMonitors()
        {
            foreach (GraphicsCaptureItem i in monitors.Values) { try { Marshal.ReleaseComObject(i); } catch { } }
            monitors.Clear();
        }

        // null — захватить окно не получилось (why — почему). Для проверок без экрана: объект захвата ждём до ProbeWait.
        public static LiveWindow Open(IntPtr hwnd)
        {
            string why;
            return Open(hwnd, out why);
        }

        public static LiveWindow Open(IntPtr hwnd, out string why) { return Open(hwnd, false, false, false, ProbeWait, out why); }

        // Весь монитор (hmonitor — HMONITOR). Для фона «Стопки 3D»: окна, спрятанные DWM, в кадр не попадают.
        // Захват идёт только без жёлтой рамки: если снять её Windows не даёт, захвата нет (why — почему).
        public static LiveWindow OpenMonitor(IntPtr hmonitor, int waitMs, out string why) { return Open(hmonitor, true, true, true, waitMs, out why); }

        // monitor — handle это HMONITOR, а не окно; borderless — без жёлтой рамки или никак; copy — сразу запасной путь через память;
        // waitMs — сколько ждать объект захвата (CaptureItems). null и why == CaptureItems.Later — ещё готовится, спросите снова.
        public static LiveWindow Open(IntPtr handle, bool monitor, bool borderless, bool copy, int waitMs, out string why)
        {
            why = "";
            if (Aside.On) { why = "smena.txt в стороне: идёт игра"; return null; }
            if (!Capture.Available) { why = Capture.Problem; return null; }
            Calls.Add(ref Calls.Captures);
            var w = new LiveWindow();
            try { w.Start(handle, monitor, borderless, copy, waitMs); return w; }
            catch (Exception e)
            {
                why = e.Message;
                bool stale = monitor && w.shared;
                w.Dispose();
                if (!stale) return null;
                // Сохранённый объект монитора устарел (монитор отключили, сменился режим) — заводим заново один раз
                monitors.Remove(handle);
                w = new LiveWindow();
                try { w.Start(handle, monitor, borderless, copy, waitMs); why = ""; return w; }
                catch (Exception e2) { why = e2.Message; w.Dispose(); return null; }
            }
        }

        void Start(IntPtr handle, bool monitor, bool borderless, bool forceCopy, int waitMs)
        {
            // Монитор: один и тот же объект на все показы
            if (monitor && monitors.TryGetValue(handle, out item)) shared = true;
            else
            {
                string why;
                item = CaptureItems.Take(handle, monitor, waitMs, out why);
                if (item == null) throw new InvalidOperationException(why);
                if (monitor) { monitors[handle] = item; shared = true; }
            }
            var size = item.Size;
            poolW = Math.Max(1, size.Width);
            poolH = Math.Max(1, size.Height);
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(Gpu.Winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, new global::Windows.Graphics.SizeInt32 { Width = poolW, Height = poolH });
            session = pool.CreateCaptureSession(item);
            try { session.IsCursorCaptureEnabled = false; } catch { }
            NoBorder();
            if (borderless && !Borderless) throw new InvalidOperationException("жёлтую рамку захвата не снять: " + BorderNote);
            copy = forceCopy || ForceCopy || Gpu.D9 == IntPtr.Zero;
            session.StartCapture();
        }

        // Жёлтая рамка вокруг захватываемого окна. Свойство есть на Windows 11; нет или не дают — идём дальше с рамкой.
        [MethodImpl(MethodImplOptions.NoInlining)]
        void NoBorder()
        {
            try
            {
                session.IsBorderRequired = false;
                Borderless = !session.IsBorderRequired;
                BorderNote = Borderless ? "снята" : "не снялась";
            }
            catch (Exception e) { Borderless = false; BorderNote = "не снята: " + e.Message; }
        }

        // Забрать свежий кадр, если есть. true — картинка обновилась.
        // Поток окна здесь не ждёт ни поток отрисовки WPF, ни видеокарту: если картинка ещё занята (D3DImage не отдана
        // потоком отрисовки) или копия в память ещё не готова, кадр откладывается до следующего вызова.
        public bool Pull()
        {
            if (Dead) return false;
            Direct3D11CaptureFrame frame = null;
            try
            {
                // Копия в память, начатая прошлым вызовом, — сначала её (и после Stop: последний кадр доходит до картинки)
                if (staged) { bool ready = Finish(); if (ready) Frames++; return ready; }
                if (pool == null) return false;
                // Берём самый свежий кадр, остальные отпускаем; отложенный — если нового нет
                while (true)
                {
                    var next = pool.TryGetNextFrame();
                    if (next == null) break;
                    if (frame != null) frame.Dispose();
                    frame = next;
                }
                if (frame == null) { frame = pending; pending = null; }
                else if (pending != null) { try { pending.Dispose(); } catch { } pending = null; }
                if (frame == null) return false;
                if (!Gpu.Alive()) { Kill("видеокарта потеряна"); return false; }

                var content = frame.ContentSize;
                int w, h;
                bool resize = CaptureMath.Fit(content.Width, content.Height, poolW, poolH, out w, out h);
                bool busy = false;
                bool done = w > 0 && Take(frame, w, h, out busy);
                if (busy) { pending = frame; frame = null; return false; }
                if (resize)
                {
                    poolW = content.Width;
                    poolH = content.Height;
                    pool.Recreate(Gpu.Winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, new global::Windows.Graphics.SizeInt32 { Width = poolW, Height = poolH });
                    Recreated++;
                }
                if (done) Frames++;
                return done;
            }
            catch (Exception e) { Kill(e.Message); return false; }
            finally { if (frame != null) { try { frame.Dispose(); } catch { } } }
        }

        // Кадр → своя текстура → картинка WPF. busy — картинку сейчас трогать нельзя, кадр надо придержать.
        bool Take(Direct3D11CaptureFrame frame, int w, int h, out bool busy)
        {
            busy = false;
            // Видеокарта у D3DImage пропала (сон монитора, блокировка экрана, UAC, смена драйвера): кадр пропускаем,
            // а когда она вернётся — картинку заводим заново (прежняя привязка к текстуре после потери не действует)
            if (!copy && d3d != null && !d3d.IsFrontBufferAvailable) { lost = true; return false; }
            if (w != Width || h != Height || tex == IntPtr.Zero || lost) { lost = false; Make(w, h); }
            bool locked = false;
            if (!copy)
            {
                if (!d3d.IsFrontBufferAvailable) { lost = true; return false; }
                // Lock ждал бы, пока поток отрисовки заберёт прошлый кадр (у больших окон — десятки и сотни мс).
                // TryLock без ожидания: занято — кадр придержим. Unlock нужен в обоих случаях (счётчик растёт всегда).
                if (!d3d.TryLock(new Duration(TimeSpan.Zero))) { d3d.Unlock(); busy = true; return false; }
                locked = true;
            }
            try
            {
                var surface = frame.Surface;
                IntPtr src = IntPtr.Zero;
                try
                {
                    var access = (IDirect3DDxgiInterfaceAccess)(object)surface;
                    Guid iid = IidTexture;
                    int hr = access.GetInterface(ref iid, out src);
                    if (hr < 0 || src == IntPtr.Zero) throw new COMException("текстура кадра", hr);
                    var box = new Gpu.BOX { Right = (uint)w, Bottom = (uint)h, Back = 1 };
                    Gpu.CopyRegion(Gpu.Context, tex, 0, 0, 0, 0, src, 0, ref box);
                }
                finally
                {
                    Com.Release(ref src);
                    try { Marshal.ReleaseComObject(surface); } catch { }
                }
                Gpu.Flush(Gpu.Context);
                if (copy) { staged = true; return Finish(); }
                d3d.AddDirtyRect(new Int32Rect(0, 0, w, h));
                return true;
            }
            finally { if (locked) d3d.Unlock(); }
        }

        // Своя текстура и картинка под размер w×h. Если D3D9Ex не принял общую текстуру — переходим на копию.
        void Make(int w, int h)
        {
            Drop();
            Width = w;
            Height = h;
            if (!copy)
            {
                try
                {
                    tex = Gpu.Texture((uint)w, (uint)h, true, false);
                    surface9 = Gpu.Open9(tex, (uint)w, (uint)h, out tex9);
                    // Lock ждал бы поток отрисовки, а без видеокарты или с упавшим потоком отрисовки — без конца.
                    // Прежнюю занятую картинку Drop уже бросил; у новой ждать нечего, и если и она занята — запасной путь.
                    if (d3d == null) d3d = new D3DImage(96, 96);
                    if (!d3d.TryLock(new Duration(TimeSpan.FromMilliseconds(LockWaitMs)))) { d3d.Unlock(); d3d = null; throw new InvalidOperationException("картинка занята потоком отрисовки"); }
                    try
                    {
                        // Запасной программный показ нужен, чтобы картинку видел и RenderTargetBitmap
                        d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface9, true);
                    }
                    finally { d3d.Unlock(); }
                    Image = d3d;
                    return;
                }
                catch (Exception e)
                {
                    Error = "d3dimage: " + e.Message;
                    Drop();
                    copy = true;
                }
            }
            tex = Gpu.Texture((uint)w, (uint)h, false, true);
            bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            Image = bitmap;
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] static extern void CopyMemory(IntPtr dst, IntPtr src, IntPtr count);

        // Запасной путь: промежуточная текстура → память → WriteableBitmap. Видеокарту не ждём: копия ещё не готова —
        // false, и следующий Pull попробует снова (staged). Большой кадр (монитор, обои) иначе держал бы поток окна.
        bool Finish()
        {
            Gpu.MAPPED m;
            int hr = Gpu.Map(Gpu.Context, tex, 0, 1, 0x100000, out m);   // D3D11_MAP_READ, D3D11_MAP_FLAG_DO_NOT_WAIT
            if (hr == StillDrawing) return false;
            staged = false;
            if (hr < 0) throw new COMException("Map", hr);
            try
            {
                bitmap.Lock();
                try
                {
                    IntPtr dst = bitmap.BackBuffer;
                    int stride = bitmap.BackBufferStride, row = Width * 4;
                    for (int y = 0; y < Height; y++)
                        CopyMemory(new IntPtr(dst.ToInt64() + (long)y * stride), new IntPtr(m.Data.ToInt64() + (long)y * m.RowPitch), new IntPtr(row));
                    bitmap.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
                }
                finally { bitmap.Unlock(); }
            }
            finally { Gpu.Unmap(Gpu.Context, tex, 0); }
            return true;
        }

        void Drop()
        {
            if (d3d != null)
            {
                // Отвязать текстуру можно только под замком, а замок ждал бы поток отрисовки (при потерянной видеокарте
                // или упавшем потоке отрисовки — без конца, и поток окна вместе с ним). Не ждём: занята — картинку
                // бросаем целиком (её текстуру держит сама D3DImage, освободит сборщик мусора), новая заведётся в Make.
                bool got = false;
                try { got = d3d.TryLock(new Duration(TimeSpan.FromMilliseconds(LockWaitMs))); if (got) d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero); } catch { }
                try { d3d.Unlock(); } catch { }   // и после неудачного TryLock: счётчик замка растёт всегда
                if (!got) d3d = null;
            }
            Com.Release(ref surface9);
            Com.Release(ref tex9);
            Com.Release(ref tex);
            bitmap = null;
            staged = false;
        }

        void DropPending()
        {
            if (pending != null) { try { pending.Dispose(); } catch { } pending = null; }
        }

        void Kill(string why)
        {
            Dead = true;
            Error = why;
        }

        // Захват остановлен, последний кадр остаётся картинкой (кадр монитора для фона). Освобождает всё — Dispose.
        public void Stop()
        {
            DropPending();
            try { if (session != null) session.Dispose(); } catch { }
            try { if (pool != null) pool.Dispose(); } catch { }
            try { if (session != null) Marshal.ReleaseComObject(session); } catch { }
            try { if (pool != null) Marshal.ReleaseComObject(pool); } catch { }
            try { if (item != null && !shared) Marshal.ReleaseComObject(item); } catch { }
            session = null; pool = null; item = null;
        }

        public void Dispose()
        {
            Dead = true;
            DropPending();
            try { if (session != null) session.Dispose(); } catch { }
            try { if (pool != null) pool.Dispose(); } catch { }
            try { if (session != null) Marshal.ReleaseComObject(session); } catch { }
            try { if (pool != null) Marshal.ReleaseComObject(pool); } catch { }
            try { if (item != null && !shared) Marshal.ReleaseComObject(item); } catch { }
            session = null; pool = null; item = null;
            Drop();
            if (counted) { counted = false; Count--; }
            lock (open) open.Remove(this);
        }
    }

    // --capture: своё окно с известным рисунком, спрятанное DWM (на экране его не видно), захват его 2 секунды и сверка цветов
    static class CaptureProbe
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        const int GWL_EXSTYLE = -20;
        const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        const int DWMWA_CLOAK = 13, DWMWA_CLOAKED = 14;
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        // Возвращает код выхода; line — одна строка итога
        public static int Run(string png, out string line)
        {
            if (!Capture.Available) { line = "capture: нет — " + Capture.Problem; return 1; }

            // Четыре четверти известных цветов; левая верхняя мигает, чтобы шли кадры, и в конце остаётся Orange
            Color[] colors = { Tokens.Palette.Orange, Tokens.Palette.Green, Tokens.Palette.Blue, Tokens.Palette.Pink };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var parts = new Border[4];
            for (int i = 0; i < 4; i++)
            {
                parts[i] = new Border { Background = new SolidColorBrush(colors[i]) };
                Grid.SetRow(parts[i], i / 2);
                Grid.SetColumn(parts[i], i % 2);
                grid.Children.Add(parts[i]);
            }
            var win = new Window
            {
                Left = SystemParameters.WorkArea.Left, Top = SystemParameters.WorkArea.Top, Width = 640, Height = 360,
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Topmost = false, Content = grid, Title = "smena.txt capture"
            };
            LiveWindow live = null;
            try
            {
                // Окно за пределами экрана WGC не снимает (DWM его не составляет — кадр пустой). Поэтому окно стоит
                // на экране, но спрятано DWM (cloak) ещё до показа: его не видно, а DWM его рисует. Не спряталось — не показываем.
                IntPtr hwnd = new WindowInteropHelper(win).EnsureHandle();
                long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
                int on = 1, cloaked;
                if (DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref on, 4) != 0 || DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, 4) != 0 || cloaked == 0)
                { line = "capture: нет — не удалось спрятать проверочное окно, показывать его не стали"; return 1; }
                win.Show();
                string why;
                live = LiveWindow.Open(hwnd, out why);
                if (live == null) { line = "capture: нет — " + why; return 1; }

                var started = DateTime.UtcNow;
                var frame = new DispatcherFrame();
                int ticks = 0;
                bool resized = false;
                var blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                blink.Tick += delegate
                {
                    double t = (DateTime.UtcNow - started).TotalMilliseconds;
                    ticks++;
                    // В середине окно меняет размер — пул и своя текстура должны пересоздаться
                    if (!resized && t > 800) { resized = true; win.Width = 560; win.Height = 320; }
                    if (t < 1700) parts[0].Background = new SolidColorBrush(ticks % 2 == 0 ? Tokens.Palette.Orange : Tokens.Palette.Purple);
                    else parts[0].Background = new SolidColorBrush(Tokens.Palette.Orange);
                    if (t >= 2000) frame.Continue = false;
                };
                var pump = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(5) };
                pump.Tick += delegate { live.Pull(); };
                blink.Start();
                pump.Start();
                Dispatcher.PushFrame(frame);
                blink.Stop();
                // Последний кадр после того, как рисунок замер
                var settle = DateTime.UtcNow;
                var wait = new DispatcherFrame();
                var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                stop.Tick += delegate { stop.Stop(); wait.Continue = false; };
                stop.Start();
                Dispatcher.PushFrame(wait);
                pump.Stop();
                live.Pull();

                if (live.Dead) { line = "capture: нет — " + live.Error; return 1; }
                if (live.Frames == 0 || live.Image == null) { line = "capture: нет — ни одного кадра (путь=" + live.Path + ")"; return 1; }

                // Картинку рисует WPF: так проверяется вся цепочка до ImageSource
                int w = live.Width, h = live.Height;
                var img = new System.Windows.Controls.Image { Source = live.Image, Width = w, Height = h, Stretch = Stretch.Fill };
                img.Measure(new Size(w, h));
                img.Arrange(new Rect(0, 0, w, h));
                img.UpdateLayout();
                var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(img);
                var px = new byte[w * h * 4];
                bmp.CopyPixels(px, w * 4, 0);
                var wrong = new System.Collections.Generic.List<string>();
                string[] names = { "левая верхняя", "правая верхняя", "левая нижняя", "правая нижняя" };
                for (int i = 0; i < 4; i++)
                {
                    int x = (i % 2 == 0 ? w / 4 : w * 3 / 4), y = (i / 2 == 0 ? h / 4 : h * 3 / 4);
                    int o = (y * w + x) * 4;
                    var got = Color.FromRgb(px[o + 2], px[o + 1], px[o]);
                    if (!CaptureMath.Near(got, colors[i], 12)) wrong.Add(names[i] + " " + got + " вместо " + colors[i]);
                }
                if (png != null)
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (var f = System.IO.File.Create(png)) enc.Save(f);
                }
                string info = "путь=" + live.Path + ", кадров=" + live.Frames + ", " + w + "x" + h + ", пересозданий=" + live.Recreated + ", рамка " + live.BorderNote
                    + (live.Error.Length > 0 ? ", замечание: " + live.Error : "");
                if (wrong.Count > 0) { line = "capture: нет — цвета не совпали (" + string.Join("; ", wrong) + "), " + info; return 1; }
                line = "capture: ok, " + info + ", цвета совпали";
                return 0;
            }
            catch (Exception e) { line = "capture: нет — " + e.GetType().Name + ": " + e.Message; return 1; }
            finally
            {
                if (live != null) live.Dispose();
                try { win.Close(); } catch { }
            }
        }
    }
}
