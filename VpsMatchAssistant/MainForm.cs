using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VPin.Inspector.Vps;
using VPin.Inspector.Vpx.Pinup;

namespace VPin.MatchAssistant;

public sealed class MainForm : Form
{
    private const string DefaultDbPath = @"C:\vPinball\PinUPSystem\PUPDatabase.db";

    private readonly TextBox _dbPathBox = new() { Width = 460 };
    private readonly TextBox _csvPathBox = new() { Width = 460 };
    private readonly ComboBox _emulatorCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _visibleOnly = new() { Text = "Visible games only", Checked = true, AutoSize = true };
    private readonly CheckBox _includePopulated = new() { Text = "Include games with WEBGameID", Checked = false, AutoSize = true };
    private readonly Button _loadEmulatorsButton = new() { Text = "Load Emulators", AutoSize = true };
    private readonly Button _matchButton = new() { Text = "Find Matches", AutoSize = true, Enabled = false };
    private readonly Button _applyButton = new() { Text = "Apply Selected (writes DB)", AutoSize = true, Enabled = false };
    private readonly Button _deselectAllButton = new() { Text = "Deselect All", AutoSize = true, Enabled = false };
    private readonly Button _toggleUncheckedButton = new() { Text = "Hide Unchecked", AutoSize = true, Enabled = false };
    private readonly Button _undoButton = new() { Text = "Undo Last Apply", AutoSize = true, Enabled = false };
    private readonly DataGridView _gamesGrid = new();
    private readonly DataGridView _candidatesGrid = new();
    private readonly TextBox _candidateSearchBox = new() { Width = 320 };
    private readonly Button _candidateSearchClear = new() { Text = "Clear", AutoSize = true };
    private readonly Label _candidatesHeader = new() { AutoSize = true, Padding = new Padding(0, 6, 6, 0), Text = "Search VPS:" };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
    };

    private readonly BindingList<GameMatch> _matches = new();
    private MatchEngine? _engine;
    private IReadOnlyList<WebGameIdChange>? _lastApplied;
    private readonly UserSettings _settings;
    private bool _hideUnchecked;

    public MainForm()
    {
        Text = "VPS Match Assistant";
        Width = 1100;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;
        _dbPathBox.Text = DefaultDbPath;

        _settings = UserSettings.Load();
        if (!string.IsNullOrWhiteSpace(_settings.DbPath))
        {
            _dbPathBox.Text = _settings.DbPath!;
        }
        if (!string.IsNullOrWhiteSpace(_settings.CsvPath))
        {
            _csvPathBox.Text = _settings.CsvPath!;
        }

        BuildLayout();
        WireEvents();
        ApplyDarkTheme();
        ApplyWindowGeometry();
    }

    private void ApplyWindowGeometry()
    {
        if (_settings.WindowWidth is > 200 and int w &&
            _settings.WindowHeight is > 200 and int h)
        {
            StartPosition = FormStartPosition.Manual;
            Size = new Size(w, h);
        }

        if (_settings.WindowX is int x && _settings.WindowY is int y)
        {
            var proposed = new Rectangle(x, y, Width, Height);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(proposed)))
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(x, y);
            }
        }

        if (_settings.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        base.OnFormClosing(e);
    }

    private void SaveSettings()
    {
        _settings.Maximized = WindowState == FormWindowState.Maximized;

        Rectangle bounds = WindowState == FormWindowState.Normal
            ? Bounds
            : RestoreBounds;

        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;

        _settings.DbPath = _dbPathBox.Text;
        _settings.CsvPath = _csvPathBox.Text;

        _settings.Save();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8, 8, 8, 16),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

        // --- Setup panel ---
        var setup = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top };
        var browseDb = new Button { Text = "Browse...", AutoSize = true };
        var browseCsv = new Button { Text = "Browse...", AutoSize = true };
        var downloadCsv = new Button { Text = "Download CSV", AutoSize = true };
        browseDb.Click += (_, _) => PickFile(_dbPathBox, "PinUP database (*.db)|*.db|All files|*.*");
        browseCsv.Click += (_, _) => PickFile(_csvPathBox, "puplookup.csv|puplookup.csv|CSV files|*.csv|All files|*.*");
        downloadCsv.Click += async (_, _) => await DownloadCsvAsync();

        setup.Controls.Add(new Label { Text = "PUPDatabase.db:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        setup.Controls.Add(_dbPathBox, 1, 0);
        setup.Controls.Add(browseDb, 2, 0);
        setup.Controls.Add(new Label { Text = "puplookup.csv:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        setup.Controls.Add(_csvPathBox, 1, 1);
        var csvButtons = new FlowLayoutPanel { AutoSize = true };
        csvButtons.Controls.Add(browseCsv);
        csvButtons.Controls.Add(downloadCsv);
        setup.Controls.Add(csvButtons, 2, 1);

        var emuRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        emuRow.Controls.Add(new Label { Text = "Emulator:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 4, 0) });
        emuRow.Controls.Add(_emulatorCombo);
        emuRow.Controls.Add(_visibleOnly);
        emuRow.Controls.Add(_includePopulated);
        emuRow.Controls.Add(_loadEmulatorsButton);
        emuRow.Controls.Add(_matchButton);
        emuRow.Controls.Add(_toggleUncheckedButton);
        setup.Controls.Add(emuRow, 1, 2);
        setup.SetColumnSpan(emuRow, 2);

        root.Controls.Add(setup, 0, 0);

        // --- Games grid ---
        ConfigureGamesGrid();
        var gamesBox = new GroupBox { Text = "Unmatched games (tick Write to apply)", Dock = DockStyle.Fill };
        gamesBox.Controls.Add(_gamesGrid);
        root.Controls.Add(gamesBox, 0, 1);

        // --- Candidates grid ---
        ConfigureCandidatesGrid();
        var candBox = new GroupBox { Text = "VPS candidates for selected game (double-click to choose)", Dock = DockStyle.Fill };
        var candLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        candLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        candLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var searchRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Top };
        searchRow.Controls.Add(_candidatesHeader);
        searchRow.Controls.Add(_candidateSearchBox);
        searchRow.Controls.Add(_candidateSearchClear);
        candLayout.Controls.Add(searchRow, 0, 0);
        candLayout.Controls.Add(_candidatesGrid, 0, 1);
        candBox.Controls.Add(candLayout);
        root.Controls.Add(candBox, 0, 2);

        // --- Bottom: actions + log ---
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        actions.Controls.Add(_applyButton);
        actions.Controls.Add(_deselectAllButton);
        actions.Controls.Add(_undoButton);
        bottom.Controls.Add(actions, 0, 0);
        bottom.Controls.Add(_log, 1, 0);
        root.Controls.Add(bottom, 0, 3);

        Controls.Add(root);
    }

    private void ConfigureGamesGrid()
    {
        _gamesGrid.Dock = DockStyle.Fill;
        _gamesGrid.AutoGenerateColumns = false;
        _gamesGrid.AllowUserToAddRows = false;
        _gamesGrid.AllowUserToDeleteRows = false;
        _gamesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gamesGrid.MultiSelect = false;
        _gamesGrid.RowHeadersVisible = false;

        _gamesGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Write",
            Name = "Write",
            Width = 55,
            ThreeState = true,
            ReadOnly = true, // we cycle the state manually so review -> confirmed is one click
            TrueValue = CheckState.Checked,
            FalseValue = CheckState.Unchecked,
            IndeterminateValue = CheckState.Indeterminate,
            ToolTipText = "Unchecked = skip. Dash = needs review. Checked = will be written on Apply.",
        });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Game", Name = "Game", Width = 220, ReadOnly = true });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Manufacturer", Name = "Manufacturer", Width = 110, ReadOnly = true });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Year", Name = "Year", Width = 55, ReadOnly = true });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Version", Name = "Version", Width = 90, ReadOnly = true });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Author", Name = "Author", Width = 120, ReadOnly = true });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Date Updated", Name = "DateUpdated", Width = 90, ReadOnly = true });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Date File Updated", Name = "DateFileUpdated", Width = 100, ReadOnly = true });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Selected WEBGameID", Name = "Selected", Width = 150, ReadOnly = true });
        _gamesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Suggestion", Name = "Suggestion", Width = 90, ReadOnly = true });

        EnableFillColumns(_gamesGrid);
    }

    private void ConfigureCandidatesGrid()
    {
        _candidatesGrid.Dock = DockStyle.Fill;
        _candidatesGrid.AutoGenerateColumns = false;
        _candidatesGrid.AllowUserToAddRows = false;
        _candidatesGrid.ReadOnly = true;
        _candidatesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _candidatesGrid.MultiSelect = false;
        _candidatesGrid.RowHeadersVisible = false;

        _candidatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Score", DataPropertyName = nameof(VpsCandidate.Score), Width = 60, DefaultCellStyle = { Format = "0.00" } });
        _candidatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "WEBGameID", DataPropertyName = nameof(VpsCandidate.WebGameId), Width = 150 });
        _candidatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "GameName", DataPropertyName = nameof(VpsCandidate.GameName), Width = 240 });
        _candidatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Manufacturer", DataPropertyName = nameof(VpsCandidate.Manufacturer), Width = 110 });
        _candidatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Year", DataPropertyName = nameof(VpsCandidate.Year), Width = 55 });
        _candidatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Version", DataPropertyName = nameof(VpsCandidate.Version), Width = 100 });
        _candidatesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Author", DataPropertyName = nameof(VpsCandidate.Author), Width = 120 });

        EnableFillColumns(_candidatesGrid);
    }

    /// <summary>
    /// Makes the grid's columns auto-expand to fill the control width, using each
    /// column's current width as its proportional fill weight.
    /// </summary>
    private static void EnableFillColumns(DataGridView grid)
    {
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.FillWeight = Math.Max(1, column.Width);
        }
    }

    private void WireEvents()
    {
        _loadEmulatorsButton.Click += (_, _) => LoadEmulators();
        _matchButton.Click += (_, _) => FindMatches();
        _applyButton.Click += (_, _) => ApplyChanges();
        _deselectAllButton.Click += (_, _) => DeselectAll();
        _toggleUncheckedButton.Click += (_, _) => ToggleUnchecked();
        _undoButton.Click += (_, _) => UndoChanges();
        _gamesGrid.SelectionChanged += (_, _) => ShowCandidates();
        _gamesGrid.CellContentClick += GamesGrid_CellContentClick;
        _candidatesGrid.CellDoubleClick += CandidatesGrid_CellDoubleClick;
        _candidateSearchBox.TextChanged += (_, _) => ShowCandidates();
        _candidateSearchClear.Click += (_, _) => _candidateSearchBox.Clear();
        _dbPathBox.Leave += (_, _) => PersistPathSettings();
        _csvPathBox.Leave += (_, _) => PersistPathSettings();
    }

    private void PersistPathSettings()
    {
        _settings.DbPath = _dbPathBox.Text;
        _settings.CsvPath = _csvPathBox.Text;
        _settings.Save();
    }

    private void GamesGrid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != _gamesGrid.Columns["Write"]!.Index)
        {
            return;
        }

        DataGridViewCell cell = _gamesGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        CheckState current = cell.Value switch
        {
            CheckState s => s,
            true => CheckState.Checked,
            false => CheckState.Unchecked,
            _ => CheckState.Unchecked,
        };

        // Unchecked -> Checked, Indeterminate (review) -> Checked, Checked -> Unchecked.
        cell.Value = current == CheckState.Checked ? CheckState.Unchecked : CheckState.Checked;
        UpdateApplyEnabled();

        if (_hideUnchecked)
        {
            ApplyRowFilter();
        }
    }

    private void ToggleUnchecked()
    {
        _hideUnchecked = !_hideUnchecked;
        _toggleUncheckedButton.Text = _hideUnchecked ? "Show Unchecked" : "Hide Unchecked";
        ApplyRowFilter();
    }

    /// <summary>
    /// When hiding unchecked rows, only games in review (dash) or confirmed
    /// (checked) state remain visible so the user can review just the matches.
    /// </summary>
    private void ApplyRowFilter()
    {
        _gamesGrid.CurrentCell = null;
        foreach (DataGridViewRow row in _gamesGrid.Rows)
        {
            bool unchecked_ = row.Cells["Write"].Value is not CheckState.Checked
                and not CheckState.Indeterminate;
            row.Visible = !_hideUnchecked || !unchecked_;
        }
    }

    private void PopulateGamesGrid()
    {
        _gamesGrid.Rows.Clear();
        foreach (GameMatch m in _matches)
        {
            CheckState state = m.SelectedWebGameId is not null
                ? CheckState.Indeterminate
                : CheckState.Unchecked;

            int index = _gamesGrid.Rows.Add(
                state,
                m.Game.GameName,
                m.Game.Manufacturer ?? string.Empty,
                m.Game.Year ?? string.Empty,
                m.Game.Version ?? string.Empty,
                m.Game.Author ?? string.Empty,
                m.Game.DateUpdated ?? string.Empty,
                m.Game.DateFileUpdated ?? string.Empty,
                m.SelectedWebGameId ?? string.Empty,
                m.HasConfidentSuggestion ? "auto" : (m.Candidates.Count > 0 ? "review" : "none"));
            _gamesGrid.Rows[index].Tag = m;
        }

        ApplyRowFilter();
    }

    private GameMatch? CurrentMatch =>
        _gamesGrid.CurrentRow?.Tag as GameMatch;

    private void LoadEmulators()
    {
        _emulatorCombo.Items.Clear();
        _matchButton.Enabled = false;
        try
        {
            using PinupDatabase db = PinupDatabase.Open(_dbPathBox.Text.Trim());
            foreach (PinupEmulator emu in db.GetEmulators()
                .Where(e => !_visibleOnly.Checked || e.Visible)
                .OrderBy(e => e.EmuName, StringComparer.OrdinalIgnoreCase))
            {
                _emulatorCombo.Items.Add(new EmulatorItem(emu.EmuId, emu.EmuName));
            }

            if (_emulatorCombo.Items.Count > 0)
            {
                _emulatorCombo.SelectedIndex = 0;
                _matchButton.Enabled = true;
            }

            Log($"Loaded {_emulatorCombo.Items.Count} emulator(s).");
        }
        catch (Exception ex)
        {
            Error("Failed to load emulators", ex);
        }
    }

    private void FindMatches()
    {
        if (_emulatorCombo.SelectedItem is not EmulatorItem emu)
        {
            return;
        }

        string csv = _csvPathBox.Text.Trim();
        if (!File.Exists(csv))
        {
            MessageBox.Show(this, "puplookup.csv not found. Browse to it or click Download CSV.",
                "Missing CSV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _engine = MatchEngine.Load(csv);
            using PinupDatabase db = PinupDatabase.Open(_dbPathBox.Text.Trim());
            IReadOnlyList<GameMatch> matches =
                _engine.BuildMatches(db, emu.EmuId, _visibleOnly.Checked, _includePopulated.Checked);

            _matches.Clear();
            foreach (GameMatch m in matches)
            {
                _matches.Add(m);
            }

            PopulateGamesGrid();
            _deselectAllButton.Enabled = matches.Count > 0;
            _toggleUncheckedButton.Enabled = matches.Count > 0;
            int autos = matches.Count(m => m.HasConfidentSuggestion);
            Log($"Found {matches.Count} game(s); {autos} auto-suggested. Review before applying.");
            UpdateApplyEnabled();
        }
        catch (Exception ex)
        {
            Error("Failed to build matches", ex);
        }
    }

    private void ShowCandidates()
    {
        string query = _candidateSearchBox.Text.Trim();

        if (!string.IsNullOrEmpty(query))
        {
            if (_engine is null)
            {
                _candidatesGrid.DataSource = null;
                return;
            }

            IReadOnlyList<VpsCandidate> hits = _engine.SearchAll(query);
            _candidatesGrid.DataSource = new BindingList<VpsCandidate>(hits.ToList());
            return;
        }

        if (CurrentMatch is { } match)
        {
            _candidatesGrid.DataSource = new BindingList<VpsCandidate>(match.Candidates.ToList());
        }
        else
        {
            _candidatesGrid.DataSource = null;
        }
    }

    private void CandidatesGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _gamesGrid.CurrentRow is not { } row || row.Tag is not GameMatch match)
        {
            return;
        }

        if (_candidatesGrid.Rows[e.RowIndex].DataBoundItem is VpsCandidate candidate)
        {
            match.SelectedWebGameId = candidate.WebGameId;
            row.Cells["Write"].Value = CheckState.Checked;
            row.Cells["Selected"].Value = candidate.WebGameId;
            Log($"Chose WEBGameID {candidate.WebGameId} for \"{match.Game.GameName}\" (confirmed).");
            UpdateApplyEnabled();
        }
    }

    private IReadOnlyList<PendingWebGameIdWrite> CollectPending()
    {
        var pending = new List<PendingWebGameIdWrite>();
        foreach (DataGridViewRow row in _gamesGrid.Rows)
        {
            if (row.Tag is not GameMatch m)
            {
                continue;
            }

            bool write = row.Cells["Write"].Value is CheckState.Checked
                or true; // legacy paranoia
            if (write && !string.IsNullOrWhiteSpace(m.SelectedWebGameId))
            {
                pending.Add(new PendingWebGameIdWrite(m.GameKey, m.Game.GameName, m.SelectedWebGameId!));
            }
        }

        return pending;
    }

    private void UpdateApplyEnabled() => _applyButton.Enabled = CollectPending().Count > 0;

    private void DeselectAll()
    {
        foreach (DataGridViewRow row in _gamesGrid.Rows)
        {
            row.Cells["Write"].Value = CheckState.Unchecked;
        }

        UpdateApplyEnabled();
        ApplyRowFilter();
    }

    private void ApplyChanges()
    {
        IReadOnlyList<PendingWebGameIdWrite> pending = CollectPending();
        if (pending.Count == 0)
        {
            return;
        }

        string preview = string.Join(Environment.NewLine,
            pending.Take(20).Select(p => $"  {p.GameName} -> {p.WebGameId}"));
        if (pending.Count > 20)
        {
            preview += $"{Environment.NewLine}  ... and {pending.Count - 20} more";
        }

        DialogResult result = MessageBox.Show(
            this,
            $"About to write WEBGameID for {pending.Count} game(s). A timestamped backup " +
            $"of the database is created first.{Environment.NewLine}{Environment.NewLine}" +
            $"Make sure PinUP Popper is closed.{Environment.NewLine}{Environment.NewLine}{preview}" +
            $"{Environment.NewLine}{Environment.NewLine}Proceed?",
            "Confirm write",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (result != DialogResult.OK)
        {
            return;
        }

        try
        {
            var writer = new PinupDatabaseWriter(_dbPathBox.Text.Trim());
            WebGameIdWriteResult write = writer.ApplyWebGameIds(pending);
            _lastApplied = write.Applied;
            _undoButton.Enabled = write.Applied.Count > 0;

            Log($"Backup created: {write.BackupPath}");
            Log($"Applied {write.Applied.Count} change(s).");
            foreach (WebGameIdChange c in write.Applied)
            {
                Log($"  {c.GameName}: '{c.OldValue ?? "(empty)"}' -> '{c.NewValue}'");
            }

            FindMatches(); // refresh list (applied games now have a WEBGameID)
        }
        catch (Exception ex)
        {
            Error("Write failed", ex);
        }
    }

    private void UndoChanges()
    {
        if (_lastApplied is not { Count: > 0 } applied)
        {
            return;
        }

        try
        {
            var writer = new PinupDatabaseWriter(_dbPathBox.Text.Trim());
            writer.UndoChanges(applied);
            Log($"Reverted {applied.Count} change(s).");
            _lastApplied = null;
            _undoButton.Enabled = false;
            FindMatches();
        }
        catch (Exception ex)
        {
            Error("Undo failed", ex);
        }
    }

    private async Task DownloadCsvAsync()
    {
        try
        {
            Log("Downloading VPS reference data...");
            var downloader = new VpsDownloader();
            var progress = new Progress<string>(Log);
            await downloader.DownloadAllAsync(progress);
            _csvPathBox.Text = downloader.GetLocalPath("puplookup.csv");
            PersistPathSettings();
            Log("Download complete.");
        }
        catch (Exception ex)
        {
            Error("Download failed", ex);
        }
    }

    private void PickFile(TextBox target, string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        if (File.Exists(target.Text))
        {
            dlg.InitialDirectory = Path.GetDirectoryName(target.Text);
            dlg.FileName = Path.GetFileName(target.Text);
        }

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dlg.FileName;
            PersistPathSettings();
        }
    }

    private void Log(string message) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");

    private void Error(string context, Exception ex)
    {
        Log($"ERROR: {context}: {ex.Message}");
        MessageBox.Show(this, $"{context}:{Environment.NewLine}{ex.Message}", "Error",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private sealed record EmulatorItem(int EmuId, string Name)
    {
        public override string ToString() => $"{Name} (id {EmuId})";
    }

    // ---- Dark theme ----

    private static readonly Color DarkBack = Color.FromArgb(30, 30, 30);
    private static readonly Color DarkPanel = Color.FromArgb(37, 37, 38);
    private static readonly Color DarkGridBack = Color.FromArgb(24, 24, 24);
    private static readonly Color DarkGridAlt = Color.FromArgb(32, 32, 32);
    private static readonly Color DarkText = Color.FromArgb(220, 220, 220);
    private static readonly Color DarkSubText = Color.FromArgb(180, 180, 180);
    private static readonly Color DarkSelect = Color.FromArgb(0, 120, 215);
    private static readonly Color DarkBorder = Color.FromArgb(60, 60, 60);
    private static readonly Color DarkButtonBack = Color.FromArgb(51, 51, 55);

    private void ApplyDarkTheme()
    {
        BackColor = DarkBack;
        ForeColor = DarkText;
        EnableWindowDarkTitleBar(Handle);

        ThemeControlTree(this);
        ThemeGrid(_gamesGrid);
        ThemeGrid(_candidatesGrid);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EnableWindowDarkTitleBar(Handle);
    }

    private void ThemeControlTree(Control root)
    {
        foreach (Control c in root.Controls)
        {
            ThemeControl(c);
            if (c.HasChildren)
            {
                ThemeControlTree(c);
            }
        }
    }

    private void ThemeControl(Control c)
    {
        switch (c)
        {
            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = DarkBorder;
                b.BackColor = DarkButtonBack;
                b.ForeColor = DarkText;
                break;
            case TextBox tb:
                tb.BackColor = DarkGridBack;
                tb.ForeColor = DarkText;
                tb.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = DarkGridBack;
                cb.ForeColor = DarkText;
                break;
            case CheckBox cx:
                cx.BackColor = Color.Transparent;
                cx.ForeColor = DarkText;
                break;
            case GroupBox gb:
                gb.BackColor = DarkBack;
                gb.ForeColor = DarkText;
                break;
            case Label lbl:
                lbl.BackColor = Color.Transparent;
                lbl.ForeColor = DarkText;
                break;
            case DataGridView:
                // handled by ThemeGrid
                break;
            default:
                c.BackColor = DarkBack;
                c.ForeColor = DarkText;
                break;
        }
    }

    private static void ThemeGrid(DataGridView g)
    {
        g.EnableHeadersVisualStyles = false;
        g.BackgroundColor = DarkGridBack;
        g.GridColor = DarkBorder;
        g.BorderStyle = BorderStyle.FixedSingle;
        g.ForeColor = DarkText;

        g.DefaultCellStyle.BackColor = DarkGridBack;
        g.DefaultCellStyle.ForeColor = DarkText;
        g.DefaultCellStyle.SelectionBackColor = DarkSelect;
        g.DefaultCellStyle.SelectionForeColor = Color.White;

        g.AlternatingRowsDefaultCellStyle.BackColor = DarkGridAlt;
        g.AlternatingRowsDefaultCellStyle.ForeColor = DarkText;

        g.ColumnHeadersDefaultCellStyle.BackColor = DarkPanel;
        g.ColumnHeadersDefaultCellStyle.ForeColor = DarkText;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = DarkPanel;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = DarkText;

        g.RowHeadersDefaultCellStyle.BackColor = DarkPanel;
        g.RowHeadersDefaultCellStyle.ForeColor = DarkText;
    }

    // Windows 11 / Win10 20H1+ immersive dark-mode title bar.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static void EnableWindowDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch
        {
            // Older Windows without dark title bar support — ignore.
        }
    }
}
