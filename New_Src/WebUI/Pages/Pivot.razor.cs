using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages
{
    public partial class Pivot
    {
        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private string? _projectName;
        private AdvancedPivotResultDto? _pivotData;
        private List<string> _allElements = new();
        private List<string> _displayColumns = new();
        private IEnumerable<string> _selectedElements = new HashSet<string>();
        private string _searchText = "";
        private int _decimalPlaces = 2;
        private bool _useOxide = false;
        private bool _useInt = false;
        // Use Int column instead of Corr Con
        private bool _mergeRepeats = false;
        // Merge repeated elements
        private bool _isLoading = true;
        // Start with loading state
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalPages = 1;
        private bool _hasRepeats = false;
        // Track if data has repeated elements

        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ?? ProjectService.CurrentProjectId;

            if (_projectId.HasValue)
            {
                _isLoading = true;
                StateHasChanged();

                var projectResult = await ProjectService.GetProjectAsync(_projectId.Value);
                if (projectResult.Succeeded && projectResult.Data != null)
                {
                    _projectName = projectResult.Data.ProjectName;
                }

                await LoadElements();
                await LoadData();
            }
            else
            {
                _isLoading = false;
            }
        }

        private async Task LoadElements()
        {
            if (_projectId == null) return;
            var result = await PivotService.GetElementsAsync(_projectId.Value);
            if (result.Succeeded && result.Data != null)
            {
                _allElements = result.Data;
            }
        }

        private async Task LoadData()
        {
            if (_projectId == null) return;
            _isLoading = true;
            StateHasChanged();

            // اصلاح شده:
            // 1. Aggregation به صورت رشته ("First") ارسال شد.
            // 2. NumberFilters اضافه شد.
            var request = new AdvancedPivotRequest(
                ProjectId: _projectId.Value,
                SearchText: string.IsNullOrWhiteSpace(_searchText) ? null : _searchText,
                SelectedElements: _selectedElements.Any() ? _selectedElements.ToList() : null,
                UseOxide: _useOxide,
                UseInt: _useInt,
                DecimalPlaces: _decimalPlaces,
                Page: _currentPage,
                PageSize: _pageSize,
                MergeRepeats: _mergeRepeats,
                Aggregation: "First", // <--- اصلاح خطا: ارسال رشته به جای عدد
                NumberFilters: null
            );
            var result = await PivotService.GetAdvancedPivotTableAsync(request);

            if (result.Succeeded && result.Data != null)
            {
                _pivotData = result.Data;
                _displayColumns = _pivotData.Columns;

                // جلوگیری از تقسیم بر صفر
                if (_pageSize > 0)
                    _totalPages = (int)Math.Ceiling(_pivotData.TotalCount / (double)_pageSize);
                else
                    _totalPages = 1;
                _hasRepeats = _pivotData.Metadata?.HasRepeats ?? false;

                if (_pivotData.Metadata?.AllElements != null && !_allElements.Any())
                {
                    _allElements = _pivotData.Metadata.AllElements;
                }
            }
            else
            {
                // اصلاح خطا: استفاده از Message به جای Messages
                // اگر کلاس wrapper شما لیست خطا دارد، ممکن است نام آن Errors باشد.
                // اما معمولاً در این پروژه Message استفاده شده است.
                var errorMsg = !string.IsNullOrEmpty(result.Message)
                               ? result.Message
                               : "Failed to load data";
                Snackbar.Add(errorMsg, Severity.Error);
            }

            _isLoading = false;
        }

        private async Task OnPageChanged(int page)
        {
            _currentPage = page;
            await LoadData();
        }

        private async Task OnPageSizeChanged(int size)
        {
            _pageSize = size;
            _currentPage = 1;
            await LoadData();
        }

        private void SelectProject()
        {
            NavManager.NavigateTo("/projects");
        }

        private async Task ExportData()
        {
            if (_projectId == null || _pivotData == null) return;
            var request = new PivotRequest
            {
                ProjectId = _projectId.Value,
                UseOxide = _useOxide,
                DecimalPlaces = _decimalPlaces
            };
            var result = await PivotService.ExportToCsvAsync(request);
            if (result.Succeeded && result.Data != null)
            {
                var fileName = $"{_projectName ?? "export"}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                await DownloadFile(result.Data, fileName, "text/csv");
                Snackbar.Add("Data exported successfully", Severity.Success);
            }
            else
            {
                Snackbar.Add(result.Message ?? "Export failed", Severity.Error);
            }
        }

        private async Task DownloadFile(byte[] data, string fileName, string contentType)
        {
            var base64 = Convert.ToBase64String(data);
            await JS.InvokeVoidAsync("downloadFile", fileName, contentType, base64);
        }
    }
}