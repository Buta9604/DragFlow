using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>Settings › AI & more: the AI helper, gestures, pen and touch, preview, scripts, recording recipes.</summary>
    public sealed partial class SettingsWindow
    {
        readonly CheckBox aiOn = new CheckBox(), gesturesOn = new CheckBox(), touchOn = new CheckBox(), previewOn = new CheckBox();
        readonly TextBox aiKey = new TextBox();
        readonly Label aiKeyState = new Label();
        readonly ComboBox aiModel = new ComboBox();
        readonly ComboBox[] gestureBoxes = new ComboBox[Gesture.Shapes.Length];

        TabPage PowerPage()
        {
            TabPage p = Page(L.T("AI & more"));
            int y = 8;
            aiOn.Text = L.T("AI helper: AI actions on the dial (only the file you pick an AI action for is sent to Anthropic's Claude)");
            aiOn.SetBounds(12, y, 560, 40); aiOn.Checked = s.AiEnabled;
            y += 42;
            p.Controls.Add(new Label { Text = L.T("API key"), Left = 12, Top = y + 4, Width = 90 });
            aiKey.SetBounds(104, y, 260, 26); aiKey.UseSystemPasswordChar = true;
            var saveKey = new Button { Text = L.T("Save key"), Left = 372, Top = y - 1, Width = 92, Height = 28 };
            var forget = new Button { Text = L.T("Forget key"), Left = 470, Top = y - 1, Width = 100, Height = 28 };
            y += 32;
            aiKeyState.SetBounds(104, y, 466, 20); aiKeyState.ForeColor = Color.DimGray;
            y += 24;
            p.Controls.Add(new Label { Text = L.T("Model"), Left = 12, Top = y + 4, Width = 90 });
            aiModel.SetBounds(104, y, 300, 26); aiModel.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string[] m in AiClient.Models) aiModel.Items.Add(m[1]);
            aiModel.SelectedIndex = Math.Max(0, Array.FindIndex(AiClient.Models, m => m[0] == s.AiModel));
            y += 32;
            p.Controls.Add(Note(L.T("Get a key at console.anthropic.com (Settings › API keys). Use is billed to that account, usually a few cents per file. The key is stored encrypted for your Windows account on this PC only, never in backups."), y, 56));
            y += 60;
            Action showKey = delegate
            {
                string hint = AiClient.KeyHint();
                aiKeyState.Text = hint == null ? L.T("No key stored.") : L.F("Key stored: {0}", hint);
            };
            showKey();
            saveKey.Click += delegate
            {
                try { AiClient.SaveKey(aiKey.Text); aiKey.Text = ""; showKey(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "DragFlow"); }
            };
            forget.Click += delegate { AiClient.ForgetKey(); showKey(); };
            aiOn.CheckedChanged += delegate
            {
                if (!aiOn.Checked || s.AiEnabled) return;
                if (MessageBox.Show(this, L.T("With the AI helper on, the dial offers AI actions (marked \"AI\"). When you pick one, that file is sent to Anthropic over an encrypted connection to be read, and the answer comes back. Nothing is sent otherwise.\r\n\r\nTurn it on?"),
                        "DragFlow", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) aiOn.Checked = false;
            };
            p.Controls.AddRange(new Control[] { aiOn, aiKey, saveKey, forget, aiKeyState, aiModel });

            gesturesOn.Text = L.T("Shapes: draw Z, V, L or N while dragging (after the dial has gone) to get that action next to the pointer");
            gesturesOn.SetBounds(12, y, 560, 40); gesturesOn.Checked = s.GesturesEnabled;
            p.Controls.Add(gesturesOn);
            y += 42;
            string[] choices = ActionCatalog.AllNames.Where(n => n != "Edit" && n != "Tools" && n != "Smart" && n != "Open & share" && n != "Convert" && n != "Resize" && n != "Organize").ToArray();
            for (int i = 0; i < Gesture.Shapes.Length; i++)
            {
                int col = i % 2, row = i / 2;
                p.Controls.Add(new Label { Text = Gesture.Shapes[i], Left = 30 + col * 280, Top = y + 4 + row * 32, Width = 24, Font = new Font(Font, FontStyle.Bold) });
                var box = new ComboBox { Left = 56 + col * 280, Top = y + row * 32, Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
                box.Items.AddRange(choices);
                box.Text = Gesture.ActionFor(s.Gestures, Gesture.Shapes[i]) ?? "";
                gestureBoxes[i] = box;
                p.Controls.Add(box);
            }
            y += 70;
            touchOn.Text = L.T("Larger dial for pen and touch, which waits a little longer"); touchOn.SetBounds(12, y, 560, 24); touchOn.Checked = s.TouchDial;
            y += 28;
            previewOn.Text = L.T("Show a thumbnail and details of what is dragged in the dial"); previewOn.SetBounds(12, y, 560, 24); previewOn.Checked = s.Preview;
            y += 34;
            var scripts = new Button { Text = L.T("Open scripts folder…"), Left = 12, Top = y, Width = 180, Height = 30 };
            scripts.Click += delegate { Scripts.EnsureFolder(); Process.Start("explorer.exe", "\"" + Scripts.Folder + "\""); };
            y += 34;
            p.Controls.Add(Note(L.T("Scripts: PowerShell, Python or batch files in that folder appear on the dial under Custom. A line like \"# DragFlow: name=Upload; types=image\" names them."), y, 40));
            p.Controls.AddRange(new Control[] { touchOn, previewOn, scripts });
            return p;
        }

        bool ApplyPower()
        {
            if (aiOn.Checked && aiKey.Text.Trim().Length > 0)
            {
                try { AiClient.SaveKey(aiKey.Text); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "DragFlow"); return false; }
            }
            if (aiOn.Checked && !AiClient.HasKey)
            {
                MessageBox.Show(this, L.T("The AI helper needs an API key: paste it and press Save key, or turn the AI helper off."), "DragFlow");
                return false;
            }
            s.AiEnabled = aiOn.Checked;
            s.AiModel = AiClient.Models[Math.Max(0, aiModel.SelectedIndex)][0];
            s.GesturesEnabled = gesturesOn.Checked;
            s.Gestures = string.Join("|", Gesture.Shapes.Select((shape, i) => shape + "=" + gestureBoxes[i].Text.Replace("|", " ").Replace("=", " ").Trim()).ToArray());
            s.TouchDial = touchOn.Checked;
            s.Preview = previewOn.Checked;
            return true;
        }
    }
}
