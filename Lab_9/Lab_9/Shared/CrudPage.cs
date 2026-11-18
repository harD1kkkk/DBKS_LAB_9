using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LibraryLabs.Shared;

public sealed class CrudPage : UserControl
{
    private readonly ILibraryStore _store;
    private readonly EntitySpec _entity;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        ScrollBars = ScrollBars.Both,
        BackgroundColor = Color.White
    };
    private readonly TextBox _search = new() { Width = 290 };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(6, 8, 0, 0) };
    private readonly Dictionary<string, Control> _editors = new();
    private readonly List<Button> _buttons = new();
    private string? _selectedId;
    private bool _busy;

    public CrudPage(ILibraryStore store, EntitySpec entity)
    {
        _store = store;
        _entity = entity;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        var searchBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 47, Padding = new Padding(8, 5, 0, 0),
            WrapContents = false
        };
        searchBar.Controls.Add(new Label
        {
            Text = "Пошук:", AutoSize = true, Padding = new Padding(0, 8, 0, 0)
        });
        searchBar.Controls.Add(_search);
        AddButton(searchBar, "Знайти", LoadAsync);
        AddButton(searchBar, "Показати всі", async () =>
        {
            _search.Clear();
            await LoadAsync();
        });
        searchBar.Controls.Add(_status);

        var editorBox = new Panel { Dock = DockStyle.Bottom, Height = 210, BackColor = Color.FromArgb(242, 246, 250) };
        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8, 7, 8, 2)
        };
        foreach (FieldSpec field in entity.Fields)
        {
            var wrapper = new Panel { Width = 205, Height = 63, Margin = new Padding(3, 2, 8, 3) };
            var label = new Label { Text = field.Label, Dock = DockStyle.Top, AutoSize = false, Height = 22 };
            Control control;
            if (field.Kind == FieldKind.Boolean)
            {
                control = new CheckBox { Dock = DockStyle.Top, Checked = true, Text = "Так" };
            }
            else
            {
                control = new TextBox { Dock = DockStyle.Top };
                if (field.Name is "IssueDate" or "RegistrationDate")
                    ((TextBox)control).Text = DateTime.Today.ToString("yyyy-MM-dd");
            }
            wrapper.Controls.Add(control);
            wrapper.Controls.Add(label);
            fields.Controls.Add(wrapper);
            _editors[field.Name] = control;
        }

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 47, Padding = new Padding(11, 4, 0, 0),
            WrapContents = false
        };
        AddButton(actions, "Додати", async () => await SaveAsync(false));
        AddButton(actions, "Оновити", async () => await SaveAsync(true));
        AddButton(actions, "Видалити", DeleteAsync);
        AddButton(actions, "Очистити поля", () =>
        {
            ClearFields();
            return Task.CompletedTask;
        });
        editorBox.Controls.Add(fields);
        editorBox.Controls.Add(actions);

        Controls.Add(_grid);
        Controls.Add(editorBox);
        Controls.Add(searchBar);

        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = _grid.Rows[e.RowIndex];
            _selectedId = Convert.ToString(row.Cells["Id"].Value, CultureInfo.InvariantCulture);
            foreach (FieldSpec field in _entity.Fields)
            {
                object? value = row.Cells[field.Name].Value;
                if (_editors[field.Name] is CheckBox box)
                    box.Checked = value is not null
                        && value != DBNull.Value
                        && (value.ToString() == "1" || string.Equals(value.ToString(), "True", StringComparison.OrdinalIgnoreCase));
                else
                    ((TextBox)_editors[field.Name]).Text =
                        value == DBNull.Value ? "" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
            _status.Text = "Вибрано ID: " + _selectedId;
        };

        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _ = ReloadAsync();
        };
    }

    public Task ReloadAsync() => RunSafeAsync(LoadAsync);

    private async Task LoadAsync()
    {
        DataTable table = await _store.GetAsync(_entity, _search.Text.Trim());
        _grid.DataSource = table;
        _grid.ClearSelection();
        _status.Text = $"Записів: {table.Rows.Count}";
    }

    private void AddButton(Control parent, string title, Func<Task> handler)
    {
        var button = new Button
        {
            Text = title, AutoSize = true, Height = 32,
            Margin = new Padding(3, 0, 6, 0),
            BackColor = Color.White, FlatStyle = FlatStyle.Standard
        };
        button.Click += async (_, _) => await RunSafeAsync(handler);
        parent.Controls.Add(button);
        _buttons.Add(button);
    }

    private async Task SaveAsync(bool updating)
    {
        if (updating && string.IsNullOrWhiteSpace(_selectedId))
            throw new InvalidOperationException("Спочатку виберіть запис у таблиці.");
        var data = new Dictionary<string, object?>();
        foreach (FieldSpec field in _entity.Fields)
        {
            if (_editors[field.Name] is CheckBox cb)
            {
                data[field.Name] = cb.Checked;
                continue;
            }
            string raw = ((TextBox)_editors[field.Name]).Text.Trim();
            if (field.Required && raw.Length == 0)
                throw new ArgumentException($"Заповніть поле «{field.Label}».");
            if (raw.Length == 0)
            {
                data[field.Name] = null;
                continue;
            }
            switch (field.Kind)
            {
                case FieldKind.Integer:
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                        || number < 0)
                        throw new ArgumentException($"Некоректне число у полі «{field.Label}».");
                    data[field.Name] = number;
                    break;
                case FieldKind.Money:
                    string normalized = raw.Replace(',', '.');
                    if (!double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double price)
                        || price < 0 || !double.IsFinite(price))
                        throw new ArgumentException("Ціна повинна бути невід’ємним числом.");
                    data[field.Name] = price;
                    break;
                case FieldKind.Date:
                    if (!DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _))
                        throw new ArgumentException($"Дата «{field.Label}» має формат рррр-мм-дд.");
                    data[field.Name] = raw;
                    break;
                default:
                    data[field.Name] = raw;
                    break;
            }
        }
        if (_entity.Name == "Loans"
            && data["ReturnDate"] is string returnDate
            && string.CompareOrdinal(returnDate, (string)data["IssueDate"]!) < 0)
            throw new ArgumentException("Дата повернення не може бути раніше дати видачі.");

        if (updating) await _store.UpdateAsync(_entity, _selectedId!, data);
        else await _store.AddAsync(_entity, data);
        ClearFields();
        await RefreshAfterWriteAsync();
    }

    private async Task DeleteAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedId))
            throw new InvalidOperationException("Спочатку виберіть запис у таблиці.");
        if (MessageBox.Show("Видалити вибраний запис?", "Підтвердження",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        await _store.DeleteAsync(_entity, _selectedId);
        ClearFields();
        await RefreshAfterWriteAsync();
    }

    private async Task RefreshAfterWriteAsync()
    {
        DataTable table = await _store.GetAsync(_entity, _search.Text.Trim());
        _grid.DataSource = table;
        _grid.ClearSelection();
        _status.Text = $"Записів: {table.Rows.Count}";
    }

    private void ClearFields()
    {
        _selectedId = null;
        foreach (FieldSpec field in _entity.Fields)
        {
            if (_editors[field.Name] is CheckBox box) box.Checked = true;
            else ((TextBox)_editors[field.Name]).Text =
                field.Name is "IssueDate" or "RegistrationDate"
                    ? DateTime.Today.ToString("yyyy-MM-dd") : "";
        }
        _grid.ClearSelection();
        _status.Text = "Новий запис";
    }

    private async Task RunSafeAsync(Func<Task> handler)
    {
        if (_busy) return;
        _busy = true;
        foreach (Button button in _buttons) button.Enabled = false;
        try
        {
            await handler();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            foreach (Button button in _buttons) button.Enabled = true;
        }
    }
}
