using Avalonia;
using Avalonia.Controls;
using System;

namespace AYLink.UI;

public partial class ProgressDialog : UserControl
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<ProgressDialog, string>(nameof(Message), "正在处理...");

    public static readonly StyledProperty<bool> ShowProgressProperty =
        AvaloniaProperty.Register<ProgressDialog, bool>(nameof(ShowProgress), false);

    public static readonly StyledProperty<double> ProgressValueProperty =
        AvaloniaProperty.Register<ProgressDialog, double>(nameof(ProgressValue));

    public static readonly StyledProperty<string> StepTextProperty =
        AvaloniaProperty.Register<ProgressDialog, string>(nameof(StepText));

    public ProgressDialog()
    {
        InitializeComponent();
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool ShowProgress
    {
        get => GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    public double ProgressValue
    {
        get => GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, Math.Clamp(value, 0, 100));
    }

    public string StepText
    {
        get => GetValue(StepTextProperty);
        set => SetValue(StepTextProperty, value);
    }

    public event EventHandler? CancelRequested;
    public event EventHandler? RunInBackgroundRequested;


    /// <summary>
    /// 初始化：外部可替换默认事件处理器
    /// </summary>
    public void Initialize(Action? onCancel = null, Action? onRunInBackground = null)
    {
        if (onCancel != null)
            CancelRequested = (_, _) => onCancel();
        if (onRunInBackground != null)
            RunInBackgroundRequested = (_, _) => onRunInBackground();
    }

    public void UpdateMessage(string text) => Message = text;
    public void UpdateStep(string text) => StepText = text;
    public void UpdateProgress(double value)
    {
        ProgressValue = value;
        PercentText.Text = $"{(int)value}%";
        DeterminateRing.Value = value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MessageProperty)
            MessageTextBlock.Text = Message;
        else if (change.Property == StepTextProperty)
            StepTextBlock.Text = StepText;
        else if (change.Property == ShowProgressProperty || change.Property == ProgressValueProperty)
            UpdateProgressArea();
    }

    private void UpdateProgressArea()
    {
        var mode = ShowProgress;
        IndeterminateArea.IsVisible = !mode;
        DeterminateArea.IsVisible = mode;

        if (mode)
        {
            DeterminateRing.Value = ProgressValue;
            PercentText.Text = $"{(int)ProgressValue}%";
        }
    }
}