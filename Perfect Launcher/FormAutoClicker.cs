using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Perfect_Launcher
{
    // Configuração do Auto Clicker (construída em código, sem Designer).
    public class FormAutoClicker : Form
    {
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);
        struct POINT { public int X, Y; }

        readonly AutoClicker _ac;

        RadioButton rbLeft, rbRight;
        NumericUpDown numInterval;
        RadioButton rbCursor, rbFixed;
        TextBox txtX, txtY, txtToggle;
        Button btnCapture, btnToggle;
        Label lblStatus;
        Keys _pendingToggle;
        Timer _capture;
        int _captureLeft;

        public FormAutoClicker(AutoClicker ac)
        {
            _ac = ac;
            BuildUi();
            LoadFromEngine();
            _ac.StateChanged += OnStateChanged;
            FormClosed += (s, e) => { _ac.StateChanged -= OnStateChanged; };
        }

        void BuildUi()
        {
            Text = "Auto Clicker";
            ClientSize = new Size(390, 372);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f);
            var ico = Theme.LoadIcon("perfectmirror.ico");
            if (ico != null) Icon = ico;

            Controls.Add(new Label { Text = "AUTO CLICKER", Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(16, 12) });

            Controls.Add(Lbl("Botão do mouse", 18, 52));
            rbLeft = new RadioButton { Text = "Esquerdo", Location = new Point(20, 74), AutoSize = true, ForeColor = Theme.Text, Checked = true };
            rbRight = new RadioButton { Text = "Direito", Location = new Point(150, 74), AutoSize = true, ForeColor = Theme.Text };
            rbLeft.CheckedChanged += (s, e) => _ac.RightButton = rbRight.Checked;
            Controls.Add(rbLeft); Controls.Add(rbRight);

            Controls.Add(Lbl("Intervalo entre cliques (ms)", 18, 110));
            numInterval = new NumericUpDown { Location = new Point(20, 132), Size = new Size(110, 24), Minimum = 1, Maximum = 60000, Value = 100, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            numInterval.ValueChanged += (s, e) => _ac.IntervalMs = (int)numInterval.Value;
            Controls.Add(numInterval);

            Controls.Add(Lbl("Onde clicar", 18, 168));
            rbCursor = new RadioButton { Text = "No cursor", Location = new Point(20, 190), AutoSize = true, ForeColor = Theme.Text, Checked = true };
            rbFixed = new RadioButton { Text = "Posição fixa:", Location = new Point(150, 190), AutoSize = true, ForeColor = Theme.Text };
            rbCursor.CheckedChanged += (s, e) => { _ac.UseFixedPoint = rbFixed.Checked; UpdateFixedEnabled(); };
            Controls.Add(rbCursor); Controls.Add(rbFixed);

            txtX = Txt(20, 216, 60); txtY = Txt(90, 216, 60);
            txtX.TextChanged += (s, e) => { int v; if (int.TryParse(txtX.Text, out v)) _ac.X = v; };
            txtY.TextChanged += (s, e) => { int v; if (int.TryParse(txtY.Text, out v)) _ac.Y = v; };
            Controls.Add(new Label { Text = "X / Y", AutoSize = true, ForeColor = Theme.TextDim, Location = new Point(160, 219) });
            Controls.Add(txtX); Controls.Add(txtY);

            btnCapture = MakeButton("Capturar posição (2s)", new Point(215, 214), 158);
            btnCapture.Click += (s, e) => StartCapture();
            Controls.Add(btnCapture);

            Controls.Add(Lbl("Tecla liga/desliga (clique e aperte)", 18, 256));
            txtToggle = Txt(20, 278, 160); txtToggle.ReadOnly = true; txtToggle.Cursor = Cursors.Hand;
            txtToggle.KeyDown += (s, e) => { _pendingToggle = e.KeyCode; _ac.ToggleKey = e.KeyCode; txtToggle.Text = e.KeyCode.ToString(); e.SuppressKeyPress = true; };
            Controls.Add(txtToggle);

            lblStatus = new Label { Text = "DESLIGADO", Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Theme.TextDim, AutoSize = true, Location = new Point(20, 322) };
            Controls.Add(lblStatus);

            btnToggle = MakeButton("LIGAR", new Point(215, 318), 158);
            btnToggle.BackColor = Theme.Accent; btnToggle.ForeColor = Color.White;
            btnToggle.Click += (s, e) => _ac.SetActive(!_ac.Active);
            Controls.Add(btnToggle);
        }

        Label Lbl(string t, int x, int y) { return new Label { Text = t, AutoSize = true, ForeColor = Theme.TextDim, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Location = new Point(x, y) }; }
        TextBox Txt(int x, int y, int w) { return new TextBox { Location = new Point(x, y), Size = new Size(w, 24), BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle }; }

        Button MakeButton(string text, Point p, int w)
        {
            var b = new Button { Text = text, Location = p, Size = new Size(w, 30), FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Theme.Accent;
            b.FlatAppearance.MouseOverBackColor = Theme.PanelHover;
            return b;
        }

        void LoadFromEngine()
        {
            rbRight.Checked = _ac.RightButton; rbLeft.Checked = !_ac.RightButton;
            numInterval.Value = Math.Min(numInterval.Maximum, Math.Max(numInterval.Minimum, _ac.IntervalMs));
            rbFixed.Checked = _ac.UseFixedPoint; rbCursor.Checked = !_ac.UseFixedPoint;
            txtX.Text = _ac.X.ToString(); txtY.Text = _ac.Y.ToString();
            txtToggle.Text = _ac.ToggleKey.ToString();
            UpdateFixedEnabled();
            OnStateChanged(_ac.Active);
        }

        void UpdateFixedEnabled()
        {
            bool f = rbFixed.Checked;
            txtX.Enabled = f; txtY.Enabled = f; btnCapture.Enabled = f;
        }

        void StartCapture()
        {
            _captureLeft = 2;
            btnCapture.Text = "Mova o mouse... 2";
            if (_capture == null)
            {
                _capture = new Timer { Interval = 1000 };
                _capture.Tick += (s, e) =>
                {
                    _captureLeft--;
                    if (_captureLeft <= 0)
                    {
                        _capture.Stop();
                        POINT p; GetCursorPos(out p);
                        _ac.X = p.X; _ac.Y = p.Y;
                        txtX.Text = p.X.ToString(); txtY.Text = p.Y.ToString();
                        btnCapture.Text = "Capturar posição (2s)";
                    }
                    else btnCapture.Text = "Mova o mouse... " + _captureLeft;
                };
            }
            _capture.Start();
        }

        void OnStateChanged(bool on)
        {
            if (!IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    lblStatus.Text = on ? "LIGADO" : "DESLIGADO";
                    lblStatus.ForeColor = on ? Theme.Accent : Theme.TextDim;
                    btnToggle.Text = on ? "DESLIGAR" : "LIGAR";
                }));
            }
            catch { }
        }
    }
}
