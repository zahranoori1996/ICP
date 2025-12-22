using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages
{
    public partial class Settings
    {
        [Inject] private UserManagementService _userManagementService { get; set; } = default!;
        // General
        //private string _language = "en";
        //private string _dateFormat = "yyyy-MM-dd";
        //private string _numberFormat = ".";
        //private int _decimalPlaces = 4;

        //// Analysis
        //private decimal _defaultMinDiff = -10m;
        //private decimal _defaultMaxDiff = 10m;
        //private decimal _driftThreshold = 0.005m;
        //private string _defaultDriftMethod = "linear";

        //// API
        //private string _apiBaseUrl = "http://192.168.0.103:5000/api/";
        //private int _apiTimeout = 30;
        //private bool _isTesting = false;
        //private string? _connectionStatus;
        //private bool _connectionSuccess = false;

        //// Export
        //private string _defaultExportFormat = "excel";
        //private bool _includeHeaders = true;
        //private bool _includeTimestamp = true;
        //private bool _compressExports = false;

        //// Appearance
        //private bool _darkMode = false;
        //private bool _compactMode = false;
        //private bool _showTooltips = true;
        //private string _primaryColor = "#1976d2";

        // Account
        private string _currentUser = "";
        private bool _isSaving = false;

        private MudForm _passwordForm = null!;
        private string _currentPassword = "";
        private string _newPassword = "";
        private string _confirmNewPassword = "";
        private bool _isChangingPassword = false;


        private InputType _currentPasswordInput = InputType.Password;
        private InputType _newPasswordInput = InputType.Password;
        private InputType _confirmNewPasswordInput = InputType.Password;
        private string _currentPasswordIcon = Icons.Material.Filled.VisibilityOff;
        private string _newPasswordIcon = Icons.Material.Filled.VisibilityOff;
        private string _confirmNewPasswordIcon = Icons.Material.Filled.VisibilityOff;
        protected override async Task OnInitializedAsync()
        {
            await LoadSettings();
        }

        private async Task LoadSettings()
        {
            // Load from localStorage
            //var savedSettings = await JSRuntime.InvokeAsync<string?>("loadFromLocalStorage", "icp_settings");
            //if (!string.IsNullOrEmpty(savedSettings))
            //{
            //    // Parse and apply settings
            //}

            var user = AuthService.GetCurrentUser();
            _currentUser = user?.Name ?? "Unknown";
        }

        private void ToggleVisibility(int field)
        {
            if (field == 1) ToggleField(ref _currentPasswordInput, ref _currentPasswordIcon);
            else if (field == 2) ToggleField(ref _newPasswordInput, ref _newPasswordIcon);
            else if (field == 3) ToggleField(ref _confirmNewPasswordInput, ref _confirmNewPasswordIcon);
        }

        private void ToggleField(ref InputType type, ref string icon)
        {
            if (type == InputType.Password)
            {
                type = InputType.Text;
                icon = Icons.Material.Filled.Visibility;
            }
            else
            {
                type = InputType.Password;
                icon = Icons.Material.Filled.VisibilityOff;
            }
        }

        private string? ValidateNewPassword(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 6)
                return "Password must be at least 6 characters";
            if (value == _currentPassword)
                return "New password cannot be the same as current password";
            return null;
        }

        private string? ValidateConfirmPassword(string value)
        {
            if (value != _newPassword)
                return "Passwords do not match";
            return null;
        }
     
        private async Task HandleChangePassword()
        {
            // ۱. بررسی اعتبار ظاهری فرم (خالی نبودن و مچ بودن پسوردهای جدید)
            await _passwordForm.Validate();
            if (!_passwordForm.IsValid) return;

            _isChangingPassword = true;
            StateHasChanged();

            try
            {
                // ۲. ارسال درخواست واقعی به API
                var result = await _userManagementService.ChangePasswordAsync(_currentPassword, _newPassword);

                if (result.Success)
                {
                    // اگر سرور تایید کرد
                    Snackbar.Add(result.Message, Severity.Success);

                    // خالی کردن فرم
                    _currentPassword = _newPassword = _confirmNewPassword = "";
                    await _passwordForm.ResetAsync();
                }
                else
                {
                    // اگر سرور خطا داد (مثلاً رمز فعلی اشتباه بود)
                    Snackbar.Add(result.Message, Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add("خطای غیرمنتظره رخ داد.", Severity.Error);
            }
            finally
            {
                _isChangingPassword = false;
                StateHasChanged();
            }
        }
        private async Task SaveSettings()
        {
            _isSaving = true;
            StateHasChanged();

            try
            {
                var settings = new
                {
                    //Language = _language,
                    //DateFormat = _dateFormat,
                    //NumberFormat = _numberFormat,
                    //DecimalPlaces = _decimalPlaces,
                    //DefaultMinDiff = _defaultMinDiff,
                    //DefaultMaxDiff = _defaultMaxDiff,
                    //DriftThreshold = _driftThreshold,
                    //DefaultDriftMethod = _defaultDriftMethod,
                    //ApiBaseUrl = _apiBaseUrl,
                    //ApiTimeout = _apiTimeout,
                    //DefaultExportFormat = _defaultExportFormat,
                    //IncludeHeaders = _includeHeaders,
                    //IncludeTimestamp = _includeTimestamp,
                    //CompressExports = _compressExports,
                    //DarkMode = _darkMode,
                    //CompactMode = _compactMode,
                    //ShowTooltips = _showTooltips,
                    //PrimaryColor = _primaryColor

                   
                };

                await JSRuntime.InvokeVoidAsync("saveToLocalStorage", "icp_settings", settings);
                Snackbar.Add("Settings saved successfully", Severity.Success);
            }
            finally
            {
                _isSaving = false;
            }
        }

        //private async Task TestConnection()
        //{
        //    _isTesting = true;
        //    _connectionStatus = null;
        //    StateHasChanged();

        //    try
        //    {
        //        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        //        var response = await client.GetAsync(_apiBaseUrl + "health");

        //        if (response.IsSuccessStatusCode)
        //        {
        //            _connectionSuccess = true;
        //            _connectionStatus = "Connection successful!";
        //        }
        //        else
        //        {
        //            _connectionSuccess = false;
        //            _connectionStatus = $"Connection failed: {response.StatusCode}";
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _connectionSuccess = false;
        //        _connectionStatus = $"Connection failed: {ex.Message}";
        //    }
        //    finally
        //    {
        //        _isTesting = false;
        //    }
        //}

        //private void ResetToDefaults()
        //{
        //    _language = "en";
        //    _dateFormat = "yyyy-MM-dd";
        //    _numberFormat = ".";
        //    _decimalPlaces = 4;
        //    _defaultMinDiff = -10m;
        //    _defaultMaxDiff = 10m;
        //    _driftThreshold = 0.005m;
        //    _defaultDriftMethod = "linear";
        //    _defaultExportFormat = "excel";
        //    _includeHeaders = true;
        //    _includeTimestamp = true;
        //    _compressExports = false;
        //    _darkMode = false;
        //    _compactMode = false;
        //    _showTooltips = true;
        //    _primaryColor = "#1976d2";

        //    Snackbar.Add("Settings reset to defaults", Severity.Info);
        //}

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