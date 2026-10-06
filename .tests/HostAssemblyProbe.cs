using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace SecRandom.BlueArchiveFlip.Tests;

/// <summary>
///     测试进程没有宿主，但插件的宿主程序集（<c>SecRandom.Core</c> / <c>SecRandom.Shared</c> /
///     <c>SecRandom.PluginSdk</c>）在真实环境里由主程序提供，插件包刻意不带它们。
///     <para>
///         这里把"宿主提供程序集"这件事模拟出来：默认加载上下文解析不到依赖时，
///         按顺序到 SDK 的 NuGet 包目录（与插件编译时用的是同一份程序集）、
///         再退到本机主程序构建输出里找同名 dll。
///     </para>
///     <para>
///         顺序有讲究：先查 NuGet 包，因为主程序仓库里有些 <c>bin</c> 目录是旧构建，
///         里面的 <c>SecRandom.Core</c> 还没有 <c>IPluginDrawService</c>；
///         主程序构建输出只用来补 FluentAvalonia、DynamicData 这类第三方依赖。
///     </para>
/// </summary>
internal static class HostAssemblyProbe
{
    private static readonly string[] HostAssemblyDirectories = BuildDirectories();

    [ModuleInitializer]
    internal static void Install()
    {
        AssemblyLoadContext.Default.Resolving += ResolveFromHostOutput;
    }

    /// <summary>实际存在、可供探测的目录，便于断言当前跑的环境确实能找到宿主程序集。</summary>
    internal static IReadOnlyList<string> AvailableDirectories =>
        HostAssemblyDirectories.Where(Directory.Exists).ToArray();

    private static Assembly? ResolveFromHostOutput(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrWhiteSpace(name.Name))
            return null;

        foreach (var directory in HostAssemblyDirectories)
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate))
                return context.LoadFromAssemblyPath(candidate);
        }

        return null;
    }

    private static string[] BuildDirectories()
    {
        var directories = new List<string>();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var cache in new[]
                 {
                     Path.Combine(profile, ".nuget", "packages"),
                     @"D:\code\SecRandom-C\.nuget\packages"
                 })
        {
            // SDK 包里带的宿主程序集，与插件编译时引用的是同一份。
            directories.Add(Path.Combine(cache, "secrandom.core", "3.1.0", "lib", "net10.0"));
            directories.Add(Path.Combine(cache, "secrandom.shared", "3.1.0", "lib", "net8.0"));
            directories.Add(Path.Combine(cache, "secrandom.pluginsdk", "3.1.0", "lib", "net10.0"));
        }

        // 主程序构建输出：补齐宿主程序集自己的第三方依赖。
        directories.Add(@"D:\code\SecRandom-C\SecRandom.Desktop\bin\Release\net10.0-windows10.0.19041.0");
        directories.Add(@"D:\code\SecRandom-C\SecRandom.Desktop\bin\Release\net10.0");
        directories.Add(@"D:\code\SecRandom-C\SecRandom.Desktop\bin\Debug\net10.0");

        return [.. directories.Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
