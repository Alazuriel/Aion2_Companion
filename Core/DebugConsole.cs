using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace AION_2_Companion.Core;

public static class DebugConsole
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    private const int TimeColumnWidth = 12;
    private const int BadgeWidth = 14;
    private const int IndentWidth = TimeColumnWidth + BadgeWidth + 2;
    private const long MaxLogFileBytes = 5L * 1024 * 1024;
    private const int MaxLogFiles = 3;
    private const int FlushEveryLines = 50;

    private static readonly object FileLogSync = new();
    private static StreamWriter? _fileWriter;
    private static int _unflushedLines;

    private static readonly HashSet<string> Categories =
        new(StringComparer.OrdinalIgnoreCase);

    // Kategória → badge háttérszín (RGB)
    private static readonly Dictionary<string, (int R, int G, int B)> CategoryColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["damage"]  = (255, 135, 55),   // narancs
            ["replay"]  = (167, 139, 250),  // lila
            ["summon"]  = (52, 211, 153),   // zöld
            ["spawn"]   = (132, 204, 22),   // lime
            ["party"]   = (96, 165, 250),   // kék
            ["skill"]   = (250, 204, 21),   // sárga
            ["dpstime"] = (34, 211, 238),   // cián
            ["class"]   = (244, 114, 182),  // pink
            ["self"]    = (148, 163, 184),  // szürke-kék
            ["forgedata"] = (56, 189, 248),
            ["icon"]    = (120, 113, 108),  // barna-szürke
            ["map"]     = (34, 211, 238),   // cián
        };

    private static readonly (int R, int G, int B) DefaultColor = (100, 116, 139);

    public static bool Enabled { get; private set; }

    public static bool FileLoggingEnabled
    {
        get
        {
            lock (FileLogSync)
                return _fileWriter is not null;
        }
    }

    public static string LogDirectory => Path.Combine(CompanionSettings.SettingsDirectory, "Logs");

    public static void Initialize(string[] args)
    {
        Enabled = Array.Exists(args, arg =>
            arg.Equals("--debug", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-debug", StringComparison.OrdinalIgnoreCase));

        if (!Enabled)
            return;

        foreach (string arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
                Categories.Add(arg[2..]);
            else if (arg.StartsWith("-", StringComparison.Ordinal))
                Categories.Add(arg[1..]);
        }

        Categories.Remove("debug");
        Categories.Remove("auto");

        AllocConsole();
        EnableVirtualTerminal();

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

        Console.Title = "AION 2 Companion | Debug Console";

        PrintBanner();
    }

    public static void Log(string category, string message)
    {
        bool consoleAllowed = Enabled && !(Categories.Count > 0 &&
            !Categories.Contains("all") &&
            !Categories.Contains(category));

        if (!consoleAllowed && !FileLoggingEnabled)
            return;

        string time = DateTime.Now.ToString("HH:mm:ss.fff");
        var color = CategoryColors.TryGetValue(category, out var c) ? c : DefaultColor;

        string label = category.ToUpperInvariant();
        string body = message;

        // Ha az üzenet eleje [TAG], azt használjuk badge-nek, és nem ismételjük meg.
        if (message.StartsWith('['))
        {
            int end = message.IndexOf(']');
            if (end > 1)
            {
                label = message[1..end].ToUpperInvariant();
                body = message[(end + 1)..].TrimStart();
            }
        }

        string line;

        // Folytató sor (pl. "    └ Csapás: 9x"): nincs badge, behúzással megy alá.
        if (message.StartsWith(' '))
        {
            line = new string(' ', IndentWidth) + Ansi.Fg(color) + message.TrimStart() + Ansi.Reset;
        }
        else
        {
            string badge = Ansi.Bg(color) + Ansi.Fg((15, 18, 30)) + Ansi.Bold
                         + $" {Fit(label, BadgeWidth - 2)} " + Ansi.Reset;

            line = Ansi.Dim + time.PadRight(TimeColumnWidth) + Ansi.Reset
                 + badge + " " + body;
        }

        if (consoleAllowed)
        {
            Console.WriteLine(line);
            Debug.WriteLine($"[{label}] {body}");
        }

        WriteToFile(time, label, body);
    }

    public static void ConfigureFileLogging(bool enabled)
    {
        lock (FileLogSync)
        {
            if (!enabled)
            {
                CloseFileWriter();
                return;
            }

            if (_fileWriter is not null)
                return;

            try
            {
                OpenNewLogFile();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CloseFileWriter();
                Debug.WriteLine($"[LOG] A fájllog nem indítható: {ex.Message}");
            }
        }
    }

    public static void Shutdown()
    {
        lock (FileLogSync)
            CloseFileWriter();
    }

    private static void WriteToFile(string time, string label, string body)
    {
        lock (FileLogSync)
        {
            if (_fileWriter is null)
                return;

            try
            {
                if (_fileWriter.BaseStream.Position >= MaxLogFileBytes)
                {
                    CloseFileWriter();
                    OpenNewLogFile();
                }

                _fileWriter.WriteLine($"{DateTime.Now:yyyy-MM-dd} {time} [{label}] {body}");
                _unflushedLines++;

                if (_unflushedLines >= FlushEveryLines)
                {
                    _fileWriter.Flush();
                    _unflushedLines = 0;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                CloseFileWriter();
                Debug.WriteLine($"[LOG] A fájllog leállt: {ex.Message}");
            }
        }
    }

    private static void OpenNewLogFile()
    {
        Directory.CreateDirectory(LogDirectory);

        string path = Path.Combine(
            LogDirectory,
            $"companion-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.log");

        _fileWriter = new StreamWriter(new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read));
        _unflushedLines = 0;

        foreach (string oldFile in Directory
                     .GetFiles(LogDirectory, "companion-*.log")
                     .Where(file => !string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(MaxLogFiles - 1))
        {
            try
            {
                File.Delete(oldFile);
            }
            catch (IOException)
            {
                // A következő indításnál újra megpróbáljuk.
            }
            catch (UnauthorizedAccessException)
            {
                // A logolás ettől még működhet, ezért nem állítjuk le.
            }
        }
    }

    private static void CloseFileWriter()
    {
        try
        {
            _fileWriter?.Flush();
            _fileWriter?.Dispose();
        }
        catch (IOException)
        {
            // Leálláskor nincs további teendő.
        }
        finally
        {
            _fileWriter = null;
            _unflushedLines = 0;
        }
    }

    private static string Fit(string text, int width)
    {
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }

    private static void EnableVirtualTerminal()
    {
        IntPtr handle = GetStdHandle(StdOutputHandle);

        if (handle == IntPtr.Zero)
            return;

        if (GetConsoleMode(handle, out uint mode))
            SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    }

    private static void PrintBanner()
    {
        const string top    = "╭──────────────────────────────────────────────╮";
        const string bottom = "╰──────────────────────────────────────────────╯";

        string cats = Categories.Count == 0
            ? "minden kategória"
            : string.Join(", ", Categories);

        Console.WriteLine(Ansi.Fg((250, 204, 21)) + top + Ansi.Reset);
        Console.WriteLine(Ansi.Fg((250, 204, 21)) + "│ " + Ansi.Reset
            + Ansi.Bold + "AION 2 Companion · Debug" + Ansi.Reset
            + "                     " + Ansi.Fg((250, 204, 21)) + "│" + Ansi.Reset);
        Console.WriteLine(Ansi.Fg((250, 204, 21)) + bottom + Ansi.Reset);
        Console.WriteLine(Ansi.Dim + "Kategóriák: " + Ansi.Reset + cats);
        Console.WriteLine();
    }

    private static class Ansi
    {
        public const string Reset = "\x1b[0m";
        public const string Bold = "\x1b[1m";
        public const string Dim = "\x1b[2m";

        public static string Fg((int R, int G, int B) c) => $"\x1b[38;2;{c.R};{c.G};{c.B}m";
        public static string Bg((int R, int G, int B) c) => $"\x1b[48;2;{c.R};{c.G};{c.B}m";
    }
}
