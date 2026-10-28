using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DragFlow
{
    /// <summary>Dashboard page: AI and keys (the Anthropic connection, the model, usage and a monthly limit).</summary>
    public sealed partial class DashboardWindow
    {
        UIElement AiPage()
        {
            bool hasKey = AiClient.HasKey;
            string hint = AiClient.KeyHint();
            var state = hasKey ? UI.Chip(L.T("Key stored") + (hint != null ? ": " + hint : ""), UI.T.Success) : UI.Chip(L.T("No key yet"), UI.T.Muted);
            var key = new PasswordBox { Width = 320, Padding = new Thickness(6, 4, 6, 4), FontSize = 13.5, Background = UI.T.Field, Foreground = UI.T.Text, BorderBrush = UI.T.CardBorder };
            System.Windows.Automation.AutomationProperties.SetName(key, L.T("API key"));
            var testResult = UI.Sub("");
            var save = UI.Btn(L.T("Save key"), delegate
            {
                AiClient.SaveKey(key.Password);
                key.Clear();
                Saved(L.T("API key saved (encrypted for your Windows account)"));
                Refresh();
            }, true);
            var forget = UI.Btn(L.T("Forget key"), delegate
            {
                if (MessageBox.Show(L.T("Remove the stored API key from this PC?"), "DragFlow", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                AiClient.ForgetKey();
                S.AiEnabled = false; S.Save(); host.Apply();
                Saved(L.T("API key removed"));
                Refresh();
            });
            var test = UI.Btn(L.T("Test connection"), delegate { });
            test.Click += delegate
            {
                if (!AiClient.HasKey) { testResult.Text = L.T("Save a key first."); testResult.Foreground = UI.T.Warning; return; }
                test.IsEnabled = false;
                testResult.Text = L.T("Asking…"); testResult.Foreground = UI.T.Sub;
                Task.Factory.StartNew(delegate { return AiClient.Test(); }).ContinueWith(delegate (Task<string> t)
                {
                    test.IsEnabled = true;
                    if (t.Exception != null) { testResult.Text = t.Exception.InnerExceptions[0].Message; testResult.Foreground = UI.T.Danger; }
                    else { testResult.Text = t.Result; testResult.Foreground = UI.T.Success; }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            };

            var model = UI.Combo(AiClient.Models.Select(m => L.T(m[1])), Math.Max(0, Array.FindIndex(AiClient.Models, m => m[0] == S.AiModel)), 300,
                delegate (int i) { Change(L.F("Model: {0}", AiClient.ModelName(AiClient.Models[i][0])), delegate { S.AiModel = AiClient.Models[i][0]; }); });

            var enable = UI.Toggle(L.T("AI helper"), S.AiEnabled, delegate (bool v)
            {
                if (v && !AiClient.HasKey) { MessageBox.Show(L.T("Save an API key first."), "DragFlow"); Refresh(); return; }
                if (v && MessageBox.Show(L.T("With the AI helper on, the dial offers AI actions (marked \"AI\"). When you pick one, that file is sent to Anthropic over an encrypted connection to be read, and the answer comes back. Nothing is sent otherwise.") + "\r\n\r\n" + L.T("Turn it on?"),
                        "DragFlow", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { Refresh(); return; }
                Change(v ? L.T("AI helper on") : L.T("AI helper off"), delegate { S.AiEnabled = v; });
            });

            AiUsage.MonthTotal month = AiUsage.For(DateTime.Now);
            AiUsage.MonthTotal last = AiUsage.For(DateTime.Now.AddMonths(-1));
            var limit = UI.Field(S.AiMonthlyLimit > 0 ? S.AiMonthlyLimit.ToString("0.##", CultureInfo.InvariantCulture) : "", 90, delegate (string v)
            {
                double d;
                string t = v.Trim().TrimStart('$').Replace(" ", "");
                // "1,000" or "1.000" with three digits after the mark is a thousands separator; otherwise the mark is the decimal point
                if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d{1,3}([.,]\d{3})+$")) t = t.Replace(",", "").Replace(".", "");
                else t = t.Replace(',', '.');
                if (t.Length == 0) d = 0;
                else if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d < 0) { MessageBox.Show(L.T("Type an amount in dollars, such as 5, or leave it empty for no limit."), "DragFlow"); return; }
                Change(d > 0 ? L.F("Monthly limit: ${0}", d.ToString("0.##", CultureInfo.InvariantCulture)) : L.T("No monthly limit"), delegate { S.AiMonthlyLimit = d; });
            });

            return UI.Stack(0,
                UI.Card(L.T("Anthropic (Claude)"), L.T("The AI helper uses your own Anthropic API key. Get one at console.anthropic.com under API keys; use is billed to that account, usually a few cents per file."),
                    UI.Row(10, UI.Text(L.T("Status"), 13.5), state),
                    UI.Setting(L.T("API key"), L.T("Stored encrypted so only your Windows account on this PC can read it. Never in settings files, backups or logs."), UI.Row(8, key, save)),
                    UI.Row(8, test, hasKey ? forget : null),
                    testResult,
                    UI.Line(),
                    UI.Setting(L.T("AI actions on the dial"), L.T("Only the file you pick an AI action for is sent, and only then."), enable),
                    UI.Setting(L.T("Model"), L.T("Opus gives the best results; Sonnet is faster and cheaper; Haiku is cheapest."), model)),
                UI.Card(L.T("Usage"), L.T("Counted from each answer; the cost is an estimate at Anthropic's list prices. Your Anthropic account shows the exact bill."),
                    UI.Wrap(UI.Tile(L.T("This month"), "$" + month.Cost.ToString("0.00", CultureInfo.InvariantCulture)),
                            UI.Tile(L.T("Requests this month"), month.Requests.ToString()),
                            UI.Tile(L.T("Tokens this month"), (month.Input + month.Output).ToString("N0")),
                            UI.Tile(L.T("Last month"), "$" + last.Cost.ToString("0.00", CultureInfo.InvariantCulture))),
                    UI.Setting(L.T("Monthly limit (US dollars)"), L.T("AI actions pause once this month's estimate reaches it. Empty for no limit."), limit),
                    UI.Btn(L.T("Clear the usage record"), delegate
                    {
                        if (MessageBox.Show(L.T("Clear the record of AI use? This also resets this month's count for the limit."), "DragFlow", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        AiUsage.Clear(); Refresh();
                    })),
                UI.Card(L.T("Other connections"), L.T("A share link straight from OneDrive would need you to sign in to Microsoft inside DragFlow. Until then, Share link copies the file into OneDrive and opens Windows' Share window, which has Copy link."), null));
        }
    }
}
