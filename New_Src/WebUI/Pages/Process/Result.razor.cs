using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages.Process
{
    public partial class Result
    {
        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private List<ResultRow> _rows = new();
        private List<ResultRow> _filteredRows = new(); // Added: Holds filtered (unpaged) data
        private List<ResultRow> _pagedRows = new();
        private List<string> _elementColumns = new();
        private bool _showOnlyRm = false;
        private bool _showBadSamples = true;
        private bool _showDiff = false;
        private bool _isLoading = false;
        // --- Pagination Variables ---
        private int _currentPage = 1;
        private int _pageSize = 50; // New default page size matching first option
        private int _totalVisibleRows = 0; // Total count after filtering
        private int _totalPages => _totalVisibleRows == 0 ? 1 : (int)Math.Ceiling((double)_totalVisibleRows / _pageSize);
        // ----------------------------
        private int _totalSamples = 0;
        private int _rmSamples = 0;
        private int _badSamples = 0;
        private decimal _passRate = 0;
        private string? _projectName;

        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ?? ProjectService.CurrentProjectId;
            if (_projectId.HasValue)
            {
                var projectResult = await ProjectService.GetProjectAsync(_projectId.Value);
                if (projectResult.Succeeded && projectResult.Data != null)
                {
                    _projectName = projectResult.Data.ProjectName;
                }
                await LoadData();
            }
        }

        private async Task LoadData()
        {
            if (_projectId == null) return;
            _isLoading = true;
            StateHasChanged();

            try
            {
                // Use Advanced Pivot for Python-compatible duplicate handling
                // NOTE: We fetch a large number of rows (10000) to allow client-side filtering/pagination
                var request = new AdvancedPivotRequest(ProjectId: _projectId.Value, PageSize: 10000);
                var result = await PivotService.GetAdvancedPivotTableAsync(request);
                if (result.Succeeded && result.Data != null)
                {
                    var elements = await PivotService.GetElementsAsync(_projectId.Value);
                    _elementColumns = elements.Data ?? new List<string>();

                    _rows.Clear();
                    int rowNum = 1;
                    int passed = 0;
                    int totalRm = 0;
                    foreach (var row in result.Data.Rows)
                    {
                        var resultRow = new ResultRow
                        {
                            RowNumber = rowNum++,
                            SolutionLabel = row.SolutionLabel,
                            SampleType = DetermineSampleType(row),
                            IsBad = DetermineIfBad(row),
                            TotalElements = _elementColumns.Count
                        };
                        foreach (var col in _elementColumns)
                        {
                            if (row.Values.TryGetValue(col, out var val) && val.HasValue)
                            {
                                resultRow.Values[col] = val.Value;
                            }

                            if (row.Values.TryGetValue(col + "_Diff", out var diff) && diff.HasValue)
                            {
                                resultRow.Diffs[col] = diff.Value;
                                if (Math.Abs(diff.Value) <= 10) resultRow.PassedCount++;
                            }
                            else if (row.Values.TryGetValue($"Diff_{col}", out var diff2) && diff2.HasValue)
                            {
                                resultRow.Diffs[col] = diff2.Value;
                                if (Math.Abs(diff2.Value) <= 10) resultRow.PassedCount++;
                            }
                        }

                        _rows.Add(resultRow);
                        if (resultRow.SampleType == "RM")
                        {
                            totalRm++;
                            if (resultRow.PassedCount == resultRow.TotalElements) passed++;
                        }
                    }

                    _totalSamples = _rows.Count;
                    _rmSamples = _rows.Count(r => r.SampleType == "RM");
                    _badSamples = _rows.Count(r => r.IsBad);
                    _passRate = totalRm > 0 ? (decimal)passed / totalRm * 100 : 0;

                    // Reset pagination state and apply filters
                    _currentPage = 1;
                    ApplyFilters();
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void ApplyFilters()
        {
            var filtered = _rows.AsEnumerable();
            if (_showOnlyRm)
            {
                filtered = filtered.Where(r => r.SampleType == "RM");
            }

            // Store filtered data and update total count for pagination
            _filteredRows = filtered.ToList();
            _totalVisibleRows = _filteredRows.Count;

            // Apply pagination
            UpdatePagedRows();
        }

        // --- Pagination Methods (Copied from VolumeCheck logic) ---
        private void UpdatePagedRows()
        {
            if (_filteredRows == null)
            {
                _pagedRows = new();
                return;
            }

            _pagedRows = _filteredRows
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize)
                .ToList();
        }

        private void OnPageChanged(int page)
        {
            _currentPage = page;
            UpdatePagedRows();
            StateHasChanged();
        }

        private void OnPageSizeChanged(int size)
        {
            _pageSize = size;
            _currentPage = 1;
            UpdatePagedRows();
            StateHasChanged();
        }
        // ----------------------------------------------------------

        private string DetermineSampleType(AdvancedPivotRowDto row)
        {
            var label = row.SolutionLabel ?? "";

            // Check if SampleType exists in values
            if (row.Values.TryGetValue("SampleType", out var sampleTypeVal) && sampleTypeVal.HasValue)
            {
                // SampleType might be stored as numeric index
                return sampleTypeVal.Value switch
                {
                    1 => "RM",
                    2 => "STD",
                    3 => "BLK",
                    _ => "Sample"
                };
            }

            if (label.Contains("RM", StringComparison.OrdinalIgnoreCase)) return "RM";
            if (label.Contains("STD", StringComparison.OrdinalIgnoreCase)) return "STD";
            if (label.Contains("BLK", StringComparison.OrdinalIgnoreCase)) return "BLK";
            return "Sample";
        }

        private bool DetermineIfBad(AdvancedPivotRowDto row)
        {
            if (row.Values.TryGetValue("IsBad", out var isBad) && isBad.HasValue && isBad.Value == 1) return true;
            if (row.Values.TryGetValue("BadWeight", out var bw) && bw.HasValue && bw.Value == 1) return true;
            if (row.Values.TryGetValue("BadVolume", out var bv) && bv.HasValue && bv.Value == 1) return true;
            if (row.Values.TryGetValue("IsEmpty", out var empty) && empty.HasValue && empty.Value == 1) return true;
            return false;
        }

        private async Task ExportData(string format)
        {
            if (_projectId == null) return;
            _isLoading = true;
            StateHasChanged();

            try
            {
                var request = new ReportRequest
                {
                    ProjectId = _projectId.Value,
                    Format = format,
                    IncludeSummary = true,
                    IncludeCharts = false
                };
                var result = await ReportService.GenerateReportAsync(request);
                if (result.Succeeded && result.Data != null)
                {
                    await JSRuntime.InvokeVoidAsync("downloadFile", result.Data.FileName, result.Data.ContentType, result.Data.Base64Data);
                    Snackbar.Add($"Exported to {format.ToUpper()}", Severity.Success);
                }
                else
                {
                    Snackbar.Add(result.Message ?? $"Export to {format} failed", Severity.Error);
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        public class ResultRow
        {
            public int RowNumber { get; set; }
            public string SolutionLabel { get; set; } = "";
            public string SampleType { get; set; } = "";
            public bool IsBad { get; set; }
            public int PassedCount { get; set; }
            public int TotalElements { get; set; }
            public Dictionary<string, decimal> Values { get; set; } = new();
            public Dictionary<string, decimal> Diffs { get; set; } = new();
        }
        private async Task OnBeforeNavigation(LocationChangingContext context)
        {
            if (_isLoading)
            {
                context.PreventNavigation();
            }
        }
    }
}