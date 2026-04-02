using ClosedXML.Excel;
using FellowOakDicom;
using Oracle.ManagedDataAccess.Client;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DicomTransferApp
{
    public partial class MainWindow : Window
    {
        private ConfigurationPACS _config;
        public ObservableCollection<ResultatTransfert> ResultatsTransfert { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            ResultatsTransfert = new ObservableCollection<ResultatTransfert>();
            listViewResultats.ItemsSource = ResultatsTransfert;

            btnConfigPACS.Click += BtnConfigPACS_Click;
            btnTransfert.Click += BtnTransfert_Click;
            btnCopierResultats.Click += BtnCopierResultats_Click;
            btnExportExcel.Click += BtnExportExcel_Click;

            _config = ConfigurationPACS.Charger();
        }

        private void Log(string message)
        {
            var logMessage = $"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nMessage: {message}\r\n{new string('-', 50)}\r\n";
            Dispatcher.Invoke(() =>
            {
                if (txtLog != null)
                {
                    txtLog.AppendText(logMessage);
                    txtLog.ScrollToEnd();
                }
            });
            System.Diagnostics.Debug.WriteLine(logMessage);
        }

        private async Task<bool> TesterConnexionPACS()
        {
            try
            {
                Log($"AE Title PACS Source: {_config.PACSSourceAETitle}");
                var client = DicomClientFactory.Create(
                    _config.PACSSourceIP,
                    _config.PACSSourcePort,
                    false,
                    _config.SourceCallingAE,
                    _config.PACSSourceAETitle
                );

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var request = new DicomCEchoRequest();

                bool success = false;
                request.OnResponseReceived += (req, res) =>
                {
                    success = res.Status == DicomStatus.Success;
                    Dispatcher.Invoke(() => Log($"C-ECHO réponse: {res.Status}"));
                };

                await client.AddRequestAsync(request);
                await client.SendAsync(cts.Token);

                Log(success ? "Test de connexion PACS réussi" : "Test de connexion PACS échoué");
                return success;
            }
            catch (Exception ex)
            {
                Log($"Erreur connexion PACS: {ex.Message}");
                return false;
            }
        }

        private void BtnConfigPACS_Click(object sender, RoutedEventArgs e)
        {
            var configWindow = new ConfigurationPACSWindow();
            if (configWindow.ShowDialog() == true)
            {
                _config = ConfigurationPACS.Charger();
                Log("Configuration PACS rechargée");
            }
        }

        private async void BtnTransfert_Click(object sender, RoutedEventArgs e)
        {
            if (_config == null)
            {
                MessageBox.Show("Configuration PACS non disponible", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Log("=== DÉBUT DU TRANSFERT ===");

            if (!await TesterConnexionPACS())
            {
                MessageBox.Show("Impossible de se connecter au PACS.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var patients = ParsePatientList(txtListePatients.Text);
            if (patients.Count == 0)
            {
                MessageBox.Show("Aucun patient détecté", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Log($"{patients.Count} patient(s) à traiter");
            ResultatsTransfert.Clear();
            btnTransfert.IsEnabled = false;

            try
            {
                foreach (var patient in patients)
                {
                    // Si plusieurs années, traiter chacune séparément
                    if (patient.AnneesExamen.Count > 1)
                    {
                        Log($"\n--- Patient: {patient.NomComplet} ({patient.AnneesExamen.Count} années demandées) ---");
                    }
                    else
                    {
                        Log($"\n--- Patient: {patient.NomComplet} ---");
                    }

                    foreach (var annee in patient.AnneesExamen)
                    {
                        // Créer un patient temporaire pour cette année spécifique
                        var patientPourAnnee = new Patient
                        {
                            NomComplet = patient.NomComplet,
                            Matricule = patient.Matricule,
                            AnneeExamen = annee
                        };

                        if (patient.AnneesExamen.Count > 1)
                        {
                            Log($"\n  >> Recherche pour l'année {annee}");
                        }

                        try
                        {
                            var mammographie = await RechercherMammographie(patientPourAnnee);

                            if (mammographie != null)
                            {
                                Log($"Mammographie trouvée: {mammographie.Description}");
                                Log($"Study UID: {mammographie.StudyUID}");

                                // Vérifier si c'est l'année demandée, un antécédent ou postérieur
                                bool estAntecedent = false;
                                string typeDifference = null;
                                if (!string.IsNullOrEmpty(mammographie.Date) && mammographie.Date.Length >= 4)
                                {
                                    string anneeExamen = mammographie.Date.Substring(0, 4);
                                    estAntecedent = (anneeExamen != annee);

                                    if (estAntecedent)
                                    {
                                        if (int.TryParse(anneeExamen, out int anneeMammo) && int.TryParse(annee, out int anneeDemandee))
                                        {
                                            if (anneeMammo < anneeDemandee)
                                            {
                                                typeDifference = "ANTERIEURE";
                                                Log($"⚠ ATTENTION: Mammographie antérieure ({anneeExamen}) - pas de mammo en {annee}");
                                            }
                                            else if (anneeMammo > anneeDemandee)
                                            {
                                                typeDifference = "POSTERIEURE";
                                                Log($"ℹ INFO: Mammographie postérieure ({anneeExamen}) - pas de mammo en {annee}");
                                            }
                                        }
                                    }
                                }

                                bool succes = await TransfererMammographie(mammographie, patientPourAnnee);

                                ResultatsTransfert.Add(new ResultatTransfert
                                {
                                    NomComplet = patient.NomComplet,
                                    Matricule = patient.Matricule,
                                    DescriptionMammographie = mammographie.Description,
                                    DateExamen = FormatDate(mammographie.Date),
                                    AnneeRecherchee = annee,
                                    EstAntecedent = estAntecedent,
                                    TypeDifference = typeDifference,
                                    Statut = succes ? "✓ Envoyé" : "✗ Échec"
                                });
                            }
                            else
                            {
                                Log($"✗ Pas de mammographie disponible pour {annee}");
                                ResultatsTransfert.Add(new ResultatTransfert
                                {
                                    NomComplet = patient.NomComplet,
                                    Matricule = patient.Matricule,
                                    DescriptionMammographie = "Pas de mammographie disponible",
                                    DateExamen = "-",
                                    AnneeRecherchee = annee,
                                    EstAntecedent = false,
                                    TypeDifference = null,
                                    Statut = "Aucune"
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"ERREUR: {ex.Message}");
                            ResultatsTransfert.Add(new ResultatTransfert
                            {
                                NomComplet = patient.NomComplet,
                                Matricule = patient.Matricule,
                                DescriptionMammographie = $"Erreur : {ex.Message}",
                                DateExamen = "-",
                                AnneeRecherchee = annee,
                                EstAntecedent = false,
                                TypeDifference = null,
                                Statut = "Erreur"
                            });
                        }
                    }
                }

                var reussis = ResultatsTransfert.Count(r => r.Statut == "✓ Envoyé");
                var echecs = ResultatsTransfert.Count(r => r.Statut == "✗ Échec" || r.Statut == "Erreur");
                var nonTrouves = ResultatsTransfert.Count(r => r.Statut == "Aucune");

                Log($"\n=== RÉSUMÉ ===");
                Log($"Réussis: {reussis}");
                Log($"Échecs: {echecs}");
                Log($"Non trouvés: {nonTrouves}");

                MessageBox.Show(
                    $"Transfert terminé\n\nRéussis : {reussis}\nÉchecs : {echecs}\nNon trouvés : {nonTrouves}",
                    "Résultat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            finally
            {
                btnTransfert.IsEnabled = true;
                Log("=== FIN DU TRANSFERT ===\n");
            }
        }

        private void BtnCopierResultats_Click(object sender, RoutedEventArgs e)
        {
            if (ResultatsTransfert.Count == 0)
            {
                MessageBox.Show("Aucun résultat à copier", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sb = new System.Text.StringBuilder();
            //sb.AppendLine("RÉSULTATS DU TRANSFERT DE MAMMOGRAPHIES");
            //sb.AppendLine($"Date : {DateTime.Now:dd/MM/yyyy HH:mm}");
            //sb.AppendLine(new string('=', 80));
            //sb.AppendLine();

            foreach (var resultat in ResultatsTransfert)
            {
                sb.AppendLine($"Patient : {resultat.NomComplet}");
                sb.AppendLine($"Matricule : {resultat.Matricule}");
                sb.AppendLine($"Année demandée : {resultat.AnneeRecherchee}");
                sb.AppendLine($"Examen : {resultat.DescriptionMammographie}");
                sb.AppendLine($"Date examen envoyé : {resultat.DateExamen}");

                if (!string.IsNullOrEmpty(resultat.TypeDifference))
                {
                    if (resultat.TypeDifference == "ANTERIEURE")
                    {
                        sb.AppendLine($"⚠ ATTENTION : Mammographie antérieure (pas de mammo en {resultat.AnneeRecherchee})");
                    }
                    else if (resultat.TypeDifference == "POSTERIEURE")
                    {
                        sb.AppendLine($"ℹ INFO : Mammographie postérieure (pas de mammo en {resultat.AnneeRecherchee})");
                    }
                }

                //sb.AppendLine($"Statut : {resultat.Statut}");
                sb.AppendLine(new string('-', 80));
            }

            var reussis = ResultatsTransfert.Count(r => r.Statut == "✓ Envoyé");
            var echecs = ResultatsTransfert.Count(r => r.Statut == "✗ Échec" || r.Statut == "Erreur");
            var nonTrouves = ResultatsTransfert.Count(r => r.Statut == "Aucune");

            sb.AppendLine();
            //sb.AppendLine("RÉSUMÉ :");
            //sb.AppendLine($"- Envoyés avec succès : {reussis}");
            //sb.AppendLine($"- Échecs : {echecs}");
            //sb.AppendLine($"- Non trouvés : {nonTrouves}");

            try
            {
                Clipboard.SetText(sb.ToString());
                MessageBox.Show("Les résultats ont été copiés dans le presse-papiers", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                Log("Résultats copiés dans le presse-papiers");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la copie : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (ResultatsTransfert.Count == 0)
            {
                MessageBox.Show("Aucun résultat à exporter.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Fichiers Excel (*.xlsx)|*.xlsx",
                FileName = $"Transferts_Mammographies_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                DefaultExt = ".xlsx"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("Résultats");

                // En-têtes
                string[] headers = { "Patient", "Matricule", "Année demandée", "Mammographie", "Date Examen", "Statut", "Remarque" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = sheet.Cell(1, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
                }

                // Données
                int row = 2;
                foreach (var r in ResultatsTransfert)
                {
                    sheet.Cell(row, 1).Value = r.NomComplet;
                    sheet.Cell(row, 2).Value = r.Matricule;
                    sheet.Cell(row, 3).Value = r.AnneeRecherchee;
                    sheet.Cell(row, 4).Value = r.DescriptionMammographie;
                    sheet.Cell(row, 5).Value = r.DateExamen;
                    sheet.Cell(row, 6).Value = r.Statut;

                    string remarque = "";
                    if (r.TypeDifference == "ANTERIEURE")
                        remarque = $"ATTENTION : Mammographie anterieure (pas de mammo en {r.AnneeRecherchee})";
                    else if (r.TypeDifference == "POSTERIEURE")
                        remarque = $"INFO : Mammographie posterieure (pas de mammo en {r.AnneeRecherchee})";
                    sheet.Cell(row, 7).Value = remarque;

                    if (r.EstAntecedent)
                        sheet.Row(row).Style.Font.FontColor = XLColor.Red;

                    row++;
                }

                sheet.Columns().AdjustToContents();

                workbook.SaveAs(dialog.FileName);
                MessageBox.Show($"Export réussi :\n{dialog.FileName}", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                Log($"Export Excel : {dialog.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'export : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"Erreur export Excel : {ex.Message}");
            }
        }

        private async Task<string> RechercherPatientCodeRIS(string matricule)
        {
            try
            {
                string dsn = $"(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST={_config.RISHost})(PORT={_config.RISPort})))(CONNECT_DATA=(SERVICE_NAME={_config.RISServiceName})))";
                string connStr = $"User Id={_config.RISUserId};Password={_config.RISPassword};Data Source={dsn}";

                using var conn = new OracleConnection(connStr);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT P_CODE FROM sysadm.patients
                                    WHERE (P_EXTRACODE = :niss1 OR P_SISCODE = :niss2)
                                    AND ROWNUM = 1";
                cmd.Parameters.Add(new OracleParameter("niss1", matricule));
                cmd.Parameters.Add(new OracleParameter("niss2", matricule));

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return result.ToString();

                return null;
            }
            catch (Exception ex)
            {
                Log($"⚠ Erreur connexion RIS Oracle: {ex.Message}");
                return null;
            }
        }

        private async Task<Mammographie> RechercherMammographie(Patient patient)
        {
            Log($"Recherche pour matricule: {patient.Matricule}");

            // ═══════════════════════════════════════════════════════════════════════
            // ÉTAPE 1 : Résolution du P_CODE dans le RIS Oracle
            // ═══════════════════════════════════════════════════════════════════════
            Log($"Interrogation RIS Oracle (P_EXTRACODE/P_SISCODE = {patient.Matricule})");
            string patientCode = await RechercherPatientCodeRIS(patient.Matricule);

            if (string.IsNullOrEmpty(patientCode))
            {
                Log($"✗ Matricule {patient.Matricule} introuvable dans le RIS");
                return null;
            }

            Log($"✓ P_CODE RIS: {patientCode}");

            // ═══════════════════════════════════════════════════════════════════════
            // ÉTAPE 2 : Recherche PACS avec PatientID = P_CODE
            // ═══════════════════════════════════════════════════════════════════════
            Log($"Recherche PACS avec PatientID = {patientCode}");

            var client = DicomClientFactory.Create(
                _config.PACSSourceIP,
                _config.PACSSourcePort,
                false,
                _config.SourceCallingAE,
                _config.PACSSourceAETitle
            );

            var mammographies = new List<Mammographie>();
            int foundCount = 0;

            var requestPacs = new DicomCFindRequest(DicomQueryRetrieveLevel.Study)
            {
                Dataset =
        {
            { DicomTag.PatientID, patientCode },
            { DicomTag.PatientName, "" },
            { DicomTag.PatientBirthDate, "" },
            { DicomTag.StudyInstanceUID, "" },
            { DicomTag.StudyDescription, "" },
            { DicomTag.StudyDate, "" },
            { DicomTag.ModalitiesInStudy, "" }
        }
            };

            requestPacs.OnResponseReceived += (req, res) =>
            {
                if (res.HasDataset && res.Status == DicomStatus.Pending)
                {
                    AjouterSiMammographie(res.Dataset, patient, mammographies, ref foundCount);
                }
            };

            await client.AddRequestAsync(requestPacs);
            await client.SendAsync();

            if (foundCount > 0)
                Log($"✓ {foundCount} mammographie(s) trouvée(s) pour PatientID {patientCode}");
            else
                Log($"ℹ Aucune mammographie dans le PACS pour PatientID {patientCode}");

            // ═══════════════════════════════════════════════════════════════════════
            // FILTRAGE DES EXAMENS FUTURS ET TROP RÉCENTS
            // - Examens dans le futur (après aujourd'hui)
            // - Examens de moins de 1 mois (probablement des erreurs de saisie)
            // ═══════════════════════════════════════════════════════════════════════
            var aujourdhui = DateTime.Today;
            var dateMinimale = aujourdhui.AddMonths(-1); // Il y a 1 mois

            bool inclureRecents = Dispatcher.Invoke(() => chkIncludeRecents.IsChecked == true);

            var mammographiesValides = mammographies.Where(m =>
            {
                if (string.IsNullOrEmpty(m.Date) || m.Date.Length < 8)
                    return true;

                if (DateTime.TryParseExact(m.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateMammo))
                {
                    // Éliminer si dans le futur
                    if (dateMammo > aujourdhui)
                        return false;

                    // Éliminer si moins de 1 mois (sauf si mode débridé activé)
                    if (!inclureRecents && dateMammo > dateMinimale)
                        return false;

                    return true;
                }
                return true;
            }).ToList();

            int mammographiesFutures = 0;
            int mammographiesTropRecentes = 0;

            foreach (var m in mammographies.Except(mammographiesValides))
            {
                if (!string.IsNullOrEmpty(m.Date) && m.Date.Length >= 8)
                {
                    if (DateTime.TryParseExact(m.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateMammo))
                    {
                        if (dateMammo > aujourdhui)
                            mammographiesFutures++;
                        else if (dateMammo > dateMinimale)
                            mammographiesTropRecentes++;
                    }
                }
            }

            if (mammographiesFutures > 0)
            {
                Log($"⚠ {mammographiesFutures} mammographie(s) dans le futur écartée(s)");
            }
            if (mammographiesTropRecentes > 0)
            {
                Log($"⚠ {mammographiesTropRecentes} mammographie(s) trop récente(s) écartée(s) (< 1 mois)");
            }

            mammographies = mammographiesValides;

            Log($"✓ Total: {mammographies.Count} mammographie(s) valide(s)");

            // ═══════════════════════════════════════════════════════════════════════
            // SÉLECTION DE LA MEILLEURE MAMMOGRAPHIE
            // ═══════════════════════════════════════════════════════════════════════
            if (mammographies.Count == 0)
            {
                Log($"✗ Aucune mammographie disponible pour ce patient");
                return null;
            }

            Mammographie meilleure = null;

            // Étape 1 : Chercher dans l'année demandée
            if (int.TryParse(patient.AnneeExamen, out int anneeRecherchee))
            {
                var mammosAnneeDemandee = mammographies.Where(m =>
                {
                    if (string.IsNullOrEmpty(m.Date) || m.Date.Length < 4)
                        return false;

                    if (int.TryParse(m.Date.Substring(0, 4), out int anneeMammo))
                    {
                        return anneeMammo == anneeRecherchee;
                    }
                    return false;
                }).ToList();

                if (mammosAnneeDemandee.Count > 0)
                {
                    Log($"✓ {mammosAnneeDemandee.Count} mammographie(s) trouvée(s) pour l'année {patient.AnneeExamen}");
                    meilleure = mammosAnneeDemandee
                        .OrderByDescending(m => m.Priorite)
                        .ThenByDescending(m =>
                        {
                            if (DateTime.TryParseExact(m.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                                return dt;
                            return DateTime.MinValue;
                        })
                        .First();

                    Log($"✓ Sélection pour {patient.AnneeExamen}: {meilleure.Description} du {FormatDate(meilleure.Date)}");
                    return meilleure;
                }
            }

            // Étape 2 : Si aucune mammo de l'année demandée, prendre la plus récente disponible
            Log($"✓ Aucune mammo en {patient.AnneeExamen}, sélection de la plus récente disponible");

            meilleure = mammographies
                .OrderByDescending(m =>
                {
                    if (DateTime.TryParseExact(m.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                        return dt;
                    return DateTime.MinValue;
                })
                .ThenByDescending(m => m.Priorite)
                .First();

            string anneeMeilleure = "";
            if (!string.IsNullOrEmpty(meilleure.Date) && meilleure.Date.Length >= 4)
            {
                anneeMeilleure = meilleure.Date.Substring(0, 4);
            }

            Log($"✓ Sélection de la plus récente: {meilleure.Description} du {FormatDate(meilleure.Date)}");

            if (!string.IsNullOrEmpty(anneeMeilleure) && int.TryParse(patient.AnneeExamen, out int annee))
            {
                if (int.TryParse(anneeMeilleure, out int anneeTrouvee))
                {
                    if (anneeTrouvee < annee)
                    {
                        Log($"  ⚠ Mammographie antérieure à {patient.AnneeExamen}");
                    }
                    else if (anneeTrouvee > annee)
                    {
                        Log($"  ℹ Mammographie postérieure à {patient.AnneeExamen} (pas de mammo en {patient.AnneeExamen})");
                    }
                }
            }

            return meilleure;
        }

        // NOUVELLE MÉTHODE : Valider l'identité du patient
        private bool ValiderIdentitePatient(string nomRecherche, string nomDicom, string dateNaissanceMatricule, string dateNaissanceDicom)
        {
            bool nomOk = false;
            bool dateOk = false;

            // Vérification du nom
            if (!string.IsNullOrEmpty(nomDicom))
            {
                nomOk = NomPrenomMatch(nomRecherche, nomDicom);
                if (nomOk)
                {
                    Log($"  ✓ Nom correspond: {nomDicom}");
                }
                else
                {
                    Log($"  ✗ Nom ne correspond PAS");
                    Log($"    Recherché: {nomRecherche}");
                    Log($"    Trouvé: {nomDicom}");
                }
            }

            // Vérification de la date de naissance
            if (!string.IsNullOrEmpty(dateNaissanceMatricule) && !string.IsNullOrEmpty(dateNaissanceDicom))
            {
                dateOk = (dateNaissanceMatricule == dateNaissanceDicom);
                if (dateOk)
                {
                    Log($"  ✓ Date naissance correspond: {FormatDate(dateNaissanceDicom)}");
                }
                else
                {
                    Log($"  ✗ Date naissance ne correspond PAS");
                    Log($"    Matricule: {FormatDate(dateNaissanceMatricule)}");
                    Log($"    DICOM: {FormatDate(dateNaissanceDicom)}");
                }
            }
            else if (!string.IsNullOrEmpty(dateNaissanceMatricule) || !string.IsNullOrEmpty(dateNaissanceDicom))
            {
                Log($"  ⚠ Date de naissance partielle (impossible de vérifier)");
                dateOk = true; // On considère OK si pas les deux dates
            }

            // Validation : au moins UN des deux critères doit correspondre
            // Idéalement les deux, mais on accepte si l'un des deux manque
            bool identiteValidee = nomOk || dateOk;

            // Si on a les deux infos, on veut les deux validées
            if (!string.IsNullOrEmpty(nomDicom) && !string.IsNullOrEmpty(dateNaissanceDicom))
            {
                identiteValidee = nomOk && dateOk;
            }

            return identiteValidee;
        }

        private void AjouterSiMammographie(DicomDataset dataset, Patient patient, List<Mammographie> list, ref int count)
        {
            string studyUID = dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
            string studyDesc = dataset.GetSingleValueOrDefault(DicomTag.StudyDescription, "");
            string studyDate = dataset.GetSingleValueOrDefault(DicomTag.StudyDate, "");

            bool isMammo = EstMammographie(studyDesc);

            if (isMammo)
            {
                count++;
                list.Add(new Mammographie
                {
                    StudyUID = studyUID,
                    Description = string.IsNullOrEmpty(studyDesc) ? "Mammographie" : studyDesc,
                    Date = studyDate,
                    Priorite = CalculerPriorite(studyDesc, studyDate, patient.AnneeExamen)
                });
            }
        }
        private string ExtraireDateNaissance(string matricule)
        {
            // Format matricule: YYYYMMDD + ...
            if (matricule.Length < 8) return null;
            string yyyy = matricule.Substring(0, 4);
            string MM = matricule.Substring(4, 2);
            string dd = matricule.Substring(6, 2);
            return $"{yyyy}{MM}{dd}";
        }
        private bool NomPrenomMatch(string nomComplet, string dicomName)
        {
            if (string.IsNullOrEmpty(dicomName)) return false;

            string nomNormalized = RemoveDiacritics(nomComplet).ToLowerInvariant().Replace(" ", "");
            string dicomNormalized = RemoveDiacritics(dicomName).ToLowerInvariant().Replace("^", "").Replace(" ", "");

            return dicomNormalized.Contains(nomNormalized) || nomNormalized.Contains(dicomNormalized);
        }
        private bool EstMammographie(string description)
        {
            if (string.IsNullOrEmpty(description)) return false;

            string desc = RemoveDiacritics(description).ToLowerInvariant();
            string[] motsCles = new[] { "mammographie", "mammo", "breast", "depistage", "dépistage" };

            bool contientMotCle = motsCles.Any(k => desc.Contains(k));
            if (contientMotCle)
                Log($"  → Mammographie détectée: {description}");

            return contientMotCle;
        }

        private string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var chars = normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
            return new string(chars.ToArray());
        }

        private int CalculerPriorite(string description, string date, string anneeRecherchee)
        {
            int priorite = 0;

            if (!string.IsNullOrEmpty(date) && date.Length >= 8 &&
                !string.IsNullOrEmpty(anneeRecherchee) &&
                int.TryParse(anneeRecherchee, out int target) &&
                int.TryParse(date.Substring(0, 4), out int studyYear))
            {
                if (studyYear == target)
                {
                    // Année exacte = priorité maximale
                    priorite = 1000;
                }
                else if (studyYear < target)
                {
                    // Année antérieure = bonne (plus c'est récent, mieux c'est)
                    priorite = 500 - (target - studyYear) * 10;
                }
                else
                {
                    // Année future = pas bon
                    priorite = -1000;
                }
            }

            var desc = description.ToLowerInvariant();
            if (desc.Contains("diagnostique")) priorite += 200;
            if (desc.Contains("dépistage") || desc.Contains("depistage")) priorite += 150;
            if (desc.Contains("bilaterale") || desc.Contains("bilatérale")) priorite += 100;

            return priorite;
        }

        private string FormatDate(string dicomDate)
        {
            if (string.IsNullOrEmpty(dicomDate) || dicomDate.Length < 8) return "-";

            if (DateTime.TryParseExact(dicomDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                return dt.ToString("dd/MM/yyyy");

            return dicomDate;
        }


        private async Task<bool> TransfererMammographie(Mammographie mammographie, Patient patient)
        {
            Log("Préparation du transfert...");
            Log($"  Study UID: {mammographie.StudyUID}");
            Log($"  Destination AE Title: {_config.DestinationAETitle}");

            try
            {
                var moveClient = DicomClientFactory.Create(
                    _config.PACSSourceIP,
                    _config.PACSSourcePort,
                    false,
                    _config.SourceCallingAE,
                    _config.PACSSourceAETitle
                );

                bool moveSuccess = false;
                int completedSubOperations = 0;
                int failedSubOperations = 0;
                int warningSubOperations = 0;
                int remainingSubOperations = 0;

                var moveRequest = new DicomCMoveRequest(_config.DestinationAETitle, mammographie.StudyUID);

                moveRequest.OnResponseReceived += (req, res) =>
                {
                    Dispatcher.Invoke(() => Log($"Réponse C-MOVE : {res.Status}"));

                    if (res.Status == DicomStatus.Success || res.Status.State == DicomState.Warning)
                    {
                        moveSuccess = true;
                    }

                    // Récupérer les sous-opérations
                    if (res.HasDataset)
                    {
                        if (res.Dataset.TryGetSingleValue(DicomTag.NumberOfCompletedSuboperations, out int completed))
                            completedSubOperations = completed;

                        if (res.Dataset.TryGetSingleValue(DicomTag.NumberOfFailedSuboperations, out int failed))
                            failedSubOperations = failed;

                        if (res.Dataset.TryGetSingleValue(DicomTag.NumberOfWarningSuboperations, out int warning))
                            warningSubOperations = warning;

                        if (res.Dataset.TryGetSingleValue(DicomTag.NumberOfRemainingSuboperations, out int remaining))
                            remainingSubOperations = remaining;

                        // Log détaillé des sous-opérations
                        if (res.Status == DicomStatus.Pending && remainingSubOperations > 0)
                        {
                            Dispatcher.Invoke(() => Log($"  Progression: {completedSubOperations} complétées, {remainingSubOperations} restantes"));
                        }
                    }
                };

                Log("Envoi de la requête C-MOVE...");
                await moveClient.AddRequestAsync(moveRequest);
                await moveClient.SendAsync();

                Log($"Sous-opérations complétées: {completedSubOperations}");

                if (failedSubOperations > 0)
                    Log($"Sous-opérations échouées: {failedSubOperations}");

                if (warningSubOperations > 0)
                    Log($"Sous-opérations avec avertissement: {warningSubOperations}");

                if (moveSuccess && completedSubOperations > 0)
                {
                    Log("✓ Transfert terminé avec succès");
                    return true;
                }
                else if (failedSubOperations > 0)
                {
                    Log("✗ Transfert échoué");
                    return false;
                }
                else if (moveSuccess)
                {
                    Log("✓ Transfert terminé (images transférées avec avertissements de coercion)");
                    return true;
                }
                else
                {
                    Log("⚠ Transfert terminé avec statut incertain");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"ERREUR lors du transfert: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }


        private List<Patient> ParsePatientList(string liste)
        {
            var patients = new List<Patient>();
            var lignes = liste.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

            Log($"Analyse de {lignes.Length} lignes");

            Patient patientCourant = null;
            bool hasMatricule = false;

            foreach (var ligne in lignes)
            {
                var trimmed = ligne.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                // CAS 1: Ligne unique contenant tout (nom + matricule + années/dates)
                var matriculeInLine = Regex.Match(trimmed, @"\b(\d{13})\b");

                if (matriculeInLine.Success)
                {
                    // Extraire le nom (tout ce qui est avant le matricule)
                    var nom = trimmed.Substring(0, matriculeInLine.Index).Trim();
                    var matricule = matriculeInLine.Groups[1].Value;

                    // Extraire tout ce qui est après le matricule
                    var resteLigne = trimmed.Substring(matriculeInLine.Index + 13).Trim();

                    var patient = new Patient
                    {
                        NomComplet = nom,
                        Matricule = matricule
                    };

                    // Extraire toutes les années du reste de la ligne
                    var anneesExtracted = ExtraireAnnees(resteLigne);
                    patient.AnneesExamen.AddRange(anneesExtracted);

                    if (patient.AnneesExamen.Count > 0)
                    {
                        patients.Add(patient);
                        Log($"  Patient ligne unique: {nom} / {matricule} / Années: {string.Join(", ", patient.AnneesExamen)}");
                    }

                    continue;
                }

                // CAS 2: Format multiligne (nom sur une ligne, matricule sur la suivante, années sur les suivantes)

                // Détection du matricule seul (13 chiffres sur toute la ligne)
                var matriculeMatch = Regex.Match(trimmed, @"^\d{13}$");
                if (matriculeMatch.Success)
                {
                    if (patientCourant != null)
                    {
                        patientCourant.Matricule = trimmed;
                        hasMatricule = true;
                        Log($"  Matricule trouvé: {trimmed}");
                    }
                    continue;
                }

                // Détection d'une ligne contenant des années/dates
                if (patientCourant != null && hasMatricule)
                {
                    var anneesExtracted = ExtraireAnnees(trimmed);
                    if (anneesExtracted.Count > 0)
                    {
                        patientCourant.AnneesExamen.AddRange(anneesExtracted);
                        Log($"  Années ajoutées: {string.Join(", ", anneesExtracted)}");
                        continue;
                    }
                }

                // Si ce n'est ni un matricule ni des années, vérifier si on doit finaliser le patient courant
                if (patientCourant != null && hasMatricule && patientCourant.AnneesExamen.Count > 0)
                {
                    // On a un patient complet, l'ajouter avant de commencer un nouveau
                    patients.Add(patientCourant);
                    Log($"  Patient multiligne ajouté: {patientCourant.NomComplet} / {patientCourant.Matricule} / Années: {string.Join(", ", patientCourant.AnneesExamen)}");
                    patientCourant = null;
                    hasMatricule = false;
                }

                // Si ce n'est ni un matricule ni des années, c'est un nom
                if (patientCourant == null)
                {
                    if (!Regex.IsMatch(trimmed, @"^\d+$"))
                    {
                        patientCourant = new Patient
                        {
                            NomComplet = trimmed
                        };
                        hasMatricule = false;
                        Log($"  Nouveau patient (multiligne): {trimmed}");
                    }
                }
            }

            // Ajouter le dernier patient si non ajouté
            if (patientCourant != null && hasMatricule && patientCourant.AnneesExamen.Count > 0)
            {
                patients.Add(patientCourant);
                Log($"  Patient multiligne ajouté: {patientCourant.NomComplet} / {patientCourant.Matricule} / Années: {string.Join(", ", patientCourant.AnneesExamen)}");
            }

            Log($"✓ Total patients parsés: {patients.Count}");

            // Afficher la liste complète avec toutes les années
            foreach (var p in patients)
            {
                Log($"  → {p.NomComplet} | {p.Matricule} | Années: {string.Join(", ", p.AnneesExamen)}");
            }

            return patients;
        }

        /// <summary>
        /// Extrait toutes les années valides d'une chaîne de caractères.
        /// Formats supportés: 23, 2023, 23+24, 2023+2024, 2023 2024, etc.
        /// Ignore le texte comme "Rx", "rx", ou tout autre mot.
        /// </summary>
        private List<string> ExtraireAnnees(string texte)
        {
            var annees = new List<string>();

            if (string.IsNullOrWhiteSpace(texte))
                return annees;

            // D'abord, chercher les dates exactes (dd.mm.yyyy ou dd/mm/yyyy)
            var datesExactes = Regex.Matches(texte, @"\b(\d{1,2})[./](\d{1,2})[./](\d{4})\b");
            if (datesExactes.Count > 0)
            {
                foreach (Match dateMatch in datesExactes)
                {
                    string annee = dateMatch.Groups[3].Value;
                    if (!annees.Contains(annee))
                    {
                        annees.Add(annee);
                        Log($"    Date exacte détectée → Année: {annee}");
                    }
                }
                return annees;
            }

            // Sinon, extraire toutes les séquences de 2 ou 4 chiffres
            // Pattern: cherche 2 ou 4 chiffres consécutifs, pas plus (pour éviter de prendre des parties de matricules)
            var matches = Regex.Matches(texte, @"(?<!\d)(\d{2}|\d{4})(?!\d)");

            foreach (Match match in matches)
            {
                string valeur = match.Groups[1].Value;

                // Vérifier le contexte: ignorer si c'est suivi ou précédé de "rx" ou "Rx"
                int position = match.Index;
                string avant = position > 0 ? texte.Substring(Math.Max(0, position - 2), Math.Min(2, position)).ToLower() : "";
                string apres = position + valeur.Length < texte.Length ?
                    texte.Substring(position + valeur.Length, Math.Min(2, texte.Length - position - valeur.Length)).ToLower() : "";

                // Si "rx" est trouvé juste avant, ignorer (ex: "rx2011" = code examen, pas une année)
                if (avant.Contains("rx"))
                    continue;

                if (valeur.Length == 2)
                {
                    // 2 chiffres: valider que c'est une année plausible (00-99)
                    if (int.TryParse(valeur, out int anneeNum) && anneeNum >= 0 && anneeNum <= 99)
                    {
                        string annee = "20" + valeur;
                        if (!annees.Contains(annee))
                        {
                            annees.Add(annee);
                        }
                    }
                }
                else if (valeur.Length == 4)
                {
                    // 4 chiffres: valider que c'est une année plausible (1900-2100)
                    if (int.TryParse(valeur, out int anneeNum) && anneeNum >= 1900 && anneeNum <= 2100)
                    {
                        if (!annees.Contains(valeur))
                        {
                            annees.Add(valeur);
                        }
                    }
                }
            }

            return annees;
        }
    }
}