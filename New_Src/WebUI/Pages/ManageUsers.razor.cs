using Application.DTOs;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages
{
    public partial class ManageUsers
    {
        private List<UserListDto>? users;
        private bool isLoading = true;

        protected override async Task OnInitializedAsync()
        {
            await LoadUsers();
        }

        private async Task LoadUsers()
        {
            try
            {
                isLoading = true;
                users = await UserManagementService.GetAllUsersAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add("Error Loading The User List: " + ex.Message, Severity.Error);
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task OpenAddUserDialog()
        {
            var parameters = new DialogParameters();
            var dialog = await DialogService.ShowAsync<AddUserDialog>("Adding A New User", parameters);
            var result = await dialog.Result;

            if (result != null && !result.Canceled)
            {
                await LoadUsers();
                Snackbar.Add("User With Added Success", Severity.Success);
            }
        }

        private async Task OpenChangePasswordDialog(Guid userId)
        {
            var parameters = new DialogParameters { { "UserId", userId } };
            var dialog = await DialogService.ShowAsync<ChangePasswordDialog>("Change Password", parameters);
            var result = await dialog.Result;

            if (result != null && !result.Canceled)
            {
                Snackbar.Add("Password Successfully Changed", Severity.Success);
            }
        }

        private async Task DeleteUserConfirm(Guid userId, string username)
        {
            bool? confirmed = await DialogService.ShowMessageBox(
                "Delete User",
                "Are You Sure You Want To Delete This User?",
                yesText: "Yes", noText: "No");
            if (username.ToLower() == "admin")
            {
                Snackbar.Add("The admin username cannot be deleted.", Severity.Warning);
                return;
            }
            if (confirmed == true)
            {
                await DeleteUser(userId);
            }
        }

        private async Task DeleteUser(Guid userId)
        {
            try
            {
                var success = await UserManagementService.DeleteUserAsync(userId);
                if (success)
                {
                    Snackbar.Add("User Successfully Deleted", Severity.Success);
                    await LoadUsers();
                }
                else
                {
                    Snackbar.Add("Error In Deletion", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add("Error: " + ex.Message, Severity.Error);
            }
        }

        private Color GetPositionColor(string position)
        {
            return position?.ToLower() switch
            {
                "admin" => Color.Error,
                "analyst" => Color.Info,
                "viewer" => Color.Default,
                _ => Color.Default
            };
        }
    }
}