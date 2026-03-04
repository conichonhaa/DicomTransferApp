namespace MammoListeApp
{
    public class MammographieEntry
    {
        public string Salle { get; set; }
        public string NomPatient { get; set; }
        public string PatientID { get; set; }
        public string DateNaissanceRaw { get; set; }
        public string HeureExamen { get; set; }
        public string Description { get; set; }
        public string StudyUID { get; set; }
        public string SourceAETitle { get; set; }
        public string AccessionNumber { get; set; }

        public string DateNaissanceFormatee => FormatDate(DateNaissanceRaw);
        public string HeureFormatee => FormatHeure(HeureExamen);

        private static string FormatDate(string dicomDate)
        {
            if (string.IsNullOrEmpty(dicomDate) || dicomDate.Length < 8) return "-";
            return $"{dicomDate[6..8]}/{dicomDate[4..6]}/{dicomDate[0..4]}";
        }

        private static string FormatHeure(string dicomTime)
        {
            if (string.IsNullOrEmpty(dicomTime) || dicomTime.Length < 4) return "-";
            return $"{dicomTime[0..2]}:{dicomTime[2..4]}";
        }
    }
}
