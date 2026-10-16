using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ArpgGameLauncher
{
    /// <summary>What the launcher is doing, for <see cref="LauncherView"/> to draw.</summary>
    public enum LauncherPhase
    {
        Checking,

        /// <summary>Not installed: shows where it will go, with a Change… link, and INSTALL.</summary>
        ChooseFolder,
        Downloading,
        Installing,
        Ready,
        Offline,
        Failed,
    }

    /// <summary>
    /// Draws the whole launcher — docs/design/distribution.md -> "The launcher".
    /// Pure System.Drawing (no WinForms) so it can be rendered off-screen for
    /// previews: everything is laid out on a fixed 960×540 logical canvas that
    /// the form scales to the monitor's DPI. The pixel-art key art
    /// (tools/art/launcher.ts) is drawn ×2 with nearest-neighbour filtering;
    /// the interface on top is smooth and modern (glass bar, gradient button,
    /// slim progress bar), with drifting embers and a breathing rift for life.
    /// </summary>
    public sealed class LauncherView : IDisposable
    {
        public const int Width = 960;
        public const int Height = 540;

        public static readonly Rectangle PlayBounds = new Rectangle(700, 444, 224, 64);
        public static readonly Rectangle CloseBounds = new Rectangle(Width - 46, 0, 46, 34);
        public static readonly Rectangle MinimizeBounds = new Rectangle(Width - 92, 0, 46, 34);
        public static readonly Rectangle FolderBounds = new Rectangle(Width - 214, 0, 122, 34);

        /// <summary>"Change…" next to the install path (only while choosing a folder).</summary>
        public static readonly Rectangle ChangeBounds = new Rectangle(560, 466, 98, 26);

        private static readonly Color Gold = Color.FromArgb(255, 207, 106);
        private static readonly Color Ember = Color.FromArgb(240, 128, 40);
        private static readonly Color Ink = Color.FromArgb(236, 232, 245);
        private static readonly Color Dim = Color.FromArgb(160, 152, 182);
        private static readonly Color Danger = Color.FromArgb(255, 120, 100);

        private readonly Image _art;
        private readonly Random _rng = new Random(3);
        private readonly List<Mote> _motes = new List<Mote>();
        private readonly FontFamily _family;
        private readonly FontFamily _display;
        private float _time;

        private struct Mote
        {
            public float X, Y, Speed, Drift, Size, Phase;
            public bool Hot;
        }

        public LauncherView(Image art)
        {
            _art = art;
            _family = PickFamily("Segoe UI Variable Text", "Segoe UI", "DejaVu Sans");
            _display = PickFamily("Segoe UI Variable Display", "Segoe UI Black", "Segoe UI", "DejaVu Sans");
            for (int i = 0; i < 46; i++)
            {
                _motes.Add(NewMote(true));
            }
        }

        // --- state the form sets -------------------------------------------------------

        public LauncherPhase Phase { get; set; } = LauncherPhase.Checking;
        public string Status { get; set; } = "Checking for updates…";
        public string Detail { get; set; } = "";
        public double Progress { get; set; } = -1;
        public bool CanPlay { get; set; }
        public bool HoverPlay { get; set; }
        public bool HoverClose { get; set; }
        public bool HoverMinimize { get; set; }
        public bool HoverFolder { get; set; }
        public bool HoverChange { get; set; }
        public bool PressPlay { get; set; }

        /// <summary>What the big button does right now.</summary>
        public string PlayLabel => Phase == LauncherPhase.ChooseFolder ? "INSTALL"
            : Phase == LauncherPhase.Failed && !CanPlay ? "RETRY"
            : CanPlay ? "PLAY"
            : Phase == LauncherPhase.Downloading || Phase == LauncherPhase.Installing ? "UPDATING"
            : "PLEASE WAIT";

        public bool PlayClickable => CanPlay || Phase == LauncherPhase.Failed || Phase == LauncherPhase.ChooseFolder;

        /// <summary>The Change… link is live while choosing, and after a failure on a fresh install.</summary>
        public bool ChangeVisible { get; set; }

        /// <summary>Shows the status line in red without leaving the current phase (a folder that can't be used).</summary>
        public bool StatusIsError { get; set; }

        // --- animation -----------------------------------------------------------------

        public void Tick(float dt)
        {
            _time += dt;
            for (int i = 0; i < _motes.Count; i++)
            {
                Mote m = _motes[i];
                m.Y -= m.Speed * dt;
                m.X += (float)Math.Sin(_time * 0.9f + m.Phase) * m.Drift * dt;
                _motes[i] = m.Y < -10 ? NewMote(false) : m;
            }
        }

        private Mote NewMote(bool anywhere)
        {
            bool hot = _rng.NextDouble() < 0.6;
            return new Mote
            {
                Hot = hot,
                X = hot ? 500 + (float)_rng.NextDouble() * 460 : (float)_rng.NextDouble() * 460,
                Y = anywhere ? (float)_rng.NextDouble() * Height : Height + 10,
                Speed = 14 + (float)_rng.NextDouble() * 30,
                Drift = 6 + (float)_rng.NextDouble() * 14,
                Size = _rng.NextDouble() < 0.25 ? 3 : 2,
                Phase = (float)_rng.NextDouble() * 6.28f,
            };
        }

        // --- painting ------------------------------------------------------------------

        public void Paint(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_art, new Rectangle(0, 0, Width, Height));

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            PaintLife(g);
            PaintTopBar(g);
            PaintBottom(g);
        }

        private void PaintLife(Graphics g)
        {
            // The clash flares and the Arcanist's charging orb throbs (positions
            // match tools/art/launcher.ts, ×2).
            float flare = 0.5f + 0.5f * (float)Math.Sin(_time * 3.1f);
            float charge = 0.5f + 0.5f * (float)Math.Sin(_time * 1.7f + 1f);
            Pulse(g, new PointF(532, 232), 70, Color.FromArgb(255, 246, 220), (int)(28 + 50 * flare));
            Pulse(g, new PointF(196, 154), 56, Color.FromArgb(122, 232, 255), (int)(30 + 60 * charge));
            Pulse(g, new PointF(300, 246), 34, Color.FromArgb(122, 232, 255), (int)(20 + 40 * charge));
            Pulse(g, new PointF(820, 228), 90, Color.FromArgb(255, 120, 50), (int)(18 + 30 * charge));

            // Embers and motes, pixel-sized like the art.
            foreach (Mote m in _motes)
            {
                float twinkle = 0.55f + 0.45f * (float)Math.Sin(_time * 4f + m.Phase * 3f);
                Color c = m.Hot ? Color.FromArgb((int)(200 * twinkle), 255, 150 + (int)(60 * twinkle), 60) : Color.FromArgb((int)(170 * twinkle), 255, 236, 170);
                using (var b = new SolidBrush(c))
                {
                    g.FillRectangle(b, (float)Math.Round(m.X / 2) * 2, (float)Math.Round(m.Y / 2) * 2, m.Size, m.Size);
                }
            }
        }

        /// <summary>A soft additive-looking glow: a radial gradient ellipse.</summary>
        private static void Pulse(Graphics g, PointF c, float r, Color color, int alpha)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
                using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color), SurroundColors = new[] { Color.FromArgb(0, color) } })
                {
                    g.FillPath(b, path);
                }
            }
        }

        private void PaintTopBar(Graphics g)
        {
            using (var shade = new LinearGradientBrush(new Rectangle(0, 0, Width, 72), Color.FromArgb(170, 6, 4, 12), Color.FromArgb(0, 6, 4, 12), LinearGradientMode.Vertical))
            {
                g.FillRectangle(shade, 0, 0, Width, 72);
            }

            using (var f = new Font(_family, 9.5f, FontStyle.Bold))
            using (var b = new SolidBrush(Color.FromArgb(200, Ink)))
            {
                g.DrawString("ARPG GAME  ·  LAUNCHER", f, b, 18, 10);
            }

            Hover(g, FolderBounds, HoverFolder);
            using (var f = new Font(_family, 9f))
            using (var b = new SolidBrush(HoverFolder ? Ink : Dim))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("Game folder", f, b, FolderBounds, sf);
            }

            Hover(g, MinimizeBounds, HoverMinimize);
            using (var p = new Pen(HoverMinimize ? Ink : Dim, 1.4f))
            {
                g.DrawLine(p, MinimizeBounds.X + 18, 17, MinimizeBounds.X + 28, 17);
            }

            if (HoverClose)
            {
                using (var b = new SolidBrush(Color.FromArgb(220, 196, 43, 28)))
                {
                    g.FillRectangle(b, CloseBounds);
                }
            }

            using (var p = new Pen(HoverClose ? Color.White : Dim, 1.4f))
            {
                g.DrawLine(p, CloseBounds.X + 18, 12, CloseBounds.X + 28, 22);
                g.DrawLine(p, CloseBounds.X + 28, 12, CloseBounds.X + 18, 22);
            }
        }

        private static void Hover(Graphics g, Rectangle r, bool on)
        {
            if (!on)
            {
                return;
            }

            using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            {
                g.FillRectangle(b, r);
            }
        }

        private void PaintBottom(Graphics g)
        {
            // Glass: a long fade into a dark, slightly purple bar.
            var bar = new Rectangle(0, 330, Width, Height - 330);
            using (var shade = new LinearGradientBrush(bar, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
            {
                shade.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, 8, 6, 14), Color.FromArgb(150, 8, 6, 14), Color.FromArgb(236, 9, 7, 16), Color.FromArgb(246, 9, 7, 16) },
                    Positions = new[] { 0f, 0.42f, 0.7f, 1f },
                };
                g.FillRectangle(shade, bar);
            }

            using (var line = new LinearGradientBrush(new Rectangle(0, 430, Width, 1), Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
            {
                line.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, Gold), Color.FromArgb(90, Gold), Color.FromArgb(0, Gold) },
                    Positions = new[] { 0f, 0.35f, 1f },
                };
                g.FillRectangle(line, 0, 430, Width, 1);
            }

            PaintTitle(g, 36, 426);
            PaintProgress(g, new Rectangle(38, 498, 620, 6));
            if (ChangeVisible)
            {
                PaintChange(g);
            }

            PaintPlay(g);
        }

        /// <summary>The "Change…" pill beside the install path.</summary>
        private void PaintChange(Graphics g)
        {
            using (GraphicsPath pill = Rounded(ChangeBounds, 6))
            using (var fill = new SolidBrush(Color.FromArgb(HoverChange ? 70 : 38, 255, 255, 255)))
            using (var edge = new Pen(Color.FromArgb(HoverChange ? 200 : 110, Gold), 1f))
            {
                g.FillPath(fill, pill);
                g.DrawPath(edge, pill);
            }

            using (var f = new Font(_family, 9.5f, FontStyle.Bold))
            using (var b = new SolidBrush(HoverChange ? Gold : Ink))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("Change…", f, b, ChangeBounds, sf);
            }
        }

        private void PaintTitle(Graphics g, float x, float y)
        {
            using (var path = new GraphicsPath())
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
            {
                path.AddString("ARPG GAME", _display, (int)FontStyle.Bold, 34f, new PointF(x, y), sf);
                // Soft glow: the outline drawn wide and faint a few times.
                for (int i = 4; i >= 1; i--)
                {
                    using (var glow = new Pen(Color.FromArgb(16, Ember), i * 3f) { LineJoin = LineJoin.Round })
                    {
                        g.DrawPath(glow, path);
                    }
                }

                RectangleF bounds = path.GetBounds();
                using (var fill = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2), Color.FromArgb(255, 246, 220), Gold, LinearGradientMode.Vertical))
                {
                    g.FillPath(fill, path);
                }
            }

            using (var f = new Font(_family, 9.5f))
            using (var b = new SolidBrush(Dim))
            {
                g.DrawString(Detail, f, b, x + 248, y + 14);
            }
        }

        private void PaintProgress(Graphics g, Rectangle track)
        {
            Color statusColor = Phase == LauncherPhase.Failed || StatusIsError ? Danger : Ink;
            using (var f = new Font(_family, 10.5f, FontStyle.Bold))
            using (var b = new SolidBrush(statusColor))
            {
                // Leave room for the Change… pill when it shows (long paths end in an ellipsis).
                float width = ChangeVisible ? ChangeBounds.X - track.X - 8 : track.Width;
                g.DrawString(Status, f, b, new RectangleF(track.X - 2, track.Y - 26, width, 22), new StringFormat { Trimming = Phase == LauncherPhase.ChooseFolder && !StatusIsError ? StringTrimming.EllipsisPath : StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            }

            using (GraphicsPath trackPath = Rounded(track, 3))
            using (var b = new SolidBrush(Color.FromArgb(34, 255, 255, 255)))
            {
                g.FillPath(b, trackPath);
            }

            double p = Progress;
            if (p < 0)
            {
                // Indeterminate: a soft comet sliding along the track.
                float head = (float)((_time * 0.55) % 1.3) - 0.15f;
                var comet = new RectangleF(track.X + track.Width * head, track.Y, track.Width * 0.22f, track.Height);
                comet.Intersect(track);
                if (comet.Width > 1)
                {
                    using (GraphicsPath cometPath = Rounded(Rectangle.Round(comet), 3))
                    using (var b = new LinearGradientBrush(comet, Color.FromArgb(0, Gold), Gold, LinearGradientMode.Horizontal))
                    {
                        g.FillPath(b, cometPath);
                    }
                }
            }
            else if (p > 0)
            {
                var fill = new Rectangle(track.X, track.Y, Math.Max(6, (int)(track.Width * Math.Min(1, p))), track.Height);
                using (GraphicsPath fillPath = Rounded(fill, 3))
                using (var b = new LinearGradientBrush(new Rectangle(track.X, track.Y, track.Width, track.Height), Gold, Ember, LinearGradientMode.Horizontal))
                {
                    g.FillPath(b, fillPath);
                }

                // A shimmer running over the filled part.
                float s = (float)((_time * 0.8) % 1.0);
                var shine = new RectangleF(fill.X + fill.Width * s - 40, fill.Y, 80, fill.Height);
                shine.Intersect(fill);
                if (shine.Width > 1)
                {
                    using (var b = new LinearGradientBrush(new RectangleF(shine.X - 1, shine.Y, shine.Width + 2, shine.Height), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(120, 255, 255, 255), LinearGradientMode.Horizontal))
                    {
                        b.SetBlendTriangularShape(0.5f);
                        g.FillRectangle(b, shine);
                    }
                }

                using (var f = new Font(_family, 9.5f, FontStyle.Bold))
                using (var b = new SolidBrush(Gold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Far };
                    g.DrawString($"{Math.Min(100, p * 100):0}%", f, b, new RectangleF(track.X, track.Y - 24, track.Width, 20), sf);
                }
            }
        }

        private void PaintPlay(Graphics g)
        {
            Rectangle r = PlayBounds;
            if (PressPlay)
            {
                r.Inflate(-2, -2);
            }

            bool live = PlayClickable;
            if (live)
            {
                // Glow behind the button, stronger on hover, breathing gently.
                float breathe = 0.5f + 0.5f * (float)Math.Sin(_time * 2.4f);
                int alpha = (int)((HoverPlay ? 60 : 30) + 20 * breathe);
                for (int i = 3; i >= 1; i--)
                {
                    Rectangle halo = r;
                    halo.Inflate(i * 5, i * 5);
                    using (GraphicsPath haloPath = Rounded(halo, 14 + i * 5))
                    using (var b = new SolidBrush(Color.FromArgb(alpha / i, Ember)))
                    {
                        g.FillPath(b, haloPath);
                    }
                }
            }

            using (GraphicsPath path = Rounded(r, 14))
            {
                Color top = live ? (HoverPlay ? Color.FromArgb(255, 232, 150) : Color.FromArgb(255, 214, 112)) : Color.FromArgb(70, 64, 86);
                Color bottom = live ? (HoverPlay ? Color.FromArgb(250, 150, 52) : Color.FromArgb(234, 124, 34)) : Color.FromArgb(46, 42, 60);
                using (var b = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical))
                {
                    g.FillPath(b, path);
                }

                // A glassy highlight on the upper half.
                var upper = new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height / 2 - 2);
                // The brush is a little taller than what it fills: GDI+ gradients wrap at
                // their exact edge, which drew a bright line across the button.
                var upperBrush = new Rectangle(upper.X, upper.Y - 1, upper.Width, upper.Height + 3);
                using (GraphicsPath upperPath = Rounded(upper, 12))
                using (var b = new LinearGradientBrush(upperBrush, Color.FromArgb(live ? 90 : 30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                {
                    g.FillPath(b, upperPath);
                }

                using (var p = new Pen(live ? Color.FromArgb(200, 255, 240, 200) : Color.FromArgb(70, 255, 255, 255), 1f))
                {
                    g.DrawPath(p, path);
                }
            }

            using (var f = new Font(_display, PlayLabel.Length > 6 ? 13f : 19f, FontStyle.Bold))
            using (var b = new SolidBrush(live ? Color.FromArgb(46, 22, 8) : Dim))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(PlayLabel, f, b, r, sf);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static FontFamily PickFamily(params string[] names)
        {
            using (var installed = new InstalledFontCollection())
            {
                foreach (string name in names)
                {
                    foreach (FontFamily f in installed.Families)
                    {
                        if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return new FontFamily(f.Name);
                        }
                    }
                }
            }

            return FontFamily.GenericSansSerif;
        }

        public void Dispose()
        {
            _family.Dispose();
            _display.Dispose();
        }
    }
}
