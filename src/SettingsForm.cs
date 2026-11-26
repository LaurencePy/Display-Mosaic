using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DisplayMosaic
{
    public class SettingsForm : Form
    {
        private sealed class MonitorChoice
        {
            public bool IsDefault;
            public bool IsDisconnected;
            public MonitorInfo Connected;
            public MonitorPreset Saved;
            public string DisplayText;

            public string Key
            {
                get
                {
                    if (IsDefault) return "__DEFAULT__";
                    if (Connected != null) return Connected.Id;
                    return Saved != null ? Saved.Id : "";
                }
            }

            public override string ToString()
            {
                return DisplayText ?? "";
            }
        }

        private sealed class CachedThumb : IDisposable
        {
            public string Signature;
            public Bitmap Thumbnail;
            public int OriginalWidth;
            public int OriginalHeight;
            public bool Failed;

            public void Dispose()
            {
                if (Thumbnail != null)
                {
                    Thumbnail.Dispose();
                    Thumbnail = null;
                }
            }
        }

        private sealed class MonitorMapItem
        {
            public MonitorInfo Monitor;
            public string Label;
            public string Resolution;
            public string Theme;
            public Image Thumbnail;
            public bool IsOverride;
            public RectangleF Bounds;
        }

        private sealed class MonitorMapPanel : Control
        {
            private readonly List<MonitorMapItem> _items = new List<MonitorMapItem>();
            private string _selectedId;
            private string _hoveredId;

            public event Action<string> MonitorClicked;

            public MonitorMapPanel()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw,
                    true);
                BackColor = Color.FromArgb(241, 244, 249);
            }

            public void SetData(List<MonitorMapItem> items, string selectedId)
            {
                _items.Clear();
                if (items != null) _items.AddRange(items);
                _selectedId = selectedId;
                Invalidate();
            }

            public void SetSelectedId(string selectedId)
            {
                if (!string.Equals(_selectedId, selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedId = selectedId;
                    Invalidate();
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                string hit = HitTest(e.Location);
                if (!string.Equals(hit, _hoveredId, StringComparison.OrdinalIgnoreCase))
                {
                    _hoveredId = hit;
                    Cursor = hit != null ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hoveredId != null)
                {
                    _hoveredId = null;
                    Cursor = Cursors.Default;
                    Invalidate();
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                string hit = HitTest(e.Location);
                if (hit != null && MonitorClicked != null)
                {
                    MonitorClicked(hit);
                }
            }

            private string HitTest(Point pt)
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    if (_items[i].Bounds.Contains(pt))
                    {
                        return _items[i].Monitor.Id;
                    }
                }
                return null;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                using (Pen borderPen = new Pen(Color.FromArgb(214, 220, 229), 1f))
                {
                    g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
                }

                if (_items.Count == 0)
                {
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        using (Brush b = new SolidBrush(Color.DimGray))
                        {
                            g.DrawString("No active monitors detected.", Font, b, ClientRectangle, sf);
                        }
                    }
                    return;
                }

                int minX = int.MaxValue;
                int minY = int.MaxValue;
                int maxX = int.MinValue;
                int maxY = int.MinValue;

                foreach (MonitorMapItem item in _items)
                {
                    MonitorInfo m = item.Monitor;
                    if (m.Left < minX) minX = m.Left;
                    if (m.Top < minY) minY = m.Top;
                    if (m.Left + m.Width > maxX) maxX = m.Left + m.Width;
                    if (m.Top + m.Height > maxY) maxY = m.Top + m.Height;
                }

                float totalW = Math.Max(1, maxX - minX);
                float totalH = Math.Max(1, maxY - minY);
                float padX = 16f;
                float padY = 12f;
                float availW = Math.Max(40f, Width - padX * 2f);
                float availH = Math.Max(40f, Height - padY * 2f);
                float scale = Math.Min(availW / totalW, availH / totalH);

                float drawW = totalW * scale;
                float drawH = totalH * scale;
                float offsetX = (Width - drawW) / 2f;
                float offsetY = (Height - drawH) / 2f;

                foreach (MonitorMapItem item in _items)
                {
                    MonitorInfo m = item.Monitor;
                    float x = offsetX + (m.Left - minX) * scale + 2f;
                    float y = offsetY + (m.Top - minY) * scale + 2f;
                    float w = Math.Max(36f, m.Width * scale - 4f);
                    float h = Math.Max(30f, m.Height * scale - 4f);
                    item.Bounds = new RectangleF(x, y, w, h);

                    bool isSelected = string.Equals(m.Id, _selectedId, StringComparison.OrdinalIgnoreCase);
                    bool isHovered = string.Equals(m.Id, _hoveredId, StringComparison.OrdinalIgnoreCase);

                    RectangleF inner = new RectangleF(x + 2f, y + 2f, Math.Max(1f, w - 4f), Math.Max(1f, h - 4f));
                    using (Brush bgBrush = new SolidBrush(Color.FromArgb(34, 39, 48)))
                    {
                        g.FillRectangle(bgBrush, inner);
                    }

                    if (item.Thumbnail != null)
                    {
                        GraphicsState st = g.Save();
                        g.SetClip(inner);
                        DrawCoverImage(g, item.Thumbnail, inner);
                        if (!item.IsOverride)
                        {
                            using (Brush dim = new SolidBrush(Color.FromArgb(95, 24, 28, 36)))
                            {
                                g.FillRectangle(dim, inner);
                            }
                        }
                        g.Restore(st);
                    }

                    float dpi = Math.Max(1f, g.DpiY / 96f);

                    // Top badges
                    float badgePad = 5f * dpi;
                    string numText = "#" + m.Index;
                    using (Font numFont = new Font(Font.FontFamily, 8.5f, FontStyle.Bold))
                    {
                        SizeF numSize = g.MeasureString(numText, numFont);
                        RectangleF numRect = new RectangleF(x + badgePad, y + badgePad, numSize.Width + 8f * dpi, numSize.Height + 2f * dpi);
                        Color badgeColor = isSelected ? Color.FromArgb(0, 103, 192) : Color.FromArgb(200, 24, 28, 36);
                        using (Brush b = new SolidBrush(badgeColor))
                        {
                            g.FillRectangle(b, numRect);
                        }
                        g.DrawString(numText, numFont, Brushes.White, numRect.X + 4f * dpi, numRect.Y + 1f * dpi);
                    }

                    if (m.IsPrimary && w > 88f * dpi)
                    {
                        using (Font pFont = new Font(Font.FontFamily, 7.5f, FontStyle.Bold))
                        {
                            string pText = "PRIMARY";
                            SizeF pSize = g.MeasureString(pText, pFont);
                            RectangleF pRect = new RectangleF(x + w - pSize.Width - 10f * dpi, y + badgePad, pSize.Width + 6f * dpi, pSize.Height + 2f * dpi);
                            using (Brush b = new SolidBrush(Color.FromArgb(185, 24, 28, 36)))
                            {
                                g.FillRectangle(b, pRect);
                            }
                            using (Brush fg = new SolidBrush(Color.FromArgb(215, 232, 250)))
                            {
                                g.DrawString(pText, pFont, fg, pRect.X + 3f * dpi, pRect.Y + 1f * dpi);
                            }
                        }
                    }

                    // Bottom caption bar inside monitor
                    float barH = Math.Min(h * 0.44f, 34f * dpi);
                    if (h >= 54f * dpi && barH >= 20f * dpi)
                    {
                        RectangleF barRect = new RectangleF(inner.X, inner.Bottom - barH, inner.Width, barH);
                        using (Brush barBrush = new SolidBrush(Color.FromArgb(195, 20, 24, 31)))
                        {
                            g.FillRectangle(barBrush, barRect);
                        }

                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Trimming = StringTrimming.EllipsisCharacter;
                            sf.FormatFlags = StringFormatFlags.NoWrap;

                            using (Font titleFont = new Font(Font.FontFamily, 8f, FontStyle.Bold))
                            {
                                RectangleF titleRect = new RectangleF(barRect.X + 5f * dpi, barRect.Y + 2f * dpi, Math.Max(10f, barRect.Width - 10f * dpi), 15f * dpi);
                                g.DrawString(item.Label ?? "", titleFont, Brushes.White, titleRect, sf);
                            }

                            if (barH >= 29f * dpi)
                            {
                                using (Font subFont = new Font(Font.FontFamily, 7.2f, FontStyle.Regular))
                                using (Brush subBrush = new SolidBrush(Color.FromArgb(202, 212, 226)))
                                {
                                    string subText = string.IsNullOrEmpty(item.Theme)
                                        ? item.Resolution
                                        : string.Format("{0} • {1}", item.Resolution, item.Theme);
                                    RectangleF subRect = new RectangleF(barRect.X + 5f * dpi, barRect.Y + 16f * dpi, Math.Max(10f, barRect.Width - 10f * dpi), 15f * dpi);
                                    g.DrawString(subText, subFont, subBrush, subRect, sf);
                                }
                            }
                        }
                    }

                    // Outer bezel border
                    Color borderColor = isSelected
                        ? Color.FromArgb(0, 103, 192)
                        : (isHovered ? Color.FromArgb(72, 144, 214) : Color.FromArgb(148, 158, 173));
                    float borderW = isSelected ? 3f : (isHovered ? 2f : 1.5f);
                    using (Pen p = new Pen(borderColor, borderW))
                    {
                        g.DrawRectangle(p, x, y, w, h);
                    }
                }
            }
        }

        private sealed class ImagePreviewPanel : Control
        {
            private Image _image;
            private string _placeholder = "No wallpaper override\n(keeps current wallpaper)";

            public ImagePreviewPanel()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw,
                    true);
                BackColor = Color.FromArgb(30, 34, 42);
            }

            public void SetPreview(Image image, string placeholder)
            {
                _image = image;
                _placeholder = placeholder ?? "";
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                RectangleF rect = new RectangleF(1f, 1f, Math.Max(1f, Width - 2f), Math.Max(1f, Height - 2f));
                if (_image != null)
                {
                    DrawContainImage(g, _image, rect);
                }
                else
                {
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        using (Brush b = new SolidBrush(Color.FromArgb(170, 180, 194)))
                        {
                            g.DrawString(_placeholder, Font, b, rect, sf);
                        }
                    }
                }

                using (Pen p = new Pen(Color.FromArgb(196, 203, 214), 1f))
                {
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
            }
        }

        private readonly MonitorMapPanel _mapPanel;
        private readonly ComboBox _cmbMonitor;
        private readonly GroupBox _grpMonitor;
        private readonly Label _lblMonitorSub;
        private readonly Label _lblNameCaption;
        private readonly TextBox _txtName;
        private readonly TextBox _txtImage;
        private readonly Button _btnBrowse;
        private readonly Button _btnUseCurrent;
        private readonly Button _btnClearImg;
        private readonly ComboBox _cmbTheme;
        private readonly Button _btnClearTheme;
        private readonly Button _btnCopyAll;
        private readonly Button _btnRemoveSaved;
        private readonly Label _lblPresetHint;
        private readonly ImagePreviewPanel _previewPanel;
        private readonly Label _lblPreviewInfo;
        private readonly ListView _list;
        private readonly ComboBox _cmbPos;
        private readonly CheckBox _chkTaskbars;
        private readonly CheckBox _chkStart;
        private readonly Label _lblStatus;
        private readonly Button _btnSaveApply;
        private readonly Button _btnSaveOnly;

        private readonly Dictionary<string, CachedThumb> _thumbCache = new Dictionary<string, CachedThumb>(StringComparer.OrdinalIgnoreCase);
        private readonly Action _onPresetsApplied;
        private readonly Action _onStartupChanged;

        private AppProfile _savedProfile;
        private AppProfile _draftProfile;
        private MonitorInfo[] _connectedMonitors = new MonitorInfo[0];
        private string[] _installedThemes = new string[0];
        private string _selectedChoiceKey;
        private bool _populating;
        private bool _startupSuppress;
        private bool _hasUnsavedChanges;

        public SettingsForm(Icon appIcon, Action onPresetsApplied, Action onStartupChanged)
        {
            _onPresetsApplied = onPresetsApplied;
            _onStartupChanged = onStartupChanged;

            Text = "DisplayMosaic — Per-Monitor Wallpapers & VS Code Themes";
            if (appIcon != null) Icon = appIcon;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);

            float dpiScale = 1f;
            using (Graphics g = CreateGraphics())
            {
                dpiScale = Math.Max(1f, g.DpiX / 96f);
            }

            ClientSize = new Size(960, 760);
            MinimumSize = new Size(800, 680);
            StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 148F * dpiScale)); // Visual monitor map
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                  // Selector toolbar
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                  // Selected monitor card
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));             // Summary list
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                  // Global options
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                  // Bottom action bar

            // 1. Visual Monitor Map
            _mapPanel = new MonitorMapPanel();
            _mapPanel.Dock = DockStyle.Fill;
            _mapPanel.Margin = new Padding(4, 2, 4, 6);
            root.Controls.Add(_mapPanel, 0, 0);

            // 2. Monitor Selector Bar
            TableLayoutPanel selectorBar = new TableLayoutPanel();
            selectorBar.Dock = DockStyle.Fill;
            selectorBar.AutoSize = true;
            selectorBar.ColumnCount = 4;
            selectorBar.RowCount = 1;
            selectorBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            selectorBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            selectorBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            selectorBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label lblSel = MakeLabel("Select monitor:");
            lblSel.Font = new Font(Font, FontStyle.Bold);
            _cmbMonitor = new ComboBox();
            _cmbMonitor.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbMonitor.Margin = new Padding(4);
            _cmbMonitor.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            Button btnIdentify = MakeButton("Identify Screens");
            Button btnRefresh = MakeButton("Refresh");

            selectorBar.Controls.Add(lblSel, 0, 0);
            selectorBar.Controls.Add(_cmbMonitor, 1, 0);
            selectorBar.Controls.Add(btnIdentify, 2, 0);
            selectorBar.Controls.Add(btnRefresh, 3, 0);
            root.Controls.Add(selectorBar, 0, 1);

            // 3. Selected Monitor Settings Card
            _grpMonitor = new GroupBox();
            _grpMonitor.Text = "Monitor Settings";
            _grpMonitor.Dock = DockStyle.Fill;
            _grpMonitor.AutoSize = true;
            _grpMonitor.Padding = new Padding(10, 8, 10, 8);
            _grpMonitor.Margin = new Padding(4, 6, 4, 6);

            TableLayoutPanel cardGrid = new TableLayoutPanel();
            cardGrid.Dock = DockStyle.Fill;
            cardGrid.AutoSize = true;
            cardGrid.ColumnCount = 2;
            cardGrid.RowCount = 1;
            cardGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            cardGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 256F * dpiScale));
            cardGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel fieldsGrid = new TableLayoutPanel();
            fieldsGrid.Dock = DockStyle.Fill;
            fieldsGrid.AutoSize = true;
            fieldsGrid.ColumnCount = 5;
            fieldsGrid.RowCount = 6;
            fieldsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fieldsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            fieldsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fieldsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fieldsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int r = 0; r < 6; r++)
            {
                fieldsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            _lblMonitorSub = MakeLabel("");
            _lblMonitorSub.ForeColor = Color.DimGray;
            fieldsGrid.Controls.Add(_lblMonitorSub, 0, 0);
            fieldsGrid.SetColumnSpan(_lblMonitorSub, 5);

            _lblNameCaption = MakeLabel("Nickname (optional):");
            _txtName = MakeTextBox("");
            fieldsGrid.Controls.Add(_lblNameCaption, 0, 1);
            fieldsGrid.Controls.Add(_txtName, 1, 1);
            fieldsGrid.SetColumnSpan(_txtName, 4);

            _txtImage = MakeTextBox("");
            _txtImage.AllowDrop = true;
            _btnBrowse = MakeButton("Browse...");
            _btnUseCurrent = MakeButton("Use Current");
            _btnClearImg = MakeButton("Clear");
            fieldsGrid.Controls.Add(MakeLabel("Wallpaper image:"), 0, 2);
            fieldsGrid.Controls.Add(_txtImage, 1, 2);
            fieldsGrid.Controls.Add(_btnBrowse, 2, 2);
            fieldsGrid.Controls.Add(_btnUseCurrent, 3, 2);
            fieldsGrid.Controls.Add(_btnClearImg, 4, 2);

            _cmbTheme = new ComboBox();
            _cmbTheme.Margin = new Padding(4);
            _cmbTheme.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _cmbTheme.DropDownStyle = ComboBoxStyle.DropDown;
            _cmbTheme.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _cmbTheme.AutoCompleteSource = AutoCompleteSource.ListItems;
            _btnClearTheme = MakeButton("Clear");
            fieldsGrid.Controls.Add(MakeLabel("VS Code theme:"), 0, 3);
            fieldsGrid.Controls.Add(_cmbTheme, 1, 3);
            fieldsGrid.SetColumnSpan(_cmbTheme, 3);
            fieldsGrid.Controls.Add(_btnClearTheme, 4, 3);

            FlowLayoutPanel quickActions = new FlowLayoutPanel();
            quickActions.AutoSize = true;
            quickActions.FlowDirection = FlowDirection.LeftToRight;
            quickActions.WrapContents = false;
            quickActions.Margin = new Padding(0, 2, 0, 0);
            _btnCopyAll = MakeButton("Copy settings to all monitors");
            _btnRemoveSaved = MakeButton("Remove saved entry");
            quickActions.Controls.Add(_btnCopyAll);
            quickActions.Controls.Add(_btnRemoveSaved);
            fieldsGrid.Controls.Add(quickActions, 1, 4);
            fieldsGrid.SetColumnSpan(quickActions, 4);

            _lblPresetHint = MakeLabel("Blank wallpaper or theme leaves that setting unchanged for this monitor. Tip: drop an image file onto the box or preview.");
            _lblPresetHint.ForeColor = Color.DimGray;
            fieldsGrid.Controls.Add(_lblPresetHint, 0, 5);
            fieldsGrid.SetColumnSpan(_lblPresetHint, 5);

            // Preview side
            TableLayoutPanel previewSide = new TableLayoutPanel();
            previewSide.Dock = DockStyle.Fill;
            previewSide.Padding = new Padding(10, 0, 0, 0);
            previewSide.ColumnCount = 1;
            previewSide.RowCount = 2;
            previewSide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            previewSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 142F * dpiScale));
            previewSide.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _previewPanel = new ImagePreviewPanel();
            _previewPanel.Dock = DockStyle.Fill;
            _previewPanel.Margin = new Padding(4);
            _previewPanel.AllowDrop = true;

            _lblPreviewInfo = new Label();
            _lblPreviewInfo.AutoSize = false;
            _lblPreviewInfo.Height = (int)Math.Round(34 * dpiScale);
            _lblPreviewInfo.Dock = DockStyle.Top;
            _lblPreviewInfo.Margin = new Padding(4, 2, 4, 0);
            _lblPreviewInfo.ForeColor = Color.DimGray;
            _lblPreviewInfo.TextAlign = ContentAlignment.TopCenter;

            previewSide.Controls.Add(_previewPanel, 0, 0);
            previewSide.Controls.Add(_lblPreviewInfo, 0, 1);

            cardGrid.Controls.Add(fieldsGrid, 0, 0);
            cardGrid.Controls.Add(previewSide, 1, 0);
            _grpMonitor.Controls.Add(cardGrid);
            root.Controls.Add(_grpMonitor, 0, 2);

            // 4. Connected Monitors Summary List
            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.MinimumSize = new Size(0, (int)Math.Round(95 * dpiScale));
            _list.Margin = new Padding(4);
            _list.Columns.Add("#", (int)Math.Round(38 * dpiScale));
            _list.Columns.Add("Monitor", (int)Math.Round(200 * dpiScale));
            _list.Columns.Add("Resolution", (int)Math.Round(98 * dpiScale));
            _list.Columns.Add("VS Code Theme", (int)Math.Round(205 * dpiScale));
            _list.Columns.Add("Wallpaper (Configured / Current)", (int)Math.Round(375 * dpiScale));
            root.Controls.Add(_list, 0, 3);

            // 5. Global Options
            TableLayoutPanel globalGrid = new TableLayoutPanel();
            globalGrid.Dock = DockStyle.Fill;
            globalGrid.AutoSize = true;
            globalGrid.Margin = new Padding(0, 4, 0, 2);
            globalGrid.ColumnCount = 4;
            globalGrid.RowCount = 2;
            globalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            globalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            globalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            globalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            globalGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            globalGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _cmbPos = new ComboBox();
            _cmbPos.Margin = new Padding(4);
            _cmbPos.Width = 110;
            _cmbPos.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbPos.Items.AddRange(new object[] { "Fill", "Fit", "Stretch", "Tile", "Center", "Span" });

            _chkTaskbars = new CheckBox();
            _chkTaskbars.Text = "Keep \"Show taskbar apps on: All taskbars\" enabled on display change";
            _chkTaskbars.Margin = new Padding(16, 6, 4, 4);
            _chkTaskbars.AutoSize = true;

            _chkStart = new CheckBox();
            _chkStart.Text = "Run at sign-in and stay minimised in taskbar hidden icons area";
            _chkStart.Margin = new Padding(4, 2, 4, 4);
            _chkStart.AutoSize = true;

            globalGrid.Controls.Add(MakeLabel("Wallpaper fit (all monitors):"), 0, 0);
            globalGrid.Controls.Add(_cmbPos, 1, 0);
            globalGrid.Controls.Add(_chkTaskbars, 2, 0);
            globalGrid.SetColumnSpan(_chkTaskbars, 2);
            globalGrid.Controls.Add(_chkStart, 0, 1);
            globalGrid.SetColumnSpan(_chkStart, 4);
            root.Controls.Add(globalGrid, 0, 4);

            // 6. Bottom Action Bar
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.Margin = new Padding(0, 4, 0, 0);
            bar.ColumnCount = 5;
            bar.RowCount = 1;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblStatus = MakeLabel("Ready");
            _lblStatus.ForeColor = Color.DimGray;

            Button btnRevert = MakeButton("Reset Changes");
            _btnSaveOnly = MakeButton("Save Only");
            _btnSaveApply = MakeButton("Save && Apply");
            _btnSaveApply.BackColor = Color.FromArgb(0, 103, 192);
            _btnSaveApply.ForeColor = Color.White;
            _btnSaveApply.FlatStyle = FlatStyle.Flat;
            _btnSaveApply.FlatAppearance.BorderSize = 0;
            _btnSaveApply.Font = new Font(Font, FontStyle.Bold);
            _btnSaveApply.Padding = new Padding(14, 3, 14, 3);
            Button btnHide = MakeButton("Minimise to Tray");

            bar.Controls.Add(_lblStatus, 0, 0);
            bar.Controls.Add(btnRevert, 1, 0);
            bar.Controls.Add(_btnSaveOnly, 2, 0);
            bar.Controls.Add(_btnSaveApply, 3, 0);
            bar.Controls.Add(btnHide, 4, 0);
            root.Controls.Add(bar, 0, 5);

            Controls.Add(root);

            // Wire events
            _mapPanel.MonitorClicked += delegate(string monitorId)
            {
                SelectMonitorByKey(monitorId);
            };

            _cmbMonitor.SelectedIndexChanged += delegate
            {
                if (_populating) return;
                MonitorChoice choice = _cmbMonitor.SelectedItem as MonitorChoice;
                if (choice != null)
                {
                    _selectedChoiceKey = choice.Key;
                    PopulateEditorForChoice(choice);
                    SyncSelectionsInViews();
                }
            };

            _list.SelectedIndexChanged += delegate
            {
                if (_populating || _list.SelectedItems.Count == 0) return;
                string key = _list.SelectedItems[0].Tag as string;
                if (!string.IsNullOrEmpty(key))
                {
                    SelectMonitorByKey(key);
                }
            };

            _txtName.TextChanged += delegate { OnCurrentEditorChanged(); };
            _txtImage.TextChanged += delegate { OnCurrentEditorChanged(); };
            _cmbTheme.TextChanged += delegate { OnCurrentEditorChanged(); };
            _cmbPos.SelectedIndexChanged += delegate
            {
                if (_populating) return;
                _draftProfile.Position = Convert.ToString(_cmbPos.SelectedItem) ?? "Fill";
                MarkDirty();
            };
            _chkTaskbars.CheckedChanged += delegate
            {
                if (_populating) return;
                _draftProfile.UpdateTaskbars = _chkTaskbars.Checked;
                MarkDirty();
            };

            _btnBrowse.Click += delegate { BrowseImage(); };
            _btnUseCurrent.Click += delegate { UseCurrentWallpaper(); };
            _btnClearImg.Click += delegate { _txtImage.Text = ""; };
            _btnClearTheme.Click += delegate { _cmbTheme.Text = ""; };
            _btnCopyAll.Click += delegate { CopyCurrentToAllMonitors(); };
            _btnRemoveSaved.Click += delegate { RemoveSelectedDisconnected(); };

            WireImageDrop(_txtImage);
            WireImageDrop(_previewPanel);

            btnIdentify.Click += delegate { MonitorService.IdentifyMonitors(); };
            btnRefresh.Click += delegate { RefreshHardwareAndThemes(true); };
            btnRevert.Click += delegate { LoadFromDisk(true); };
            _btnSaveOnly.Click += delegate { SaveProfile(false); };
            _btnSaveApply.Click += delegate { SaveProfile(true); };
            btnHide.Click += delegate { HideToTray(); };

            _chkStart.CheckedChanged += delegate
            {
                if (_startupSuppress) return;
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
            LoadFromDisk(false);
        }

        public void ShowFromTray()
        {
            SyncStartupCheckbox();
            if (!_hasUnsavedChanges)
            {
                LoadFromDisk(false);
            }
            else
            {
                RefreshConnectedMonitorsOnly();
            }

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
            _startupSuppress = true;
            try
            {
                _chkStart.Checked = StartupManager.IsInstalled();
            }
            finally
            {
                _startupSuppress = false;
            }
        }

        public void RefreshConnectedMonitorsOnly()
        {
            _connectedMonitors = MonitorService.GetMonitors();
            if (_draftProfile == null)
            {
                LoadFromDisk(false);
                return;
            }
            RebuildMonitorDropdownAndViews();
        }

        private void RefreshHardwareAndThemes(bool preserveDraft)
        {
            _installedThemes = ThemeService.GetInstalledThemes();
            PopulateThemeDropdownItems();
            if (preserveDraft && _draftProfile != null)
            {
                _connectedMonitors = MonitorService.GetMonitors();
                RebuildMonitorDropdownAndViews();
                SetStatus(string.Format("Refreshed: {0} monitor(s), {1} VS Code theme(s).", _connectedMonitors.Length, _installedThemes.Length), false);
            }
            else
            {
                LoadFromDisk(false);
            }
        }

        private void LoadFromDisk(bool userInitiated)
        {
            _connectedMonitors = MonitorService.GetMonitors();
            _installedThemes = ThemeService.GetInstalledThemes();
            _savedProfile = ProfileStore.Load(null);
            ProfileStore.EnsureLegacyMigration(_savedProfile, _connectedMonitors);
            _draftProfile = _savedProfile.Clone();
            _hasUnsavedChanges = false;

            PopulateThemeDropdownItems();

            _populating = true;
            try
            {
                _cmbPos.SelectedItem = _draftProfile.Position ?? "Fill";
                if (_cmbPos.SelectedIndex < 0) _cmbPos.SelectedIndex = 0;
                _chkTaskbars.Checked = _draftProfile.UpdateTaskbars;
            }
            finally
            {
                _populating = false;
            }

            RebuildMonitorDropdownAndViews();
            SetStatus(
                userInitiated
                    ? "Reverted to saved configuration."
                    : string.Format("{0} monitor(s) connected • Ready", _connectedMonitors.Length),
                false);
        }

        private void PopulateThemeDropdownItems()
        {
            _populating = true;
            try
            {
                string currentText = _cmbTheme.Text;
                _cmbTheme.Items.Clear();
                if (_installedThemes != null && _installedThemes.Length > 0)
                {
                    _cmbTheme.Items.AddRange(_installedThemes);
                }
                _cmbTheme.Text = currentText;
            }
            finally
            {
                _populating = false;
            }
        }

        private void RebuildMonitorDropdownAndViews()
        {
            List<MonitorChoice> choices = new List<MonitorChoice>();
            HashSet<string> connectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (MonitorInfo m in _connectedMonitors)
            {
                connectedIds.Add(m.Id);
                MonitorPreset mp = ProfileStore.FindMonitorPreset(_draftProfile, m.Id);
                string label = MonitorService.GetDisplayLabel(m, mp);
                string code = MonitorService.GetMonitorCode(m.Id);
                string primaryTag = m.IsPrimary ? ", Primary" : "";
                string display = string.Format(
                    "Monitor #{0} — {1} ({2}) • {3}×{4}{5}",
                    m.Index,
                    label,
                    code,
                    m.Width,
                    m.Height,
                    primaryTag);

                MonitorChoice c = new MonitorChoice();
                c.Connected = m;
                c.Saved = mp;
                c.DisplayText = display;
                choices.Add(c);
            }

            if (_draftProfile.Monitors != null)
            {
                foreach (MonitorPreset mp in _draftProfile.Monitors)
                {
                    if (mp == null || string.IsNullOrEmpty(mp.Id) || connectedIds.Contains(mp.Id)) continue;
                    string namePart = !string.IsNullOrWhiteSpace(mp.Name)
                        ? mp.Name
                        : (!string.IsNullOrWhiteSpace(mp.Model) ? mp.Model : mp.Code);
                    if (string.IsNullOrWhiteSpace(namePart)) namePart = "Saved Monitor";

                    MonitorChoice c = new MonitorChoice();
                    c.IsDisconnected = true;
                    c.Saved = mp;
                    c.DisplayText = string.Format(
                        "[Disconnected] {0} ({1}){2}",
                        namePart,
                        !string.IsNullOrEmpty(mp.Code) ? mp.Code : MonitorService.GetMonitorCode(mp.Id),
                        !string.IsNullOrEmpty(mp.LastResolution) ? " • " + mp.LastResolution : "");
                    choices.Add(c);
                }
            }

            MonitorChoice defChoice = new MonitorChoice();
            defChoice.IsDefault = true;
            defChoice.DisplayText = "Default preset (fallback for new / unconfigured monitors)";
            choices.Add(defChoice);

            _populating = true;
            try
            {
                _cmbMonitor.Items.Clear();
                foreach (MonitorChoice c in choices)
                {
                    _cmbMonitor.Items.Add(c);
                }

                int selectIdx = -1;
                if (!string.IsNullOrEmpty(_selectedChoiceKey))
                {
                    for (int i = 0; i < choices.Count; i++)
                    {
                        if (string.Equals(choices[i].Key, _selectedChoiceKey, StringComparison.OrdinalIgnoreCase))
                        {
                            selectIdx = i;
                            break;
                        }
                    }
                }

                if (selectIdx < 0 && choices.Count > 0)
                {
                    selectIdx = 0;
                }

                if (selectIdx >= 0)
                {
                    _cmbMonitor.SelectedIndex = selectIdx;
                    _selectedChoiceKey = choices[selectIdx].Key;
                }
            }
            finally
            {
                _populating = false;
            }

            MonitorChoice selected = _cmbMonitor.SelectedItem as MonitorChoice;
            if (selected != null)
            {
                PopulateEditorForChoice(selected);
            }

            RefreshMapAndList();
        }

        private void SelectMonitorByKey(string key)
        {
            for (int i = 0; i < _cmbMonitor.Items.Count; i++)
            {
                MonitorChoice c = _cmbMonitor.Items[i] as MonitorChoice;
                if (c != null && string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (_cmbMonitor.SelectedIndex != i)
                    {
                        _cmbMonitor.SelectedIndex = i;
                    }
                    else
                    {
                        _selectedChoiceKey = c.Key;
                        PopulateEditorForChoice(c);
                        SyncSelectionsInViews();
                    }
                    return;
                }
            }
        }

        private void PopulateEditorForChoice(MonitorChoice choice)
        {
            _populating = true;
            try
            {
                if (choice == null || choice.IsDefault)
                {
                    _grpMonitor.Text = "Default Preset (Fallback for New / Unconfigured Monitors)";
                    _lblMonitorSub.Text = "Applied automatically only to newly connected monitors that do not have their own per-monitor settings.";
                    _lblNameCaption.Visible = false;
                    _txtName.Visible = false;
                    _txtName.Text = "";
                    _txtImage.Text = (_draftProfile.Default != null ? _draftProfile.Default.Image : "") ?? "";
                    _cmbTheme.Text = (_draftProfile.Default != null ? _draftProfile.Default.Theme : "") ?? "";
                    _btnUseCurrent.Enabled = false;
                    _btnRemoveSaved.Visible = false;
                    _btnCopyAll.Visible = _connectedMonitors.Length > 0;
                }
                else if (choice.Connected != null)
                {
                    MonitorInfo m = choice.Connected;
                    MonitorPreset mp = ProfileStore.FindMonitorPreset(_draftProfile, m.Id);
                    string code = MonitorService.GetMonitorCode(m.Id);
                    string modelPart = !string.IsNullOrWhiteSpace(m.Model) ? m.Model : code;

                    _grpMonitor.Text = string.Format("Monitor #{0} — {1}", m.Index, MonitorService.GetDisplayLabel(m, mp));
                    _lblMonitorSub.Text = string.Format(
                        "Hardware: {0} ({1})  •  {2} × {3}  •  Position: ({4}, {5}){6}",
                        modelPart,
                        code,
                        m.Width,
                        m.Height,
                        m.Left,
                        m.Top,
                        m.IsPrimary ? "  •  Primary display" : "");

                    _lblNameCaption.Visible = true;
                    _txtName.Visible = true;
                    _txtName.Text = mp != null ? (mp.Name ?? "") : "";
                    _txtImage.Text = mp != null ? (mp.Image ?? "") : "";
                    _cmbTheme.Text = mp != null ? (mp.Theme ?? "") : "";
                    _btnUseCurrent.Enabled = !string.IsNullOrEmpty(m.Wallpaper);
                    _btnRemoveSaved.Visible = false;
                    _btnCopyAll.Visible = _connectedMonitors.Length > 1;
                }
                else if (choice.Saved != null)
                {
                    MonitorPreset mp = choice.Saved;
                    _grpMonitor.Text = string.Format("Saved Disconnected Monitor — {0}", !string.IsNullOrWhiteSpace(mp.Name) ? mp.Name : mp.Model);
                    _lblMonitorSub.Text = string.Format(
                        "Not currently connected  •  Code: {0}  •  Model: {1}{2}",
                        mp.Code,
                        mp.Model,
                        !string.IsNullOrEmpty(mp.LastResolution) ? "  •  " + mp.LastResolution : "");

                    _lblNameCaption.Visible = true;
                    _txtName.Visible = true;
                    _txtName.Text = mp.Name ?? "";
                    _txtImage.Text = mp.Image ?? "";
                    _cmbTheme.Text = mp.Theme ?? "";
                    _btnUseCurrent.Enabled = false;
                    _btnRemoveSaved.Visible = true;
                    _btnCopyAll.Visible = false;
                }

                UpdateHintForChoice(choice);
                UpdatePreviewPanel(choice);
            }
            finally
            {
                _populating = false;
            }
        }

        private void UpdateHintForChoice(MonitorChoice choice)
        {
            if (choice != null && !choice.IsDefault && _draftProfile.Default != null && !_draftProfile.Default.IsEmpty())
            {
                MonitorPreset mp = ProfileStore.FindMonitorPreset(_draftProfile, choice.Key);
                if (mp == null || (string.IsNullOrWhiteSpace(mp.Image) && string.IsNullOrWhiteSpace(mp.Theme)))
                {
                    _lblPresetHint.Text = "Note: If left blank, this monitor will inherit the Default preset until you choose a wallpaper or theme here.";
                    return;
                }
            }

            _lblPresetHint.Text = "Leave Wallpaper or VS Code theme blank to keep it unchanged for this monitor. Tip: drop an image file onto the path or preview.";
        }

        private void OnCurrentEditorChanged()
        {
            if (_populating) return;
            MonitorChoice choice = _cmbMonitor.SelectedItem as MonitorChoice;
            if (choice == null) return;

            string img = _txtImage.Text.Trim('"', ' ');
            string theme = _cmbTheme.Text.Trim();
            string name = _txtName.Text.Trim();

            if (choice.IsDefault)
            {
                _draftProfile.Default = new PresetConfig { Image = img, Theme = theme };
            }
            else if (choice.Connected != null)
            {
                MonitorPreset mp = ProfileStore.FindMonitorPreset(_draftProfile, choice.Connected.Id);
                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(img) && string.IsNullOrEmpty(theme))
                {
                    if (mp != null)
                    {
                        _draftProfile.Monitors.Remove(mp);
                    }
                }
                else
                {
                    mp = ProfileStore.GetOrCreateMonitorPreset(_draftProfile, choice.Connected);
                    mp.Name = name;
                    mp.Image = img;
                    mp.Theme = theme;
                }

                // Update dropdown display text if nickname changed
                MonitorPreset updated = ProfileStore.FindMonitorPreset(_draftProfile, choice.Connected.Id);
                string newLabel = MonitorService.GetDisplayLabel(choice.Connected, updated);
                string newText = string.Format(
                    "Monitor #{0} — {1} ({2}) • {3}×{4}{5}",
                    choice.Connected.Index,
                    newLabel,
                    MonitorService.GetMonitorCode(choice.Connected.Id),
                    choice.Connected.Width,
                    choice.Connected.Height,
                    choice.Connected.IsPrimary ? ", Primary" : "");

                if (!string.Equals(choice.DisplayText, newText, StringComparison.Ordinal))
                {
                    choice.DisplayText = newText;
                    _grpMonitor.Text = string.Format("Monitor #{0} — {1}", choice.Connected.Index, newLabel);
                    _populating = true;
                    try
                    {
                        int idx = _cmbMonitor.SelectedIndex;
                        if (idx >= 0) _cmbMonitor.Items[idx] = choice;
                    }
                    finally
                    {
                        _populating = false;
                    }
                }
            }
            else if (choice.Saved != null)
            {
                choice.Saved.Name = name;
                choice.Saved.Image = img;
                choice.Saved.Theme = theme;
            }

            UpdateHintForChoice(choice);
            UpdatePreviewPanel(choice);
            RefreshMapAndList();
            MarkDirty();
        }

        private void UpdatePreviewPanel(MonitorChoice choice)
        {
            string imgPath = _txtImage.Text.Trim('"', ' ');
            if (string.IsNullOrEmpty(imgPath))
            {
                if (choice != null && choice.Connected != null && !string.IsNullOrEmpty(choice.Connected.Wallpaper))
                {
                    CachedThumb currentThumb = GetThumbnail(choice.Connected.Wallpaper);
                    if (currentThumb != null && currentThumb.Thumbnail != null)
                    {
                        _previewPanel.SetPreview(currentThumb.Thumbnail, "");
                        _lblPreviewInfo.ForeColor = Color.DimGray;
                        _lblPreviewInfo.Text = string.Format(
                            "No override set — showing current Windows wallpaper\r\n{0}",
                            Path.GetFileName(choice.Connected.Wallpaper));
                        return;
                    }
                }

                _previewPanel.SetPreview(null, "No wallpaper override\n(keeps current wallpaper)");
                _lblPreviewInfo.ForeColor = Color.DimGray;
                _lblPreviewInfo.Text = "Leave blank to keep current wallpaper";
                return;
            }

            if (!File.Exists(imgPath))
            {
                _previewPanel.SetPreview(null, "Image file not found");
                _lblPreviewInfo.ForeColor = Color.Firebrick;
                _lblPreviewInfo.Text = "File not found: " + Path.GetFileName(imgPath);
                return;
            }

            CachedThumb thumb = GetThumbnail(imgPath);
            if (thumb == null || thumb.Thumbnail == null)
            {
                _previewPanel.SetPreview(null, "Cannot preview image");
                _lblPreviewInfo.ForeColor = Color.Firebrick;
                _lblPreviewInfo.Text = "Unsupported or unreadable image";
                return;
            }

            _previewPanel.SetPreview(thumb.Thumbnail, "");
            _lblPreviewInfo.ForeColor = Color.DimGray;
            _lblPreviewInfo.Text = string.Format(
                "{0} × {1} px  •  {2}",
                thumb.OriginalWidth,
                thumb.OriginalHeight,
                Path.GetFileName(imgPath));
        }

        private void RefreshMapAndList()
        {
            // Update Visual Map
            List<MonitorMapItem> mapItems = new List<MonitorMapItem>();
            foreach (MonitorInfo m in _connectedMonitors)
            {
                MonitorPreset mp = ProfileStore.FindMonitorPreset(_draftProfile, m.Id);
                PresetConfig resolved = ProfileStore.ResolvePreset(_draftProfile, m);
                bool isOverride = !string.IsNullOrEmpty(resolved.Image);
                string previewPath = isOverride ? resolved.Image : (m.Wallpaper ?? "");
                CachedThumb thumb = !string.IsNullOrEmpty(previewPath) ? GetThumbnail(previewPath) : null;

                MonitorMapItem item = new MonitorMapItem();
                item.Monitor = m;
                item.Label = MonitorService.GetDisplayLabel(m, mp);
                item.Resolution = string.Format("{0}×{1}", m.Width, m.Height);
                item.Theme = resolved.Theme ?? "";
                item.Thumbnail = thumb != null ? thumb.Thumbnail : null;
                item.IsOverride = isOverride;
                mapItems.Add(item);
            }
            _mapPanel.SetData(mapItems, _selectedChoiceKey);

            // Update ListView
            _populating = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (MonitorInfo m in _connectedMonitors)
                {
                    MonitorPreset mp = ProfileStore.FindMonitorPreset(_draftProfile, m.Id);
                    PresetConfig resolved = ProfileStore.ResolvePreset(_draftProfile, m);
                    string label = MonitorService.GetDisplayLabel(m, mp);
                    if (m.IsPrimary) label += " [Primary]";

                    string wpText;
                    if (!string.IsNullOrEmpty(resolved.Image))
                    {
                        wpText = resolved.Image;
                    }
                    else if (!string.IsNullOrEmpty(m.Wallpaper))
                    {
                        wpText = "(Unchanged) " + m.Wallpaper;
                    }
                    else
                    {
                        wpText = "(Unchanged)";
                    }

                    string themeText = !string.IsNullOrEmpty(resolved.Theme) ? resolved.Theme : "(Unchanged)";

                    ListViewItem lvi = new ListViewItem(m.Index.ToString());
                    lvi.Tag = m.Id;
                    lvi.SubItems.Add(label);
                    lvi.SubItems.Add(string.Format("{0}×{1}", m.Width, m.Height));
                    lvi.SubItems.Add(themeText);
                    lvi.SubItems.Add(wpText);
                    if (string.Equals(m.Id, _selectedChoiceKey, StringComparison.OrdinalIgnoreCase))
                    {
                        lvi.Selected = true;
                    }
                    _list.Items.Add(lvi);
                }
            }
            finally
            {
                _list.EndUpdate();
                _populating = false;
            }
        }

        private void SyncSelectionsInViews()
        {
            _mapPanel.SetSelectedId(_selectedChoiceKey);

            _populating = true;
            try
            {
                foreach (ListViewItem lvi in _list.Items)
                {
                    string key = lvi.Tag as string;
                    lvi.Selected = string.Equals(key, _selectedChoiceKey, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                _populating = false;
            }
        }

        private void BrowseImage()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.jfif|All files|*.*";
                string current = _txtImage.Text.Trim('"', ' ');
                if (!string.IsNullOrEmpty(current))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(current);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            dlg.InitialDirectory = dir;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _txtImage.Text = dlg.FileName;
                }
            }
        }

        private void UseCurrentWallpaper()
        {
            MonitorChoice choice = _cmbMonitor.SelectedItem as MonitorChoice;
            if (choice != null && choice.Connected != null && !string.IsNullOrEmpty(choice.Connected.Wallpaper))
            {
                _txtImage.Text = choice.Connected.Wallpaper;
            }
        }

        private void CopyCurrentToAllMonitors()
        {
            if (_connectedMonitors.Length == 0) return;

            string img = _txtImage.Text.Trim('"', ' ');
            string theme = _cmbTheme.Text.Trim();

            foreach (MonitorInfo m in _connectedMonitors)
            {
                MonitorPreset mp = ProfileStore.FindMonitorPreset(_draftProfile, m.Id);
                if (string.IsNullOrEmpty(img) && string.IsNullOrEmpty(theme))
                {
                    if (mp != null)
                    {
                        mp.Image = "";
                        mp.Theme = "";
                        if (!mp.HasCustomSettings()) _draftProfile.Monitors.Remove(mp);
                    }
                }
                else
                {
                    mp = ProfileStore.GetOrCreateMonitorPreset(_draftProfile, m);
                    mp.Image = img;
                    mp.Theme = theme;
                }
            }

            RefreshMapAndList();
            MarkDirty();
            SetStatus("Copied wallpaper and theme to all connected monitors (click Save & Apply to persist).", true);
        }

        private void RemoveSelectedDisconnected()
        {
            MonitorChoice choice = _cmbMonitor.SelectedItem as MonitorChoice;
            if (choice == null || !choice.IsDisconnected || choice.Saved == null) return;

            _draftProfile.Monitors.Remove(choice.Saved);
            _selectedChoiceKey = _connectedMonitors.Length > 0 ? _connectedMonitors[0].Id : "__DEFAULT__";
            RebuildMonitorDropdownAndViews();
            MarkDirty();
        }

        private void WireImageDrop(Control target)
        {
            target.DragEnter += delegate(object sender, DragEventArgs e)
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
            };
            target.DragDrop += delegate(object sender, DragEventArgs e)
            {
                if (e.Data == null) return;
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0 && !string.IsNullOrEmpty(files[0]))
                {
                    _txtImage.Text = files[0];
                }
            };
        }

        private void SaveProfile(bool applyNow)
        {
            try
            {
                _draftProfile.Position = Convert.ToString(_cmbPos.SelectedItem) ?? "Fill";
                _draftProfile.UpdateTaskbars = _chkTaskbars.Checked;

                if (_draftProfile.Default != null && !string.IsNullOrWhiteSpace(_draftProfile.Default.Image))
                {
                    _draftProfile.Default.Image = ProfileStore.ResolveImagePath(_draftProfile.Default.Image.Trim('"', ' '));
                }

                if (_draftProfile.Monitors != null)
                {
                    foreach (MonitorPreset mp in _draftProfile.Monitors)
                    {
                        if (mp != null && !string.IsNullOrWhiteSpace(mp.Image))
                        {
                            mp.Image = ProfileStore.ResolveImagePath(mp.Image.Trim('"', ' '));
                        }
                    }
                }

                ProfileStore.Save(_draftProfile);
                _savedProfile = _draftProfile.Clone();
                _hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "DisplayMosaic", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!applyNow)
            {
                SetStatus("Saved configuration to " + ProfileStore.ProfilePath, false);
                return;
            }

            _btnSaveApply.Enabled = false;
            _btnSaveOnly.Enabled = false;
            SetStatus("Applying wallpapers and VS Code theme...", false);

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
                                _btnSaveApply.Enabled = true;
                                _btnSaveOnly.Enabled = true;
                                _connectedMonitors = MonitorService.GetMonitors();
                                RebuildMonitorDropdownAndViews();
                                SetStatus(string.Format("Saved and applied at {0:HH:mm:ss}.", DateTime.Now), false);
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

        private void MarkDirty()
        {
            _hasUnsavedChanges = true;
            SetStatus("Unsaved changes — click Save & Apply to apply.", true);
        }

        private void SetStatus(string text, bool highlight)
        {
            _lblStatus.Text = text ?? "";
            _lblStatus.ForeColor = highlight ? Color.FromArgb(0, 103, 192) : Color.DimGray;
        }

        private CachedThumb GetThumbnail(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            string sig;
            try
            {
                sig = path + "|" + File.GetLastWriteTimeUtc(path).Ticks.ToString();
            }
            catch (Exception)
            {
                return null;
            }

            CachedThumb cached;
            if (_thumbCache.TryGetValue(path, out cached) && string.Equals(cached.Signature, sig, StringComparison.Ordinal))
            {
                return cached.Failed ? null : cached;
            }

            if (cached != null)
            {
                cached.Dispose();
                _thumbCache.Remove(path);
            }

            CachedThumb created = new CachedThumb();
            created.Signature = sig;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image src = Image.FromStream(ms))
                {
                    created.OriginalWidth = src.Width;
                    created.OriginalHeight = src.Height;

                    int maxW = 360;
                    int maxH = 220;
                    double ratio = Math.Min((double)maxW / src.Width, (double)maxH / src.Height);
                    if (ratio > 1.0) ratio = 1.0;
                    int w = Math.Max(1, (int)Math.Round(src.Width * ratio));
                    int h = Math.Max(1, (int)Math.Round(src.Height * ratio));

                    Bitmap bmp = new Bitmap(w, h);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, w, h);
                    }
                    created.Thumbnail = bmp;
                }
            }
            catch (Exception)
            {
                created.Failed = true;
            }

            _thumbCache[path] = created;
            return created.Failed ? null : created;
        }

        private static void DrawCoverImage(Graphics g, Image img, RectangleF bounds)
        {
            if (img == null || img.Width <= 0 || img.Height <= 0) return;
            float scale = Math.Max(bounds.Width / img.Width, bounds.Height / img.Height);
            float w = img.Width * scale;
            float h = img.Height * scale;
            float x = bounds.X + (bounds.Width - w) / 2f;
            float y = bounds.Y + (bounds.Height - h) / 2f;
            g.DrawImage(img, x, y, w, h);
        }

        private static void DrawContainImage(Graphics g, Image img, RectangleF bounds)
        {
            if (img == null || img.Width <= 0 || img.Height <= 0) return;
            float scale = Math.Min(bounds.Width / img.Width, bounds.Height / img.Height);
            float w = img.Width * scale;
            float h = img.Height * scale;
            float x = bounds.X + (bounds.Width - w) / 2f;
            float y = bounds.Y + (bounds.Height - h) / 2f;
            g.DrawImage(img, x, y, w, h);
        }

        private void HideToTray()
        {
            Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (CachedThumb ct in _thumbCache.Values)
                {
                    if (ct != null) ct.Dispose();
                }
                _thumbCache.Clear();
            }
            base.Dispose(disposing);
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
    }
}
