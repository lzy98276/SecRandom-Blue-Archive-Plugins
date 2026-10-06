using SecRandom.Shared.Models.Profile;

namespace SecRandom.BlueArchiveFlip.Models;

/// <summary>结果窗要展示的一张牌：牌面文字 + 稀有度 + 副标题（分组 / 课程之类）。</summary>
/// <remarks>
///     <see cref="Name" /> 通常是宿主按它自己的「显示格式」拼好的文本（点名/闪抽是「编号 名称」，抽奖是模板拼出来的
///     「编号 奖品 / 分组-成员」），所以牌面上的字和主程序内置结果卡一致。
/// </remarks>
public sealed record RecruitItem(string Name, LetterRarity Rarity, string SubText = "");

/// <summary>这次结果是从哪条抽签通道来的，面板标题会用到。</summary>
public enum RecruitSource
{
    /// <summary>点名。</summary>
    RollCall = 0,

    /// <summary>抽奖。</summary>
    Lottery = 1
}

/// <summary>
///     把宿主的数据模型翻译成结果窗要的 <see cref="RecruitItem" />。
///     <para>
///         宿主没有稀有度字段，所以 <see cref="RarityMode.ByTag" /> 只能在标签里找线索：
///         标签里出现 "3"（含 ★3 / 三星）当三星，出现 "2" 当二星，出现 "1" 当一星。
///         任何猜不出来的情况都退回统一档位，绝不抛异常——结果展示失败不该影响已经落库的抽签。
///     </para>
/// </summary>
public static class RecruitItemFactory
{
    private const int NameLengthLimit = 60;

    /// <summary>把宿主的学生翻译成一张牌。</summary>
    /// <param name="student">宿主的学生数据。</param>
    /// <param name="settings">插件设置（决定稀有度档位）。</param>
    /// <param name="title">宿主按自己的「显示格式」拼好的文本；为空时退回学生名字。</param>
    public static RecruitItem FromStudent(Student student, RecruitSettings settings, string? title = null)
    {
        var name = ResolveTitle(title, student.Name, "未命名");
        var sub = BuildStudentSubText(student);
        return new RecruitItem(name, ResolveRarity(student.Tags, settings), sub);
    }

    /// <summary>把宿主的奖品翻译成一张牌。</summary>
    /// <param name="prize">宿主的奖品数据。</param>
    /// <param name="settings">插件设置（决定稀有度档位）。</param>
    /// <param name="title">宿主按抽奖显示模板拼好的文本；为空时退回奖品名字。</param>
    public static RecruitItem FromPrize(Prize prize, RecruitSettings settings, string? title = null)
    {
        var name = ResolveTitle(title, prize.Name, "未命名奖品");
        var sub = prize.Count > 1 ? $"×{prize.Count}" : string.Empty;
        return new RecruitItem(name, ResolveRarity(prize.Tags, settings), sub);
    }

    /// <summary>
    ///     牌面上的文字：优先用宿主按显示格式拼好的文本（「编号 名称」、抽奖模板拼出来的那一串），
    ///     宿主没给（老宿主 / 手机端）才退回数据里的名字，再不行才用占位词。
    /// </summary>
    private static string ResolveTitle(string? title, string? fallback, string placeholder)
    {
        var text = title?.Trim();
        if (string.IsNullOrEmpty(text))
            text = fallback?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return placeholder;

        return text.Length > NameLengthLimit ? text[..NameLengthLimit] : text;
    }

    /// <summary>按标签猜稀有度；猜不出来用统一档位。</summary>
    public static LetterRarity ResolveRarity(string? tags, RecruitSettings settings)
    {
        if (settings.RarityMode != RarityMode.ByTag || string.IsNullOrWhiteSpace(tags))
            return settings.UniformRarity;

        var text = tags;
        if (text.Contains("★3", StringComparison.Ordinal) || text.Contains("三星", StringComparison.Ordinal))
            return LetterRarity.Rainbow;
        if (text.Contains("★2", StringComparison.Ordinal) || text.Contains("二星", StringComparison.Ordinal))
            return LetterRarity.Gold;
        if (text.Contains("★1", StringComparison.Ordinal) || text.Contains("一星", StringComparison.Ordinal))
            return LetterRarity.Blue;

        // 退一步只认数字：3 优先于 2 优先于 1。
        if (ContainsDigit(text, '3'))
            return LetterRarity.Rainbow;
        if (ContainsDigit(text, '2'))
            return LetterRarity.Gold;
        if (ContainsDigit(text, '1'))
            return LetterRarity.Blue;

        return settings.UniformRarity;
    }

    private static bool ContainsDigit(string text, char digit)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == digit)
                return true;
        }

        return false;
    }

    private static string BuildStudentSubText(Student student)
    {
        var group = student.Group?.Trim() ?? string.Empty;
        var gender = student.Gender?.Trim() ?? string.Empty;

        if (group.Length == 0)
            return gender;
        if (gender.Length == 0)
            return group;

        return $"{group} · {gender}";
    }
}
