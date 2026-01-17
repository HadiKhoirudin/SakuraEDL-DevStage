using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using Sunny.UI;

namespace LoveAlways
{
    public partial class SplashForm : Form
    {
        private Timer _timer;
        private int _angle = 0;
        private int _progress = 0;

        // 徽标进入动画字段
        private int _logoAnimStep = 0;
        private int _logoAnimTotal = 36; // 放慢：更多步骤，动画时长约 36 * 30ms ≈ 1.08s
        private Point _logoStart;
        private Point _logoTarget;
        private Color _logoBaseColor;

        // 状态分段显示控制：0=注册进程,1=启动进程,2=加载中
        private int _statusStage = 0;
        private int _statusTickCounter = 0; // 用于计算每段显示的延时（以 Timer_Tick 次数计）

        public SplashForm()
        {
            InitializeComponent();
            DoubleBuffered = true;
            // 启动时背景略微透明，随后淡入
            this.Opacity = 0.85;

            _timer = new Timer();
            _timer.Interval = 30; // ~33 FPS
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // 初始化徽标进入动画：向下移动并淡入（如有异常则忽略）
            try
            {
                _logoAnimStep = 0;
                _logoAnimTotal = 36;
                _logoBaseColor = uiLedLabel1.ForeColor;
                _logoTarget = uiLedLabel1.Location;
                _logoStart = new Point(_logoTarget.X, _logoTarget.Y - 40);
                uiLedLabel1.Location = _logoStart;
                // 不使用半透明 ForeColor（会产生白边），使用纯色并先隐藏，动画开始时显示
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
            _angle = (_angle + 6) % 360;

            // fade-in（放慢）
            if (this.Opacity < 1.0)
                this.Opacity = Math.Min(1.0, this.Opacity + 0.01);

            // progress goes up slowly
            if (_progress < 100)
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
                    // 状态分段延时显示：每段显示 durationTicks 个定时器周期后切换到下一段
                    int durationTicks = 25; // 25 * 30ms ~= 750ms，可调整

                    // 仅在还没进入最终加载阶段时推进计数
                    if (_statusStage < 2)
                    {
                        _statusTickCounter++;
                        if (_statusTickCounter >= durationTicks)
                        {
                            _statusTickCounter = 0;
                            _statusStage++;
                        }
                    }

                    switch (_statusStage)
                    {
                        case 0:
                            uiLabelStatus.Text = "注册进程...";
                            break;
                        case 1:
                            uiLabelStatus.Text = "启动进程...";
                            break;
                        case 2:
                            uiLabelStatus.Text = "检查环境和版本...";
                            break;
                        case 3:
                            uiLabelStatus.Text = "加载进程...";
                            break;
                        default:
                            uiLabelStatus.Text = $"加载中 {_progress}%";
                            break;
                    }
                }
            }
            catch { }

            // 徽标进入动画（位置与淡入）
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
                    // 动画开始时显示并使用不透明颜色以避免白色锯齿边
                    if (!uiLedLabel1.Visible) uiLedLabel1.Visible = true;
                    uiLedLabel1.ForeColor = _logoBaseColor;
                }
            }
            catch { }

            Invalidate();

            if (_progress >= 100)
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
            // 多开检测：如果发现同名进程超过 1 个，弹出错误提示并退出（不打开程序）
            try
            {
                var procs = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName);
                if (procs.Length > 1)
                {
                    // 设置 SunnyUI 全局字体为 微软雅黑，确保提示窗体使用该字体
                    try
                    {
                        UIStyles.GlobalFont = true;
                        UIStyles.GlobalFontName = "微软雅黑";
                    }
                    catch { }

                    // 使用自定义窗口，控制大小和字体，然后退出当前进程，防止继续打开主窗体
                    try
                    {
                        using (var dlg = new MultiInstanceForm())
                        {
                            dlg.Message = "检测到已打开多个进程，请关闭其他程序";
                            dlg.ShowDialog(this);
                        }
                    }
                    catch { }

                    // 退出当前进程，防止继续打开主窗体
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
