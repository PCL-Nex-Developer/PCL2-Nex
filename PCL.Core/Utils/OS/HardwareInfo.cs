using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;

namespace PCL.Core.Utils.OS;

public static class HardwareInfo
{
    private static readonly object _CollectionLock = new();
    private static readonly object _StateLock = new();
    private static int _HasCollected;
    private static Task? _CollectionTask;
    
    /// <summary>
    /// 系统 CPU 信息
    /// </summary>
    public static string CPUName = "Unknown";

    /// <summary>
    /// 系统 GPU 信息
    /// </summary>
    public static IReadOnlyList<GPUInfo> GPUs { get; private set; } = [];

    /// <summary>
    /// 已安装物理内存大小，单位 MiB
    /// </summary>
    public static long SystemMemorySize = (long)KernelInterop.GetPhysicalMemoryBytes().Total / 1024 / 1024;

    public readonly record struct GPUInfo(string Name, string DriverVersion, long Memory);

    public sealed record HardwareSnapshot(string CPUName, IReadOnlyList<GPUInfo> GPUs, long SystemMemorySize);

    private static HardwareSnapshot _Snapshot = new(CPUName, GPUs, SystemMemorySize);

    /// <summary>
    /// 获取系统信息，例如 CPU 与 GPU，并存储到 CPUName 和 GPUs
    /// </summary>
    public static void GetHardwareInfo()
    {
        lock (_CollectionLock)
        {
            _Publish(_CollectHardwareInfo(Volatile.Read(ref _Snapshot)));
        }
    }

    /// <summary>
    /// 确保系统硬件信息已经获取。
    /// </summary>
    public static void EnsureHardwareInfo()
    {
        if (Volatile.Read(ref _HasCollected) != 0)
            return;

        lock (_CollectionLock)
        {
            if (Volatile.Read(ref _HasCollected) != 0)
                return;

            _Publish(_CollectHardwareInfo(Volatile.Read(ref _Snapshot)));
        }
    }

    /// <summary>
    /// 在后台获取系统硬件信息，不重复启动正在执行的查询。
    /// </summary>
    public static void BeginHardwareInfoCollection()
    {
        lock (_StateLock)
        {
            if (Volatile.Read(ref _HasCollected) != 0 || _CollectionTask is { IsCompleted: false })
                return;

            _CollectionTask = Task.Run(EnsureHardwareInfo);
        }
    }

    /// <summary>
    /// 获取一致的硬件信息快照，并可有限等待后台查询完成。
    /// </summary>
    public static HardwareSnapshot GetSnapshot(int waitMilliseconds = 0)
    {
        BeginHardwareInfoCollection();

        Task? collectionTask;
        lock (_StateLock)
            collectionTask = _CollectionTask;

        if (waitMilliseconds > 0 && collectionTask is { IsCompleted: false })
            try
            {
                collectionTask.Wait(waitMilliseconds);
            }
            catch (AggregateException ex)
            {
                LogWrapper.Warn(ex.Flatten(), "获取硬件信息时出错");
            }

        return Volatile.Read(ref _Snapshot);
    }

    private static HardwareSnapshot _CollectHardwareInfo(HardwareSnapshot previous)
    {
        // CPU
        var cpuName = previous.CPUName;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT * FROM Win32_Processor");
            foreach (ManagementObject queryObj in searcher.Get())
            {
                var queriedCpuName = queryObj["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(queriedCpuName))
                    cpuName = queriedCpuName;
                break;
            }
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "获取 CPU 信息时出错");
        }

        // GPU
        IReadOnlyList<GPUInfo> gpus = previous.GPUs;
        try
        {
            var gpuList = new List<GPUInfo>();
            using var searcher =
                new ManagementObjectSearcher(@"root\CIMV2", "SELECT * FROM Win32_VideoController");
            foreach (ManagementObject queryObj in searcher.Get())
            {
                var gpuInfo = new GPUInfo
                {
                    Name = queryObj["Name"]?.ToString() ?? "",
                    DriverVersion = queryObj["DriverVersion"]?.ToString() ?? "",
                    Memory = queryObj["AdapterRAM"] is not null and not DBNull
                        ? Convert.ToInt64(queryObj["AdapterRAM"]) / (1024 * 1024)
                        : 0
                };
                gpuList.Add(gpuInfo);
            }

            if (gpuList.Count > 0)
                gpus = gpuList.AsReadOnly();
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "获取 GPU 信息时出错");
        }

        LogWrapper.Info("已获取系统硬件信息");
        return new HardwareSnapshot(cpuName, gpus, SystemMemorySize);
    }

    private static void _Publish(HardwareSnapshot snapshot)
    {
        lock (_StateLock)
        {
            CPUName = snapshot.CPUName;
            GPUs = snapshot.GPUs;
            Volatile.Write(ref _Snapshot, snapshot);
            Volatile.Write(ref _HasCollected, 1);
        }
    }
}
