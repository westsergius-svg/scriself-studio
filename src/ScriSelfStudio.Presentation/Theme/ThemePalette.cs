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
        AppTheme.Starter => Starter,
        AppTheme.GreenStart => GreenStart,
        AppTheme.Graphite => Graphite,
        AppTheme.Ocean => Ocean,
        AppTheme.Forest => Forest,
        AppTheme.Cabinet => Cabinet,
        AppTheme.Jam => Jam,
        _ => Studio,
    };

    // Общий порядок ключей для массивов цветов всех тем ниже.
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
        "#252525", "#303030", "#191919",
        "#F5F5F5", "#202020", "#181818", "#D9D9D9",
        "#242424", "#6B6B6B", "#B7B7B7",
        "#2D73C2", "#FFFFFF",
        "#303030", "#252525", "#191919",
        "#EEEEEE", "#EEEEEE",
        "#384B60", "#E0E0E0", "#B7B7B7",
        "#434343", "#FFFFFF",
        "#F19691", "#88C29C", "#DEC38B",
        "#76B7FF", "#88C29C", "#444444", "#DDDDDD",
        "#252525", "#202020", "#303030", "#252525",
        "#484848", "#3C3C3C", "#384B60", "#679BD0",
        "#88C29C", "#A2D5B4", "#304339",
        "#F19691", "#79504E", "#47312F",
        "#E8E8E8", "#B7B7B7", "#D0D0D0", "#76B7FF", "#3B3B3B",
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

    private static readonly string[] StarterValues =
    [
        "#EAF2F8", "#F5F8FC", "#D9E5F0",
        "#F8FBFF", "#F3F7FB", "#D9E5F0", "#DDEAF7",
        "#1F2B39", "#5E6F82", "#5B6E83",
        "#286FBD", "#FFFFFF",
        "#EAF3FF", "#F2F7FC", "#E0EBF8",
        "#24364A", "#24364A",
        "#D9E7F6", "#586E82", "#586D82",
        "#E2EEF9", "#1E2E3D",
        "#D65C52", "#3C9B7A", "#D2A14C",
        "#286FBD", "#3BAA82", "#EAF2F8", "#2A6DC3",
        "#F4F8FD", "#F9FBFF", "#FFFFFF", "#EDF4FE",
        "#D7E3F0", "#EAF2FF", "#DCEAFB", "#B9CEE3",
        "#3B8AE5", "#3C7DCC", "#E4F0FF",
        "#D86052", "#DFB157", "#F2F7FF",
        "#1F2B39", "#586D82", "#4A607A", "#286FBD", "#E8F1FB",
    ];

    private static readonly string[] GreenStartValues =
    [
        "#F1F0EA", "#F5F4EF", "#D8DAD2",
        "#FCFBF7", "#EEEFE9", "#D8DAD2", "#D8DAD2",
        "#292F2C", "#616B63", "#626C64",
        "#38634F", "#FFFFFF",
        "#F5F4EF", "#ECEEE7", "#D8DAD2",
        "#292F2C", "#292F2C",
        "#E1E9DC", "#3E4B42", "#626C64",
        "#E1E9DC", "#284E3B",
        "#A43D35", "#326348", "#805719",
        "#38634F", "#38634F", "#E1E9DC", "#38634F",
        "#F1F0EA", "#F5F4EF", "#FCFBF7", "#ECEEE7",
        "#D8DAD2", "#E8EBE2", "#E1E9DC", "#9CAC9A",
        "#38634F", "#326348", "#E1E9DC",
        "#A43D35", "#CFA49D", "#F7EAE5",
        "#292F2C", "#626C64", "#465449", "#38634F", "#E8EBE2",
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

    /// <summary>Стандартная светлая тема с синим акцентом.</summary>
    public static readonly ThemePalette Starter = Build(StarterValues);

    /// <summary>«Зелёный старт»: светлая палитра с зелёным акцентом «Кабинета».</summary>
    public static readonly ThemePalette GreenStart = Build(GreenStartValues);

    /// <summary>Тёмная нейтральная тема.</summary>
    public static readonly ThemePalette Graphite = Build(GraphiteValues);

    /// <summary>Холодная сине-зелёная тема.</summary>
    public static readonly ThemePalette Ocean = Build(OceanValues);

    /// <summary>Глубокая зелёная тема.</summary>
    public static readonly ThemePalette Forest = Build(ForestValues);

    /// <summary>Тёплые нейтральные поверхности и контрастный шалфейный акцент.</summary>
    public static readonly ThemePalette Cabinet = GreenStart;

    /// <summary>Стеклянные поверхности. Текст и лист непрозрачны для устойчивой читаемости.</summary>
    public static readonly ThemePalette Jam = Build(
    [
        "#B8F5F7FC", "#BFFFFFFF", "#CCFFFFFF",
        "#FCFDFF", "#30FFFFFF", "#D9E0EF", "#E1E6F0",
        "#242A3B", "#566078", "#566078",
        "#9E376D", "#FFFFFF",
        "#BFFFFFFF", "#A6F3F5FC", "#CCFFFFFF",
        "#252A3D", "#252A3D",
        "#DCEBD8E8", "#364056", "#566078",
        "#DCEBD8E8", "#812653",
        "#A12839", "#216347", "#825615",
        "#9E376D", "#287556", "#EBD8E8", "#812653",
        "#B8FFFFFF", "#18FFFFFF", "#CFFFFFFF", "#B3F3F5FC",
        "#9EBAC5DB", "#D9FFFFFF", "#DCEBD8E8", "#B98AAA",
        "#287556", "#216347", "#DCEEE5",
        "#A12839", "#D2A0AB", "#F9EAF0",
        "#252A3D", "#566078", "#364056", "#9E376D", "#D9FFFFFF",
    ]);

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
