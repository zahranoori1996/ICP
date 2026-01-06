using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages.Process
{
    public partial class DFCheck
    {
        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private string? _projectName;
        private decimal _expectedDf = 1m;
        private decimal _tolerancePercent = 0m;
        private bool _autoCorrect = false;
        private bool _isLoading = false;
        private int _totalSamples = 0;
        private bool _detailsExpanded = true;

        private List<DfSample> _badDfSamples = new();
        private List<DfSample> _allSamples = new();
        private List<DfDistribution> _dfDistribution = new();

        // ✅ متغیرهای صفحه‌بندی
        private List<DfSample> _pagedBadSamples = new();
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalBadCount = 0;
        private int _totalPages => _totalBadCount == 0 ? 1 : (int)Math.Ceiling((double)_totalBadCount / _pageSize);

        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ?? ProjectService.CurrentProjectId;
            if (_projectId.HasValue)
            {
                var projectResult = await ProjectService.GetProjectAsync(_projectId.Value, includeLatestState: true);
                if (projectResult.Succeeded && projectResult.Data != null)
                {
                    _projectName = projectResult.Data.ProjectName;
                }
                await LoadSamples();
            }
        }

        private async Task LoadSamples()
        {
            if (_projectId == null) return;

            var result = await CorrectionService.GetDfSamplesAsync(_projectId.Value);
            if (result.Succeeded && result.Data != null)
            {
                _allSamples.Clear();
                var dfValues = new Dictionary<decimal, int>();

                foreach (var sample in result.Data)
                {
                    _allSamples.Add(new DfSample
                    {
                        RowNumber = sample.RowNumber,
                        SolutionLabel = sample.SolutionLabel,
                        CurrentDf = sample.CurrentDf,
                        ExpectedDf = _expectedDf
                    });

                    var roundedDf = Math.Round(sample.CurrentDf);
                    if (!dfValues.ContainsKey(roundedDf))
                        dfValues[roundedDf] = 0;
                    dfValues[roundedDf]++;
                }

                _totalSamples = _allSamples.Count;
                _dfDistribution = dfValues
                    .OrderByDescending(x => x.Value)
                    .Take(5)
                    .Select(x => new DfDistribution
                    {
                        DfValue = x.Key,
                        Count = x.Value,
                        Percentage = (double)x.Value / _totalSamples * 100
                    })
                    .ToList();
            }
        }

        private async Task DetectBadDf()
        {
            if (_projectId == null) return;

            _isLoading = true;
            StateHasChanged();

            try
            {
                await LoadSamples();

                _badDfSamples = _allSamples
                    .Where(s =>
                    {
                        var deviation = Math.Abs(s.CurrentDf - _expectedDf) / _expectedDf * 100;
                        s.Deviation = deviation;
                        return deviation > _tolerancePercent;
                    })
                    .ToList();

                // ✅ تنظیم مقادیر اولیه صفحه‌بندی
                _totalBadCount = _badDfSamples.Count;
                _currentPage = 1;
                UpdatePagedRows();

                if (_badDfSamples.Any())
                {
                    Snackbar.Add($"Found {_badDfSamples.Count} samples with abnormal DF", Severity.Warning);
                }
                else
                {
                    Snackbar.Add("All samples have normal DF values", Severity.Success);
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async Task ApplyCorrection()
        {
            if (_projectId == null || !_badDfSamples.Any()) return;

            _isLoading = true;
            StateHasChanged();

            try
            {
                var solutionLabels = _badDfSamples.Select(s => s.SolutionLabel).ToList();
                var result = await CorrectionService.ApplyDfCorrectionAsync(_projectId.Value, solutionLabels, _expectedDf);

                if (result.Succeeded && result.Data != null)
                {
                    Snackbar.Add($"Corrected {result.Data.CorrectedRows} samples to DF = {_expectedDf}", Severity.Success);

                    // Reload samples to show updated state
                    // نکته: برای اینکه جدول خالی شود یا آپدیت شود، باید دوباره Detect صدا زده شود یا لیست پاک شود
                    await LoadSamples();

                    // لیست بدها را پاک میکنیم چون اصلاح شدند
                    _badDfSamples.Clear();
                    _totalBadCount = 0;
                    UpdatePagedRows();
                }
                else
                {
                    Snackbar.Add(result.Message ?? "Failed to apply correction", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error: {ex.Message}", Severity.Error);
            }
            finally
            {
                _isLoading = false;
            }
        }

        // ✅ متدهای صفحه‌بندی
        private void UpdatePagedRows()
        {
            if (_badDfSamples == null)
            {
                _pagedBadSamples = new();
                return;
            }

            _pagedBadSamples = _badDfSamples
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize)
                .ToList();
        }

        private void OnPageChanged(int page)
        {
            _currentPage = page;
            UpdatePagedRows();
        }

        private void OnPageSizeChanged(int size)
        {
            _pageSize = size;
            _currentPage = 1;
            UpdatePagedRows();
        }

        public class DfSample
        {
            public int RowNumber { get; set; }
            public string SolutionLabel { get; set; } = "";
            public decimal CurrentDf { get; set; }
            public decimal ExpectedDf { get; set; }
            public decimal Deviation { get; set; }
            public bool IsCorrected { get; set; }
        }

        public class DfDistribution
        {
            public decimal DfValue { get; set; }
            public int Count { get; set; }
            public double Percentage { get; set; }
        }

        private async Task OnBeforeNavigation(LocationChangingContext context)
        {
            if (_isLoading)
            {
                context.PreventNavigation();
            }
            await Task.CompletedTask;
        }
    }
}
