using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using WebUI.Services;
using System;

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
        private string? _focusElement;
        private decimal _previewBlank = 0m;
        private double _previewScale = 1.0;
        private string _sampleFilter = "";
        private BlankScaleOptimizationResult? _result;
        private ManualBlankScaleResult? _manualResult;
        private List<OptimizedSampleRow> _optimizedRows = new();
        private List<OptimizedSampleRow> _manualRows = new();
        private bool _isLoading = false;
        private string? _projectName;
        private List<CrmMethodOptionDto> _crmOptions = new();
        private Dictionary<string, string> _crmSelections = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _includedCrmIds = new(StringComparer.OrdinalIgnoreCase);
        private string _excludedLabelsInput = string.Empty;
        private List<CrmSelectionRowDto> _crmSelectionRows = new();

        // Scale Application Range (Python feature)
        private decimal? _scaleRangeMin;
        private decimal? _scaleRangeMax;
        private bool _scaleAbove50Only = false;

        // Acceptable Ranges (Python feature - magnitude-based thresholds)
        private decimal _rangeLow = 2.0m;     // |x| < 10: absolute ±
        private decimal _rangeMid = 20.0m;    // 10 ≤ |x| < 100: percentage
        private decimal _rangeHigh1 = 10.0m;  // 100 ≤ |x| < 1000: percentage
        private decimal _rangeHigh2 = 8.0m;   // 1000 ≤ |x| < 10000: percentage
        private decimal _rangeHigh3 = 5.0m;   // 10000 ≤ |x| < 100000: percentage
        private decimal _rangeHigh4 = 3.0m;   // |x| ≥ 100000: percentage
        private bool _rangesDialogVisible = false;


        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ?? ProjectService.CurrentProjectId;
            if (!_projectId.HasValue)
                return;

            var projectResult = await ProjectService.GetProjectAsync(_projectId.Value);
            if (projectResult.Succeeded && projectResult.Data != null)
            {
                _projectName = projectResult.Data.ProjectName;
            }
            else if (!string.IsNullOrWhiteSpace(projectResult.Message))
            {
                Snackbar.Add(projectResult.Message, Severity.Warning);
            }

            await LoadElements();
            await LoadCrmOptions();
            await LoadCrmSelections();
            //await GetCurrentStats();
        }

        private async Task LoadCrmOptions()
        {
            if (_projectId == null) return;

            var result = await OptimizationService.GetCrmOptionsAsync(_projectId.Value);
            if (result.Succeeded && result.Data != null)
            {
                _crmOptions = result.Data.Items;
                _crmSelections.Clear();
                _includedCrmIds.Clear();

                foreach (var option in _crmOptions)
                {
                    if (!string.IsNullOrWhiteSpace(option.DefaultMethod))
                    {
                        _crmSelections[option.CrmId] = option.DefaultMethod!;
                    }
                    _includedCrmIds.Add(option.CrmId);
                }
            }
            else if (!string.IsNullOrWhiteSpace(result.Message))
            {
                Snackbar.Add(result.Message, Severity.Warning);
            }
        }

        private async Task LoadCrmSelections()
        {
            if (_projectId == null) return;

            var result = await OptimizationService.GetCrmSelectionOptionsAsync(_projectId.Value);
            if (result.Succeeded && result.Data != null)
            {
                _crmSelectionRows = result.Data.Items;
            }
            else if (!string.IsNullOrWhiteSpace(result.Message))
            {
                Snackbar.Add(result.Message, Severity.Warning);
            }
        }

        private List<string> GetRowOptions(CrmSelectionRowDto row)
        {
            var options = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var opt in row.PreferredOptions)
            {
                if (seen.Add(opt))
                    options.Add(opt);
            }

            foreach (var opt in row.AllOptions)
            {
                if (seen.Add(opt))
                    options.Add(opt);
            }

            if (!string.IsNullOrWhiteSpace(row.SelectedOption) && seen.Add(row.SelectedOption))
                options.Insert(0, row.SelectedOption);

            return options;
        }

        private EventCallback<string> GetRowSelectionChangedHandler(CrmSelectionRowDto row)
        {
            return EventCallback.Factory.Create<string>(this, v => SaveRowSelectionAsync(row, v));
        }

        private async Task SaveRowSelectionAsync(CrmSelectionRowDto row, string? selected)
        {
            if (_projectId == null || string.IsNullOrWhiteSpace(selected))
                return;

            row.SelectedOption = selected;

            var request = new CrmSelectionSaveRequest
            {
                ProjectId = _projectId.Value,
                Selections = new List<CrmSelectionItemDto>
                {
                    new CrmSelectionItemDto
                    {
                        SolutionLabel = row.SolutionLabel,
                        RowIndex = row.RowIndex,
                        SelectedCrmKey = selected
                    }
                }
            };

            var result = await OptimizationService.SaveCrmSelectionsAsync(request);
            if (!result.Succeeded)
            {
                Snackbar.Add(result.Message ?? "Failed to save CRM selection", Severity.Error);
            }
        }

        private string? GetCrmSelection(string crmId)
        {
            return _crmSelections.TryGetValue(crmId, out var method) ? method : null;
        }

        private void SetCrmSelection(string crmId, string? method)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                _crmSelections.Remove(crmId);
                return;
            }

            _crmSelections[crmId] = method;
        }

        private void ToggleIncludedCrmId(string crmId, bool isIncluded)
        {
            if (isIncluded)
                _includedCrmIds.Add(crmId);
            else
                _includedCrmIds.Remove(crmId);
        }

        private List<string> ParseExcludedLabels()
        {
            if (string.IsNullOrWhiteSpace(_excludedLabelsInput))
                return new List<string>();

            return _excludedLabelsInput
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task LoadElements()
        {
            var result = await PivotService.GetElementsAsync(_projectId!.Value);
            if (result.Succeeded && result.Data != null)
            {
                _allElements = result.Data;
                if (_allElements.Count > 0 && string.IsNullOrWhiteSpace(_focusElement))
                {
                    _focusElement = _allElements[0];
                }
            }
            else
            {
                Snackbar.Add(result.Message ?? "Failed to load elements", Severity.Error);
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
                UpdateOptimizedRows();
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

            if (_crmSelectionRows.Any(r => string.IsNullOrWhiteSpace(r.SelectedOption)))
            {
                Snackbar.Add("Please select CRM method for all CRM rows before optimization.", Severity.Warning);
                _isLoading = false;
                return;
            }

            var request = new BlankScaleOptimizationRequest
            {
                ProjectId = _projectId.Value,
                MinDiffPercent = _minDiff,
                MaxDiffPercent = _maxDiff,
                MaxIterations = _maxIterations,
                PopulationSize = _populationSize,
                UseMultiModel = _useMultiModel,
                Elements = _selectedElements.Any() ? _selectedElements.ToList() : null,
                // Acceptable Ranges (Python: calculate_dynamic_range)
                RangeLow = _rangeLow,
                RangeMid = _rangeMid,
                RangeHigh1 = _rangeHigh1,
                RangeHigh2 = _rangeHigh2,
                RangeHigh3 = _rangeHigh3,
                RangeHigh4 = _rangeHigh4,
                // Scale Application Range
                ScaleRangeMin = _scaleRangeMin,
                ScaleRangeMax = _scaleRangeMax,
                ScaleAbove50Only = _scaleAbove50Only,
                CrmSelections = _crmSelections.Count > 0 ? new Dictionary<string, string>(_crmSelections) : null,
                IncludedCrmIds = _includedCrmIds.Count > 0 ? _includedCrmIds.ToList() : null,
                ExcludedSolutionLabels = ParseExcludedLabels()
            };

            var result = await OptimizationService.OptimizeAsync(request);

            if (result.Succeeded && result.Data != null)
            {
                _result = result.Data;
                UpdateOptimizedRows();
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

        private async Task PreviewManualAsync()
        {
            if (_projectId == null || string.IsNullOrWhiteSpace(_focusElement))
                return;

            _isLoading = true;
            StateHasChanged();

            var result = await OptimizationService.PreviewManualDetailsAsync(
                _projectId.Value,
                _focusElement,
                _previewBlank,
                (decimal)_previewScale);

            if (result.Succeeded && result.Data != null)
            {
                _manualResult = result.Data;
                UpdateManualRows();
            }
            else
            {
                Snackbar.Add(result.Message ?? "Preview failed", Severity.Error);
            }

            _isLoading = false;
        }

        private async Task ApplyManualAsync()
        {
            if (_projectId == null || string.IsNullOrWhiteSpace(_focusElement))
                return;

            _isLoading = true;
            StateHasChanged();

            var result = await OptimizationService.ApplyManualAsync(
                _projectId.Value,
                _focusElement,
                _previewBlank,
                (decimal)_previewScale);

            if (result.Succeeded && result.Data != null)
            {
                _manualResult = result.Data;
                UpdateManualRows();
                Snackbar.Add("Manual blank/scale applied.", Severity.Success);
            }
            else
            {
                Snackbar.Add(result.Message ?? "Apply failed", Severity.Error);
            }

            _isLoading = false;
        }

        private async Task UndoManualAsync()
        {
            if (_projectId == null)
                return;

            _isLoading = true;
            StateHasChanged();

            var result = await CorrectionService.UndoLastCorrectionAsync(_projectId.Value);
            if (result.Succeeded)
            {
                Snackbar.Add("Undo applied.", Severity.Success);
                await GetCurrentStats();
            }
            else
            {
                Snackbar.Add(result.Message ?? "Undo failed", Severity.Error);
            }

            _isLoading = false;
        }

        private void ResetPreview()
        {
            _previewBlank = 0m;
            _previewScale = 1.0;
        }

        private void SetFocusElement(string? element)
        {
            if (string.IsNullOrWhiteSpace(element))
                return;

            _focusElement = element;
            UpdateOptimizedRows();
            UpdateManualRows();
        }

        private void PrevElement()
        {
            if (_allElements.Count == 0 || string.IsNullOrWhiteSpace(_focusElement))
                return;

            var idx = _allElements.IndexOf(_focusElement);
            if (idx > 0)
                SetFocusElement(_allElements[idx - 1]);
        }

        private void NextElement()
        {
            if (_allElements.Count == 0 || string.IsNullOrWhiteSpace(_focusElement))
                return;

            var idx = _allElements.IndexOf(_focusElement);
            if (idx < _allElements.Count - 1)
                SetFocusElement(_allElements[idx + 1]);
        }

        private void UpdateOptimizedRows()
        {
            _optimizedRows = BuildRows(_result?.OptimizedData, _focusElement);
        }

        private void UpdateManualRows()
        {
            _manualRows = BuildRows(_manualResult?.OptimizedData, _focusElement);
        }

        private List<OptimizedSampleRow> BuildRows(IEnumerable<OptimizedSampleDto>? data, string? element)
        {
            if (data == null || string.IsNullOrWhiteSpace(element))
                return new List<OptimizedSampleRow>();

            var rows = new List<OptimizedSampleRow>();
            foreach (var sample in data)
            {
                sample.OriginalValues.TryGetValue(element, out var original);
                sample.OptimizedValues.TryGetValue(element, out var optimized);
                sample.CrmValues.TryGetValue(element, out var crmValue);
                sample.DiffPercentBefore.TryGetValue(element, out var diffBefore);
                sample.DiffPercentAfter.TryGetValue(element, out var diffAfter);
                var passed = sample.PassStatusAfter.TryGetValue(element, out var p) && p;

                if (original == null && optimized == null && crmValue == null)
                    continue;

                rows.Add(new OptimizedSampleRow(
                    sample.SolutionLabel,
                    sample.CrmId,
                    element,
                    original,
                    optimized,
                    crmValue,
                    diffBefore,
                    diffAfter,
                    passed));
            }

            return rows;
        }

        private IEnumerable<OptimizedSampleRow> FilterRows(IEnumerable<OptimizedSampleRow> rows)
        {
            if (string.IsNullOrWhiteSpace(_sampleFilter))
                return rows;

            return rows.Where(r =>
                r.SolutionLabel.Contains(_sampleFilter, StringComparison.OrdinalIgnoreCase));
        }

        private sealed record OptimizedSampleRow(
            string SolutionLabel,
            string CrmId,
            string Element,
            decimal? OriginalValue,
            decimal? OptimizedValue,
            decimal? CrmValue,
            decimal DiffBefore,
            decimal DiffAfter,
            bool IsPassed);

        /// <summary>
        /// Opens the Acceptable Ranges dialog (matches Python's open_range_dialog)
        /// </summary>
        private void OpenRangesDialog()
        {
            _rangesDialogVisible = true;
        }

        /// <summary>
        /// Closes the Acceptable Ranges dialog
        /// </summary>
        private void CloseRangesDialog()
        {
            _rangesDialogVisible = false;
        }

        /// <summary>
        /// Applies the ranges and refreshes statistics
        /// </summary>
        private async Task ApplyRangesAsync()
        {
            _rangesDialogVisible = false;
            await GetCurrentStats();
            Snackbar.Add("Acceptable ranges updated", Severity.Success);
        }

        /// <summary>
        /// Resets the ranges to default values
        /// </summary>
        private void ResetRanges()
        {
            _rangeLow = 2.0m;
            _rangeMid = 20.0m;
            _rangeHigh1 = 10.0m;
            _rangeHigh2 = 8.0m;
            _rangeHigh3 = 5.0m;
            _rangeHigh4 = 3.0m;
        }


        private string GetSampleDetailsTitle()
        {
            if (_manualRows.Any())
                return "Manual Preview Details";
            return "Optimized Sample Details";
        }

        private string GetImprovementCardClass()
        {
            var baseClass = "summary-card";
            var statusClass = _result?.ImprovementPercent >= 0 ? "success" : "error";
            return $"{baseClass} {statusClass}";
        }
    }
}
