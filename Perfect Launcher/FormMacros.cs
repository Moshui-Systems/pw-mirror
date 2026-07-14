using System;
using System.Drawing;
using System.Windows.Forms;

namespace Perfect_Launcher
{
    // Janela para criar/editar/excluir macros. Construída em código (sem Designer).
    public class FormMacros : Form
    {
        readonly MacroEngine _engine;
        readonly string _savePath;

        ListBox lst;
        TextBox txtName;
        TextBox txtTrigger;
        RadioButton rbAll;
        RadioButton rbCurrent;
        TextBox txtSeq;

        Keys _pendingTrigger = Keys.None;

        public FormMacros(MacroEngine engine, string savePath)
        {
            _engine = engine;
            _savePath = savePath;
            BuildUi();
            RefreshList();
            if (lst.Items.Count > 0)
                lst.SelectedIndex = 0;
        }

        void BuildUi()
        {
            Text = "Macros";
            ClientSize = new Size(600, 400);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f);
            var ico = Theme.LoadIcon("perfectmirror.ico");
            if (ico != null) Icon = ico;

            // ---- Lista (esquerda) ----
            lst = new ListBox
            {
                Location = new Point(12, 12),
                Size = new Size(180, 300),
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            lst.SelectedIndexChanged += (s, e) => LoadSelected();
            Controls.Add(lst);

            var btnNovo = MakeButton("Novo", new Point(12, 320), 88);
            btnNovo.Click += (s, e) => NewMacro();
            var btnExcluir = MakeButton("Excluir", new Point(104, 320), 88);
            btnExcluir.Click += (s, e) => DeleteMacro();
            Controls.Add(btnNovo);
            Controls.Add(btnExcluir);

            // ---- Editor (direita) ----
            int x = 212, w = 372;

            Controls.Add(MakeLabel("Nome", new Point(x, 16)));
            txtName = MakeText(new Point(x, 36), w);
            Controls.Add(txtName);

            Controls.Add(MakeLabel("Tecla de atalho (clique e aperte a tecla)", new Point(x, 74)));
            txtTrigger = MakeText(new Point(x, 94), w);
            txtTrigger.ReadOnly = true;
            txtTrigger.Cursor = Cursors.Hand;
            txtTrigger.KeyDown += TxtTrigger_KeyDown;
            Controls.Add(txtTrigger);

            Controls.Add(MakeLabel("Enviar para", new Point(x, 132)));
            rbAll = new RadioButton { Text = "Todas as contas", Location = new Point(x, 152), AutoSize = true, ForeColor = Theme.Text, Checked = true };
            rbCurrent = new RadioButton { Text = "Conta atual", Location = new Point(x + 160, 152), AutoSize = true, ForeColor = Theme.Text };
            Controls.Add(rbAll);
            Controls.Add(rbCurrent);

            Controls.Add(MakeLabel("Sequência de teclas", new Point(x, 186)));
            txtSeq = MakeText(new Point(x, 206), w);
            txtSeq.Multiline = true;
            txtSeq.Height = 60;
            Controls.Add(txtSeq);

            Controls.Add(new Label
            {
                Text = "Ex.: F1 300 F2 R  — teclas separadas por espaço; números = espera em ms.\n" +
                       "Teclas numéricas do topo usam D1..D0 (ex.: D1). Numérico usa NumPad1.",
                Location = new Point(x, 272),
                Size = new Size(w, 44),
                ForeColor = Theme.TextDim,
                Font = new Font("Segoe UI", 8f)
            });

            var btnSalvar = MakeButton("Salvar", new Point(x, 320), w);
            btnSalvar.BackColor = Theme.Accent;
            btnSalvar.ForeColor = Color.White;
            btnSalvar.Click += (s, e) => SaveSelected();
            Controls.Add(btnSalvar);
        }

        Label MakeLabel(string text, Point p)
        {
            return new Label { Text = text, Location = p, AutoSize = true, ForeColor = Theme.TextDim, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
        }

        TextBox MakeText(Point p, int w)
        {
            return new TextBox { Location = p, Size = new Size(w, 24), BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        }

        Button MakeButton(string text, Point p, int w)
        {
            var b = new Button
            {
                Text = text,
                Location = p,
                Size = new Size(w, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Theme.Accent;
            b.FlatAppearance.MouseOverBackColor = Theme.PanelHover;
            return b;
        }

        void TxtTrigger_KeyDown(object sender, KeyEventArgs e)
        {
            _pendingTrigger = e.KeyCode;
            txtTrigger.Text = e.KeyCode.ToString();
            e.SuppressKeyPress = true;
        }

        void RefreshList()
        {
            int sel = lst.SelectedIndex;
            lst.Items.Clear();
            foreach (var m in _engine.Macros)
                lst.Items.Add(string.IsNullOrWhiteSpace(m.Name) ? "(sem nome)" : m.Name);
            if (sel >= 0 && sel < lst.Items.Count)
                lst.SelectedIndex = sel;
        }

        void LoadSelected()
        {
            int i = lst.SelectedIndex;
            if (i < 0 || i >= _engine.Macros.Count)
                return;

            var m = _engine.Macros[i];
            txtName.Text = m.Name;
            _pendingTrigger = m.Trigger;
            txtTrigger.Text = m.Trigger == Keys.None ? "" : m.Trigger.ToString();
            rbAll.Checked = m.AllAccounts;
            rbCurrent.Checked = !m.AllAccounts;
            txtSeq.Text = m.SequenceText;
        }

        void SaveSelected()
        {
            int i = lst.SelectedIndex;
            if (i < 0 || i >= _engine.Macros.Count)
                return;

            var m = _engine.Macros[i];
            m.Name = string.IsNullOrWhiteSpace(txtName.Text) ? "(sem nome)" : txtName.Text.Trim();
            m.Trigger = _pendingTrigger;
            m.AllAccounts = rbAll.Checked;
            m.SetSequenceFromText(txtSeq.Text);

            MacroEngine.Save(_savePath, _engine.Macros);
            RefreshList();
        }

        void NewMacro()
        {
            _engine.Macros.Add(new Macro());
            RefreshList();
            lst.SelectedIndex = lst.Items.Count - 1;
        }

        void DeleteMacro()
        {
            int i = lst.SelectedIndex;
            if (i < 0 || i >= _engine.Macros.Count)
                return;

            _engine.Macros.RemoveAt(i);
            MacroEngine.Save(_savePath, _engine.Macros);
            RefreshList();
            if (lst.Items.Count > 0)
                lst.SelectedIndex = Math.Min(i, lst.Items.Count - 1);
            else
                ClearEditor();
        }

        void ClearEditor()
        {
            txtName.Text = "";
            txtTrigger.Text = "";
            txtSeq.Text = "";
            _pendingTrigger = Keys.None;
        }
    }
}
