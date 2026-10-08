namespace ScriSelfStudio.TestSupport;

using System.IO;

/// <summary>Временная папка для тестов, работающих с файлами.</summary>
public sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "scriself-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string FilePath(string fileName) => System.IO.Path.Combine(Root, fileName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
