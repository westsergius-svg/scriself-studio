namespace ScriSelfStudio.Presentation.Theme;

using ScriSelfStudio.Core.Model;

/// <summary>
/// Палитра оформления темы (макет ScriSelf Studio): значения кистей
/// <c>DynamicResource</c>, которые подменяются при смене темы.
/// </summary>
public sealed class ThemePalette
{
    private ThemePalette(IReadOnlyDictionary<string, string> brushes) => Brushes = brushes;

    /// <summary>Кисти темы по ключам ресурсов.</summary>
    public IReadOnlyDictionary<string, string> Brushes { get; }

    /// <summary>Палитра для темы; Studio — основа для System/Light/Dark.</summary>
    public static ThemePalette For(AppTheme theme) => theme switch
    {
        AppTheme.Paper => Paper,
        AppTheme.Graphite => Graphite,
        AppTheme.Ocean => Ocean,
        AppTheme.Forest => Forest,
        _ => Studio,
    };

    // Ключи в порядке колонок таблицы ниже: Studio, Paper, Graphite, Ocean, Forest.
    private static readonly string[] Keys =
    [
        "ChromeBrush", "PanelBrush", "ChromeBorderBrush",
        "PaperBrush", "MapCanvasBrush", "PaperBorderBrush", "PaperRuleBrush",
        "PaperTextBrush", "PaperMutedBrush", "MutedBrush",
        "AccentBrush", "AccentTextBrush",
        "ContextBrush", "ContextFieldBrush", "PanelBorderBrush",
        "HeadingBrush", "PanelHeadingBrush",
        "MapCardActiveBrush", "SidebarTextBrush", "SidebarSubTextBrush",
        "SidebarActiveBrush", "SidebarActiveTextBrush",
        "ErrorBrush", "SuccessBrush", "PendingBrush",
        "ActiveAccentBrush", "SavedDotBrush", "AvatarBrush", "AvatarTextBrush",
        "SurfaceChromeBrush", "SurfaceCanvasBrush", "SurfaceCardBrush", "SurfaceFieldBrush",
        "SurfaceBorderBrush", "SurfaceHoverBrush", "SurfaceSelectedBrush", "SurfaceSelectedBorderBrush",
        "StatusActiveDotBrush", "StatusActiveTextBrush", "StatusActiveFillBrush",
        "DangerTextBrush", "DangerBorderBrush", "DangerFillBrush",
        "ShellTitleBrush", "ShellSubtitleBrush", "ShellNavBrush", "ShellNavActiveBrush", "ShellNavHoverBrush",
    ];

    private static readonly string[] StudioValues =
    [
        "#0F181D", "#142127", "#29353B",
        "#F5F3EE", "#FBFAF7", "#DDD8CE", "#DDD8CE",
        "#202428", "#6F7476", "#929696",
        "#C56C45", "#FFFFFF",
        "#142026", "#1A292F", "#2B3B42",
        "#25292C", "#F1F2F2",
        "#2B373C", "#AEB7BA", "#69767B",
        "#2B373C", "#F2F4F4",
        "#C0392B", "#36B97B", "#C07A1E",
        "#C56C45", "#36B97B", "#26363D", "#D8BCAE",
        "#131A1E", "#161E22", "#1B2428", "#1B2428",
        "#2B3B42", "#1F292E", "#2A3238", "#3E4C52",
        "#5FBF7F", "#7FD8A2", "#1E2E24",
        "#E07A6C", "#5A322C", "#241C1A",
        "#F1F2F2", "#9AA3A7", "#C6CED1", "#E5856F", "#1F292E",
    ];

    private static readonly string[] PaperValues =
    [
        "#EEE8E0", "#E7E0D6", "#D2CCC2",
        "#FFFDFA", "#FFFDFA", "#DDD7CD", "#D8D4CC",
        "#25282A", "#6F7476", "#929696",
        "#AD6246", "#FFFFFF",
        "#ECE8E1", "#F5F1EB", "#D6D0C6",
        "#303537", "#303537",
        "#E4DCD0", "#5A5F61", "#8A8F90",
        "#DCD4C8", "#25282A",
        "#B3261E", "#1E7B4D", "#9A6A1E",
        "#AD6246", "#1E9E63", "#E7DFD2", "#7A4A38",
        "#F4EEE5", "#F7F3EC", "#FDFBF6", "#F9F5EF",
        "#E6DED1", "#F1EBE0", "#F3E3DB", "#DFC0B4",
        "#5F9A46", "#5F8A4A", "#EDF0E2",
        "#A83A32", "#E2B2A9", "#FDF7F4",
        "#2E231B", "#9C9081", "#4D4034", "#B6524B", "#EFE3D8",
    ];

    private static readonly string[] GraphiteValues =
    [
        "#17191A", "#202324", "#363A3B",
        "#26292A", "#2B2E2F", "#414546", "#3A3F40",
        "#E1E2DF", "#9B9E9D", "#909493",
        "#D27A51", "#1A1C1D",
        "#202425", "#292E30", "#3D4548",
        "#E5E5E2", "#E5E5E2",
        "#3A3221", "#B4B9BA", "#7D8588",
        "#2B373C", "#F2F4F4",
        "#FF7A6B", "#66D19E", "#FFB454",
        "#D27A51", "#5FBF8B", "#34383A", "#CFAE9E",
        "#17191A", "#1B1D1E", "#222526", "#222526",
        "#363A3B", "#262A2B", "#2E3233", "#42474A",
        "#6FBF56", "#8FCF77", "#202A1C",
        "#E08A7C", "#5C3A34", "#221E1C",
        "#E5E5E2", "#9B9E9D", "#C7C7C3", "#E08A7C", "#262A2B",
    ];

    private static readonly string[] OceanValues =
    [
        "#0B1B22", "#10262E", "#23414B",
        "#F0F5F5", "#F8FBFB", "#D3E0E1", "#C7D6D8",
        "#1E2B2F", "#6B7C81", "#8B9BA0",
        "#4F9EB0", "#FFFFFF",
        "#10252D", "#183039", "#294751",
        "#1B282C", "#1B282C",
        "#1C3942", "#B3C2C6", "#778A90",
        "#1C3942", "#F2F6F7",
        "#C0392B", "#1E9E63", "#B07A1E",
        "#4F9EB0", "#2FBE86", "#1D3A44", "#A9D6E2",
        "#0C1D24", "#0F232B", "#142C35", "#142C35",
        "#23414B", "#17303A", "#1C3A44", "#2A505C",
        "#5FBF9A", "#7FD8B4", "#16332C",
        "#E08A7C", "#4A3A36", "#1E2126",
        "#EDF3F4", "#8B9BA0", "#BFCBD0", "#6FC3D6", "#17303A",
    ];

    private static readonly string[] ForestValues =
    [
        "#101B18", "#172520", "#30423A",
        "#F1F3ED", "#FAFBF7", "#D6DCD2", "#C8D0C4",
        "#26302B", "#6A736D", "#8A938C",
        "#78946F", "#FFFFFF",
        "#17241F", "#1E3029", "#32463C",
        "#232E28", "#232E28",
        "#24382F", "#B0C0B6", "#768A7E",
        "#24382F", "#F1F5F1",
        "#B3261E", "#1E7B4D", "#9A6A1E",
        "#78946F", "#2FBE86", "#26352E", "#CBDCC6",
        "#0F1B17", "#132220", "#182B26", "#182B26",
        "#30423A", "#1B2E29", "#21362F", "#33503F",
        "#6FBF68", "#93D68C", "#1B2E1F",
        "#E0786C", "#463A34", "#1B2620",
        "#EFF3EE", "#8A938C", "#C0CBC2", "#8FCB84", "#1B2E29",
    ];

    /// <summary>Тёмно-графитовая тема «Studio» — базовая.</summary>
    public static readonly ThemePalette Studio = Build(StudioValues);

    /// <summary>Светлая бумажная тема.</summary>
    public static readonly ThemePalette Paper = Build(PaperValues);

    /// <summary>Тёмная нейтральная тема.</summary>
    public static readonly ThemePalette Graphite = Build(GraphiteValues);

    /// <summary>Холодная сине-зелёная тема.</summary>
    public static readonly ThemePalette Ocean = Build(OceanValues);

    /// <summary>Глубокая зелёная тема.</summary>
    public static readonly ThemePalette Forest = Build(ForestValues);

    private static ThemePalette Build(string[] values)
    {
        if (values.Length != Keys.Length)
        {
            // Разноуровневая защита: забытое значение в одной теме иначе дало бы
            // исключение времени выполнения при первой же смене темы.
            throw new InvalidOperationException(
                $"Палитра темы содержит {values.Length} значений, ожидается {Keys.Length}.");
        }

        var map = new Dictionary<string, string>(Keys.Length, StringComparer.Ordinal);
        for (var index = 0; index < Keys.Length; index++)
        {
            map[Keys[index]] = values[index];
        }

        return new ThemePalette(map);
    }
}
