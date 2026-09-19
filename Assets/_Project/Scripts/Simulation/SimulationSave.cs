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
        public SavedHeroPose HeroPose;
        public bool HeroEnabled;
    }
    [Serializable]
    public sealed class SavedHeroPose
    {
        public float X, Y, Z, FacingYaw, CameraYaw, CameraPitch = 20, CameraDistance = 6;
        public bool FirstPerson;
        public SavedHeroPose Copy() => (SavedHeroPose)MemberwiseClone();
        public void Validate()
        {
            foreach (float value in new[] { X, Y, Z, FacingYaw, CameraYaw, CameraPitch, CameraDistance })
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Non-finite hero pose.");
            if (Math.Abs(X) > 100000 || Math.Abs(Y) > 100000 || Math.Abs(Z) > 100000
                || FacingYaw < 0 || FacingYaw >= 360 || CameraYaw < 0 || CameraYaw >= 360
                || CameraPitch < -75 || CameraPitch > 75 || CameraDistance < 3 || CameraDistance > 10
                || (!FirstPerson && (CameraPitch < 8 || CameraPitch > 65)))
                throw new ArgumentException("Invalid hero pose.");
        }
    }
    [Serializable]
    public sealed class SavedPoint
    {
        public float X, Y, Z;
        public SavedPoint Copy() => (SavedPoint)MemberwiseClone();
        public void Validate()
        {
            foreach (float value in new[] { X, Y, Z })
                if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 100000)
                    throw new ArgumentException("Invalid world position.");
        }
    }
    [Serializable]
    public sealed class SavedAccount
    {
        public string Id, Name;
        public int Profession, Grain, Bread, Hunger, Thirst;
        public long Money;
        public bool IsDead;
        public SavedPoint DeathPoint;
        public List<SavedItem> Items = new List<SavedItem>();
    }
    [Serializable]
    public sealed class SavedItem { public string Id; public int Quantity; }
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
        // A renamed product reads an old save only when no current save exists.
        // It never copies, deletes, or silently bypasses a current (even invalid) save.
        public static string ResolveReadPath(string primaryPath, string legacyPath)
        {
            if (string.IsNullOrWhiteSpace(primaryPath)) throw new ArgumentException("Missing primary save path.");
            if (File.Exists(primaryPath) || string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath)) return primaryPath;
            return legacyPath;
        }

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
