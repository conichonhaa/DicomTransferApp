# Exemples d'utilisation des scripts Python

## Configuration rapide

### 1. Installer les dépendances

```bash
pip install -r requirements.txt
```

### 2. Créer le fichier de configuration

Le script peut utiliser le fichier `pacs_config.json` existant de l'application C#, ou vous pouvez en créer un nouveau :

```json
{
  "SourceCallingAE": "MAMMOAPP1",
  "PACSSourceIP": "172.27.1.65",
  "PACSSourcePort": 104,
  "PACSSourceAETitle": "HRSRXEI"
}
```

## Script de base (list_mammographies.py)

### Tester la connexion

```bash
python list_mammographies.py --test
```

**Sortie attendue :**
```
=== TEST DE CONNEXION PACS ===
AE Title local: MAMMOAPP1
PACS: 172.27.1.65:104
AE Title PACS: HRSRXEI
✓ Test de connexion PACS réussi (C-ECHO Success)
```

### Lister les examens du jour

```bash
python list_mammographies.py
```

**Sortie :**
```
=== TEST DE CONNEXION PACS ===
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
```

### Lister avec détails

```bash
python list_mammographies.py -v
```

### Lister une date spécifique

```bash
python list_mammographies.py -d 20260115
```

### Exporter en CSV

```bash
python list_mammographies.py -c examens_du_jour.csv
```

**Contenu du CSV :**
```csv
Matricule,PatientName,StudyDate,PerformingPhysicianName,OperatorsName,AccessionNumber,SourceApplicationEntityTitle
1980010112345,DOE^JANE,23/01/2026,Dr. Martin,Tech1,ACC123456,
1975050698765,SMITH^JOHN,23/01/2026,Dr. Durand,Tech2,ACC123457,
1990120454321,MARTIN^MARIE,23/01/2026,Dr. Bernard,Tech3,ACC123458,
```

### Exporter en JSON

```bash
python list_mammographies.py -j examens_du_jour.json
```

**Contenu du JSON :**
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
    "SourceApplicationEntityTitle_Raw": "",
    "SourceApplicationEntityTitle": "",
    "StudyInstanceUID": "1.2.840.113619.2.xxx",
    "StudyDescription": "Mammographie de Depistage",
    "ModalitiesInStudy": "MG"
  }
]
```

### Exporter en CSV et JSON simultanément

```bash
python list_mammographies.py -c resultats.csv -j resultats.json
```

## Script avancé (list_mammographies_advanced.py)

⚠️ **ATTENTION** : Ce script récupère des instances DICOM pour lire le champ SourceApplicationEntityTitle. Cela peut générer un trafic réseau important !

### Utilisation

```bash
python list_mammographies_advanced.py
```

Le script demandera confirmation avant de télécharger les instances :

```
⚠ ATTENTION ⚠
Ce script va récupérer des instances DICOM via C-GET.
Cela peut générer un trafic réseau important.
Continuer? (o/N):
```

### Avec export

```bash
python list_mammographies_advanced.py -c examens_avec_source.csv
```

### Conserver les fichiers DICOM téléchargés

```bash
python list_mammographies_advanced.py --keep-files
```

**Sortie :**
```
=== RÉCUPÉRATION DES INSTANCES POUR LIRE LE SOURCE AE ===

Étude 1/3: DOE^JANE
  Récupération d'une instance via C-GET...
  ✓ SourceAE trouvé: ZKPRISTINA

Étude 2/3: SMITH^JOHN
  Récupération d'une instance via C-GET...
  ✓ SourceAE trouvé: HKPRISTINA

Étude 3/3: MARTIN^MARIE
  Récupération d'une instance via C-GET...
  ✓ SourceAE trouvé: MCCO_MG1

=== RÉSUMÉ ===
Nombre d'examens: 3

Liste des patients:
  1. DOE^JANE - Matricule: 1980010112345 - Date: 23/01/2026 - Source: ZK
  2. SMITH^JOHN - Matricule: 1975050698765 - Date: 23/01/2026 - Source: HK
  3. MARTIN^MARIE - Matricule: 1990120454321 - Date: 23/01/2026 - Source: Cloche d'Or
```

## Mapping du SourceApplicationEntityTitle

Le mapping automatique est appliqué :

| Valeur DICOM | Valeur mappée |
|--------------|---------------|
| ZKPRISTINA   | ZK            |
| HKPRISTINA   | HK            |
| MCCO_MG1     | Cloche d'Or   |

## Intégration dans un workflow

### Script bash pour automatiser

```bash
#!/bin/bash
# extract_mammographies_daily.sh

DATE=$(date +%Y%m%d)
OUTPUT_DIR="resultats_$(date +%Y%m%d)"

mkdir -p "$OUTPUT_DIR"

echo "Extraction des mammographies pour le $DATE"

python list_mammographies.py -d "$DATE" \
    -c "$OUTPUT_DIR/mammographies_$DATE.csv" \
    -j "$OUTPUT_DIR/mammographies_$DATE.json"

echo "Résultats sauvegardés dans $OUTPUT_DIR"
```

### Tâche cron quotidienne

```cron
# Lancer tous les jours à 19h00
0 19 * * * cd /path/to/DicomTransferApp && ./extract_mammographies_daily.sh
```

### Import dans Python

```python
from list_mammographies import ConfigurationPACS, lister_mammographies_depistage

# Charger la configuration
config = ConfigurationPACS('pacs_config.json')

# Lister les examens
examens = lister_mammographies_depistage(config, date_str='20260123')

# Traiter les résultats
for examen in examens:
    print(f"Patient: {examen['PatientName']}")
    print(f"Matricule: {examen['Matricule']}")
    print(f"Date: {examen['StudyDate']}")
    print("---")
```

## Dépannage

### Erreur: pynetdicom non trouvé

```bash
pip install pynetdicom pydicom
```

### Erreur de connexion

1. Vérifier que le PACS est accessible :
   ```bash
   ping 172.27.1.65
   ```

2. Vérifier les ports :
   ```bash
   telnet 172.27.1.65 104
   ```

3. Activer le mode debug :
   ```bash
   python list_mammographies.py --debug
   ```

### Aucun résultat

La description exacte doit être "Mammographie de Depistage". Si vous n'obtenez aucun résultat, modifiez le script ligne 91 :

```python
# Remplacer :
ds.StudyDescription = 'Mammographie de Depistage'

# Par un wildcard :
ds.StudyDescription = 'Mammographie*'
```

Ou rechercher toutes les descriptions :
```python
ds.StudyDescription = ''
```

### Modifier la recherche pour toutes les mammographies

Éditez `list_mammographies.py` ligne 91-92 :

```python
# Ligne 91 - Enlever le filtre de date pour chercher toutes les dates
ds.StudyDate = ''  # Au lieu de date_str

# Ligne 92 - Chercher toutes les mammographies
ds.StudyDescription = 'Mammographie*'  # Wildcard
```

## Performance

### Script de base (C-FIND seulement)
- **Rapide** : ~1-2 secondes pour lister 10 examens
- **Trafic réseau** : Minimal (<100 KB)
- **Limitation** : SourceApplicationEntityTitle non disponible

### Script avancé (C-GET)
- **Lent** : ~5-30 secondes par examen (dépend de la taille des images)
- **Trafic réseau** : Important (plusieurs MB par examen)
- **Avantage** : SourceApplicationEntityTitle disponible

### Recommandation

Pour un usage quotidien, utilisez le **script de base** (`list_mammographies.py`).

N'utilisez le **script avancé** que si vous avez absolument besoin du champ SourceApplicationEntityTitle.

## Support

- Documentation pynetdicom : https://pydicom.github.io/pynetdicom/
- Documentation pydicom : https://pydicom.github.io/pydicom/
- DICOM Standard : https://www.dicomstandard.org/

## Améliorations possibles

1. **Interface graphique** : Créer une GUI avec tkinter ou PyQt
2. **Base de données** : Stocker les résultats dans SQLite ou PostgreSQL
3. **API REST** : Exposer les fonctionnalités via Flask ou FastAPI
4. **Notifications** : Envoyer des emails avec les résultats
5. **Statistiques** : Générer des rapports statistiques
6. **Multi-threading** : Accélérer le script avancé avec des threads

## Exemples de scripts personnalisés

### Générer un rapport HTML

```python
from list_mammographies import ConfigurationPACS, lister_mammographies_depistage, format_date

config = ConfigurationPACS()
examens = lister_mammographies_depistage(config)

html = """
<html>
<head><title>Rapport Mammographies</title></head>
<body>
<h1>Mammographies du jour</h1>
<table border="1">
<tr>
<th>Nom</th>
<th>Matricule</th>
<th>Date</th>
<th>Médecin</th>
<th>Opérateur</th>
</tr>
"""

for examen in examens:
    html += f"""
<tr>
<td>{examen['PatientName']}</td>
<td>{examen['Matricule']}</td>
<td>{format_date(examen['StudyDate'])}</td>
<td>{examen['PerformingPhysicianName']}</td>
<td>{examen['OperatorsName']}</td>
</tr>
"""

html += """
</table>
</body>
</html>
"""

with open('rapport.html', 'w') as f:
    f.write(html)

print("Rapport généré: rapport.html")
```

### Envoyer par email

```python
import smtplib
from email.mime.text import MIMEText
from list_mammographies import ConfigurationPACS, lister_mammographies_depistage

config = ConfigurationPACS()
examens = lister_mammographies_depistage(config)

# Générer le contenu du mail
contenu = f"Nombre d'examens: {len(examens)}\n\n"
for examen in examens:
    contenu += f"- {examen['PatientName']} ({examen['Matricule']})\n"

# Envoyer l'email
msg = MIMEText(contenu)
msg['Subject'] = 'Mammographies du jour'
msg['From'] = 'pacs@hopital.fr'
msg['To'] = 'medecin@hopital.fr'

smtp = smtplib.SMTP('localhost')
smtp.send_message(msg)
smtp.quit()

print("Email envoyé")
```
