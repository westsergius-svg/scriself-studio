namespace ScriSelfStudio.EditorEngine;

using ScriSelfStudio.Core.Text;

/// <summary>
/// Операции над канонической моделью документа сцены (ТЗ п. 11, 22, 37).
/// Модель — единственный источник истины; форматирование и структура блоков
/// сохраняются при текстовых правках, где это возможно.
/// </summary>
internal static class DocumentEditor
{
    /// <summary>Гарантирует, что в документе есть хотя бы один блок.</summary>
    public static void Normalize(RichDocument document)
    {
        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new TextBlock());
        }
    }

    public static int PlainLength(RichDocument document) => document.ToPlainText().Length;

    /// <summary>
    /// Отображает смещение в плоском тексте на блок и локальное смещение внутри него.
    /// Смещение на границе блоков относится к концу предыдущего блока.
    /// </summary>
    public static (int BlockIndex, int OffsetInBlock) MapPosition(RichDocument document, int offset)
    {
        Normalize(document);
        var position = Math.Clamp(offset, 0, PlainLength(document));
        var running = 0;
        for (var index = 0; index < document.Blocks.Count; index++)
        {
            var length = document.Blocks[index].Length;
            if (position <= running + length)
            {
                return (index, position - running);
            }

            running += length + 1;
        }

        var last = document.Blocks.Count - 1;
        return (last, document.Blocks[last].Length);
    }

    /// <summary>Заменяет диапазон плоского текста, сохраняя форматирование вокруг правки.</summary>
    public static void ReplaceText(RichDocument document, int offset, int removeLength, string insertedText)
    {
        Normalize(document);
        if (removeLength == 0 && insertedText.Length == 0)
        {
            return;
        }

        var (startBlock, startLocal) = MapPosition(document, offset);
        var (endBlock, endLocal) = MapPosition(document, offset + removeLength);

        TextBlock head;
        TextBlock tail;
        if (startBlock == endBlock)
        {
            var (beforeHead, rest) = SplitBlock(document.Blocks[startBlock], startLocal);
            var (_, afterTail) = SplitBlock(rest, endLocal - startLocal);
            head = beforeHead;
            tail = afterTail;
        }
        else
        {
            var (beforeHead, _) = SplitBlock(document.Blocks[startBlock], startLocal);
            var (_, afterTail) = SplitBlock(document.Blocks[endBlock], endLocal);
            head = beforeHead;
            tail = afterTail;
        }

        var format = ResolveInsertFormat(head, tail);
        var pieces = insertedText.Length == 0 ? [] : insertedText.Split('\n');

        var rebuilt = new List<TextBlock>();
        for (var index = 0; index < startBlock; index++)
        {
            rebuilt.Add(document.Blocks[index]);
        }

        if (pieces.Length <= 1)
        {
            rebuilt.Add(MergeBlocks(head, tail, format, pieces.Length == 1 ? pieces[0] : null));
        }
        else
        {
            rebuilt.Add(MergeBlocks(head, null, format, pieces[0]));
            for (var index = 1; index < pieces.Length - 1; index++)
            {
                var middle = new TextBlock();
                AddText(middle, pieces[index], format);
                rebuilt.Add(middle);
            }

            rebuilt.Add(MergeBlocks(null, tail, format, pieces[^1]));
        }

        for (var index = endBlock + 1; index < document.Blocks.Count; index++)
        {
            rebuilt.Add(document.Blocks[index]);
        }

        document.Blocks.Clear();
        document.Blocks.AddRange(rebuilt);
    }

    /// <summary>Оформление всех символов диапазона (пересечение). Для пустого диапазона — оформление позиции.</summary>
    public static SpanStyle GetFormatting(RichDocument document, int start, int end)
    {
        Normalize(document);
        start = Math.Clamp(start, 0, PlainLength(document));
        end = Math.Clamp(end, start, PlainLength(document));

        if (start == end)
        {
            return GetFormatAt(document, start);
        }

        var result = (SpanStyle?)null;
        foreach (var (text, style) in EnumerateRange(document, start, end))
        {
            _ = text;
            result = result is null ? style : result.Value.Intersect(style);
        }

        return result ?? SpanStyle.Default;
    }

    /// <summary>Оформление символа, следующего за позицией вставки.</summary>
    public static SpanStyle GetFormatAt(RichDocument document, int offset)
    {
        Normalize(document);
        var position = Math.Clamp(offset, 0, PlainLength(document));
        var fallback = SpanStyle.Default;
        var seen = false;

        foreach (var (text, style) in EnumerateRange(document, 0, position))
        {
            if (text.Length > 0)
            {
                fallback = style;
                seen = true;
            }
        }

        if (position < PlainLength(document))
        {
            foreach (var (text, style) in EnumerateRange(document, position, position + 1))
            {
                if (text.Length > 0)
                {
                    return style;
                }
            }
        }

        return seen ? fallback : SpanStyle.Default;
    }

    /// <summary>Применяет маску форматирования к символам диапазона, не меняя структуру блоков.</summary>
    public static void ApplyFormatting(RichDocument document, int start, int end, Func<SpanStyle, SpanStyle> transform)
    {
        Normalize(document);
        var (startBlock, startLocal) = MapPosition(document, start);
        var (endBlock, endLocal) = MapPosition(document, end);

        for (var index = startBlock; index <= endBlock; index++)
        {
            var block = document.Blocks[index];
            var localStart = index == startBlock ? startLocal : 0;
            var localEnd = index == endBlock ? endLocal : block.Length;
            TransformBlock(block, localStart, localEnd, transform);
        }
    }

    /// <summary>Блок, содержащий позицию курсора.</summary>
    public static TextBlock BlockAt(RichDocument document, int offset)
    {
        var (index, _) = MapPosition(document, offset);
        return document.Blocks[index];
    }

    /// <summary>Блоки, пересекающиеся с диапазоном. Пустой диапазон даёт один блок.</summary>
    public static IReadOnlyList<TextBlock> BlocksInRange(RichDocument document, int start, int end)
    {
        Normalize(document);
        var (startBlock, _) = MapPosition(document, start);
        var (endBlock, _) = MapPosition(document, end);
        var blocks = new List<TextBlock>();
        for (var index = startBlock; index <= endBlock && index < document.Blocks.Count; index++)
        {
            blocks.Add(document.Blocks[index]);
        }

        return blocks;
    }

    /// <summary>Структурное сравнение без учёта идентификаторов блоков.</summary>
    public static bool IsEquivalent(RichDocument left, RichDocument right)
    {
        if (!ReviewEditing.SameReview(left.Review, right.Review)) return false;
        if (left.Blocks.Count != right.Blocks.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Blocks.Count; index++)
        {
            var a = left.Blocks[index];
            var b = right.Blocks[index];
            if (a.Kind != b.Kind
                || a.Alignment != b.Alignment
                || a.IndentLevel != b.IndentLevel
                || a.ListMarker != b.ListMarker)
            {
                return false;
            }

            var leftSpans = NormalizedSpans(a);
            var rightSpans = NormalizedSpans(b);
            if (leftSpans.Count != rightSpans.Count)
            {
                return false;
            }

            for (var spanIndex = 0; spanIndex < leftSpans.Count; spanIndex++)
            {
                if (leftSpans[spanIndex].Style != rightSpans[spanIndex].Style ||
                    !string.Equals(leftSpans[spanIndex].Text, rightSpans[spanIndex].Text, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static IEnumerable<(string Text, SpanStyle Style)> EnumerateRange(RichDocument document, int start, int end)
    {
        var (blockIndex, local) = MapPosition(document, start);
        var remaining = end - start;
        while (remaining > 0 && blockIndex < document.Blocks.Count)
        {
            var block = document.Blocks[blockIndex];
            var position = local;
            foreach (var span in block.Spans)
            {
                if (position >= span.Text.Length)
                {
                    position -= span.Text.Length;
                    continue;
                }

                var take = Math.Min(remaining, span.Text.Length - position);
                yield return (span.Text.Substring(position, take), span.Style);
                remaining -= take;
                position = 0;
                if (remaining == 0)
                {
                    yield break;
                }
            }

            remaining -= Math.Min(remaining, 1);
            blockIndex++;
            local = 0;
        }
    }

    private static List<TextSpan> NormalizedSpans(TextBlock block)
    {
        var spans = new List<TextSpan>();
        foreach (var span in block.Spans)
        {
            if (span.Text.Length == 0)
            {
                continue;
            }

            if (spans.Count > 0 && spans[^1].Style == span.Style)
            {
                spans[^1].Text += span.Text;
            }
            else
            {
                spans.Add(new TextSpan { Text = span.Text, Style = span.Style });
            }
        }

        return spans;
    }

    /// <summary>
    /// Меняет формат части блока, не меняя число блоков: границы диапазона воплощаются
    /// как границы спанов (ТЗ п. 11: форматирование не должно менять структуру абзацев).
    /// </summary>
    private static void TransformBlock(TextBlock block, int localStart, int localEnd, Func<SpanStyle, SpanStyle> transform)
    {
        var result = new List<TextSpan>();
        var position = 0;
        foreach (var span in block.Spans)
        {
            var spanStart = position;
            var spanEnd = position + span.Text.Length;
            position = spanEnd;

            if (spanEnd <= localStart || spanStart >= localEnd)
            {
                AddSpan(result, span.Text, span.Style);
                continue;
            }

            var cutStart = Math.Max(localStart, spanStart) - spanStart;
            var cutEnd = Math.Min(localEnd, spanEnd) - spanStart;
            AddSpan(result, span.Text[..cutStart], span.Style);
            AddSpan(result, span.Text[cutStart..cutEnd], transform(span.Style));
            AddSpan(result, span.Text[cutEnd..], span.Style);
        }

        block.Spans.Clear();
        block.Spans.AddRange(result);
    }

    private static TextBlock MergeBlocks(TextBlock? primary, TextBlock? secondary, SpanStyle format, string? inserted)
    {
        var source = primary ?? secondary ?? new TextBlock();
        var block = new TextBlock
        {
            Id = source.Id,
            Kind = source.Kind,
            Alignment = source.Alignment,
            IndentLevel = source.IndentLevel,
            ListMarker = source.ListMarker,
        };

        if (primary is not null)
        {
            CopySpans(primary, block);
        }

        if (inserted is not null)
        {
            AddText(block, inserted, format);
        }

        if (secondary is not null)
        {
            CopySpans(secondary, block);
        }

        return block;
    }

    private static void CopySpans(TextBlock from, TextBlock to)
    {
        foreach (var span in from.Spans)
        {
            AddText(to, span.Text, span.Style);
        }
    }

    private static void AddText(TextBlock block, string text, SpanStyle style)
    {
        if (text.Length == 0)
        {
            return;
        }

        AddSpan(block.Spans, text, style);
    }

    private static void AddSpan(List<TextSpan> spans, string text, SpanStyle style)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (spans.Count > 0 && spans[^1].Style == style)
        {
            spans[^1].Text += text;
            return;
        }

        spans.Add(new TextSpan { Text = text, Style = style });
    }

    private static (TextBlock Head, TextBlock Tail) SplitBlock(TextBlock block, int local)
    {
        var split = Math.Clamp(local, 0, block.Length);
        var head = new TextBlock
        {
            Id = block.Id,
            Kind = block.Kind,
            Alignment = block.Alignment,
            IndentLevel = block.IndentLevel,
            ListMarker = block.ListMarker,
        };

        var tail = new TextBlock
        {
            Id = Guid.NewGuid(),
            Kind = block.Kind,
            Alignment = block.Alignment,
            IndentLevel = block.IndentLevel,
            ListMarker = block.ListMarker,
        };

        var remaining = split;
        foreach (var span in block.Spans)
        {
            if (remaining >= span.Text.Length)
            {
                AddText(head, span.Text, span.Style);
                remaining -= span.Text.Length;
            }
            else
            {
                AddText(head, span.Text[..remaining], span.Style);
                AddText(tail, span.Text[remaining..], span.Style);
                remaining = 0;
            }
        }

        return (head, tail);
    }

    private static SpanStyle ResolveInsertFormat(TextBlock head, TextBlock tail)
    {
        for (var index = head.Spans.Count - 1; index >= 0; index--)
        {
            if (head.Spans[index].Text.Length > 0)
            {
                return head.Spans[index].Style;
            }
        }

        foreach (var span in tail.Spans)
        {
            if (span.Text.Length > 0)
            {
                return span.Style;
            }
        }

        return SpanStyle.Default;
    }
}
