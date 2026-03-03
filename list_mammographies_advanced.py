#!/usr/bin/env python3
"""
Script AVANCÉ pour lister les examens de mammographie de dépistage du jour depuis un PACS
avec récupération du SourceApplicationEntityTitle (0002,0016) via C-GET

Ce script est plus complexe car il récupère les instances DICOM pour lire le File Meta Information.

Dépendances:
    pip install pynetdicom pydicom
"""

import sys
import os
import tempfile
import shutil
from datetime import datetime
from pynetdicom import AE, StoragePresentationContexts, debug_logger
from pynetdicom.sop_class import (
    StudyRootQueryRetrieveInformationModelFind,
    StudyRootQueryRetrieveInformationModelGet
)
from pydicom.dataset import Dataset
import pydicom
import csv
import json

# Réutiliser la classe de configuration
from list_mammographies import ConfigurationPACS, mapper_source_ae_title, format_date, tester_connexion_pacs


class DicomStorageSCP:
    """Gestionnaire pour recevoir les instances DICOM via C-GET"""

    def __init__(self, output_dir):
        self.output_dir = output_dir
        self.received_files = []

    def handle_store(self, event):
        """Callback appelé quand une instance DICOM est reçue"""
        ds = event.dataset
        ds.file_meta = event.file_meta

        # Générer un nom de fichier unique
        filename = f"{ds.SOPInstanceUID}.dcm"
        filepath = os.path.join(self.output_dir, filename)

        # Sauvegarder l'instance
        ds.save_as(filepath, write_like_original=False)
        self.received_files.append(filepath)

        return 0x0000  # Success


def lister_mammographies_avec_source_ae(config, date_str=None, verbose=False, keep_files=False):
    """
    Liste tous les examens de mammographie de dépistage pour une date donnée
    avec récupération du SourceApplicationEntityTitle via C-GET

    Args:
        config: Configuration PACS
        date_str: Date au format YYYYMMDD (par défaut: aujourd'hui)
        verbose: Afficher les logs détaillés
        keep_files: Conserver les fichiers DICOM téléchargés

    Returns:
        Liste de dictionnaires contenant les informations des examens
    """
    if date_str is None:
        date_str = datetime.now().strftime('%Y%m%d')

    print(f"\n=== RECHERCHE DES MAMMOGRAPHIES DE DÉPISTAGE (MODE AVANCÉ) ===")
    print(f"Date: {date_str[:4]}-{date_str[4:6]}-{date_str[6:]}")
    print("⚠ Ce mode récupère les instances DICOM pour lire le File Meta Information")

    # Créer un répertoire temporaire pour stocker les instances
    temp_dir = tempfile.mkdtemp(prefix='dicom_')
    if verbose:
        print(f"Répertoire temporaire: {temp_dir}")

    try:
        # ÉTAPE 1: Rechercher les études avec C-FIND
        examens = rechercher_etudes(config, date_str, verbose)

        if not examens:
            print("✓ Aucun examen trouvé")
            return []

        print(f"✓ {len(examens)} étude(s) trouvée(s)")

        # ÉTAPE 2: Pour chaque étude, récupérer une instance pour lire le SourceAE
        print("\n=== RÉCUPÉRATION DES INSTANCES POUR LIRE LE SOURCE AE ===")

        for i, examen in enumerate(examens, 1):
            study_uid = examen['StudyInstanceUID']
            print(f"\nÉtude {i}/{len(examens)}: {examen['PatientName']}")

            source_ae = recuperer_source_ae_title(
                config,
                study_uid,
                temp_dir,
                verbose
            )

            examen['SourceApplicationEntityTitle_Raw'] = source_ae
            examen['SourceApplicationEntityTitle'] = mapper_source_ae_title(source_ae)

            if verbose:
                print(f"  SourceApplicationEntityTitle: {source_ae} → {examen['SourceApplicationEntityTitle']}")

        return examens

    finally:
        # Nettoyer le répertoire temporaire
        if not keep_files:
            shutil.rmtree(temp_dir, ignore_errors=True)
            if verbose:
                print(f"\n✓ Répertoire temporaire nettoyé")
        else:
            print(f"\n✓ Fichiers DICOM conservés dans: {temp_dir}")


def rechercher_etudes(config, date_str, verbose=False):
    """
    Recherche les études de mammographie de dépistage avec C-FIND
    """
    ae = AE(ae_title=config.source_calling_ae)
    ae.add_requested_context(StudyRootQueryRetrieveInformationModelFind)

    ds = Dataset()
    ds.QueryRetrieveLevel = 'STUDY'
    ds.StudyDate = date_str
    ds.StudyDescription = 'Mammographie de Depistage'

    # Champs à récupérer
    ds.PatientID = ''
    ds.OtherPatientIDs = ''
    ds.PatientName = ''
    ds.AccessionNumber = ''
    ds.PerformingPhysicianName = ''
    ds.OperatorsName = ''
    ds.StudyInstanceUID = ''
    ds.StudyDescription = ''
    ds.ModalitiesInStudy = ''

    examens = []

    try:
        assoc = ae.associate(
            config.pacs_source_ip,
            config.pacs_source_port,
            ae_title=config.pacs_source_ae_title
        )

        if assoc.is_established:
            responses = assoc.send_c_find(ds, StudyRootQueryRetrieveInformationModelFind)

            for (status, identifier) in responses:
                if status and status.Status in (0xFF00, 0xFF01):  # Pending
                    if identifier:
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
                        examens.append(examen)

            assoc.release()

    except Exception as e:
        print(f"✗ Erreur lors de la recherche C-FIND: {e}")

    return examens


def recuperer_source_ae_title(config, study_uid, output_dir, verbose=False):
    """
    Récupère le SourceApplicationEntityTitle d'une étude en téléchargeant une instance via C-GET

    Args:
        config: Configuration PACS
        study_uid: UID de l'étude
        output_dir: Répertoire où sauvegarder les instances
        verbose: Affichage détaillé

    Returns:
        str: SourceApplicationEntityTitle ou chaîne vide
    """
    # Créer l'Application Entity avec support du storage
    ae = AE(ae_title=config.source_calling_ae)

    # Ajouter le contexte pour C-GET
    ae.add_requested_context(StudyRootQueryRetrieveInformationModelGet)

    # Ajouter TOUS les contextes de storage pour pouvoir recevoir n'importe quel type d'image
    for context in StoragePresentationContexts:
        ae.add_requested_context(context.abstract_syntax)

    # Créer le gestionnaire de stockage
    storage_handler = DicomStorageSCP(output_dir)

    # Dataset pour la requête C-GET (au niveau IMAGE pour limiter le téléchargement)
    ds = Dataset()
    ds.QueryRetrieveLevel = 'IMAGE'
    ds.StudyInstanceUID = study_uid
    ds.SeriesInstanceUID = ''  # Récupérer seulement la première série
    ds.SOPInstanceUID = ''

    source_ae = ''

    try:
        assoc = ae.associate(
            config.pacs_source_ip,
            config.pacs_source_port,
            ae_title=config.pacs_source_ae_title,
            evt_handlers=[
                (pynetdicom.evt.EVT_C_STORE, storage_handler.handle_store)
            ]
        )

        if assoc.is_established:
            if verbose:
                print(f"  Récupération d'une instance via C-GET...")

            # MÉTHODE 1: Essayer C-GET au niveau STUDY (plus simple)
            ds_study = Dataset()
            ds_study.QueryRetrieveLevel = 'STUDY'
            ds_study.StudyInstanceUID = study_uid

            # Récupérer seulement une instance (limiter le trafic)
            # Note: Le PACS peut ne pas supporter la limitation, il enverra peut-être tout
            responses = assoc.send_c_get(ds_study, StudyRootQueryRetrieveInformationModelGet)

            instance_count = 0
            for (status, identifier) in responses:
                if status and status.Status in (0xFF00, 0xFF01):  # Pending
                    instance_count += 1
                    # Arrêter après la première instance reçue
                    if instance_count >= 1:
                        # Note: On ne peut pas vraiment "annuler" un C-GET en cours
                        # Le PACS va continuer à envoyer, mais on peut fermer l'association
                        break

            assoc.release()

            # Lire le File Meta Information de la première instance reçue
            if storage_handler.received_files:
                first_file = storage_handler.received_files[0]
                try:
                    dcm = pydicom.dcmread(first_file)
                    if hasattr(dcm.file_meta, 'SourceApplicationEntityTitle'):
                        source_ae = dcm.file_meta.SourceApplicationEntityTitle
                        if verbose:
                            print(f"  ✓ SourceAE trouvé: {source_ae}")
                    else:
                        if verbose:
                            print(f"  ⚠ SourceApplicationEntityTitle non présent dans le File Meta")
                except Exception as e:
                    if verbose:
                        print(f"  ✗ Erreur lecture DICOM: {e}")

                # Nettoyer les fichiers reçus
                for f in storage_handler.received_files:
                    try:
                        os.remove(f)
                    except:
                        pass
            else:
                if verbose:
                    print(f"  ⚠ Aucune instance reçue")

        else:
            print(f"  ✗ Impossible d'établir l'association pour C-GET")

    except Exception as e:
        print(f"  ✗ Erreur lors du C-GET: {e}")
        if verbose:
            import traceback
            traceback.print_exc()

    return source_ae


def exporter_csv(examens, fichier_sortie='mammographies_depistage_advanced.csv'):
    """Exporte les résultats dans un fichier CSV"""
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
                examen_copy = examen.copy()
                examen_copy['StudyDate'] = format_date(examen['StudyDate'])
                writer.writerow(examen_copy)

        print(f"✓ Résultats exportés dans {fichier_sortie}")

    except Exception as e:
        print(f"✗ Erreur lors de l'export CSV: {e}")


def exporter_json(examens, fichier_sortie='mammographies_depistage_advanced.json'):
    """Exporte les résultats dans un fichier JSON"""
    if not examens:
        print("Aucun examen à exporter")
        return

    try:
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


def main():
    """Fonction principale"""
    import argparse

    parser = argparse.ArgumentParser(
        description='Lister les mammographies de dépistage avec SourceApplicationEntityTitle (mode avancé)',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
ATTENTION: Ce script récupère des instances DICOM via C-GET pour lire le File Meta Information.
Cela peut générer un trafic réseau important. Utilisez-le avec précaution.

Le script tente de limiter le téléchargement à une seule instance par étude, mais certains
PACS peuvent ne pas supporter cette limitation et envoyer toutes les instances.

Exemples:
    # Lister les examens du jour avec SourceAE
    python list_mammographies_advanced.py

    # Lister avec export CSV
    python list_mammographies_advanced.py -c resultat.csv

    # Conserver les fichiers DICOM téléchargés
    python list_mammographies_advanced.py --keep-files
        """
    )

    parser.add_argument('-d', '--date', type=str, help='Date de recherche (YYYYMMDD)')
    parser.add_argument('-v', '--verbose', action='store_true', help='Affichage détaillé')
    parser.add_argument('-c', '--csv', type=str, nargs='?', const='mammographies_depistage_advanced.csv', help='Exporter en CSV')
    parser.add_argument('-j', '--json', type=str, nargs='?', const='mammographies_depistage_advanced.json', help='Exporter en JSON')
    parser.add_argument('--test', action='store_true', help='Tester la connexion PACS uniquement')
    parser.add_argument('--config', type=str, default='pacs_config.json', help='Fichier de configuration')
    parser.add_argument('--debug', action='store_true', help='Activer les logs de debug pynetdicom')
    parser.add_argument('--keep-files', action='store_true', help='Conserver les fichiers DICOM téléchargés')

    args = parser.parse_args()

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

    # Avertissement
    print("\n⚠ ATTENTION ⚠")
    print("Ce script va récupérer des instances DICOM via C-GET.")
    print("Cela peut générer un trafic réseau important.")
    response = input("Continuer? (o/N): ")
    if response.lower() not in ['o', 'oui', 'y', 'yes']:
        print("Abandon.")
        return 0

    # Lister les examens avec récupération du SourceAE
    examens = lister_mammographies_avec_source_ae(
        config,
        args.date,
        args.verbose,
        args.keep_files
    )

    # Afficher un résumé
    if examens:
        print(f"\n=== RÉSUMÉ ===")
        print(f"Nombre d'examens: {len(examens)}")

        if not args.verbose:
            print("\nListe des patients:")
            for i, examen in enumerate(examens, 1):
                source_info = f" - Source: {examen['SourceApplicationEntityTitle']}" if examen['SourceApplicationEntityTitle'] else ""
                print(f"  {i}. {examen['PatientName']} - Matricule: {examen['Matricule']} - Date: {format_date(examen['StudyDate'])}{source_info}")

    # Exporter si demandé
    if args.csv:
        exporter_csv(examens, args.csv)

    if args.json:
        exporter_json(examens, args.json)

    return 0


if __name__ == '__main__':
    # Import nécessaire pour le handler
    import pynetdicom.evt

    sys.exit(main())
