
using System;
using System.Diagnostics;
using System.Windows;
using System.IO;

namespace DicomTransferApp
{
    public partial class ConfigurationPACSWindow : Window
    {
        private ConfigurationPACS _config;

        public ConfigurationPACSWindow()
        {
            InitializeComponent();
            _config = ConfigurationPACS.Charger();
            ChargerValeurs();
        }

        private void ChargerValeurs()
        {
            // Source (votre PC)
            txtSourceIP.Text = _config.SourceIP;
            txtSourcePort.Text = _config.SourcePort.ToString();
            txtSourceAE.Text = _config.SourceAETitle;
            txtSourceCallingAE.Text = _config.SourceCallingAE;

            // PACS Source
            txtPACSSourceIP.Text = _config.PACSSourceIP;
            txtPACSSourcePort.Text = _config.PACSSourcePort.ToString();
            txtPACSSourceAE.Text = _config.PACSSourceAETitle;

            // PACS Destination
            txtDestIP.Text = _config.DestinationIP;
            txtDestPort.Text = _config.DestinationPort.ToString();
            txtDestAE.Text = _config.DestinationAETitle;

            txtUsurperAE.Text = _config.UsurperAETitle;
        }

        private void BtnSauvegarder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Source (votre PC)
                _config.SourceIP = txtSourceIP.Text;
                _config.SourcePort = int.Parse(txtSourcePort.Text);
                _config.SourceAETitle = txtSourceAE.Text;
                _config.SourceCallingAE = txtSourceCallingAE.Text;

                // PACS Source
                _config.PACSSourceIP = txtPACSSourceIP.Text;
                _config.PACSSourcePort = int.Parse(txtPACSSourcePort.Text);
                _config.PACSSourceAETitle = txtPACSSourceAE.Text;

                // PACS Destination
                _config.DestinationIP = txtDestIP.Text;
                _config.DestinationPort = int.Parse(txtDestPort.Text);
                _config.DestinationAETitle = txtDestAE.Text;

                _config.UsurperAETitle = txtUsurperAE.Text;

                _config.Sauvegarder();

                MessageBox.Show(
                    "Configuration sauvegardée !",
                    "Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur : {ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnOuvrirFichier_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var chemin = ConfigurationPACS.ObtenirCheminConfig();

                if (!File.Exists(chemin))
                {
                    _config.Sauvegarder();
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = chemin,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Impossible d'ouvrir : {ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
    }
}
