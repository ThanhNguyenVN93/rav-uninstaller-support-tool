using System.Diagnostics;
using System.Text;
using Guna.UI2.WinForms;
using Microsoft.Win32;
using RavUninstallerTool.Models;
using RavUninstallerTool.Services;

namespace RavUninstallerTool;

public sealed class MainForm : Form
{
    private static string L(string key, params object[] a) => LocalizationService.T(key, a);

    private readonly StartupAction _startup;
    private readonly StringBuilder _log = new();
    private ScanResult? _scan;
    private ScanCategory _tab = ScanCategory.Folders;
    private bool _dark;
    private bool _busy;
    private bool _cleanupOnClose;
    private bool _restorePointAttempted;

    // controls
    private Guna2Panel _topBar = null!, _bottomBar = null!, _card1 = null!, _card2 = null!, _card3 = null!;
    private Label _lblTitle = null!, _lblStatus = null!, _lblStep = null!;
    private Label _c1Title = null!, _c1Body = null!, _c2Title = null!, _c2Body = null!, _c3Title = null!, _c3Body = null!;
    private Guna2Button _btnTheme = null!, _btnLang = null!, _btnUpdate = null!, _btnReport = null!, _btnAbout = null!, _btnRescan = null!;
    private Guna2Button _btnManual = null!, _btnQuick = null!, _btnAuto = null!;
    private Guna2Panel _grpManual = null!, _grpAuto = null!;
    private Label _lblGrpManual = null!, _lblGrpAuto = null!;
    private readonly Dictionary<ScanCategory, Guna2Button> _tabButtons = new();
    private Guna2DataGridView _grid = null!;
    private Guna2ProgressBar _progress = null!;
    private TableLayoutPanel _actions = null!, _tabStrip = null!;
    private ContextMenuStrip _menu = null!;
    private ToolStripMenuItem _miExplorer = null!, _miRegedit = null!, _miCopy = null!;
    private readonly ToolTip _tip = new();

    private static readonly ScanCategory[] Tabs =
    {
        ScanCategory.Folders, ScanCategory.Registry, ScanCategory.Services,
        ScanCategory.Drivers, ScanCategory.Tasks, ScanCategory.Extras
    };

    public MainForm(StartupAction startup, ScanResult? initialScan = null)
    {
        _startup = startup;
        _scan = initialScan;
        BuildUi();
        LocalizationService.LanguageChanged += () => UiInvoke(ApplyLanguage);
        ApplyTheme();
        ApplyLanguage();
        Shown += async (_, _) => await OnShownAsync();
        FormClosed += (_, _) => OnClosedCleanup();
    }

    // ================================================================ UI construction

    private void BuildUi()
    {
        SuspendLayout();
        Text = L("app_title");
        try { if (Environment.ProcessPath is { } exePath) Icon = Icon.ExtractAssociatedIcon(exePath); } catch { }
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(1000, 740);
        Size = new Size(1120, 800);
        StartPosition = FormStartPosition.CenterScreen;

        // ---- content (Fill) first, then bottom, then top (docking order)
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 12), Tag = "bg" };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Tag = "bg",
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140)); // cards
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));  // tab strip
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // grid
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));  // progress
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104)); // actions (Group cards)

        layout.Controls.Add(BuildCards(), 0, 0);
        layout.Controls.Add(BuildTabStrip(), 0, 1);
        layout.Controls.Add(BuildGrid(), 0, 2);
        layout.Controls.Add(BuildProgress(), 0, 3);
        layout.Controls.Add(BuildActions(), 0, 4);
        content.Controls.Add(layout);
        Controls.Add(content);

        _bottomBar = new Guna2Panel { Dock = DockStyle.Bottom, Height = 34, Tag = "card", Padding = new Padding(18, 0, 18, 0) };
        _lblStatus = MakeLabel("sub", 9f);
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.AutoEllipsis = true;
        _bottomBar.Controls.Add(_lblStatus);
        Controls.Add(_bottomBar);

        Controls.Add(BuildTopBar());

        _menu = new ContextMenuStrip();
        _miExplorer = new ToolStripMenuItem(); _miExplorer.Click += (_, _) => OpenInExplorer();
        _miRegedit = new ToolStripMenuItem(); _miRegedit.Click += (_, _) => OpenInRegedit();
        _miCopy = new ToolStripMenuItem(); _miCopy.Click += (_, _) => CopySelectedPath();
        _menu.Items.AddRange(new ToolStripItem[] { _miExplorer, _miRegedit, new ToolStripSeparator(), _miCopy });
        _grid.ContextMenuStrip = _menu;

        ResumeLayout(true);
    }

    private Control BuildTopBar()
    {
        _topBar = new Guna2Panel { Dock = DockStyle.Top, Height = 64, Tag = "card", Padding = new Padding(18, 0, 14, 0) };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 14, 0, 0),
            BackColor = Color.Transparent
        };
        _btnTheme = MakeButton(46, 36, "tab"); _btnTheme.Font = new Font("Segoe UI Emoji", 11f);
        _btnLang = MakeButton(52, 36, "tab");
        _btnUpdate = MakeButton(140, 36, "tab");
        _btnReport = MakeButton(130, 36, "tab");
        _btnAbout = MakeButton(100, 36, "tab");

        _btnTheme.Click += (_, _) => { _dark = !_dark; ApplyTheme(); };
        _btnLang.Click += (_, _) => LocalizationService.Toggle();
        _btnUpdate.Click += async (_, _) => await CheckUpdateAsync();
        _btnReport.Click += (_, _) => ShowReport();
        _btnAbout.Click += (_, _) => ShowAbout();

        buttons.Controls.AddRange(new Control[] { _btnTheme, _btnLang, _btnUpdate, _btnReport, _btnAbout });

        _lblTitle = MakeLabel("text", 15f, FontStyle.Bold);
        _lblTitle.Dock = DockStyle.Fill;
        _lblTitle.TextAlign = ContentAlignment.MiddleLeft;

        _topBar.Controls.Add(_lblTitle);
        _topBar.Controls.Add(buttons);
        return _topBar;
    }

    private Control BuildCards()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Tag = "bg", BackColor = Color.Transparent };
        for (int i = 0; i < 3; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

        _card1 = MakeCard(out _c1Title, out _c1Body);
        _card2 = MakeCard(out _c2Title, out _c2Body);
        _card3 = MakeCard(out _c3Title, out _c3Body);

        _btnRescan = MakeButton(96, 30, "accent");
        _btnRescan.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnRescan.Location = new Point(_card2.Width - 110, _card2.Height - 42);
        _btnRescan.Click += async (_, _) => await RunBusyAsync(ScanAsync);
        _card2.Controls.Add(_btnRescan);
        _btnRescan.BringToFront();

        t.Controls.Add(_card1, 0, 0);
        t.Controls.Add(_card2, 1, 0);
        t.Controls.Add(_card3, 2, 0);
        return t;
    }

    private Guna2Panel MakeCard(out Label title, out Label body)
    {
        var p = new Guna2Panel
        {
            Dock = DockStyle.Fill, BorderRadius = 12, Tag = "card",
            Margin = new Padding(0, 0, 12, 0), Padding = new Padding(16, 12, 16, 12), Size = new Size(300, 130)
        };
        body = MakeLabel("sub", 9.5f);
        body.Dock = DockStyle.Fill;
        title = MakeLabel("text", 11f, FontStyle.Bold);
        title.Dock = DockStyle.Top;
        title.Height = 28;
        p.Controls.Add(body);
        p.Controls.Add(title);
        return p;
    }

    private Control BuildTabStrip()
    {
        _tabStrip = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = Tabs.Length, RowCount = 1, Tag = "bg", BackColor = Color.Transparent };
        for (int i = 0; i < Tabs.Length; i++)
        {
            _tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Tabs.Length));
            var cat = Tabs[i];
            var b = MakeButton(100, 38, "tab");
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0, 4, 8, 4);
            b.Click += (_, _) => { _tab = cat; RefreshGrid(); UpdateTabButtons(); };
            _tabButtons[cat] = b;
            _tabStrip.Controls.Add(b, i, 0);
        }
        return _tabStrip;
    }

    private Control BuildGrid()
    {
        _grid = new Guna2DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, BorderStyle = BorderStyle.None,
            ColumnHeadersHeight = 38, RowTemplate = { Height = 32 }, EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Tag = "grid",
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", FillWeight = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDetail", FillWeight = 33 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colState", FillWeight = 12 });
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
            }
        };
        _grid.ContextMenuStripChanged += (_, _) => { };
        return _grid;
    }

    private Control BuildProgress()
    {
        var p = new Panel { Dock = DockStyle.Fill, Tag = "bg", BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
        _progress = new Guna2ProgressBar { Dock = DockStyle.Top, Height = 18, BorderRadius = 8, Minimum = 0, Maximum = 100, Value = 0 };
        _lblStep = MakeLabel("sub", 9.5f);
        _lblStep.Dock = DockStyle.Fill;
        _lblStep.TextAlign = ContentAlignment.MiddleLeft;
        p.Controls.Add(_lblStep);
        p.Controls.Add(_progress);
        return p;
    }

    private Control BuildActions()
    {
        _actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Tag = "bg", BackColor = Color.Transparent };
        _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65f));
        _actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));

        _grpManual = MakeGroupCard(new Padding(0, 4, 12, 0));
        _grpAuto = MakeGroupCard(new Padding(0, 4, 0, 0));

        _lblGrpManual = MakeLabel("text", 9.5f, FontStyle.Bold);
        _lblGrpManual.Dock = DockStyle.Top;
        _lblGrpManual.Height = 24;
        _lblGrpManual.TextAlign = ContentAlignment.MiddleLeft;

        _lblGrpAuto = MakeLabel("text", 9.5f, FontStyle.Bold);
        _lblGrpAuto.Dock = DockStyle.Top;
        _lblGrpAuto.Height = 24;
        _lblGrpAuto.TextAlign = ContentAlignment.MiddleLeft;

        var manualLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0)
        };
        manualLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        manualLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var autoHolder = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };

        _btnManual = MakeButton(100, 42, "tab");
        _btnQuick = MakeButton(100, 42, "accent");
        _btnAuto = MakeButton(100, 42, "danger");
        foreach (var b in new[] { _btnManual, _btnQuick, _btnAuto })
        {
            b.Dock = DockStyle.Fill;
            b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        }
        _btnManual.Margin = new Padding(0, 0, 6, 0);
        _btnQuick.Margin = new Padding(6, 0, 0, 0);
        _btnAuto.Margin = new Padding(0);

        _btnManual.Click += (_, _) => RunManualUninstall();
        _btnQuick.Click += async (_, _) => await RunBusyAsync(() => QuickCleanAsync(true));
        _btnAuto.Click += async (_, _) => await RunBusyAsync(() => AutoSafeModeAsync(true));

        manualLayout.Controls.Add(_btnManual, 0, 0);
        manualLayout.Controls.Add(_btnQuick, 1, 0);
        autoHolder.Controls.Add(_btnAuto);

        // Docking order: Fill first, then Top
        _grpManual.Controls.Add(manualLayout);
        _grpManual.Controls.Add(_lblGrpManual);
        _grpAuto.Controls.Add(autoHolder);
        _grpAuto.Controls.Add(_lblGrpAuto);

        _actions.Controls.Add(_grpManual, 0, 0);
        _actions.Controls.Add(_grpAuto, 1, 0);
        return _actions;
    }

    private static Guna2Panel MakeGroupCard(Padding margin) => new()
    {
        Dock = DockStyle.Fill, BorderRadius = 10, BorderThickness = 1,
        Tag = "groupcard", Padding = new Padding(12, 8, 12, 10), Margin = margin
    };

    private static Label MakeLabel(string tag, float size, FontStyle style = FontStyle.Regular) => new()
    {
        Tag = tag, Font = new Font("Segoe UI", size, style), UseMnemonic = false,
        BackColor = Color.Transparent, AutoSize = false
    };

    private static Guna2Button MakeButton(int w, int h, string tag) => new()
    {
        Size = new Size(w, h), BorderRadius = 8, Animated = true, Tag = tag,
        Font = new Font("Segoe UI", 9.5f), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand
    };

    // ================================================================ theme & language

    private void ApplyTheme()
    {
        Color bg = _dark ? Color.FromArgb(18, 20, 28) : Color.FromArgb(243, 245, 249);
        Color card = _dark ? Color.FromArgb(30, 34, 46) : Color.White;
        Color text = _dark ? Color.FromArgb(235, 238, 245) : Color.FromArgb(30, 35, 50);
        Color sub = _dark ? Color.FromArgb(150, 160, 180) : Color.FromArgb(100, 110, 130);
        Color accent = _dark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(59, 130, 246);
        Color tab = _dark ? Color.FromArgb(42, 47, 62) : Color.FromArgb(232, 236, 244);
        Color danger = Color.FromArgb(220, 70, 70);

        BackColor = bg;
        ForeColor = text;

        void Walk(Control c)
        {
            switch (c.Tag as string)
            {
                case "bg": c.BackColor = Color.Transparent; break;
                case "card":
                    if (c is Guna2Panel gp) gp.FillColor = card;
                    break;
                case "groupcard":
                    if (c is Guna2Panel gc) { gc.FillColor = card; gc.BorderColor = tab; }
                    break;
                case "text": c.ForeColor = text; break;
                case "sub": c.ForeColor = sub; break;
                case "grid": StyleGrid((DataGridView)c, card, text, sub, tab); break;
                case "accent": if (c is Guna2Button a) { a.FillColor = accent; a.ForeColor = Color.White; } break;
                case "danger": if (c is Guna2Button d) { d.FillColor = danger; d.ForeColor = Color.White; } break;
                case "tab": if (c is Guna2Button t) { t.FillColor = tab; t.ForeColor = text; } break;
            }
            foreach (Control child in c.Controls) Walk(child);
        }
        Walk(this);

        _progress.FillColor = tab;
        _progress.ProgressColor = accent;
        _progress.ProgressColor2 = accent;
        _btnTheme.Text = _dark ? "☀️" : "🌙";
        _menu.BackColor = card;
        _menu.ForeColor = text;
        UpdateTabButtons();
        Invalidate(true);
    }

    private static void StyleGrid(DataGridView g, Color card, Color text, Color sub, Color header)
    {
        g.BackgroundColor = card;
        g.GridColor = header;
        g.DefaultCellStyle.BackColor = card;
        g.DefaultCellStyle.ForeColor = text;
        g.DefaultCellStyle.SelectionBackColor = header;
        g.DefaultCellStyle.SelectionForeColor = text;
        g.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.ColumnHeadersDefaultCellStyle.BackColor = header;
        g.ColumnHeadersDefaultCellStyle.ForeColor = text;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = header;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        if (g is Guna2DataGridView gg)
        {
            gg.ThemeStyle.BackColor = card;
            gg.ThemeStyle.GridColor = header;
            gg.ThemeStyle.HeaderStyle.BackColor = header;
            gg.ThemeStyle.HeaderStyle.ForeColor = text;
            gg.ThemeStyle.RowsStyle.BackColor = card;
            gg.ThemeStyle.RowsStyle.ForeColor = text;
            gg.ThemeStyle.RowsStyle.SelectionBackColor = header;
            gg.ThemeStyle.RowsStyle.SelectionForeColor = text;
            gg.ThemeStyle.AlternatingRowsStyle.BackColor = card;
            gg.ThemeStyle.AlternatingRowsStyle.ForeColor = text;
            gg.ThemeStyle.AlternatingRowsStyle.SelectionBackColor = header;
            gg.ThemeStyle.AlternatingRowsStyle.SelectionForeColor = text;
        }
    }

    private void UpdateTabButtons()
    {
        Color accent = _dark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(59, 130, 246);
        Color tab = _dark ? Color.FromArgb(42, 47, 62) : Color.FromArgb(232, 236, 244);
        Color text = _dark ? Color.FromArgb(235, 238, 245) : Color.FromArgb(30, 35, 50);
        foreach (var (cat, b) in _tabButtons)
        {
            bool sel = cat == _tab;
            b.FillColor = sel ? accent : tab;
            b.ForeColor = sel ? Color.White : text;
            int n = _scan?.Items.Count(i => i.Category == cat) ?? 0;
            b.Text = $"{TabName(cat)} ({n})";
        }
    }

    private static string TabName(ScanCategory c) => c switch
    {
        ScanCategory.Folders => L("tab_folders"),
        ScanCategory.Registry => L("tab_registry"),
        ScanCategory.Services => L("tab_services"),
        ScanCategory.Drivers => L("tab_drivers"),
        ScanCategory.Tasks => L("tab_tasks"),
        _ => L("tab_extras")
    };

    private void ApplyLanguage()
    {
        Text = L("app_title");
        try { if (Environment.ProcessPath is { } exePath) Icon = Icon.ExtractAssociatedIcon(exePath); } catch { }
        _lblTitle.Text = $"{L("app_title")}  v{UpdateService.CurrentVersion}";
        _btnLang.Text = LocalizationService.Language.ToUpperInvariant();
        _btnUpdate.Text = L("btn_update");
        _btnReport.Text = L("btn_report");
        _btnAbout.Text = L("btn_about");
        _btnRescan.Text = L("btn_rescan");
        _lblGrpManual.Text = L("group_manual");
        _lblGrpAuto.Text = L("group_auto");
        _btnManual.Text = L("btn_manual");
        _btnQuick.Text = L("btn_quick");
        _btnAuto.Text = L("btn_auto");
        _tip.SetToolTip(_btnTheme, L("btn_theme_tip"));
        _tip.SetToolTip(_btnLang, L("btn_lang_tip"));
        _grid.Columns["colName"]!.HeaderText = L("col_name");
        _grid.Columns["colDetail"]!.HeaderText = L("col_detail");
        _grid.Columns["colState"]!.HeaderText = L("col_state");
        _miExplorer.Text = L("ctx_explorer");
        _miRegedit.Text = L("ctx_regedit");
        _miCopy.Text = L("ctx_copy");
        _c1Title.Text = L("card_os");
        _c2Title.Text = L("card_rav");
        _c3Title.Text = L("card_env");
        if (!_busy) _lblStep.Text = L("step_idle");
        UpdateCards();
        UpdateTabButtons();
    }

    private void UpdateCards()
    {
        if (_scan == null)
        {
            _c1Body.Text = _c2Body.Text = _c3Body.Text = L("status_scanning");
            return;
        }
        var s = _scan;
        _c1Body.Text = (s.IsWindows10OrHigher ? "" : L("os_unsupported") + "\n")
                       + L("os_line", s.OsDescription, s.Architecture) + "\n" + L("defender_line", s.DefenderStatus);
        _c2Body.Text = (s.Items.Count > 0 ? L("rav_found", s.Items.Count) : L("rav_clean")) + "\n"
                       + (s.HasActiveDriversOrServices ? L("rav_active") : L("rav_inactive"));
        _c3Body.Text = L("env_mode", s.IsSafeMode ? L("mode_safe") : L("mode_normal")) + "\n" + L("env_restore");
    }

    // ================================================================ grid

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        if (_scan == null) return;
        foreach (var it in _scan.Items.Where(i => i.Category == _tab))
        {
            int idx = _grid.Rows.Add(it.NameOrPath, it.Detail, it.State);
            if (it.IsCritical) _grid.Rows[idx].DefaultCellStyle.ForeColor = Color.FromArgb(220, 70, 70);
        }
    }

    private ScanItem? SelectedItem()
    {
        if (_scan == null || _grid.SelectedRows.Count == 0) return null;
        string? path = _grid.SelectedRows[0].Cells[0].Value as string;
        return _scan.Items.FirstOrDefault(i => i.Category == _tab && i.NameOrPath == path);
    }

    private void OpenInExplorer()
    {
        var it = SelectedItem(); if (it == null) return;
        try
        {
            string p = it.NameOrPath;
            if (File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{p}\"");
            else if (Directory.Exists(p)) Process.Start("explorer.exe", $"\"{p}\"");
            else
            {
                string? parent = Path.GetDirectoryName(p);
                if (parent != null && Directory.Exists(parent)) Process.Start("explorer.exe", $"\"{parent}\"");
            }
        }
        catch (Exception ex) { Log(ex.Message); }
    }

    private void OpenInRegedit()
    {
        var it = SelectedItem(); if (it == null) return;
        try
        {
            string key = it.NameOrPath;
            if (key.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)) key = "HKEY_LOCAL_MACHINE" + key[4..];
            if (!key.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase)) { Log("Not a registry path: " + key); return; }
            // Run / Policy entries point at a value: open its parent key.
            if (it.State is "Startup" or "Policy") key = key[..key.LastIndexOf('\\')];

            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
                k.SetValue("LastKey", "Computer\\" + key);
            foreach (var p in Process.GetProcessesByName("regedit")) { try { p.Kill(); } catch { } }
            Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }
        catch (Exception ex) { Log(ex.Message); }
    }

    private void CopySelectedPath()
    {
        var it = SelectedItem(); if (it == null) return;
        try { Clipboard.SetText(it.NameOrPath); Log(L("copied")); }
        catch (Exception ex) { Log(ex.Message); }
    }

    // ================================================================ workflow

    private async Task OnShownAsync()
    {
        switch (_startup)
        {
            case StartupAction.SafeModeClean: await RunSafeModePhaseAsync(); break;
            case StartupAction.PostVerify: await RunPostVerifyAsync(); break;
            case StartupAction.CleanRemnants:
                await RunBusyAsync(ScanAsync);
                await RunBusyAsync(() => QuickCleanAsync(false));
                break;
            case StartupAction.AutoSafeMode:
                await RunBusyAsync(ScanAsync);
                await RunBusyAsync(() => AutoSafeModeAsync(false));
                break;
            default:
                if (_scan != null)
                {
                    Log(L("status_scan_done", _scan.Items.Count));
                    UpdateCards();
                    UpdateTabButtons();
                    RefreshGrid();
                }
                else
                {
                    await RunBusyAsync(ScanAsync);
                }
                await RunBusyAsync(CreateStartupRestorePointAsync);
                break;
        }
    }

    private async Task CreateStartupRestorePointAsync()
    {
        if (_restorePointAttempted) return;
        _restorePointAttempted = true;
        SetProgress(20, L("step_restore"));
        await Task.Run(() => CleanerService.CreateSilentRestorePoint(Log));
        SetProgress(0, L("step_idle"));
    }

    private async Task ScanAsync()
    {
        SetProgress(0, L("status_scanning"));
        Log(L("status_scanning"));
        try
        {
            _scan = await Task.Run(() => SystemScannerService.Scan(m => Log(m)));
            Log(L("status_scan_done", _scan.Items.Count));
        }
        catch (Exception ex) { Log(L("status_scan_failed", ex.Message)); }
        UpdateCards();
        UpdateTabButtons();
        RefreshGrid();
        SetProgress(0, L("step_idle"));
    }

    private void RunManualUninstall()
    {
        if (_scan?.UninstallExecutablePath is not { } exe)
        {
            MessageBox.Show(L("no_uninstaller"), L("info_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe, _scan.UninstallArgs ?? "") { UseShellExecute = true });
            Log(L("uninstaller_started", exe));
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task QuickCleanAsync(bool confirm)
    {
        if (confirm && MessageBox.Show(L("confirm_quick"), L("confirm_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            if (!_restorePointAttempted)
            {
                _restorePointAttempted = true;
                SetProgress(5, L("step_restore"));
                await Task.Run(() => CleanerService.CreateSilentRestorePoint(Log));
            }
            SetProgress(15, L("step_clean"));
            int exit = await CleanerService.RunPowerShellCleanerAsync(true, CleanerService.DefaultLogPath,
                (p, m) => { SetProgress(Math.Max(15, p), m); Log(m); });
            Log($"RAV.ps1 => {exit}");
            SetProgress(100, L("step_done"));
            await ScanAsync();
            SetProgress(100, L("clean_done"));
        }
        catch (Exception ex) { ShowError(L("clean_failed", ex.Message)); }
    }

    private async Task AutoSafeModeAsync(bool confirm)
    {
        if (confirm && MessageBox.Show(L("confirm_auto"), L("confirm_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        bool ok = await CleanerService.PrepareSafeModeAndReboot(
            (p, m) => SetProgress(p, m), Log, createRestorePoint: !_restorePointAttempted);
        if (!ok) ShowError(L("safe_prepare_failed", "see log"));
    }

    private async Task RunSafeModePhaseAsync()
    {
        SetBusy(true);
        _actions.Visible = false;
        SetProgress(2, L("safe_clean_title"));
        _scan = null;
        UpdateCards();
        await CleanerService.ExecuteSafeModeCleanupAsync((p, m) => SetProgress(p, m), Log);
    }

    private async Task RunPostVerifyAsync()
    {
        SetBusy(true);
        _actions.Visible = false;
        SetProgress(10, L("post_verify_title"));
        await ScanAsync();
        SetProgress(100, L("step_done"));
        int left = _scan?.Items.Count ?? 0;
        string msg = left == 0 ? L("post_verify_ok") : L("post_verify_left", left);
        Log(msg);
        _cleanupOnClose = true;
        SetBusy(false);
        MessageBox.Show(msg, L("post_verify_title"), MessageBoxButtons.OK,
            left == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (left == 0) Close();
        else _actions.Visible = true;
    }

    private void OnClosedCleanup()
    {
        if (!_cleanupOnClose) return;
        try { Task.Run(() => CleanerService.CompletePostCleanAndCleanup(Log)).GetAwaiter().GetResult(); }
        catch { /* best effort */ }
    }

    private async Task CheckUpdateAsync()
    {
        Log(L("update_checking"));
        var r = await UpdateService.CheckAsync();
        if (r.Success && r.IsNewer && r.CanAutoUpdate)
        {
            Log(L("update_confirm_install", r.LatestVersion).Replace('\n', ' '));
            if (MessageBox.Show(L("update_confirm_install", r.LatestVersion), L("btn_update"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                var progress = new Progress<string>(Log);
                if (await UpdateService.DownloadAndApplyUpdateAsync(r, progress))
                {
                    Application.Exit();
                    return;
                }
                MessageBox.Show(L("update_apply_failed", r.Url), L("btn_update"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }
        string msg = !r.Success ? L("update_failed")
            : r.IsNewer ? L("update_available", r.LatestVersion, r.Url)
            : L("update_latest", UpdateService.CurrentVersion);
        Log(msg.Replace('\n', ' '));
        MessageBox.Show(msg, L("btn_update"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowReport()
    {
        string fileLog = "";
        try
        {
            if (File.Exists(CleanerService.DefaultLogPath)) fileLog = "\r\n---- " + CleanerService.DefaultLogPath + " ----\r\n" + File.ReadAllText(CleanerService.DefaultLogPath);
        }
        catch { /* ignore */ }

        using var f = new Form
        {
            Text = L("report_title"), Size = new Size(820, 560), StartPosition = FormStartPosition.CenterParent,
            BackColor = BackColor, ForeColor = ForeColor, Font = Font
        };
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.5f), Text = _log.ToString() + fileLog,
            BackColor = _dark ? Color.FromArgb(30, 34, 46) : Color.White, ForeColor = ForeColor
        };
        var copy = new Guna2Button
        {
            Text = L("btn_copy"), Dock = DockStyle.Bottom, Height = 40, BorderRadius = 8,
            FillColor = Color.FromArgb(59, 130, 246), ForeColor = Color.White
        };
        copy.Click += (_, _) => { try { Clipboard.SetText(box.Text); } catch { } };
        f.Controls.Add(box);
        f.Controls.Add(copy);
        f.ShowDialog(this);
    }

    private void ShowAbout()
    {
        using var f = new Form
        {
            Text = L("about_title"), Size = new Size(640, 480), StartPosition = FormStartPosition.CenterParent,
            BackColor = BackColor, ForeColor = ForeColor, Font = Font, MinimizeBox = false, MaximizeBox = false
        };
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 10f), Text = L("about_text", UpdateService.CurrentVersion),
            BackColor = _dark ? Color.FromArgb(30, 34, 46) : Color.White, ForeColor = ForeColor
        };
        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = box.BackColor };
        pad.Controls.Add(box);
        var close = new Guna2Button
        {
            Text = L("btn_close"), Dock = DockStyle.Bottom, Height = 40, BorderRadius = 8,
            FillColor = Color.FromArgb(59, 130, 246), ForeColor = Color.White, Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        close.Click += (_, _) => f.Close();
        f.Controls.Add(pad);
        f.Controls.Add(close);
        f.ShowDialog(this);
    }

    // ================================================================ helpers

    private async Task RunBusyAsync(Func<Task> work)
    {
        if (_busy) return;
        SetBusy(true);
        try { await work(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var b in new[] { _btnManual, _btnQuick, _btnAuto, _btnRescan }) b.Enabled = !busy;
    }

    private void ShowError(string msg)
    {
        Log(msg);
        MessageBox.Show(msg, L("error_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetProgress(int pct, string text) => UiInvoke(() =>
    {
        _progress.Value = Math.Clamp(pct, 0, 100);
        _lblStep.Text = text;
    });

    private void Log(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        lock (_log) _log.AppendLine(line);
        if (_startup is StartupAction.SafeModeClean or StartupAction.PostVerify)
        {
            try { if (Directory.Exists(CleanerService.WorkDir)) File.AppendAllText(CleanerService.DefaultLogPath, line + Environment.NewLine); }
            catch { /* ignore */ }
        }
        UiInvoke(() => _lblStatus.Text = msg.Replace('\r', ' ').Replace('\n', ' '));
    }

    private void UiInvoke(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { try { BeginInvoke(a); } catch (ObjectDisposedException) { } }
        else a();
    }
}
