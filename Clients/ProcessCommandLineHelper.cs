using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace League.Clients
{
    /// <summary>
    /// 优先 Native(ProcessCommandLineInformation)，失败回退 WMI。
    /// 遍历所有 LeagueClientUx，只认真正带有 --app-port + --remoting-auth-token 的进程。
    /// </summary>
    public static class ProcessCommandLineHelper
    {
        private const int ProcessCommandLineInformation = 60; // Win8.1+
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
        private const int STATUS_SUCCESS = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr ProcessHandle,
            int ProcessInformationClass,
            IntPtr ProcessInformation,
            int ProcessInformationLength,
            out int ReturnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        /// <summary>
        /// 优先 Native，失败再 WMI。返回完整命令行，失败返回 null。
        /// </summary>
        public static string GetCommandLine(int pid)
        {
            string cmd = GetCommandLineNative(pid);
            if (!string.IsNullOrWhiteSpace(cmd))
                return cmd;

            return GetCommandLineWmi(pid);
        }

        private static string GetCommandLineNative(int pid)
        {
            IntPtr hProcess = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess == IntPtr.Zero)
                    return null;

                int returnLength;
                int status = NtQueryInformationProcess(
                    hProcess, ProcessCommandLineInformation, IntPtr.Zero, 0, out returnLength);

                if (status != STATUS_INFO_LENGTH_MISMATCH && status != STATUS_SUCCESS)
                    return null;
                if (returnLength <= 0)
                    return null;

                buffer = Marshal.AllocHGlobal(returnLength);
                status = NtQueryInformationProcess(
                    hProcess, ProcessCommandLineInformation, buffer, returnLength, out returnLength);

                if (status != STATUS_SUCCESS)
                    return null;

                var us = Marshal.PtrToStructure<UNICODE_STRING>(buffer);
                if (us.Buffer == IntPtr.Zero || us.Length == 0)
                    return null;

                return Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
                if (hProcess != IntPtr.Zero)
                    CloseHandle(hProcess);
            }
        }

        private static string GetCommandLineWmi(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                    return obj["CommandLine"]?.ToString();
            }
            catch
            {
                // ignore
            }
            return null;
        }

        public static string Clean(string value) =>
            value?.Trim().Trim('"', '\'', ' ', '\r', '\n');

        /// <summary>
        /// 从命令行提取参数值（兼容引号）
        /// </summary>
        public static string ExtractArgument(string cmdLine, string key)
        {
            if (string.IsNullOrEmpty(cmdLine) || string.IsNullOrEmpty(key))
                return null;

            // --app-port=12345  或  --app-port="12345"
            var match = Regex.Match(
                cmdLine,
                Regex.Escape(key) + @"([""']?)([^""'\s]+)\1");
            return match.Success ? Clean(match.Groups[2].Value) : null;
        }

        public static bool TryParseLcuArgs(
            string cmdLine,
            out string port,
            out string token,
            out string region,
            out string rso)
        {
            port = null;
            token = null;
            region = null;
            rso = null;

            if (string.IsNullOrWhiteSpace(cmdLine))
                return false;

            port = ExtractArgument(cmdLine, "--app-port=");
            token = ExtractArgument(cmdLine, "--remoting-auth-token=");
            region = ExtractArgument(cmdLine, "--region=");
            rso = ExtractArgument(cmdLine, "--rso-platform-id=");
            if (string.IsNullOrEmpty(rso))
                rso = ExtractArgument(cmdLine, "--platform-id=");

            return !string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token);
        }

        /// <summary>
        /// 遍历所有 LeagueClientUx，返回第一个真正带有 port+token 的结果。
        /// </summary>
        public static bool TryFindLcuCredentials(
            out string port,
            out string token,
            out string region,
            out string rso,
            out int pid)
        {
            port = null;
            token = null;
            region = null;
            rso = null;
            pid = 0;

            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName("LeagueClientUx");
            }
            catch
            {
                return false;
            }

            if (processes == null || processes.Length == 0)
                return false;

            // 优先较新的进程，减少拿僵尸进程概率
            try
            {
                Array.Sort(processes, (a, b) =>
                {
                    try { return b.StartTime.CompareTo(a.StartTime); }
                    catch { return 0; }
                });
            }
            catch { }

            foreach (var p in processes)
            {
                try
                {
                    string cmd = GetCommandLine(p.Id);
                    if (string.IsNullOrWhiteSpace(cmd))
                    {
                        Debug.WriteLine($"[LCU] PID={p.Id} 命令行为空 (Native+WMI 均失败)");
                        continue;
                    }

                    if (TryParseLcuArgs(cmd, out var pPort, out var pToken, out var pRegion, out var pRso))
                    {
                        port = pPort;
                        token = pToken;
                        region = pRegion;
                        rso = pRso;
                        pid = p.Id;
                        Debug.WriteLine($"[LCU] 命中有效 UX PID={pid}, Port={port}");
                        return true;
                    }

                    Debug.WriteLine($"[LCU] PID={p.Id} 有命令行但无有效 port/token");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LCU] PID={p.Id} 解析异常: {ex.Message}");
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }

            return false;
        }
    }
}