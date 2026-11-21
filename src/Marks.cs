// smena.txt. Marks.cs — отметки времени показа объёмной сцены: от нажатия до первого кадра, полёта и ухода.
// Собираются только для --flipprobe и когда задана переменная окружения SMENA_TRACE=<файл>: тогда каждый настоящий
// показ дописывает в этот файл одну строку. Без переменной ничего не считается и никакие файлы не появляются.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DevTxt.Smena
{
    static class Marks
    {
        public static readonly string File = Environment.GetEnvironmentVariable("SMENA_TRACE");
        public static bool Probe;                       // --flipprobe: отметки нужны в памяти, файл не пишется
        static readonly bool toFile = !string.IsNullOrEmpty(File);
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly List<string> order = new List<string>();
        static readonly Dictionary<string, double> at = new Dictionary<string, double>();
        static readonly List<string> notes = new List<string>();
        static double t0;
        static string label = "";
        static bool begun;

        public static bool On { get { return Probe || toFile; } }

        static double Now { get { return clock.Elapsed.TotalMilliseconds; } }

        // Нажали: отсчёт с нуля. what — что за показ (alttab, sticky, probe…)
        public static void Begin(string what)
        {
            if (!On) return;
            t0 = Now;
            label = what ?? "";
            order.Clear(); at.Clear(); notes.Clear();
            begun = true;
        }

        // Отметка «name» — только первая за показ
        public static void Hit(string name)
        {
            if (!On || !begun || at.ContainsKey(name)) return;
            order.Add(name);
            at[name] = Now - t0;
        }

        // Миллисекунды от Begin до отметки; −1 — отметки не было
        public static double Get(string name)
        {
            double v;
            return at.TryGetValue(name, out v) ? v : -1;
        }

        public static double Since { get { return begun ? Now - t0 : 0; } }

        // Значение без времени: доля полёта, число кадров и т. п.
        public static void Note(string name, double value)
        {
            if (!On || !begun) return;
            notes.Add(name + "=" + value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        // Значение строкой (без пробелов): распределение промежутков между кадрами и т. п.
        public static void Text(string name, string value)
        {
            if (!On || !begun) return;
            notes.Add(name + "=" + (value ?? "").Replace(' ', '_'));
        }

        public static string Line
        {
            get
            {
                var sb = new StringBuilder(label);
                foreach (string n in order) sb.Append(' ').Append(n).Append('=').Append(at[n].ToString("0.0", CultureInfo.InvariantCulture));
                foreach (string n in notes) sb.Append(' ').Append(n);
                return sb.ToString();
            }
        }

        // Сторож потока окна (StallGuard): отдельная строка, сразу, из любого потока; отметки показа не трогает.
        // Последняя такая строка хранится и в памяти — для --flipprobe --stall.
        static readonly object fileGate = new object();
        public static volatile string LastStall = "";
        public static void Stall(string text)
        {
            LastStall = text;
            if (!toFile) return;
            try { lock (fileGate) System.IO.File.AppendAllText(File, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " stall " + text + "\r\n"); } catch { }
        }

        // Показ закончен: строка в файл SMENA_TRACE (если задан). Только если сцена в этот раз вообще готовилась (была «warm»).
        public static void Flush()
        {
            if (!On || !begun || !at.ContainsKey("warm")) return;
            begun = false;
            if (!toFile) return;
            try { lock (fileGate) System.IO.File.AppendAllText(File, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + Line + "\r\n"); } catch { }
        }
    }
}
