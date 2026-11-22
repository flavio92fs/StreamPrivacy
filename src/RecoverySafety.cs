using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

namespace StreamPrivacy
{
    // The journal is written before a capture-affinity change. Its independent
    // companion process survives termination of the UI and restores only windows
    // carrying this session's unique property. There is no background service.
    internal sealed class RecoverySafety : IDisposable
    {
        internal const long MarkerValue = 0x53505256;
        internal static string DirectoryOverride = null;
        internal static string DefaultDirectory
        {
            get { return DirectoryOverride ?? Path.Combine(Path.GetDirectoryName(SettingStore.DefaultPath), "Recovery"); }
        }

        private readonly string directory;
        private readonly string marker;
        private readonly int ownerPid;
        private readonly long ownerStart;
        private string journalPath;
        private Process watcher;

        internal RecoverySafety(string recoveryDirectory, string sessionMarker)
        {
            directory = Path.GetFullPath(recoveryDirectory ?? DefaultDirectory);
            marker = sessionMarker;
            using (Process owner = Process.GetCurrentProcess()) {
                ownerPid = owner.Id; ownerStart = owner.StartTime.ToUniversalTime().Ticks;
            }
        }

        internal int WatcherProcessId
        {
            get { return watcher == null || watcher.HasExited ? 0 : watcher.Id; }
        }

        internal void Prepare(IEnumerable<ProtectedWindow> windows)
        {
            Directory.CreateDirectory(directory);
            if (journalPath == null) journalPath = Path.Combine(directory, "session-" + Guid.NewGuid().ToString("N") + ".xml");
            Save(journalPath, marker, ownerPid, ownerStart, windows);
            EnsureWatcher();
        }

        internal void Update(IEnumerable<ProtectedWindow> windows, int count)
        {
            if (journalPath == null) return;
            Save(journalPath, marker, ownerPid, ownerStart, windows);
            if (count != 0) return;
            // The complete empty snapshot is durable before deleting the file.
            // The watcher exits when it sees that this journal is gone.
            File.Delete(journalPath);
            TryDelete(journalPath + ".ready");
            journalPath = null;
            if (watcher != null) { watcher.Dispose(); watcher = null; }
        }

        internal void Maintain(IEnumerable<ProtectedWindow> windows, int count)
        {
            if (count > 0 && (watcher == null || watcher.HasExited)) Prepare(windows);
        }

        private void EnsureWatcher()
        {
            if (watcher == null || watcher.HasExited)
            {
                if (watcher != null) watcher.Dispose();
                TryDelete(journalPath + ".ready");
                ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath,
                    "--recovery-watch \"" + journalPath + "\" " + ownerPid.ToString(CultureInfo.InvariantCulture) + " " + ownerStart.ToString(CultureInfo.InvariantCulture));
                start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden;
                watcher = Process.Start(start);
                if (watcher == null) throw new InvalidOperationException("Impossibile avviare il recupero delle finestre. Nessuna nuova protezione applicata.");
            }
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                if (watcher.HasExited) break;
                try {
                    int readyPid;
                    if (File.Exists(journalPath + ".ready") && Int32.TryParse(File.ReadAllText(journalPath + ".ready"), out readyPid) && readyPid == watcher.Id) return;
                } catch (IOException) { }
                Thread.Sleep(25);
            }
            throw new InvalidOperationException("Il recupero delle finestre non è pronto. Nessuna nuova protezione applicata.");
        }

        public void Dispose()
        {
            // Do not terminate a companion that may still have work to undo.
            // It exits after the parent, or after the final journal is removed.
            if (watcher != null) { watcher.Dispose(); watcher = null; }
        }

        internal static int RunWatcher(string[] args)
        {
            if (args.Length != 4) return 2;
            string path = Path.GetFullPath(args[1]);
            int pid; long start;
            if (!Int32.TryParse(args[2], out pid) || !Int64.TryParse(args[3], out start) || pid <= 0 || start <= 0) return 2;
            try
            {
                if (!File.Exists(path)) return 0;
                Journal journal = Load(path);
                if (journal.OwnerPid != pid || journal.OwnerStart != start) return 2;
                using (Process owner = OpenMatchingProcess(pid, start))
                {
                    if (owner != null)
                    {
                        // The process handle is acquired before acknowledging the
                        // parent. PID reuse cannot make us monitor another app.
                        WriteReady(path + ".ready");
                        while (!owner.WaitForExit(250)) if (!File.Exists(path)) return 0;
                    }
                }
                return RecoverOne(path) ? 0 : 1;
            }
            catch (Exception) { return 1; }
            finally { TryDelete(path + ".ready"); }
        }

        internal static void RecoverPending(string recoveryDirectory)
        {
            string directoryPath = Path.GetFullPath(recoveryDirectory ?? DefaultDirectory);
            if (!Directory.Exists(directoryPath)) return;
            foreach (string path in Directory.GetFiles(directoryPath, "session-*.xml", SearchOption.TopDirectoryOnly))
                if (!RecoverOne(path))
                    throw new InvalidOperationException("Recupero incompleto delle finestre: chiudi e riapri le app interessate prima di riattivare la protezione. File locale: " + path);
        }

        private static bool RecoverOne(string path)
        {
            string id = Path.GetFileNameWithoutExtension(path);
            Guid parsed;
            if (!id.StartsWith("session-", StringComparison.Ordinal) || !Guid.TryParseExact(id.Substring(8), "N", out parsed))
                throw new InvalidDataException("Nome del file di recupero non valido.");
            using (Mutex gate = new Mutex(false, "Local\\StreamPrivacy.Recovery." + parsed.ToString("N")))
            {
                bool entered = false;
                try
                {
                    try { entered = gate.WaitOne(TimeSpan.FromSeconds(35)); }
                    catch (AbandonedMutexException) { entered = true; }
                    if (!entered) return false;
                    if (!File.Exists(path)) return true;
                    Journal journal = Load(path);
                    using (Process owner = OpenMatchingProcess(journal.OwnerPid, journal.OwnerStart)) if (owner != null) return true;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                    do
                    {
                        List<ProtectedWindow> remaining = new List<ProtectedWindow>();
                        for (int index = 0; index < journal.Windows.Count; index++)
                        {
                            ProtectedWindow window = journal.Windows[index];
                            if (DateTime.UtcNow >= deadline)
                            {
                                for (; index < journal.Windows.Count; index++) remaining.Add(journal.Windows[index]);
                                break;
                            }
                            try { RestoreMarked(window, journal.Marker); }
                            catch (Exception) { remaining.Add(window); }
                        }
                        journal.Windows = remaining;
                        Save(path, journal.Marker, journal.OwnerPid, journal.OwnerStart, remaining);
                        if (remaining.Count == 0)
                        {
                            File.Delete(path); TryDelete(path + ".ready"); return true;
                        }
                        if (DateTime.UtcNow >= deadline) return false;
                        Thread.Sleep(250);
                    } while (true);
                }
                finally { if (entered) gate.ReleaseMutex(); }
            }
        }

        internal static long ProcessStartTicks(uint pid)
        {
            using (Process process = Process.GetProcessById(checked((int)pid))) return process.StartTime.ToUniversalTime().Ticks;
        }

        private static Process OpenMatchingProcess(int pid, long start)
        {
            Process process = null;
            try
            {
                process = Process.GetProcessById(pid);
                IntPtr acquiredHandle = process.Handle;
                if (acquiredHandle != IntPtr.Zero && process.StartTime.ToUniversalTime().Ticks == start && !process.HasExited) return process;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch { if (process != null) process.Dispose(); throw; }
            if (process != null) process.Dispose();
            return null;
        }

        internal static bool Matches(ProtectedWindow item, string sessionMarker)
        {
            uint pid;
            uint thread = Native.GetWindowThreadProcessId(item.Window.Handle, out pid);
            if (thread != item.Window.Thread || pid != item.Window.Pid || Native.GetProp(item.Window.Handle, sessionMarker) != new IntPtr(MarkerValue)) return false;
            try { return ProcessStartTicks(pid) == item.ProcessStartTicks; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        internal static void RestoreMarked(ProtectedWindow tracked, string sessionMarker)
        {
            if (!Matches(tracked, sessionMarker)) return;
            uint current;
            if (!Native.GetWindowDisplayAffinity(tracked.Window.Handle, out current)) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            // A different setting made independently is left untouched. An
            // uncertain write to the same thread is ordered by a restoration
            // request even when the current setting still equals the original.
            if ((current == 0x11 || (tracked.Uncertain && current == tracked.Original)) && !Native.SetCapturePrivacyChecked(tracked.Window.Handle, tracked.Original, sessionMarker))
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            uint restored;
            if (!Native.GetWindowDisplayAffinity(tracked.Window.Handle, out restored)) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            if (restored == 0x11 && tracked.Original != 0x11) throw new InvalidOperationException("Ripristino della finestra non confermato.");
            Native.RemoveProp(tracked.Window.Handle, sessionMarker);
        }

        private sealed class Journal
        {
            internal string Marker;
            internal int OwnerPid;
            internal long OwnerStart;
            internal List<ProtectedWindow> Windows = new List<ProtectedWindow>();
        }

        private static void Save(string path, string sessionMarker, int pid, long start, IEnumerable<ProtectedWindow> windows)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(path), "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false }))
                    {
                        writer.WriteStartDocument(); writer.WriteStartElement("streamPrivacyRecovery"); writer.WriteAttributeString("version", "1");
                        writer.WriteAttributeString("marker", sessionMarker); writer.WriteAttributeString("ownerPid", pid.ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("ownerStart", start.ToString(CultureInfo.InvariantCulture));
                        int count = 0;
                        foreach (ProtectedWindow tracked in windows)
                        {
                            if (++count > 4096) throw new InvalidOperationException("Troppe finestre da proteggere nella stessa sessione.");
                            writer.WriteStartElement("window");
                            writer.WriteAttributeString("handle", tracked.Window.Handle.ToInt64().ToString(CultureInfo.InvariantCulture));
                            writer.WriteAttributeString("pid", tracked.Window.Pid.ToString(CultureInfo.InvariantCulture));
                            writer.WriteAttributeString("thread", tracked.Window.Thread.ToString(CultureInfo.InvariantCulture));
                            writer.WriteAttributeString("start", tracked.ProcessStartTicks.ToString(CultureInfo.InvariantCulture));
                            writer.WriteAttributeString("original", tracked.Original.ToString(CultureInfo.InvariantCulture));
                            writer.WriteAttributeString("uncertain", XmlConvert.ToString(tracked.Uncertain)); writer.WriteEndElement();
                        }
                        writer.WriteEndElement(); writer.WriteEndDocument();
                    }
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { TryDelete(temporary); }
        }

        private static Journal Load(string path)
        {
            XmlDocument document = new XmlDocument(); document.XmlResolver = null;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 })) document.Load(reader);
            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "streamPrivacyRecovery" || root.GetAttribute("version") != "1") throw new InvalidDataException("Formato del file di recupero non valido.");
            Journal result = new Journal(); result.Marker = root.GetAttribute("marker");
            Guid markerGuid;
            const string prefix = "StreamPrivacy.Window.";
            if (!result.Marker.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(result.Marker.Substring(prefix.Length), "N", out markerGuid)) throw new InvalidDataException("Identificatore del recupero non valido.");
            result.OwnerPid = Int32.Parse(root.GetAttribute("ownerPid"), CultureInfo.InvariantCulture);
            result.OwnerStart = Int64.Parse(root.GetAttribute("ownerStart"), CultureInfo.InvariantCulture);
            if (result.OwnerPid <= 0 || result.OwnerStart <= 0) throw new InvalidDataException("Processo del recupero non valido.");
            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Whitespace || node.NodeType == XmlNodeType.Comment) continue;
                XmlElement window = node as XmlElement;
                if (window == null || window.Name != "window" || result.Windows.Count >= 4096) throw new InvalidDataException("Finestra del recupero non valida.");
                ProtectedWindow tracked = new ProtectedWindow { Window = new WindowItem() };
                long handle = Int64.Parse(window.GetAttribute("handle"), CultureInfo.InvariantCulture);
                tracked.Window.Handle = new IntPtr(handle);
                tracked.Window.Pid = UInt32.Parse(window.GetAttribute("pid"), CultureInfo.InvariantCulture);
                tracked.Window.Thread = UInt32.Parse(window.GetAttribute("thread"), CultureInfo.InvariantCulture);
                tracked.ProcessStartTicks = Int64.Parse(window.GetAttribute("start"), CultureInfo.InvariantCulture);
                tracked.Original = UInt32.Parse(window.GetAttribute("original"), CultureInfo.InvariantCulture);
                tracked.Uncertain = XmlConvert.ToBoolean(window.GetAttribute("uncertain"));
                if (handle <= 0 || tracked.Window.Pid == 0 || tracked.Window.Thread == 0 || tracked.ProcessStartTicks <= 0 || (tracked.Original != 0 && tracked.Original != 1)) throw new InvalidDataException("Valori del recupero non validi.");
                result.Windows.Add(tracked);
            }
            return result;
        }

        private static void WriteReady(string path)
        {
            using (Process self = Process.GetCurrentProcess()) File.WriteAllText(path, self.Id.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
