using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DragFlow
{
    /// <summary>
    /// What the AI helper has used, per month and model (tokens, from each answer), and an estimate of what that cost at
    /// Anthropic's list prices. Kept in %LOCALAPPDATA%\DragFlow\ai-usage.tsv. Also enforces the optional monthly limit.
    /// </summary>
    public static class AiUsage
    {
        static readonly object gate = new object();
        static string FilePath { get { return Path.Combine(AppPaths.Local, "ai-usage.tsv"); } }

        /// <summary>US dollars per million input and output tokens.</summary>
        public static void Prices(string model, out double input, out double output)
        {
            string m = model ?? "";
            if (m.StartsWith("claude-haiku")) { input = 0.10; output = 0.50; }
            else if (m.StartsWith("claude-sonnet")) { input = 2; output = 10; }
            else if (m.StartsWith("claude-fable") || m.StartsWith("claude-mythos")) { input = 10; output = 50; }
            else if (m.StartsWith("claude-opus-5-5")) { input = 4; output = 20; }
            else { input = 5; output = 25; } // other and older Opus models
        }

        public static double Cost(string model, long input, long output)
        {
            double pi, po;
            Prices(model, out pi, out po);
            if ((model ?? "").StartsWith("claude-haiku-5") && input > 100000) { pi = 0.50; po = 2.50; } // Haiku 5.5's price for long prompts
            return input / 1e6 * pi + output / 1e6 * po;
        }

        static string Month(DateTime d) { return d.ToString("yyyy-MM", CultureInfo.InvariantCulture); }

        public static void Record(string model, long input, long output, DateTime when)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.Local);
                    File.AppendAllText(FilePath, Month(when) + "\t" + model + "\t" + input + "\t" + output + "\r\n");
                }
                catch (Exception ex) { Log.Error("AI usage", ex); }
            }
        }

        public sealed class MonthTotal
        {
            public long Input, Output, Requests;
            public double Cost;
        }

        /// <summary>Totals for the month that contains this date.</summary>
        public static MonthTotal For(DateTime when)
        {
            var m = new MonthTotal();
            string key = Month(when);
            lock (gate)
            {
                try
                {
                    if (!File.Exists(FilePath)) return m;
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        string[] p = line.Split('\t');
                        long i, o;
                        if (p.Length < 4 || p[0] != key || !long.TryParse(p[2], out i) || !long.TryParse(p[3], out o)) continue;
                        m.Input += i; m.Output += o; m.Requests++;
                        m.Cost += Cost(p[1], i, o);
                    }
                }
                catch (Exception ex) { Log.Error("AI usage", ex); }
            }
            return m;
        }

        public static void Clear()
        {
            lock (gate) { try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception) { } }
        }

        /// <summary>Refuse before sending when this month's estimated spend has reached the limit set in the dashboard.</summary>
        public static void CheckLimit(double limit, DateTime now)
        {
            if (limit <= 0) return;
            double spent = For(now).Cost;
            if (spent >= limit)
                throw new InvalidOperationException("The AI helper has reached this month's limit of $" + limit.ToString("0.00", CultureInfo.InvariantCulture) +
                    " (about $" + spent.ToString("0.00", CultureInfo.InvariantCulture) + " used). Raise or remove the limit in the dashboard under AI and keys.");
        }
    }
}
