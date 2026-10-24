namespace ScriSelfStudio.EditorUI;

using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation.Localization;

/// <summary>
/// Сцена в ленте главы (ТЗ п. 27): номер, заголовок, статус и текстовый снимок.
/// Rich-text редактор объединяет документы сцен только для отображения; модель
/// сохраняет отдельный документ и отдельную ревизию для каждой сцены.
/// </summary>
public sealed partial class SceneBlockViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<SceneStatus, string> StatusLabels =
        new Dictionary<SceneStatus, string>
        {
            [SceneStatus.Idea] = "Идея",
            [SceneStatus.Draft] = "Черновик",
            [SceneStatus.Revised] = "В работе",
            [SceneStatus.Final] = "Готово",
        };

    private static readonly IReadOnlyDictionary<SceneStatus, IBrush> StatusBrushes =
        new Dictionary<SceneStatus, IBrush>
        {
            [SceneStatus.Idea] = new SolidColorBrush(Color.Parse("#9AA0A6")),
            [SceneStatus.Draft] = new SolidColorBrush(Color.Parse("#E8963E")),
            [SceneStatus.Revised] = new SolidColorBrush(Color.Parse("#4A7DFF")),
            [SceneStatus.Final] = new SolidColorBrush(Color.Parse("#3FBF7F")),
        };

    private readonly Action<SceneBlockViewModel> _textChanged;
    private bool _silent;
    private string _text;

    public SceneBlockViewModel(
        Guid sceneId,
        int order,
        string title,
        SceneStatus status,
        string text,
        Action<SceneBlockViewModel> textChanged)
    {
        SceneId = sceneId;
        Order = order;
        Title = title;
        Status = status;
        _text = text;
        _textChanged = textChanged;
    }

    public Guid SceneId { get; }

    public int Order { get; }

    public string Title { get; }

    public SceneStatus Status { get; }

    /// <summary>Номер сцены в главе: «01», «02», …</summary>
    public string NumberLabel => (Order + 1).ToString("D2", CultureInfo.InvariantCulture);

    public string StatusLabel => Loc.T(StatusLabels[Status]);

    public IBrush StatusBrush => StatusBrushes[Status];

    /// <summary>Сцена, с которой открыта текущая сессия редактора.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Текстовый снимок сцены.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value) && !_silent)
            {
                _textChanged(this);
                OnPropertyChanged(nameof(Words));
            }
        }
    }

    /// <summary>Объём текста сцены в словах — для подписи блока.</summary>
    public int Words
    {
        get
        {
            var count = 0;
            var inWord = false;
            foreach (var ch in _text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    inWord = false;
                }
                else if (!inWord)
                {
                    inWord = true;
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Обновляет текст без обратного вызова: снимок приходит извне.</summary>
    public void SetTextSilently(string value)
    {
        _silent = true;
        Text = value;
        _silent = false;
    }
}
