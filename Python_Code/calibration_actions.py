"""
Action/side-effect handlers extracted from CalibrationPro.
Each function expects `self` to be an instance of CalibrationPro.
"""
import pandas as pd
from PyQt6.QtWidgets import QMessageBox, QProgressDialog
from PyQt6.QtCore import Qt


def reset_all(self):
    self.reset_to_original()
    if hasattr(self, "reset_bs"):
        self.reset_bs()
    self.update_displays()
    QMessageBox.information(self, "Reset", "Reset to original and blank subtraction applied.")


def start_check_rm_thread(self):
    from .find_rm import CheckRMThread
    keyword = self.keyword_entry2.text().strip()
    if not keyword:
        QMessageBox.critical(self, "Error", "Please enter a valid keyword.")
        return
    self.keyword = keyword
    self.progress_dialog = QProgressDialog("Processing RM Changes...", "Cancel", 0, 100, self)
    self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
    self.thread = CheckRMThread(self.app, keyword)
    self.thread.progress.connect(self.progress_dialog.setValue)
    self.thread.finished.connect(self.on_check_rm_finished)
    self.thread.error.connect(self.on_check_rm_error)
    self.thread.start()


def on_check_rm_finished(self, results):
    self.all_initial_rm_df = results['rm_df'].copy(deep=True)
    self.all_rm_df = results['rm_df'].copy(deep=True)
    self.all_positions_df = results['positions_df'].copy(deep=True)
    self.all_segments = results['segments']
    self.all_pivot_df = results['pivot_df'].copy(deep=True)
    self.elements = results['elements']
    self.file_ranges = self.app.file_ranges if hasattr(self.app, 'file_ranges') else []

    self.empty_rows_from_check = results.get('empty_rows', pd.DataFrame())

    if 'original_index' not in self.empty_rows_from_check.columns:
        self.empty_rows_from_check = pd.DataFrame(columns=['original_index'])

    self.empty_pivot_set = set(
        self.empty_rows_from_check['original_index']
        .dropna()
        .astype(int)
        .tolist()
    )

    self.on_file_changed(0)
    self.filter_by_file(-1)
    self.progress_dialog.close()
    self.data_changed.emit()
    self.update_navigation_buttons()


def on_check_rm_error(self, message):
    self.progress_dialog.close()
    QMessageBox.critical(self, "Error", message)


def auto_optimize_to_flat(self):
    if len(self.display_rm_values) == 0:
        return

    self.rm_df, self.all_rm_df = self.rm_handler.auto_optimize_to_flat(
        self.rm_df, self.all_rm_df, self.selected_element, self.current_rm_num,
        self.empty_rows_from_check, self.rm_handler.ignored_pivots, self.positions_df,
        self.global_optimize_cb.isChecked(), self.per_file_cb.isChecked(), self.file_ranges,
        self.current_file_index
    )
    self.filter_by_file(self.current_file_index if self.current_file_index > 0 else -1)
    self.sync_rm_to_all()
    self.update_displays()
    self.update_slope_from_data()
    QMessageBox.information(self, "Info", "Selected RM optimized to flat relative to the first valid point in each segment (or globally if checked).")


def auto_optimize_to_flat_per_file_global_style(self):
    self.rm_df, self.all_rm_df = self.rm_handler._auto_optimize_to_flat_per_file_global_style(
        self.all_rm_df, self.selected_element, self.current_rm_num, self.empty_pivot_set,
        self.rm_handler.ignored_pivots, self.file_ranges, self.rm_df
    )
    self.filter_by_file(self.current_file_index if self.current_file_index > 0 else -1)
    self.update_displays()
    self.update_slope_from_data()
    self.update_rm_plot()
    QMessageBox.information(
        self,
        "Success",
        "Optimized to flat per file → using first valid RM in each file as reference\n"
        "(Global Optimize + Per File mode)"
    )


def auto_optimize_to_flat_per_file(self):
    self.rm_df, self.all_rm_df = self.rm_handler._auto_optimize_to_flat_per_file(
        self.all_rm_df, self.selected_element, self.current_rm_num, self.empty_pivot_set,
        self.rm_handler.ignored_pivots, self.file_ranges, self.rm_df
    )
    self.filter_by_file(self.current_file_index if self.current_file_index > 0 else -1)
    self.update_displays()
    self.update_slope_from_data()
    self.update_rm_plot()
    QMessageBox.information(self, "Success", "Optimized to flat per file\n(ignored and low-intensity RMs skipped)")


def auto_optimize_slope_to_zero(self):
    self.rm_df, self.all_rm_df, self.display_rm_values = self.rm_handler.auto_optimize_slope_to_zero(
        self.rm_df, self.all_rm_df, self.display_rm_values, self.selected_element,
        self.current_rm_num, self.empty_rows_from_check, self.rm_handler.ignored_pivots,
        self.positions_df, self.global_optimize_cb.isChecked(), self.per_file_cb.isChecked(),
        self.file_ranges, self.current_file_index
    )
    self.update_displays()
    self.update_slope_from_data()
    QMessageBox.information(
        self, "Success",
        "Slope successfully set to zero for the selected RM\n"
        "(per segment or globally — starting point preserved)"
    )


def auto_optimize_slope_to_zero_per_file(self):
    self.rm_df, self.all_rm_df, self.display_rm_values = self.rm_handler._auto_optimize_slope_to_zero_per_file(
        self.all_rm_df, self.selected_element, self.current_rm_num,
        self.rm_handler.ignored_pivots, self.file_ranges, self.rm_df, self.display_rm_values
    )
    self.filter_by_file(self.current_file_index if self.current_file_index > 0 else -1)
    self.update_displays()
    self.update_slope_from_data()
    self.update_rm_plot()
    QMessageBox.information(self, "Info", "Optimized to slope zero per file")


def apply_to_single_rm(self):
    current_report_change = self.app.results.report_change.copy() if hasattr(self.app.results, 'report_change') else pd.DataFrame()

    self.rm_handler.undo_stack.append((
        self.app.results.last_filtered_data.copy(),
        self.rm_df.copy(),
        self.rm_handler.corrected_drift.copy(),
        self.rm_handler.manual_corrections.copy(),
        current_report_change,
        self.all_rm_df.copy()
    ))
    self.undo_rm_btn.setEnabled(True)

    try:
        new_df, self.rm_handler.corrected_drift = self.rm_handler.apply_to_single_rm(
            self.selected_element, self.current_rm_num, self.app.results.last_filtered_data, self.rm_df, self.all_rm_df,
            self.display_rm_values, self.original_rm_values, self.current_valid_pivot_indices,
            self.empty_rows_from_check, self.rm_handler.ignored_pivots, self.stepwise_cb.isChecked(),
            self.rm_handler.manual_corrections, self.rm_handler.corrected_drift
        )
        self.app.results.last_filtered_data = new_df
        self.save_corrected_drift()
        self.results_update_requested.emit(new_df)
        self.update_displays()
        QMessageBox.information(self, "Success", "All corrections (Drift + Manual) applied and saved!")
    except ValueError as e:
        QMessageBox.critical(self, "Error", str(e))


def save_corrected_drift(self):
    if not hasattr(self.app.results, 'corrected_drift'):
        self.app.results.corrected_drift = {}
    self.app.results.corrected_drift.update(self.rm_handler.corrected_drift)

    if not hasattr(self.app.results, 'report_change'):
        self.app.results.report_change = pd.DataFrame(columns=['Solution Label', 'Element', 'Ratio'])

    self.app.results.report_change = self.rm_handler.save_corrected_drift(
        self.rm_handler.corrected_drift, self.app.results.report_change
    )


def sync_rm_to_all(self):
    self.all_rm_df = self.rm_handler._sync_rm_to_all(self.rm_df, self.all_rm_df, self.selected_element)


def sync_corrected_drift_to_report_change(self):
    if hasattr(self.app.results, 'report_change'):
        self.rm_handler.corrected_drift = self.rm_handler.sync_corrected_drift_to_report_change(
            self.app.results.report_change
        )


def undo_changes(self):
    self.app.results.last_filtered_data, self.rm_df, self.rm_handler.corrected_drift, \
    self.rm_handler.manual_corrections, self.app.results.report_change, self.all_rm_df, \
    self.rm_handler.undo_stack = self.rm_handler.undo_changes(
        self.rm_handler.undo_stack, self.app.results.last_filtered_data, self.rm_df,
        self.rm_handler.corrected_drift, self.rm_handler.manual_corrections,
        self.app.results.report_change, self.all_rm_df
    )

    self.update_displays()
    self.undo_rm_btn.setEnabled(bool(self.rm_handler.undo_stack))

    if hasattr(self.app.results, 'notify_data_changed'):
        self.app.results.notify_data_changed()

    QMessageBox.information(
        self, "Undo",
        "✅ Last RM changes undone successfully!\n"
        "• Data restored\n"
        "• Coefficients restored\n"
        "• Report changes restored"
    )


def undo_crm_changes(self):
    if hasattr(self.crm_handler, 'undo_stack') and self.crm_handler.undo_stack:
        last_state = self.crm_handler.undo_stack.pop()
        self.app.results.last_filtered_data = last_state[0]
        self.undo_crm_btn.setEnabled(bool(self.crm_handler.undo_stack))
        self.update_displays()
        QMessageBox.information(self, "Undo", "Last CRM changes undone.")
    else:
        QMessageBox.warning(self, "No Undo", "No CRM changes to undo.")


def update_rm_data(self):
    self.rm_df, self.all_rm_df, self.app.results.last_filtered_data = self.rm_handler.update_rm_data(
        self.rm_df, self.all_rm_df, self.display_rm_values, self.current_rm_num, self.selected_element,
        self.app.results.last_filtered_data, self.current_valid_pivot_indices
    )


def update_rm_table_ratios(self):
    model = self.rm_table.model()
    for i in range(model.rowCount()):
        if i < len(self.original_rm_values):
            ratio = self.display_rm_values[i] / self.original_rm_values[i] if self.original_rm_values[i] != 0 else np.nan
            model.item(i, 6).setText(f"{ratio:.2f}" if pd.notna(ratio) else "N/A")

