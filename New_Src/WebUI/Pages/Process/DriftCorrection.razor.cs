using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages.Process
{
    public partial class DriftCorrection
    {
        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private string? _projectName;

        private DriftMethod _method = DriftMethod.Linear;
        private IEnumerable<string> _selectedElements = new HashSet<string>();
        private List<string> _allElements = new();
        private bool _useSegmentation = true;
        private DriftCorrectionResult? _analysisResult;

        private bool _isLoading = false;
        private bool _isApplying = false;

        // Pagination for ElementDrifts
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalCount = 0;
        private int _totalPages => _totalCount == 0
            ? 1
            : (int)Math.Ceiling((double)_totalCount / _pageSize);

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
                await LoadElements();
            }
        }

        private async Task LoadElements()
        {
            var result = await PivotService.GetElementsAsync(_projectId!.Value);
            if (result.Succeeded && result.Data != null)
            {
                _allElements = result.Data;
            }
        }

        private async Task AnalyzeDrift()
        {
            if (_projectId == null) return;

            _isLoading = true;
            StateHasChanged();

            var request = new DriftCorrectionRequest
            {
                ProjectId = _projectId.Value,
                Method = _method,
                UseSegmentation = _useSegmentation,
                SelectedElements = _selectedElements.Any() ? _selectedElements.ToList() : null,
                Keyword = "RM"
            };

            var result = await DriftService.AnalyzeDriftAsync(request);

            if (result.Succeeded && result.Data != null)
            {
                _analysisResult = result.Data;
                _totalCount = _analysisResult.ElementDrifts.Count;
                _currentPage = 1;

                Snackbar.Add($"Analyzed {_analysisResult.ElementDrifts.Count} elements", Severity.Success);
            }
            else
            {
                _analysisResult = null;
                _totalCount = 0;
                Snackbar.Add(result.Message ?? "Analysis failed", Severity.Error);
            }

            _isLoading = false;
        }

        private async Task ApplyCorrection()
        {
            if (_projectId == null) return;

            _isApplying = true;
            StateHasChanged();

            var request = new DriftCorrectionRequest
            {
                ProjectId = _projectId.Value,
                Method = _method,
                UseSegmentation = _useSegmentation,
                SelectedElements = _selectedElements.Any() ? _selectedElements.ToList() : null,
                Keyword = "RM"
            };

            var result = await DriftService.ApplyDriftCorrectionAsync(request);

            if (result.Succeeded && result.Data != null)
            {
                _analysisResult = result.Data;
                _totalCount = _analysisResult.ElementDrifts.Count;
                _currentPage = 1;

                Snackbar.Add($"Corrected {_analysisResult.CorrectedSamples} samples", Severity.Success);
            }
            else
            {
                Snackbar.Add(result.Message ?? "Correction failed", Severity.Error);
            }

            _isApplying = false;
        }

        private IEnumerable<dynamic> GetPagedElementDrifts()
        {
            if (_analysisResult == null || !_analysisResult.ElementDrifts.Any())
                return Enumerable.Empty<dynamic>();

            return _analysisResult.ElementDrifts.Values
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize);
        }

        private Color GetDriftColor(decimal drift)
        {
            var abs = Math.Abs(drift);
            if (abs < 2) return Color.Success;
            if (abs < 5) return Color.Warning;
            return Color.Error;
        }

        private void OnPageChanged(int page)
        {
            _currentPage = page;
            StateHasChanged();
        }

        private void OnPageSizeChanged(int size)
        {
            _pageSize = size;
            _currentPage = 1;
            StateHasChanged();
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
