using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MammoListeApp
{
    /// <summary>
    /// Gère le mapping valeur DICOM brute → nom d'opérateur affiché.
    /// Le fichier operateurs_mapping.json est dans le répertoire de l'application.
    /// </summary>
    public static class OperateurMappingService
    {
        public static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "operateurs_mapping.json");

        private static Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

        static OperateurMappingService() => Load();

        public static void Load()
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            _map = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }

        public static void Save(Dictionary<string, string> map)
        {
            _map = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Retourne le nom affiché pour une valeur DICOM brute.</summary>
        public static string Resolve(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            return _map.TryGetValue(raw, out var mapped) ? mapped : raw;
        }

        public static Dictionary<string, string> GetAll() => new(_map);
    }
}
