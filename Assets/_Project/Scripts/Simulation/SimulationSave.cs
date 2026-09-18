using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace LivingEconomy.Simulation
{
    [Serializable]
    public sealed class SaveData
    {
        public int Version = 1;
        public long Tick, InitialMoney, Reserve;
        public int FarmYield, LastPaid, LastFed, LastBread;
        public bool AutoEmployment;
        public int LastUnpaid, LastUnreachable;
        public List<SavedAccount> Accounts = new List<SavedAccount>();
        public List<SavedBusiness> Businesses = new List<SavedBusiness>();
        public List<SavedJob> Jobs = new List<SavedJob>();
        public List<SavedEntry> Ledger = new List<SavedEntry>();
    }
    [Serializable]
    public sealed class SavedAccount
    {
        public string Id, Name;
        public int Profession, Grain, Bread, Hunger, Thirst;
        public long Money;
    }
    [Serializable]
    public sealed class SavedBusiness { public string Id, Owner; public int Capacity; public long Wage; }
    [Serializable]
    public sealed class SavedJob { public string Resident, Employer; }
    [Serializable]
    public sealed class SavedEntry
    {
        public long Sequence, Tick, Amount;
        public string Kind, From, To, Reason;
        public int Good = -1, Quantity;
        public bool Success;
    }

    public static class SimulationSave
    {
        public static string ToXml(DailySimulation simulation)
        {
            var data = simulation.Capture();
            using (var writer = new StringWriter())
            {
                new XmlSerializer(typeof(SaveData)).Serialize(writer, data);
                return writer.ToString();
            }
        }

        public static DailySimulation FromXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) throw new ArgumentException("Missing snapshot.");
            using (var text = new StringReader(xml))
            using (var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return DailySimulation.FromSave((SaveData)new XmlSerializer(typeof(SaveData)).Deserialize(reader));
        }
        public static void Write(string path, DailySimulation simulation)
        {
            var data = simulation.Capture();
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            string temporary = fullPath + ".tmp";
            try
            {
                using (var writer = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new XmlSerializer(typeof(SaveData)).Serialize(writer, data);
                    writer.Flush(true);
                }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, fullPath + ".bak");
                else File.Move(temporary, fullPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static DailySimulation Read(string path)
        {
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return DailySimulation.FromSave((SaveData)new XmlSerializer(typeof(SaveData)).Deserialize(reader));
        }
    }

    [Serializable]
    public sealed class ReloadCheckpoint
    {
        public string Xml;
        public bool Interrupted;
        public bool HasSnapshot => !string.IsNullOrWhiteSpace(Xml);

        public void Remember(DailySimulation simulation)
        {
            if (simulation.DayInProgress)
            {
                if (!HasSnapshot) throw new InvalidOperationException("Missing completed-day checkpoint.");
                Interrupted = true;
                return;
            }
            string next = SimulationSave.ToXml(simulation);
            Xml = next; Interrupted = false;
        }
        public DailySimulation Restore() => SimulationSave.FromXml(Xml);
    }
}
