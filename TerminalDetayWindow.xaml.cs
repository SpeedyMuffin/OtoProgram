using System;
using System.Windows;
using System.Windows.Input;

namespace OtoProgram
{
    public partial class TerminalDetayWindow : Window
    {
        public bool IsApplied { get; private set; } = false;
        public string ResultText { get; private set; } = string.Empty;

        public TerminalDetayWindow(string initialText, bool isReadOnly, string title, string icon)
        {
            InitializeComponent();

            txtContent.Text = initialText;
            txtContent.IsReadOnly = isReadOnly;
            lblTitle.Text = title;
            lblTitleIcon.Text = icon;

            if (isReadOnly)
            {
                btnApply.Visibility = Visibility.Collapsed;
                btnCancel.Content = "Kapat";
                txtContent.Focus();
                txtContent.Select(txtContent.Text.Length, 0);
            }
            else
            {
                btnApply.Visibility = Visibility.Visible;
                btnCancel.Content = "İptal";
                txtContent.Focus();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    this.DragMove();
                }
                catch { }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void BtnKapat_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(txtContent.Text);
                MessageBox.Show("Metin panoya kopyalandı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kopyalama hatası: " + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ResultText = txtContent.Text;
            IsApplied = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
