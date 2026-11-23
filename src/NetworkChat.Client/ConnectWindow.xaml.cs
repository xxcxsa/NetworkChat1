using System.Windows;

namespace NetworkChat.Client;

public partial class ConnectWindow : Window
{
    public string UserName { get; private set; } = "";
    public string Host { get; private set; } = "127.0.0.1";
    public int TcpPort { get; private set; } = 5000;
    public int UdpPort { get; private set; } = 5001;

    public ConnectWindow() => InitializeComponent();

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) ||
            !int.TryParse(TcpPortBox.Text, out var tcp) ||
            !int.TryParse(UdpPortBox.Text, out var udp) ||
            tcp is < 1 or > 65535 || udp is < 1 or > 65535)
        {
            MessageBox.Show("Проверьте имя и номера портов.");
            return;
        }
        UserName = NameBox.Text.Trim();
        Host = HostBox.Text.Trim();
        TcpPort = tcp;
        UdpPort = udp;
        DialogResult = true;
    }
}
