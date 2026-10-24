namespace ScriSelfStudio.StoryUI;

using CommunityToolkit.Mvvm.ComponentModel;
using ScriSelfStudio.Presentation.Localization;

/// <summary>
/// Поле карточки для табличной раскладки: подпись и двусторонняя привязка к черновику.
/// Позволяет показать 14 разделов одним шаблоном, не повторяя разметку для каждого поля.
/// </summary>
public sealed class CardField : ObservableObject
{
    private readonly Func<string?> _get;
    private readonly Action<string?> _set;

    public CardField(
        string label,
        Func<string?> get,
        Action<string?> set,
        bool multiline = false)
    {
        _label = label;
        _get = get;
        _set = set;
        Multiline = multiline;
    }

    private readonly string _label;

    /// <summary>Подпись поля переводится при отображении.</summary>
    public string Label => Loc.T(_label);

    public bool Multiline { get; }

    /// <summary>Поля-абзацы выше строки: в них пишут развёрнутые описания.</summary>
    public double MinHeight => Multiline ? 56 : 0;

    public string? Value
    {
        get => _get();
        set
        {
            if (!string.Equals(_get(), value, StringComparison.Ordinal))
            {
                _set(value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Черновик заменён целиком: поле должно перечитать значение.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Value));
}

/// <summary>Раздел карточки персонажа с набором полей.</summary>
public sealed record CardSection
{
    private readonly string _title;

    public CardSection(string title, IReadOnlyList<CardField> fields)
    {
        _title = title;
        Fields = fields;
    }

    public string Title => Loc.T(_title);

    public IReadOnlyList<CardField> Fields { get; }
}
