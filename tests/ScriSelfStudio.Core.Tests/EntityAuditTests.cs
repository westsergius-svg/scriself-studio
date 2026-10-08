namespace ScriSelfStudio.Core.Tests;

using ScriSelfStudio.Core.Model;

public sealed class EntityAuditTests
{
    [Fact]
    public void MarkDeleted_and_restore_keep_timestamps_consistent()
    {
        var chapter = new Chapter();
        var created = chapter.Audit.CreatedAt;
        var deletedAt = created.AddHours(2);

        chapter.MarkDeleted(deletedAt);

        Assert.True(chapter.IsDeleted);
        Assert.True(chapter.Audit.IsDeleted);
        Assert.Equal(deletedAt, chapter.Audit.DeletedAt);
        Assert.Equal(deletedAt, chapter.Audit.UpdatedAt);

        chapter.RestoreFromDeleted(deletedAt.AddMinutes(1));

        Assert.False(chapter.IsDeleted);
        Assert.Null(chapter.Audit.DeletedAt);
        Assert.Equal(deletedAt.AddMinutes(1), chapter.Audit.UpdatedAt);
        Assert.Equal(created, chapter.Audit.CreatedAt);
    }

    [Fact]
    public void Touch_updates_only_modification_time()
    {
        var scene = new Scene();
        var created = scene.Audit.CreatedAt;
        var touchedAt = created.AddMinutes(30);

        scene.Touch(touchedAt);

        Assert.Equal(touchedAt, scene.Audit.UpdatedAt);
        Assert.Equal(created, scene.Audit.CreatedAt);
    }
}
