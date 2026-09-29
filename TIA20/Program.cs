using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Forms.Application;
using Label = System.Windows.Forms.Label;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;

class TIAToolApp
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

class MainForm : Form
{
    Color DARK = Color.FromArgb(18, 20, 28);
    Color DARKER = Color.FromArgb(12, 14, 20);
    Color CARD = Color.FromArgb(26, 30, 42);
    Color ACCENT = Color.FromArgb(0, 200, 150);
    Color ACCENT2 = Color.FromArgb(255, 160, 40);
    Color ACCENT3 = Color.FromArgb(80, 160, 255);
    Color CANCEL = Color.FromArgb(200, 60, 60);
    Color TEXT = Color.FromArgb(220, 225, 235);
    Color SUBTEXT = Color.FromArgb(120, 130, 150);

    Button btnConnect, btnExport, btnImport, btnCancel, btnBrowse, btnRefresh, btnSelAll, btnSelNone, btnSearch, btnXmlRefresh;
    ComboBox cmbImportGroup, cmbPlc;
    Dictionary<string, PlcBlockUserGroup> allGroups = new Dictionary<string, PlcBlockUserGroup>();
    Label lblConnected, lblStatus, lblSelCount;
    TextBox txtFolder, txtLog, txtSearch;
    TreeView treeBlocks;

    Button btnXmlBrowseFolder, btnXmlPreview, btnXmlApply, btnXmlCancel, btnAddRule, btnRemoveRule;
    TextBox txtXmlFolder, txtXmlLog;
    DataGridView gridRules;
    Label lblXmlFileCount;
    CancellationTokenSource xcts;

    TiaPortal tiaPortal;
    PlcSoftware plcSoftware;
    CancellationTokenSource cts;
    bool suppressCheck = false;

    Dictionary<string, PlcBlock> blockMap = new Dictionary<string, PlcBlock>();
    Dictionary<string, PlcBlockUserGroup> groupMap = new Dictionary<string, PlcBlockUserGroup>();

    public MainForm()
    {
        this.Text = "TIA Block Manager  v5.0";
        this.Size = new Size(1020, 720);
        this.MinimumSize = new Size(860, 600);
        this.BackColor = DARK;
        this.ForeColor = TEXT;
        this.Font = new Font("Segoe UI", 9.5f);
        this.StartPosition = FormStartPosition.CenterScreen;
        BuildUI();
    }

    void BuildUI()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = DARKER };
        var lblTitle = new Label { Text = "⚙  TIA BLOCK MANAGER", Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = ACCENT, AutoSize = true, Location = new Point(18, 10) };
        lblConnected = new Label { Text = "● Neprisijungta", ForeColor = CANCEL, AutoSize = true, Font = new Font("Segoe UI", 8.5f), Location = new Point(20, 40) };
        btnConnect = Btn("⚡  Prisijungti", 0, 0, 160, 60, ACCENT);
        btnConnect.Dock = DockStyle.Right;
        btnConnect.Click += async (s, e) => await Connect();

        cmbPlc = new ComboBox
        {
            Size = new Size(280, 32),
            Dock = DockStyle.None,
            Anchor = AnchorStyles.None,
            BackColor = Color.FromArgb(10, 12, 18),
            ForeColor = ACCENT,
            Font = new Font("Segoe UI", 9.5f),
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbPlc.Items.Add("[ Prisijunkite pirma ]");
        cmbPlc.SelectedIndex = 0;
        cmbPlc.MouseWheel += (s, e) => ((HandledMouseEventArgs)e).Handled = true;
        header.Layout += (s, e) => {
            cmbPlc.Location = new Point(header.Width - 460, 18);
            cmbPlc.Width = 280;
        };

        header.Controls.AddRange(new Control[] { lblTitle, lblConnected, cmbPlc, btnConnect });
        this.Controls.Add(header);

        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = DARKER };
        lblStatus = new Label { Text = "Paruošta", ForeColor = SUBTEXT, Font = new Font("Segoe UI", 8.5f), AutoSize = true, Location = new Point(12, 6) };
        statusBar.Controls.Add(lblStatus);
        this.Controls.Add(statusBar);

        var tabs = new TabControl
        {
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ItemSize = new Size(200, 32),
            Location = new Point(0, 60),
            Size = new Size(this.ClientSize.Width, this.ClientSize.Height - 86)
        };
        tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        var tabManager = new TabPage("⚙  BLOKŲ VALDYMAS") { BackColor = DARK };
        tabs.TabPages.Add(tabManager);
        BuildBlockManagerTab(tabManager);

        var tabXml = new TabPage("✏  XML REDAKTORIUS") { BackColor = DARK };
        tabs.TabPages.Add(tabXml);
        BuildXmlEditorTab(tabXml);

        this.Controls.Add(tabs);
    }

    void BuildBlockManagerTab(Control tab)
    {
        var main = new Panel { Dock = DockStyle.Fill, BackColor = DARK };
        tab.Controls.Add(main);

        var treePanel = new Panel
        {
            Location = new Point(10, 10),
            Size = new Size(430, main.Height - 20),
            BackColor = CARD,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        var treeHeader = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = DARKER };
        var lblTree = new Label { Text = "PROJEKTO MEDIS", ForeColor = SUBTEXT, Font = new Font("Segoe UI", 8f, FontStyle.Bold), AutoSize = true, Location = new Point(10, 11) };
        btnRefresh = SmallBtn("↻", 0, 0, 36, 36, DARKER);
        btnRefresh.Dock = DockStyle.Right;
        btnRefresh.ForeColor = ACCENT3;
        btnRefresh.Font = new Font("Segoe UI", 14f);
        btnRefresh.Click += async (s, e) => await LoadTree();
        treeHeader.Controls.AddRange(new Control[] { lblTree, btnRefresh });

        var searchBar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(20, 24, 34) };
        txtSearch = new TextBox
        {
            Text = "Ieškoti pagal vardą...",
            Location = new Point(6, 6),
            Size = new Size(316, 22),
            BackColor = CARD,
            ForeColor = SUBTEXT,
            Font = new Font("Segoe UI", 9.5f),
            BorderStyle = BorderStyle.FixedSingle
        };
        txtSearch.GotFocus += (s, e) => { if (txtSearch.Text == "Ieškoti pagal vardą...") { txtSearch.Text = ""; txtSearch.ForeColor = TEXT; } };
        txtSearch.LostFocus += (s, e) => { if (string.IsNullOrEmpty(txtSearch.Text)) { txtSearch.Text = "Ieškoti pagal vardą..."; txtSearch.ForeColor = SUBTEXT; } };
        txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) SearchAndCheck(); };
        btnSearch = SmallBtn("Pažymėti", 328, 6, 84, 22, ACCENT);
        btnSearch.ForeColor = DARKER;
        btnSearch.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnSearch.Click += (s, e) => SearchAndCheck();
        searchBar.Controls.AddRange(new Control[] { txtSearch, btnSearch });

        var selBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(16, 20, 30) };
        btnSelAll = SmallBtn("☑ Viską", 6, 5, 78, 22, CARD);
        btnSelAll.ForeColor = ACCENT;
        btnSelAll.Click += (s, e) => { SetAll(treeBlocks.Nodes, true); UpdateCount(); };
        btnSelNone = SmallBtn("☐ Atžymėti", 90, 5, 88, 22, CARD);
        btnSelNone.ForeColor = SUBTEXT;
        btnSelNone.Click += (s, e) => { SetAll(treeBlocks.Nodes, false); UpdateCount(); };
        lblSelCount = new Label { Text = "0 pasirinkta", ForeColor = ACCENT2, Font = new Font("Segoe UI", 8f), AutoSize = true, Location = new Point(186, 9) };
        selBar.Controls.AddRange(new Control[] { btnSelAll, btnSelNone, lblSelCount });

        treeBlocks = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = CARD,
            ForeColor = TEXT,
            Font = new Font("Segoe UI", 9.5f),
            BorderStyle = BorderStyle.None,
            CheckBoxes = true,
            HideSelection = false
        };
        treeBlocks.AfterCheck += TreeAfterCheck;

        treePanel.Controls.Add(treeBlocks);
        treePanel.Controls.Add(selBar);
        treePanel.Controls.Add(searchBar);
        treePanel.Controls.Add(treeHeader);
        main.Controls.Add(treePanel);

        int rx = 450, rw = main.Width - rx - 10;

        AddLabel(main, "EXPORTO APLANKAS", rx, 10, SUBTEXT);
        txtFolder = StyledTxt(@"C:\TIA_Export", rx, 30, rw - 46, 30);
        main.Controls.Add(txtFolder);
        btnBrowse = SmallBtn("📁", rx + rw - 40, 30, 36, 30, CARD);
        btnBrowse.ForeColor = ACCENT2;
        btnBrowse.Click += (s, e) => { var d = new FolderBrowserDialog { SelectedPath = txtFolder.Text }; if (d.ShowDialog() == DialogResult.OK) txtFolder.Text = d.SelectedPath; };
        main.Controls.Add(btnBrowse);

        AddLabel(main, "IMPORTO GRUPĖ (nebūtina)", rx, 68, SUBTEXT);
        cmbImportGroup = new ComboBox
        {
            Location = new Point(rx, 88),
            Size = new Size(rw, 28),
            BackColor = CARD,
            ForeColor = TEXT,
            Font = new Font("Segoe UI", 9.5f),
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbImportGroup.Items.Add("[ Auto - pagal bloko vardą ]");
        cmbImportGroup.SelectedIndex = 0;
        main.Controls.Add(cmbImportGroup);

        btnExport = Btn("⬆  EXPORTUOTI PAŽYMĖTUS", rx, 124, rw, 44, ACCENT);
        btnExport.Enabled = false;
        btnExport.Click += async (s, e) => await ExportSelected();
        main.Controls.Add(btnExport);

        btnImport = Btn("⬇  IMPORTUOTI IŠ APLANKO", rx, 176, rw, 44, ACCENT2);
        btnImport.Enabled = false;
        btnImport.Click += async (s, e) => await ImportFolder();
        main.Controls.Add(btnImport);

        btnCancel = Btn("✕  ATŠAUKTI", rx, 228, rw, 32, CANCEL);
        btnCancel.Enabled = false;
        btnCancel.Click += (s, e) => { cts?.Cancel(); Log("⚠ Atšaukta!"); SetStatus("Atšaukta"); };
        main.Controls.Add(btnCancel);

        AddLabel(main, "ŽURNALO ĮRAŠAI", rx, 272, SUBTEXT);
        txtLog = new TextBox
        {
            Location = new Point(rx, 292),
            Size = new Size(rw, main.Height - 254),
            BackColor = DARKER,
            ForeColor = Color.FromArgb(140, 220, 160),
            Font = new Font("Consolas", 9f),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        main.Controls.Add(txtLog);

        main.Resize += (s, e) => {
            treePanel.Height = main.Height - 20;
            int w = main.Width - rx - 10;
            txtFolder.Width = w - 46; btnBrowse.Left = rx + w - 40;
            btnExport.Width = w; btnImport.Width = w; btnCancel.Width = w;
            txtLog.Size = new Size(w, main.Height - 302);
            cmbImportGroup.Width = w;
        };

        Log("Įrankis paleistas. Spauskite 'Prisijungti'.");
    }

    void BuildXmlEditorTab(Control tab)
    {
        var main = new Panel { Dock = DockStyle.Fill, BackColor = DARK, Padding = new Padding(12) };
        tab.Controls.Add(main);

        int y = 12;

        AddLabel(main, "XML FAILŲ APLANKAS", 12, y, SUBTEXT); y += 22;
        txtXmlFolder = StyledTxt(@"C:\TIA_Export", 12, y, main.Width - 120, 30);
        txtXmlFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        main.Controls.Add(txtXmlFolder);

        btnXmlBrowseFolder = SmallBtn("📁", main.Width - 100, y, 36, 30, CARD);
        btnXmlBrowseFolder.ForeColor = ACCENT2;
        btnXmlBrowseFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnXmlBrowseFolder.Click += (s, e) => {
            var d = new FolderBrowserDialog { SelectedPath = txtXmlFolder.Text };
            if (d.ShowDialog() == DialogResult.OK) { txtXmlFolder.Text = d.SelectedPath; RefreshXmlFileCount(); }
        };
        main.Controls.Add(btnXmlBrowseFolder);

        btnXmlRefresh = SmallBtn("↻", main.Width - 58, y, 36, 30, CARD);
        btnXmlRefresh.ForeColor = ACCENT3;
        btnXmlRefresh.Font = new Font("Segoe UI", 13f);
        btnXmlRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnXmlRefresh.Click += (s, e) => RefreshXmlFileCount();
        main.Controls.Add(btnXmlRefresh);
        y += 42;

        lblXmlFileCount = new Label
        {
            Text = "0 XML failų rasta",
            ForeColor = ACCENT3,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(12, y)
        };
        main.Controls.Add(lblXmlFileCount);
        y += 22;

        AddLabel(main, "KEITIMO TAISYKLĖS", 12, y, SUBTEXT);

        btnAddRule = SmallBtn("+ Pridėti", main.Width - 200, y - 2, 90, 22, ACCENT);
        btnAddRule.ForeColor = DARKER;
        btnAddRule.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnAddRule.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnAddRule.Click += (s, e) => AddRule();
        main.Controls.Add(btnAddRule);

        btnRemoveRule = SmallBtn("− Ištrinti", main.Width - 104, y - 2, 90, 22, CANCEL);
        btnRemoveRule.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnRemoveRule.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnRemoveRule.Click += (s, e) => RemoveRule();
        main.Controls.Add(btnRemoveRule);
        y += 22;

        gridRules = new DataGridView
        {
            Location = new Point(12, y),
            Size = new Size(main.Width - 24, 180),
            BackgroundColor = CARD,
            ForeColor = TEXT,
            Font = new Font("Segoe UI", 9.5f),
            BorderStyle = BorderStyle.None,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            GridColor = Color.FromArgb(35, 42, 60),
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        gridRules.ColumnHeadersDefaultCellStyle.BackColor = DARKER;
        gridRules.ColumnHeadersDefaultCellStyle.ForeColor = SUBTEXT;
        gridRules.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        gridRules.DefaultCellStyle.BackColor = CARD;
        gridRules.DefaultCellStyle.ForeColor = TEXT;
        gridRules.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 80, 60);
        gridRules.DefaultCellStyle.SelectionForeColor = TEXT;
        gridRules.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(22, 26, 38);

        var colAction = new DataGridViewComboBoxColumn { Name = "Action", HeaderText = "Veiksmas", Width = 160, FlatStyle = FlatStyle.Flat };
        colAction.Items.AddRange("Rasti ir pakeisti", "Pridėti po elemento", "Pridėti prieš elementą", "Ištrinti elementą");
        var colFind = new DataGridViewTextBoxColumn { Name = "Find", HeaderText = "Rasti (tekstas arba XML)", Width = 260, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } };
        var colReplace = new DataGridViewTextBoxColumn { Name = "Replace", HeaderText = "Pakeisti / Pridėti", Width = 260, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } };
        var colActive = new DataGridViewCheckBoxColumn { Name = "Active", HeaderText = "✓", Width = 36 };
        gridRules.Columns.AddRange(new DataGridViewColumn[] { colActive, colAction, colFind, colReplace });
        main.Controls.Add(gridRules);

        AddDefaultRules();
        y += 190;

        btnXmlPreview = Btn("🔍  PERŽIŪRĖTI PAKEITIMUS", 12, y, 200, 40, ACCENT3);
        btnXmlPreview.Click += async (s, e) => await XmlPreview();
        main.Controls.Add(btnXmlPreview);

        btnXmlApply = Btn("✓  TAIKYTI VISIEMS FAILAMS", 220, y, 220, 40, ACCENT);
        btnXmlApply.Click += async (s, e) => await XmlApply();
        main.Controls.Add(btnXmlApply);

        btnXmlCancel = Btn("✕  ATŠAUKTI", 448, y, 120, 40, CANCEL);
        btnXmlCancel.Enabled = false;
        btnXmlCancel.Click += (s, e) => { xcts?.Cancel(); XmlLog("⚠ Atšaukta!"); };
        main.Controls.Add(btnXmlCancel);
        y += 52;

        AddLabel(main, "REZULTATAI", 12, y, SUBTEXT); y += 20;
        txtXmlLog = new TextBox
        {
            Location = new Point(12, y),
            Size = new Size(main.Width - 24, main.Height - y - 12),
            BackColor = DARKER,
            ForeColor = Color.FromArgb(140, 220, 160),
            Font = new Font("Consolas", 9f),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        main.Controls.Add(txtXmlLog);

        main.Resize += (s, e) => {
            txtXmlFolder.Width = main.Width - 120;
            btnXmlBrowseFolder.Left = main.Width - 100;
            btnXmlRefresh.Left = main.Width - 58;
            btnAddRule.Left = main.Width - 200;
            btnRemoveRule.Left = main.Width - 104;
            gridRules.Width = main.Width - 24;
            txtXmlLog.Size = new Size(main.Width - 24, main.Height - txtXmlLog.Top - 12);
        };
    }

    void AddDefaultRules()
    {
        gridRules.Rows.Add(true, "Pridėti po elemento",
            "<AutoNumber>false</AutoNumber>",
            "<DBAccessibleFromWebserver>false</DBAccessibleFromWebserver>");
    }

    void AddRule()
    {
        gridRules.Rows.Add(true, "Rasti ir pakeisti", "", "");
        gridRules.FirstDisplayedScrollingRowIndex = gridRules.Rows.Count - 1;
        gridRules.Rows[gridRules.Rows.Count - 1].Cells["Find"].Selected = true;
        gridRules.BeginEdit(true);
    }

    void RemoveRule()
    {
        if (gridRules.SelectedRows.Count > 0)
            gridRules.Rows.Remove(gridRules.SelectedRows[0]);
    }

    void RefreshXmlFileCount()
    {
        try
        {
            int count = Directory.GetFiles(txtXmlFolder.Text, "*.xml").Length;
            lblXmlFileCount.Text = $"{count} XML failų";
            lblXmlFileCount.ForeColor = count > 0 ? ACCENT3 : CANCEL;
        }
        catch { lblXmlFileCount.Text = "?"; }
    }

    async Task XmlPreview()
    {
        txtXmlLog.Clear();
        string folder = txtXmlFolder.Text.Trim();
        if (!Directory.Exists(folder)) { XmlLog("✗ Aplankas nerastas!"); return; }
        var files = Directory.GetFiles(folder, "*.xml");
        if (files.Length == 0) { XmlLog("✗ XML failų nerasta!"); return; }
        var rules = GetActiveRules();
        if (rules.Count == 0) { XmlLog("✗ Nėra aktyvių taisyklių!"); return; }
        XmlLog($"🔍 Peržiūra: {files.Length} failų, {rules.Count} taisyklių");
        XmlLog("─────────────────────────────────────");
        await Task.Run(() => {
            int totalChanges = 0;
            foreach (var f in files)
            {
                string content = File.ReadAllText(f);
                string modified = ApplyRules(content, rules, out int changes);
                if (changes > 0)
                    Invoke((Action)(() => XmlLog($"  ✓ {Path.GetFileName(f)}  →  {changes} pakeitimų")));
                else
                    Invoke((Action)(() => XmlLog($"  – {Path.GetFileName(f)}  (pakeitimų nėra)")));
                totalChanges += changes;
            }
            Invoke((Action)(() => {
                XmlLog("─────────────────────────────────────");
                XmlLog($"Iš viso pakeitimų: {totalChanges} {files.Length} failuose");
            }));
        });
    }

    async Task XmlApply()
    {
        string folder = txtXmlFolder.Text.Trim();
        if (!Directory.Exists(folder)) { XmlLog("✗ Aplankas nerastas!"); return; }
        var files = Directory.GetFiles(folder, "*.xml");
        if (files.Length == 0) { XmlLog("✗ XML failų nerasta!"); return; }
        var rules = GetActiveRules();
        if (rules.Count == 0) { XmlLog("✗ Nėra aktyvių taisyklių!"); return; }
        if (MessageBox.Show(
            $"Ar tikrai norite pakeisti {files.Length} XML failų?\n\nOriginalūs failai bus perrašyti!\nRekomenduojama prieš tai padaryti kopiją.",
            "Patvirtinti pakeitimus", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        xcts = new CancellationTokenSource();
        btnXmlApply.Enabled = false;
        btnXmlCancel.Enabled = true;
        txtXmlLog.Clear();
        XmlLog($"▶ Taikoma {rules.Count} taisyklių → {files.Length} failams...");
        int ok = 0, skipped = 0;
        await Task.Run(() => {
            foreach (var f in files)
            {
                if (xcts.Token.IsCancellationRequested) break;
                try
                {
                    string content = File.ReadAllText(f);
                    string modified = ApplyRules(content, rules, out int changes);
                    if (changes > 0)
                    {
                        File.WriteAllText(f, modified, System.Text.Encoding.UTF8);
                        ok++;
                        Invoke((Action)(() => XmlLog($"  ✓ {Path.GetFileName(f)}  ({changes} pakeitimų)")));
                    }
                    else { skipped++; }
                }
                catch (Exception ex) { Invoke((Action)(() => XmlLog($"  ✗ {Path.GetFileName(f)}: {ex.Message}"))); }
            }
        });
        XmlLog("─────────────────────────────────────");
        XmlLog($"✓ Pakeista: {ok} failų,  praleista: {skipped} (jau teisingi)");
        SetStatus($"XML: {ok} failų pakeista");
        btnXmlApply.Enabled = true;
        btnXmlCancel.Enabled = false;
    }

    string ApplyRules(string content, List<XmlRule> rules, out int totalChanges)
    {
        totalChanges = 0;
        foreach (var rule in rules)
        {
            switch (rule.Action)
            {
                case "Rasti ir pakeisti":
                    if (!string.IsNullOrEmpty(rule.Find) && content.Contains(rule.Find))
                    {
                        int count = (content.Length - content.Replace(rule.Find, "").Length) / rule.Find.Length;
                        content = content.Replace(rule.Find, rule.Replace);
                        totalChanges += count;
                    }
                    break;
                case "Pridėti po elemento":
                    if (!string.IsNullOrEmpty(rule.Find) && content.Contains(rule.Find)
                        && !string.IsNullOrEmpty(rule.Replace) && !content.Contains(rule.Replace))
                    {
                        content = content.Replace(rule.Find, rule.Find + "\r\n    " + rule.Replace);
                        totalChanges++;
                    }
                    break;
                case "Pridėti prieš elementą":
                    if (!string.IsNullOrEmpty(rule.Find) && content.Contains(rule.Find)
                        && !string.IsNullOrEmpty(rule.Replace) && !content.Contains(rule.Replace))
                    {
                        content = content.Replace(rule.Find, rule.Replace + "\r\n    " + rule.Find);
                        totalChanges++;
                    }
                    break;
                case "Ištrinti elementą":
                    if (!string.IsNullOrEmpty(rule.Find) && content.Contains(rule.Find))
                    {
                        content = content.Replace(rule.Find + "\r\n    ", "")
                                         .Replace(rule.Find + "\r\n", "")
                                         .Replace(rule.Find, "");
                        totalChanges++;
                    }
                    break;
            }
        }
        return content;
    }

    List<XmlRule> GetActiveRules()
    {
        var list = new List<XmlRule>();
        foreach (DataGridViewRow row in gridRules.Rows)
        {
            bool active = row.Cells["Active"].Value is true;
            if (!active) continue;
            list.Add(new XmlRule
            {
                Action = row.Cells["Action"].Value?.ToString() ?? "",
                Find = row.Cells["Find"].Value?.ToString() ?? "",
                Replace = row.Cells["Replace"].Value?.ToString() ?? ""
            });
        }
        return list;
    }

    class XmlRule { public string Action, Find, Replace; }

    // ═══════════════════════════════════════════
    // BLOCK MANAGER LOGIKA
    // ═══════════════════════════════════════════
    Dictionary<string, PlcSoftware> allPlcs = new Dictionary<string, PlcSoftware>();

    async Task Connect()
    {
        btnConnect.Enabled = false; btnConnect.Text = "Jungiamasi...";
        await Task.Run(() => {
            try
            {
                var procs = TiaPortal.GetProcesses();
                if (procs.Count == 0) throw new Exception("TIA Portal neuzhkrautas!");
                tiaPortal = procs[0].Attach();
                var proj = tiaPortal.Projects[0];

                allPlcs.Clear();
                foreach (var g in proj.DeviceGroups)
                    FindAllPlcs(g, allPlcs);

                Invoke((Action)(async () => {
                    lblConnected.Text = $"● {proj.Name}";
                    lblConnected.ForeColor = ACCENT;
                    btnConnect.Text = "✓  Prisijungta";
                    btnConnect.BackColor = Color.FromArgb(0, 110, 80);
                    Log($"✓ Projektas: {proj.Name}");
                    Log($"✓ Rasta PLC: {allPlcs.Count}");

                    cmbPlc.Items.Clear();
                    foreach (var key in allPlcs.Keys)
                        cmbPlc.Items.Add(key);
                    if (cmbPlc.Items.Count > 0) cmbPlc.SelectedIndex = 0;
                    cmbPlc.Enabled = allPlcs.Count > 1;

                    plcSoftware = allPlcs.Values.FirstOrDefault();
                    cmbPlc.SelectedIndexChanged += async (s2, e2) => {
                        string sel = cmbPlc.SelectedItem?.ToString();
                        if (sel != null && allPlcs.ContainsKey(sel))
                        {
                            plcSoftware = allPlcs[sel];
                            Log($"▶ Pasirinktas PLC: {sel}");
                            await LoadTree();
                        }
                    };

                    btnExport.Enabled = true; btnImport.Enabled = true;
                    SetStatus($"Prisijungta → {allPlcs.Count} PLC rasta");
                    await LoadTree();
                }));
            }
            catch (Exception ex)
            {
                Invoke((Action)(() => {
                    Log($"✗ {ex.Message}");
                    btnConnect.Enabled = true;
                    btnConnect.Text = "⚡  Prisijungti";
                }));
            }
        });
    }

    void FindAllPlcs(DeviceUserGroup group, Dictionary<string, PlcSoftware> result)
    {
        foreach (var d in group.Devices)
            foreach (var i in d.DeviceItems) FindAllPlcsInItem(i, result);
        foreach (var s in group.Groups) FindAllPlcs(s, result);
    }

    void FindAllPlcsInItem(DeviceItem item, Dictionary<string, PlcSoftware> result)
    {
        var sc = item.GetService<SoftwareContainer>();
        if (sc?.Software is PlcSoftware p && !result.ContainsValue(p))
            result[p.Name] = p;
        try { foreach (var c in item.DeviceItems) FindAllPlcsInItem(c, result); } catch { }
    }

    async Task LoadTree()
    {
        if (plcSoftware == null) return;
        treeBlocks.Nodes.Clear(); blockMap.Clear(); groupMap.Clear();
        SetStatus("Kraunamas medis...");
        await Task.Run(() => {
            var root = new TreeNode($"📦  {plcSoftware.Name}") { Tag = "root" };

            // ── Program blocks (root lygis, be grupės) ──
            foreach (var block in plcSoftware.BlockGroup.Blocks)
            {
                string icon = block is FC ? "⚙" : block is FB ? "🔷" : block is GlobalDB ? "🗄" : block is InstanceDB ? "📋" : block is OB ? "🔄" : "▪";
                string key = $"__root__::{block.Name}";
                var node = new TreeNode($"{icon}  {block.Name}") { Tag = "block", Name = key, ForeColor = ACCENT2 };
                blockMap[key] = block;
                groupMap[key] = null;
                root.Nodes.Add(node);
            }

            // ── Grupės ──
            AddGroupNodes(plcSoftware.BlockGroup.Groups, root.Nodes);

            Invoke((Action)(() => {
                treeBlocks.Nodes.Add(root); root.Expand(); UpdateCount(); SetStatus("Medis užkrautas");
                PopulateGroupCombo();
            }));
        });
    }

    void PopulateGroupCombo()
    {
        allGroups.Clear();
        cmbImportGroup.Items.Clear();
        cmbImportGroup.Items.Add("[ Auto - pagal bloko vardą ]");
        CollectAllGroups(plcSoftware.BlockGroup.Groups, "");
        foreach (var key in allGroups.Keys)
            cmbImportGroup.Items.Add(key);
        cmbImportGroup.SelectedIndex = 0;
    }

    void CollectAllGroups(PlcBlockUserGroupComposition groups, string prefix)
    {
        foreach (var grp in groups)
        {
            string path = string.IsNullOrEmpty(prefix) ? grp.Name : prefix + " ▸ " + grp.Name;
            allGroups[path] = grp;
            CollectAllGroups(grp.Groups, path);
        }
    }

    void AddGroupNodes(PlcBlockUserGroupComposition groups, TreeNodeCollection nodes)
    {
        foreach (var grp in groups)
        {
            var grpNode = new TreeNode($"📁  {grp.Name}") { Tag = "folder", ForeColor = ACCENT3 };
            foreach (var block in grp.Blocks)
            {
                string icon = block is FC ? "⚙" : block is FB ? "🔷" : block is GlobalDB ? "🗄" : block is InstanceDB ? "📋" : block is OB ? "🔄" : "▪";
                string key = $"{grp.Name}::{block.Name}";
                var node = new TreeNode($"{icon}  {block.Name}") { Tag = "block", Name = key };
                blockMap[key] = block; groupMap[key] = grp;
                grpNode.Nodes.Add(node);
            }
            AddGroupNodes(grp.Groups, grpNode.Nodes);
            if (grpNode.Nodes.Count > 0) nodes.Add(grpNode);
        }
    }

    async Task ExportSelected()
    {
        var keys = GetChecked(treeBlocks.Nodes);
        if (keys.Count == 0) { Log("✗ Nepasirinktas nė vienas blokas!"); return; }
        string folder = txtFolder.Text.Trim();
        Directory.CreateDirectory(folder);
        cts = new CancellationTokenSource();
        SetBusy(true); Log($"▶ Exportuojama {keys.Count} blokų → {folder}");
        int ok = 0, fail = 0;
        await Task.Run(() => {
            foreach (var key in keys)
            {
                if (cts.Token.IsCancellationRequested) break;
                if (!blockMap.ContainsKey(key)) continue;
                var block = blockMap[key];
                try { block.Export(new FileInfo(Path.Combine(folder, block.Name + ".xml")), ExportOptions.None); ok++; Invoke((Action)(() => Log($"  ✓ {block.Name}"))); }
                catch (Exception ex) { fail++; Invoke((Action)(() => Log($"  ✗ {block.Name}: {ex.Message}"))); }
            }
        });
        Log($"▶ Baigta: {ok} sėkmingai, {fail} klaidų");
        SetStatus($"Export: {ok}/{keys.Count}");
        SetBusy(false);
    }

    async Task ImportFolder()
    {
        string folder = txtFolder.Text.Trim();
        if (!Directory.Exists(folder)) { Log($"✗ Aplankas nerastas: {folder}"); return; }
        var files = Directory.GetFiles(folder, "*.xml");
        if (files.Length == 0) { Log("✗ XML failų nerasta!"); return; }

        PlcBlockUserGroup forcedGroup = null;
        string selectedGroup = cmbImportGroup.SelectedItem?.ToString() ?? "";
        if (!selectedGroup.StartsWith("[ Auto") && allGroups.ContainsKey(selectedGroup))
        {
            forcedGroup = allGroups[selectedGroup];
            Log($"▶ Importuojama į grupę: {selectedGroup}");
        }

        cts = new CancellationTokenSource();
        SetBusy(true); Log($"▶ Importuojama {files.Length} failų");
        int ok = 0, fail = 0;
        await Task.Run(() => {
            foreach (var f in files)
            {
                if (cts.Token.IsCancellationRequested) break;
                string name = Path.GetFileNameWithoutExtension(f);
                try
                {
                    PlcBlockUserGroup g = forcedGroup;
                    if (g == null)
                    {
                        PlcBlock b = null;
                        FindBlock(plcSoftware.BlockGroup.Groups, name, ref b, ref g);
                    }
                    if (g != null) g.Blocks.Import(new FileInfo(f), ImportOptions.Override);
                    else plcSoftware.BlockGroup.Blocks.Import(new FileInfo(f), ImportOptions.Override);
                    ok++; Invoke((Action)(() => Log($"  ✓ {name}")));
                }
                catch (Exception ex) { fail++; Invoke((Action)(() => Log($"  ✗ {name}: {ex.Message}"))); }
            }
        });
        Log($"▶ Baigta: {ok} sėkmingai, {fail} klaidų");
        SetStatus($"Import: {ok}/{files.Length}");
        SetBusy(false);
        await LoadTree();
    }

    void TreeAfterCheck(object sender, TreeViewEventArgs e)
    {
        if (suppressCheck) return;
        suppressCheck = true;
        try
        {
            if (e.Node.Tag is string t && t == "folder") SetAll(e.Node.Nodes, e.Node.Checked);
            if (!e.Node.Checked && e.Node.Parent != null) e.Node.Parent.Checked = false;
        }
        finally { suppressCheck = false; UpdateCount(); }
    }

    void SearchAndCheck()
    {
        string q = txtSearch.Text.Trim().ToLower();
        if (string.IsNullOrEmpty(q) || q == "ieškoti pagal vardą...") return;
        int found = SearchNodes(treeBlocks.Nodes, q);
        Log($"🔍 '{txtSearch.Text}' – pažymėta {found} blokų");
        UpdateCount();
    }

    int SearchNodes(TreeNodeCollection nodes, string q)
    {
        int c = 0;
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is string t && t == "block" && n.Text.ToLower().Contains(q)) { n.Checked = true; n.EnsureVisible(); c++; }
            c += SearchNodes(n.Nodes, q);
        }
        return c;
    }

    void SetBusy(bool busy) { btnExport.Enabled = !busy; btnImport.Enabled = !busy; btnCancel.Enabled = busy; }
    List<string> GetChecked(TreeNodeCollection nodes) { var r = new List<string>(); foreach (TreeNode n in nodes) { if (n.Checked && n.Tag is string t && t == "block") r.Add(n.Name); r.AddRange(GetChecked(n.Nodes)); } return r; }
    void SetAll(TreeNodeCollection nodes, bool val) { foreach (TreeNode n in nodes) { n.Checked = val; SetAll(n.Nodes, val); } }
    void UpdateCount() { int c = GetChecked(treeBlocks.Nodes).Count; lblSelCount.Text = $"{c} pasirinkta"; lblSelCount.ForeColor = c > 0 ? ACCENT2 : SUBTEXT; }
    void Log(string msg) { if (txtLog.InvokeRequired) { Invoke((Action)(() => Log(msg))); return; } txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n"); txtLog.SelectionStart = txtLog.Text.Length; txtLog.ScrollToCaret(); }
    void XmlLog(string msg) { if (txtXmlLog.InvokeRequired) { Invoke((Action)(() => XmlLog(msg))); return; } txtXmlLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n"); txtXmlLog.SelectionStart = txtXmlLog.Text.Length; txtXmlLog.ScrollToCaret(); }
    void SetStatus(string msg) { if (lblStatus.InvokeRequired) Invoke((Action)(() => lblStatus.Text = msg)); else lblStatus.Text = msg; }
    void AddLabel(Control p, string t, int x, int y, Color c) => p.Controls.Add(new Label { Text = t, Location = new Point(x, y), ForeColor = c, AutoSize = true, Font = new Font("Segoe UI", 8f, FontStyle.Bold) });
    Button Btn(string t, int x, int y, int w, int h, Color bg) { var b = new Button { Text = t, Location = new Point(x, y), Size = new Size(w, h), BackColor = bg, ForeColor = Color.FromArgb(15, 17, 26), Font = new Font("Segoe UI", 10f, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand }; b.FlatAppearance.BorderSize = 0; b.MouseEnter += (s, e) => b.BackColor = ControlPaint.Light(bg, 0.15f); b.MouseLeave += (s, e) => b.BackColor = bg; return b; }
    Button SmallBtn(string t, int x, int y, int w, int h, Color bg) { var b = new Button { Text = t, Location = new Point(x, y), Size = new Size(w, h), BackColor = bg, ForeColor = TEXT, Font = new Font("Segoe UI", 9f), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand }; b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = Color.FromArgb(35, 42, 60); return b; }
    TextBox StyledTxt(string val, int x, int y, int w, int h) => new TextBox { Text = val, Location = new Point(x, y), Size = new Size(w, h), BackColor = CARD, ForeColor = TEXT, Font = new Font("Segoe UI", 10f), BorderStyle = BorderStyle.FixedSingle };
    void FindBlock(PlcBlockUserGroupComposition groups, string name, ref PlcBlock fb, ref PlcBlockUserGroup fg) { foreach (var g in groups) { foreach (var b in g.Blocks) if (b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { fb = b; fg = g; return; } FindBlock(g.Groups, name, ref fb, ref fg); } }
    PlcSoftware FindPlc(DeviceUserGroup group) { foreach (var d in group.Devices) foreach (var i in d.DeviceItems) { var r = FindPlcItem(i); if (r != null) return r; } foreach (var s in group.Groups) { var r = FindPlc(s); if (r != null) return r; } return null; }
    PlcSoftware FindPlcItem(DeviceItem item) { var sc = item.GetService<SoftwareContainer>(); if (sc?.Software is PlcSoftware p) return p; try { foreach (var c in item.DeviceItems) { var r = FindPlcItem(c); if (r != null) return r; } } catch { } return null; }
}
