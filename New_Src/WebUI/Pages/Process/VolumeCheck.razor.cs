using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using WebUI.Services;
using Application.DTOs;

namespace WebUI.Pages.Process
{
    public partial class VolumeCheck
    {
        [SupplyParameterFromQuery]
        public Guid? projectId
        {
            get; set;
        }

        private Guid? _projectId;
        private string? _projectName;

        private decimal _expectedVolume = 50m;
        private decimal _newVolume = 50m;
        private List<BadSampleDto>? _badSamples;
        private List<BadSampleDto> _pagedRows = new();
        private HashSet<BadSampleDto> _selectedSamples = new();

        private bool _isLoading = false;
        private bool _isApplying = false;

        // Pagination
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalCount = 0;
        private int _totalPages => _totalCount == 0 ? 1 : (int)Math.Ceiling((double)_totalCount / _pageSize);

        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ??
     ProjectService.CurrentProjectId;

            if (_projectId.HasValue)
            {
                var result = await ProjectService.GetProjectAsync(_projectId.Value);
                if (result.Succeeded && result.Data != null)
                {
                    _projectName = result.Data.ProjectName;
                }
            }
        }

        private async Task FindBadVolumes()
        {
            if (_projectId == null) return;
            _isLoading = true;
            _selectedSamples.Clear();
            StateHasChanged();

            var result = await CorrectionService.FindBadVolumesAsync(_projectId.Value, _expectedVolume);
            if (result.Succeeded && result.Data != null)
            {
                _badSamples = result.Data;
                _totalCount = _badSamples.Count;
            }
            else
            {
                Snackbar.Add(result.Message ?? "Failed", Severity.Error);
                _badSamples = new();
                _totalCount = 0;
            }

            _currentPage = 1;
            UpdatePagedRows();
            _isLoading = false;
        }

        private void UpdatePagedRows()
        {
            if (_badSamples == null)
            {
                _pagedRows = new();
                return;
            }

            _pagedRows = _badSamples
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

        private async Task ApplyCorrection()
        {
            if (_projectId == null || !_selectedSamples.Any()) return;
            _isApplying = true;
            StateHasChanged();

            var labels = _selectedSamples.Select(s => s.SolutionLabel).ToList();
            var result = await CorrectionService.ApplyVolumeCorrectionAsync(_projectId.Value, labels, _newVolume);
            if (result.Succeeded)
            {
                Snackbar.Add($"Corrected {result.Data?.CorrectedRows ?? 0} samples", Severity.Success);
                await FindBadVolumes();
            }
            else
            {
                Snackbar.Add(result.Message ?? "Failed", Severity.Error);
            }

            _isApplying = false;
        }

        private void SelectAll()
        {
            if (_badSamples != null)
            {
                _selectedSamples = new HashSet<BadSampleDto>(_badSamples);
            }
        }

        private void ClearSelection()
        {
            _selectedSamples.Clear();
        }

        private async Task OnBeforeNavigation(LocationChangingContext context)
        {
            if (_isApplying)
            {
                context.PreventNavigation();
            }
        }
    }
}