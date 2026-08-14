using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;
using VPin.Inspector.Core;
using VPin.Inspector.Core.Reporting;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Platforms.Vpx;
using VPin.Inspector.Platforms.Vpx.Reporting;
using VPin.Inspector.Vpx.Rules;

namespace VPin.Inspector.UI;

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
    private readonly RichTextBox _outputBox;
    private readonly RichTextBox _summaryBox;
    private readonly TreeView _rulesTree;
    private readonly LinkLabel _openRulesLink;
    private readonly TextBox _excludeBox;
    private readonly NumericUpDown _maxRunTime;
    private readonly TextBox _vpxExeBox;
    private readonly TextBox _dofConfigBox;
    private readonly ToolTip _toolTip = new();
    private readonly SplitContainer _split;
    private readonly SplitContainer _outputSplit;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    private readonly string _rulesPath;
    private RuleEngine? _engine;
    private string? _serviceError;
    private bool _suppressTreeCheck;

    // The most recent scan results, used to drive "rescan flagged".
    private List<TableReport> _lastResults = new();
    private CancellationTokenSource? _cts;

    // Maps checklist table-name link text to the full .vpx file path to open.
    private readonly Dictionary<string, string> _tableLinkPaths =
        new(StringComparer.OrdinalIgnoreCase);

    public MainForm()
    {
        Text = "VPin Inspector";
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

        _outputBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false,
            DetectUrls = false,
            Font = new Font("Consolas", 9.5f),
            BackColor = Color.White,
        };
        _outputBox.LinkClicked += OnOutputLinkClicked;

        _summaryBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false,
            DetectUrls = false,
            Font = new Font("Consolas", 9.5f),
            BackColor = Color.White,
        };
        _summaryBox.LinkClicked += OnOutputLinkClicked;

        // Right-hand side splits into a live log (top) and the clickable summary
        // (bottom), so the streaming status stays separate from the actionable
        // checklist.
        var logLabel = new Label
        {
            Text = "Log:",
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 2),
        };
        var logPanel = new Panel { Dock = DockStyle.Fill };
        logPanel.Controls.Add(_outputBox);
        logPanel.Controls.Add(logLabel);

        var summaryLabel = new Label
        {
            Text = "Summary:",
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 2),
        };

        // Colored severity legend so users learn the color scheme.
        var legend = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 2),
        };
        legend.Controls.Add(MakeLegendItem("Error", Color.Firebrick));
        legend.Controls.Add(MakeLegendItem("Warning", Color.DarkGoldenrod));
        legend.Controls.Add(MakeLegendItem("Info", SystemColors.ControlText));

        var summaryPanel = new Panel { Dock = DockStyle.Fill };
        summaryPanel.Controls.Add(_summaryBox);
        summaryPanel.Controls.Add(legend);
        summaryPanel.Controls.Add(summaryLabel);

        var outputSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
        };
        outputSplit.Panel1.Controls.Add(logPanel);
        outputSplit.Panel2.Controls.Add(summaryPanel);
        _outputSplit = outputSplit;

        var outputHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 6, 12, 6),
        };
        outputHost.Controls.Add(outputSplit);

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
            RowCount = 5,
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

        var vpxExeLabel = new Label
        {
            Text = "VPX exe:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 6, 3),
        };
        _vpxExeBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3, 3, 3, 3),
        };
        _toolTip.SetToolTip(_vpxExeBox,
            "Full path to vpinballx64.exe. Enables clicking a table name in the checklist to open it with -edit.");

        var dofConfigLabel = new Label
        {
            Text = "DOF config:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 6, 3),
        };
        _dofConfigBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3, 3, 3, 3),
        };
        _toolTip.SetToolTip(_dofConfigBox,
            "Path to the DirectOutput config .ini used by the dof-check rule. Empty = default directoutputconfig51.ini if present.");

        settingsPanel.Controls.Add(settingsHeader, 0, 0);
        settingsPanel.SetColumnSpan(settingsHeader, 2);
        settingsPanel.Controls.Add(excludeLabel, 0, 1);
        settingsPanel.Controls.Add(_excludeBox, 1, 1);
        settingsPanel.Controls.Add(maxTimeLabel, 0, 2);
        settingsPanel.Controls.Add(_maxRunTime, 1, 2);
        settingsPanel.Controls.Add(vpxExeLabel, 0, 3);
        settingsPanel.Controls.Add(_vpxExeBox, 1, 3);
        settingsPanel.Controls.Add(dofConfigLabel, 0, 4);
        settingsPanel.Controls.Add(_dofConfigBox, 1, 4);

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

    /// <summary>Builds a small colored swatch + label for the severity legend.</summary>
    private static Label MakeLegendItem(string text, Color color) => new()
    {
        Text = "\u25A0 " + text,
        ForeColor = color,
        AutoSize = true,
        Margin = new Padding(0, 0, 12, 0),
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

        // Give the log the top ~35% and the actionable summary the rest.
        try
        {
            _outputSplit.Panel1MinSize = 80;
            _outputSplit.Panel2MinSize = 120;

            int usable = _outputSplit.Height - _outputSplit.SplitterWidth;
            int min = _outputSplit.Panel1MinSize;
            int max = usable - _outputSplit.Panel2MinSize;
            if (max > min)
            {
                int distance = (int)(usable * 0.35);
                _outputSplit.SplitterDistance = Math.Clamp(distance, min, max);
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

        // Rules are grouped by analysis cost, not by scope: "Quick" rules only
        // read file names / listings / databases; "Deep Analysis" rules must fully
        // parse each .vpx. A rule's IsConfiguration flag (collection vs table)
        // still drives selection routing, independent of which group it shows in.
        InspectionRegistry registry = VpxRegistryFactory.Build(_engine);

        var quickParent = new TreeNode("Quick Checks") { Tag = GroupTag };
        var deepParent = new TreeNode("Deep Analysis") { Tag = GroupTag };

        void AddRule(IInspectionRule rule, bool isConfiguration)
        {
            var node = new TreeNode($"{rule.Id}  —  {rule.Description}")
            {
                Tag = new CheckNodeTag(rule.Id, IsConfiguration: isConfiguration),
                Checked = rule.EnabledByDefault,
                ToolTipText = rule.Description,
            };

            TreeNode parent = rule.Depth == AnalysisDepth.Quick ? quickParent : deepParent;
            parent.Nodes.Add(node);
        }

        foreach (ICollectionRule check in registry.CollectionRules)
        {
            AddRule(check, isConfiguration: true);
        }

        foreach (ITableRule rule in registry.TableRules)
        {
            AddRule(rule, isConfiguration: false);
        }

        _rulesTree.Nodes.Add(quickParent);
        _rulesTree.Nodes.Add(deepParent);

        // Parent checkboxes reflect children and start expanded.
        quickParent.Checked = quickParent.Nodes.Cast<TreeNode>().Any(n => n.Checked);
        deepParent.Checked = deepParent.Nodes.Cast<TreeNode>().Any(n => n.Checked);
        quickParent.Expand();
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
        _vpxExeBox.Text = settings.VpxExecutablePath;
        _dofConfigBox.Text = settings.DofConfigPath;

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
            VpxExecutablePath = _vpxExeBox.Text.Trim(),
            DofConfigPath = _dofConfigBox.Text.Trim(),
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
                MessageBox.Show(this, $"Rules file not found at '{_rulesPath}'.", "VPin Inspector",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Open in the system default editor for .json files.
            var psi = new System.Diagnostics.ProcessStartInfo(_rulesPath) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open rules file: {ex.Message}", "VPin Inspector",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnReloadRules(object? sender, EventArgs e)
    {
        // Force the engine to be rebuilt from disk on the next scan.
        _engine = null;

        if (!EnsureService())
        {
            MessageBox.Show(this, _serviceError, "VPin Inspector", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show(this, _serviceError, "VPin Inspector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Effective settings from the UI panel (overrides the file for this run).
        InspectionSettings settings = BuildSettingsFromUi();
        InspectionRegistry registry = VpxRegistryFactory.Build(_engine!, settings);
        var service = new InspectionService(registry);

        string scanInput = _folderBox.Text.Trim();

        // Determine the set of files to scan.
        List<string> files;
        if (mode == ScanMode.FlaggedOnly)
        {
            files = _lastResults.Where(r => r.IsFlagged).Select(r => r.FilePath).ToList();
            if (files.Count == 0)
            {
                MessageBox.Show(this, "No flagged tables to rescan.", "VPin Inspector",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(scanInput))
            {
                MessageBox.Show(this, "Please choose a tables folder first.", "VPin Inspector",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            files = service.ResolveFiles(
                scanInput,
                new ScanOptions { ExcludePatterns = settings.ExcludePatterns }).ToList();
            if (files.Count == 0)
            {
                MessageBox.Show(this, $"No tables found at '{scanInput}'.", "VPin Inspector",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        // Rules currently checked in the tree (table + collection ids combined).
        HashSet<string> enabledRuleIds = GetEnabledRuleIds();
        HashSet<string> enabledCheckIds = mode == ScanMode.Full
            ? GetEnabledConfigurationCheckIds()
            : new HashSet<string>();

        if (enabledRuleIds.Count == 0 && enabledCheckIds.Count == 0)
        {
            MessageBox.Show(this, "Nothing selected. Check at least one rule or configuration check.",
                "VPin Inspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedIds = new HashSet<string>(enabledRuleIds, StringComparer.OrdinalIgnoreCase);
        selectedIds.UnionWith(enabledCheckIds);

        var options = new ScanOptions
        {
            SelectedRuleIds = selectedIds,
            ExcludePatterns = settings.ExcludePatterns,
            MaxRunTimeSeconds = settings.MaxRunTimeSeconds,
            ExplicitFiles = mode == ScanMode.FlaggedOnly ? files : null,
            // Collection rules only make sense on a full folder scan.
            RunCollectionRules = mode == ScanMode.Full,
        };

        _cts = new CancellationTokenSource();
        SetScanningState(true, files.Count);

        string headerVerb = mode == ScanMode.FlaggedOnly ? "Rescanning flagged" : "Scanning";
        _outputBox.Clear();
        _summaryBox.Clear();
        string budgetNote = options.MaxRunTimeSeconds > 0
            ? $" (time budget: {options.MaxRunTimeSeconds:0.##}s — scan may stop early)"
            : string.Empty;
        AppendOutput(
            $"{headerVerb} {files.Count} table(s) found with {selectedIds.Count} rule(s) enabled{budgetNote}..." +
            $"{Environment.NewLine}{Environment.NewLine}");

        ScanReport? report = null;

        try
        {
            report = await Task.Run(() =>
                service.Scan(
                    scanInput,
                    options,
                    onTable: (table, done, total) =>
                    {
                        string detail = ReportRenderer.FormatTableDetail(table) + Environment.NewLine;
                        BeginInvoke(() =>
                        {
                            AppendOutput(detail);
                            _progressBar.Value = Math.Min(done, _progressBar.Maximum);
                            _statusLabel.Text = $"{headerVerb}: {done}/{total}  ({table.TableName})";
                        });
                    },
                    _cts.Token),
                _cts.Token);

            var results = report.Tables.ToList();

            RegisterTableLinks(results);
            WriteSummary(report);
            LinkifyTableNames();

            if (results.Count < files.Count)
            {
                AppendOutput(
                    $"{Environment.NewLine}Note: scanned {results.Count} of {files.Count} table(s); " +
                    $"the run stopped early (time budget reached).{Environment.NewLine}");
            }

            AppendOutput($"{Environment.NewLine}Scan complete. See the Summary pane for results.{Environment.NewLine}");

            MergeResults(mode, results);
            _statusLabel.Text = BuildStatusSummary();
        }
        catch (OperationCanceledException)
        {
            AppendOutput($"{Environment.NewLine}Scan cancelled.{Environment.NewLine}");
            _statusLabel.Text = "Scan cancelled.";
            if (report is not null)
            {
                MergeResults(mode, report.Tables.ToList());
            }
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
    private void MergeResults(ScanMode mode, List<TableReport> results)
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
                if (byPath.TryGetValue(_lastResults[i].FilePath, out TableReport? updated))
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
        _vpxExeBox.Enabled = !scanning;
        _dofConfigBox.Enabled = !scanning;
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

    /// <summary>
    /// Writes the summary into the summary pane. Only a leading severity tag
    /// (e.g. "[ERROR]" / "[WARN]") is colored; the rest of the line stays in the
    /// default color for readability.
    /// </summary>
    private void WriteSummary(ScanReport report)
    {
        Color defaultColor = _summaryBox.ForeColor;

        foreach (RenderedLine line in ReportRenderer.BuildSummaryLines(report))
        {
            Color tagColor = line.Severity switch
            {
                FindingSeverity.Error => Color.Firebrick,
                FindingSeverity.Warning => Color.DarkGoldenrod,
                _ => defaultColor,
            };

            AppendSummaryLine(line.Text, tagColor, defaultColor);
        }

        _summaryBox.SelectionColor = defaultColor;
    }

    /// <summary>
    /// Appends one summary line, coloring only a leading "[TAG]" span (when the
    /// line starts with one, after optional leading whitespace) in
    /// <paramref name="tagColor"/> and the remainder in <paramref name="defaultColor"/>.
    /// </summary>
    private void AppendSummaryLine(string text, Color tagColor, Color defaultColor)
    {
        int tagEnd = -1;
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i < text.Length && text[i] == '[')
        {
            int close = text.IndexOf(']', i);
            if (close > i)
            {
                // Only treat known severity tags as colorable (not the "[ ]"
                // checklist checkbox markers).
                string inner = text[(i + 1)..close].Trim();
                if (inner is "ERROR" or "WARN" or "WARNING" or "INFO")
                {
                    tagEnd = close + 1;
                }
            }
        }

        _summaryBox.SelectionStart = _summaryBox.TextLength;
        _summaryBox.SelectionLength = 0;

        if (tagEnd > 0 && tagColor != defaultColor)
        {
            _summaryBox.SelectionColor = tagColor;
            _summaryBox.AppendText(text[..tagEnd]);

            _summaryBox.SelectionStart = _summaryBox.TextLength;
            _summaryBox.SelectionLength = 0;
            _summaryBox.SelectionColor = defaultColor;
            _summaryBox.AppendText(text[tagEnd..] + Environment.NewLine);
        }
        else
        {
            _summaryBox.SelectionColor = defaultColor;
            _summaryBox.AppendText(text + Environment.NewLine);
        }
    }

    /// <summary>
    /// Rebuilds the map of clickable table names to their full .vpx paths. Only
    /// flagged tables are registered, and only when a VPX executable is
    /// configured (otherwise nothing is made clickable).
    /// </summary>
    private void RegisterTableLinks(IReadOnlyList<TableReport> results)
    {
        _tableLinkPaths.Clear();

        if (string.IsNullOrWhiteSpace(_vpxExeBox.Text))
        {
            return;
        }

        foreach (TableReport result in results)
        {
            if (result.IsFlagged && !string.IsNullOrEmpty(result.FilePath))
            {
                _tableLinkPaths[result.TableName] = result.FilePath;
            }
        }
    }

    /// <summary>
    /// Marks each registered table name in the checklist as a clickable link.
    /// Only occurrences that begin a checklist entry ("[ ] &lt;name&gt;") are linked.
    /// </summary>
    private void LinkifyTableNames()
    {
        if (_tableLinkPaths.Count == 0)
        {
            return;
        }

        int originalStart = _summaryBox.SelectionStart;
        int originalLength = _summaryBox.SelectionLength;
        string text = _summaryBox.Text;

        foreach (string tableName in _tableLinkPaths.Keys)
        {
            string needle = "[ ] " + tableName;
            int searchFrom = 0;
            while (true)
            {
                int idx = text.IndexOf(needle, searchFrom, StringComparison.Ordinal);
                if (idx < 0)
                {
                    break;
                }

                int nameStart = idx + "[ ] ".Length;
                _summaryBox.Select(nameStart, tableName.Length);
                SetSelectionLink(true);
                searchFrom = nameStart + tableName.Length;
            }
        }

        _summaryBox.Select(originalStart, originalLength);
    }

    /// <summary>Launches the configured VPX executable to edit the clicked table.</summary>
    private void OnOutputLinkClicked(object? sender, LinkClickedEventArgs e)
    {
        if (e.LinkText is null || !_tableLinkPaths.TryGetValue(e.LinkText, out string? tablePath))
        {
            return;
        }

        string exe = _vpxExeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            MessageBox.Show(this,
                "The configured VPX executable path is empty or does not exist. Set it in the Settings panel.",
                "VPin Inspector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-edit \"{tablePath}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to open the table in VPX:{Environment.NewLine}{ex.Message}",
                "VPin Inspector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // --- RichTextBox link support (mark current selection as a hyperlink) ---

    [StructLayout(LayoutKind.Sequential)]
    private struct CHARFORMAT2
    {
        public int cbSize;
        public int dwMask;
        public int dwEffects;
        public int yHeight;
        public int yOffset;
        public int crTextColor;
        public byte bCharSet;
        public byte bPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public char[] szFaceName;
        public short wWeight;
        public short sSpacing;
        public int crBackColor;
        public int lcid;
        public int dwReserved;
        public short sStyle;
        public short wKerning;
        public byte bUnderlineType;
        public byte bAnimation;
        public byte bRevAuthor;
        public byte bReserved1;
    }

    private const int WM_USER = 0x0400;
    private const int EM_SETCHARFORMAT = WM_USER + 68;
    private const int SCF_SELECTION = 0x0001;
    private const int CFM_LINK = 0x00000020;
    private const int CFE_LINK = 0x00000020;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CHARFORMAT2 lParam);

    private void SetSelectionLink(bool link)
    {
        var cf = new CHARFORMAT2
        {
            cbSize = Marshal.SizeOf<CHARFORMAT2>(),
            szFaceName = new char[32],
            dwMask = CFM_LINK,
            dwEffects = link ? CFE_LINK : 0,
        };

        SendMessage(_summaryBox.Handle, EM_SETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
    }
}
