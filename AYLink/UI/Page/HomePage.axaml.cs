using AYLink.ADB;
using AYLink.Scrcpy;
using AYLink.UI.Themes;
using AYLink.UIModel;
using AYLink.Utils;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AYLink.UI;

public partial class HomePage : UserControl
{
    public event Action<string, DeviceModel?>? RequestNavigate;
    private readonly AdbManager adbManager = AdbManager.Instance;
    private readonly BackgroundEventManager backgroundEventManager = new();

    public HomePage()
    {
        InitializeComponent();

        AddDeviceButton.Click += OnAddDeviceClicked;
        Refresh.Click += Refresh_Click;

        _ = RefreshDevices();

        backgroundEventManager.AddAsyncEvent("RefreshDevices", RefreshDevices, 5000);
        backgroundEventManager.StartAllEvents();
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshDevices();
    }

    private async void OnAddDeviceClicked(object? sender, RoutedEventArgs e)
    {
        var dialogContent = new AddDeviceDialog();

        var dialog = new ContentDialog
        {
            Title = L.Tr("HomePage_Dialog_AddDevice_Title"),
            Content = dialogContent,
            PrimaryButtonText = L.Tr("HomePage_Dialog_Connect"),
            SecondaryButtonText = L.Tr("HomePage_Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            string? ip = dialogContent.IpAddress;
            int port = int.TryParse(dialogContent.Port, out int tempPort) ? tempPort : 5555;
            string? pairPort = dialogContent.PairPort;
            string? pairCode = dialogContent.PairCode;

            if (string.IsNullOrEmpty(ip))
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Dialog_Error_Title"), L.Tr("HomePage_Dialog_Error_IpEmpty"));
                return;
            }

            bool success = int.TryParse(pairPort, out int pairIntPort);
            if (!success)
            {
                pairPort = "";
            }

            if (!string.IsNullOrEmpty(pairPort) && !string.IsNullOrEmpty(pairCode))
            {
                bool pairSuccess = AdbManager.PairWifiDevice(ip, pairIntPort, pairCode);
                if (!pairSuccess)
                {
                    await DialogHelper.MessageShowAsync(L.Tr("HomePage_Dialog_Failure_Title"), L.Tr("HomePage_Dialog_Failure_Pairing"));
                    return;
                }
            }

            var deviceModel = adbManager.ConnectDevice(ip, port);
            if (deviceModel == null)
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Dialog_Failure_Title"), L.Tr("HomePage_Dialog_Failure_Connection"));
            }
            else
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Dialog_Success_Title"), L.Tr("HomePage_Dialog_Success_Connection"));
            }
        }
    }

    public async Task RefreshDevices()
    {
        await adbManager.RefreshConnectedDevices();
        RenderDeviceList(adbManager.GetConnectedDevices());
    }

    public void RenderDeviceList(List<DeviceModel> devices)
    {
        var tip = this.FindControl<StackPanel>("Tip");
        if (tip != null)
        {
            bool hasDevices = devices != null && devices.Count > 0;
            tip.IsVisible = !hasDevices;
            DeviceList.IsVisible = hasDevices;
        }

        if (devices == null || devices.Count == 0)
        {
            DeviceListBox.Items.Clear();
            return;
        }

        var deviceMap = devices.ToDictionary(d => d.Serial);

        var itemsToRemove = DeviceListBox.Items
            .OfType<ListBoxItem>()
            .Where(item => item.Name != null && !deviceMap.ContainsKey(item.Name))
            .Cast<object>()
            .ToList();

        foreach (var item in itemsToRemove)
        {
            DeviceListBox.Items.Remove(item);
        }

        var existingSerialsInUI = DeviceListBox.Items
            .OfType<ListBoxItem>()
            .Select(item => item.Name)
            .Where(name => name != null)
            .ToHashSet();

        foreach (var device in devices)
        {
            if (existingSerialsInUI.Contains(device.Serial))
            {
                var deviceItem = DeviceListBox.Items
                    .OfType<ListBoxItem>()
                    .FirstOrDefault(item => item.Name == device.Serial);
                if (deviceItem != null)
                {
                    UpdateDeviceListItem(deviceItem, device);
                }
            }
            else
            {
                DeviceListBox.Items.Add(CreateDeviceListItem(device));
            }
        }

        var separatorsToRemove = DeviceListBox.Items.OfType<Separator>().ToList();
        foreach (var sep in separatorsToRemove)
        {
            DeviceListBox.Items.Remove(sep);
        }

        if (DeviceListBox.Items.Count > 0)
        {
            for (int i = DeviceListBox.Items.Count - 2; i >= 0; i--)
            {
                if (DeviceListBox.Items[i + 1] is not Separator)
                {
                    DeviceListBox.Items.Insert(i + 1, new Separator());
                }
            }
        }
    }

    private static void UpdateDeviceListItem(ListBoxItem item, DeviceModel device)
    {
        var grid = (Grid)item.Content!;
        grid.DataContext = device;

        item.IsEnabled = device.IsConnected;

        if (grid.Children[1] is StackPanel connectionPanel &&
            connectionPanel.Children[0] is SymbolIcon connectionIcon)
        {
            connectionIcon.Symbol = device.ConnectionType switch
            {
                "WiFi" => Symbol.Wifi4,
                "USB" => Symbol.Link,
                _ => Symbol.Clear,
            };
        }
    }

    private ListBoxItem CreateDeviceListItem(DeviceModel device)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*, 1.5*, Auto"),
            DataContext = device,
        };

        var deviceInfoPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        var deviceNameText = new TextBlock
        {
            Theme = this.FindResource("BodyStrongTextBlockStyle") as ControlTheme
        };
        var deviceSerialText = new TextBlock
        {
            Opacity = 0.7,
            Theme = this.FindResource("CaptionTextBlockStyle") as ControlTheme
        };

        deviceNameText.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Name"));
        deviceSerialText.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Serial"));

        deviceInfoPanel.Children.Add(deviceNameText);
        deviceInfoPanel.Children.Add(deviceSerialText);
        Grid.SetColumn(deviceInfoPanel, 0);

        var connectionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var connectionIcon = new SymbolIcon { FontSize = 18 };
        var connectionText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        connectionIcon.Symbol = device.ConnectionType switch
        {
            "WiFi" => Symbol.Wifi4,
            "USB" => Symbol.Link,
            _ => Symbol.Clear,
        };
        connectionText.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("ConnectionType"));

        connectionPanel.Children.Add(connectionIcon);
        connectionPanel.Children.Add(connectionText);
        Grid.SetColumn(connectionPanel, 1);

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var mirrorButton = CreateActionButton(Symbol.Play, "HomePage_Action_ScreenMirror", MirrorButton_Click);
        var fileButton = CreateActionButton(Symbol.Folder, "HomePage_Action_FileTransfer", FileButton_Click);
        var appButton = CreateActionButton(Symbol.Repair, "HomePage_Action_AppManagement", AppButton_Click);
        var moreButton = CreateActionButton(Symbol.More, "HomePage_Action_More", MoreButton_Click);

        var shellMenu = new MenuItem();
        shellMenu.BindLocalized(MenuItem.HeaderProperty, "HomePage_Action_OpenConsole");

        var settingMenu = new MenuItem();
        settingMenu.BindLocalized(MenuItem.HeaderProperty, "HomePage_Action_DeviceSettings");

        var listEncoderMenu = new MenuItem();
        listEncoderMenu.BindLocalized(MenuItem.HeaderProperty, "HomePage_Action_ListEncoder");

        var newDisplayMenu = new MenuItem();
        newDisplayMenu.BindLocalized(MenuItem.HeaderProperty, "HomePage_Action_NewDisplay");

        shellMenu.Click += ShellMenu_Click;
        shellMenu.Tag = grid.DataContext;

        settingMenu.Click += SettingMenu_Click;
        settingMenu.Tag = grid.DataContext;

        listEncoderMenu.Click += ListEncoderMenu_Click;
        listEncoderMenu.Tag = grid.DataContext;

        newDisplayMenu.Click += NewDisplayMenu_Click;
        newDisplayMenu.Tag = grid.DataContext;

        moreButton.ContextFlyout = new MenuFlyout()
        {
            Items = { newDisplayMenu, shellMenu, settingMenu, listEncoderMenu }
        };

        actionsPanel.Children.Add(mirrorButton);
        actionsPanel.Children.Add(fileButton);
        actionsPanel.Children.Add(appButton);
        actionsPanel.Children.Add(moreButton);
        Grid.SetColumn(actionsPanel, 2);

        grid.Children.Add(deviceInfoPanel);
        grid.Children.Add(connectionPanel);
        grid.Children.Add(actionsPanel);

        var listBoxItem = new ListBoxItem
        {
            Content = grid,
            Padding = new Thickness(16, 12),
            IsEnabled = device.IsConnected,
            Name = device.Serial
        };

        return listBoxItem;
    }

    private async void NewDisplayMenu_Click(object? sender, RoutedEventArgs e)
    {

        if (sender is MenuItem menuItem && menuItem.Tag is DeviceModel device)
        {
            if (!adbManager.IsDeviceTrulyOnline(device.Serial))
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Warning"), L.Tr("HomePage_Warning_NoDevice"));
                return;
            }

            device.ServerOptions = new ServerOptions
            {
                DisplayId = -1
            };
            RequestNavigate?.Invoke("ScreenPage", device);
        }
    }

    private async void ListEncoderMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is DeviceModel device)
        {
            if (!adbManager.IsDeviceTrulyOnline(device.Serial))
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Warning"), L.Tr("HomePage_Warning_NoDevice"));
                return;
            }

            ScrcpyTool scrcpyTool = new(device);
            await DialogHelper.MessageShowAsync(L.Tr("HomePage_Dialog_ListEncoder"), string.Join('\n', scrcpyTool.GetEncoders()));
        }
        
    }

    private async void SettingMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is DeviceModel device)
        {
            if (!adbManager.IsDeviceTrulyOnline(device.Serial))
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Warning"), L.Tr("HomePage_Warning_NoDevice"));
                return;
            }

            RequestNavigate?.Invoke("SettingPage", device);
        }
    }

    private async void ShellMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is DeviceModel device)
        {
            if (!adbManager.IsDeviceTrulyOnline(device.Serial))
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Warning"), L.Tr("HomePage_Warning_NoDevice"));
                return;
            }

            RequestNavigate?.Invoke("ShellPage", device);
        }
    }

    private static Button CreateActionButton(Symbol symbol, string tooltipKey, EventHandler<RoutedEventArgs> clickHandler)
    {
        var button = new Button
        {
            Content = new SymbolIcon { Symbol = symbol },
        };

        button.BindLocalized(ToolTip.TipProperty, tooltipKey);
        button.Click += clickHandler;
        return button;
    }

    private async void MirrorButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is DeviceModel device)
        {
            if (!adbManager.IsDeviceTrulyOnline(device.Serial))
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Warning"), L.Tr("HomePage_Warning_NoDevice"));
                return;
            }

            RequestNavigate?.Invoke("ScreenPage", device);
        }
    }



    private async void FileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is DeviceModel device)
        {
            if (!adbManager.IsDeviceTrulyOnline(device.Serial))
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Warning"), L.Tr("HomePage_Warning_NoDevice"));
                return;
            }

            RequestNavigate?.Invoke("FilePage", device);
        }
    }

    private async void AppButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is DeviceModel device)
        {
            if (!adbManager.IsDeviceTrulyOnline(device.Serial))
            {
                await DialogHelper.MessageShowAsync(L.Tr("HomePage_Warning"), L.Tr("HomePage_Warning_NoDevice"));
                return;
            }

            RequestNavigate?.Invoke("AppPage", device);
        }
    }

    private void MoreButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextFlyout != null)
        {
            button.ContextFlyout.ShowAt(button);
        }
    }
}