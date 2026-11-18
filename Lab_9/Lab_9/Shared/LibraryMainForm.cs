using System.Drawing;
using System.Windows.Forms;

namespace LibraryLabs.Shared;

public sealed class LibraryMainForm : Form
{
    private readonly ILibraryStore _store;
    private readonly List<CrudPage> _pages = [];

    public LibraryMainForm(ILibraryStore store, string labNumber, string databaseTitle)
    {
        _store = store;
        Text = $"Лабораторна робота №{labNumber} — Бібліотека ({databaseTitle})";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1190;
        Height = 750;
        MinimumSize = new Size(850, 550);
        Font = new Font("Segoe UI", 10);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        foreach (EntitySpec entity in LibrarySchema.All)
        {
            var page = new CrudPage(store, entity);
            var tab = new TabPage(entity.Title);
            tab.Controls.Add(page);
            tabs.TabPages.Add(tab);
            _pages.Add(page);
        }
        Controls.Add(tabs);
        Shown += async (_, _) =>
        {
            try
            {
                await _store.InitializeAsync();
                foreach (CrudPage page in _pages) await page.ReloadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Помилка підключення",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
    }
}
