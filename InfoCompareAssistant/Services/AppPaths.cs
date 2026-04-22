namespace InfoCompareAssistant.Services;

public sealed class AppPaths
{
    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public AppPaths()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InfoCompareAssistant");
        Directory.CreateDirectory(DataDirectory);
        DatabasePath = Path.Combine(DataDirectory, "app.db");
    }
}
