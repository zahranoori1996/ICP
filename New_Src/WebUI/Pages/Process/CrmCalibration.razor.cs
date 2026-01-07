using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using WebUI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebUI.Pages.Process
{
    public partial class CrmCalibration
    {
        [Inject]
        private IDialogService DialogService { get; set; } = default!;

        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private string? _projectName;
        private bool _isLoading = false;

        // پارامترهای کنترل بالا
        private decimal _minDiff = -12m;
        private decimal _maxDiff = 12m;
        private int _decimalPlaces = 1;

        // داده‌های جدول Pivot
        private AdvancedPivotResultDto? _pivotData;
        private List<string> _displayColumns = new();

        // داده‌های CRM و بهینه‌سازی
        private List<CrmSelectionRowDto> _crmSelectionRows = new();

        // --- متغیرهای مربوط به پاپ‌آپ مرحله‌ای ---
        private bool _isCrmDialogVisible = false;
        private Queue<CrmSelectionRowDto> _conflictQueue = new(); // صف برای آیتم‌های تکراری
        private CrmSelectionRowDto? _currentConflictRow; // آیتم فعلی که دارد نمایش داده می‌شود
        private string? _tempSelectedCrmOption; // انتخابی که کاربر در پاپ‌آپ انجام داده
        private int _currentStepKey = 0;
        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ?? ProjectService.CurrentProjectId;
            if (!_projectId.HasValue) return;

            var projectResult = await ProjectService.GetProjectAsync(_projectId.Value);
            if (projectResult.Succeeded && projectResult.Data != null)
            {
                _projectName = projectResult.Data.ProjectName;
            }

            // لود همزمان داده‌های Pivot و CRM
            _isLoading = true;
            await Task.WhenAll(LoadPivotData(), LoadCrmSelections());
            _isLoading = false;
        }

        // 1. لود کردن داده‌های جدول بزرگ (مشابه صفحه Pivot)
        private async Task LoadPivotData()
        {
            if (_projectId == null) return;

            var request = new AdvancedPivotRequest(
                ProjectId: _projectId.Value,
                SearchText: null,
                SelectedElements: null,
                UseOxide: false,
                UseInt: false,
                DecimalPlaces: _decimalPlaces,
                Page: 1,
                PageSize: 1000, // تعداد زیاد برای نمایش همه مثل دسکتاپ
                MergeRepeats: false,
                Aggregation: "First",
                NumberFilters: null
            );

            var result = await PivotService.GetAdvancedPivotTableAsync(request);
            if (result.Succeeded && result.Data != null)
            {
                _pivotData = result.Data;
                _displayColumns = _pivotData.Columns;
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
        }

        // 2. منطق دکمه Check CRM
        private void StartCheckCrmProcess()
        {
            // پیدا کردن تمام CRMهایی که ابهام دارند (چند گزینه دارند)
            // یا هنوز انتخاب نشده‌اند
            var conflicts = _crmSelectionRows
                .Where(r => (GetRowAllOptions(r).Count() > 1) || string.IsNullOrEmpty(r.SelectedOption))
                .ToList();

            if (conflicts.Count == 0)
            {
                Snackbar.Add("No ambiguous CRMs found. All set!", Severity.Success);
                return;
            }

            // پر کردن صف
            _conflictQueue = new Queue<CrmSelectionRowDto>(conflicts);

            // شروع پروسه
            ShowNextConflict();
        }

      
        private void ShowNextConflict()
        {
            if (_conflictQueue.Count > 0)
            {
                _currentConflictRow = _conflictQueue.Dequeue();

                var options = GetRowAllOptions(_currentConflictRow).ToList();

                // انتخاب گزینه پیش‌فرض
                _tempSelectedCrmOption = !string.IsNullOrEmpty(_currentConflictRow.SelectedOption)
                    ? _currentConflictRow.SelectedOption
                    : options.FirstOrDefault();

                _currentStepKey++;
                _isCrmDialogVisible = true;
                StateHasChanged();
            }
            else
            {
                // پایان صف و بستن دیالوگ
                _isCrmDialogVisible = false;
                _currentConflictRow = null;

                _isLoading = true;
                StateHasChanged(); // برای نمایش وضعیت Loading در UI

                // 2. حالا await در اینجا بدون خطا کار می‌کند
                 LoadPivotData();

                _isLoading = false;
                Snackbar.Add("All CRMs checked and selected.", Severity.Success);
                StateHasChanged();
            }
        }

        // تایید انتخاب در پاپ‌آپ
        private void CancelCrmProcess()
        {
            // بستن دیالوگ
            _isCrmDialogVisible = false;

            // خالی کردن صف باقی‌مانده‌ها
            _conflictQueue.Clear();
            _currentConflictRow = null;

            Snackbar.Add("Process cancelled by user.", Severity.Info);
        }

        // تایید انتخاب در پاپ‌آپ (بدون تغییر)
        private async Task ConfirmCrmSelection()
        {
            if (_currentConflictRow != null && !string.IsNullOrEmpty(_tempSelectedCrmOption))
            {
                await SaveRowSelectionAsync(_currentConflictRow, _tempSelectedCrmOption);
                ShowNextConflict();
            }
        }
        //private void SkipCrmSelection()
        //{
        //    // بدون ذخیره رد می‌شویم
        //    ShowNextConflict();
        //}

        // هلپر برای گرفتن تمام گزینه‌ها
        private IEnumerable<string> GetRowAllOptions(CrmSelectionRowDto row)
        {
            return row.PreferredOptions.Concat(row.AllOptions).Distinct();
        }

        private async Task SaveRowSelectionAsync(CrmSelectionRowDto row, string selected)
        {
            if (_projectId == null) return;

            row.SelectedOption = selected; // آپدیت لوکال

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

            await OptimizationService.SaveCrmSelectionsAsync(request);
        }

        private async Task RunOptimization()
        {
            // ۱. بررسی اینکه آیا CRM انتخاب شده است یا خیر
            // اگر هیچ ردیفی انتخاب نشده باشد یا ابهامی باقی مانده باشد
            bool isCrmChecked = _crmSelectionRows.Any() && !_crmSelectionRows.Any(r => string.IsNullOrEmpty(r.SelectedOption));

            if (!isCrmChecked)
            {
                // نمایش هشدار مشابه تصویر ارائه شده
                await DialogService.ShowMessageBox("Warning", "No valid CRM selection found. Please check CRMs first.", yesText: "OK");
            }

            // ۲. تنظیم پارامترها برای ارسال به دیالوگ
            var parameters = new DialogParameters<CalibrationDialog>
    {
        // ارسال لیست ستون‌ها (Ag 328.068, ...) برای نمایش در MudSelect
        { x => x.Elements, _displayColumns.Where(c => c != "Solution Label").ToList() }
    };

            var options = new DialogOptions
            {
                MaxWidth = MaxWidth.ExtraLarge,
                FullWidth = true,
                CloseButton = true
            };

            // ۳. باز کردن دیالوگ
            await DialogService.ShowAsync<CalibrationDialog>("Verification Plot", parameters, options);
        }




        // جلوگیری از خروج هنگام لودینگ
        private async Task OnBeforeNavigation(LocationChangingContext context)
        {
            if (_isLoading) context.PreventNavigation();
        }
      
    }
}