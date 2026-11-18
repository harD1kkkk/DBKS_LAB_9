using System.Drawing;
using System.Windows.Forms;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Lab_9;

public sealed class ConnectionForm : Form
{
    private readonly TextBox _address = new()
    {
        Dock = DockStyle.Top,
        Height = 30,
        UseSystemPasswordChar = true
    };

    public string ConnectionString { get; private set; } = "";

    public ConnectionForm()
    {
        Text = "Підключення до MongoDB";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(710, 230);
        MinimumSize = new Size(580, 270);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        layout.Controls.Add(new Label
        {
            Text = "Адреса MongoDB (локальна або MongoDB Atlas)",
            Dock = DockStyle.Fill
        }, 0, 0);

        _address.Text = Environment.GetEnvironmentVariable("MONGODB_URI")
            ?? "mongodb://127.0.0.1:27017";
        layout.Controls.Add(_address, 0, 1);

        var reveal = new CheckBox
        {
            Text = "Показати адресу",
            Dock = DockStyle.Fill
        };
        reveal.CheckedChanged += (_, _) => _address.UseSystemPasswordChar = !reveal.Checked;
        layout.Controls.Add(reveal, 0, 2);

        layout.Controls.Add(new Label
        {
            Text = "База LibraryLab9 створюється після першого додавання документів.",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray
        }, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var connect = new Button { Text = "Підключитися", Width = 145, Height = 31 };
        var cancel = new Button { Text = "Скасувати", Width = 110, Height = 31, DialogResult = DialogResult.Cancel };
        connect.Click += async (_, _) =>
        {
            string value = _address.Text.Trim();
            if (!value.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Введіть адресу MongoDB, що починається з mongodb:// або mongodb+srv://.",
                    "Некоректна адреса", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            connect.Enabled = false;
            try
            {
                var settings = MongoClientSettings.FromConnectionString(value);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(12);
                settings.ConnectTimeout = TimeSpan.FromSeconds(10);
                var client = new MongoClient(settings);
                await client.GetDatabase("LibraryLab9")
                    .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
                ConnectionString = value;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося підключитися до MongoDB.\n\n{ex.Message}",
                    "Помилка підключення", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                connect.Enabled = true;
            }
        };
        actions.Controls.Add(connect);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 4);

        AcceptButton = connect;
        CancelButton = cancel;
        Controls.Add(layout);
    }
}
