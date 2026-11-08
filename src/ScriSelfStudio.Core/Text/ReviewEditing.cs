namespace ScriSelfStudio.Core.Text;

using System.Text;

/// <summary>Scene-local review operations. Offsets are UTF-16; no UI or storage dependencies.</summary>
public static class ReviewEditing
{
    private sealed record Atom(char Value, SpanStyle Style, TextBlock Block, bool Boundary, bool Terminal);
    private readonly record struct Edit(int Start, int Removed, int Added);

    public static bool SameReview(DocumentReview left, DocumentReview right)
    {
        if (left.TrackChanges != right.TrackChanges || left.Author != right.Author
            || !left.Comments.SequenceEqual(right.Comments) || left.Changes.Count != right.Changes.Count) return false;
        for (var i = 0; i < left.Changes.Count; i++)
        {
            var a = left.Changes[i];
            var b = right.Changes[i];
            if (a.Id != b.Id || a.Author != b.Author || a.CreatedAt != b.CreatedAt || a.Start != b.Start
                || a.Length != b.Length || a.OriginalStart != b.OriginalStart || a.OriginalLength != b.OriginalLength
                || !SameContent(a.Before, b.Before)) return false;
        }
        return true;
    }

    private static bool SameContent(RichDocument left, RichDocument right)
    {
        if (left.SchemaVersion != right.SchemaVersion || left.Blocks.Count != right.Blocks.Count) return false;
        for (var i = 0; i < left.Blocks.Count; i++)
        {
            var a = left.Blocks[i]; var b = right.Blocks[i];
            if (a.Id != b.Id || a.Kind != b.Kind || a.Alignment != b.Alignment || a.IndentLevel != b.IndentLevel
                || a.ListMarker != b.ListMarker || a.Spans.Count != b.Spans.Count) return false;
            for (var j = 0; j < a.Spans.Count; j++)
                if (a.Spans[j].Text != b.Spans[j].Text || a.Spans[j].Style != b.Spans[j].Style) return false;
        }
        return true;
    }

    public static RichDocument ContentOnly(RichDocument source)
    {
        // Avoid cloning the history when only a text snapshot is needed.
        var result = new RichDocument { SchemaVersion = source.SchemaVersion };
        foreach (var block in source.Blocks)
        {
            var copy = CopyBlock(block);
            copy.Spans.AddRange(block.Spans.Select(s => new TextSpan { Text = s.Text, Style = s.Style }));
            result.Blocks.Add(copy);
        }
        return result;
    }

    public static void Capture(RichDocument previous, RichDocument current, DateTimeOffset now)
    {
        current.Review = previous.Review.Clone();
        var before = Atoms(previous);
        var after = Atoms(current);
        var edit = Difference(before, after);
        if (edit.Removed == 0 && edit.Added == 0)
        {
            foreach (var comment in current.Review.Comments) SetBlockIds(current, comment);
            return;
        }
        MoveComments(current, previous.ToPlainText(), current.ToPlainText());

        var overlapping = new List<TrackedTextChange>();
        var start = edit.Start;
        var end = start + edit.Removed;
        bool expanded;
        do
        {
            expanded = false;
            foreach (var change in current.Review.Changes)
            {
                if (overlapping.Contains(change) || change.Start > end || change.Start + change.Length < start) continue;
                overlapping.Add(change);
                start = Math.Min(start, change.Start);
                end = Math.Max(end, change.Start + change.Length);
                expanded = true;
            }
        } while (expanded);

        var original = new List<Atom>(before);
        foreach (var change in overlapping.OrderByDescending(c => c.Start))
        {
            original.RemoveRange(change.Start, change.Length);
            original.InsertRange(change.Start, Atoms(change.Before).GetRange(change.OriginalStart, change.OriginalLength));
            current.Review.Changes.Remove(change);
        }
        foreach (var change in current.Review.Changes)
            if (change.Start >= edit.Start + edit.Removed) change.Start += edit.Added - edit.Removed;

        if (!current.Review.TrackChanges && overlapping.Count == 0) return;
        var merged = Difference(original, after);
        if (merged.Removed == 0 && merged.Added == 0) return;
        var first = overlapping.OrderBy(c => c.CreatedAt).FirstOrDefault();
        current.Review.Changes.Add(new TrackedTextChange
        {
            Id = first?.Id ?? Guid.NewGuid(), Author = first?.Author ?? current.Review.Author,
            CreatedAt = first?.CreatedAt ?? now, Start = merged.Start, Length = merged.Added,
            OriginalStart = merged.Start, OriginalLength = merged.Removed, Before = FromAtoms(original),
        });
    }

    public static TextComment AddComment(RichDocument document, int start, int length, string text, DateTimeOffset now)
    {
        var plain = document.ToPlainText();
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 10000)
            throw new ArgumentException("Комментарий должен содержать от 1 до 10000 символов.", nameof(text));
        if (start < 0 || length <= 0 || start > plain.Length - length || SplitsPair(plain, start) || SplitsPair(plain, start + length))
            throw new ArgumentException("Выделите текст внутри одной сцены.", nameof(start));
        var comment = new TextComment
        {
            Author = document.Review.Author, CreatedAt = now, Text = text.Trim(),
            Quote = plain.Substring(start, length), Start = start, Length = length,
        };
        SetBlockIds(document, comment);
        document.Review.Comments.Add(comment);
        return comment;
    }

    public static bool Accept(RichDocument document, Guid id) => document.Review.Changes.RemoveAll(c => c.Id == id) > 0;

    public static bool Reject(RichDocument document, Guid id)
    {
        var change = document.Review.Changes.FirstOrDefault(c => c.Id == id);
        if (change is null) return false;
        var oldText = document.ToPlainText();
        var atoms = Atoms(document);
        atoms.RemoveRange(change.Start, change.Length);
        atoms.InsertRange(change.Start, Atoms(change.Before).GetRange(change.OriginalStart, change.OriginalLength));
        var restored = FromAtoms(atoms);
        document.Blocks.Clear();
        document.Blocks.AddRange(restored.Blocks);
        document.Review.Changes.Remove(change);
        foreach (var other in document.Review.Changes)
            if (other.Start >= change.Start + change.Length) other.Start += change.OriginalLength - change.Length;
        MoveComments(document, oldText, document.ToPlainText());
        return true;
    }

    public static RichDocument RemovedFragment(TrackedTextChange change) =>
        FromAtoms(Atoms(change.Before).GetRange(change.OriginalStart, change.OriginalLength));

    public static string AddedText(RichDocument document, TrackedTextChange change) =>
        TextOf(Atoms(document).GetRange(change.Start, change.Length));

    public static string RemovedText(TrackedTextChange change) =>
        TextOf(Atoms(change.Before).GetRange(change.OriginalStart, change.OriginalLength));

    public static void Validate(RichDocument document)
    {
        var review = document.Review ?? throw new InvalidDataException("Повреждены данные редактуры.");
        var length = document.ToPlainText().Length;
        if (review.Comments is null || review.Changes is null || string.IsNullOrWhiteSpace(review.Author)
            || review.Comments.Any(c => c is null || c.Id == Guid.Empty || c.Start < 0 || c.Length < 0 || c.Start > length - c.Length
                || string.IsNullOrWhiteSpace(c.Author) || string.IsNullOrWhiteSpace(c.Text) || c.Quote is null)
            || review.Comments.Select(c => c.Id).Distinct().Count() != review.Comments.Count)
            throw new InvalidDataException("Повреждены комментарии сцены.");
        if (review.Changes.Any(c => c is null)) throw new InvalidDataException("Повреждена правка сцены.");
        var end = -1;
        foreach (var change in review.Changes.OrderBy(c => c.Start))
        {
            if (change is null || change.Before is null || change.Id == Guid.Empty || string.IsNullOrWhiteSpace(change.Author)
                || change.Before.Review is not { Changes.Count: 0, Comments.Count: 0, TrackChanges: false, Sections: null }
                || change.Before.Blocks.Any(b => b is null || b.Spans.Any(s => s is null || s.Text is null))
                || change.Start < 0 || change.Length < 0
                || change.Start > length + 1 - change.Length || change.Start < end
                || change.OriginalStart < 0 || change.OriginalLength < 0
                || change.OriginalStart > change.Before.ToPlainText().Length + 1 - change.OriginalLength)
                throw new InvalidDataException("Повреждена правка сцены.");
            end = change.Start + change.Length;
        }
        if (review.Changes.Select(c => c.Id).Distinct().Count() != review.Changes.Count)
            throw new InvalidDataException("Повторяющийся идентификатор правки.");
    }

    private static void MoveComments(RichDocument document, string before, string after)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        while (SplitsPair(before, prefix) || SplitsPair(after, prefix)) prefix--;
        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        while (SplitsPair(before, before.Length - suffix) || SplitsPair(after, after.Length - suffix)) suffix--;
        var removed = before.Length - prefix - suffix;
        var added = after.Length - prefix - suffix;
        if (removed == 0 && added == 0) return;
        var cutEnd = prefix + removed;
        foreach (var comment in document.Review.Comments)
        {
            var oldStart = comment.Start;
            var oldEnd = oldStart + comment.Length;
            if (!comment.IsOrphaned && removed > 0 && prefix <= oldStart && cutEnd >= oldEnd)
            {
                comment.IsOrphaned = true;
                comment.Start = prefix;
                comment.Length = 0;
            }
            else
            {
                comment.Start = Map(oldStart, true);
                comment.Length = Math.Max(0, Map(oldEnd, false) - comment.Start);
            }
            SetBlockIds(document, comment);
        }
        int Map(int offset, bool right)
        {
            if (offset < prefix) return offset;
            if (offset > cutEnd) return offset + added - removed;
            if (removed == 0) return prefix + (right ? added : 0);
            if (offset == cutEnd) return prefix + added;
            return prefix + (right ? added : 0);
        }
    }

    private static void SetBlockIds(RichDocument document, TextComment comment)
    {
        comment.StartBlockId = BlockAt(comment.Start);
        comment.EndBlockId = BlockAt(comment.Start + comment.Length);
        Guid BlockAt(int offset)
        {
            foreach (var block in document.Blocks)
            {
                if (offset <= block.Length) return block.Id;
                offset -= block.Length + 1;
            }
            return document.Blocks.LastOrDefault()?.Id ?? Guid.Empty;
        }
    }

    private static bool SplitsPair(string text, int offset) => offset > 0 && offset < text.Length
        && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);

    private static List<Atom> Atoms(RichDocument document)
    {
        var atoms = new List<Atom>();
        var blocks = document.Blocks.Count == 0 ? [new TextBlock()] : document.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            foreach (var span in block.Spans)
                foreach (var c in span.Text) atoms.Add(new(c, span.Style, block, false, false));
            atoms.Add(new('\n', SpanStyle.Default, block, true, i == blocks.Count - 1));
        }
        return atoms;
    }

    private static bool Same(Atom a, Atom b) => a.Value == b.Value && a.Style == b.Style
        && a.Boundary == b.Boundary && a.Terminal == b.Terminal
        && (!a.Boundary || (a.Block.Kind == b.Block.Kind && a.Block.Alignment == b.Block.Alignment
            && a.Block.IndentLevel == b.Block.IndentLevel && a.Block.ListMarker == b.Block.ListMarker));

    private static Edit Difference(List<Atom> before, List<Atom> after)
    {
        var prefix = 0;
        while (prefix < before.Count && prefix < after.Count && Same(before[prefix], after[prefix])) prefix++;
        // Keep surrogate pairs in one change, including replacements of one emoji by another.
        while (prefix > 0 && ((prefix < before.Count && char.IsLowSurrogate(before[prefix].Value))
            || (prefix < after.Count && char.IsLowSurrogate(after[prefix].Value)))) prefix--;
        var suffix = 0;
        while (suffix < before.Count - prefix && suffix < after.Count - prefix
            && Same(before[^(suffix + 1)], after[^(suffix + 1)])) suffix++;
        while (suffix > 0 && (char.IsLowSurrogate(before[before.Count - suffix].Value)
            || char.IsLowSurrogate(after[after.Count - suffix].Value))) suffix--;
        return new(prefix, before.Count - prefix - suffix, after.Count - prefix - suffix);
    }

    private static TextBlock CopyBlock(TextBlock source) => new()
    {
        Id = source.Id, Kind = source.Kind, Alignment = source.Alignment,
        IndentLevel = source.IndentLevel, ListMarker = source.ListMarker,
    };

    private static RichDocument FromAtoms(List<Atom> atoms)
    {
        var result = new RichDocument();
        var spans = new List<TextSpan>();
        var text = new StringBuilder();
        var style = SpanStyle.Default;
        var ids = new HashSet<Guid>();
        foreach (var atom in atoms)
        {
            if (atom.Boundary)
            {
                Flush();
                var block = CopyBlock(atom.Block);
                if (!ids.Add(block.Id)) { block.Id = Guid.NewGuid(); ids.Add(block.Id); }
                block.Spans.AddRange(spans);
                spans.Clear();
                result.Blocks.Add(block);
            }
            else
            {
                if (style != atom.Style) { Flush(); style = atom.Style; }
                text.Append(atom.Value);
            }
        }
        Flush();
        if (spans.Count > 0 || result.Blocks.Count == 0)
        {
            var block = atoms.Count > 0 ? CopyBlock(atoms[^1].Block) : new TextBlock();
            if (!ids.Add(block.Id)) block.Id = Guid.NewGuid();
            block.Spans.AddRange(spans);
            result.Blocks.Add(block);
        }
        return result;
        void Flush()
        {
            if (text.Length == 0) return;
            spans.Add(new TextSpan { Text = text.ToString(), Style = style });
            text.Clear();
        }
    }

    private static string TextOf(IEnumerable<Atom> atoms) => new(atoms.Where(a => !a.Terminal).Select(a => a.Value).ToArray());
}
