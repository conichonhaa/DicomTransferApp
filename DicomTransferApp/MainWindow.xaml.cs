using FellowOakDicom;
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

                                // Vérifier si c'est l'année demandée ou un antécédent
                                bool estAntecedent = false;
                                if (!string.IsNullOrEmpty(mammographie.Date) && mammographie.Date.Length >= 4)
                                {
                                    string anneeExamen = mammographie.Date.Substring(0, 4);
                                    estAntecedent = (anneeExamen != annee);

                                    if (estAntecedent)
                                    {
                                        Log($"⚠ ATTENTION: Mammographie antérieure ({anneeExamen}) - pas de mammo en {annee}");
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

                if (resultat.EstAntecedent)
                {
                    sb.AppendLine($"⚠ ATTENTION : Mammographie antérieure (pas de mammo en {resultat.AnneeRecherchee})");
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
        private async Task<Mammographie> RechercherMammographie(Patient patient)
        {
            Log($"Recherche pour matricule: {patient.Matricule}");

            var client = DicomClientFactory.Create(
                _config.PACSSourceIP,
                _config.PACSSourcePort,
                false,
                _config.SourceCallingAE,
                _config.PACSSourceAETitle
            );

            var mammographies = new List<Mammographie>();
            int foundCount = 0;
            string patientIdTrouve = null;
            string nomTrouve = null;
            string dateNaissanceTrouvee = null;

            // ═══════════════════════════════════════════════════════════════════════
            // ÉTAPE 1 : Recherche directe avec OtherPatientIDs (0010,1000)
            // ═══════════════════════════════════════════════════════════════════════
            Log($"Étape 1: Recherche avec OtherPatientIDs (0010,1000) = {patient.Matricule}");

            var requestOtherIds = new DicomCFindRequest(DicomQueryRetrieveLevel.Study)
            {
                Dataset =
        {
            { new DicomTag(0x0010, 0x1000), patient.Matricule },
            { DicomTag.PatientID, "" },
            { DicomTag.PatientName, "" },
            { DicomTag.PatientBirthDate, "" },
            { DicomTag.StudyInstanceUID, "" },
            { DicomTag.StudyDescription, "" },
            { DicomTag.StudyDate, "" },
            { DicomTag.ModalitiesInStudy, "" }
        }
            };

            requestOtherIds.OnResponseReceived += (req, res) =>
            {
                if (res.HasDataset && res.Status == DicomStatus.Pending)
                {
                    // Récupérer PatientID, Nom et Date de naissance du premier examen trouvé
                    if (patientIdTrouve == null)
                    {
                        patientIdTrouve = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, "");
                        nomTrouve = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, "");
                        dateNaissanceTrouvee = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientBirthDate, "");

                        if (!string.IsNullOrEmpty(patientIdTrouve))
                        {
                            Dispatcher.Invoke(() => Log($"✓ PatientID trouvé: {patientIdTrouve}"));
                            if (!string.IsNullOrEmpty(nomTrouve))
                            {
                                Dispatcher.Invoke(() => Log($"  Nom DICOM: {nomTrouve}"));
                            }
                            if (!string.IsNullOrEmpty(dateNaissanceTrouvee))
                            {
                                Dispatcher.Invoke(() => Log($"  Date naissance: {FormatDate(dateNaissanceTrouvee)}"));
                            }
                        }
                    }

                    AjouterSiMammographie(res.Dataset, patient, mammographies, ref foundCount);
                }
            };

            await client.AddRequestAsync(requestOtherIds);
            await client.SendAsync();

            if (foundCount > 0)
            {
                Log($"✓ {foundCount} mammographie(s) trouvée(s) avec OtherPatientIDs");
            }

            // ═══════════════════════════════════════════════════════════════════════
            // ÉTAPE 2 : Si aucune mammo trouvée mais PatientID récupéré,
            //           valider l'identité avec nom ET date de naissance
            // ═══════════════════════════════════════════════════════════════════════
            if (mammographies.Count == 0 && !string.IsNullOrEmpty(patientIdTrouve))
            {
                Log($"Étape 2: Validation identité et recherche avec PatientID = {patientIdTrouve}");

                // Extraire la date de naissance du matricule
                string dateNaissanceMatricule = ExtraireDateNaissance(patient.Matricule);

                bool identiteValidee = ValiderIdentitePatient(
                    patient.NomComplet,
                    nomTrouve,
                    dateNaissanceMatricule,
                    dateNaissanceTrouvee
                );

                if (!identiteValidee)
                {
                    Log($"⚠ ATTENTION: Identité ne correspond PAS!");
                    Log($"  → Abandon de la recherche pour éviter une erreur de patient");
                    return null;
                }

                Log($"✓ Identité validée, recherche des mammographies...");

                // Continuer la recherche avec le PatientID validé
                var client2 = DicomClientFactory.Create(
                    _config.PACSSourceIP,
                    _config.PACSSourcePort,
                    false,
                    _config.SourceCallingAE,
                    _config.PACSSourceAETitle
                );

                var requestByPatientId = new DicomCFindRequest(DicomQueryRetrieveLevel.Study)
                {
                    Dataset =
            {
                { DicomTag.PatientID, patientIdTrouve },
                { DicomTag.PatientName, "" },
                { DicomTag.PatientBirthDate, "" },
                { DicomTag.StudyInstanceUID, "" },
                { DicomTag.StudyDescription, "" },
                { DicomTag.StudyDate, "" },
                { DicomTag.ModalitiesInStudy, "" }
            }
                };

                int foundCountEtape2 = 0;

                requestByPatientId.OnResponseReceived += (req, res) =>
                {
                    if (res.HasDataset && res.Status == DicomStatus.Pending)
                    {
                        AjouterSiMammographie(res.Dataset, patient, mammographies, ref foundCountEtape2);
                    }
                };

                await client2.AddRequestAsync(requestByPatientId);
                await client2.SendAsync();

                if (foundCountEtape2 > 0)
                {
                    Log($"✓ {foundCountEtape2} mammographie(s) trouvée(s) avec PatientID");
                }
                else
                {
                    Log($"ℹ Patient identifié (PatientID: {patientIdTrouve}) mais aucune mammographie dans le PACS");
                }
            }

            // ═══════════════════════════════════════════════════════════════════════
            // ÉTAPE 3 : Si toujours rien, essayer recherche directe sur PatientID
            //           (cas où le matricule = PatientID)
            // ═══════════════════════════════════════════════════════════════════════
            if (mammographies.Count == 0 && patientIdTrouve == null)
            {
                Log($"Étape 3: Recherche directe avec PatientID (0010,0020) = {patient.Matricule}");

                var client3 = DicomClientFactory.Create(
                    _config.PACSSourceIP,
                    _config.PACSSourcePort,
                    false,
                    _config.SourceCallingAE,
                    _config.PACSSourceAETitle
                );

                var requestDirectPatientId = new DicomCFindRequest(DicomQueryRetrieveLevel.Study)
                {
                    Dataset =
            {
                { DicomTag.PatientID, patient.Matricule },
                { DicomTag.PatientName, "" },
                { DicomTag.PatientBirthDate, "" },
                { DicomTag.StudyInstanceUID, "" },
                { DicomTag.StudyDescription, "" },
                { DicomTag.StudyDate, "" },
                { DicomTag.ModalitiesInStudy, "" }
            }
                };

                int foundCountEtape3 = 0;
                string nomPatientIdDirect = null;
                string dateNaissancePatientIdDirect = null;

                requestDirectPatientId.OnResponseReceived += (req, res) =>
                {
                    if (res.HasDataset && res.Status == DicomStatus.Pending)
                    {
                        if (nomPatientIdDirect == null)
                        {
                            nomPatientIdDirect = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, "");
                            dateNaissancePatientIdDirect = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientBirthDate, "");

                            if (!string.IsNullOrEmpty(nomPatientIdDirect))
                            {
                                Dispatcher.Invoke(() => Log($"  Nom DICOM: {nomPatientIdDirect}"));
                            }
                            if (!string.IsNullOrEmpty(dateNaissancePatientIdDirect))
                            {
                                Dispatcher.Invoke(() => Log($"  Date naissance: {FormatDate(dateNaissancePatientIdDirect)}"));
                            }

                            string dateNaissanceMatricule = ExtraireDateNaissance(patient.Matricule);
                            bool identiteOk = ValiderIdentitePatient(
                                patient.NomComplet,
                                nomPatientIdDirect,
                                dateNaissanceMatricule,
                                dateNaissancePatientIdDirect
                            );

                            if (identiteOk)
                            {
                                Dispatcher.Invoke(() => Log($"✓ Identité validée"));
                            }
                            else
                            {
                                Dispatcher.Invoke(() => Log($"⚠ Identité différente"));
                            }
                        }

                        AjouterSiMammographie(res.Dataset, patient, mammographies, ref foundCountEtape3);
                    }
                };

                await client3.AddRequestAsync(requestDirectPatientId);
                await client3.SendAsync();

                if (foundCountEtape3 > 0)
                {
                    Log($"✓ {foundCountEtape3} mammographie(s) trouvée(s) avec PatientID direct");
                }
            }

            // ═══════════════════════════════════════════════════════════════════════
            // ÉTAPE 4 : Recherche par date de naissance (dernier recours)
            // ═══════════════════════════════════════════════════════════════════════
            if (mammographies.Count == 0 && patientIdTrouve == null && patient.Matricule.Length >= 8)
            {
                string dateNaissance = ExtraireDateNaissance(patient.Matricule);
                Log($"Étape 4: Recherche par date de naissance (0010,0030) = {FormatDate(dateNaissance)}");

                var client4 = DicomClientFactory.Create(
                    _config.PACSSourceIP,
                    _config.PACSSourcePort,
                    false,
                    _config.SourceCallingAE,
                    _config.PACSSourceAETitle
                );

                var requestByBirthDate = new DicomCFindRequest(DicomQueryRetrieveLevel.Study)
                {
                    Dataset =
            {
                { DicomTag.PatientBirthDate, dateNaissance },
                { DicomTag.PatientID, "" },
                { DicomTag.PatientName, "" },
                { DicomTag.StudyInstanceUID, "" },
                { DicomTag.StudyDescription, "" },
                { DicomTag.StudyDate, "" },
                { DicomTag.ModalitiesInStudy, "" }
            }
                };

                int foundCountEtape4 = 0;
                var patientsCorrespondants = new Dictionary<string, (string nom, int count)>();

                requestByBirthDate.OnResponseReceived += (req, res) =>
                {
                    if (res.HasDataset && res.Status == DicomStatus.Pending)
                    {
                        string nom = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, "");
                        string pid = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, "");

                        // Vérifier si le nom correspond
                        if (NomPrenomMatch(patient.NomComplet, nom))
                        {
                            if (!patientsCorrespondants.ContainsKey(pid))
                            {
                                patientsCorrespondants[pid] = (nom, 0);
                                Dispatcher.Invoke(() => Log($"  → Patient trouvé: {nom} (ID: {pid})"));
                            }

                            AjouterSiMammographie(res.Dataset, patient, mammographies, ref foundCountEtape4);
                        }
                    }
                };

                await client4.AddRequestAsync(requestByBirthDate);
                await client4.SendAsync();

                if (foundCountEtape4 > 0)
                {
                    Log($"✓ {foundCountEtape4} mammographie(s) trouvée(s) par date de naissance");
                }
                else if (patientsCorrespondants.Count > 0)
                {
                    Log($"ℹ Patient(s) trouvé(s) par date de naissance mais sans mammographie");
                }
            }

            // ═══════════════════════════════════════════════════════════════════════
            // FILTRAGE DES EXAMENS FUTURS
            // ═══════════════════════════════════════════════════════════════════════
            if (int.TryParse(patient.AnneeExamen, out int anneeRecherchee))
            {
                var mammographiesValides = mammographies.Where(m =>
                {
                    if (string.IsNullOrEmpty(m.Date) || m.Date.Length < 4)
                        return true;

                    if (int.TryParse(m.Date.Substring(0, 4), out int anneeMammo))
                    {
                        return anneeMammo <= anneeRecherchee;
                    }
                    return true;
                }).ToList();

                int mammographiesFutures = mammographies.Count - mammographiesValides.Count;
                if (mammographiesFutures > 0)
                {
                    Log($"⚠ {mammographiesFutures} mammographie(s) future(s) écartée(s)");
                }

                mammographies = mammographiesValides;
            }

            Log($"✓ Total: {mammographies.Count} mammographie(s) valide(s)");

            // ═══════════════════════════════════════════════════════════════════════
            // SÉLECTION DE LA MEILLEURE MAMMOGRAPHIE
            // ═══════════════════════════════════════════════════════════════════════
            var meilleure = mammographies
                .OrderByDescending(m => m.Priorite)
                .ThenByDescending(m =>
                {
                    if (DateTime.TryParseExact(m.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                        return dt;
                    return DateTime.MinValue;
                })
                .FirstOrDefault();

            if (meilleure != null)
            {
                Log($"✓ Meilleure correspondance: {meilleure.Description} (priorité: {meilleure.Priorite})");
            }
            else
            {
                Log($"✗ Aucune mammographie disponible pour ce patient");
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

            string[] modalities = Array.Empty<string>();
            if (dataset.TryGetValues(DicomTag.ModalitiesInStudy, out string[] mods))
                modalities = mods;

            bool isMG = modalities.Any(m => m.Equals("MG", StringComparison.InvariantCultureIgnoreCase));
            bool isMammo = EstMammographie(studyDesc);

            if (isMG && isMammo)
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

                    if (res.Status == DicomStatus.Success)
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
                    Log("✓ Transfert terminé (C-MOVE Success)");
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
                    // Extraire tout ce qui est après le matricule
                    var resteLigne = trimmed.Substring(matriculeInLine.Index + 13).Trim();

                    // Extraire le nom (tout ce qui est avant le matricule)
                    var nom = trimmed.Substring(0, matriculeInLine.Index).Trim();
                    var matricule = matriculeInLine.Groups[1].Value;

                    var patient = new Patient
                    {
                        NomComplet = nom,
                        Matricule = matricule
                    };

                    // Chercher toutes les dates exactes (dd.mm.yyyy ou dd/mm/yyyy)
                    var datesExactes = Regex.Matches(resteLigne, @"\b(\d{1,2})[./](\d{1,2})[./](\d{4})\b");

                    if (datesExactes.Count > 0)
                    {
                        // On a des dates exactes
                        foreach (Match dateMatch in datesExactes)
                        {
                            string jour = dateMatch.Groups[1].Value.PadLeft(2, '0');
                            string mois = dateMatch.Groups[2].Value.PadLeft(2, '0');
                            string annee = dateMatch.Groups[3].Value;

                            patient.AnneesExamen.Add(annee);
                            Log($"  Date exacte détectée: {jour}/{mois}/{annee} → Année: {annee}");
                        }
                    }
                    else
                    {
                        // Pas de dates exactes, chercher les années (avec ou sans RX)
                        // Formats: 23, 2023, 23rx, 23 rx, 2023rx, 2023 rx
                        var anneesMatches = Regex.Matches(resteLigne, @"(\d{2}|\d{4})(?:\s*[Rr][Xx])?");

                        foreach (Match match in anneesMatches)
                        {
                            string anneeStr = match.Groups[1].Value;

                            // Vérifier que c'est bien une année (pas juste un nombre au hasard)
                            if (anneeStr.Length == 2)
                            {
                                // 2 chiffres: considérer comme année 20xx
                                int anneeNum = int.Parse(anneeStr);
                                if (anneeNum >= 0 && anneeNum <= 99) // Années de 2000 à 2099
                                {
                                    string annee = "20" + anneeStr;
                                    patient.AnneesExamen.Add(annee);
                                }
                            }
                            else if (anneeStr.Length == 4)
                            {
                                // 4 chiffres: année complète
                                int anneeNum = int.Parse(anneeStr);
                                if (anneeNum >= 1900 && anneeNum <= 2100) // Années valides
                                {
                                    patient.AnneesExamen.Add(anneeStr);
                                }
                            }
                        }
                    }

                    if (patient.AnneesExamen.Count > 0)
                    {
                        patients.Add(patient);
                        Log($"  Patient ligne unique: {nom} / {matricule} / Années: {string.Join(", ", patient.AnneesExamen)}");
                    }

                    continue;
                }

                // CAS 2: Format multiligne (nom sur une ligne, matricule sur la suivante, années/dates sur les suivantes)

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

                // Détection d'une date exacte seule (dd.mm.yyyy ou dd/mm/yyyy)
                var dateExacteSeule = Regex.Match(trimmed, @"^(\d{1,2})[./](\d{1,2})[./](\d{4})$");
                if (dateExacteSeule.Success)
                {
                    if (patientCourant != null && hasMatricule)
                    {
                        string jour = dateExacteSeule.Groups[1].Value.PadLeft(2, '0');
                        string mois = dateExacteSeule.Groups[2].Value.PadLeft(2, '0');
                        string annee = dateExacteSeule.Groups[3].Value;

                        patientCourant.AnneesExamen.Add(annee);
                        Log($"  Date exacte ajoutée: {jour}/{mois}/{annee} → Année: {annee}");
                        continue;
                    }
                }

                // Détection de l'année seule (2 ou 4 chiffres, avec ou sans RX)
                // Formats: 23, 2023, 23rx, 23 rx, 2023rx, 2023 rx
                var anneeSeuleMatch = Regex.Match(trimmed, @"^(\d{2}|\d{4})(?:\s*[Rr][Xx])?$", RegexOptions.IgnoreCase);
                if (anneeSeuleMatch.Success)
                {
                    if (patientCourant != null && hasMatricule)
                    {
                        string anneeStr = anneeSeuleMatch.Groups[1].Value;
                        string annee;

                        if (anneeStr.Length == 2)
                        {
                            int anneeNum = int.Parse(anneeStr);
                            if (anneeNum >= 0 && anneeNum <= 99)
                            {
                                annee = "20" + anneeStr;
                                patientCourant.AnneesExamen.Add(annee);
                                Log($"  Année ajoutée: {annee}");
                            }
                        }
                        else if (anneeStr.Length == 4)
                        {
                            int anneeNum = int.Parse(anneeStr);
                            if (anneeNum >= 1900 && anneeNum <= 2100)
                            {
                                patientCourant.AnneesExamen.Add(anneeStr);
                                Log($"  Année ajoutée: {anneeStr}");
                            }
                        }

                        continue;
                    }
                }

                // Si ce n'est ni un matricule ni une année/date, vérifier si c'est la fin d'un patient multiligne
                if (patientCourant != null && hasMatricule && patientCourant.AnneesExamen.Count > 0)
                {
                    // On a un patient complet, l'ajouter avant de commencer un nouveau
                    patients.Add(patientCourant);
                    Log($"  Patient multiligne ajouté: {patientCourant.NomComplet} / {patientCourant.Matricule} / Années: {string.Join(", ", patientCourant.AnneesExamen)}");
                    patientCourant = null;
                    hasMatricule = false;
                }

                // Si ce n'est ni un matricule ni une année/date, c'est un nom
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
    }
}