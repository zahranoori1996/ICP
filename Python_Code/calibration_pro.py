# screens/process/verification/master_verification.py
import logging
import pandas as pd
import numpy as np
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QCheckBox, QLabel, QLineEdit, QPushButton,
    QGroupBox, QSlider, QComboBox, QTableView, QSplitter, QDoubleSpinBox, QFrame,
    QHeaderView, QSizePolicy, QMenu, QScrollArea
)
from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QColor, QStandardItemModel, QStandardItem, QFont
import pyqtgraph as pg
from typing import Any, Dict, List, Optional, Tuple
from datetime import datetime
from .rm_drift_handler import RMDriftDataManager
from .crm_verification_handler import CRMVerificationDataManager
from .calibration_state import RangeSettings, PreviewSettings, make_default_params
from functools import partial
from styles.common import common_styles
from .calibration_plot_logic import (
    highlight_rm as plot_highlight_rm,
    update_rm_plot as plot_update_rm_plot,
    update_detail_table as plot_update_detail_table,
    on_detail_table_clicked as plot_on_detail_table_clicked,
    update_slope_from_data as plot_update_slope_from_data,
)
from .calibration_logic import (
    filter_by_file as logic_filter_by_file,
    display_rm_table as logic_display_rm_table,
    update_displays as logic_update_displays,
    update_labels as logic_update_labels,
    update_tables_and_plot as logic_update_tables_and_plot,
    on_filter_changed as logic_on_filter_changed,
)
from .calibration_handlers import (
    on_element_changed as handler_on_element_changed,
    on_file_changed as handler_on_file_changed,
    apply_solution_filter as handler_apply_solution_filter,
    prev as handler_prev,
    next as handler_next,
    prompt_apply_changes as handler_prompt_apply_changes,
    has_changes as handler_has_changes,
    update_navigation_buttons as handler_update_navigation_buttons,
    show_rm_context_menu as handler_show_rm_context_menu,
    update_rm_list_and_go_first as handler_update_rm_list_and_go_first,
    apply_slope_from_spin as handler_apply_slope_from_spin,
    reset_to_original as handler_reset_to_original,
    update_rm_table_values_only as handler_update_rm_table_values_only,
    on_rm_value_changed as handler_on_rm_value_changed,
)
from .calibration_dialogs import (
    open_range_dialog as dialog_open_range_dialog,
    apply_ranges as dialog_apply_ranges,
    open_exclude_window as dialog_open_exclude_window,
    toggle_exclude_check as dialog_toggle_exclude_check,
    open_select_crms_window as dialog_open_select_crms_window,
    toggle_crm_check as dialog_toggle_crm_check,
    set_all_crms as dialog_set_all_crms,
    update_scale_range as dialog_update_scale_range,
)
from .calibration_actions import (
    reset_all as action_reset_all,
    start_check_rm_thread as action_start_check_rm_thread,
    on_check_rm_finished as action_on_check_rm_finished,
    on_check_rm_error as action_on_check_rm_error,
    auto_optimize_to_flat as action_auto_optimize_to_flat,
    auto_optimize_to_flat_per_file_global_style as action_auto_optimize_to_flat_per_file_global_style,
    auto_optimize_to_flat_per_file as action_auto_optimize_to_flat_per_file,
    auto_optimize_slope_to_zero as action_auto_optimize_slope_to_zero,
    auto_optimize_slope_to_zero_per_file as action_auto_optimize_slope_to_zero_per_file,
    apply_to_single_rm as action_apply_to_single_rm,
    save_corrected_drift as action_save_corrected_drift,
    sync_rm_to_all as action_sync_rm_to_all,
    sync_corrected_drift_to_report_change as action_sync_corrected_drift_to_report_change,
    undo_changes as action_undo_changes,
    undo_crm_changes as action_undo_crm_changes,
    update_rm_data as action_update_rm_data,
    update_rm_table_ratios as action_update_rm_table_ratios,
)
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


class CalibrationPro(QWidget):
    data_changed = pyqtSignal()
    results_update_requested = pyqtSignal(pd.DataFrame)

    def __init__(self, parent, annotations=None):
        super().__init__(parent)
        self.setStyleSheet(common_styles)
        self.app = parent
        # Handlers
        # Initial variables
        self.analysis_data = None
        self.selected_element = None
        self.all_pivot_df = None
        self.all_rm_df = None
        self.all_initial_rm_df = None
        self.all_positions_df = None
        self.pivot_df = None
        self.rm_df = None
        self.initial_rm_df = None
        self.positions_df = None
        self.rm_numbers_list = []
        self.current_rm_index = -1
        self.element_list = []
        self.current_element_index = -1
        self.current_file_index = -1
        self.current_nav_index = -1
        self.navigation_list = []
        self.logger = logging.getLogger(__name__)
        self.file_ranges = getattr(self.app, 'file_ranges', [])
        self.manual_corrections = {}
        self.empty_rows_from_check = pd.DataFrame()
        self.empty_pivot_set = set()
        self.ignored_pivots = set()
        self.selected_row = 0
        self.selected_point_pivot = None
        self.corrected_drift = {}
        self.current_rm_num = None
        self.undo_stack = []
        self.elements = []
        self.current_element_index = -1
        if getattr(self.app.results, 'last_filtered_data', None) is not None:
            df = self.app.results.last_filtered_data
            self.elements = [col for col in df.columns if col != 'Solution Label']
            if self.elements:
                self.current_element_index = 0
                self.selected_element = self.elements[self.current_element_index]
        empty_outliers = {el: set() for el in self.elements} if self.elements else {}
        self.original_df = None

        # Structured config/state
        self.range_settings = RangeSettings()
        self.preview_settings = PreviewSettings(
            excluded_outliers=empty_outliers.copy(),
            excluded_from_correct=set()
        )
        # Maintain existing attribute names for backward compatibility
        self.range_low = self.range_settings.range_low
        self.range_mid = self.range_settings.range_mid
        self.range_high1 = self.range_settings.range_high1
        self.range_high2 = self.range_settings.range_high2
        self.range_high3 = self.range_settings.range_high3
        self.range_high4 = self.range_settings.range_high4
        self.preview_blank = self.preview_settings.preview_blank
        self.preview_scale = self.preview_settings.preview_scale
        self.excluded_outliers = self.preview_settings.excluded_outliers
        self.excluded_from_correct = self.preview_settings.excluded_from_correct
        self.scale_range_min = self.preview_settings.scale_range_min
        self.scale_range_max = self.preview_settings.scale_range_max
        self.calibration_range = self.preview_settings.calibration_range
        self.blank_labels = self.preview_settings.blank_labels

        self.params = make_default_params(self.file_ranges, self.elements)

        self.rm_handler = RMDriftDataManager()
        self.crm_handler = CRMVerificationDataManager()
        self.setup_ui()
    
        self.setup_plot_items()
        self.connect_signals()
        
    def setup_ui(self):
        # Delegated to keep this file short
        from .calibration_ui import setup_calibration_ui
        setup_calibration_ui(self)


    def setup_plot_items(self):
        from .calibration_ui import setup_plot_items
        setup_plot_items(self)

    def highlight_rm(self):
        return plot_highlight_rm(self)

    def reset_blank_and_scale(self):
        """Reset blank and scale to default values."""
        self.preview_blank = 0.0
        self.blank_edit.setText("0.0")
        self.preview_scale = 1.0
        self.scale_slider.setValue(100)
        self.scale_label.setText(f"Scale: {self.preview_scale:.2f}")
        self.update_pivot_plot()

    def update_rm_plot(self):
        return plot_update_rm_plot(self)

    def update_slope_from_data(self):
        return plot_update_slope_from_data(self)

    def on_table_row_clicked(self, index):
        self.selected_start_rm_points.setData([], [])
        self.selected_end_rm_points.setData([], [])
        self.selected_segment_line.setData([], [])
        new_row = index.row()
        self.selected_row = new_row
        if 0 <= self.selected_row < len(self.current_valid_pivot_indices):
            pivot = self.current_valid_pivot_indices[self.selected_row]
            y = self.display_rm_values[self.selected_row]
            self.selected_point_pivot = pivot
            self.selected_point_y = y
            self.highlight_point.setData([pivot], [y])
        self.highlight_rm()
        self.update_detail_table()

    def update_detail_table(self):
        return plot_update_detail_table(self)

    def has_changes(self):
        return handler_has_changes(self)

    def prompt_apply_changes(self):
        return handler_prompt_apply_changes(self)

    def update_navigation_buttons(self):
        return handler_update_navigation_buttons(self)

    def apply_solution_filter(self):
        return handler_apply_solution_filter(self)
    
    def show_rm_context_menu(self, pos):
        index = self.rm_table.indexAt(pos)
        if not index.isValid():
            return
        row = index.row()
        if row < 0 or row >= len(self.current_valid_pivot_indices):
            return
        pivot = self.current_valid_pivot_indices[row]
        if pivot in self.empty_pivot_set:
            return
        menu = QMenu(self)
        if pivot in self.ignored_pivots:
            action = menu.addAction("Unignore this point")
        else:
            action = menu.addAction("Ignore this point")
        if menu.exec(self.rm_table.mapToGlobal(pos)) == action:
            if pivot in self.ignored_pivots:
                self.ignored_pivots.remove(pivot)
            else:
                self.ignored_pivots.add(pivot)
            self.update_displays()

    def on_filter_changed(self):
        return logic_on_filter_changed(self)

    def prev(self):
        return handler_prev(self)

    def next(self):
        return handler_next(self)

    def update_rm_list_and_go_first(self):
        return handler_update_rm_list_and_go_first(self)

    def update_tables_and_plot(self):
        return logic_update_tables_and_plot(self)

    def on_element_changed(self, element):
        return handler_on_element_changed(self, element)

    def on_file_changed(self, combo_index: int):
        return handler_on_file_changed(self, combo_index)

    def filter_by_file(self, index):
        return logic_filter_by_file(self, index)

    def display_rm_table(self):
        return logic_display_rm_table(self)

    def update_displays(self):
        return logic_update_displays(self)

    def update_labels(self):
        return logic_update_labels(self)
    def _create_segments(self, positions_df: pd.DataFrame) -> List[Dict[str, Any]]:
        segments = []
        for seg_id in positions_df['segment_id'].unique():
            seg_df = positions_df[positions_df['segment_id'] == seg_id].copy()
            if seg_df.empty:
                continue
            ref_num = seg_df['ref_rm_num'].iloc[0]
            segments.append({
                'segment_id': seg_id,
                'ref_rm_num': ref_num,
                'positions': seg_df
            })
        return segments 

    def update_preview_params(self):
        try:
            self.preview_blank = float(self.blank_edit.text())
        except ValueError:
            self.preview_blank = 0.0
        self.preview_scale = self.scale_slider.value() / 100.0
        self.scale_label.setText(f"Scale: {self.preview_scale:.2f}")
        # آپدیت هر دو نمودار با پیش‌نمایش blank/scale
        self.update_rm_plot()
        self.update_pivot_plot()
    def connect_signals(self):
        self.element_combo.currentTextChanged.connect(self.on_element_changed)
        self.prev_rm_btn.clicked.connect(self.prev)
        self.next_rm_btn.clicked.connect(self.next)
        self.blank_edit.textChanged.connect(self.update_preview_params)
        self.scale_slider.valueChanged.connect(self.update_preview_params)
        self.auto_flat_btn.clicked.connect(self.auto_optimize_to_flat)
        self.auto_zero_slope_btn.clicked.connect(self.auto_optimize_slope_to_zero)
        self.run_rm_btn.clicked.connect(self.start_check_rm_thread)
        self.rm_table.clicked.connect(self.on_table_row_clicked)
        self.rm_table.customContextMenuRequested.connect(self.show_rm_context_menu)
        self.detail_table.clicked.connect(self.on_detail_table_clicked)
        self.plot_calibration_btn.clicked.connect(self.run_calibration)
        self.show_cert_cb.toggled.connect(self.update_pivot_plot)
        self.show_crm_cb.toggled.connect(self.update_pivot_plot)
        self.show_range_cb.toggled.connect(self.update_pivot_plot)
        self.filter_solution_edit.textChanged.connect(self.on_filter_changed)
        self.apply_slope_btn.clicked.connect(self.apply_slope_from_spin)
        self.undo_rm_btn.clicked.connect(self.undo_changes)
        self.file_selector.currentIndexChanged.connect(self.on_file_changed)
        


    def run_calibration(self):
        file_ranges = getattr(self.app, 'file_ranges', [])
        if not file_ranges:
            from PyQt6.QtWidgets import QMessageBox
            QMessageBox.warning(self, "No Files", "No file ranges detected. Please load data first.")
            return

        self.file_selector.clear()
        self.file_selector.setEnabled(True)
        self.file_selector.setToolTip("Switch between files")
        self.file_selector.addItem("All Files")

        for i, fr in enumerate(file_ranges):
            clean_name = fr.get('clean_name', f'File {i+1}')
            start_row = fr.get('start_pivot_row', 0) + 1
            end_row = fr.get('end_pivot_row', 0)
            row_count = fr.get('pivot_row_count', end_row - start_row + 2)
            display_text = f"{clean_name}  |  Rows {start_row}–{end_row}  ({row_count} rows)"
            self.file_selector.addItem(display_text)

        try:
            self.file_selector.currentIndexChanged.disconnect()
        except Exception:
            pass
        self.start_check_rm_thread()
        self.file_selector.setCurrentIndex(0)

    def on_detail_table_clicked(self, index):
        return plot_on_detail_table_clicked(self, index)
        
    def update_current_rm_after_file_change(self):
        """
        بعد از تغییر فایل، اولین RM موجود در داده‌های جدید رو انتخاب کنه
        """
        if not hasattr(self, 'rm_df') or self.rm_df.empty:
            return

        available_rm_nums = sorted(self.rm_df['rm_num'].dropna().unique().astype(int).tolist())
        
        if not available_rm_nums:
            self.current_rm_num = None
            self.current_rm_label.setText("None")
            return

        # اگر RM فعلی هنوز در این فایل هست، همون رو نگه دار
        if self.current_rm_num in available_rm_nums:
            new_rm_num = self.current_rm_num
        else:
            # در غیر این صورت اولین RM موجود در فایل جدید
            new_rm_num = available_rm_nums[0]

        self.current_rm_num = new_rm_num
        self.current_rm_label.setText(f"Current RM: {new_rm_num}")
        
        # ریست انتخاب ردیف در جدول
        self.selected_row = -1

    def reset_all(self):
        return action_reset_all(self)

    def apply_slope_from_spin(self):
        return handler_apply_slope_from_spin(self)


    def handle_point_click(self, table_type, scatter, points, ev):
        if not points:
            return
        pt = points[0]
        label = pt.data()
        x = pt.pos().x()
        y = pt.pos().y()
        self.selected_point_pivot = x
        self.selected_point_y = y
        self.highlight_point.setData([x], [y])
        if table_type == 'rm':
            model = self.rm_table.model()
            for row in range(model.rowCount()):
                item_label = model.item(row, 0).text()
                if item_label.startswith(label):
                    self.rm_table.selectRow(row)
                    self.selected_row = row
                    self.update_detail_table()
                    break

    def reset_to_original(self):
        return handler_reset_to_original(self)



    def open_range_dialog(self):
        return dialog_open_range_dialog(self)

    def apply_ranges(self, dialog):
        return dialog_apply_ranges(self, dialog)

    def open_exclude_window(self):
        return dialog_open_exclude_window(self)

    def toggle_exclude_check(self, index, model):
        return dialog_toggle_exclude_check(self, index, model)

    def open_select_crms_window(self):
        return dialog_open_select_crms_window(self)

    def update_scale_range(self):
        return dialog_update_scale_range(self)

    def toggle_crm_check(self, index, model):
        return dialog_toggle_crm_check(self, index, model)

    def set_all_crms(self, value, model):
        return dialog_set_all_crms(self, value, model)

    def calculate_dynamic_range(self, value):
        """Calculate the dynamic range for a given value."""
        try:
            value = float(value)
            abs_value = abs(value)
            if abs_value < 10:
                return self.w.range_low
            elif 10 <= abs_value < 100:
                return abs_value * (self.w.range_mid / 100)
            elif 100 <= abs_value < 1000:
                return abs_value * (self.w.range_high1 / 100)
            elif 1000 <= abs_value < 10000:
                return abs_value * (self.w.range_high2 / 100)
            elif 10000 <= abs_value < 100000:
                return abs_value * (self.w.range_high3 / 100)
            else:
                return abs_value * (self.w.range_high4 / 100)
        except (ValueError, TypeError):
            return 0
    

    def start_check_rm_thread(self):
        return action_start_check_rm_thread(self)

    def on_check_rm_finished(self, results):
        return action_on_check_rm_finished(self, results)

    def on_check_rm_error(self, message):
        return action_on_check_rm_error(self, message)

    def get_valid_rm_data(self):
        return self.rm_handler.get_valid_rm_data(
            self.rm_df, self.initial_rm_df, self.selected_element, self.current_rm_num,
            self.empty_rows_from_check, self.rm_handler.ignored_pivots, self.positions_df
        )

    def get_file_name_for_pivot(self, pivot_idx):
        return self.rm_handler._get_file_names_for_pivots([pivot_idx], self.file_ranges)[0]

    def update_rm_table_values_only(self):
        return handler_update_rm_table_values_only(self)

    def auto_optimize_to_flat(self):
        return action_auto_optimize_to_flat(self)

    def auto_optimize_to_flat_per_file_global_style(self):
        return action_auto_optimize_to_flat_per_file_global_style(self)

    def auto_optimize_to_flat_per_file(self):
        return action_auto_optimize_to_flat_per_file(self)

    def auto_optimize_slope_to_zero(self):
        return action_auto_optimize_slope_to_zero(self)

    def auto_optimize_slope_to_zero_per_file(self):
        return action_auto_optimize_slope_to_zero_per_file(self)

    def on_detail_value_changed(self, item):
        if item.column() != 2:
            return
        try:
            new_val = float(item.text())
            orig_index = item.data(Qt.ItemDataRole.UserRole)
            if orig_index is None:
                return
            self.rm_handler.manual_corrections[orig_index] = new_val
            self.update_rm_plot()
            self.update_slope_from_data()
        except ValueError:
            QMessageBox.warning(self, "Invalid Value", "Please enter a valid number.")

    def get_data_between_rm(self):
        return self.rm_handler.get_data_between_rm(
            self.selected_row, self.current_valid_pivot_indices, self.pivot_df,
            self.selected_element, self.filter_solution_edit.text()
        )

    def update_rm_data(self):
        return action_update_rm_data(self)

    def update_rm_table_ratios(self):
        return action_update_rm_table_ratios(self)

    def on_rm_value_changed(self, item):
        return handler_on_rm_value_changed(self, item)

    def apply_to_single_rm(self):
        return action_apply_to_single_rm(self)

    def save_corrected_drift(self):
        return action_save_corrected_drift(self)

    def sync_rm_to_all(self):
        return action_sync_rm_to_all(self)

    def sync_corrected_drift_to_report_change(self):
        return action_sync_corrected_drift_to_report_change(self)

    def undo_changes(self):
        return action_undo_changes(self)

    def undo_crm_changes(self):
        return action_undo_crm_changes(self)


    def update_pivot_plot(self):
        """Update the plot based on current settings."""
        if not self.selected_element or self.selected_element not in self.pivot_df.columns:
            self.logger.warning(f"Element '{self.selected_element}' not found in pivot data!")
            QMessageBox.warning(self, "Warning", f"Element '{self.selected_element}' not found!")
            return
        try:
            self.verification_plot.clear()
            self.annotations = []

            soln_conc_min, soln_conc_max, soln_conc_range, in_calibration_range_soln = self.crm_handler.get_solution_concentration_range(
                self.original_df, self.selected_element
            )

            blank_val, blank_correction_status, selected_blank_label, self.blank_labels = self.crm_handler.get_best_blank(
                self.pivot_df, self.selected_element, self.app.crm_check, self.excluded_outliers, 
                self.calibration_range, in_calibration_range_soln, ' '.join(self.selected_element.split()[1:]), 
                datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            )

            self.blank_display.setText("Blanks:\n" + "\n".join(self.blank_labels) if self.blank_labels else "Blanks: None")

            blank_labels_set = set([label.split(':')[0] for label in self.blank_labels])  # Adjust as needed
            crm_labels = self.crm_handler.get_crm_labels(self.app.crm_check, blank_labels_set)

            unique_crm_ids, crm_id_to_labels = self.crm_handler.group_crm_labels(crm_labels, self.crm_handler.extract_crm_id)

            certificate_values, sample_values, outlier_values, lower_bounds, upper_bounds, soln_concs, int_values = self.crm_handler.collect_crm_data(
                unique_crm_ids, crm_id_to_labels, self.pivot_df, self.selected_element, self.original_df, 
                self.excluded_outliers, self.excluded_from_correct, self.scale_range_min, self.scale_range_max, 
                self.scale_above_50_cb.isChecked(), self.preview_blank, self.preview_scale, self.app.crm_check
            )

            if not unique_crm_ids:
                self.verification_plot.clear()
                self.logger.warning(f"No valid Verification data for {self.selected_element}")
                QMessageBox.warning(self, "Warning", f"No valid Verification data for {self.selected_element}")
                return

            self.annotations = self.crm_handler.build_annotations(
                unique_crm_ids, crm_id_to_labels, certificate_values, sample_values, outlier_values, 
                lower_bounds, upper_bounds, soln_concs, int_values, self.pivot_df, self.selected_element, 
                self.excluded_outliers, blank_val, selected_blank_label, blank_correction_status, 
                in_calibration_range_soln, self.calibration_range, ' '.join(self.selected_element.split()[1:]), 
                datetime.now().strftime("%Y-%m-%d %H:%M:%S"), self.app.crm_check
            )

            y_min, y_max = self.crm_handler.get_plot_data_bounds(
                certificate_values, sample_values, outlier_values, lower_bounds, upper_bounds, unique_crm_ids
            )

            self.verification_plot.setLabel('bottom', 'Verification ID')
            self.verification_plot.setLabel('left', f'{self.selected_element} Value')
            self.verification_plot.setTitle(f'Verification Values for {self.selected_element}')
            self.verification_plot.getAxis('bottom').setTicks([[(i, f'V {id}') for i, id in enumerate(unique_crm_ids)]])

            if y_min is not None and y_max is not None:
                self.verification_plot.setXRange(-0.5, len(unique_crm_ids) - 0.5)
                self.verification_plot.setYRange(y_min, y_max)

            x_pos_map = {crm_id: i for i, crm_id in enumerate(unique_crm_ids)}

            if self.show_crm_cb.isChecked():
                for crm_id in unique_crm_ids:
                    x_pos = x_pos_map[crm_id]
                    cert_vals = certificate_values.get(crm_id, [])
                    if cert_vals:
                        x_vals = [x_pos] * len(cert_vals)
                        scatter = pg.PlotDataItem(
                            x=x_vals, y=cert_vals, pen=None, symbol='o', symbolSize=8,
                            symbolPen='g', symbolBrush='g', name='Certificate Value'
                        )
                        self.verification_plot.addItem(scatter)

            if self.show_cert_cb.isChecked():
                for crm_id in unique_crm_ids:
                    x_pos = x_pos_map[crm_id]
                    for idx, row_key in enumerate(crm_id_to_labels[crm_id]):
                        sol_label = row_key[0] if isinstance(row_key, tuple) else row_key

                        samp_vals = sample_values.get(crm_id, [])
                        if idx < len(samp_vals):
                            scatter = pg.PlotDataItem(
                                x=[x_pos], y=[samp_vals[idx]], pen=None, symbol='t', symbolSize=8,
                                symbolPen='b', symbolBrush='b', name=sol_label
                            )
                            self.verification_plot.addItem(scatter)

                        outlier_vals = outlier_values.get(crm_id, [])
                        if idx < len(outlier_vals):
                            scatter = pg.PlotDataItem(
                                x=[x_pos], y=[outlier_vals[idx]], pen=None, symbol='t', symbolSize=8,
                                symbolPen='#FFA500', symbolBrush='#FFA500', name=f"{sol_label} (Outlier)"
                            )
                            self.verification_plot.addItem(scatter)

            if self.show_range_cb.isChecked():
                for crm_id in unique_crm_ids:
                    x_pos = x_pos_map[crm_id]
                    low_bounds = lower_bounds.get(crm_id, [])
                    up_bounds = upper_bounds.get(crm_id, [])
                    if low_bounds and up_bounds:
                        for low, up in zip(low_bounds, up_bounds):
                            line_lower = pg.PlotDataItem(
                                x=[x_pos - 0.2, x_pos + 0.2], y=[low, low],
                                pen=pg.mkPen('r', width=2)
                            )
                            line_upper = pg.PlotDataItem(
                                x=[x_pos - 0.2, x_pos + 0.2], y=[up, up],
                                pen=pg.mkPen('r', width=2)
                            )
                            self.verification_plot.addItem(line_lower)
                            self.verification_plot.addItem(line_upper)

            self.verification_plot.showGrid(x=True, y=True, alpha=0.3)

            filter_text = self.filter_solution_edit.text().strip().lower()
            x_sec, y_sec = self.crm_handler.get_filtered_data(self.pivot_df, self.selected_element, filter_text)

        except Exception as e:
            self.verification_plot.clear()
            self.logger.error(f"Failed to update plot: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to update plot: {str(e)}")

    def update_calibration_range(self):
        if not hasattr(self, 'calibration_display'):
            return
        self.calibration_range = self.crm_handler.update_calibration_range(self.original_df, self.selected_element)
        self.calib_range_label.setText(f"Calibration: {self.calibration_range}")

    def run_pivot_plot(self):
        if self.element_combo:
            self.update_calibration_range()
        self.update_navigation_buttons()
        self.update_pivot_plot()

    def undo_crm_correction(self):
        try:
            self.pivot_df, self.all_pivot_df, self.app.results.report_change = self.crm_handler.undo_crm_correction(
                self.selected_element, self.app.results, self.pivot_df, self.all_pivot_df
            )
            column = self.selected_element
            self.app.crm_check.restore_column(column)

            if hasattr(self.app.results, 'show_processed_data'):
                self.app.results.last_filtered_data = self.all_pivot_df.copy() if hasattr(self, 'all_pivot_df') else self.pivot_df.copy()
                self.app.results.show_processed_data()

            removed_count = 0  # Adjust to count
            QMessageBox.information(
                self, "✅ CRM Undo Successful",
                f"✅ Removed {removed_count} CRM coefficients\n"
                f"🔄 Element: {self.selected_element}\n"
                f"📊 Values restored to original!\n\n"
                f"💾 Results tab updated!"
            )

            self.update_pivot_plot()
            logger.info(f"✅ CRM undo: removed {removed_count} coefficients for {self.selected_element}")

        except ValueError as e:
            QMessageBox.warning(self, "⚠️ No Data", str(e))
        except Exception as e:
            logger.error(f"❌ CRM Undo failed: {str(e)}")
            QMessageBox.critical(self, "❌ Undo Error", f"Failed to undo:\n{str(e)}")

    def correct_crm_callback(self):
        try:
            self.pivot_df, corrected_count, correction_data = self.crm_handler.correct_crm(
                self.pivot_df, self.selected_element, self.excluded_from_correct, self.scale_range_min, 
                self.scale_range_max, self.scale_above_50_cb.isChecked(), self.preview_blank, self.preview_scale
            )

            self.all_pivot_df, self.pivot_df, self.original_df, self.all_original_df = self.crm_handler.update_global_data_crm(
                self.selected_element, correction_data, self.all_pivot_df, self.pivot_df, self.app.results, 
                self.original_df, self.all_original_df
            )

            self.app.results.report_change = self.crm_handler.save_crm_to_report_change(
                correction_data, self.selected_element, self.app.results
            )

            if hasattr(self.app.results, 'show_processed_data'):
                self.app.results.show_processed_data()

            if hasattr(self.app, 'notify_data_changed'):
                self.app.notify_data_changed()

            range_text = (f"[{self.crm_handler.format_number(self.scale_range_min)} to {self.crm_handler.format_number(self.scale_range_max)}]"
                          if self.scale_range_min is not None and self.scale_range_max is not None else "All values")
            QMessageBox.information(
                self, "✅ Success",
                f"✅ CRM Correction applied!\n\n"
                f"📊 Corrected: {corrected_count} samples\n"
                f"🧪 Blank: {self.crm_handler.format_number(self.preview_blank)}\n"
                f"⚖️  Scale: {self.preview_scale:.4f}\n"
                f"📏 Range: {range_text}\n\n"
                f"💾 Values UPDATED in Results tab!\n"
                f"📋 Coefficients saved to Report Changes!"
            )

            self.update_pivot_plot()

        except ValueError as e:
            QMessageBox.warning(self, "Error", str(e))
        except Exception as e:
            logger.error(f"❌ CRM Correction failed: {str(e)}")
            QMessageBox.critical(self, "❌ Error", f"Failed to apply CRM correction:\n{str(e)}")

    def apply_model(self):
        try:
            from ..report_dialog import ReportDialog
            dialog = ReportDialog(self, self.annotations)
            recommended_blank, recommended_scale = dialog.get_correction_parameters()
            self.blank_edit.setText(f"{recommended_blank:.3f}")
            self.scale_slider.setValue(int(recommended_scale * 100))
            self.update_preview_params()
        except Exception as e:
            logger.error(f"Error applying model: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to apply model: {str(e)}")

    def show_report(self):
        try:
            from ..report_dialog import ReportDialog
            logger.debug(f"Opening report with {len(self.annotations)} annotations")
            dialog = ReportDialog(self, self.annotations)
            result = dialog.exec()
            if result == QDialog.DialogCode.Accepted:
                logger.debug("Report dialog accepted")
            else:
                logger.debug("Report dialog closed without applying corrections")
        except Exception as e:
            logger.error(f"Error opening ReportDialog: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to open report: {str(e)}")