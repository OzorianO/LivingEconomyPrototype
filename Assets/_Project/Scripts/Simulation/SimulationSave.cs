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
        public List<SavedAccount> Accounts = new List<SavedAccount>();
        public List<SavedBusiness> Businesses = new List<SavedBusiness>();
        public List<SavedJob> Jobs = new List<SavedJob>();
        public List<SavedEntry> Ledger = new List<SavedEntry>();
    }
    [Serializable]
    public sealed class SavedAccount
    {
        public string Id, Name;
        public int Profession, Grain, Bread, Hunger;
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
}
