using System;
using System.Windows;
using EUVA.UI.Theming;
using EUVA.UI.Helpers;

namespace EUVA.UI.Windows;

public partial class AiSettingsWindow : Window
{
    public AiSettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        TxtApiKey.Password = AiSecurityHelper.Decrypt(EuvaSettings.Default.AiApiKeyEncrypted);
        TxtBaseUrl.Text = EuvaSettings.Default.AiBaseUrl;
        TxtModelName.Text = EuvaSettings.Default.AiModelName;

        string customPrompt = EuvaSettings.Default.AiCustomPrompt;
        if (!string.IsNullOrEmpty(customPrompt))
        {
            TxtSystemPrompt.Text = customPrompt;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        EuvaSettings.Default.AiApiKeyEncrypted = AiSecurityHelper.Encrypt(TxtApiKey.Password);
        EuvaSettings.Default.AiBaseUrl = TxtBaseUrl.Text;
        EuvaSettings.Default.AiModelName = TxtModelName.Text;
        EuvaSettings.Default.AiCustomPrompt = TxtSystemPrompt.Text;
        EuvaSettings.Default.Save();
        
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
