
using System;
using System.IO;
using System.Text.Json;

namespace DicomTransferApp
{
    public class ConfigurationPACS
    {
        // Paramètres de votre PC (source)
        public string SourceIP { get; set; } = "172.27.2.97";
        public int SourcePort { get; set; } = 104;
        public string SourceCallingAE { get; set; } = "MAMMOAPP1";
        public string SourceAETitle { get; set; } = "MAMMOAPP1";

        // Paramètres du PACS source
        public string PACSSourceIP { get; set; } = "172.27.1.65";
        public int PACSSourcePort { get; set; } = 104;
        public string PACSSourceAETitle { get; set; } = "HRSRXEI";

        // Paramètres du PACS distant (destination)
        public string DestinationIP { get; set; } = "10.111.134.11";
        public int DestinationPort { get; set; } = 11112;
        public string DestinationAETitle { get; set; } = "ITTM35201";

        // AE Title pour usurpation si nécessaire
        public string UsurperAETitle { get; set; } = "MAMMOAPP1";

        // Paramètres Oracle RIS
        public string RISHost { get; set; } = "ripr01dbpr.int.hs.lu";
        public int RISPort { get; set; } = 1543;
        public string RISServiceName { get; set; } = "ripr01";
        public string RISUserId { get; set; } = "sysadm";
        public string RISPassword { get; set; } = "sysadm";

        // Méthode de sauvegarde
        public void Sauvegarder()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(this, options);
            File.WriteAllText(ObtenirCheminConfig(), jsonString);
        }

        // Méthode de chargement
        public static ConfigurationPACS Charger()
        {
            string cheminConfig = ObtenirCheminConfig();
            if (!File.Exists(cheminConfig))
            {
                // Configuration par défaut
                var configDefaut = new ConfigurationPACS();
                configDefaut.Sauvegarder();
                return configDefaut;
            }

            string jsonString = File.ReadAllText(cheminConfig);
            return JsonSerializer.Deserialize<ConfigurationPACS>(jsonString);
        }

        // Méthode pour obtenir le chemin de configuration
        public static string ObtenirCheminConfig()
        {
            string dossierApplication = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(dossierApplication, "pacs_config.json");
        }
    }
}
