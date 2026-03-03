using System;
using System.IO;
using System.Text.Json;

namespace MammoListeApp
{
    public class ConfigurationPACS
    {
        public string SourceIP { get; set; } = "172.27.2.97";
        public int SourcePort { get; set; } = 104;
        public string SourceCallingAE { get; set; } = "MAMMOAPP1";
        public string SourceAETitle { get; set; } = "MAMMOAPP1";

        public string PACSSourceIP { get; set; } = "172.27.1.65";
        public int PACSSourcePort { get; set; } = 104;
        public string PACSSourceAETitle { get; set; } = "HRSRXEI";

        public void Sauvegarder()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ObtenirCheminConfig(), JsonSerializer.Serialize(this, options));
        }

        public static ConfigurationPACS Charger()
        {
            string chemin = ObtenirCheminConfig();
            if (!File.Exists(chemin))
            {
                var defaut = new ConfigurationPACS();
                defaut.Sauvegarder();
                return defaut;
            }
            return JsonSerializer.Deserialize<ConfigurationPACS>(File.ReadAllText(chemin));
        }

        public static string ObtenirCheminConfig()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pacs_config.json");
        }
    }
}
