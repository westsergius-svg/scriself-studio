namespace ScriSelfStudio.StoryUI;

using CommunityToolkit.Mvvm.ComponentModel;

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
        Label = label;
        _get = get;
        _set = set;
        Multiline = multiline;
    }

    public string Label { get; }

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
public sealed record CardSection(string Title, IReadOnlyList<CardField> Fields);
