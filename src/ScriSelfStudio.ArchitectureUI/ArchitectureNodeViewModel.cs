namespace ScriSelfStudio.ArchitectureUI;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation.Localization;

/// <summary>
/// Узел дерева книги: серия, книга, глава или сцена (ТЗ п. 27, 35).
/// Узел — снимок для отображения; данные меняет только сервис структуры.
/// </summary>
public sealed partial class ArchitectureNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;

    private ArchitectureNodeViewModel(EntityKind kind, Guid id, string title, int order, ArchitectureNodeViewModel? parent)
    {
        Kind = kind;
        Id = id;
        Title = title;
        Order = order;
        Parent = parent;
    }

    public EntityKind Kind { get; }

    public Guid Id { get; }

    public string Title { get; }

    public int Order { get; }

    public SceneStatus? Status { get; private init; }

    public bool HasText { get; private init; }

    public string? Description { get; private init; }

    /// <summary>Объём в словах: у сцены — её текст, у главы и книги — сумма сцен.</summary>
    public int Words { get; private init; }

    /// <summary>Ключевая сцена книги (ТЗ п. 27).</summary>
    public bool IsKey { get; private init; }

    /// <summary>Сцена заблокирована от правок: на карте помечается замком (ТЗ п. 27).</summary>
    public bool IsLocked { get; private init; }

    public long Revision { get; private init; }

    public ArchitectureNodeViewModel? Parent { get; }

    public ObservableCollection<ArchitectureNodeViewModel> Children { get; } = [];

    public string KindLabel => Kind switch
    {
        EntityKind.Series => Loc.T("Серия"),
        EntityKind.Book => Loc.T("Книга"),
        EntityKind.Chapter => Loc.T("Глава"),
        _ => Loc.T("Сцена"),
    };

    public string Detail => Kind switch
    {
        EntityKind.Series => Loc.F("Книг: {0}", Children.Count),
        EntityKind.Book => Loc.F("Глав: {0}", Children.Count),
        EntityKind.Chapter => Loc.F("Сцен: {0}", Children.Count),
        _ => StatusLabel(),
    };

    /// <summary>Объём для карты: «1 234 слова» или «пусто».</summary>
    public string WordsLabel => Words > 0
        ? Loc.F("{0:N0} сл.", Words)
        : Loc.T("пусто");

    public string StatusText => Status switch
    {
        SceneStatus.Draft => Loc.T("черновик"),
        SceneStatus.Revised => Loc.T("на правке"),
        SceneStatus.Final => Loc.T("готово"),
        _ => Loc.T("идея"),
    };

    public bool IsScene => Kind == EntityKind.Scene;

    public static ArchitectureNodeViewModel ForSeries(SeriesDto series, ArchitectureNodeViewModel? parent = null)
    {
        var node = new ArchitectureNodeViewModel(EntityKind.Series, series.Id, series.Title, series.Order, parent);
        foreach (var book in series.Books)
        {
            node.Children.Add(ForBook(book, node));
        }

        return node;
    }

    public static ArchitectureNodeViewModel ForBook(BookDto book, ArchitectureNodeViewModel? parent = null)
    {
        var node = new ArchitectureNodeViewModel(EntityKind.Book, book.Id, book.Title, book.Order, parent);
        foreach (var chapter in book.Chapters)
        {
            node.Children.Add(ForChapter(chapter, node));
        }

        return node;
    }

    public static ArchitectureNodeViewModel ForChapter(ChapterDto chapter, ArchitectureNodeViewModel? parent = null)
    {
        var node = new ArchitectureNodeViewModel(EntityKind.Chapter, chapter.Id, chapter.Title, chapter.Order, parent)
        {
            Description = chapter.Description,
            Words = chapter.Words,
        };

        foreach (var scene in chapter.Scenes)
        {
            node.Children.Add(ForScene(scene, node));
        }

        return node;
    }

    public static ArchitectureNodeViewModel ForScene(SceneDto scene, ArchitectureNodeViewModel? parent = null) => new(EntityKind.Scene, scene.Id, scene.Title, scene.Order, parent)
    {
        Status = scene.Status,
        HasText = scene.HasText,
        Description = scene.Description,
        Words = scene.Words,
        IsKey = scene.IsKey,
        IsLocked = scene.IsLocked,
        Revision = scene.Revision,
    };

    /// <summary>Ближайший предок заданного вида (или сам узел, если он этого вида).</summary>
    public ArchitectureNodeViewModel? FindAncestor(EntityKind kind)
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            if (node.Kind == kind)
            {
                return node;
            }
        }

        return null;
    }

    private string StatusLabel() => StatusText + (HasText ? ", есть текст" : ", пусто");
}
