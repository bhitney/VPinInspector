using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;
using VPX_Inspector.Vpx;
using VPX_Inspector.Vpx.Checks;
using VPX_Inspector.Vpx.Rules;

namespace VPX_Inspector.UI;

/// <summary>
/// Simple inspector window: pick a folder, scan all .vpx files, view the report
/// in a scrollable text area, and optionally rescan only the flagged tables.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MainForm : Form
{
    private readonly TextBox _folderBox;
    private readonly Button _browseButton;
    private readonly Button _scanButton;
    private readonly Button _rescanFlaggedButton;
    private readonly Button _reloadRulesButton;
    private readonly Button _cancelButton;
    private readonly TextBox _outputBox;
    private readonly TreeView _rulesTree;
    private readonly LinkLabel _openRulesLink;
    private readonly TextBox _excludeBox;
    private readonly NumericUpDown _maxRunTime;
    private readonly ToolTip _toolTip = new();
    private readonly SplitContainer _split;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    private readonly string _rulesPath;
    private RuleEngine? _engine;
    private string? _serviceError;
    private bool _suppressTreeCheck;

    // The most recent scan results, used to drive "rescan flagged".
    private List<TableResult> _lastResults = new();
    private CancellationTokenSource? _cts;

    public MainForm()
    {
        Text = "VPX Inspector";
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(760, 540);
        ClientSize = new Size(1000, 720);

        _rulesPath = Path.Combine(AppContext.BaseDirectory, "rules.json");

        // Two-row top area: folder row, then button row. AutoSize + docked flow
        // panels scale correctly under high DPI instead of using fixed pixels.
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(12, 12, 12, 6),
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        topPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var folderLabel = new Label
        {
            Text = "Tables folder:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 3),
        };

        _folderBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3, 5, 3, 3),
        };

        // Pre-populate with the common default tables folder when it exists.
        const string defaultTablesFolder = @"C:\vPinball\VisualPinball\Tables";
        if (Directory.Exists(defaultTablesFolder))
        {
            _folderBox.Text = defaultTablesFolder;
        }

        _browseButton = new Button
        {
            Text = "Browse...",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(3, 3, 3, 3),
        };
        _browseButton.Click += OnBrowse;

        // Button row lives in a flow panel so buttons size to their text and
        // wrap/space consistently at any DPI.
        var buttonFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 6, 0, 0),
        };

        _scanButton = MakeButton("Scan");
        _scanButton.Click += async (_, _) => await RunScanAsync(ScanMode.Full);

        _rescanFlaggedButton = MakeButton("Rescan flagged");
        _rescanFlaggedButton.Enabled = false;
        _rescanFlaggedButton.Click += async (_, _) => await RunScanAsync(ScanMode.FlaggedOnly);

        _reloadRulesButton = MakeButton("Reload rules");
        _reloadRulesButton.Click += OnReloadRules;

        _cancelButton = MakeButton("Cancel");
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => _cts?.Cancel();

        buttonFlow.Controls.Add(_scanButton);
        buttonFlow.Controls.Add(_rescanFlaggedButton);
        buttonFlow.Controls.Add(_reloadRulesButton);
        buttonFlow.Controls.Add(_cancelButton);

        topPanel.Controls.Add(folderLabel, 0, 0);
        topPanel.Controls.Add(_folderBox, 1, 0);
        topPanel.Controls.Add(_browseButton, 2, 0);
        topPanel.Controls.Add(buttonFlow, 0, 1);
        topPanel.SetColumnSpan(buttonFlow, 3);

        _outputBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9.5f),
            BackColor = Color.White,
        };

        var outputHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 6, 12, 6),
        };
        outputHost.Controls.Add(_outputBox);

        // Left-hand rules panel: header link + checkboxed tree of rules.
        var rulesPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 6, 6, 6),
        };

        _rulesTree = new TreeView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = true,
            ShowLines = true,
            ShowRootLines = true,
            ShowPlusMinus = true,
            HideSelection = false,
            FullRowSelect = false,
        };
        _rulesTree.AfterCheck += OnRuleTreeAfterCheck;

        var rulesHeader = new Panel { Dock = DockStyle.Top, Height = 26 };
        var rulesLabel = new Label
        {
            Text = "Rules (check to enable):",
            AutoSize = true,
            Dock = DockStyle.Left,
        };
        _openRulesLink = new LinkLabel
        {
            Text = "Edit rules.json",
            AutoSize = true,
            Dock = DockStyle.Right,
        };
        _openRulesLink.LinkClicked += OnOpenRules;
        rulesHeader.Controls.Add(_openRulesLink);
        rulesHeader.Controls.Add(rulesLabel);

        // Settings panel above the rules tree: exclude globs + max run time.
        var settingsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 6),
        };
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var settingsHeader = new Label
        {
            Text = "Settings:",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(3, 3, 3, 4),
        };

        var excludeLabel = new Label
        {
            Text = "Exclude:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 6, 3),
        };
        _excludeBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3, 3, 3, 3),
        };
        _toolTip.SetToolTip(_excludeBox, "Semicolon-separated file-name globs to skip, e.g. VR ROOM*; *backup*");

        var maxTimeLabel = new Label
        {
            Text = "Max time (s):",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 6, 3),
        };
        _maxRunTime = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100000,
            DecimalPlaces = 0,
            Increment = 5,
            Width = 90,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 3),
        };
        _toolTip.SetToolTip(_maxRunTime, "Stop scanning after this many seconds (0 = no limit).");

        settingsPanel.Controls.Add(settingsHeader, 0, 0);
        settingsPanel.SetColumnSpan(settingsHeader, 2);
        settingsPanel.Controls.Add(excludeLabel, 0, 1);
        settingsPanel.Controls.Add(_excludeBox, 1, 1);
        settingsPanel.Controls.Add(maxTimeLabel, 0, 2);
        settingsPanel.Controls.Add(_maxRunTime, 1, 2);

        rulesPanel.Controls.Add(_rulesTree);
        rulesPanel.Controls.Add(settingsPanel);
        rulesPanel.Controls.Add(rulesHeader);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
        };
        split.Panel1.Controls.Add(rulesPanel);
        split.Panel2.Controls.Add(outputHost);
        _split = split;

        var bottomPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12, 4, 12, 8),
        };
        bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _statusLabel = new Label
        {
            Text = "Ready.",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 3, 3),
        };
        _progressBar = new ProgressBar
        {
            Width = 260,
            Height = 18,
            Visible = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 4, 3, 3),
        };
        bottomPanel.Controls.Add(_statusLabel, 0, 0);
        bottomPanel.Controls.Add(_progressBar, 1, 0);

        Controls.Add(split);
        Controls.Add(topPanel);
        Controls.Add(bottomPanel);

        LoadRulesIntoTree();
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(10, 4, 10, 4),
        Margin = new Padding(0, 0, 8, 0),
        MinimumSize = new Size(90, 0),
    };

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Start the rules pane at 30% of the available width now that the
        // SplitContainer has a real size. Guarded because SplitterDistance
        // throws if the value conflicts with the panel minimum sizes.
        try
        {
            _split.Panel1MinSize = 150;
            _split.Panel2MinSize = 200;

            int usable = _split.Width - _split.SplitterWidth;
            int min = _split.Panel1MinSize;
            int max = usable - _split.Panel2MinSize;
            if (max > min)
            {
                int distance = (int)(usable * 0.30);
                _split.SplitterDistance = Math.Clamp(distance, min, max);
            }
        }
        catch (InvalidOperationException)
        {
            // Leave the default splitter position if the window is too small.
        }
    }

    private enum ScanMode
    {
        Full,
        FlaggedOnly,
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder containing your .vpx tables",
            UseDescriptionForTitle = true,
        };

        if (!string.IsNullOrWhiteSpace(_folderBox.Text) && Directory.Exists(_folderBox.Text))
        {
            dialog.SelectedPath = _folderBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folderBox.Text = dialog.SelectedPath;
        }
    }

    private bool EnsureService()
    {
        if (_engine is not null)
        {
            return true;
        }

        if (!File.Exists(_rulesPath))
        {
            _serviceError = $"Rules file not found at '{_rulesPath}'.";
            return false;
        }

        try
        {
            _engine = RuleEngine.LoadFromFile(_rulesPath);
            return true;
        }
        catch (Exception ex)
        {
            _serviceError = $"Failed to load rules: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Populates the rules tree from the loaded engine, checking rules whose
    /// <see cref="InspectionRule.Enabled"/> flag is true.
    /// </summary>
    private void LoadRulesIntoTree()
    {
        _rulesTree.BeginUpdate();
        _rulesTree.Nodes.Clear();

        if (!EnsureService() || _engine is null)
        {
            var errorNode = new TreeNode(_serviceError ?? "No rules loaded") { ForeColor = Color.Firebrick };
            _rulesTree.Nodes.Add(errorNode);
            _rulesTree.EndUpdate();
            return;
        }

        // Group 1: Configuration (collection-scope) checks.
        var configParent = new TreeNode("Configuration") { Tag = GroupTag };
        foreach (IConfigurationCheck check in ConfigurationCheckRunner.BuildChecks(_engine.Settings))
        {
            var node = new TreeNode($"{check.Id}  —  {check.Description}")
            {
                Tag = new CheckNodeTag(check.Id, IsConfiguration: true),
                Checked = check.Enabled,
                ToolTipText = check.Description,
            };
            configParent.Nodes.Add(node);
        }

        // Group 2: Deep Analysis (per-table) rules.
        var deepParent = new TreeNode("Deep Analysis") { Tag = GroupTag };
        foreach (InspectionRule rule in _engine.Rules)
        {
            var node = new TreeNode($"{rule.Id}  —  {rule.Description}")
            {
                Tag = new CheckNodeTag(rule.Id, IsConfiguration: false),
                Checked = rule.Enabled,
                ToolTipText = rule.Description,
            };
            deepParent.Nodes.Add(node);
        }

        _rulesTree.Nodes.Add(configParent);
        _rulesTree.Nodes.Add(deepParent);

        // Parent checkboxes reflect children and start expanded.
        configParent.Checked = configParent.Nodes.Cast<TreeNode>().Any(n => n.Checked);
        deepParent.Checked = deepParent.Nodes.Cast<TreeNode>().Any(n => n.Checked);
        configParent.Expand();
        deepParent.Expand();

        _rulesTree.EndUpdate();

        LoadSettingsIntoUi();
    }

    /// <summary>Marker tag for group (parent) nodes.</summary>
    private const string GroupTag = "__group__";

    /// <summary>Identifies a leaf check node and which family it belongs to.</summary>
    private sealed record CheckNodeTag(string Id, bool IsConfiguration);

    /// <summary>Keeps parent/child checkboxes in sync when the user toggles a node.</summary>
    private void OnRuleTreeAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_suppressTreeCheck || e.Node is null)
        {
            return;
        }

        _suppressTreeCheck = true;
        try
        {
            if (ReferenceEquals(e.Node.Tag, GroupTag))
            {
                foreach (TreeNode child in e.Node.Nodes)
                {
                    child.Checked = e.Node.Checked;
                }
            }
            else if (e.Node.Parent is TreeNode parent)
            {
                parent.Checked = parent.Nodes.Cast<TreeNode>().Any(n => n.Checked);
            }
        }
        finally
        {
            _suppressTreeCheck = false;
        }
    }

    /// <summary>Populates the settings controls from the loaded engine settings.</summary>
    private void LoadSettingsIntoUi()
    {
        if (_engine is null)
        {
            return;
        }

        InspectionSettings settings = _engine.Settings;
        _excludeBox.Text = string.Join("; ", settings.ExcludePatterns);

        decimal seconds = (decimal)settings.MaxRunTimeSeconds;
        _maxRunTime.Value = Math.Clamp(seconds, _maxRunTime.Minimum, _maxRunTime.Maximum);
    }

    /// <summary>
    /// Builds an effective settings object from the UI controls, so edits made in
    /// the Settings panel override the file values for this run (not persisted).
    /// </summary>
    private InspectionSettings BuildSettingsFromUi()
    {
        var excludes = _excludeBox.Text
            .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new InspectionSettings
        {
            MaxRunTimeSeconds = (double)_maxRunTime.Value,
            ExcludePatterns = excludes,
            // Configuration checks aren't edited in the UI; carry file config through.
            ConfigurationChecks = _engine?.Settings.ConfigurationChecks ?? new ConfigurationChecksSettings(),
        };
    }

    /// <summary>The deep-analysis rule ids currently checked in the tree.</summary>
    private HashSet<string> GetEnabledRuleIds() => GetCheckedLeafIds(isConfiguration: false);

    /// <summary>The configuration check ids currently checked in the tree.</summary>
    private HashSet<string> GetEnabledConfigurationCheckIds() => GetCheckedLeafIds(isConfiguration: true);

    private HashSet<string> GetCheckedLeafIds(bool isConfiguration)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (TreeNode parent in _rulesTree.Nodes)
        {
            foreach (TreeNode leaf in parent.Nodes)
            {
                if (leaf.Checked && leaf.Tag is CheckNodeTag tag && tag.IsConfiguration == isConfiguration)
                {
                    ids.Add(tag.Id);
                }
            }
        }

        return ids;
    }

    private void OnOpenRules(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            if (!File.Exists(_rulesPath))
            {
                MessageBox.Show(this, $"Rules file not found at '{_rulesPath}'.", "VPX Inspector",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Open in the system default editor for .json files.
            var psi = new System.Diagnostics.ProcessStartInfo(_rulesPath) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open rules file: {ex.Message}", "VPX Inspector",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnReloadRules(object? sender, EventArgs e)
    {
        // Force the engine to be rebuilt from disk on the next scan.
        _engine = null;

        if (!EnsureService())
        {
            MessageBox.Show(this, _serviceError, "VPX Inspector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "Rules failed to load.";
            return;
        }

        LoadRulesIntoTree();

        _statusLabel.Text = $"Rules reloaded from {Path.GetFileName(_rulesPath)}. " +
            (_lastResults.Count > 0
                ? "Use 'Rescan flagged' or 'Scan' to apply."
                : "Choose a folder and scan.");
    }

    private async Task RunScanAsync(ScanMode mode)
    {
        if (!EnsureService())
        {
            MessageBox.Show(this, _serviceError, "VPX Inspector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Effective settings from the UI panel (overrides the file for this run).
        InspectionSettings settings = BuildSettingsFromUi();

        // Determine the set of files to scan.
        List<string> files;
        string scanInput = _folderBox.Text.Trim();
        if (mode == ScanMode.FlaggedOnly)
        {
            files = _lastResults.Where(r => r.IsFlagged).Select(r => r.FilePath).ToList();
            if (files.Count == 0)
            {
                MessageBox.Show(this, "No flagged tables to rescan.", "VPX Inspector",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        else
        {
            string input = _folderBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                MessageBox.Show(this, "Please choose a tables folder first.", "VPX Inspector",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            IReadOnlyList<string> resolved = TableScanService.ResolveVpxFiles(
                input, out string? error, settings.ExcludePatterns);
            if (error is not null)
            {
                MessageBox.Show(this, error, "VPX Inspector", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            files = resolved.ToList();
            if (files.Count == 0)
            {
                MessageBox.Show(this, $"No .vpx files found at '{input}'.", "VPX Inspector",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        _cts = new CancellationTokenSource();
        SetScanningState(true, files.Count);

        // Build a service using the rules currently checked in the tree.
        HashSet<string> enabledIds = GetEnabledRuleIds();
        HashSet<string> enabledCheckIds = mode == ScanMode.Full
            ? GetEnabledConfigurationCheckIds()
            : new HashSet<string>();

        if (enabledIds.Count == 0 && enabledCheckIds.Count == 0)
        {
            SetScanningState(false, 0);
            _cts.Dispose();
            _cts = null;
            MessageBox.Show(this, "Nothing selected. Check at least one rule or configuration check.",
                "VPX Inspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var service = new TableScanService(_engine!, enabledIds, settings);

        string headerVerb = mode == ScanMode.FlaggedOnly ? "Rescanning flagged" : "Scanning";
        _outputBox.Clear();
        AppendOutput(
            $"{headerVerb} {files.Count} table(s) with {enabledIds.Count} rule(s) enabled..." +
            $"{Environment.NewLine}{Environment.NewLine}");

        var results = new List<TableResult>(files.Count);
        var sb = new StringBuilder();

        try
        {
            await Task.Run(() =>
            {
                service.ScanFiles(files, onResult: (result, done, total) =>
                {
                    results.Add(result);
                    string detail = ReportFormatter.FormatTableDetail(result) + Environment.NewLine;

                    // Marshal UI updates back to the UI thread.
                    BeginInvoke(() =>
                    {
                        AppendOutput(detail);
                        _progressBar.Value = Math.Min(done, _progressBar.Maximum);
                        _statusLabel.Text = $"{headerVerb}: {done}/{total}  ({result.TableName})";
                    });
                }, _cts.Token);
            }, _cts.Token);

            // Append the summary and remember results for future rescans.
            sb.Append(ReportFormatter.FormatSummary(results));

            // Configuration (collection-scope) checks - full folder scans only.
            if (mode == ScanMode.Full)
            {
                var checkContext = new ConfigurationCheckContext
                {
                    InputPath = scanInput,
                    ExcludePatterns = settings.ExcludePatterns,
                    IsFullScan = true,
                };
                HashSet<string> selectedCheckIds = enabledCheckIds;
                var checkResults = ConfigurationCheckRunner.Run(settings, checkContext, selectedCheckIds);
                foreach (var checkResult in checkResults)
                {
                    sb.Append(ReportFormatter.FormatConfigurationCheck(checkResult));
                }
            }

            AppendOutput(Environment.NewLine + sb);

            MergeResults(mode, results);
            _statusLabel.Text = BuildStatusSummary();
        }
        catch (OperationCanceledException)
        {
            AppendOutput($"{Environment.NewLine}Scan cancelled.{Environment.NewLine}");
            _statusLabel.Text = "Scan cancelled.";
            MergeResults(mode, results);
        }
        catch (Exception ex)
        {
            AppendOutput($"{Environment.NewLine}ERROR: {ex.Message}{Environment.NewLine}");
            _statusLabel.Text = "Scan failed.";
        }
        finally
        {
            SetScanningState(false, 0);
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Updates the retained result set. A full scan replaces it; a flagged-only
    /// rescan updates just those entries in place.
    /// </summary>
    private void MergeResults(ScanMode mode, List<TableResult> results)
    {
        if (mode == ScanMode.Full)
        {
            _lastResults = results;
        }
        else
        {
            var byPath = results.ToDictionary(r => r.FilePath, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _lastResults.Count; i++)
            {
                if (byPath.TryGetValue(_lastResults[i].FilePath, out TableResult? updated))
                {
                    _lastResults[i] = updated;
                }
            }
        }

        _rescanFlaggedButton.Enabled = _lastResults.Any(r => r.IsFlagged);
    }

    private string BuildStatusSummary()
    {
        int flagged = _lastResults.Count(r => r.IsFlagged);
        int clean = _lastResults.Count(r => r.IsClean);
        int failed = _lastResults.Count(r => r.Failed);
        return $"Done. {_lastResults.Count} table(s): {flagged} flagged, {clean} clean, {failed} unreadable.";
    }

    private void SetScanningState(bool scanning, int total)
    {
        _scanButton.Enabled = !scanning;
        _browseButton.Enabled = !scanning;
        _folderBox.Enabled = !scanning;
        _reloadRulesButton.Enabled = !scanning;
        _rulesTree.Enabled = !scanning;
        _excludeBox.Enabled = !scanning;
        _maxRunTime.Enabled = !scanning;
        _openRulesLink.Enabled = !scanning;
        _rescanFlaggedButton.Enabled = !scanning && _lastResults.Any(r => r.IsFlagged);
        _cancelButton.Enabled = scanning;

        if (scanning)
        {
            _progressBar.Value = 0;
            _progressBar.Maximum = Math.Max(1, total);
            _progressBar.Visible = true;
        }
        else
        {
            _progressBar.Visible = false;
        }
    }

    private void AppendOutput(string text)
    {
        _outputBox.AppendText(text);
    }
}
