using WebUI.Services;

namespace WebUI.Pages
{
    public partial class Dashboard
    {
        private int _totalProjects = 0;
        private int _samplesAnalyzed = 0;
        private int _crmStandards = 25;
        private int _passRate = 92;
        private List<RecentProject> _recentProjects = new();

        protected override async Task OnInitializedAsync()
        {
            var user = AuthService.GetCurrentUser();
            if (user == null || !user.IsAuthenticated)
            {
                NavManager.NavigateTo("/login");
                return;
            }

            await LoadDashboardData();
        }

        private async Task LoadDashboardData()
        {
            try
            {
                var result = await ProjectService.GetProjectsAsync();
                if (result.Succeeded && result.Data != null)
                {
                    _totalProjects = result.Data.Count;
                    _recentProjects = result.Data
                        .OrderByDescending(p => p.CreatedAt)
                        .Take(5)
                        .Select(p => new RecentProject(p.ResultProjectId ?? Guid.Empty, p.ProjectName ?? "Untitled", p.CreatedAt))
                        .ToList();
                }
            }
            catch
            {
                // Silently fail - dashboard will show zeros
            }
        }

        private record RecentProject(Guid Id, string Name, DateTime Date);

    }
}