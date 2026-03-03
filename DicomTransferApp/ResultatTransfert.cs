namespace DicomTransferApp
{
    public class ResultatTransfert
    {
        public string Salle { get; set; }
        public string NomComplet { get; set; }
        public string Matricule { get; set; }
        public string DescriptionMammographie { get; set; }
        public string DateExamen { get; set; }
        public string AnneeRecherchee { get; set; }
        public bool EstAntecedent { get; set; }
        public string TypeDifference { get; set; } // null = année exacte, "ANTERIEURE" ou "POSTERIEURE"
        public string Statut { get; set; }
    }
}