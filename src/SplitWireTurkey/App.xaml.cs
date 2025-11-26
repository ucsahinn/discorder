using System;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using System.Diagnostics;

namespace SplitWireTurkey
{
    public partial class App : Application
    {
        private static Mutex _mutex = null;
        private const string MutexName = "SplitWireTurkeySingleInstanceMutex";
        
        public static bool IsSingleInstanceRejected { get; private set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Load language first before using LanguageManager
            LoadLanguage();
            
            // Check if another instance is already running
            if (!CheckSingleInstance())
            {
                IsSingleInstanceRejected = true; // Single instance reddedildiğini işaretle
                MessageBox.Show(
                    GetLocalizedText("messages", "single_instance_message", "SplitWire-Turkey zaten çalışıyor. Pencereyi göremiyorsanız Görev Yöneticisi kullanarak SplitWire-Turkey.exe'yi sonlandırın."),
                    GetLocalizedText("messages", "single_instance_title", "Uygulama Zaten Çalışıyor"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }
            
            // Set up global exception handling
            Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            
            // Check if running as administrator
            if (!IsRunningAsAdministrator())
            {
                MessageBox.Show(
                    GetLocalizedText("messages", "admin_required_message", "Bu uygulama yönetici izinleri gerektirir. Lütfen yönetici olarak çalıştırın."),
                    GetLocalizedText("messages", "admin_required_title", "Yönetici İzinleri Gerekli"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                string.Format(GetLocalizedText("messages", "unexpected_error_message", "Beklenmeyen bir hata oluştu:\n{0}"), e.Exception.Message),
                GetLocalizedText("messages", "unexpected_error_title", "Hata"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                string.Format(GetLocalizedText("messages", "critical_error_message", "Kritik bir hata oluştu:\n{0}"), e.ExceptionObject),
                GetLocalizedText("messages", "critical_error_title", "Kritik Hata"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private bool CheckSingleInstance()
        {
            try
            {
                _mutex = new Mutex(true, MutexName, out bool createdNew);
                return createdNew;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tek instance kontrolü sırasında hata: {ex.Message}");
                return false;
            }
        }

        private bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void LoadLanguage()
        {
            try
            {
                // Try to load language from registry first
                string language = "TR"; // Default language
                
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\SplitWire-Turkey"))
                {
                    if (key != null)
                    {
                        var regLanguage = key.GetValue("Language") as string;
                        Debug.WriteLine($"Registry'den dil okundu: {regLanguage}");
                        if (!string.IsNullOrEmpty(regLanguage) && (regLanguage == "TR" || regLanguage == "EN" || regLanguage == "RU" || regLanguage == "ES"))
                        {
                            language = regLanguage;
                            Debug.WriteLine($"Dil ayarlandı: {language}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Registry anahtarı bulunamadı, varsayılan dil kullanılıyor");
                    }
                }
                
                // Load the language file
                Debug.WriteLine($"LanguageManager.LoadLanguage çağrılıyor: {language}");
                bool loadResult = LanguageManager.LoadLanguage(language);
                Debug.WriteLine($"Dil yükleme sonucu: {loadResult}");
            }
            catch (Exception ex)
            {
                // If language loading fails, continue with default (TR)
                Debug.WriteLine($"Dil yüklenirken hata: {ex.Message}");
            }
        }

        private string GetLocalizedText(string category, string key, string fallbackText)
        {
            try
            {
                var text = LanguageManager.GetText(category, key);
                Debug.WriteLine($"GetLocalizedText: {category}.{key} -> {text}");
                // If the text is the same as the key, it means the translation wasn't found
                if (text == $"{category}.{key}")
                {
                    Debug.WriteLine($"Çeviri bulunamadı, fallback kullanılıyor: {fallbackText}");
                    return fallbackText;
                }
                return text;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetLocalizedText hatası: {ex.Message}");
                return fallbackText;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Mutex'i temizle
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }
            
            base.OnExit(e);
        }
    }
} 