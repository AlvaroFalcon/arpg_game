using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArpgGameLauncher
{
    /// <summary>
    /// The launcher window: checks for a new build, downloads it with a
    /// progress bar, then lets you play. Offline it simply runs what's installed.
    /// </summary>
    public sealed class LauncherForm : Form
    {
        private static readonly Color Background = Color.FromArgb(22, 19, 30);
        private static readonly Color Panel = Color.FromArgb(38, 33, 52);
        private static readonly Color Gold = Color.FromArgb(232, 184, 74);
        private static readonly Color Ink = Color.FromArgb(225, 220, 235);
        private static readonly Color Dim = Color.FromArgb(150, 144, 170);

        private readonly Updater _updater;
        private readonly Label _status;
        private readonly Label _version;
        private readonly ProgressBar _bar;
        private readonly Button _play;
        private readonly Button _retry;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public LauncherForm(Updater updater)
        {
            _updater = updater;
            Text = "ARPG Game Launcher";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 230);
            BackColor = Background;
            ForeColor = Ink;
            Font = new Font("Segoe UI", 10f);

            Controls.Add(new Label
            {
                Text = "ARPG GAME",
                Font = new Font("Segoe UI", 26f, FontStyle.Bold),
                ForeColor = Gold,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(0, 14, 520, 52),
            });
            _status = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(20, 76, 480, 24) };
            _bar = new ProgressBar { Bounds = new Rectangle(40, 108, 440, 18), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
            _version = new Label { AutoSize = false, ForeColor = Dim, Font = new Font("Segoe UI", 8.5f), TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(20, 130, 480, 20) };
            _play = MakeButton("PLAY", new Rectangle(170, 160, 180, 46), true);
            _retry = MakeButton("Retry", new Rectangle(360, 168, 100, 32), false);
            _play.Enabled = false;
            _retry.Visible = false;
            _play.Click += (s, e) => Launch();
            _retry.Click += async (s, e) => await CheckAsync();
            Controls.AddRange(new Control[] { _status, _bar, _version, _play, _retry });
            AcceptButton = _play;

            Shown += async (s, e) => await CheckAsync();
            FormClosing += (s, e) => _cts.Cancel();
        }

        private static Button MakeButton(string text, Rectangle bounds, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Bounds = bounds,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(70, 52, 24) : Panel,
                ForeColor = primary ? Gold : Ink,
                Font = new Font("Segoe UI", primary ? 14f : 10f, primary ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderColor = primary ? Gold : Dim;
            return b;
        }

        private async Task CheckAsync()
        {
            _retry.Visible = false;
            _play.Enabled = false;
            _bar.Style = ProgressBarStyle.Marquee;
            _status.Text = "Checking for updates…";
            string installed = _updater.InstalledVersion;
            _version.Text = installed is null ? "Not installed yet" : $"Installed: {installed}";

            string latest;
            try
            {
                latest = await _updater.FetchLatestVersionAsync(_cts.Token);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Offline(installed, ex);
                return;
            }

            if (_updater.IsInstalled && latest == installed)
            {
                Ready($"Up to date ({installed})");
                return;
            }

            _status.Text = installed is null ? $"Downloading {latest}…" : $"Updating {installed} → {latest}…";
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = 0;
            var progress = new Progress<double>(p =>
            {
                if (p < 0)
                {
                    _bar.Style = ProgressBarStyle.Marquee;
                    return;
                }

                _bar.Value = (int)(Math.Min(1.0, p) * 100);
            });

            try
            {
                await Task.Run(() => _updater.InstallAsync(latest, progress, _cts.Token));
            }
            catch (IOException ex) when (_updater.IsInstalled)
            {
                Failed($"Couldn't replace the game files — is the game still running? ({ex.Message})");
                return;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Failed($"Download failed: {ex.Message}");
                return;
            }

            Ready($"Installed {latest}");
        }

        private void Offline(string installed, Exception ex)
        {
            if (_updater.IsInstalled)
            {
                Ready($"Offline — playing installed {installed ?? "build"}");
            }
            else
            {
                Failed($"Can't reach the download server: {ex.Message}");
            }
        }

        private void Ready(string message)
        {
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = 100;
            _status.Text = message;
            _version.Text = $"Installed: {_updater.InstalledVersion} · {_updater.GameDir}";
            _play.Enabled = true;
            _play.Focus();
        }

        private void Failed(string message)
        {
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = 0;
            _status.Text = message;
            _retry.Visible = true;
            _play.Enabled = _updater.IsInstalled;
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
    }
}
