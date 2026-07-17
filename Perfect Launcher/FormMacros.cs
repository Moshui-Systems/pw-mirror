using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Perfect_Launcher
{
    // Editor completo de macros (construído em código). Lista de macros à esquerda;
    // à direita: nome, tecla, modo, alvo, e um construtor visual de passos.
    public class FormMacros : Form
    {
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);
        struct POINT { public int X, Y; }

        readonly MacroEngine _engine;
        readonly string _savePath;

        ListBox lstMacros, lstSteps;
        TextBox txtName, txtTrigger;
        ComboBox cmbMode, cmbTarget;
        NumericUpDown numRepeat;
        Keys _pendingTrigger;

        // Editor de passo
        ComboBox cmbType, cmbKeyAction, cmbButton;
        TextBox txtStepKey, txtStepText, txtSX, txtSY;
        NumericUpDown numDelay;
        CheckBox chkFixed;
        Button btnCapStep, btnAddStep;
        Label lblMs;
        Keys _stepKey;
        int _editingStep = -1; // índice do passo em edição, ou -1

        Timer _cap; int _capLeft;

        public FormMacros(MacroEngine engine, string savePath)
        {
            _engine = engine;
            _savePath = savePath;
            BuildUi();
            RefreshList();
            if (lstMacros.Items.Count > 0) lstMacros.SelectedIndex = 0;
            RefreshTypeUI();
        }

        void BuildUi()
        {
            Text = "Macros";
            ClientSize = new Size(680, 516);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9f);
            var ico = Theme.LoadIcon("perfectmirror.ico"); if (ico != null) Icon = ico;

            // ---- Lista de macros ----
            Add(Lbl("MACROS", 12, 12, true));
            lstMacros = new ListBox { Location = new Point(12, 34), Size = new Size(180, 360), BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            lstMacros.SelectedIndexChanged += (s, e) => LoadSelected();
            Add(lstMacros);
            var bNew = Btn("Novo", 12, 402, 86); bNew.Click += (s, e) => NewMacro(); Add(bNew);
            var bDel = Btn("Excluir", 106, 402, 86); bDel.Click += (s, e) => DeleteMacro(); Add(bDel);

            // ---- Cabeçalho do macro ----
            int x = 205;
            Add(Lbl("Nome", x, 12));
            txtName = Txt(x, 32, 455); Add(txtName);

            Add(Lbl("Tecla de atalho", x, 64));
            txtTrigger = Txt(x, 84, 150); txtTrigger.ReadOnly = true; txtTrigger.Cursor = Cursors.Hand;
            txtTrigger.KeyDown += (s, e) => { _pendingTrigger = e.KeyCode; txtTrigger.Text = e.KeyCode.ToString(); e.SuppressKeyPress = true; };
            Add(txtTrigger);

            Add(Lbl("Modo", 365, 64));
            cmbMode = Combo(365, 84, 150, new[] { "Uma vez", "Repetir Nx", "Alternar (loop)", "Segurar" });
            cmbMode.SelectedIndexChanged += (s, e) => numRepeat.Enabled = cmbMode.SelectedIndex == 1;
            Add(cmbMode);
            Add(Lbl("Repetir", 525, 64));
            numRepeat = Num(525, 84, 60, 1, 9999, 1); Add(numRepeat);

            Add(Lbl("Enviar para", x, 116));
            cmbTarget = Combo(x, 136, 200, new[] { "Conta atual", "Todas as contas" }); Add(cmbTarget);

            // ---- Passos ----
            Add(Lbl("PASSOS", x, 172, true));
            lstSteps = new ListBox { Location = new Point(x, 194), Size = new Size(370, 150), BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            lstSteps.SelectedIndexChanged += (s, e) => LoadStepToEditor();
            Add(lstSteps);
            var bUp = Btn("↑", 585, 194, 80); bUp.Click += (s, e) => MoveStep(-1); Add(bUp);
            var bDown = Btn("↓", 585, 226, 80); bDown.Click += (s, e) => MoveStep(1); Add(bDown);
            var bRem = Btn("Remover", 585, 258, 80); bRem.Click += (s, e) => RemoveStep(); Add(bRem);

            // ---- Editor de passo ----
            Add(Lbl("Adicionar / editar passo", x, 356));
            cmbType = Combo(x, 378, 120, new[] { "Tecla", "Espera", "Clique", "Mover mouse", "Digitar texto" });
            cmbType.SelectedIndexChanged += (s, e) => RefreshTypeUI();
            Add(cmbType);

            // Tecla
            cmbKeyAction = Combo(335, 378, 110, new[] { "Pressionar", "Segurar", "Soltar" }); Add(cmbKeyAction);
            txtStepKey = Txt(451, 378, 120); txtStepKey.ReadOnly = true; txtStepKey.Cursor = Cursors.Hand;
            txtStepKey.KeyDown += (s, e) => { _stepKey = e.KeyCode; txtStepKey.Text = e.KeyCode.ToString(); e.SuppressKeyPress = true; };
            Add(txtStepKey);
            // Espera
            numDelay = Num(335, 378, 90, 0, 600000, 300); Add(numDelay);
            lblMs = Lbl("ms", 430, 381); Add(lblMs);
            // Clique / Mover
            cmbButton = Combo(335, 378, 90, new[] { "Esquerdo", "Direito", "Meio" }); Add(cmbButton);
            chkFixed = new CheckBox { Text = "Fixo", Location = new Point(432, 380), AutoSize = true, ForeColor = Theme.Text };
            chkFixed.CheckedChanged += (s, e) => UpdateXYEnabled(); Add(chkFixed);
            txtSX = Txt(486, 378, 46); Add(txtSX);
            txtSY = Txt(536, 378, 46); Add(txtSY);
            btnCapStep = Btn("captar", 588, 377, 78); btnCapStep.Click += (s, e) => StartCapture(); Add(btnCapStep);
            // Texto
            txtStepText = Txt(335, 378, 330); Add(txtStepText);

            btnAddStep = Btn("Adicionar passo", x, 414, 150); btnAddStep.Click += (s, e) => AddOrUpdateStep(); Add(btnAddStep);

            var bSave = Btn("SALVAR MACRO", 470, 470, 195); bSave.BackColor = Theme.Accent; bSave.ForeColor = Color.White;
            bSave.Click += (s, e) => SaveSelected(); Add(bSave);
        }

        // ---------- helpers de UI ----------
        void Add(Control c) { Controls.Add(c); }
        Label Lbl(string t, int x, int y, bool head = false) { return new Label { Text = t, AutoSize = true, ForeColor = head ? Theme.Text : Theme.TextDim, Font = new Font("Segoe UI", head ? 9.5f : 8.5f, FontStyle.Bold), Location = new Point(x, y) }; }
        TextBox Txt(int x, int y, int w) { return new TextBox { Location = new Point(x, y), Size = new Size(w, 24), BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle }; }
        NumericUpDown Num(int x, int y, int w, int min, int max, int val) { return new NumericUpDown { Location = new Point(x, y), Size = new Size(w, 24), Minimum = min, Maximum = max, Value = val, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle }; }
        ComboBox Combo(int x, int y, int w, string[] items)
        {
            var c = new ComboBox { Location = new Point(x, y), Size = new Size(w, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
            c.Items.AddRange(items); c.SelectedIndex = 0; return c;
        }
        Button Btn(string t, int x, int y, int w)
        {
            var b = new Button { Text = t, Location = new Point(x, y), Size = new Size(w, 28), FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Theme.Accent; b.FlatAppearance.MouseOverBackColor = Theme.PanelHover; return b;
        }

        void RefreshTypeUI()
        {
            int t = cmbType.SelectedIndex; // 0 Tecla 1 Espera 2 Clique 3 Mover 4 Texto
            cmbKeyAction.Visible = txtStepKey.Visible = t == 0;
            numDelay.Visible = lblMs.Visible = t == 1;
            cmbButton.Visible = t == 2;
            chkFixed.Visible = t == 2;
            bool xy = t == 2 || t == 3;
            txtSX.Visible = txtSY.Visible = btnCapStep.Visible = xy;
            txtStepText.Visible = t == 4;
            UpdateXYEnabled();
        }

        void UpdateXYEnabled()
        {
            bool fixo = cmbType.SelectedIndex == 3 || (cmbType.SelectedIndex == 2 && chkFixed.Checked);
            txtSX.Enabled = txtSY.Enabled = btnCapStep.Enabled = fixo;
        }

        // ---------- macros ----------
        void RefreshList()
        {
            int sel = lstMacros.SelectedIndex;
            lstMacros.Items.Clear();
            foreach (var m in _engine.Macros) lstMacros.Items.Add(string.IsNullOrWhiteSpace(m.Name) ? "(sem nome)" : m.Name);
            if (sel >= 0 && sel < lstMacros.Items.Count) lstMacros.SelectedIndex = sel;
        }

        Macro Current { get { int i = lstMacros.SelectedIndex; return (i >= 0 && i < _engine.Macros.Count) ? _engine.Macros[i] : null; } }

        void LoadSelected()
        {
            var m = Current; if (m == null) return;
            txtName.Text = m.Name;
            _pendingTrigger = m.Trigger; txtTrigger.Text = m.Trigger == Keys.None ? "" : m.Trigger.ToString();
            cmbMode.SelectedIndex = (int)m.Mode;
            numRepeat.Value = Math.Min(numRepeat.Maximum, Math.Max(numRepeat.Minimum, m.RepeatCount));
            numRepeat.Enabled = m.Mode == TriggerMode.Repeat;
            cmbTarget.SelectedIndex = m.AllAccounts ? 1 : 0;
            RefreshSteps();
        }

        void RefreshSteps()
        {
            lstSteps.Items.Clear();
            var m = Current; if (m == null) return;
            for (int i = 0; i < m.Steps.Count; i++)
                lstSteps.Items.Add((i + 1) + ". " + m.Steps[i].Describe());
        }

        void SaveSelected()
        {
            var m = Current; if (m == null) return;
            m.Name = string.IsNullOrWhiteSpace(txtName.Text) ? "(sem nome)" : txtName.Text.Trim();
            m.Trigger = _pendingTrigger;
            m.Mode = (TriggerMode)cmbMode.SelectedIndex;
            m.RepeatCount = (int)numRepeat.Value;
            m.AllAccounts = cmbTarget.SelectedIndex == 1;
            MacroEngine.Save(_savePath, _engine.Macros);
            RefreshList();
        }

        void NewMacro() { _engine.Macros.Add(new Macro()); RefreshList(); lstMacros.SelectedIndex = lstMacros.Items.Count - 1; }
        void DeleteMacro()
        {
            int i = lstMacros.SelectedIndex; if (i < 0) return;
            _engine.Macros.RemoveAt(i); MacroEngine.Save(_savePath, _engine.Macros); RefreshList();
        }

        // ---------- passos ----------
        void AddOrUpdateStep()
        {
            var m = Current; if (m == null) { MessageBox.Show("Crie/selecione um macro primeiro."); return; }
            var st = new MacroStep();
            switch (cmbType.SelectedIndex)
            {
                case 0:
                    st.Type = cmbKeyAction.SelectedIndex == 1 ? StepType.KeyDown : cmbKeyAction.SelectedIndex == 2 ? StepType.KeyUp : StepType.KeyPress;
                    st.Key = _stepKey;
                    if (st.Key == Keys.None) { MessageBox.Show("Clique no campo e aperte a tecla do passo."); return; }
                    break;
                case 1: st.Type = StepType.Delay; st.DelayMs = (int)numDelay.Value; break;
                case 2:
                    st.Type = StepType.MouseClick; st.Button = (MacroButton)cmbButton.SelectedIndex;
                    st.UseCursor = !chkFixed.Checked; ParseXY(st); break;
                case 3: st.Type = StepType.MouseMove; st.UseCursor = false; ParseXY(st); break;
                case 4:
                    st.Type = StepType.Text; st.Text = txtStepText.Text;
                    if (st.Text.Length == 0) { MessageBox.Show("Digite o texto do passo."); return; }
                    break;
            }

            if (_editingStep >= 0 && _editingStep < m.Steps.Count) m.Steps[_editingStep] = st;
            else m.Steps.Add(st);
            _editingStep = -1; btnAddStep.Text = "Adicionar passo";
            MacroEngine.Save(_savePath, _engine.Macros);
            RefreshSteps();
        }

        void ParseXY(MacroStep st) { int v; if (int.TryParse(txtSX.Text, out v)) st.X = v; if (int.TryParse(txtSY.Text, out v)) st.Y = v; }

        void LoadStepToEditor()
        {
            var m = Current; int i = lstSteps.SelectedIndex;
            if (m == null || i < 0 || i >= m.Steps.Count) return;
            var st = m.Steps[i]; _editingStep = i; btnAddStep.Text = "Atualizar passo";
            switch (st.Type)
            {
                case StepType.KeyPress: cmbType.SelectedIndex = 0; cmbKeyAction.SelectedIndex = 0; _stepKey = st.Key; txtStepKey.Text = st.Key.ToString(); break;
                case StepType.KeyDown: cmbType.SelectedIndex = 0; cmbKeyAction.SelectedIndex = 1; _stepKey = st.Key; txtStepKey.Text = st.Key.ToString(); break;
                case StepType.KeyUp: cmbType.SelectedIndex = 0; cmbKeyAction.SelectedIndex = 2; _stepKey = st.Key; txtStepKey.Text = st.Key.ToString(); break;
                case StepType.Delay: cmbType.SelectedIndex = 1; numDelay.Value = Math.Min(numDelay.Maximum, st.DelayMs); break;
                case StepType.MouseClick: cmbType.SelectedIndex = 2; cmbButton.SelectedIndex = (int)st.Button; chkFixed.Checked = !st.UseCursor; txtSX.Text = st.X.ToString(); txtSY.Text = st.Y.ToString(); break;
                case StepType.MouseMove: cmbType.SelectedIndex = 3; txtSX.Text = st.X.ToString(); txtSY.Text = st.Y.ToString(); break;
                case StepType.Text: cmbType.SelectedIndex = 4; txtStepText.Text = st.Text; break;
            }
            RefreshTypeUI();
        }

        void RemoveStep()
        {
            var m = Current; int i = lstSteps.SelectedIndex;
            if (m == null || i < 0 || i >= m.Steps.Count) return;
            m.Steps.RemoveAt(i); _editingStep = -1; btnAddStep.Text = "Adicionar passo";
            MacroEngine.Save(_savePath, _engine.Macros); RefreshSteps();
        }

        void MoveStep(int dir)
        {
            var m = Current; int i = lstSteps.SelectedIndex; int j = i + dir;
            if (m == null || i < 0 || j < 0 || j >= m.Steps.Count) return;
            var tmp = m.Steps[i]; m.Steps[i] = m.Steps[j]; m.Steps[j] = tmp;
            MacroEngine.Save(_savePath, _engine.Macros); RefreshSteps(); lstSteps.SelectedIndex = j;
        }

        void StartCapture()
        {
            _capLeft = 2; btnCapStep.Text = "2...";
            if (_cap == null)
            {
                _cap = new Timer { Interval = 1000 };
                _cap.Tick += (s, e) =>
                {
                    _capLeft--;
                    if (_capLeft <= 0) { _cap.Stop(); POINT p; GetCursorPos(out p); txtSX.Text = p.X.ToString(); txtSY.Text = p.Y.ToString(); chkFixed.Checked = true; btnCapStep.Text = "captar"; }
                    else btnCapStep.Text = _capLeft + "...";
                };
            }
            _cap.Start();
        }
    }
}
