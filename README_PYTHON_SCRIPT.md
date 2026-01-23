# Script de Listing des Mammographies de Dépistage

Ce script Python permet de lister les examens de mammographie de dépistage depuis un PACS en utilisant le protocole DICOM.

## Fonctionnalités

- Connexion au PACS via le protocole DICOM (C-FIND)
- Listing des examens du jour avec Study Description = "Mammographie de Depistage"
- Extraction des champs DICOM suivants :
  - **Matricule** (0010,1000 - OtherPatientIDs)
  - **PatientName** (0010,0010)
  - **StudyDate** (0008,0020)
  - **PerformingPhysicianName** (0008,1050)
  - **OperatorsName** (0008,1070)
  - **AccessionNumber** (0008,0050)
  - **SourceApplicationEntityTitle** (0002,0016) *
- Mapping automatique du SourceApplicationEntityTitle :
  - ZKPRISTINA → ZK
  - HKPRISTINA → HK
  - MCCO_MG1 → Cloche d'Or
- Export des résultats en CSV ou JSON

\* **Note importante** : Le champ SourceApplicationEntityTitle (0002,0016) fait partie du File Meta Information et n'est généralement **pas disponible** dans les requêtes C-FIND au niveau Study. Pour l'obtenir, il faudrait faire un C-MOVE ou C-GET pour récupérer les instances DICOM complètes. Le champ sera probablement vide dans les résultats.

## Installation

### 1. Installer Python 3.x

Assurez-vous d'avoir Python 3.7 ou supérieur installé :

```bash
python --version
```

### 2. Installer les dépendances

```bash
pip install -r requirements.txt
```

Ou manuellement :

```bash
pip install pynetdicom pydicom
```

## Configuration

### Utiliser le fichier de configuration existant

Le script peut lire la configuration depuis le fichier `pacs_config.json` utilisé par l'application C# :

```json
{
  "SourceCallingAE": "MAMMOAPP1",
  "PACSSourceIP": "172.27.1.65",
  "PACSSourcePort": 104,
  "PACSSourceAETitle": "HRSRXEI"
}
```

### Configuration par défaut

Si aucun fichier de configuration n'existe, le script utilisera les valeurs par défaut du code C# :
- **SourceCallingAE** : MAMMOAPP1
- **PACSSourceIP** : 172.27.1.65
- **PACSSourcePort** : 104
- **PACSSourceAETitle** : HRSRXEI

## Utilisation

### Afficher l'aide

```bash
python list_mammographies.py --help
```

### Tester la connexion PACS

```bash
python list_mammographies.py --test
```

### Lister les examens du jour

```bash
python list_mammographies.py
```

### Lister les examens d'une date spécifique

```bash
python list_mammographies.py -d 20260115
```

### Lister avec affichage détaillé

```bash
python list_mammographies.py -v
```

### Exporter en CSV

```bash
python list_mammographies.py -c resultat.csv
```

### Exporter en JSON

```bash
python list_mammographies.py -j resultat.json
```

### Exporter en CSV et JSON simultanément

```bash
python list_mammographies.py -c resultat.csv -j resultat.json
```

### Utiliser un fichier de configuration personnalisé

```bash
python list_mammographies.py --config mon_pacs.json
```

### Activer les logs de debug

Pour déboguer les communications DICOM :

```bash
python list_mammographies.py --debug
```

## Exemples de sortie

### Affichage console

```
=== TEST DE CONNEXION PACS ===
AE Title local: MAMMOAPP1
PACS: 172.27.1.65:104
AE Title PACS: HRSRXEI
✓ Test de connexion PACS réussi (C-ECHO Success)

=== RECHERCHE DES MAMMOGRAPHIES DE DÉPISTAGE ===
Date: 2026-01-23
✓ Association établie avec le PACS

✓ 3 examen(s) trouvé(s)

=== RÉSUMÉ ===
Nombre d'examens: 3

Liste des patients:
  1. DOE^JANE - Matricule: 1980010112345 - Date: 23/01/2026
  2. SMITH^JOHN - Matricule: 1975050698765 - Date: 23/01/2026
  3. MARTIN^MARIE - Matricule: 1990120454321 - Date: 23/01/2026

✓ Résultats exportés dans resultat.csv
✓ Résultats exportés dans resultat.json
```

### Format CSV

```csv
Matricule,PatientName,StudyDate,PerformingPhysicianName,OperatorsName,AccessionNumber,SourceApplicationEntityTitle
1980010112345,DOE^JANE,23/01/2026,Dr. Martin,Tech1,ACC123456,ZK
1975050698765,SMITH^JOHN,23/01/2026,Dr. Durand,Tech2,ACC123457,HK
1990120454321,MARTIN^MARIE,23/01/2026,Dr. Bernard,Tech3,ACC123458,Cloche d'Or
```

### Format JSON

```json
[
  {
    "Matricule": "1980010112345",
    "PatientName": "DOE^JANE",
    "StudyDate": "20260123",
    "StudyDate_Formatted": "23/01/2026",
    "PerformingPhysicianName": "Dr. Martin",
    "OperatorsName": "Tech1",
    "AccessionNumber": "ACC123456",
    "SourceApplicationEntityTitle_Raw": "ZKPRISTINA",
    "SourceApplicationEntityTitle": "ZK",
    "StudyInstanceUID": "1.2.840.113619.2.xxx",
    "StudyDescription": "Mammographie de Depistage",
    "ModalitiesInStudy": "MG"
  },
  ...
]
```

## Différences avec l'application C#

### Ce qui est identique
- Utilisation du même protocole DICOM (C-FIND)
- Même configuration de connexion PACS
- Même critères de recherche (StudyDescription = "Mammographie de Depistage")

### Ce qui est différent
- **Pas de transfert d'images** : Le script liste uniquement les examens, il ne fait pas de C-MOVE
- **Recherche simplifiée** : Recherche uniquement par date et description, pas de recherche multi-critères comme dans le code C#
- **Pas de validation d'identité** : Le script ne valide pas l'identité du patient (nom, date de naissance)
- **SourceApplicationEntityTitle limité** : Ce champ est rarement disponible dans les C-FIND au niveau Study

## Limitations connues

### SourceApplicationEntityTitle (0002,0016)

Ce champ fait partie du **File Meta Information** et n'est **pas inclus** dans le dataset principal DICOM. Les requêtes C-FIND au niveau Study ne retournent généralement pas cette information.

### Solutions possibles

1. **C-MOVE puis lecture du fichier** : Faire un C-MOVE pour récupérer les instances DICOM complètes, puis lire le File Meta Information
2. **C-GET** : Similaire à C-MOVE mais récupère directement les instances
3. **Requête au niveau Instance** : Faire des C-FIND au niveau IMAGE pour chaque étude, puis lire le champ (très lent)

### Script étendu pour récupérer le SourceApplicationEntityTitle

Si vous avez vraiment besoin de ce champ, il faudra :
1. Faire d'abord un C-FIND pour lister les études
2. Pour chaque étude, faire un C-MOVE ou C-GET pour récupérer au moins une instance
3. Lire le File Meta Information de l'instance récupérée

Cela nécessiterait un script plus complexe avec un SCP (Service Class Provider) pour recevoir les images.

## Dépannage

### Erreur de connexion

```
✗ Impossible d'établir l'association avec le PACS
```

**Solutions** :
1. Vérifier que le PACS est accessible (ping)
2. Vérifier les paramètres de configuration (IP, port, AE Title)
3. Vérifier que votre AE Title est autorisé sur le PACS
4. Vérifier les règles de firewall

### Aucun résultat trouvé

```
✓ 0 examen(s) trouvé(s)
```

**Causes possibles** :
1. Aucun examen avec la description exacte "Mammographie de Depistage" ce jour-là
2. La description exacte est différente (espaces, accents, majuscules)
3. Utiliser l'option `--debug` pour voir les requêtes DICOM

### Modifier la recherche

Pour chercher toutes les mammographies (pas seulement de dépistage), modifiez la ligne 91 du script :

```python
# Au lieu de :
ds.StudyDescription = 'Mammographie de Depistage'

# Utiliser un wildcard :
ds.StudyDescription = 'Mammographie*'  # ou ''
```

## Support

Pour toute question ou problème, consultez la documentation de pynetdicom :
- Documentation : https://pydicom.github.io/pynetdicom/
- GitHub : https://github.com/pydicom/pynetdicom

## Licence

Ce script est basé sur le code de DicomTransferApp et suit la même licence.
