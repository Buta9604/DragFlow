using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DragFlow
{
    /// <summary>Video and audio tools on top of ffmpeg: compress to a size, rotate, speed, merge, contact sheet, subtitles, fade, silence.</summary>
    public static partial class FileOps
    {
        static string FindFfprobe()
        {
            string ff = FindFfmpeg();
            if (ff == null) return null;
            string beside = Path.Combine(Path.GetDirectoryName(ff), "ffprobe.exe");
            return File.Exists(beside) ? beside : FindOnPath("ffprobe.exe");
        }

        /// <summary>Length of a video or audio file in seconds (0 if unknown).</summary>
        public static double Duration(string file)
        {
            string probe = FindFfprobe();
            if (probe == null) return 0;
            try
            {
                var psi = new ProcessStartInfo(probe, "-v error -show_entries format=duration -of default=nw=1:nk=1 \"" + file + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (Process p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(20000);
                    double d;
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
                }
            }
            catch (Exception) { return 0; }
        }

        /// <summary>The codec of a file's first sound track ("aac", "mp3", "vorbis"…), or null when it has no sound.</summary>
        public static string AudioCodec(string file)
        {
            string probe = FindFfprobe();
            if (probe == null) return "unknown";
            try
            {
                var psi = new ProcessStartInfo(probe, "-v error -select_streams a:0 -show_entries stream=codec_name -of default=nw=1:nk=1 \"" + file + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (Process p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(20000);
                    return s.Length == 0 ? null : s.Split('\n')[0].Trim();
                }
            }
            catch (Exception) { return "unknown"; }
        }

        /// <summary>How to carry a file's sound into an .mp4: drop it, keep it as is, or re-encode what MP4 cannot hold (Vorbis, FLAC, PCM).</summary>
        static string AudioForMp4(string file)
        {
            string codec = AudioCodec(file);
            if (codec == null) return "-an";
            return codec == "aac" || codec == "mp3" || codec == "ac3" ? "-c:a copy" : "-c:a aac -b:a 192k";
        }

        static HashSet<string> filters;

        /// <summary>Like HasFilter, but never waits: until the first probe has finished it says yes and starts the probe in the background.</summary>
        public static bool FilterLikely(string name)
        {
            if (filters != null) return filters.Contains(name);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { HasFilter(name); } catch (Exception) { } });
            return true;
        }

        /// <summary>Does this ffmpeg have a filter (e.g. "subtitles" needs libass)?</summary>
        public static bool HasFilter(string name)
        {
            if (filters == null)
            {
                var set = new HashSet<string>();
                string ff = FindFfmpeg();
                if (ff != null)
                    try
                    {
                        var psi = new ProcessStartInfo(ff, "-hide_banner -filters") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                        using (Process p = Process.Start(psi))
                        {
                            foreach (string line in p.StandardOutput.ReadToEnd().Split('\n'))
                            {
                                string[] cols = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (cols.Length >= 3 && cols[0].Length <= 3) set.Add(cols[1]);
                            }
                            p.WaitForExit(10000);
                        }
                    }
                    catch (Exception) { }
                filters = set;
            }
            return filters.Contains(name);
        }

        static string Num(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        static string MediaOut(string input, string suffix, string ext, string title, string args)
        {
            string ff = RequireFfmpeg();
            string dest = UniquePath(ParentOf(input), Path.GetFileNameWithoutExtension(input) + suffix, ext);
            using (UndoLog.Begin(title))
            {
                RunFfmpegTo(ff, "-i \"" + input + "\" " + args, dest);
                if (!File.Exists(dest)) throw new InvalidOperationException("ffmpeg produced no output.");
                UndoLog.Created(dest);
            }
            return dest;
        }

        /// <summary>Re-encode a video so it is no bigger than the given size (two passes for accuracy).</summary>
        public static string CompressToSize(string video, double targetMB)
        {
            if (targetMB <= 0.1) throw new ArgumentException("Give a size in MB, for example 25.");
            double dur = Duration(video);
            if (dur <= 0) throw new InvalidOperationException("Could not read how long the video is (ffprobe missing?).");
            double totalKbps = targetMB * 8192 / dur * 0.95; // 5 % margin for the container
            int audio = totalKbps > 400 ? 96 : 48;
            int videoKbps = (int)Math.Max(80, totalKbps - audio);
            string ff = RequireFfmpeg();
            string dest = UniquePath(ParentOf(video), Path.GetFileNameWithoutExtension(video) + "_" + Num(targetMB) + "MB", ".mp4");
            string log = Path.Combine(AppPaths.LocalDir("convert"), Guid.NewGuid().ToString("N"));
            try
            {
                using (UndoLog.Begin("Compress to size"))
                {
                    string common = "-c:v libx264 -b:v " + videoKbps + "k -maxrate " + (int)(videoKbps * 1.2) + "k -bufsize " + videoKbps * 2 + "k -preset medium -pix_fmt yuv420p -passlogfile \"" + log + "\"";
                    RunTool(ff, "-y -hide_banner -loglevel error -i \"" + video + "\" " + common + " -pass 1 -an -f null NUL", "ffmpeg failed");
                    RunFfmpegTo(ff, "-i \"" + video + "\" " + common + " -pass 2 -c:a aac -b:a " + audio + "k -movflags +faststart", dest);
                    if (!File.Exists(dest)) throw new InvalidOperationException("ffmpeg produced no output.");
                    UndoLog.Created(dest);
                }
            }
            finally { foreach (string f in Directory.GetFiles(AppPaths.LocalDir("convert"), Path.GetFileName(log) + "*")) try { File.Delete(f); } catch (Exception) { } }
            return dest;
        }

        public static string RotateVideo(string video, string direction)
        {
            return MediaOut(video, "_rotated", ".mp4", "Rotate video", "-vf transpose=" + (direction == "left" ? "2" : "1") + " -c:v libx264 -crf 22 -preset medium " + AudioForMp4(video));
        }

        /// <summary>Faster or slower (0.5 = half speed, 2 = double), sound pitch kept.</summary>
        public static string Speed(string file, double factor)
        {
            if (factor < 0.25 || factor > 4) throw new ArgumentException("Choose a speed between 0.25 and 4 (1 = unchanged).");
            string suffix = "_x" + Num(factor);
            bool video = Classify(file) == FileKind.Video;
            string tempo = factor >= 0.5 && factor <= 2 ? "atempo=" + Num(factor) : factor < 0.5 ? "atempo=0.5,atempo=" + Num(factor / 0.5) : "atempo=2,atempo=" + Num(factor / 2);
            if (!video) return MediaOut(file, suffix, Path.GetExtension(file).ToLowerInvariant() == ".wav" ? ".wav" : ".mp3", "Change speed", "-af " + tempo + (Path.GetExtension(file).ToLowerInvariant() == ".wav" ? "" : " -codec:a libmp3lame -q:a 2"));
            if (AudioCodec(file) == null) // a silent video: no sound track to speed up
                return MediaOut(file, suffix, ".mp4", "Change speed", "-filter_complex \"[0:v]setpts=PTS/" + Num(factor) + "[v]\" -map [v] -an -c:v libx264 -crf 22 -preset medium");
            return MediaOut(file, suffix, ".mp4", "Change speed",
                "-filter_complex \"[0:v]setpts=PTS/" + Num(factor) + "[v];[0:a]" + tempo + "[a]\" -map [v] -map [a] -c:v libx264 -crf 22 -preset medium -c:a aac");
        }

        /// <summary>Join clips (all video or all audio) into one, in the order given; videos are fitted to the first one's size.</summary>
        public static string Merge(IList<string> paths)
        {
            var files = paths.Where(File.Exists).ToList();
            if (files.Count < 2) throw new InvalidOperationException("Merging needs at least two files.");
            bool video = files.All(f => Classify(f) == FileKind.Video), audio = files.All(f => Classify(f) == FileKind.Audio);
            if (!video && !audio) throw new InvalidOperationException("Merge works on videos together or audio files together.");
            string ff = RequireFfmpeg();
            var sb = new StringBuilder();
            foreach (string f in files) sb.Append("-i \"").Append(f).Append("\" ");
            var fc = new StringBuilder();
            if (video)
            {
                bool sound = files.All(f => AudioCodec(f) != null); // a silent clip among them: the result is silent (no track to join)
                for (int i = 0; i < files.Count; i++)
                    fc.Append("[" + i + ":v]scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30[v" + i + "];");
                for (int i = 0; i < files.Count; i++) fc.Append("[v" + i + "]" + (sound ? "[" + i + ":a]" : ""));
                fc.Append("concat=n=" + files.Count + ":v=1:a=" + (sound ? "1[v][a]" : "0[v]"));
                sb.Append("-filter_complex \"" + fc + "\" -map [v] " + (sound ? "-map [a] -c:a aac" : "-an") + " -c:v libx264 -crf 22 -preset medium -movflags +faststart");
            }
            else
            {
                for (int i = 0; i < files.Count; i++) fc.Append("[" + i + ":a]");
                fc.Append("concat=n=" + files.Count + ":v=0:a=1[a]");
                sb.Append("-filter_complex \"" + fc + "\" -map [a] -codec:a libmp3lame -q:a 2");
            }
            string dest = UniquePath(ParentOf(files[0]), "Merged", video ? ".mp4" : ".mp3");
            using (UndoLog.Begin("Merge"))
            {
                RunFfmpegTo(ff, sb.ToString(), dest);
                if (!File.Exists(dest)) throw new InvalidOperationException("ffmpeg produced no output.");
                UndoLog.Created(dest);
            }
            return dest;
        }

        /// <summary>16 frames spread over the video, on one picture.</summary>
        public static string ContactSheet(string video)
        {
            double dur = Duration(video);
            double interval = Math.Max(0.5, dur > 0 ? dur / 16 : 1);
            return MediaOut(video, "_sheet", ".jpg", "Contact sheet", "-vf \"fps=1/" + Num(interval) + ",scale=320:-1,tile=4x4\" -frames:v 1 -q:v 3");
        }

        /// <summary>The matching .srt (same name) burned into the picture. Needs ffmpeg with libass.</summary>
        public static string BurnSubtitles(string video)
        {
            string srt = Path.ChangeExtension(video, ".srt");
            if (!File.Exists(srt)) throw new InvalidOperationException("Put the subtitles next to the video as " + Path.GetFileName(srt) + " first.");
            if (!HasFilter("subtitles")) throw new InvalidOperationException("This ffmpeg cannot draw subtitles (it was built without libass).");
            // The subtitles filter is fussy about drive letters, backslashes and quotes in paths: ffmpeg runs inside a plain
            // folder holding a copy of the file, so the filter only ever sees "sub.srt".
            string work = Path.Combine(AppPaths.LocalDir("convert"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            File.Copy(srt, Path.Combine(work, "sub.srt"));
            string ff = RequireFfmpeg();
            string dest = UniquePath(ParentOf(video), Path.GetFileNameWithoutExtension(video) + "_subtitled", ".mp4");
            try
            {
                using (UndoLog.Begin("Subtitles"))
                {
                    RunFfmpegTo(ff, "-i \"" + video + "\" -vf subtitles=sub.srt -c:v libx264 -crf 22 -preset medium " + AudioForMp4(video), dest, work);
                    if (!File.Exists(dest)) throw new InvalidOperationException("ffmpeg produced no output.");
                    UndoLog.Created(dest);
                }
            }
            finally { try { Directory.Delete(work, true); } catch (Exception) { } }
            return dest;
        }

        /// <summary>Two-second fade in and fade out.</summary>
        public static string Fade(string audio)
        {
            double dur = Duration(audio);
            string fades = "afade=t=in:st=0:d=2" + (dur > 4 ? ",afade=t=out:st=" + Num(dur - 2) + ":d=2" : "");
            bool wav = Path.GetExtension(audio).ToLowerInvariant() == ".wav";
            return MediaOut(audio, "_fade", wav ? ".wav" : ".mp3", "Fade", "-af \"" + fades + "\"" + (wav ? "" : " -codec:a libmp3lame -q:a 2"));
        }

        /// <summary>Silence at the start and end removed.</summary>
        public static string TrimSilence(string audio)
        {
            bool wav = Path.GetExtension(audio).ToLowerInvariant() == ".wav";
            return MediaOut(audio, "_nosilence", wav ? ".wav" : ".mp3", "Trim silence",
                "-af \"silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.3,areverse,silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.3,areverse\"" + (wav ? "" : " -codec:a libmp3lame -q:a 2"));
        }
    }
}
