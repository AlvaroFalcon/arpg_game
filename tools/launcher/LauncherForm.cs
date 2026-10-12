using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArpgGameLauncher
{
    /// <summary>
    /// The launcher window: borderless and entirely drawn by <see cref="LauncherView"/>
    /// on a 960×540 logical canvas scaled to the monitor's DPI. Checks for a new
    /// build, downloads it with a progress bar, then lets you play; offline it
    /// simply runs what's installed. Drag anywhere to move it.
    /// </summary>
    public sealed class LauncherForm : Form
    {
        private readonly Updater _updater;
        private readonly LauncherView _view;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 33 };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private float _scale = 1f;
        private bool _busy;

        public LauncherForm(Updater updater, Image art, Icon icon)
        {
            _updater = updater;
            _view = new LauncherView(art);
            Text = "ARPG Game Launcher";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(9, 7, 16);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            ApplyScale(DeviceDpi);

            _timer.Tick += (s, e) =>
            {
                _view.Tick((float)_clock.Elapsed.TotalSeconds);
                _clock.Restart();
                Invalidate();
            };
            Shown += async (s, e) =>
            {
                RoundCorners();
                _timer.Start();
                await CheckAsync();
            };
            FormClosing += (s, e) =>
            {
                _cts.Cancel();
                _timer.Stop();
            };
            DpiChanged += (s, e) => ApplyScale(e.DeviceDpiNew);
        }

        private void ApplyScale(int dpi)
        {
            _scale = dpi / 96f;
            ClientSize = new Size((int)Math.Round(LauncherView.Width * _scale), (int)Math.Round(LauncherView.Height * _scale));
        }

        /// <summary>A drop shadow around the borderless window.</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        // --- painting and input --------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.ScaleTransform(_scale, _scale);
            _view.Paint(e.Graphics);
        }

        private Point Logical(Point p) => new Point((int)(p.X / _scale), (int)(p.Y / _scale));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = Logical(e.Location);
            _view.HoverPlay = LauncherView.PlayBounds.Contains(p) && _view.PlayClickable;
            _view.HoverClose = LauncherView.CloseBounds.Contains(p);
            _view.HoverMinimize = LauncherView.MinimizeBounds.Contains(p);
            _view.HoverFolder = LauncherView.FolderBounds.Contains(p);
            Cursor = _view.HoverPlay || _view.HoverClose || _view.HoverMinimize || _view.HoverFolder ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _view.HoverPlay = _view.HoverClose = _view.HoverMinimize = _view.HoverFolder = false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point p = Logical(e.Location);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (LauncherView.PlayBounds.Contains(p) && _view.PlayClickable)
            {
                _view.PressPlay = true;
                return;
            }

            if (LauncherView.CloseBounds.Contains(p) || LauncherView.MinimizeBounds.Contains(p) || LauncherView.FolderBounds.Contains(p))
            {
                return;
            }

            // Anywhere else drags the window, like a title bar.
            ReleaseCapture();
            SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
        }

        protected override async void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Point p = Logical(e.Location);
            bool pressed = _view.PressPlay;
            _view.PressPlay = false;
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (LauncherView.CloseBounds.Contains(p))
            {
                Close();
            }
            else if (LauncherView.MinimizeBounds.Contains(p))
            {
                WindowState = FormWindowState.Minimized;
            }
            else if (LauncherView.FolderBounds.Contains(p))
            {
                Directory.CreateDirectory(_updater.InstallRoot);
                Process.Start(new ProcessStartInfo(_updater.InstallRoot) { UseShellExecute = true });
            }
            else if (pressed && LauncherView.PlayBounds.Contains(p))
            {
                if (_view.CanPlay)
                {
                    Launch();
                }
                else if (_view.Phase == LauncherPhase.Failed)
                {
                    await CheckAsync();
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter && _view.CanPlay)
            {
                Launch();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        }

        // --- update flow ---------------------------------------------------------------

        private async Task CheckAsync()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            try
            {
                await RunUpdateAsync();
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task RunUpdateAsync()
        {
            string installed = _updater.InstalledVersion;
            Set(LauncherPhase.Checking, "Checking for updates…", -1, false);
            _view.Detail = installed is null ? "Not installed yet" : $"Installed {installed}";

            Build latest;
            try
            {
                latest = await _updater.FetchLatestAsync(_cts.Token);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                if (_updater.IsInstalled)
                {
                    Set(LauncherPhase.Offline, "Offline — you can still play the installed version", 1, true);
                }
                else
                {
                    Set(LauncherPhase.Failed, $"Can't reach the download server: {Describe(ex)}", 0, false);
                }

                return;
            }

            if (_updater.IsInstalled && latest.Version == installed)
            {
                Set(LauncherPhase.Ready, "Ready to play — you have the latest version", 1, true);
                _view.Detail = $"Version {installed}";
                return;
            }

            Set(LauncherPhase.Downloading, installed is null ? $"Downloading {latest.Version}…" : $"Updating {installed} → {latest.Version}…", 0, false);
            var progress = new Progress<double>(p =>
            {
                if (p >= 1 && _view.Phase == LauncherPhase.Downloading)
                {
                    Set(LauncherPhase.Installing, "Installing…", -1, false);
                }
                else if (_view.Phase == LauncherPhase.Downloading)
                {
                    _view.Progress = p;
                }
            });

            try
            {
                await Task.Run(() => _updater.InstallAsync(latest, progress, _cts.Token));
            }
            catch (IOException ex) when (_updater.IsInstalled)
            {
                Set(LauncherPhase.Failed, $"Couldn't replace the game files — is the game still running? ({Describe(ex)})", 0, true);
                return;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Set(LauncherPhase.Failed, $"Download failed: {Describe(ex)}", 0, _updater.IsInstalled);
                return;
            }

            Set(LauncherPhase.Ready, $"Installed {latest.Version} — ready to play", 1, true);
            _view.Detail = $"Version {latest.Version}";
        }

        private void Set(LauncherPhase phase, string status, double progress, bool canPlay)
        {
            _view.Phase = phase;
            _view.Status = status;
            _view.Progress = progress;
            _view.CanPlay = canPlay;
            Invalidate();
        }

        /// <summary>The innermost message: .NET Framework wraps the real network error.</summary>
        private static string Describe(Exception ex)
        {
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            return ex.Message;
        }

        private void Launch()
        {
            try
            {
                Process.Start(new ProcessStartInfo(_updater.GameExePath) { WorkingDirectory = _updater.GameDir, UseShellExecute = true });
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Couldn't start the game", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // --- Win32 ---------------------------------------------------------------------

        private void RoundCorners()
        {
            try
            {
                int round = 2; // DWMWCP_ROUND (Windows 11; ignored elsewhere)
                DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
            }
            catch (Exception)
            {
                // Older Windows: square corners.
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _view.Dispose();
                _cts.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
