using System;

namespace AYLink.Desktop.Services.Design;

public static class DebugMockDataInitializer
{
    /// <summary>
    /// 初始化所有服务的测试数据
    /// </summary>
    public static void InitializeAll()
    {
        InitializeTask();
    }

    /// <summary>
    /// 初始化任务中心虚拟数据
    /// </summary>
    private static void InitializeTask()
    {
        var service = Tasks.TaskService.Instance;

        // 如果已经有数据了 就不重复添加
        if (service.Items.Count > 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;

        service.Start(new Tasks.TaskStartOptions
        {
            Title = "同步照片到电脑",
            Description = "正在从 Pixel 设备导出照片",
            Source = "Pixel 8 Pro",
            IsCancelable = true
        });
        
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            service.Items.Add(new Tasks.TaskItem
            {
                Title = "安装测试 APK",
                Description = "向 Redmi K70 安装调试包",
                Detail = "正在安装 base.apk",
                Source = "Redmi K70",
                Status = Tasks.TaskItemStatus.Running,
                Progress = 82,
                IsIndeterminate = false,
                IsCancelable = true,
                CreatedAt = now.AddMinutes(-3),
                CancelAction = () => { }
            });

            service.Items.Add(new Tasks.TaskItem
            {
                Title = "抓取应用列表",
                Description = "读取设备应用清单",
                Detail = "已完成，共 214 个应用",
                Source = "OnePlus 12",
                Status = Tasks.TaskItemStatus.Completed,
                Progress = 100,
                IsIndeterminate = false,
                IsCancelable = false,
                CreatedAt = now.AddMinutes(-15),
                FinishedAt = now.AddMinutes(-13)
            });

            service.Items.Add(new Tasks.TaskItem
            {
                Title = "导出日志包",
                Description = "生成最近 7 天运行日志",
                Detail = "用户手动取消",
                Source = "Galaxy S24",
                Status = Tasks.TaskItemStatus.Cancelled,
                Progress = 41,
                IsIndeterminate = false,
                IsCancelable = false,
                CreatedAt = now.AddMinutes(-25),
                FinishedAt = now.AddMinutes(-23)
            });

            service.Items.Add(new Tasks.TaskItem
            {
                Title = "备份应用数据",
                Description = "导出指定应用的私有数据",
                Detail = "/sdcard/Android/data 访问失败",
                Source = "MIX Fold 3",
                Status = Tasks.TaskItemStatus.Failed,
                Progress = 64,
                IsIndeterminate = false,
                IsCancelable = false,
                CreatedAt = now.AddMinutes(-11),
                FinishedAt = now.AddMinutes(-9)
            });
            
            service.Items.Add(new Tasks.TaskItem
            {
                Title = "无线连接设备",
                Description = "通过 Wi-Fi 调试连接设备",
                Detail = "等待设备确认配对",
                Source = "vivo X100",
                Status = Tasks.TaskItemStatus.Running,
                Progress = 0,
                IsIndeterminate = true,
                IsCancelable = true,
                CreatedAt = now.AddMinutes(-1),
                CancelAction = () => { }
            });
        });
    }
}
