namespace ScriSelfStudio.Core.Text;

/// <summary>
/// Подсчёт слов в тексте (ТЗ п. 11): словом считается цепочка букв и цифр.
/// Реализация одна на проект: и статистика редактора, и объём сцен на карте
/// книги считают одинаково, иначе числа на экранах разойдутся.
/// </summary>
public static class WordCounting
{
    public static int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var count = 0;
        var insideWord = false;

        foreach (var character in text)
        {
            var isWordCharacter = char.IsLetterOrDigit(character);
            if (isWordCharacter && !insideWord)
            {
                count++;
            }

            insideWord = isWordCharacter;
        }

        return count;
    }

    public static int Count(RichDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Count(document.ToPlainText());
    }
}
