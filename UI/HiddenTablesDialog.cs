using System.Runtime.Versioning;
using System.Windows.Forms;
using VPin.Inspector.Platforms.Vpx;

namespace VPin.Inspector.UI;

/// <summary>
/// A small modal dialog that lists the table file names currently stored in
/// <c>hidden_tables.json</c> and lets the user remove entries so those tables
/// resurface on the next scan. Changes are written through
/// <see cref="HiddenTablesStore"/> as they are made.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HiddenTablesDialog : Form
{
    private readonly HiddenTablesStore _store;
    private readonly ListBox _list;
    private readonly Button _removeButton;

    public HiddenTablesDialog(HiddenTablesStore store)
    {
        _store = store;

        Text = "Hidden Tables";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.Sizable;
        ClientSize = new Size(420, 320);
        MinimumSize = new Size(320, 220);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            Text = $"Tables hidden from scans (stored in {HiddenTablesStore.FileName}):",
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 6),
        };

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.MultiExtended,
            IntegralHeight = false,
        };
        _list.SelectedIndexChanged += (_, _) => UpdateRemoveEnabled();
        _list.DoubleClick += (_, _) => RemoveSelected();

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
        };

        var closeButton = new Button
        {
            Text = "Close",
            AutoSize = true,
            DialogResult = DialogResult.OK,
        };

        _removeButton = new Button
        {
            Text = "Remove",
            AutoSize = true,
        };
        _removeButton.Click += (_, _) => RemoveSelected();

        buttonRow.Controls.Add(closeButton);
        buttonRow.Controls.Add(_removeButton);

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        layout.Controls.Add(buttonRow, 0, 2);

        Controls.Add(layout);
        AcceptButton = closeButton;
        CancelButton = closeButton;

        ReloadList();
        DarkTheme.Apply(this);
    }

    private void ReloadList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (string name in _store.Load().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            _list.Items.Add(name);
        }

        _list.EndUpdate();
        UpdateRemoveEnabled();
    }

    private void UpdateRemoveEnabled() =>
        _removeButton.Enabled = _list.SelectedItems.Count > 0;

    private void RemoveSelected()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }

        var names = _list.SelectedItems.Cast<object>().Select(o => o.ToString()!).ToList();
        try
        {
            foreach (string name in names)
            {
                _store.Remove(name);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Failed to update {HiddenTablesStore.FileName}:{Environment.NewLine}{ex.Message}",
                "VPin Inspector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        ReloadList();
    }
}
