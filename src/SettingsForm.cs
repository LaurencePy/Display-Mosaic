using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace DisplayMosaic
{
    public class SettingsForm : Form
    {
        private readonly ListView _list;
        private readonly TextBox _txtMatch;
        private readonly TextBox _txtOledImg;
        private readonly ComboBox _cmbOledTheme;
        private readonly TextBox _txtOtherImg;
        private readonly ComboBox _cmbOtherTheme;
        private readonly ComboBox _cmbPos;
        private readonly CheckBox _chkStart;
        private readonly Button _btnSave;

        private AppProfile _cfg;
        private bool _suppressStartupEvent;
        private readonly Action _onPresetsApplied;
        private readonly Action _onStartupChanged;

        public SettingsForm(Icon appIcon, Action onPresetsApplied, Action onStartupChanged)
        {
            _onPresetsApplied = onPresetsApplied;
            _onStartupChanged = onStartupChanged;

            _cfg = ProfileStore.Load(null);
            string[] themes = ThemeService.GetInstalledThemes();

            Text = "DisplayMosaic — Per-Monitor Wallpaper & Theme Presets";
            if (appIcon != null) Icon = appIcon;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(920, 660);
            MinimumSize = new Size(760, 620);
            StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.MinimumSize = new Size(0, 90);
            _list.Margin = new Padding(4);
            _list.Columns.Add("#", 40);
            _list.Columns.Add("Code", 80);
            _list.Columns.Add("Model", 170);
            _list.Columns.Add("Resolution", 100);
            _list.Columns.Add("Preset", 70);
            _list.Columns.Add("Current wallpaper", 410);
            root.Controls.Add(_list, 0, 0);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.AutoSize = true;
            grid.ColumnCount = 3;
            grid.RowCount = 10;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int r = 0; r < 10; r++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            root.Controls.Add(grid, 0, 1);

            _txtMatch = MakeTextBox(_cfg.OledMatch);
            grid.Controls.Add(MakeLabel("OLED monitor (model name or code)"), 0, 0);
            grid.Controls.Add(_txtMatch, 1, 0);
            grid.SetColumnSpan(_txtMatch, 2);

            Label h1 = MakeHeading("OLED monitor preset");
            grid.Controls.Add(h1, 0, 1);
            grid.SetColumnSpan(h1, 3);

            _txtOledImg = MakeTextBox(_cfg.Oled.Image);
            Button btnOBrowse = MakeButton("Browse...");
            grid.Controls.Add(MakeLabel("Wallpaper"), 0, 2);
            grid.Controls.Add(_txtOledImg, 1, 2);
            grid.Controls.Add(btnOBrowse, 2, 2);

            _cmbOledTheme = MakeThemeBox(themes, _cfg.Oled.Theme);
            grid.Controls.Add(MakeLabel("VS Code theme"), 0, 3);
            grid.Controls.Add(_cmbOledTheme, 1, 3);
            grid.SetColumnSpan(_cmbOledTheme, 2);

            Label h2 = MakeHeading("All other monitors (including the laptop screen)");
            grid.Controls.Add(h2, 0, 4);
            grid.SetColumnSpan(h2, 3);

            _txtOtherImg = MakeTextBox(_cfg.Other.Image);
            Button btnXBrowse = MakeButton("Browse...");
            grid.Controls.Add(MakeLabel("Wallpaper"), 0, 5);
            grid.Controls.Add(_txtOtherImg, 1, 5);
            grid.Controls.Add(btnXBrowse, 2, 5);

            _cmbOtherTheme = MakeThemeBox(themes, _cfg.Other.Theme);
            grid.Controls.Add(MakeLabel("VS Code theme"), 0, 6);
            grid.Controls.Add(_cmbOtherTheme, 1, 6);
            grid.SetColumnSpan(_cmbOtherTheme, 2);

            _cmbPos = new ComboBox();
            _cmbPos.Margin = new Padding(4);
            _cmbPos.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbPos.Anchor = AnchorStyles.Left;
            _cmbPos.Items.AddRange(new object[] { "Fill", "Fit", "Stretch", "Tile", "Center", "Span" });
            _cmbPos.SelectedItem = _cfg.Position;
            if (_cmbPos.SelectedIndex < 0) _cmbPos.SelectedIndex = 0;
            grid.Controls.Add(MakeLabel("Fit (all monitors)"), 0, 7);
            grid.Controls.Add(_cmbPos, 1, 7);
            grid.SetColumnSpan(_cmbPos, 2);

            _chkStart = new CheckBox();
            _chkStart.Text = "Run at sign-in and stay minimised in taskbar hidden icons area";
            _chkStart.Margin = new Padding(4);
            _chkStart.Anchor = AnchorStyles.Left;
            _chkStart.AutoSize = true;
            grid.Controls.Add(_chkStart, 0, 8);
            grid.SetColumnSpan(_chkStart, 3);

            Label hint = MakeLabel(
                "Theme names are those in the VS Code theme picker (Ctrl+K Ctrl+T), not extension identifiers. Blank = leave unchanged.\r\n" +
                "Closing or minimising hides this window to the taskbar hidden icons area (right-click the tray icon -> Exit to quit).");
            hint.ForeColor = Color.DimGray;
            hint.AutoSize = true;
            grid.Controls.Add(hint, 0, 9);
            grid.SetColumnSpan(hint, 3);

            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.ColumnCount = 4;
            bar.RowCount = 1;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Button btnIdent = MakeButton("Identify");
            _btnSave = MakeButton("Save && apply");
            Button btnHide = MakeButton("Minimise to tray");
            bar.Controls.Add(btnIdent, 0, 0);
            bar.Controls.Add(_btnSave, 2, 0);
            bar.Controls.Add(btnHide, 3, 0);
            root.Controls.Add(bar, 0, 2);

            Controls.Add(root);

            btnOBrowse.Click += delegate { BrowseImage(_txtOledImg); };
            btnXBrowse.Click += delegate { BrowseImage(_txtOtherImg); };
            _txtMatch.Leave += delegate { RefreshMonitorList(); };
            _btnSave.Click += delegate { SaveAndApply(); };
            btnIdent.Click += delegate { MonitorService.IdentifyMonitors(); };
            btnHide.Click += delegate { HideToTray(); };

            _chkStart.CheckedChanged += delegate
            {
                if (_suppressStartupEvent) return;
                try
                {
                    if (_chkStart.Checked) StartupManager.Install();
                    else StartupManager.Remove();
                    if (_onStartupChanged != null) _onStartupChanged();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "DisplayMosaic", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SyncStartupCheckbox();
                }
            };

            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    HideToTray();
                }
            };

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    HideToTray();
                }
            };

            SyncStartupCheckbox();
            RefreshMonitorList();
        }

        public void ShowFromTray()
        {
            SyncStartupCheckbox();
            RefreshMonitorList();
            if (!Visible)
            {
                Show();
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
        }

        public void SyncStartupCheckbox()
        {
            _suppressStartupEvent = true;
            try
            {
                _chkStart.Checked = StartupManager.IsInstalled();
            }
            finally
            {
                _suppressStartupEvent = false;
            }
        }

        public void RefreshMonitorList()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                string match = _txtMatch.Text.Trim();
                foreach (MonitorInfo m in MonitorService.GetMonitors(match))
                {
                    ListViewItem item = new ListViewItem(m.Index.ToString());
                    item.SubItems.Add(MonitorService.GetMonitorCode(m.Id));
                    item.SubItems.Add(m.Model ?? "");
                    item.SubItems.Add(string.Format("{0}x{1}", m.Width, m.Height));
                    item.SubItems.Add(m.IsOled ? "OLED" : "Other");
                    item.SubItems.Add(m.Wallpaper ?? "");
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        private void HideToTray()
        {
            Hide();
        }

        private void BrowseImage(TextBox box)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.jfif|All files|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    box.Text = dlg.FileName;
                }
            }
        }

        private void SaveAndApply()
        {
            try
            {
                string oledImg = _txtOledImg.Text.Trim('"', ' ');
                string otherImg = _txtOtherImg.Text.Trim('"', ' ');
                if (!string.IsNullOrEmpty(oledImg)) oledImg = ProfileStore.ResolveImagePath(oledImg);
                if (!string.IsNullOrEmpty(otherImg)) otherImg = ProfileStore.ResolveImagePath(otherImg);

                _cfg.Position = Convert.ToString(_cmbPos.SelectedItem) ?? "Fill";
                _cfg.OledMatch = _txtMatch.Text.Trim();
                _cfg.Oled = new PresetConfig { Image = oledImg, Theme = _cmbOledTheme.Text.Trim() };
                _cfg.Other = new PresetConfig { Image = otherImg, Theme = _cmbOtherTheme.Text.Trim() };
                ProfileStore.Save(_cfg);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "DisplayMosaic", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnSave.Enabled = false;
            Thread t = new Thread(delegate()
            {
                try
                {
                    MonitorService.ApplyAll(null, null);
                    if (_onPresetsApplied != null) _onPresetsApplied();
                }
                finally
                {
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _btnSave.Enabled = true;
                                RefreshMonitorList();
                            });
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private static Label MakeLabel(string text)
        {
            Label c = new Label();
            c.Text = text;
            c.Margin = new Padding(4);
            c.Anchor = AnchorStyles.Left;
            c.AutoSize = true;
            return c;
        }

        private Label MakeHeading(string text)
        {
            Label c = MakeLabel(text);
            c.Font = new Font(Font, FontStyle.Bold);
            c.Margin = new Padding(4, 14, 4, 2);
            return c;
        }

        private static TextBox MakeTextBox(string text)
        {
            TextBox c = new TextBox();
            c.Text = text ?? "";
            c.Margin = new Padding(4);
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            return c;
        }

        private static Button MakeButton(string text)
        {
            Button c = new Button();
            c.Text = text;
            c.Margin = new Padding(4);
            c.AutoSize = true;
            c.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            c.Padding = new Padding(10, 2, 10, 2);
            c.Anchor = AnchorStyles.Left;
            return c;
        }

        private static ComboBox MakeThemeBox(string[] themes, string value)
        {
            ComboBox c = new ComboBox();
            c.Margin = new Padding(4);
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            c.DropDownStyle = ComboBoxStyle.DropDown;
            c.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            c.AutoCompleteSource = AutoCompleteSource.ListItems;
            if (themes != null && themes.Length > 0)
            {
                c.Items.AddRange(themes);
            }
            c.Text = value ?? "";
            return c;
        }
    }
}
