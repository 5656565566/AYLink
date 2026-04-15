using System;
using AYLink.Desktop.Services;
using AYLink.Desktop.ViewModels.Pages;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.ViewModels.Design;

/// <summary>
/// 仅用于设计时预览的任务管理页 ViewModel
/// </summary>
public class DesignTaskCenterPageViewModel : TaskCenterPageViewModel
{
    public static DesignTaskCenterPageViewModel DesignInstance { get; } = new();

    public DesignTaskCenterPageViewModel()
    {
        var service = TaskCenterService.Instance;

        // 仅在空的时候注入假数据 避免重复执行
        if (service.Tasks.Count == 0)
        {
            var now = DateTimeOffset.Now;
            
            service.Tasks.Add(new ManagedTaskItem
            {
                Title = "同步照片到电脑",
                Description = "正在从 Pixel 设备导出照片",
                Detail = "已处理 328 / 1200 张照片",
                Source = "Pixel 8 Pro",
                Status = ManagedTaskStatus.Running,
                Severity = InfoBarSeverity.Informational,
                Progress = 27,
                ShowProgress = true,
                IsIndeterminate = false,
                IsCancelable = true,
                CreatedAt = now.AddMinutes(-6),
                StartedAt = now.AddMinutes(-6),
                CancelAction = () => { }
            });

            service.Tasks.Add(new ManagedTaskItem
            {
                Title = "安装测试 APK",
                Description = "向 Redmi K70 安装调试包",
                Detail = "正在安装 base.apk",
                Source = "Redmi K70",
                Status = ManagedTaskStatus.Running,
                Severity = InfoBarSeverity.Informational,
                Progress = 82,
                ShowProgress = true,
                IsIndeterminate = false,
                IsCancelable = true,
                CreatedAt = now.AddMinutes(-3),
                StartedAt = now.AddMinutes(-3),
                CancelAction = () => { }
            });

            service.Tasks.Add(new ManagedTaskItem
            {
                Title = "抓取应用列表",
                Description = "读取设备应用清单",
                Detail = "已完成，共 214 个应用",
                Source = "OnePlus 12",
                Status = ManagedTaskStatus.Completed,
                Severity = InfoBarSeverity.Success,
                Progress = 100,
                ShowProgress = true,
                IsIndeterminate = false,
                IsCancelable = false,
                CreatedAt = now.AddMinutes(-15),
                StartedAt = now.AddMinutes(-14),
                FinishedAt = now.AddMinutes(-13)
            });

            service.Tasks.Add(new ManagedTaskItem
            {
                Title = "导出日志包",
                Description = "生成最近 7 天运行日志",
                Detail = "用户手动取消",
                Source = "Galaxy S24",
                Status = ManagedTaskStatus.Cancelled,
                Severity = InfoBarSeverity.Warning,
                Progress = 41,
                ShowProgress = true,
                IsIndeterminate = false,
                IsCancelable = false,
                CreatedAt = now.AddMinutes(-25),
                StartedAt = now.AddMinutes(-24),
                FinishedAt = now.AddMinutes(-23)
            });

            service.Tasks.Add(new ManagedTaskItem
            {
                Title = "备份应用数据",
                Description = "导出指定应用的私有数据",
                Detail = "/sdcard/Android/data 访问失败",
                Source = "MIX Fold 3",
                Status = ManagedTaskStatus.Failed,
                Severity = InfoBarSeverity.Error,
                Progress = 64,
                ShowProgress = true,
                IsIndeterminate = false,
                IsCancelable = false,
                CreatedAt = now.AddMinutes(-11),
                StartedAt = now.AddMinutes(-10),
                FinishedAt = now.AddMinutes(-9)
            });

            service.Tasks.Add(new ManagedTaskItem
            {
                Title = "无线连接设备",
                Description = "通过 Wi-Fi 调试连接设备",
                Detail = "等待设备确认配对",
                Source = "vivo X100",
                Status = ManagedTaskStatus.Running,
                Severity = InfoBarSeverity.Informational,
                Progress = 0,
                ShowProgress = true,
                IsIndeterminate = true,
                IsCancelable = true,
                CreatedAt = now.AddMinutes(-1),
                StartedAt = now.AddMinutes(-1),
                CancelAction = () => { }
            });
        }
        OnNavigatedTo(null);
    }
}
