using MudBlazor;
using System.Globalization;
using WebUI.Services;
using WebUI.Shared;

namespace WebUI.Pages
{
    public partial class Projects
    {
        // private List<ProjectDto>? _projects;
        private List<ProjectDto> _allProjects = new();

        // لیستی که فقط پروژه‌های صفحه جاری را نگه می‌دارد (مثل _pagedRows در WeightCheck)
        private List<ProjectDto> _pagedProjects = new();

        private bool _isLoading = true;
        private string _searchText = "";

        // Pagination variables
        private int _currentPage = 1;
        private int _pageSize = 10; // ✅ پیش‌فرض را روی 10 گذاشتیم تا اگر 15 آیتم دارید، صفحه دوم فعال شود
        private int _totalItems = 0;
        // محاسبه تعداد کل صفحات
        private int _totalPages => _totalItems == 0 ? 1 : (int)Math.Ceiling((double)_totalItems / _pageSize);

        protected override async Task OnInitializedAsync()
        {
            await LoadProjects();
        }

        private async Task LoadProjects()
        {
            _isLoading = true;
            StateHasChanged();

            // نکته مهم: اینجا درخواست تعداد بسیار زیاد (مثل 10000) می‌دهیم تا "همه" پروژه‌ها را بگیریم
            // چون می‌خواهیم صفحه‌بندی را سمت کلاینت انجام دهیم
            var result = await ProjectService.GetProjectsAsync(1, 10000, _searchText);

            if (result.Succeeded && result.Data != null)
            {
                // ذخیره همه پروژه‌ها در لیست اصلی
                _allProjects = result.Data.Items ?? new List<ProjectDto>();

                // محاسبه تعداد کل بر اساس لیستی که گرفتیم (نه چیزی که سرور می‌گوید)
                _totalItems = _allProjects.Count;

                // اگر سرچ شده بود، فیلتر سمت کلاینت هم اعمال شود (اختیاری)
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    _allProjects = _allProjects
                       .Where(p => p.ProjectName.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                       .ToList();
                    _totalItems = _allProjects.Count;
                }
            }
            else
            {
                Snackbar.Add(result.Message ?? "Failed to load projects", Severity.Error);
                _allProjects = new List<ProjectDto>();
                _totalItems = 0;
            }

            // به‌روزرسانی لیست نمایشی (صفحه اول)
            _currentPage = 1;
            UpdatePagedProjects();

            _isLoading = false;
        }


        private void UpdatePagedProjects()
        {
            _pagedProjects = _allProjects
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize)
                .ToList();
        }

        private async Task ReloadProjects()
        {
            _currentPage = 1;
            await LoadProjects();
        }

        private async Task SearchProjects()
        {
            // _currentPage = 1;
            await LoadProjects();
        }

        // ✅ متد تغییر صفحه
        private async Task OnPageChanged(int page)
        {
            _currentPage = page;
            UpdatePagedProjects();
            StateHasChanged();
        }

        // ✅ متد تغییر تعداد آیتم در صفحه
        private async Task OnPageSizeChanged(int size)
        {
            _pageSize = size;
            _currentPage = 1;
            UpdatePagedProjects();
            StateHasChanged();
        }

        private void OpenProject(ProjectDto project)
        {
            ProjectService.SetCurrentProject(project);
            NavManager.NavigateTo($"/pivot?projectId={project.ProjectId}");
        }

        private void ProcessProject(ProjectDto project)
        {
            ProjectService.SetCurrentProject(project);
            NavManager.NavigateTo($"/process/weight?projectId={project.ProjectId}");
        }

        private async Task DeleteProject(ProjectDto project)
        {
            var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.ContentText, $"Are you sure you want to delete project '{project.ProjectName}'? This action cannot be undone." },
            { x => x.ButtonText, "Delete" },
            { x => x.Color, Color.Error }
        };

            var dialog = await DialogService.ShowAsync<ConfirmDialog>("Delete Project", parameters);
            var result = await dialog.Result;

            if (result != null && !result.Canceled)
            {
                var deleteResult = await ProjectService.DeleteProjectAsync(project.ProjectId);
                if (deleteResult.Succeeded)
                {
                    Snackbar.Add($"Project '{project.ProjectName}' deleted", Severity.Success);
                    await LoadProjects(); // لود مجدد لیست
                }
                else
                {
                    Snackbar.Add(deleteResult.Message ?? "Failed to delete project", Severity.Error);
                }
            }
        }

        private void OpenVersionHistory(ProjectDto project)
        {
            NavManager.NavigateTo($"/project/{project.ProjectId}/versions");
        }

        private string ToShamsi(DateTime date)
        {
            if (date == DateTime.MinValue) return "-";

            var pc = new PersianCalendar();
            TimeZoneInfo iranTimeZone;
            try
            {
                iranTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
            }
            catch
            {
                iranTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
            }

            var iranTime = TimeZoneInfo.ConvertTimeFromUtc(date, iranTimeZone);
            return $"{pc.GetYear(iranTime)}/{pc.GetMonth(iranTime):00}/{pc.GetDayOfMonth(iranTime):00} {iranTime.Hour:00}:{iranTime.Minute:00}";
        }
    }
}