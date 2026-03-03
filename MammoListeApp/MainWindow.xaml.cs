using ClosedXML.Excel;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MammoListeApp
{
    public partial class MainWindow : Window
    {
        // Mapping AE Title → Nom salle affiché
        private static readonly Dictionary<string, string> _salleMapping = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ZKPRISTINA", "ZK" },
            { "HKPRISTINA", "HK" },
            { "MCCO_MG1",   "Cloche d'Or" },
        };

        private ConfigurationPACS _config;
        public ObservableCollection<MammographieEntry> Resultats { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            dgResultats.ItemsSource = Resultats;
            dpDate.SelectedDate = DateTime.Today;

            btnRechercher.Click   += async (_, _) => await Rechercher();
            btnExportExcel.Click  += BtnExportExcel_Click;
            btnConfig.Click       += BtnConfig_Click;

            _config = ConfigurationPACS.Charger();
            Log("Application démarrée. Sélectionnez une date et cliquez sur Rechercher.");
        }

        // ─── Logging ────────────────────────────────────────────────────────────

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
                txtLog.ScrollToEnd();
            });
        }

        private void SetStatus(string msg)
        {
            Dispatcher.Invoke(() => txtStatus.Text = msg);
        }

        // ─── Config PACS ─────────────────────────────────────────────────────────

        private void BtnConfig_Click(object sender, RoutedEventArgs e)
        {
            var win = new ConfigWindow(_config);
            if (win.ShowDialog() == true)
            {
                _config = ConfigurationPACS.Charger();
                Log("Configuration PACS rechargée.");
            }
        }

        // ─── Recherche DICOM ─────────────────────────────────────────────────────

        private string GetSalleFiltre()
        {
            if (cboSalle.SelectedItem is ComboBoxItem item)
                return item.Tag?.ToString() ?? "";
            return "";
        }

        private async Task Rechercher()
        {
            if (dpDate.SelectedDate == null)
            {
                MessageBox.Show("Veuillez sélectionner une date.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string dateStr     = dpDate.SelectedDate.Value.ToString("yyyyMMdd");
            string salleFiltre = GetSalleFiltre();       // AE Title cible ("" = toutes)
            bool depistageSeul = chkDepistageSeul.IsChecked == true;

            btnRechercher.IsEnabled = false;
            SetStatus("Recherche en cours...");
            Log($"=== Recherche du {dpDate.SelectedDate.Value:dd/MM/yyyy} ===");
            if (!string.IsNullOrEmpty(salleFiltre))
                Log($"Filtre salle : {salleFiltre} ({MapperSalle(salleFiltre, salleFiltre)})");

            var resultats = new List<MammographieEntry>();

            try
            {
                // ── Test connexion ──────────────────────────────────────────────
                bool connexionOk = await TesterConnexion();
                if (!connexionOk)
                {
                    MessageBox.Show("Impossible de contacter le PACS.\nVérifiez la configuration.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // ── C-FIND ─────────────────────────────────────────────────────
                var client = DicomClientFactory.Create(
                    _config.PACSSourceIP,
                    _config.PACSSourcePort,
                    false,
                    _config.SourceCallingAE,
                    _config.PACSSourceAETitle);

                var dataset = new DicomDataset
                {
                    { DicomTag.StudyDate,         dateStr },
                    { DicomTag.PatientName,        "" },
                    { DicomTag.PatientID,          "" },
                    { DicomTag.PatientBirthDate,   "" },
                    { DicomTag.StudyInstanceUID,   "" },
                    { DicomTag.StudyDescription,   "" },
                    { DicomTag.StudyTime,          "" },
                    { DicomTag.ModalitiesInStudy,  "" },
                    { DicomTag.StationName,        "" },   // (0008,1010) - identifie la machine
                    { DicomTag.AccessionNumber,    "" },
                };

                var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study) { Dataset = dataset };

                request.OnResponseReceived += (_, res) =>
                {
                    if (!res.HasDataset || res.Status != DicomStatus.Pending) return;

                    // ── Debug : afficher ce que le PACS renvoie ────────────────
                    string[] modsDbg = Array.Empty<string>();
                    res.Dataset.TryGetValues(DicomTag.ModalitiesInStudy, out modsDbg);
                    string descDbg    = res.Dataset.GetSingleValueOrDefault(DicomTag.StudyDescription, "(vide)");
                    string stationDbg = res.Dataset.GetSingleValueOrDefault(DicomTag.StationName, "(vide)");
                    string patDbg     = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, "?");
                    Log($"  [DEBUG] Patient={patDbg} | Mods=[{string.Join(",", modsDbg ?? Array.Empty<string>())}] | Station={stationDbg} | Desc={descDbg}");

                    // ── Filtrage Modalité ──────────────────────────────────────
                    string[] mods = modsDbg;
                    bool isMG = mods?.Any(m => m.Equals("MG", StringComparison.OrdinalIgnoreCase)) == true;
                    if (!isMG) { Log($"    → ignoré (pas MG)"); return; }

                    // ── Filtrage Dépistage ─────────────────────────────────────
                    string desc = res.Dataset.GetSingleValueOrDefault(DicomTag.StudyDescription, "");
                    if (depistageSeul && !EstDepistage(desc)) { Log($"    → ignoré (pas dépistage)"); return; }

                    // ── Identification de la salle ─────────────────────────────
                    string stationName = res.Dataset.GetSingleValueOrDefault(DicomTag.StationName, "");
                    string salle       = MapperSalle(stationName, stationName);

                    // ── Filtrage par salle ─────────────────────────────────────
                    if (!string.IsNullOrEmpty(salleFiltre))
                    {
                        bool match = stationName.Equals(salleFiltre, StringComparison.OrdinalIgnoreCase);
                        if (!match) { Log($"    → ignoré (salle '{stationName}' ≠ filtre '{salleFiltre}')"); return; }
                    }

                    resultats.Add(new MammographieEntry
                    {
                        Salle            = salle,
                        NomPatient       = FormatNomDicom(res.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, "")),
                        PatientID        = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, ""),
                        DateNaissanceRaw = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientBirthDate, ""),
                        HeureExamen      = res.Dataset.GetSingleValueOrDefault(DicomTag.StudyTime, ""),
                        Description      = desc,
                        StudyUID         = res.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, ""),
                        SourceAETitle    = stationName,
                    });
                };

                await client.AddRequestAsync(request);
                await client.SendAsync();

                // ── Tri et affichage ───────────────────────────────────────────
                var tries = resultats
                    .OrderBy(r => r.Salle)
                    .ThenBy(r => r.HeureExamen)
                    .ThenBy(r => r.NomPatient)
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    Resultats.Clear();
                    foreach (var r in tries) Resultats.Add(r);
                });

                string msg = $"{tries.Count} mammographie(s) trouvée(s) pour le {dpDate.SelectedDate.Value:dd/MM/yyyy}";
                if (!string.IsNullOrEmpty(salleFiltre))
                    msg += $"  —  Salle : {MapperSalle(salleFiltre, salleFiltre)}";
                SetStatus(msg);
                Log($"✓ {tries.Count} résultat(s)");
            }
            catch (Exception ex)
            {
                Log($"ERREUR : {ex.Message}");
                MessageBox.Show($"Erreur lors de la recherche :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnRechercher.IsEnabled = true;
            }
        }

        // ─── Test connexion ──────────────────────────────────────────────────────

        private async Task<bool> TesterConnexion()
        {
            try
            {
                var client = DicomClientFactory.Create(
                    _config.PACSSourceIP, _config.PACSSourcePort,
                    false, _config.SourceCallingAE, _config.PACSSourceAETitle);

                bool ok = false;
                var echo = new DicomCEchoRequest();
                echo.OnResponseReceived += (_, res) => ok = res.Status == DicomStatus.Success;
                await client.AddRequestAsync(echo);
                await client.SendAsync();
                Log(ok ? "C-ECHO : OK" : "C-ECHO : ECHEC");
                return ok;
            }
            catch (Exception ex)
            {
                Log($"C-ECHO erreur : {ex.Message}");
                return false;
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string MapperSalle(string stationName, string fallback)
        {
            if (!string.IsNullOrEmpty(stationName) && _salleMapping.TryGetValue(stationName, out string nom))
                return nom;
            return fallback; // Retourne la valeur brute si non mappée
        }

        private static bool EstDepistage(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return false;
            return description.Equals("Mammographie de Depistage", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var normalized = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().ToLowerInvariant();
        }

        private static string FormatNomDicom(string dicomName)
        {
            // DICOM: "NOM^PRENOM" → "Nom Prenom"
            if (string.IsNullOrEmpty(dicomName)) return "";
            return dicomName.Replace('^', ' ').Trim();
        }

        // ─── Export Excel ─────────────────────────────────────────────────────────

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (Resultats.Count == 0)
            {
                MessageBox.Show("Aucun résultat à exporter.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string dateLabel = dpDate.SelectedDate?.ToString("yyyyMMdd") ?? "date";
            string salleLabel = GetSalleLabel();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter      = "Fichiers Excel (*.xlsx)|*.xlsx",
                FileName    = $"Mammographies_{dateLabel}_{salleLabel}.xlsx",
                DefaultExt  = ".xlsx",
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                using var wb   = new XLWorkbook();
                var ws = wb.Worksheets.Add("Mammographies");

                // ── Titre du rapport ───────────────────────────────────────────
                string dateFr = dpDate.SelectedDate?.ToString("dd/MM/yyyy") ?? "";
                ws.Cell(1, 1).Value = $"Mammographies de Dépistage — {dateFr}  —  {GetSalleLabel()}";
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 14;
                ws.Range(1, 1, 1, 7).Merge();

                // ── En-têtes ───────────────────────────────────────────────────
                string[] headers = { "Salle", "Patient", "Patient ID / Matricule", "Date Naissance", "Heure", "Description", "AE Source" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(2, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1565C0");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                // ── Données ────────────────────────────────────────────────────
                int row = 3;
                foreach (var r in Resultats)
                {
                    ws.Cell(row, 1).Value = r.Salle;
                    ws.Cell(row, 2).Value = r.NomPatient;
                    ws.Cell(row, 3).Value = r.PatientID;
                    ws.Cell(row, 4).Value = r.DateNaissanceFormatee;
                    ws.Cell(row, 5).Value = r.HeureFormatee;
                    ws.Cell(row, 6).Value = r.Description;
                    ws.Cell(row, 7).Value = r.SourceAETitle;

                    // Alterner la couleur de fond
                    if (row % 2 == 0)
                        ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");

                    row++;
                }

                // ── Mise en forme ──────────────────────────────────────────────
                ws.Columns().AdjustToContents();
                ws.Column(2).Width = Math.Max(ws.Column(2).Width, 22); // Patient
                ws.Column(6).Width = Math.Max(ws.Column(6).Width, 35); // Description

                // Figer la ligne d'en-tête
                ws.SheetView.FreezeRows(2);

                wb.SaveAs(dialog.FileName);
                MessageBox.Show($"Export réussi :\n{dialog.FileName}", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                Log($"Export Excel : {dialog.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'export :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"Erreur export Excel : {ex.Message}");
            }
        }

        private string GetSalleLabel()
        {
            if (cboSalle.SelectedItem is ComboBoxItem item && !string.IsNullOrEmpty(item.Tag?.ToString()))
                return item.Content?.ToString() ?? "ToutesSalles";
            return "ToutesSalles";
        }
    }
}
