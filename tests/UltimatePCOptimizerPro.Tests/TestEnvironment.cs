using System.Runtime.CompilerServices;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core;

namespace UltimatePCOptimizerPro.Tests;

internal static class TestEnvironment
{
    public const string TestRegistryRoot = @"Software\UltimatePCOptimizerPro.Tests";
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "UPCO-Tests-" + Guid.NewGuid().ToString("N")[..8]);

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Root);
        AppPaths.OverrideRoot(Root);
    }

    /// <summary>A unique, disposable key under HKCU used by registry tests (never touches real settings).</summary>
    public sealed class TempKey : IDisposable
    {
        public string Path { get; } = $@"{TestRegistryRoot}\{Guid.NewGuid():N}";
        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Path, throwOnMissingSubKey: false); } catch { }
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(TestRegistryRoot);
                if (root is { SubKeyCount: 0, ValueCount: 0 }) Registry.CurrentUser.DeleteSubKey(TestRegistryRoot, false);
            }
            catch { }
        }
    }
}
