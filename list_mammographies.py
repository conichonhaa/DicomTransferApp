#!/usr/bin/env python3
"""
Script pour lister les examens de mammographie de dépistage du jour depuis un PACS
Basé sur le code de DicomTransferApp

Dépendances:
    pip install pynetdicom pydicom
"""

import sys
from datetime import datetime
from pynetdicom import AE, debug_logger
from pynetdicom.sop_class import StudyRootQueryRetrieveInformationModelFind
from pydicom.dataset import Dataset
import csv
import json
import os

# Configuration PACS (à adapter selon votre configuration)
class ConfigurationPACS:
    def __init__(self, config_file='pacs_config.json'):
        """Charge la configuration depuis le fichier JSON ou utilise des valeurs par défaut"""
        self.config_file = config_file

        # Valeurs par défaut (à modifier selon votre environnement)
        self.source_calling_ae = "MAMMOAPP1"
        self.pacs_source_ip = "172.27.1.65"
        self.pacs_source_port = 104
        self.pacs_source_ae_title = "HRSRXEI"

        # Charger la configuration depuis le fichier si elle existe
        self.charger()

    def charger(self):
        """Charge la configuration depuis le fichier JSON"""
        if os.path.exists(self.config_file):
            try:
                with open(self.config_file, 'r') as f:
                    config_data = json.load(f)
                    self.source_calling_ae = config_data.get('SourceCallingAE', self.source_calling_ae)
                    self.pacs_source_ip = config_data.get('PACSSourceIP', self.pacs_source_ip)
                    self.pacs_source_port = config_data.get('PACSSourcePort', self.pacs_source_port)
                    self.pacs_source_ae_title = config_data.get('PACSSourceAETitle', self.pacs_source_ae_title)
                print(f"✓ Configuration chargée depuis {self.config_file}")
            except Exception as e:
                print(f"⚠ Erreur lors du chargement de la configuration: {e}")
                print("  Utilisation de la configuration par défaut")

    def sauvegarder(self):
        """Sauvegarde la configuration dans un fichier JSON"""
        config_data = {
            'SourceCallingAE': self.source_calling_ae,
            'PACSSourceIP': self.pacs_source_ip,
            'PACSSourcePort': self.pacs_source_port,
            'PACSSourceAETitle': self.pacs_source_ae_title
        }
        try:
            with open(self.config_file, 'w') as f:
                json.dump(config_data, f, indent=2)
            print(f"✓ Configuration sauvegardée dans {self.config_file}")
        except Exception as e:
            print(f"✗ Erreur lors de la sauvegarde: {e}")


def mapper_source_ae_title(source_ae_title):
    """
    Mappe le SourceApplicationEntityTitle selon les règles définies:
    - ZKPRISTINA -> ZK
    - HKPRISTINA -> HK
    - MCCO_MG1 -> Cloche d'Or
    """
    mapping = {
        'ZKPRISTINA': 'ZK',
        'HKPRISTINA': 'HK',
        'MCCO_MG1': "Cloche d'Or"
    }
    return mapping.get(source_ae_title, source_ae_title)


def tester_connexion_pacs(config):
    """
    Teste la connexion au PACS avec C-ECHO
    """
    print(f"\n=== TEST DE CONNEXION PACS ===")
    print(f"AE Title local: {config.source_calling_ae}")
    print(f"PACS: {config.pacs_source_ip}:{config.pacs_source_port}")
    print(f"AE Title PACS: {config.pacs_source_ae_title}")

    ae = AE(ae_title=config.source_calling_ae)
    ae.add_requested_context('1.2.840.10008.1.1')  # Verification SOP Class

    try:
        assoc = ae.associate(
            config.pacs_source_ip,
            config.pacs_source_port,
            ae_title=config.pacs_source_ae_title
        )

        if assoc.is_established:
            status = assoc.send_c_echo()
            assoc.release()

            if status and status.Status == 0x0000:
                print("✓ Test de connexion PACS réussi (C-ECHO Success)")
                return True
            else:
                print(f"✗ Test de connexion PACS échoué (Status: {status})")
                return False
        else:
            print("✗ Impossible d'établir l'association avec le PACS")
            return False

    except Exception as e:
        print(f"✗ Erreur de connexion PACS: {e}")
        return False


def lister_mammographies_depistage(config, date_str=None, verbose=False):
    """
    Liste tous les examens de mammographie de dépistage pour une date donnée

    Args:
        config: Configuration PACS
        date_str: Date au format YYYYMMDD (par défaut: aujourd'hui)
        verbose: Afficher les logs détaillés

    Returns:
        Liste de dictionnaires contenant les informations des examens
    """
    if date_str is None:
        date_str = datetime.now().strftime('%Y%m%d')

    print(f"\n=== RECHERCHE DES MAMMOGRAPHIES DE DÉPISTAGE ===")
    print(f"Date: {date_str[:4]}-{date_str[4:6]}-{date_str[6:]}")

    # Créer l'Application Entity
    ae = AE(ae_title=config.source_calling_ae)
    ae.add_requested_context(StudyRootQueryRetrieveInformationModelFind)

    # Créer le dataset de requête C-FIND au niveau Study
    ds = Dataset()
    ds.QueryRetrieveLevel = 'STUDY'

    # Critères de recherche
    ds.StudyDate = date_str  # Date du jour
    ds.StudyDescription = 'Mammographie de Depistage'  # Description exacte

    # Champs à récupérer
    ds.PatientID = ''                      # 0010,0020
    ds.OtherPatientIDs = ''                # 0010,1000 - Matricule
    ds.PatientName = ''                    # 0010,0010
    ds.AccessionNumber = ''                # 0008,0050
    ds.PerformingPhysicianName = ''        # 0008,1050
    ds.OperatorsName = ''                  # 0008,1070
    ds.StudyInstanceUID = ''               # 0008,0020
    ds.ModalitiesInStudy = ''              # 0008,0061

    # Note: SourceApplicationEntityTitle (0002,0016) fait partie du File Meta Information
    # et n'est généralement pas disponible dans les requêtes C-FIND au niveau Study.
    # Il faudrait faire un C-MOVE ou C-GET pour récupérer les instances et lire ce champ.
    # Pour l'instant, nous allons essayer de le demander, mais il sera probablement vide.

    examens = []

    try:
        # Établir l'association avec le PACS
        assoc = ae.associate(
            config.pacs_source_ip,
            config.pacs_source_port,
            ae_title=config.pacs_source_ae_title
        )

        if assoc.is_established:
            print("✓ Association établie avec le PACS")

            # Envoyer la requête C-FIND
            responses = assoc.send_c_find(ds, StudyRootQueryRetrieveInformationModelFind)

            count = 0
            for (status, identifier) in responses:
                if status and status.Status in (0xFF00, 0xFF01):  # Pending
                    count += 1

                    if identifier:
                        # Extraire les informations
                        examen = {
                            'Matricule': identifier.get('OtherPatientIDs', ''),
                            'PatientName': str(identifier.get('PatientName', '')),
                            'StudyDate': identifier.get('StudyDate', ''),
                            'PerformingPhysicianName': str(identifier.get('PerformingPhysicianName', '')),
                            'OperatorsName': str(identifier.get('OperatorsName', '')),
                            'AccessionNumber': identifier.get('AccessionNumber', ''),
                            'StudyInstanceUID': identifier.get('StudyInstanceUID', ''),
                            'StudyDescription': identifier.get('StudyDescription', ''),
                            'ModalitiesInStudy': identifier.get('ModalitiesInStudy', '')
                        }

                        # Tenter de récupérer le SourceApplicationEntityTitle
                        # (probablement vide car pas disponible au niveau Study)
                        source_ae = ''
                        if hasattr(identifier, 'SourceApplicationEntityTitle'):
                            source_ae = identifier.SourceApplicationEntityTitle
                        examen['SourceApplicationEntityTitle_Raw'] = source_ae
                        examen['SourceApplicationEntityTitle'] = mapper_source_ae_title(source_ae)

                        examens.append(examen)

                        if verbose:
                            print(f"\n  Examen {count}:")
                            print(f"    Matricule: {examen['Matricule']}")
                            print(f"    Nom: {examen['PatientName']}")
                            print(f"    Date: {examen['StudyDate']}")
                            print(f"    Médecin: {examen['PerformingPhysicianName']}")
                            print(f"    Opérateur: {examen['OperatorsName']}")
                            print(f"    Accession Number: {examen['AccessionNumber']}")

            # Libérer l'association
            assoc.release()

            print(f"\n✓ {len(examens)} examen(s) trouvé(s)")

        else:
            print("✗ Impossible d'établir l'association avec le PACS")

    except Exception as e:
        print(f"✗ Erreur lors de la recherche: {e}")
        import traceback
        traceback.print_exc()

    return examens


def format_date(date_str):
    """Formate une date DICOM (YYYYMMDD) en format lisible (DD/MM/YYYY)"""
    if len(date_str) == 8:
        return f"{date_str[6:]}/{date_str[4:6]}/{date_str[:4]}"
    return date_str


def exporter_csv(examens, fichier_sortie='mammographies_depistage.csv'):
    """
    Exporte les résultats dans un fichier CSV
    """
    if not examens:
        print("Aucun examen à exporter")
        return

    fieldnames = [
        'Matricule',
        'PatientName',
        'StudyDate',
        'PerformingPhysicianName',
        'OperatorsName',
        'AccessionNumber',
        'SourceApplicationEntityTitle'
    ]

    try:
        with open(fichier_sortie, 'w', newline='', encoding='utf-8') as csvfile:
            writer = csv.DictWriter(csvfile, fieldnames=fieldnames, extrasaction='ignore')
            writer.writeheader()

            for examen in examens:
                # Formater la date pour le CSV
                examen_copy = examen.copy()
                examen_copy['StudyDate'] = format_date(examen['StudyDate'])
                writer.writerow(examen_copy)

        print(f"✓ Résultats exportés dans {fichier_sortie}")

    except Exception as e:
        print(f"✗ Erreur lors de l'export CSV: {e}")


def exporter_json(examens, fichier_sortie='mammographies_depistage.json'):
    """
    Exporte les résultats dans un fichier JSON
    """
    if not examens:
        print("Aucun examen à exporter")
        return

    try:
        # Formater les dates
        examens_formatte = []
        for examen in examens:
            examen_copy = examen.copy()
            examen_copy['StudyDate_Formatted'] = format_date(examen['StudyDate'])
            examens_formatte.append(examen_copy)

        with open(fichier_sortie, 'w', encoding='utf-8') as jsonfile:
            json.dump(examens_formatte, jsonfile, indent=2, ensure_ascii=False)

        print(f"✓ Résultats exportés dans {fichier_sortie}")

    except Exception as e:
        print(f"✗ Erreur lors de l'export JSON: {e}")


def afficher_aide():
    """Affiche l'aide du script"""
    print("""
Usage: python list_mammographies.py [OPTIONS]

Options:
    -h, --help              Afficher cette aide
    -d, --date YYYYMMDD     Date de recherche (défaut: aujourd'hui)
    -v, --verbose           Affichage détaillé
    -c, --csv FICHIER       Exporter en CSV (défaut: mammographies_depistage.csv)
    -j, --json FICHIER      Exporter en JSON (défaut: mammographies_depistage.json)
    --test                  Tester la connexion PACS uniquement
    --config FICHIER        Fichier de configuration JSON (défaut: pacs_config.json)

Exemples:
    # Lister les examens du jour
    python list_mammographies.py

    # Lister les examens d'une date spécifique
    python list_mammographies.py -d 20260115

    # Lister avec export CSV et JSON
    python list_mammographies.py -c resultat.csv -j resultat.json

    # Tester la connexion PACS
    python list_mammographies.py --test

    # Utiliser un fichier de configuration personnalisé
    python list_mammographies.py --config mon_pacs.json

Configuration:
    Le script cherche un fichier pacs_config.json dans le répertoire courant.
    Si le fichier n'existe pas, il utilise les valeurs par défaut du code C#.

    Structure du fichier pacs_config.json:
    {
        "SourceCallingAE": "MAMMOAPP1",
        "PACSSourceIP": "172.27.1.65",
        "PACSSourcePort": 104,
        "PACSSourceAETitle": "HRSRXEI"
    }

Note sur SourceApplicationEntityTitle (0002,0016):
    Ce champ fait partie du File Meta Information et n'est généralement pas
    disponible dans les requêtes C-FIND au niveau Study. Pour l'obtenir,
    il faudrait faire un C-MOVE ou C-GET pour récupérer les instances DICOM.
    Le champ sera probablement vide dans les résultats.
""")


def main():
    """Fonction principale"""
    import argparse

    parser = argparse.ArgumentParser(description='Lister les mammographies de dépistage du jour', add_help=False)
    parser.add_argument('-h', '--help', action='store_true', help='Afficher l\'aide')
    parser.add_argument('-d', '--date', type=str, help='Date de recherche (YYYYMMDD)')
    parser.add_argument('-v', '--verbose', action='store_true', help='Affichage détaillé')
    parser.add_argument('-c', '--csv', type=str, nargs='?', const='mammographies_depistage.csv', help='Exporter en CSV')
    parser.add_argument('-j', '--json', type=str, nargs='?', const='mammographies_depistage.json', help='Exporter en JSON')
    parser.add_argument('--test', action='store_true', help='Tester la connexion PACS uniquement')
    parser.add_argument('--config', type=str, default='pacs_config.json', help='Fichier de configuration')
    parser.add_argument('--debug', action='store_true', help='Activer les logs de debug pynetdicom')

    args = parser.parse_args()

    if args.help:
        afficher_aide()
        return 0

    # Activer les logs de debug si demandé
    if args.debug:
        debug_logger()

    # Charger la configuration
    config = ConfigurationPACS(args.config)

    # Test de connexion uniquement
    if args.test:
        success = tester_connexion_pacs(config)
        return 0 if success else 1

    # Tester la connexion d'abord
    if not tester_connexion_pacs(config):
        print("\n✗ Impossible de continuer sans connexion PACS")
        return 1

    # Lister les examens
    examens = lister_mammographies_depistage(config, args.date, args.verbose)

    # Afficher un résumé
    if examens:
        print(f"\n=== RÉSUMÉ ===")
        print(f"Nombre d'examens: {len(examens)}")

        if not args.verbose:
            print("\nListe des patients:")
            for i, examen in enumerate(examens, 1):
                print(f"  {i}. {examen['PatientName']} - Matricule: {examen['Matricule']} - Date: {format_date(examen['StudyDate'])}")

    # Exporter si demandé
    if args.csv:
        exporter_csv(examens, args.csv)

    if args.json:
        exporter_json(examens, args.json)

    return 0


if __name__ == '__main__':
    sys.exit(main())
