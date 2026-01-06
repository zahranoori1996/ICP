using Application.DTOs;
using Application.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using WebUI.Services;
using System.Text.Json;
using WebUI.Models;
using WebUI.Shared;

namespace WebUI.Pages.App
{
    public partial class Import
    {
        // کنترل نمایش پنجره دو تبی (وقتی یک فایل انتخاب شد)
        private bool isWindowOpen = false;

        // بعد از آپلود موفق: دکمه آپلود غیرفعال بماند
        private bool uploadCompleted = false;

        // ============================================
        // متغیرهای بخش آپلود (Tab 1)
        // ============================================
        private IBrowserFile? selectedFile;
        private byte[]? fileContent;
        private string fileName = "";

        // نام نمایشی فایل (برای نام پروژه)
        private string displayFileName = "";

        private string fileContentType = "";
        private long fileSizeBytes;

        private bool isViewer = false;
        private bool isLoading;

        private string description = "";
        private string selectedDevice = "";
        private string selectedFileType = "";
        private bool skipLastRow = true;

        private List<string> deviceOptions = new() { "Mass elan9000 1", "Mass elan9000 2", "OES 715", "OES 735 1", "OES 735 2" };
        private List<string> fileTypeOptions = new() { "oes 4cc", "oes 6cc", "txt format", "xlsx format" };

        private AnalysisPreviewResult? analysisData;

        // ============================================
        // متغیرهای بخش مدیریت فایل‌ها (Tab 2)
        // ============================================
        private string filterContract = "";
        private List<ProjectUiModel> existingFiles = new();
        private ProjectUiModel? selectedItem;
        private bool isItemSelected => selectedItem != null;

        // ============================================
        // متدها
        // ============================================

        protected override void OnInitialized()
        {
            var user = AuthService.GetCurrentUser();
            if (user == null || !user.IsAuthenticated)
            {
                NavManager.NavigateTo("/login");
                return;
            }
            if (string.Equals(user.Position, "Viewer", StringComparison.OrdinalIgnoreCase)) isViewer = true;

            selectedDevice = deviceOptions.FirstOrDefault() ?? "";
            selectedFileType = fileTypeOptions.FirstOrDefault() ?? "";

            // demo data
            existingFiles = new List<ProjectUiModel>
            {
                new ProjectUiModel
                {
                    Id = Guid.NewGuid(),
                    ProjectName = "1404-08-26 oes RET 1713 1745 1748 22009",
                    CreatedBy = "Device Operator",
                    Created = DateTime.Now.AddDays(-1),
                    Device = "Mass elan9000 1"
                },
                new ProjectUiModel
                {
                    Id = Guid.NewGuid(),
                    ProjectName = "1404-09-01 oes RET 1714 (test file)",
                    CreatedBy = "Admin",
                    Created = DateTime.Now,
                    Device = "OES 735 1"
                }
            };
        }

        private void GoToDashboard() => NavManager.NavigateTo("/dashboard");

        private async Task OnInputFileChangeStandard(InputFileChangeEventArgs e)
        {
            if (isViewer) return;
            var file = e.File;
            if (file == null) return;
            await ProcessFile(file);
        }

        private async Task ProcessFile(IBrowserFile file)
        {
            isLoading = true;
            StateHasChanged();

            try
            {
                selectedFile = file;
                fileName = file.Name;
                displayFileName = file.Name; // نام پیش‌فرض پروژه
                fileContentType = file.ContentType ?? "application/octet-stream";
                fileSizeBytes = file.Size;

                using var ms = new MemoryStream();
                await file.OpenReadStream(maxAllowedSize: 200 * 1024 * 1024).CopyToAsync(ms);
                fileContent = ms.ToArray();

                await DoPreview();
                // باز نگه داشتن پنجره دو تب بعد از پردازش فایل
                isWindowOpen = true;
                // Load existing projects for Manage tab
                await LoadExistingFilesAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error reading file: {ex.Message}", Severity.Error);
                ClearFile();
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task ReselectFile(InputFileChangeEventArgs e)
        {
            await OnInputFileChangeStandard(e);
        }

        private void ClearFile()
        {
            selectedFile = null;
            fileContent = null;
            fileName = "";
            displayFileName = "";
            fileContentType = "";
            fileSizeBytes = 0;
            analysisData = null;
            description = "";
            // بستن پنجره دو تب وقتی کاربر Close یا X را می‌زند
            isWindowOpen = false;
            // ریست وضعیت آپلود
            uploadCompleted = false;
            selectedItem = null;
        }

        private async Task DoPreview()
        {
            if (fileContent == null || fileContent.Length == 0) return;
            try
            {
                using var ms = new MemoryStream(fileContent);
                var result = await ImportService.AnalyzeFileAsync(ms, fileName);
                if (result.Succeeded && result.Data != null)
                {
                    analysisData = result.Data;
                    Snackbar.Add("Analysis completed", Severity.Success);
                }
                else
                {
                    var errorMsg = result.Messages?.FirstOrDefault() ?? "Analysis failed";
                    Snackbar.Add(errorMsg, Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error: {ex.Message}", Severity.Error);
            }
        }

        private async Task ImportFile()
        {
            if (isViewer)
            {
                Snackbar.Add("Viewers cannot import data.", Severity.Error);
                return;
            }

            if (fileContent == null) return;

            isLoading = true;
            StateHasChanged();

            try
            {
                var user = AuthService.GetCurrentUser();
                using var stream = new MemoryStream(fileContent);

                // استفاده از نام ویرایش شده توسط کاربر
                var finalProjectName = string.IsNullOrWhiteSpace(displayFileName)
                                     ? fileName
                                     : displayFileName;
                if (string.IsNullOrEmpty(selectedDevice) || string.IsNullOrEmpty(selectedFileType))
                {
                    Snackbar.Add("Please select Device and File Type before uploading.", Severity.Warning);
                    return;
                }
                var request = new AdvancedImportRequest(finalProjectName, user?.Name)
                {
                    SkipLastRow = skipLastRow,
                    Device = selectedDevice,
                    FileType = selectedFileType,
                    Description = description
                };

                var result = await ImportService.ImportAdvancedAsync(stream, fileName, request);

                if (result.Succeeded)
                {
                    Snackbar.Add($"Project '{finalProjectName}' imported successfully!", Severity.Success);
                    // علامت می‌زنیم که عملیات آپلود کامل شده تا دکمه Upload غیرفعال بماند
                    uploadCompleted = true;
                    // رفرش لیست پروژه‌های موجود از سرور/دیتابیس
                    await LoadExistingFilesAsync();
                }
                else
                {
                    var errorMsg = result.Messages?.FirstOrDefault() ?? "Import failed";
                    Snackbar.Add(errorMsg, Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error: {ex.Message}", Severity.Error);
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task LoadExistingFilesAsync()
        {
            try
            {
                var res = await ProjectService.GetProjectsAsync();
                if (res.Succeeded && res.Data != null)
                {
                    existingFiles = res.Data.Select(p =>
                    {
                        // 1. دریافت مقادیر از دیتابیس
                        string finalDevice = p.Device;
                        string finalFileType = p.FileType;

                        // 2. اگر دیتابیس خالی بود، از روی نام پروژه حدس بزن (برای نمایش در جدول)
                        if (string.IsNullOrWhiteSpace(finalDevice) || string.IsNullOrWhiteSpace(finalFileType))
                        {
                            var inferred = InferDeviceAndFileTypeFromName(p.ProjectName ?? "");

                            if (string.IsNullOrWhiteSpace(finalDevice)) finalDevice = inferred.device;
                            if (string.IsNullOrWhiteSpace(finalFileType)) finalFileType = inferred.fileType;
                        }

                        return new ProjectUiModel
                        {
                            Id = p.ProjectId,
                            ProjectName = p.ProjectName,
                            CreatedBy = p.Owner ?? "",
                            Created = p.CreatedAt,
                            Device = finalDevice,   // مقدار یا از دیتابیس است یا حدس زده شده
                            FileType = finalFileType, // مقدار یا از دیتابیس است یا حدس زده شده
                            Description = p.Description ?? ""
                        };
                    }).ToList();
                }
                else
                {
                    Snackbar.Add(res.Message ?? "Failed to load projects", Severity.Warning);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error loading projects: {ex.Message}", Severity.Error);
            }
            StateHasChanged();
        }

        private void OnRowClicked(TableRowClickEventArgs<ProjectUiModel> args)
        {
            selectedItem = args.Item;
            StateHasChanged();
        }

        private string RowClassFunc(ProjectUiModel item, int rowNumber) =>
     item == selectedItem ? "selected-row" : string.Empty;

        // Try to infer device and file type from project name using known options
        private (string device, string fileType) InferDeviceAndFileTypeFromName(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName)) return (string.Empty, string.Empty);
            var name = projectName.ToLowerInvariant();
            string foundDevice = string.Empty;
            string foundFileType = string.Empty;

            foreach (var d in deviceOptions)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                if (name.Contains(d.ToLowerInvariant().Split(' ')[0]))
                {
                    foundDevice = d;
                    break;
                }
            }

            foreach (var ft in fileTypeOptions)
            {
                if (string.IsNullOrWhiteSpace(ft)) continue;
                if (name.Contains(ft.ToLowerInvariant().Split(' ')[0]))
                {
                    foundFileType = ft;
                    break;
                }
            }

            return (foundDevice, foundFileType);
        }

        private async Task ConfirmDelete()
        {
            if (selectedItem == null) return;

            var parameters = new MudBlazor.DialogParameters { ["ProjectName"] = selectedItem.ProjectName };
            var options = new MudBlazor.DialogOptions { CloseButton = false };
            var dialog = await DialogService.ShowAsync<ConfirmDeleteDialog>("Are you sure?", parameters, options);
            var res = await dialog.Result;
            if (res != null && !res.Canceled)
            {
                // Check admin role
                var user = AuthService.GetCurrentUser();
                if (user != null && string.Equals(user.Position, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    var serverRes = await ProjectService.DeleteProjectAsync(selectedItem.Id);
                    if (serverRes.Succeeded)
                    {
                        Snackbar.Add("Project deleted.", Severity.Success);
                        await LoadExistingFilesAsync();
                        selectedItem = null;
                    }
                    else
                    {
                        Snackbar.Add(serverRes.Message ?? "Failed to delete project.", Severity.Error);
                    }
                }
                else
                {
                    Snackbar.Add("You do not have permission to delete projects.", Severity.Error);
                }
            }
        }

        private async Task OpenEditDialog()
        {
            if (selectedItem == null) return;

            // 1. ابتدا سعی می‌کنیم آخرین وضعیت را از سرور بگیریم
            var projRes = await ProjectService.GetProjectAsync(selectedItem.Id);

            // مدلی که قرار است به فرم ارسال شود
            ProjectUiModel dialogModel = new ProjectUiModel
            {
                Id = selectedItem.Id,
                ProjectName = selectedItem.ProjectName,
                Description = selectedItem.Description
            };

            // مقادیر پیش‌فرض را از آیتم انتخاب شده در جدول می‌گیریم (که ممکن است حدس زده شده باشند)
            string deviceToUse = selectedItem.Device;
            string fileTypeToUse = selectedItem.FileType;

            // 2. بررسی پاسخ سرور
            if (projRes.Succeeded && projRes.Data != null)
            {
                var dbData = projRes.Data;
                dialogModel.ProjectName = dbData.ProjectName;
                dialogModel.Description = dbData.Description ?? ""; // دیسکریپشن اختیاری است

                // اگر در دیتابیس مقدار واقعی ذخیره شده بود، آن ارجحیت دارد
                if (!string.IsNullOrWhiteSpace(dbData.Device))
                {
                    deviceToUse = dbData.Device;
                }

                if (!string.IsNullOrWhiteSpace(dbData.FileType))
                {
                    fileTypeToUse = dbData.FileType;
                }
            }
            else
            {
                // اگر نتوانستیم از سرور بگیریم، هشدار می‌دهیم اما کار را با دیتای جدول ادامه می‌دهیم
                Console.WriteLine("Could not fetch fresh data from DB, using grid data.");
            }

            // مقداردهی نهایی به مدل دیالوگ
            dialogModel.Device = deviceToUse;
            dialogModel.FileType = fileTypeToUse;

            // 3. مدیریت لیست‌های دراپ‌داون (بسیار مهم برای نمایش صحیح)
            // اگر مقداری که داریم در لیست آپشن‌ها نباشد، موقتاً اضافه می‌کنیم تا فرم خالی نشان ندهد
            var currentDeviceOptions = new List<string>(deviceOptions);
            if (!string.IsNullOrEmpty(dialogModel.Device) && !currentDeviceOptions.Contains(dialogModel.Device))
            {
                currentDeviceOptions.Insert(0, dialogModel.Device);
            }

            var currentFileTypeOptions = new List<string>(fileTypeOptions);
            if (!string.IsNullOrEmpty(dialogModel.FileType) && !currentFileTypeOptions.Contains(dialogModel.FileType))
            {
                currentFileTypeOptions.Insert(0, dialogModel.FileType);
            }

            // 4. باز کردن دیالوگ
            var parameters = new MudBlazor.DialogParameters
            {
                ["Item"] = dialogModel,
                ["DeviceOptions"] = currentDeviceOptions,
                ["FileTypeOptions"] = currentFileTypeOptions
            };

            var dialog = await DialogService.ShowAsync<EditProjectDialog>("Edit Project", parameters);
            var res = await dialog.Result;

            // 5. ذخیره تغییرات در سرور
            if (res != null && !res.Canceled)
            {
                var updated = res.Data as ProjectUiModel;
                if (updated != null)
                {
                    // اینجا وقتی Save زده شود، دیتای حدس زده شده یا تغییر کرده در دیتابیس ثبت می‌شود
                    // و مشکل نال بودن برای همیشه حل می‌شود
                    var serverRes = await ProjectService.UpdateProjectAsync(
                        updated.Id,
                        updated.ProjectName,
                        updated.Device,
                        updated.FileType,
                        updated.Description
                    );

                    if (serverRes.Succeeded)
                    {
                        Snackbar.Add("Project updated successfully.", Severity.Success);
                        await LoadExistingFilesAsync(); // رفرش لیست
                    }
                    else
                    {
                        Snackbar.Add(serverRes.Message ?? "Failed to update project.", Severity.Error);
                    }
                }
            }
        }

        private async Task RefreshList()
        {
            await LoadExistingFilesAsync();
        }

        // Helpers
        private string GetFileIcon()
        {
            if (string.IsNullOrEmpty(fileName)) return Icons.Material.Filled.InsertDriveFile;
            var ext = Path.GetExtension(fileName).ToLower();
            return ext switch
            {
                ".csv" => Icons.Material.Filled.TableChart,
                ".xlsx" or ".xls" => Icons.Material.Filled.GridOn,
                ".txt" => Icons.Material.Filled.Description,
                _ => Icons.Material.Filled.InsertDriveFile
            };
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1048576) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / 1048576.0:F1} MB";
        }

        private bool CanImport => fileContent != null
                          && !string.IsNullOrWhiteSpace(displayFileName)
                          && !string.IsNullOrWhiteSpace(selectedDevice)
                          && !isLoading
                          && !isViewer
                          && !uploadCompleted;

        private async Task OnBeforeNavigation(LocationChangingContext context)
        {
            if (isLoading) context.PreventNavigation();
        }
    }
}