using LoveAlways.Common;
using Sunny.UI;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LoveAlways
{
    public partial class SplashForm : Form
    {
        private Timer _timer;
        private int _angle = 0;
        private int _progress = 0;

        // Logo entry animation fields
        private int _logoAnimStep = 0;
        private int _logoAnimTotal = 36; // Slow: more steps, duration approx. 36 * 30ms ≈ 1.08s
        private Point _logoStart;
        private Point _logoTarget;
        private Color _logoBaseColor;

        // Low performance mode flag
        private bool _lowPerformanceMode;

        public SplashForm()
        {
            InitializeComponent();
            DoubleBuffered = true;

            // Read performance configuration
            _lowPerformanceMode = PerformanceConfig.LowPerformanceMode;

            // Start with slightly transparent background, then fade in (low performance mode stays opaque)
            this.Opacity = _lowPerformanceMode ? 1.0 : 0.85;

            // Start background preload
            PreloadManager.StartPreload();

            _timer = new Timer();
            // Adjust frame rate based on performance configuration
            _timer.Interval = PerformanceConfig.AnimationInterval;
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Low performance mode reduces animation steps
            if (_lowPerformanceMode)
            {
                _logoAnimTotal = 18;
            }

            // Initialize logo entry animation: move down and fade in (ignore if exception)
            try
            {
                _logoAnimStep = 0;
                _logoAnimTotal = 36;
                _logoBaseColor = uiLedLabel1.ForeColor;
                _logoTarget = uiLedLabel1.Location;
                _logoStart = new Point(_logoTarget.X, _logoTarget.Y - 40);
                uiLedLabel1.Location = _logoStart;
                // Do not use translucent ForeColor (causes white edges), use solid and hide first, show when animation starts
                uiLedLabel1.ForeColor = _logoBaseColor;
                uiLedLabel1.Visible = false;
            }
            catch { }

            // allow user to skip splash with click or ESC key
            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) this.Close(); };
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            // Increase angle change speed in low performance mode to compensate for lower frame rate
            _angle = (_angle + (_lowPerformanceMode ? 12 : 6)) % 360;

            // fade-in (low performance mode skips or speeds up)
            if (!_lowPerformanceMode && this.Opacity < 1.0)
                this.Opacity = Math.Min(1.0, this.Opacity + 0.02);

            // Sync preload progress (animation progress not lower than preload progress, but can lead slightly)
            int targetProgress = Math.Max(_progress, PreloadManager.Progress);
            if (_progress < targetProgress)
                _progress = Math.Min(_progress + 2, targetProgress);
            else if (_progress < 100)
                _progress++;

            // reflect on UI controls if available
            try
            {
                if (uiProcessBar1 != null)
                {
                    uiProcessBar1.Value = _progress;
                }
                if (uiLabelStatus != null)
                {
                    // Show actual status of preload module
                    uiLabelStatus.Text = PreloadManager.CurrentStatus;
                }
            }
            catch { }

            // Logo entry animation (position and fade-in)
            try
            {
                if (_logoAnimStep < _logoAnimTotal)
                {
                    _logoAnimStep++;
                    float t = (float)_logoAnimStep / _logoAnimTotal;
                    // ease-out
                    t = 1f - (1f - t) * (1f - t);
                    int newY = _logoStart.Y + (int)((_logoTarget.Y - _logoStart.Y) * t);
                    uiLedLabel1.Location = new System.Drawing.Point(_logoStart.X, newY);
                    // Show when animation starts and use opaque color to avoid white aliased edges
                    if (!uiLedLabel1.Visible) uiLedLabel1.Visible = true;
                    uiLedLabel1.ForeColor = _logoBaseColor;
                }
            }
            catch { }

            Invalidate();

            // Close only when preload is complete and progress reaches 100%
            if (_progress >= 100 && PreloadManager.IsPreloadComplete)
            {
                _timer.Stop();
                // short delay so user sees 100%
                var t = new Timer();
                t.Interval = 300;
                t.Tick += (s, a) => { t.Stop(); t.Dispose(); this.Close(); };
                t.Start();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Multi-instance detection: If more than 1 instance found, show error and exit
            try
            {
                var procs = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName);
                if (procs.Length > 1)
                {
                    // Set SunnyUI global font to Segoe UI to ensure prompt form uses this font
                    try
                    {
                        UIStyles.GlobalFont = true;
                        UIStyles.GlobalFontName = "Segoe UI";
                    }
                    catch { }

                    // Use custom window, control size and font, then exit current process to prevent main form opening
                    try
                    {
                        using (var dlg = new MultiInstanceForm())
                        {
                            dlg.Message = "Multiple instances detected, please close other programs";
                            dlg.ShowDialog(this);
                        }
                    }
                    catch { }

                    // Exit current process to prevent main form opening
                    Environment.Exit(0);
                }
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Rectangle r = ClientRectangle;

            // draw spinner
            int spinnerSize = 64;
            Point spinnerCenter = new Point(r.Width / 2, r.Height * 2 / 3);
            Rectangle spinnerRect = new Rectangle(spinnerCenter.X - spinnerSize / 2, spinnerCenter.Y - spinnerSize / 2, spinnerSize, spinnerSize);

            DrawSpinner(g, spinnerRect, _angle);

            // subtle glossy overlay on spinner
            using (var overlay = new SolidBrush(Color.FromArgb(20, Color.White)))
            {
                g.FillEllipse(overlay, spinnerRect.X + 2, spinnerRect.Y + 2, spinnerRect.Width / 2, spinnerRect.Height / 2);
            }
        }

        private void DrawSpinner(Graphics g, Rectangle r, int angle)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int segments = 12;
            float radius = r.Width / 2f;
            PointF center = new PointF(r.X + radius, r.Y + radius);

            for (int i = 0; i < segments; i++)
            {
                float a = (i * 360f / segments + angle) * (float)Math.PI / 180f;
                float lx = center.X + (radius - 10) * (float)Math.Cos(a);
                float ly = center.Y + (radius - 10) * (float)Math.Sin(a);
                float size = 6f * (1f - (i / (float)segments));

                int alpha = 255 - (i * 200 / segments);
                var col = Color.FromArgb(Math.Max(alpha, 30), 255, 255, 255);
                using (var b = new SolidBrush(col))
                {
                    g.FillEllipse(b, lx - size / 2, ly - size / 2, size, size);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _timer?.Stop();
            _timer?.Dispose();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            this.Close();
        }

        private void uiProcessBar1_ValueChanged(object sender, int value)
        {

        }
    }
}
