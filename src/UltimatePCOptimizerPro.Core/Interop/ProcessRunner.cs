using System.Diagnostics;
using System.Text;

namespace UltimatePCOptimizerPro.Core.Interop;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Success => !TimedOut && ExitCode == 0;
    public string Combined => (StdOut + Environment.NewLine + StdErr).Trim();
}

/// <summary>Runs Microsoft-supplied command line tools (powercfg, schtasks, dism, reg, netsh) without a window.</summary>
public static class ProcessRunner
{
    public static string System32(string exe) => Path.Combine(Environment.SystemDirectory, exe);

    public static ProcessResult Run(string fileName, string arguments, int timeoutMs = 60_000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var p = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), true);
        }
        p.WaitForExit();
        return new ProcessResult(p.ExitCode, stdout.ToString(), stderr.ToString(), false);
    }

    public static Task<ProcessResult> RunAsync(string fileName, string arguments, int timeoutMs = 60_000)
        => Task.Run(() => Run(fileName, arguments, timeoutMs));

    /// <summary>Runs a short Windows PowerShell script (read-only queries only) and returns stdout.</summary>
    public static ProcessResult PowerShell(string script, int timeoutMs = 60_000)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script));
        var ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        return Run(ps, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", timeoutMs);
    }
}
