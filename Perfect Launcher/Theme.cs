using System.Drawing;
using System.Windows.Forms;

namespace Perfect_Launcher
{
    // Tema visual da Moshui Systems ("ink"): fundo escuro + destaque violeta.
    // Aplicado em runtime para não precisar mexer nos arquivos do Designer.
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(22, 22, 27);
        public static readonly Color Panel = Color.FromArgb(37, 37, 46);
        public static readonly Color PanelHover = Color.FromArgb(52, 52, 64);
        public static readonly Color Accent = Color.FromArgb(124, 92, 255);
        public static readonly Color AccentHover = Color.FromArgb(150, 120, 255);
        public static readonly Color Text = Color.FromArgb(235, 235, 240);
        public static readonly Color TextDim = Color.FromArgb(160, 160, 172);

        // Estiliza só os botões (usado na janela principal, que já tem fundo
        // customizável pelo usuário e não deve ter a cor sobrescrita).
        public static void StyleButtons(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Button b)
                    StyleButton(b);

                if (c.HasChildren)
                    StyleButtons(c);
            }
        }

        // Aplica o tema escuro completo a uma janela (fundo, textos, botões, listas).
        public static void ApplyDark(Control root)
        {
            foreach (Control c in root.Controls)
            {
                switch (c)
                {
                    case Button b:
                        StyleButton(b);
                        break;
                    case ListBox lb:
                        lb.BackColor = Panel;
                        lb.ForeColor = Text;
                        lb.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case NumericUpDown nud:
                        nud.BackColor = Panel;
                        nud.ForeColor = Text;
                        nud.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case GroupBox gb:
                        gb.ForeColor = Text;
                        break;
                    case Label lbl:
                        lbl.ForeColor = TextDim;
                        break;
                    case CheckBox cb:
                        cb.ForeColor = Text;
                        break;
                    case TabControl tc:
                        foreach (TabPage tp in tc.TabPages)
                            tp.BackColor = Bg;
                        break;
                }

                if (c.HasChildren)
                    ApplyDark(c);
            }

            root.BackColor = Bg;
            root.ForeColor = Text;
        }

        private static void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Accent;
            b.FlatAppearance.MouseOverBackColor = PanelHover;
            b.FlatAppearance.MouseDownBackColor = Accent;
            b.BackColor = Panel;
            b.ForeColor = Text;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
        }
    }
}
