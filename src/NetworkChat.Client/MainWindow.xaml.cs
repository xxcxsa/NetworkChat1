using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using NetworkChat.Shared.Models;
using NetworkChat.Client.Services;

namespace NetworkChat.Client;

public partial class MainWindow : Window
{
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _snapshotReceived;
    private readonly HttpClient _http = new();
    private readonly WeatherService _weatherService;
    private string _name = "";
    private string _host = "127.0.0.1";
    private int _udpPort = 5001;

    public MainWindow()
    {
        InitializeComponent();
        _weatherService = new WeatherService(_http);
        Loaded += async (_, _) => await ConnectDialogAsync();
        Closed += async (_, _) => await DisconnectAsync();
    }

    private async Task ConnectDialogAsync()
    {
        var dialog = new ConnectWindow { Owner = this };
        if (dialog.ShowDialog() != true) { Close(); return; }

        _name = dialog.UserName;
        _host = dialog.Host;
        _udpPort = dialog.UdpPort;

        try
        {
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(dialog.Host, dialog.TcpPort);

            var stream = _tcp.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            _writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await _writer.WriteLineAsync(_name);

            _udp = new UdpClient(0);
            _cts = new CancellationTokenSource();
            _snapshotReceived = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _ = UdpReceiveLoopAsync(_cts.Token);

            var snapshotTask = _snapshotReceived.Task;
            for (var attempt = 0; attempt < 5 && !snapshotTask.IsCompleted; attempt++)
            {
                await SendUdpCommandAsync($"JOIN|{_name}");
                await Task.WhenAny(snapshotTask, Task.Delay(500));
            }

            if (!snapshotTask.IsCompleted)
                MessagesList.Items.Add("Не удалось получить UDP-список пользователей. Проверьте UDP-порт.");

            _ = ReceiveLoopAsync(_cts.Token);
            StatusText.Text = $"Подключено: {_name}";
            StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось подключиться: {ex.Message}", "NetworkChat",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(token);
                if (line is null) break;
                var packet = JsonSerializer.Deserialize<ServerPacket>(line);
                if (packet is null) continue;
                await Dispatcher.InvokeAsync(() => HandlePacket(packet));
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => MessagesList.Items.Add($"Ошибка: {ex.Message}"));
        }

        await Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = "Соединение закрыто";
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
        });
    }

    private async Task UdpReceiveLoopAsync(CancellationToken token)
    {
        if (_udp is null) return;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await _udp.ReceiveAsync(token);
                var notification = JsonSerializer.Deserialize<UdpNotification>(
                    Encoding.UTF8.GetString(result.Buffer));
                if (notification is null) continue;
                await Dispatcher.InvokeAsync(() => HandleUdpNotification(notification));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private void HandleUdpNotification(UdpNotification notification)
    {
        switch (notification.Type)
        {
            case "SNAPSHOT":
                UsersList.Items.Clear();
                foreach (var user in notification.Users)
                    UsersList.Items.Add(user);
                OnlineText.Text = $"Онлайн: {notification.OnlineCount}";
                _snapshotReceived?.TrySetResult(true);
                MessagesList.Items.Add($"Список пользователей синхронизирован ({notification.OnlineCount})");
                break;

            case "ONLINE":
                if (!string.IsNullOrWhiteSpace(notification.UserName) &&
                    !UsersList.Items.Cast<object>().Any(x => x.ToString() == notification.UserName))
                {
                    UsersList.Items.Add(notification.UserName);
                    MessagesList.Items.Add($"● {notification.UserName} ONLINE");
                }
                OnlineText.Text = $"Онлайн: {notification.OnlineCount}";
                break;

            case "OFFLINE":
                RemoveUser(notification.UserName);
                MessagesList.Items.Add($"● {notification.UserName} OFFLINE");
                OnlineText.Text = $"Онлайн: {notification.OnlineCount}";
                break;
        }
    }

    private void HandlePacket(ServerPacket packet)
    {
        switch (packet.Type)
        {
            case "message":
                MessagesList.Items.Add($"{packet.SentAt:HH:mm}  {packet.UserName}: {packet.Text}");
                break;
            case "users":
                OnlineText.Text = $"Онлайн: {packet.OnlineCount}";
                break;
            case "system":
            case "error":
                MessagesList.Items.Add(packet.Text);
                break;
        }

        if (MessagesList.Items.Count > 0)
            MessagesList.ScrollIntoView(MessagesList.Items[^1]);
    }

    private void RemoveUser(string? name)
    {
        if (name is null) return;
        var item = UsersList.Items.Cast<object>().FirstOrDefault(x => x.ToString() == name);
        if (item is not null) UsersList.Items.Remove(item);
    }

    private async Task SendUdpCommandAsync(string command)
    {
        if (_udp is null) return;
        var bytes = Encoding.UTF8.GetBytes(command);
        await _udp.SendAsync(bytes, new IPEndPoint(IPAddress.Parse(_host), _udpPort));
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await SendMessageAsync(); }
    }

    private async Task SendMessageAsync()
    {
        var text = MessageInput.Text.Trim();
        if (text.Length == 0 || _writer is null) return;

        try
        {
            await _writer.WriteLineAsync(text);
            MessagesList.Items.Add($"{DateTime.Now:HH:mm}  Вы: {text}");
            MessageInput.Clear();
        }
        catch (Exception ex) { MessagesList.Items.Add($"Ошибка отправки: {ex.Message}"); }
    }

    private async void Weather_Click(object sender, RoutedEventArgs e)
    {
        if (_writer is null)
        {
            MessageBox.Show("Сначала подключитесь к чату.", "NetworkChat",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        WeatherButton.IsEnabled = false;
        try
        {
            var weatherMessage = await _weatherService.GetYekaterinburgWeatherAsync();
            await _writer.WriteLineAsync(weatherMessage);

            MessagesList.Items.Add($"{DateTime.Now:HH:mm}  Вы: {weatherMessage}");
            MessagesList.ScrollIntoView(MessagesList.Items[^1]);
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show($"Не удалось получить погоду. Проверьте подключение к интернету.\\n{ex.Message}",
                "Погода", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка получения погоды: {ex.Message}",
                "Погода", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            WeatherButton.IsEnabled = true;
        }
    }

    private async void Stats_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var response = await _http.GetStringAsync("http://localhost:5080/api/stats");
            var stats = JsonSerializer.Deserialize<ChatStatistics>(response);
            if (stats is not null)
                MessageBox.Show($"Пользователей онлайн: {stats.OnlineUsers}\n" +
                    $"Всего сообщений: {stats.TotalMessages}\n" +
                    $"Сервер запущен: {stats.StartedAt:g}\n" +
                    $"Пользователи: {string.Join(", ", stats.Users)}",
                    "Статистика чата");
        }
        catch (Exception ex) { MessageBox.Show($"HTTP API недоступен: {ex.Message}"); }
    }

    private async Task DisconnectAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Dispose(); } catch { }
        try { _tcp?.Close(); } catch { }
        _http.Dispose();
    }
}
