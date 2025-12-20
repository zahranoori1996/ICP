using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages.Process
{
    public partial class CrmCalibration
    {
        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private decimal _minDiff = -10m;
        private decimal _maxDiff = 10m;
        private int _maxIterations = 100;
        private int _populationSize = 50;
        private bool _useMultiModel = true;
        private bool _detailsExpanded = true;
        private IEnumerable<string> _selectedElements = new HashSet<string>();
        private List<string> _allElements = new();
        private BlankScaleOptimizationResult? _result;
        private bool _isLoading = false;
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

        private async Task GetCurrentStats()
        {
            if (_projectId == null) return;

            _isLoading = true;
            StateHasChanged();

            var result = await OptimizationService.GetCurrentStatsAsync(_projectId.Value, _minDiff, _maxDiff);

            if (result.Succeeded && result.Data != null)
            {
                _result = result.Data;
            }
            else
            {
                Snackbar.Add(result.Message ?? "Failed to get stats", Severity.Error);
            }

            _isLoading = false;
        }

        private async Task RunOptimization()
        {
            if (_projectId == null) return;

            _isLoading = true;
            StateHasChanged();

            var request = new BlankScaleOptimizationRequest
            {
                ProjectId = _projectId.Value,
                MinDiffPercent = _minDiff,
                MaxDiffPercent = _maxDiff,
                MaxIterations = _maxIterations,
                PopulationSize = _populationSize,
                UseMultiModel = _useMultiModel,
                Elements = _selectedElements.Any() ? _selectedElements.ToList() : null
            };

            var result = await OptimizationService.OptimizeAsync(request);

            if (result.Succeeded && result.Data != null)
            {
                _result = result.Data;
                Snackbar.Add($"Optimization complete! Improvement: {_result.ImprovementPercent:F1}%", Severity.Success);
            }
            else
            {
                Snackbar.Add(result.Message ?? "Optimization failed", Severity.Error);
            }

            _isLoading = false;
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