using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace DisplayMosaic
{
    public class TrayAppContext : ApplicationContext
    {
        private readonly Icon _appIcon;
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _startupMenuItem;
        private readonly Control _uiMarshal;

        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);
        private readonly AutoResetEvent _wakeEvent = new AutoResetEvent(false);
        private readonly EventWaitHandle _showGuiEvent;
        private readonly bool _startupDelay;

        private volatile bool _forceApply;
        private volatile bool _resetThemeTracking;
        private SettingsForm _settingsForm;
        private Thread _watcherThread;
        private Thread _ipcThread;

        public TrayAppContext(bool startMinimized, bool startupDelay, EventWaitHandle showGuiEvent)
        {
            _startupDelay = startupDelay;
            _showGuiEvent = showGuiEvent;

            _uiMarshal = new Control();
            IntPtr unusedHandle = _uiMarshal.Handle;

            try
            {
                _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch (Exception)
            {
                _appIcon = SystemIcons.Application;
            }

            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem openItem = new ToolStripMenuItem("Open Settings");
            openItem.Font = new Font(menu.Font, FontStyle.Bold);
            openItem.Click += delegate { ShowSettings(); };
            menu.Items.Add(openItem);

            ToolStripMenuItem applyItem = new ToolStripMenuItem("Apply Presets Now");
            applyItem.Click += delegate
            {
                _forceApply = true;
                _wakeEvent.Set();
            };
            menu.Items.Add(applyItem);

            ToolStripMenuItem identItem = new ToolStripMenuItem("Identify Monitors");
            identItem.Click += delegate { MonitorService.IdentifyMonitors(); };
            menu.Items.Add(identItem);

            menu.Items.Add(new ToolStripSeparator());

            _startupMenuItem = new ToolStripMenuItem("Run at Startup");
            _startupMenuItem.Checked = StartupManager.IsInstalled();
            _startupMenuItem.Click += delegate { ToggleStartup(); };
            menu.Items.Add(_startupMenuItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { Shutdown(); };
            menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _appIcon;
            _notifyIcon.Text = "DisplayMosaic";
            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowSettings();
                }
            };

            StartWatcher();
            StartIpcListener();

            if (!startMinimized)
            {
                ShowSettings();
            }
        }

        private void StartWatcher()
        {
            _watcherThread = new Thread(WatcherLoop);
            _watcherThread.IsBackground = true;
            _watcherThread.SetApartmentState(ApartmentState.STA);
            _watcherThread.Start();
        }

        private void StartIpcListener()
        {
            if (_showGuiEvent == null) return;

            _ipcThread = new Thread(delegate()
            {
                WaitHandle[] handles = new WaitHandle[] { _stopEvent, _showGuiEvent };
                while (true)
                {
                    int idx = WaitHandle.WaitAny(handles);
                    if (idx == 0) break;
                    try
                    {
                        if (_uiMarshal.IsHandleCreated && !_uiMarshal.IsDisposed)
                        {
                            _uiMarshal.BeginInvoke((MethodInvoker)ShowSettings);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            });
            _ipcThread.IsBackground = true;
            _ipcThread.Start();
        }

        private void WatcherLoop()
        {
            int tick = 0;
            string lastSig = null;
            MonitorInfo[] monitors = new MonitorInfo[0];
            AppProfile p = ProfileStore.Load(null);
            string lastMonitorId = null;
            string candidateMonitorId = null;
            WaitHandle[] waitHandles = new WaitHandle[] { _stopEvent, _wakeEvent };

            while (true)
            {
                try
                {
                    if (_forceApply)
                    {
                        _forceApply = false;
                        _resetThemeTracking = false;
                        p = ProfileStore.Load(null);
                        monitors = MonitorService.GetMonitors();
                        ProfileStore.EnsureLegacyMigration(p, monitors);
                        lastSig = MonitorService.GetMonitorSignature();
                        MonitorService.ApplyAll(null, null);
                        lastMonitorId = null;
                        candidateMonitorId = null;
                        NotifySettingsRefreshed();
                    }
                    else if (_resetThemeTracking)
                    {
                        _resetThemeTracking = false;
                        p = ProfileStore.Load(null);
                        monitors = MonitorService.GetMonitors();
                        ProfileStore.EnsureLegacyMigration(p, monitors);
                        lastSig = MonitorService.GetMonitorSignature();
                        lastMonitorId = null;
                        candidateMonitorId = null;
                    }
                    else if (tick % 2 == 0)
                    {
                        string sig = MonitorService.GetMonitorSignature();
                        if (!string.Equals(sig, lastSig, StringComparison.Ordinal))
                        {
                            int delayMs = (lastSig == null) ? (_startupDelay ? 8000 : 0) : 3000;
                            if (delayMs > 0)
                            {
                                int signaled = WaitHandle.WaitAny(waitHandles, delayMs);
                                if (signaled == 0) break;
                            }

                            p = ProfileStore.Load(null);
                            monitors = MonitorService.GetMonitors();
                            ProfileStore.EnsureLegacyMigration(p, monitors);
                            lastSig = MonitorService.GetMonitorSignature();
                            MonitorService.ApplyAll(null, null);
                            lastMonitorId = null;
                            candidateMonitorId = null;
                            NotifySettingsRefreshed();
                        }
                        else if (tick % 10 == 0)
                        {
                            p = ProfileStore.Load(null);
                            monitors = MonitorService.GetMonitors();
                            ProfileStore.EnsureLegacyMigration(p, monitors);
                        }
                    }

                    MonitorInfo target = MonitorService.GetVsCodeMonitor(monitors, true);
                    if (target != null)
                    {
                        if (string.Equals(target.Id, lastMonitorId, StringComparison.OrdinalIgnoreCase))
                        {
                            candidateMonitorId = null;
                        }
                        else if (string.Equals(target.Id, candidateMonitorId, StringComparison.OrdinalIgnoreCase))
                        {
                            ThemeService.ApplyVsCodeTheme(p, monitors, target, null, null);
                            lastMonitorId = target.Id;
                            candidateMonitorId = null;
                        }
                        else
                        {
                            candidateMonitorId = target.Id;
                        }
                    }
                }
                catch (Exception)
                {
                }

                tick++;
                int waitResult = WaitHandle.WaitAny(waitHandles, 1000);
                if (waitResult == 0) break;
            }
        }

        private void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(
                    _appIcon,
                    delegate
                    {
                        _resetThemeTracking = true;
                        _wakeEvent.Set();
                    },
                    SyncStartupMenu);
            }
            _settingsForm.ShowFromTray();
        }

        private void NotifySettingsRefreshed()
        {
            try
            {
                if (_settingsForm != null && !_settingsForm.IsDisposed && _settingsForm.Visible && _settingsForm.IsHandleCreated)
                {
                    _settingsForm.BeginInvoke((MethodInvoker)delegate
                    {
                        if (_settingsForm != null && !_settingsForm.IsDisposed && _settingsForm.Visible)
                        {
                            _settingsForm.RefreshConnectedMonitorsOnly();
                        }
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        private void SyncStartupMenu()
        {
            _startupMenuItem.Checked = StartupManager.IsInstalled();
        }

        private void ToggleStartup()
        {
            try
            {
                if (StartupManager.IsInstalled())
                {
                    StartupManager.Remove();
                }
                else
                {
                    StartupManager.Install();
                }
                SyncStartupMenu();
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                {
                    _settingsForm.SyncStartupCheckbox();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "DisplayMosaic", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Shutdown()
        {
            _stopEvent.Set();
            if (_watcherThread != null && _watcherThread.IsAlive)
            {
                _watcherThread.Join(1500);
            }
            if (_ipcThread != null && _ipcThread.IsAlive)
            {
                _ipcThread.Join(500);
            }

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Dispose();
            }

            _uiMarshal.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stopEvent.Set();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                _stopEvent.Dispose();
                _wakeEvent.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
