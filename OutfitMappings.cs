using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace outfitToggler
{
    [XmlRoot("OutfitMappings")]
    public class OutfitMappingsDocument
    {
        [XmlArray("Components")]
        [XmlArrayItem("Component")]
        public List<OutfitComponentXml> Components { get; set; } = new();
    }

    public class OutfitComponentXml
    {
        [XmlAttribute("name")]
        public string Name { get; set; } = string.Empty;

        [XmlArray("Male")]
        [XmlArrayItem("Entry")]
        public List<OutfitMapEntryXml> Male { get; set; } = new();

        [XmlArray("Female")]
        [XmlArrayItem("Entry")]
        public List<OutfitMapEntryXml> Female { get; set; } = new();
    }

    public class OutfitMapEntryXml
    {
        [XmlAttribute("on")]
        public int On { get; set; }

        [XmlAttribute("off")]
        public int Off { get; set; }
    }

    public class OutfitComponentMappings
    {
        public Dictionary<int, int> Male { get; set; } = new();
        public Dictionary<int, int> Female { get; set; } = new();
    }

    public class OutfitMappingsData
    {
        public OutfitComponentMappings Gloves { get; set; } = new();
        public OutfitComponentMappings Bags { get; set; } = new();
        public OutfitComponentMappings Jackets { get; set; } = new();
        public OutfitComponentMappings Visor { get; set; } = new();
        public OutfitComponentMappings Hair { get; set; } = new();
    }

    internal static class OutfitMappingsLoader
    {
        private static readonly string[] RequiredComponents =
        {
            "Gloves",
            "Bags",
            "Jackets",
            "Visor",
            "Hair"
        };

        public static OutfitMappingsData Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Mapping XML not found: {path}");

            var serializer = new XmlSerializer(typeof(OutfitMappingsDocument));
            using var stream = File.OpenRead(path);

            if (serializer.Deserialize(stream) is not OutfitMappingsDocument document)
                throw new InvalidDataException("Could not deserialize outfit mapping XML.");

            var componentMap = document.Components
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (string required in RequiredComponents)
            {
                if (!componentMap.ContainsKey(required))
                    throw new InvalidDataException($"Missing required component mapping: {required}");
            }

            return new OutfitMappingsData
            {
                Gloves = ToRuntime(componentMap["Gloves"]),
                Bags = ToRuntime(componentMap["Bags"]),
                Jackets = ToRuntime(componentMap["Jackets"]),
                Visor = ToRuntime(componentMap["Visor"]),
                Hair = ToRuntime(componentMap["Hair"])
            };
        }

        private static OutfitComponentMappings ToRuntime(OutfitComponentXml source)
        {
            var result = new OutfitComponentMappings();

            foreach (var entry in source.Male)
                result.Male[entry.On - 1] = entry.Off - 1;

            foreach (var entry in source.Female)
                result.Female[entry.On - 1] = entry.Off - 1;

            return result;
        }
    }
}
