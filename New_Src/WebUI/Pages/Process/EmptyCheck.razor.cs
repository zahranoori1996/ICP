using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using WebUI.Services;
using EmptyRowDto = Application.DTOs.EmptyRowDto;

namespace WebUI.Pages.Process
{
    public partial class EmptyCheck
    {
        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private string? _projectName;
        private decimal _thresholdPercent = 70m;
        private List<EmptyRowDto>? _emptyRows;
        private List<EmptyRowDto> _pagedRows = new();
        private EmptyRowDto? _selectedEmpty;
        private HashSet<EmptyRowDto> _selectedRows = new();
        private bool _isLoading = false;
        private bool _isDeleting = false;
        private bool _detailsExpanded = true;

        // Progress tracking for delete
        private double _deleteProgress = 0;
        private int _deletedCount = 0;
        private int _totalToDelete = 0;

        // Pagination
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalCount = 0;
        private int _totalPages => _totalCount == 0 ? 1 : (int)Math.Ceiling((double)_totalCount / _pageSize);

        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ?? ProjectService.CurrentProjectId;
            if (_projectId.HasValue)
            {
                var result = await ProjectService.GetProjectAsync(_projectId.Value, includeLatestState: true);
                if (result.Succeeded && result.Data != null)
                {
                    _projectName = result.Data.ProjectName;
                }
            }
        }

        private async Task FindEmptyRows()
        {
            if (_projectId == null) return;
            _isLoading = true;
            _selectedRows.Clear();
            StateHasChanged();

            var result = await CorrectionService.FindEmptyRowsAsync(_projectId.Value, _thresholdPercent);
            if (result.Succeeded && result.Data != null)
            {
                _emptyRows = result.Data.OrderBy(x => x.OverallScore).ToList();
                _totalCount = _emptyRows.Count;
                _currentPage = 1;
                UpdatePagedRows();

                if (_emptyRows.Any())
                {
                    _selectedEmpty = _emptyRows.First();
                    Snackbar.Add($"Found {_emptyRows.Count} empty/outlier samples", Severity.Warning);
                }
            }
            else
            {
                Snackbar.Add(result.Message ?? "Failed", Severity.Error);
                _emptyRows = new();
                _totalCount = 0;
            }

            _isLoading = false;
        }

        private void UpdatePagedRows()
        {
            if (_emptyRows == null) return;
            _pagedRows = _emptyRows
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

        private void SelectRow(EmptyRowDto row)
        {
            _selectedEmpty = row;
        }

        private void ToggleRowSelection(EmptyRowDto row, bool selected)
        {
            if (selected)
                _selectedRows.Add(row);
            else
                _selectedRows.Remove(row);
        }

        private bool IsAllSelectedOnPage()
        {
            return _pagedRows.Any() && _pagedRows.All(r => _selectedRows.Contains(r));
        }

        private void ToggleSelectAllOnPage(bool selectAll)
        {
            if (selectAll)
            {
                foreach (var row in _pagedRows)
                    _selectedRows.Add(row);
            }
            else
            {
                foreach (var row in _pagedRows)
                    _selectedRows.Remove(row);
            }
        }

        private void SelectAllOnPage()
        {
            foreach (var row in _pagedRows)
                _selectedRows.Add(row);
        }

        private void SelectAllRows()
        {
            if (_emptyRows == null) return;
            foreach (var row in _emptyRows)
                _selectedRows.Add(row);
        }

        private void ClearSelection()
        {
            _selectedRows.Clear();
        }

        private async Task DeleteAllRows()
        {
            if (_emptyRows == null || !_emptyRows.Any() || _projectId == null) return;
            var confirm = await DialogService.ShowMessageBox(
                "⚠️ Delete ALL Empty Rows",
                $"Are you sure you want to delete ALL {_totalCount} empty/outlier rows?\n\nThis action CANNOT be undone!",
                yesText: "Yes, Delete All",
                cancelText: "Cancel");
            if (confirm != true) return;

            // دومین تأیید برای اطمینان
            var doubleConfirm = await DialogService.ShowMessageBox(
                "⚠️ Final Confirmation",
                $"This will permanently delete {_totalCount} rows. Are you absolutely sure?",
                yesText: "DELETE",
                cancelText: "Cancel");
            if (doubleConfirm != true) return;

            _isDeleting = true;
            _deleteProgress = 0;
            _deletedCount = 0;
            StateHasChanged();

            // حذف به صورت دسته‌ای (batch)
            var allLabels = _emptyRows.Select(r => r.SolutionLabel).ToList();
            var batchSize = 500;
            _totalToDelete = allLabels.Count;
            var totalDeleted = 0;

            for (int i = 0; i < allLabels.Count; i += batchSize)
            {
                var batch = allLabels.Skip(i).Take(batchSize).ToList();
                var result = await CorrectionService.DeleteRowsAsync(_projectId.Value, batch);

                if (result.Succeeded)
                {
                    totalDeleted += result.Data;
                    _deletedCount = totalDeleted;
                    _deleteProgress = (double)totalDeleted / _totalToDelete * 100;
                    StateHasChanged();
                    Snackbar.Add($"Batch {i / batchSize + 1}: Deleted {result.Data} rows ({_deleteProgress:F0}%)", Severity.Info);
                }
                else
                {
                    Snackbar.Add($"Failed to delete batch: {result.Message}", Severity.Error);
                    break;
                }
            }

            if (totalDeleted > 0)
            {
                Snackbar.Add($"✅ Successfully deleted {totalDeleted} rows total!", Severity.Success);
                _emptyRows?.Clear();
                _selectedRows.Clear();
                _totalCount = 0;
                UpdatePagedRows();
            }

            _isDeleting = false;
            _deleteProgress = 0;
            _deletedCount = 0;
            _totalToDelete = 0;
        }

        private async Task DeleteSelectedRows()
        {
            if (!_selectedRows.Any() || _projectId == null) return;
            var confirm = await DialogService.ShowMessageBox(
                "Confirm Delete",
                $"Are you sure you want to delete {_selectedRows.Count} selected rows? This action cannot be undone.",
                yesText: "Delete",
                cancelText: "Cancel");
            if (confirm != true) return;

            _isDeleting = true;
            StateHasChanged();

            var labels = _selectedRows.Select(r => r.SolutionLabel).ToList();
            var result = await CorrectionService.DeleteRowsAsync(_projectId.Value, labels);

            if (result.Succeeded)
            {
                Snackbar.Add($"Deleted {result.Data} rows successfully", Severity.Success);
                foreach (var row in _selectedRows.ToList())
                {
                    _emptyRows?.Remove(row);
                }
                _selectedRows.Clear();
                _totalCount = _emptyRows?.Count ?? 0;
                UpdatePagedRows();
            }
            else
            {
                Snackbar.Add(result.Message ?? "Failed to delete rows", Severity.Error);
            }

            _isDeleting = false;
        }

        private async Task DeleteSingleRow(EmptyRowDto row)
        {
            if (_projectId == null) return;
            var confirm = await DialogService.ShowMessageBox(
                "Confirm Delete",
                $"Are you sure you want to delete '{row.SolutionLabel}'?",
                yesText: "Delete",
                cancelText: "Cancel");
            if (confirm != true) return;
            _isDeleting = true;
            StateHasChanged();
            var result = await CorrectionService.DeleteRowsAsync(_projectId.Value, new List<string> { row.SolutionLabel });

            if (result.Succeeded)
            {
                Snackbar.Add($"Deleted '{row.SolutionLabel}'", Severity.Success);
                _emptyRows?.Remove(row);
                _selectedRows.Remove(row);
                _totalCount = _emptyRows?.Count ?? 0;
                UpdatePagedRows();

            }
            else
            {
                Snackbar.Add(result.Message ?? "Failed to delete", Severity.Error);
            }
            _isDeleting = false;
        }

        private Color GetScoreColor(decimal score)
        {
            if (score < 30) return Color.Error;
            if (score < 50) return Color.Warning;
            return Color.Info;
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
