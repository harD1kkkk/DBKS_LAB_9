using LibraryLabs.Shared;
using System.Windows.Forms;

namespace Lab_9;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var connectionForm = new ConnectionForm();
        if (connectionForm.ShowDialog() != DialogResult.OK) return;

        var store = new MongoLibraryStore(connectionForm.ConnectionString);
        Application.Run(new LibraryMainForm(store, "9", "MongoDB"));
    }
}

