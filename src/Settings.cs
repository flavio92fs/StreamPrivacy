using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace StreamPrivacy
{
    internal sealed class AppSettings
    {
        internal bool ChatOnly = false;
        internal bool AutoProtect = true;
        internal bool DarkMode = true;
        internal string Language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it" ? "it" : "en";
        internal readonly List<string> ProtectedApps = new List<string>();

        internal static string NormalizeAppPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.IndexOf('"') >= 0)
                throw new ArgumentException("Percorso dell'app non valido.");
            bool driveAbsolute = path.Length >= 3 && Char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
            bool uncAbsolute = path.StartsWith("\\\\", StringComparison.Ordinal);
            if (!driveAbsolute && !uncAbsolute)
                throw new ArgumentException("Il percorso dell'app deve essere assoluto.");
            foreach (char c in path) if (Char.IsControl(c)) throw new ArgumentException("Il percorso contiene caratteri non validi.");
            string normalized = Path.GetFullPath(path);
            if (!String.Equals(Path.GetExtension(normalized), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Seleziona il file eseguibile dell'app (.exe).");
            return normalized;
        }

        internal bool ContainsApp(string path)
        {
            return ProtectedApps.Exists(delegate(string saved) { return String.Equals(saved, path, StringComparison.OrdinalIgnoreCase); });
        }

        internal void AddApp(string path)
        {
            string normalized = NormalizeAppPath(path);
            if (!ContainsApp(normalized)) ProtectedApps.Add(normalized);
        }
    }

    internal sealed class SettingStore
    {
        private readonly string path;
        internal SettingStore(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Percorso delle impostazioni mancante.");
            this.path = Path.GetFullPath(path);
        }

        internal static string DefaultPath
        {
            get {
                string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (File.Exists(Path.Combine(applicationDirectory, "portable.flag")))
                    return Path.Combine(applicationDirectory, "Data", "settings.xml");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamPrivacy", "settings.xml");
            }
        }

        internal AppSettings Load()
        {
            if (!File.Exists(path)) return new AppSettings();
            try
            {
                XmlReaderSettings readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 };
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (XmlReader reader = XmlReader.Create(stream, readerSettings)) document.Load(reader);
                XmlElement root = document.DocumentElement;
                if (root == null || root.Name != "streamPrivacy" || root.GetAttribute("version") != "1")
                    throw new InvalidDataException("Formato o versione delle impostazioni non riconosciuti.");
                AppSettings result = new AppSettings();
                result.ChatOnly = ReadBoolean(root, "chatOnly");
                result.AutoProtect = ReadBoolean(root, "autoProtect");
                result.DarkMode = root.SelectSingleNode("darkMode") == null || ReadBoolean(root, "darkMode");
                XmlNode language = root.SelectSingleNode("language");
                if (language != null) {
                    string code = language.InnerText.ToLowerInvariant();
                    if (code != "en" && code != "it") throw new InvalidDataException("Unsupported interface language.");
                    result.Language = code;
                }
                XmlNode list = root.SelectSingleNode("protectedApps");
                if (list == null) throw new InvalidDataException("L'elenco delle app salvate è mancante.");
                foreach (XmlNode node in list.ChildNodes)
                {
                    if (node.NodeType == XmlNodeType.Comment || node.NodeType == XmlNodeType.Whitespace) continue;
                    XmlElement app = node as XmlElement;
                    if (app == null || app.Name != "app") throw new InvalidDataException("Voce app non riconosciuta.");
                    result.AddApp(app.GetAttribute("path"));
                    if (result.ProtectedApps.Count > 1024) throw new InvalidDataException("Sono state salvate troppe app.");
                }
                return result;
            }
            catch (Exception error)
            {
                throw new InvalidDataException("Impossibile leggere le impostazioni salvate: " + error.Message + " Il file originale è stato conservato. Percorso: " + path, error);
            }
        }

        private static bool ReadBoolean(XmlElement root, string name)
        {
            XmlNode node = root.SelectSingleNode(name);
            bool value;
            if (node == null || !Boolean.TryParse(node.InnerText, out value))
                throw new InvalidDataException("Impostazione non valida: " + name + ".");
            return value;
        }

        internal void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (settings.Language != "en" && settings.Language != "it") throw new InvalidDataException("Unsupported interface language.");
            List<string> apps = new List<string>();
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string app in settings.ProtectedApps)
            {
                string normalized = AppSettings.NormalizeAppPath(app);
                if (unique.Add(normalized)) apps.Add(normalized);
            }
            if (apps.Count > 1024) throw new InvalidDataException("È possibile salvare al massimo 1024 app.");
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    XmlWriterSettings writerSettings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false };
                    using (XmlWriter writer = XmlWriter.Create(stream, writerSettings))
                    {
                        writer.WriteStartDocument();
                        writer.WriteStartElement("streamPrivacy"); writer.WriteAttributeString("version", "1");
                        writer.WriteElementString("chatOnly", XmlConvert.ToString(settings.ChatOnly));
                        writer.WriteElementString("autoProtect", XmlConvert.ToString(settings.AutoProtect));
                        writer.WriteElementString("darkMode", XmlConvert.ToString(settings.DarkMode));
                        writer.WriteElementString("language", settings.Language);
                        writer.WriteStartElement("protectedApps");
                        foreach (string app in apps)
                        {
                            writer.WriteStartElement("app"); writer.WriteAttributeString("path", app); writer.WriteEndElement();
                        }
                        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
                    }
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                // The randomly named temporary file belongs exclusively to this save.
                if (File.Exists(temporary)) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }
    }

    internal static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "StreamPrivacy";

        internal static string CommandForExecutable(string executable)
        {
            return "\"" + AppSettings.NormalizeAppPath(executable) + "\" --tray";
        }

        internal static bool IsOwnedCommand(string command, string executable)
        {
            if (command == null) return false;
            return String.Equals(command.Trim(), CommandForExecutable(executable), StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                string command = key == null ? null : key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                return IsOwnedCommand(command, Application.ExecutablePath);
            }
        }

        internal static void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) throw new InvalidOperationException("Impossibile aprire le impostazioni di avvio di Windows.");
                    object existing = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    // Updating an older copy is allowed; only this named app value is touched.
                    if (existing != null && !(existing is string))
                        throw new InvalidOperationException("La voce di avvio StreamPrivacy è occupata da un valore non riconosciuto.");
                    key.SetValue(ValueName, CommandForExecutable(Application.ExecutablePath), RegistryValueKind.String);
                }
            }
            else
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    string existing = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (IsOwnedCommand(existing, Application.ExecutablePath)) key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
