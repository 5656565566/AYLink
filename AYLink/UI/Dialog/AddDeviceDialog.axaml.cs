using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AYLink.UI;

public partial class AddDeviceDialog : UserControl
{
    public AddDeviceDialog()
    {
        InitializeComponent();
    }

    public string IpAddress => IpAddressTextBox.Text ?? string.Empty;
    public string Port => PortTextBox.Text ?? string.Empty;
    public string PairPort => PairPortTextBox.Text ?? string.Empty;
    public string PairCode => PairCodeTextBox.Text ?? string.Empty;
}