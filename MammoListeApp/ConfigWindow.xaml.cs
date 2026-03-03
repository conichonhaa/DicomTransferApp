using System.Windows;

namespace MammoListeApp
{
    public partial class ConfigWindow : Window
    {
        private readonly ConfigurationPACS _config;

        public ConfigWindow(ConfigurationPACS config)
        {
            InitializeComponent();
            _config = config;

            txtPACSIP.Text    = config.PACSSourceIP;
            txtPACSPort.Text  = config.PACSSourcePort.ToString();
            txtPACSAE.Text    = config.PACSSourceAETitle;
            txtCallingAE.Text = config.SourceCallingAE;
            txtSourceIP.Text  = config.SourceIP;

            btnSauvegarder.Click += BtnSauvegarder_Click;
        }

        private void BtnSauvegarder_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtPACSPort.Text, out int port))
            {
                MessageBox.Show("Le port doit être un nombre entier.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _config.PACSSourceIP       = txtPACSIP.Text.Trim();
            _config.PACSSourcePort     = port;
            _config.PACSSourceAETitle  = txtPACSAE.Text.Trim();
            _config.SourceCallingAE    = txtCallingAE.Text.Trim();
            _config.SourceIP           = txtSourceIP.Text.Trim();

            _config.Sauvegarder();
            DialogResult = true;
        }
    }
}
