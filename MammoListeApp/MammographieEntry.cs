namespace MammoListeApp
{
    public class MammographieEntry
    {
        public string Salle { get; set; }
        public string NomPatient { get; set; }
        public string PatientID { get; set; }
        public string DateNaissanceRaw { get; set; }
        public string HeureExamen { get; set; }
        public string DateExamenRaw { get; set; }
        public string Description { get; set; }
        public string StudyUID { get; set; }
        public string SourceAETitle { get; set; }
        public string AccessionNumber { get; set; }
        public string OtherPatientIDs { get; set; }
        public string OperatorsName { get; set; }
        public string ScheduledStartTimeRaw { get; set; }   // (0040,0003) via MWL
        // Champs TEST
        public string AdmittingDateRaw   { get; set; }   // (0038,0020) STUDY
        public string AdmittingTimeRaw   { get; set; }   // (0038,0021) STUDY
        public string PerfStepDateRaw    { get; set; }   // (0040,0244) SERIES
        public string PerfStepTimeRaw    { get; set; }   // (0040,0245) SERIES

        public string DateNaissanceFormatee      => FormatDate(DateNaissanceRaw);
        public string DateExamenFormatee         => FormatDate(DateExamenRaw);
        public string HeureFormatee              => FormatHeure(HeureExamen);
        public string OperatorsNameFormatee      => OperateurMappingService.Resolve(OperatorsName);
        public string ScheduledStartTimeFormatee => FormatHeure(ScheduledStartTimeRaw);
        public string AdmittingDateFormatee  => FormatDate(AdmittingDateRaw);
        public string AdmittingTimeFormatee  => FormatHeure(AdmittingTimeRaw);
        public string PerfStepDateFormatee   => FormatDate(PerfStepDateRaw);
        public string PerfStepTimeFormatee   => FormatHeure(PerfStepTimeRaw);

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
