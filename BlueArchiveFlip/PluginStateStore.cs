using System.Text.Json;
using SecRandom.BlueArchiveFlip.Models;

namespace SecRandom.BlueArchiveFlip;

/// <summary>
///     插件自己的运行时状态。宿主不会替插件保存任何东西，这个对象就是插件在配置目录里的全部记忆。
/// </summary>
public sealed class PluginState
{
    /// <summary>插件被加载的次数。</summary>
    public int LoadCount { get; set; }

    /// <summary>最近一次写入状态的时间。</summary>
    public DateTimeOffset? LastUpdated { get; set; }

    /// <summary>最近一次操作留下的说明，设置页会展示它。</summary>
    public string LastMessage { get; set; } = string.Empty;

    /// <summary>累计抽签次数，展示在设置页里。</summary>
    public int DrawCount { get; set; }

    /// <summary>招募结果动画参数；用户没改过就是默认那套。</summary>
    public RecruitSettings Recruit { get; set; } = new();
}

/// <summary>
///     插件专属配置目录里的读写实现。
///     <para>
///         <see cref="SecRandom.Core.Plugins.PluginBase.PluginConfigFolder" /> 由宿主在加载插件时创建，
///         每个插件一个目录（通常是 <c>data/config/plugins/&lt;插件 id&gt;</c>）。
///         插件只应把数据写在这里，不要碰主程序目录的其他位置。
///     </para>
///     <para>
///         读写都加锁：抽签页面和设置页面会同时持有同一个 store。
///     </para>
/// </summary>
public sealed class PluginStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly Lock _gate = new();

    public PluginStateStore(string configFolder)
    {
        _filePath = Path.Combine(configFolder, "state.json");
    }

    /// <summary>状态文件的绝对路径，便于在设置页展示、方便排查问题。</summary>
    public string FilePath => _filePath;

    public PluginState Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new PluginState();

                var json = File.ReadAllText(_filePath);
                var state = JsonSerializer.Deserialize<PluginState>(json, SerializerOptions);
                if (state is null)
                    return new PluginState();

                // 手改过配置文件也可能带来越界数值，读进来先夹一遍。
                state.Recruit = (state.Recruit ?? new RecruitSettings()).Sanitized();
                return state;
            }
            catch (Exception)
            {
                // 状态文件损坏不该影响插件加载，直接退回默认值。
                return new PluginState();
            }
        }
    }

    public void Save(PluginState state)
    {
        lock (_gate)
        {
            try
            {
                state.LastUpdated = DateTimeOffset.Now;

                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(_filePath, JsonSerializer.Serialize(state, SerializerOptions));
            }
            catch (Exception)
            {
                // 磁盘写失败不该让抽签崩掉：状态只是锦上添花。
            }
        }
    }
}
