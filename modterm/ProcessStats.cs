using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System.Diagnostics;

namespace modterm
{
    public struct ProcStats
    {
        public ulong WorkingSetBytes;
        public ulong PrivateMemoryBytes;
        public double CpuUsagePercentage;
    }

    public class ProcessStats
    {
        public static ProcStats? GetProcessStatsAsync(uint processId)
        {
            // Request diagnostics for a specific process ID
            var processInfo = ProcessDiagnosticInfo.TryGetForProcessId(processId);

            if (processInfo != null)
            {
                // Get the memory usage report (matches Task Manager "Memory" column)
                var memReport = processInfo.MemoryUsage.GetReport();

                // NonPagedPoolSizeInBytes, PagedPoolSizeInBytes, WorkingSetSizeInBytes, etc.
                ulong workingSetBytes = memReport.WorkingSetSizeInBytes;
                ulong privateMemoryBytes = memReport.PrivatePageCount * 4096; // Convert pages to bytes (assuming 4KB pages)

                // set the proc stats struct
                var procStats = new ProcStats
                {
                    WorkingSetBytes = workingSetBytes,
                    PrivateMemoryBytes = privateMemoryBytes,
                    CpuUsagePercentage = processInfo.CpuUsage.GetReport().KernelTime.TotalMilliseconds + processInfo.CpuUsage.GetReport().UserTime.TotalMilliseconds
                };

                return procStats;

            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Process not found or access denied.");
                return null;
            }
           
        }
    }
}