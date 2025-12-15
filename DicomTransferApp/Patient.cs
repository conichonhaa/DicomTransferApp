namespace DicomTransferApp
{
    public class Patient
    {
        public string NomComplet { get; set; }
        public string Matricule { get; set; }
        public List<string> AnneesExamen { get; set; } = new List<string>();  // MODIFIÉ : Liste au lieu de string

        // Propriété de compatibilité pour le code existant
        public string AnneeExamen
        {
            get => AnneesExamen.FirstOrDefault();
            set
            {
                if (!string.IsNullOrEmpty(value) && !AnneesExamen.Contains(value))
                    AnneesExamen.Add(value);
            }
        }
    }
}