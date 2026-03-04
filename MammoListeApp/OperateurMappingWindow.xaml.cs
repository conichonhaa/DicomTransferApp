using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace MammoListeApp
{
    public partial class OperateurMappingWindow : Window
    {
        public class MappingEntry : INotifyPropertyChanged
        {
            private string _dicomValue = "";
            private string _affichageName = "";

            public string DicomValue
            {
                get => _dicomValue;
                set { _dicomValue = value; PropertyChanged?.Invoke(this, new(nameof(DicomValue))); }
            }
            public string AffichageName
            {
                get => _affichageName;
                set { _affichageName = value; PropertyChanged?.Invoke(this, new(nameof(AffichageName))); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly ObservableCollection<MappingEntry> _entries = new();

        public OperateurMappingWindow()
        {
            InitializeComponent();

            // Charger le mapping existant
            foreach (var kv in OperateurMappingService.GetAll())
                _entries.Add(new MappingEntry { DicomValue = kv.Key, AffichageName = kv.Value });

            dgMapping.ItemsSource = _entries;

            btnAjouter.Click     += (_, _) => { _entries.Add(new MappingEntry()); dgMapping.ScrollIntoView(_entries.Last()); };
            btnSupprimer.Click   += BtnSupprimer_Click;
            btnSauvegarder.Click += BtnSauvegarder_Click;
        }

        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (dgMapping.SelectedItem is MappingEntry entry)
                _entries.Remove(entry);
        }

        private void BtnSauvegarder_Click(object sender, RoutedEventArgs e)
        {
            // Commit any in-progress cell edit
            dgMapping.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

            var map = _entries
                .Where(e => !string.IsNullOrWhiteSpace(e.DicomValue))
                .ToDictionary(e => e.DicomValue.Trim(), e => e.AffichageName.Trim());

            OperateurMappingService.Save(map);
            DialogResult = true;
        }
    }
}
