namespace ScriSelfStudio.Core.Tests;

using ScriSelfStudio.Core.Text;

public sealed class SceneTextDiffTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("", "Начало.")]
    [InlineData("Финал.", "")]
    [InlineData("Она ждала у окна.", "Она стояла у окна.")]
    [InlineData("— Привет!\n\nЁж 🚀", "— Здравствуй!\n\nЁж 🚀")]
    [InlineData("одно  слово", "одно слово")]
    [InlineData("same same same", "same new same")]
    public void Comparison_reconstructs_both_versions_without_losing_characters(string before, string after)
    {
        var diff = SceneTextDiff.Compare(before, after);
        Assert.Equal(before, string.Concat(diff.Parts.Where(p => p.Kind != SceneDiffKind.Added).Select(p => p.Text)));
        Assert.Equal(after, string.Concat(diff.Parts.Where(p => p.Kind != SceneDiffKind.Removed).Select(p => p.Text)));
        Assert.False(diff.IsCoarse);
    }

    [Fact]
    public void Long_rewrite_has_bounded_comparison_and_keeps_both_texts()
    {
        var before = string.Join(' ', Enumerable.Repeat("старый", 1800));
        var after = string.Join(' ', Enumerable.Repeat("новый", 1800));
        var diff = SceneTextDiff.Compare(before, after);
        Assert.True(diff.IsCoarse);
        Assert.Equal(before, string.Concat(diff.Parts.Where(p => p.Kind != SceneDiffKind.Added).Select(p => p.Text)));
        Assert.Equal(after, string.Concat(diff.Parts.Where(p => p.Kind != SceneDiffKind.Removed).Select(p => p.Text)));
    }
}
