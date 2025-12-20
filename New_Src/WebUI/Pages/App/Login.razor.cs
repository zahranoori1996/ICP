using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages.App
{
    public partial class Login
    {
        // Sign In fields
        private MudForm loginForm = null!;
        private bool loginValid;
        private bool loginProcessing = false;
        private string loginEmail = "";
        private string loginPassword = "";
        private bool rememberMe = true;

        // Sign Up fields
        private MudForm signupForm = null!;
        private bool signupValid;
        private bool signupProcessing = false;
        private string signupFullName = "";
        private string signupEmail = "";
        private string signupPassword = "";
        private string signupPosition = "";

        // Toggle between Sign In / Sign Up
        private bool showSignUp = false;

        protected override async Task OnInitializedAsync()
        {
            var autoLoginResult = await AuthService.CheckAutoLoginAsync();
            if (autoLoginResult.IsAuthenticated)
            {
                Snackbar.Add($"Welcome back, {autoLoginResult.Name}!", Severity.Success);
                NavManager.NavigateTo("/dashboard");
            }
        }

        private async Task HandleLogin()
        {
            await loginForm.Validate();
            if (!loginForm.IsValid) return;

            loginProcessing = true;
            StateHasChanged();

            var result = await AuthService.LoginAsync(loginEmail, loginPassword, rememberMe);

            loginProcessing = false;

            if (result.IsAuthenticated)
            {
                Snackbar.Add($"Welcome back, {result.Name}!", Severity.Success);
                NavManager.NavigateTo("/dashboard");
            }
            else
            {
                Snackbar.Add(result.Message, Severity.Error);
            }
        }

        private async Task HandleSignUp()
        {
            await signupForm.Validate();
            if (!signupForm.IsValid) return;

            if (string.IsNullOrEmpty(signupPosition))
            {
                Snackbar.Add("Please select your position", Severity.Warning);
                return;
            }

            signupProcessing = true;
            StateHasChanged();

            var result = await AuthService.RegisterAsync(signupEmail, signupPassword, signupFullName, signupPosition);

            signupProcessing = false;

            if (result.IsAuthenticated)
            {
                Snackbar.Add("Account created!  Please sign in.", Severity.Success);
                signupFullName = "";
                signupEmail = "";
                signupPassword = "";
                signupPosition = "";
                showSignUp = false;
            }
            else
            {
                Snackbar.Add(result.Message, Severity.Error);
            }
        }

        // Password visibility toggle
        private bool loginPasswordVisible = false;
        private InputType loginPasswordInputType => loginPasswordVisible ? InputType.Text : InputType.Password;
        private string loginPasswordIcon => loginPasswordVisible ? Icons.Material.Filled.Visibility : Icons.Material.Filled.VisibilityOff;

        private bool IsLoginFormValid()
        {
            return !string.IsNullOrWhiteSpace(loginEmail) &&
                   loginEmail.Length >= 4 &&
                   !ContainsPersian(loginEmail) &&
                   !string.IsNullOrWhiteSpace(loginPassword) &&
                   loginPassword.Length >= 6 &&
                   !ContainsPersian(loginPassword);
        }

        // چک کردن متن فارسی
        private bool ContainsPersian(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // رنج کاراکترهای فارسی/عربی
            return text.Any(c => c >= 0x0600 && c <= 0x06FF);
        }

        // Validation برای Username
        private string? ValidateUsername(string value)
        {
            if (ContainsPersian(value))
                return "Please use English characters only";
            if (!string.IsNullOrEmpty(value) && value.Length < 4)
            {
                return "Username must be at least 4 characters";
            }
            return null;
        }

        // Validation برای Password
        private string? ValidatePassword(string value)
        {
            if (ContainsPersian(value))
                return "Please use English characters only";
            if (!string.IsNullOrEmpty(value) && value.Length < 6)
                return "Password must be at least 6 characters";
            return null;
        }

        private async Task HandleEnterKey(KeyboardEventArgs e)
        {
            if (e.Key == "Enter" || e.Code == "Enter" || e.Code == "NumpadEnter")
            {
                // بررسی اینکه فرم معتبر باشد و در حال پردازش نباشد
                if (IsLoginFormValid() && !loginProcessing)
                {
                    await HandleLogin();
                }
            }
        }
    }
}