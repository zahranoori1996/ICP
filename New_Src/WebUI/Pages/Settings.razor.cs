using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages
{
    public partial class Settings
    {
        // General
        private string _language = "en";
        private string _dateFormat = "yyyy-MM-dd";
        private string _numberFormat = ".";
        private int _decimalPlaces = 4;

        // Analysis
        private decimal _defaultMinDiff = -10m;
        private decimal _defaultMaxDiff = 10m;
        private decimal _driftThreshold = 0.005m;
        private string _defaultDriftMethod = "linear";

        // API
        private string _apiBaseUrl = "http://192.168.0.103:5000/api/";
        private int _apiTimeout = 30;
        private bool _isTesting = false;
        private string? _connectionStatus;
        private bool _connectionSuccess = false;

        // Export
        private string _defaultExportFormat = "excel";
        private bool _includeHeaders = true;
        private bool _includeTimestamp = true;
        private bool _compressExports = false;

        // Appearance
        private bool _darkMode = false;
        private bool _compactMode = false;
        private bool _showTooltips = true;
        private string _primaryColor = "#1976d2";

        // Account
        private string _currentUser = "";
        private bool _isSaving = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadSettings();
        }

        private async Task LoadSettings()
        {
            // Load from localStorage
            var savedSettings = await JSRuntime.InvokeAsync<string?>("loadFromLocalStorage", "icp_settings");
            if (!string.IsNullOrEmpty(savedSettings))
            {
                // Parse and apply settings
            }

            var user = AuthService.GetCurrentUser();
            _currentUser = user?.Name ?? "Unknown";
        }

        private async Task SaveSettings()
        {
            _isSaving = true;
            StateHasChanged();

            try
            {
                var settings = new
                {
                    Language = _language,
                    DateFormat = _dateFormat,
                    NumberFormat = _numberFormat,
                    DecimalPlaces = _decimalPlaces,
                    DefaultMinDiff = _defaultMinDiff,
                    DefaultMaxDiff = _defaultMaxDiff,
                    DriftThreshold = _driftThreshold,
                    DefaultDriftMethod = _defaultDriftMethod,
                    ApiBaseUrl = _apiBaseUrl,
                    ApiTimeout = _apiTimeout,
                    DefaultExportFormat = _defaultExportFormat,
                    IncludeHeaders = _includeHeaders,
                    IncludeTimestamp = _includeTimestamp,
                    CompressExports = _compressExports,
                    DarkMode = _darkMode,
                    CompactMode = _compactMode,
                    ShowTooltips = _showTooltips,
                    PrimaryColor = _primaryColor
                };

                await JSRuntime.InvokeVoidAsync("saveToLocalStorage", "icp_settings", settings);
                Snackbar.Add("Settings saved successfully", Severity.Success);
            }
            finally
            {
                _isSaving = false;
            }
        }

        private async Task TestConnection()
        {
            _isTesting = true;
            _connectionStatus = null;
            StateHasChanged();

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await client.GetAsync(_apiBaseUrl + "health");

                if (response.IsSuccessStatusCode)
                {
                    _connectionSuccess = true;
                    _connectionStatus = "Connection successful!";
                }
                else
                {
                    _connectionSuccess = false;
                    _connectionStatus = $"Connection failed: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _connectionSuccess = false;
                _connectionStatus = $"Connection failed: {ex.Message}";
            }
            finally
            {
                _isTesting = false;
            }
        }

        private void ResetToDefaults()
        {
            _language = "en";
            _dateFormat = "yyyy-MM-dd";
            _numberFormat = ".";
            _decimalPlaces = 4;
            _defaultMinDiff = -10m;
            _defaultMaxDiff = 10m;
            _driftThreshold = 0.005m;
            _defaultDriftMethod = "linear";
            _defaultExportFormat = "excel";
            _includeHeaders = true;
            _includeTimestamp = true;
            _compressExports = false;
            _darkMode = false;
            _compactMode = false;
            _showTooltips = true;
            _primaryColor = "#1976d2";

            Snackbar.Add("Settings reset to defaults", Severity.Info);
        }

        private void ChangePassword()
        {
            NavigationManager.NavigateTo("/change-password");
        }

        private async Task Logout()
        {
            await AuthService.LogoutAsync();
            NavigationManager.NavigateTo("/login", forceLoad: true);
        }
    }
}