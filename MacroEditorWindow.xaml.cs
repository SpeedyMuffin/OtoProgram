using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace OtoProgram
{
    public partial class MacroEditorWindow : Window
    {
        public ObservableCollection<MacroItem> Macros { get; set; }

        public MacroEditorWindow(List<MacroItem> existingMacros)
        {
            InitializeComponent();
            
            Macros = new ObservableCollection<MacroItem>();
            if (existingMacros != null)
            {
                foreach (var item in existingMacros)
                {
                    Macros.Add(new MacroItem { Ad = item.Ad, Komut = item.Komut });
                }
            }
            
            gridMacros.ItemsSource = Macros;
        }

        private void GridMacros_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (gridMacros.SelectedItem is MacroItem selected)
            {
                txtAd.Text = selected.Ad;
                txtKomut.Text = selected.Komut;
            }
        }

        private void BtnEkle_Click(object sender, RoutedEventArgs e)
        {
            string ad = txtAd.Text.Trim();
            string komut = txtKomut.Text.Trim();

            if (string.IsNullOrEmpty(ad) || string.IsNullOrEmpty(komut))
            {
                MessageBox.Show("Lütfen ad ve komut alanlarını doldurun.", "Uyarı");
                return;
            }

            bool updated = false;
            for (int i = 0; i < Macros.Count; i++)
            {
                if (Macros[i].Ad.Equals(ad, StringComparison.OrdinalIgnoreCase))
                {
                    Macros[i] = new MacroItem { Ad = ad, Komut = komut };
                    updated = true;
                    break;
                }
            }

            if (!updated)
            {
                Macros.Add(new MacroItem { Ad = ad, Komut = komut });
            }

            txtAd.Text = "";
            txtKomut.Text = "";
            gridMacros.SelectedItem = null;
        }

        private void BtnSil_Click(object sender, RoutedEventArgs e)
        {
            if (gridMacros.SelectedItem is MacroItem selected)
            {
                Macros.Remove(selected);
                txtAd.Text = "";
                txtKomut.Text = "";
            }
            else
            {
                MessageBox.Show("Lütfen silmek istediğiniz makroyu listeden seçin.", "Uyarı");
            }
        }

        private void BtnKaydet_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnVazgec_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
