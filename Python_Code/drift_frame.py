from PyQt6.QtWidgets import (
    QWidget, QFrame, QVBoxLayout, QHBoxLayout, QPushButton, QLineEdit, QLabel,
    QTableView, QHeaderView, QMessageBox, QGroupBox, QDoubleSpinBox, QProgressDialog, QCheckBox,
    QComboBox, QApplication,QMenu
)
from PyQt6.QtCore import Qt, QThread, pyqtSignal
from PyQt6.QtGui import QFont, QStandardItemModel, QStandardItem, QColor
import pyqtgraph as pg
import pandas as pd
import numpy as np
import logging
import re
from typing import Any, Dict, List, Optional, Tuple
from functools import partial
from .find_rm import CheckRMThread
from .rm_ratio import ApplySingleRM
from styles.common import common_styles
# Setup logging
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

class CheckRMFrame(QWidget):
    data_changed = pyqtSignal()
    results_update_requested = pyqtSignal(pd.DataFrame)

    def __init__(self, app, parent=None):
        super().__init__(parent)
        self.app = app
        self.app.check_rm_frame = self  # برای دسترسی از ApplySingleRM
        self.empty_rows_from_check = pd.DataFrame()
        self.initial_rm_df = None
        self.undo_stack = []
        self.navigation_list = []
        self.current_nav_index = -1
        self.segments = []
        self.file_ranges = self.app.file_ranges if hasattr(self.app, 'file_ranges') else []
        self.current_file_index = -1
        self.all_rm_df = None
        self.all_initial_rm_df = None
        self.all_positions_df = None
        self.all_segments = None
        self.all_pivot_df = None

        # جدید: نقاطی که کاربر دستی ignore کرده
        self.ignored_pivots = set()

        self.selected_point_pivot = None
        self.selected_point_y = None
        self.reset_state()
        self.setup_ui()

        if hasattr(self.app, 'empty_check_frame'):
            self.app.empty_check_frame.empty_rows_found.connect(self.on_empty_rows_received)

    def reset_state(self):
        self.rm_df = self.positions_df = self.pivot_df = self.initial_rm_df = None
        self.all_rm_df = self.all_initial_rm_df = self.all_positions_df = self.all_pivot_df = None
        self.all_segments = None
        self.selected_element = self.current_rm_num = None
        self.elements = self.unique_rm_nums = []
        self.current_slope = 0.0
        self.original_rm_values = self.display_rm_values = np.array([])
        self.current_valid_pivot_indices = []
        self.selected_row = -1
        self.undo_stack = []
        self.corrected_drift = {}
        self.navigation_list = []
        self.current_nav_index = -1
        self.segments = []
        self.current_file_index = -1
        self.selected_point_pivot = None
        self.selected_point_y = None
        self.ignored_pivots = set()  # ریست ignoreهای دستی

        if hasattr(self, 'keyword_entry'): self.keyword_entry.setText("RM")
        if hasattr(self, 'element_combo'): self.element_combo.clear()
        if hasattr(self, 'label_label'): self.label_label.setText("Current RM: None")
        if hasattr(self, 'slope_label'): self.slope_label.setText("Current Slope: 0.000")
        if hasattr(self, 'slope_spinbox'): self.slope_spinbox.blockSignals(True); self.slope_spinbox.setValue(0.0); self.slope_spinbox.blockSignals(False)
        if hasattr(self, 'rm_table'): self.rm_table.setModel(QStandardItemModel())
        if hasattr(self, 'detail_table'): self.detail_table.setModel(QStandardItemModel())
        if hasattr(self, 'plot_widget'): self.plot_widget.clear()
        if hasattr(self, 'auto_optimize_flat_button'): self.auto_optimize_flat_button.setEnabled(False)
        if hasattr(self, 'auto_optimize_zero_button'): self.auto_optimize_zero_button.setEnabled(False)
        if hasattr(self, 'undo_button'): self.undo_button.setEnabled(False)
        if hasattr(self, 'stepwise_checkbox'): self.stepwise_checkbox.setChecked(False)
        if hasattr(self, 'file_selector'): self.file_selector.clear()

    def setup_ui(self):
        self.setStyleSheet(common_styles)
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(20, 20, 20, 20)
        content_frame = QFrame()
        content_layout = QHBoxLayout(content_frame)
        content_layout.setSpacing(15)

        # --- Left Panel ---
        left_frame = QGroupBox("Controls")
        left_layout = QVBoxLayout(left_frame)
        self.left_layout = left_layout
        left_layout.setSpacing(15)

        # Keyword & Run
        control_frame = QFrame()
        control_layout = QHBoxLayout(control_frame)
        control_layout.addWidget(QLabel("Keyword:"))
        self.keyword_entry = QLineEdit("RM")
        self.keyword_entry.setFixedWidth(100)
        control_layout.addWidget(self.keyword_entry)
        self.run_button = QPushButton("Check RM Changes")
        self.run_button.clicked.connect(self.start_check_rm_thread)
        control_layout.addWidget(self.run_button)
        self.undo_button = QPushButton("Undo Last Correction")
        self.undo_button.clicked.connect(self.undo_correction)
        self.undo_button.setEnabled(False)
        control_layout.addWidget(self.undo_button)
        self.stepwise_checkbox = QCheckBox("Apply Stepwise Changes")
        control_layout.addWidget(self.stepwise_checkbox)
        left_layout.addWidget(control_frame)

        # File selector
        self.file_selector_layout = QHBoxLayout()
        self.file_selector_label = QLabel("Select File:")
        self.file_selector = QComboBox()
        self.file_selector.currentIndexChanged.connect(self.on_file_selected)
        self.file_selector_layout.addWidget(self.file_selector_label)
        self.file_selector_layout.addWidget(self.file_selector)
        if len(self.file_ranges) > 1:
            self.file_selector.addItem("All")
            self.file_selector.addItems([fr['clean_name'] for fr in self.file_ranges])
            left_layout.addLayout(self.file_selector_layout)

        # Filter
        self.filter_layout = QHBoxLayout()
        self.filter_label = QLabel("Filter Solution Labels:")
        self.filter_entry = QLineEdit()
        self.filter_entry.setPlaceholderText("Enter text to filter (e.g., 1256)")
        self.filter_entry.textChanged.connect(self.update_displays)
        self.filter_layout.addWidget(self.filter_label)
        self.filter_layout.addWidget(self.filter_entry)
        left_layout.addLayout(self.filter_layout)

        # Optimize buttons
        optimize_frame = QFrame()
        optimize_layout = QHBoxLayout(optimize_frame)
        self.auto_optimize_flat_button = QPushButton("Auto Optimize to Flat")
        self.auto_optimize_flat_button.clicked.connect(self.auto_optimize_to_flat)
        self.auto_optimize_flat_button.setEnabled(False)
        optimize_layout.addWidget(self.auto_optimize_flat_button)
        self.auto_optimize_zero_button = QPushButton("Auto Optimize Slope to Zero")
        self.auto_optimize_zero_button.clicked.connect(self.auto_optimize_slope_to_zero)
        self.auto_optimize_zero_button.setEnabled(False)
        optimize_layout.addWidget(self.auto_optimize_zero_button)
        self.global_optimize_checkbox = QCheckBox("Global Optimize (Ignore Checks)")
        optimize_layout.addWidget(self.global_optimize_checkbox)
        self.per_file_checkbox = QCheckBox("Per File RM Reference")
        self.per_file_checkbox.setEnabled(False)
        optimize_layout.addWidget(self.per_file_checkbox)
        left_layout.addWidget(optimize_frame)

        # Element + RM label
        element_label_layout = QHBoxLayout()
        self.element_combo = QComboBox()
        self.element_combo.currentTextChanged.connect(self.on_element_changed)
        element_label_layout.addWidget(self.element_combo)
        self.label_label = QLabel("Current RM: None")
        self.label_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        element_label_layout.addWidget(self.label_label)
        left_layout.addLayout(element_label_layout)

        # Navigation
        nav_layout = QHBoxLayout()
        self.prev_btn = QPushButton("Previous"); self.prev_btn.clicked.connect(self.prev); nav_layout.addWidget(self.prev_btn)
        self.next_btn = QPushButton("Next"); self.next_btn.clicked.connect(self.next); nav_layout.addWidget(self.next_btn)
        left_layout.addLayout(nav_layout)

        # RM Table (with right-click menu)
        left_layout.addWidget(QLabel("RM Points and Ratios"))
        self.rm_table = QTableView()
        self.rm_table.setSelectionMode(QTableView.SelectionMode.SingleSelection)
        self.rm_table.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
        self.rm_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.rm_table.verticalHeader().setVisible(False)
        self.rm_table.clicked.connect(self.on_table_row_clicked)
        self.rm_table.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.rm_table.customContextMenuRequested.connect(self.show_rm_context_menu)  # راست‌کلیک
        left_layout.addWidget(self.rm_table)

        # Detail Table
        left_layout.addWidget(QLabel("Data Between Selected RM Points"))
        self.detail_table = QTableView()
        self.detail_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.detail_table.verticalHeader().setVisible(False)
        self.detail_table.clicked.connect(self.on_detail_table_clicked)
        left_layout.addWidget(self.detail_table)

        # Slope controls
        slope_controls = QGroupBox("Slope Optimization")
        slope_layout = QVBoxLayout(slope_controls)
        self.slope_label = QLabel("Current Slope: 0.000")
        slope_layout.addWidget(self.slope_label)
        slope_controls_layout = QHBoxLayout()
        slope_controls_layout.addWidget(QLabel("Adjust Slope:"))
        self.slope_spinbox = QDoubleSpinBox()
        self.slope_spinbox.setRange(-1000, 1000)
        self.slope_spinbox.setSingleStep(0.1)
        self.slope_spinbox.valueChanged.connect(self.update_slope)
        slope_controls_layout.addWidget(self.slope_spinbox)
        self.up_button = QPushButton("Rotate Up"); self.up_button.clicked.connect(self.rotate_up); slope_controls_layout.addWidget(self.up_button)
        self.down_button = QPushButton("Rotate Down"); self.down_button.clicked.connect(self.rotate_down); slope_controls_layout.addWidget(self.down_button)
        self.reset_button = QPushButton("Reset to Original"); self.reset_button.clicked.connect(self.reset_to_original); slope_controls_layout.addWidget(self.reset_button)
        slope_layout.addLayout(slope_controls_layout)
        left_layout.addWidget(slope_controls)

        content_layout.addWidget(left_frame, stretch=1)

        # --- Right Panel (Plot) ---
        right_frame = QFrame()
        right_layout = QVBoxLayout(right_frame)
        right_layout.addWidget(QLabel("RM Points Plot"))
        self.plot_widget = pg.PlotWidget()
        self.plot_widget.setLabel('left', 'Value')
        self.plot_widget.setLabel('bottom', 'Sample Index')
        self.plot_widget.showGrid(x=True, y=True)
        self.plot_widget.addLegend()
        self.plot_widget.setBackground('w')
        right_layout.addWidget(self.plot_widget, stretch=2)
        content_layout.addWidget(right_frame, stretch=2)

        main_layout.addWidget(content_frame)
        self.update_navigation_buttons()

        # Highlight point (yellow circle)
        self.highlight_point = pg.ScatterPlotItem(size=20, pen=pg.mkPen('yellow', width=4), brush=None, symbol='o')
        self.plot_widget.addItem(self.highlight_point)

    # راست‌کلیک منو برای ignore/unignore
    def show_rm_context_menu(self, pos):
        index = self.rm_table.indexAt(pos)
        if not index.isValid():
            return
        row = index.row()
        if row < 0 or row >= len(self.current_valid_pivot_indices):
            return

        pivot = self.current_valid_pivot_indices[row]
        # نقاط واقعاً empty را نمی‌گذاریم دستی ignore کنیم
        if pivot in self.empty_pivot_set:
            return

        menu = QMenu(self)
        if pivot in self.ignored_pivots:
            action = menu.addAction("Unignore this point)")
        else:
            action = menu.addAction("Ignore this point")

        if menu.exec(self.rm_table.mapToGlobal(pos)) == action:
            if pivot in self.ignored_pivots:
                self.ignored_pivots.remove(pivot)
            else:
                self.ignored_pivots.add(pivot)
            self.update_displays()  # همه چیز دوباره محاسبه می‌شود
    # جدید: کلیک روی جدول detail → highlight نقطه
    def on_detail_table_clicked(self, index):
        row = index.row()
        if row < 0 or self.selected_row < 0: return

        data = self.get_data_between_rm()
        if data.empty or row >= len(data): return

        pivot = data.iloc[row]['pivot_index']
        orig_y = data.iloc[row][self.selected_element]

        self.selected_point_pivot = pivot
        self.selected_point_y = orig_y
        self.highlight_point.setData([pivot], [orig_y])

    # جدید: کلیک روی نقطه در نمودار → انتخاب سطر مربوطه در جدول
    def handle_point_click(self, table_type, scatter, points, ev):
        if not points: return
        pt = points[0]
        label = pt.data()
        pivot = pt.pos().x()
        y = pt.pos().y()

        self.selected_point_pivot = pivot
        self.selected_point_y = y
        self.highlight_point.setData([pivot], [y])

        if table_type == 'rm' or self.keyword.lower() in str(label).lower():
            model = self.rm_table.model()
            for r in range(model.rowCount()):
                if model.item(r, 0).text().startswith(label):
                    self.rm_table.selectRow(r)
                    self.selected_row = r
                    self.update_detail_table()
                    break
        else:
            model = self.detail_table.model()
            for r in range(model.rowCount()):
                if model.item(r, 0).text() == label:
                    self.detail_table.selectRow(r)
                    break

    def on_empty_rows_received(self, empty_df):
        self.empty_rows_from_check = empty_df.copy()

    def update_navigation_buttons(self):
        self.prev_btn.setEnabled(self.current_nav_index > 0)
        self.next_btn.setEnabled(self.current_nav_index < len(self.navigation_list) - 1)
        enabled = bool(self.current_rm_num is not None and self.selected_element)
        self.up_button.setEnabled(enabled); self.down_button.setEnabled(enabled); self.reset_button.setEnabled(enabled)
        self.slope_spinbox.setEnabled(enabled); self.auto_optimize_flat_button.setEnabled(enabled); self.auto_optimize_zero_button.setEnabled(enabled)

    def update_labels(self):
        self.label_label.setText(f"Current RM: {self.current_rm_num if self.current_rm_num is not None else 'None'}")
        if self.element_combo.count() > 0:
            self.element_combo.blockSignals(True); self.element_combo.setCurrentText(self.selected_element or ''); self.element_combo.blockSignals(False)

    def has_changes(self):
        if len(self.original_rm_values) == 0 or len(self.display_rm_values) == 0:
            return False
        return not np.allclose(self.original_rm_values, self.display_rm_values, rtol=1e-5, atol=1e-8, equal_nan=True)

    def prompt_apply_changes(self):
        if self.has_changes():
            reply = QMessageBox.question(self, 'Apply Changes', 'Do you want to apply the changes to this RM?', QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No, QMessageBox.StandardButton.No)
            if reply == QMessageBox.StandardButton.Yes:
                self.apply_to_single_rm()

    def prev(self):
        if self.current_nav_index > 0:
            self.prompt_apply_changes()
            self.current_nav_index -= 1
            self.selected_element, self.current_rm_num = self.navigation_list[self.current_nav_index]
            self.selected_row = -1
            self.selected_point_pivot = None
            self.selected_point_y = None
            self.update_labels(); self.update_displays(); self.update_navigation_buttons()

    def next(self):
        if self.current_nav_index < len(self.navigation_list) - 1:
            self.prompt_apply_changes()
            self.current_nav_index += 1
            self.selected_element, self.current_rm_num = self.navigation_list[self.current_nav_index]
            self.selected_row = -1
            self.selected_point_pivot = None
            self.selected_point_y = None
            self.update_labels(); self.update_displays(); self.update_navigation_buttons()

    def on_element_changed(self, text):
        if text and text in self.elements:
            self.selected_element = text
            for idx, (el, num) in enumerate(self.navigation_list):
                if el == self.selected_element and num == self.current_rm_num:
                    self.current_nav_index = idx
                    break
            self.selected_row = -1
            self.selected_point_pivot = None
            self.selected_point_y = None
            self.update_displays(); self.update_navigation_buttons()

    def update_displays(self):
        if self.current_rm_num is not None and self.selected_element:
            self.display_rm_table()
            self.update_plot()
            self.update_detail_table()

    def start_check_rm_thread(self):
        keyword = self.keyword_entry.text().strip()
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
        self.progress_dialog.close()
        self.all_initial_rm_df = results['rm_df'].copy(deep=True)
        self.all_rm_df = results['rm_df'].copy(deep=True)
        self.all_positions_df = results['positions_df']
        self.all_segments = results['segments']
        self.all_pivot_df = results['pivot_df'].copy(deep=True)
        self.elements = results['elements']

        self.file_ranges = self.app.file_ranges if hasattr(self.app, 'file_ranges') else []

        if len(self.file_ranges) > 1:
            if self.file_selector.count() == 0:
                self.file_selector.addItem("All")
                self.file_selector.addItems([fr['clean_name'] for fr in self.file_ranges])
                self.left_layout.insertLayout(1, self.file_selector_layout)
            self.filter_by_file(-1)
            self.current_file_index = 0
            self.file_selector.setCurrentIndex(0)
        else:
            self.initial_rm_df = self.all_initial_rm_df
            self.rm_df = self.all_rm_df
            self.positions_df = self.all_positions_df
            self.segments = self.all_segments
            self.pivot_df = self.all_pivot_df
            self.unique_rm_nums = sorted(self.rm_df['rm_num'].unique())

            if self.unique_rm_nums and self.elements:
                self.navigation_list = [(el, num) for el in self.elements for num in self.unique_rm_nums]
                self.current_nav_index = 0
                self.selected_element, self.current_rm_num = self.navigation_list[0]
                self.element_combo.addItems(self.elements)
                self.update_labels(); self.update_displays()
                self.auto_optimize_flat_button.setEnabled(True); self.auto_optimize_zero_button.setEnabled(True)

        self.save_corrected_drift()
        self.data_changed.emit(); self.update_navigation_buttons()

    def filter_by_file(self, index):
        if index < 0:  # All selected
            self.pivot_df = self.all_pivot_df.copy()
            self.rm_df = self.all_rm_df.copy()
            self.initial_rm_df = self.all_initial_rm_df.copy()
            self.positions_df = self.all_positions_df.copy()
            self.segments = self._create_segments(self.positions_df)
            self.unique_rm_nums = sorted(self.rm_df['rm_num'].unique())
        else:
            fr = self.file_ranges[index]
            start = fr['start_pivot_row']
            end = fr['end_pivot_row']

            if 'pivot_index' in self.all_pivot_df.columns:
                self.pivot_df = self.all_pivot_df[self.all_pivot_df['pivot_index'].between(start, end)].copy()
                self.rm_df = self.all_rm_df[self.all_rm_df['pivot_index'].between(start, end)].copy()
                self.initial_rm_df = self.all_initial_rm_df[self.all_initial_rm_df['pivot_index'].between(start, end)].copy()
                self.positions_df = self.all_positions_df[self.all_positions_df['pivot_index'].between(start, end)].copy()
            else:
                self.pivot_df = self.all_pivot_df.iloc[start:end+1].copy()
                self.rm_df = self.all_rm_df.iloc[start:end+1].copy()
                self.initial_rm_df = self.all_initial_rm_df.iloc[start:end+1].copy()
                self.positions_df = self.all_positions_df.iloc[start:end+1].copy()

            self.segments = self._create_segments(self.positions_df)
            self.unique_rm_nums = sorted(self.rm_df['rm_num'].unique())

        if self.unique_rm_nums and self.elements:
            self.navigation_list = [(el, num) for el in self.elements for num in self.unique_rm_nums]
            self.current_nav_index = 0
            self.selected_element, self.current_rm_num = self.navigation_list[0]
            self.element_combo.clear()
            self.element_combo.addItems(self.elements)
            self.update_labels(); self.update_displays()
            self.auto_optimize_flat_button.setEnabled(True); self.auto_optimize_zero_button.setEnabled(True)
        else:
            self.current_nav_index = -1

        self.selected_row = -1
        self.selected_point_pivot = None
        self.selected_point_y = None
        self.update_navigation_buttons()

    def on_file_selected(self, index):
        self.prompt_apply_changes()
        if index == 0:
            self.filter_by_file(-1)
            self.per_file_checkbox.setEnabled(True)
        else:
            self.filter_by_file(index - 1)
            self.per_file_checkbox.setEnabled(False)
        self.current_file_index = index

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

    def on_check_rm_error(self, message):
        self.progress_dialog.close()
        QMessageBox.critical(self, "Error", message)

    def display_rm_table(self):
        model = QStandardItemModel()
        model.setHorizontalHeaderLabels(["RM Label", "Next RM", "Type", "Original Value", "Current Value", "Ratio"])

        label_df = self.rm_df[self.rm_df['rm_num'] == self.current_rm_num].sort_values('pivot_index')
        initial_label_df = self.initial_rm_df[self.initial_rm_df['rm_num'] == self.current_rm_num].sort_values('pivot_index')

        # همسان‌سازی طول
        if len(label_df) != len(initial_label_df):
            common_pivot = np.intersect1d(label_df['pivot_index'], initial_label_df['pivot_index'])
            label_df = label_df[label_df['pivot_index'].isin(common_pivot)].sort_values('pivot_index')
            initial_label_df = initial_label_df[initial_label_df['pivot_index'].isin(common_pivot)].sort_values('pivot_index')

        pivot_indices = label_df['pivot_index'].values
        original_values = pd.to_numeric(initial_label_df[self.selected_element], errors='coerce').values
        display_values = pd.to_numeric(label_df[self.selected_element], errors='coerce').values

        valid_mask = ~np.isnan(original_values) & ~np.isnan(display_values)
        self.current_valid_pivot_indices = pivot_indices[valid_mask]
        self.original_rm_values = original_values[valid_mask]
        self.display_rm_values = display_values[valid_mask]
        self.rm_types = label_df.loc[label_df.index[valid_mask], 'rm_type'].values
        self.solution_labels_for_group = label_df.loc[label_df.index[valid_mask], 'Solution Label'].values

        # تشخیص نقاط واقعاً خالی و دستی ignore شده
        self.empty_pivot_set = set(self.empty_rows_from_check['original_index'].dropna().astype(int).tolist()) \
            if not self.empty_rows_from_check.empty and 'original_index' in self.empty_rows_from_check.columns else set()

        is_really_empty = np.array([p in self.empty_pivot_set for p in self.current_valid_pivot_indices], dtype=bool)
        is_manually_ignored = np.array([p in self.ignored_pivots for p in self.current_valid_pivot_indices], dtype=bool)
        effective_empty = is_really_empty | is_manually_ignored

        blue_pivot_indices = self.current_valid_pivot_indices[~effective_empty]
        blue_index_to_pos = {idx: i for i, idx in enumerate(blue_pivot_indices)}

        if len(self.display_rm_values) == 0:
            model.appendRow([QStandardItem("No Data") for _ in range(6)])
        else:
            for i in range(len(self.display_rm_values)):
                current_rm_label = f"{self.solution_labels_for_group[i]}-{self.current_valid_pivot_indices[i]}"
                next_rm_label = "N/A"
                if not effective_empty[i]:
                    pos = blue_index_to_pos.get(self.current_valid_pivot_indices[i])
                    if pos is not None and pos < len(blue_pivot_indices) - 1:
                        next_rm_label = f"{self.solution_labels_for_group[pos + 1]}-{blue_pivot_indices[pos + 1]}"

                orig_val = self.original_rm_values[i]
                curr_val = self.display_rm_values[i]
                ratio = curr_val / orig_val if orig_val != 0 else np.nan
                rm_type = self.rm_types[i]

                row_items = [
                    QStandardItem(current_rm_label),
                    QStandardItem(next_rm_label),
                    QStandardItem(rm_type),
                    QStandardItem(f"{orig_val:.3f}"),
                    QStandardItem(f"{curr_val:.3f}"),
                    QStandardItem(f"{ratio:.3f}" if pd.notna(ratio) else "N/A")
                ]

                # رنگ‌بندی
                if is_really_empty[i]:
                    bg = QColor('red')
                    fg = QColor('white')
                elif is_manually_ignored[i]:
                    bg = QColor('#FF9800')  # نارنجی
                    fg = QColor('white')
                else:
                    bg = None
                    fg = None

                if bg:
                    for item in row_items:
                        item.setBackground(bg)
                        item.setForeground(fg)
                        item.setEditable(False)
                else:
                    for j in [0, 1, 3]: row_items[j].setEditable(False)
                    row_items[4].setEditable(True)
                    row_items[5].setEditable(True)
                    color_map = {'Base': '#2E7D32', 'Check': '#FF6B00', 'Cone': '#7B1FA2'}
                    color = QColor(color_map.get(rm_type, '#000000'))
                    row_items[2].setForeground(color)
                    row_items[2].setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))

                model.appendRow(row_items)

        self.rm_table.setModel(model)
        try: model.itemChanged.disconnect()
        except: pass
        model.itemChanged.connect(self.on_rm_value_changed)

        self.update_slope_from_data()

        if 0 <= self.selected_row < len(self.current_valid_pivot_indices):
            self.rm_table.selectRow(self.selected_row)

    def on_rm_value_changed(self, item):
        row = item.row()
        model = self.rm_table.model()
        try:
            if item.column() == 4:  # Current Value
                val = float(item.text())
                self.display_rm_values[row] = val
                ratio = val / self.original_rm_values[row] if self.original_rm_values[row] != 0 else np.nan
                model.item(row, 5).setText(f"{ratio:.3f}" if pd.notna(ratio) else "N/A")
            elif item.column() == 5:  # Ratio
                ratio = float(item.text())
                val = self.original_rm_values[row] * ratio
                self.display_rm_values[row] = val
                model.item(row, 4).setText(f"{val:.3f}")

            self.selected_row = row
            self.update_rm_data()
            self.update_plot(); self.update_slope_from_data()
            self.update_detail_table()
        except ValueError as e:
            QMessageBox.warning(self, "Invalid Value", str(e))
            # برگرداندن مقدار قبلی

    def on_table_row_clicked(self, index):
        self.selected_row = index.row()

        if 0 <= self.selected_row < len(self.current_valid_pivot_indices):
            pivot = self.current_valid_pivot_indices[self.selected_row]
            y = self.display_rm_values[self.selected_row]
            self.selected_point_pivot = pivot
            self.selected_point_y = y
            self.highlight_point.setData([pivot], [y])

        self.update_plot()
        self.update_detail_table()

    def update_rm_data(self):
        if len(self.display_rm_values) == 0: return

        valid_mask = ~np.isnan(self.display_rm_values)
        valid_pivot_indices = np.array(self.current_valid_pivot_indices)[valid_mask]
        valid_display_values = self.display_rm_values[valid_mask]

        label_df = self.rm_df[(self.rm_df['rm_num'] == self.current_rm_num) & (self.rm_df['pivot_index'].isin(valid_pivot_indices))].sort_values('pivot_index').reset_index(drop=True)

        if len(label_df) != len(valid_display_values): return

        for i, row in label_df.iterrows():
            self.rm_df.loc[self.rm_df['pivot_index'] == row['pivot_index'], self.selected_element] = valid_display_values[i]
            self.all_rm_df.loc[self.all_rm_df['pivot_index'] == row['pivot_index'], self.selected_element] = valid_display_values[i]

            df = self.app.results.last_filtered_data
            if 'original_index' not in df.columns:
                if 'pivot_index' in df.columns:
                    df['original_index'] = df['pivot_index']
                else:
                    df['original_index'] = df.index

            cond = (df['original_index'] == row['original_index'])
            if not df[cond].empty:
                df.loc[cond, self.selected_element] = valid_display_values[i]

    def update_rm_table_ratios(self):
        model = self.rm_table.model()
        for i in range(model.rowCount()):
            if i < len(self.original_rm_values):
                ratio = self.display_rm_values[i] / self.original_rm_values[i] if self.original_rm_values[i] != 0 else np.nan
                model.item(i, 5).setText(f"{ratio:.3f}" if pd.notna(ratio) else "N/A")

    def update_slope_from_data(self):
        if len(self.display_rm_values) >= 2:
            x = self.current_valid_pivot_indices
            is_really_empty = np.array([p in self.empty_pivot_set for p in x])
            is_ignored = np.array([p in self.ignored_pivots for p in x])
            normal_mask = ~(is_really_empty | is_ignored)
            if np.sum(normal_mask) >= 2:
                self.current_slope = np.polyfit(x[normal_mask], self.display_rm_values[normal_mask], 1)[0]
            else:
                self.current_slope = 0.0
        else:
            self.current_slope = 0.0

        self.slope_spinbox.blockSignals(True)
        self.slope_spinbox.setValue(self.current_slope)
        self.slope_spinbox.blockSignals(False)
        self.slope_label.setText(f"Current Slope: {self.current_slope:.3f}")

    def update_plot(self):
        self.plot_widget.clear()
        self.highlight_point.setData([], [])

        if len(self.display_rm_values) == 0:
            return

        valid_mask = ~np.isnan(self.display_rm_values)
        pivot_valid = self.current_valid_pivot_indices[valid_mask]
        x_valid = self.current_valid_pivot_indices[valid_mask]
        y_valid = self.display_rm_values[valid_mask]
        types_valid = self.rm_types[valid_mask]

        is_really_empty_arr = np.array([p in self.empty_pivot_set for p in x_valid])
        is_ignored_arr = np.array([p in self.ignored_pivots for p in x_valid])
        effective_empty_arr = is_really_empty_arr | is_ignored_arr

        # رنگ نقاط RM
        brush_colors = []
        for i in range(len(x_valid)):
            if is_ignored_arr[i]:
                brush_colors.append('#FF9800')      # نارنجی = دستی ignore
            elif is_really_empty_arr[i]:
                brush_colors.append('#B0BEC5')      # خاکستری = واقعاً خالی
            else:
                brush_colors.append('#2E7D32')      # سبز = معتبر

        tip_func = lambda x, y, data: f"Label: {data}\nValue: {y:.3f}"

        # --- رسم نقاط RM ---
        self.rm_scatter = pg.ScatterPlotItem(
            x_valid, y_valid,
            symbol=[{'Base': 'o', 'Check': 't', 'Cone': 's'}.get(t, 'o') for t in types_valid],
            size=11, brush=brush_colors, pen='w',
            hoverable=True, tip=tip_func,
            data=self.solution_labels_for_group[valid_mask]
        )
        self.plot_widget.addItem(self.rm_scatter)

        # --- اتصال کلیک روی نقاط RM ---
        self.rm_scatter.sigClicked.connect(partial(self.handle_point_click, 'rm'))

        # --- رسم داده‌های بین RMها (Original + Corrected) ---
        self.data_scatters = []  # برای اتصال کلیک
        filter_text = self.filter_entry.text().strip().lower()

        normal_mask = ~effective_empty_arr

        for i in range(len(pivot_valid) - 1):
            if not normal_mask[i] or not normal_mask[i + 1]:
                continue  # اگر یکی از دو RM ignore یا خالی بود، داده‌های بینشون رسم نمیشه

            pivot_prev = pivot_valid[i]
            pivot_curr = pivot_valid[i + 1]

            min_row = self.positions_df[self.positions_df['pivot_index'] == pivot_prev]
            max_row = self.positions_df[self.positions_df['pivot_index'] == pivot_curr]
            if min_row.empty or max_row.empty:
                continue

            min_pos = min_row['min'].values[0]
            max_pos = max_row['max'].values[0]

            cond = (self.pivot_df['original_index'] > min_pos) & \
                   (self.pivot_df['original_index'] < max_pos) & \
                   self.pivot_df[self.selected_element].notna()

            between_data = self.pivot_df[cond].copy().sort_values('original_index')
            if between_data.empty:
                continue
            if filter_text:
                mask = between_data['Solution Label'].str.lower().str.contains(filter_text, na=False)
                between_data = between_data[mask]
                if between_data.empty:
                    continue

            between_x = between_data['pivot_index'].values
            between_y_orig = between_data[self.selected_element].values
            labels = between_data['Solution Label'].values

            # Original points (آبی)
            scatter_orig = pg.ScatterPlotItem(
                between_x, between_y_orig,
                symbol='o', size=7, brush='#2196F3', pen='#1976D2',
                hoverable=True, tip=tip_func, data=labels
            )
            self.plot_widget.addItem(scatter_orig)
            scatter_orig.sigClicked.connect(partial(self.handle_point_click, 'detail'))
            self.data_scatters.append(scatter_orig)

            # Corrected points (قرمز ×)
            ratio = y_valid[i + 1] / self.original_rm_values[i + 1] if self.original_rm_values[i + 1] != 0 else 1.0
            between_y_corr = self.calculate_corrected_values(between_y_orig, ratio)

            scatter_corr = pg.ScatterPlotItem(
                between_x, between_y_corr,
                symbol='x', size=9, brush='#F44336', pen='#D32F2F',
                hoverable=True,
                tip=lambda x, y, data: f"Label: {data}\nCorrected: {y:.3f}",
                data=labels
            )
            self.plot_widget.addItem(scatter_corr)
            scatter_corr.sigClicked.connect(partial(self.handle_point_click, 'detail'))
            self.data_scatters.append(scatter_corr)

        # --- خطوط و trendlineها (فقط از نقاط معتبر) ---
        line_colors = ['#43A047', '#FF6B00', '#7B1FA2', '#1A3C34']
        for seg_idx, seg in enumerate(self.segments):
            seg_positions = seg['positions']
            seg_pivot = seg_positions['pivot_index'].values
            seg_mask = np.isin(x_valid, seg_pivot) & normal_mask
            if np.any(seg_mask):
                x_n = x_valid[seg_mask]
                y_n = y_valid[seg_mask]
                color_idx = seg['segment_id'] % len(line_colors)
                line_pen = pg.mkPen(line_colors[color_idx], width=2.5)
                self.plot_widget.plot(x_n, y_n, pen=line_pen)
                if len(x_n) >= 2:
                    p = np.poly1d(np.polyfit(x_n, y_n, 1))
                    self.plot_widget.plot(x_n, p(x_n), pen=pg.mkPen(line_colors[color_idx], width=2, style=Qt.PenStyle.DashLine))

        # Global trendline
        if np.sum(normal_mask) >= 2:
            x_all = x_valid[normal_mask]
            y_all = y_valid[normal_mask]
            p_global = np.poly1d(np.polyfit(x_all, y_all, 1))
            self.plot_widget.plot(x_all, p_global(x_all), pen=pg.mkPen('k', width=2, style=Qt.PenStyle.DashLine))

        # Highlight خط بین دو RM انتخاب شده
        if self.selected_row >= 0 and self.selected_row < len(pivot_valid) - 1 and normal_mask[self.selected_row] and normal_mask[self.selected_row + 1]:
            start_x = pivot_valid[self.selected_row]
            start_y = y_valid[self.selected_row]
            end_x = pivot_valid[self.selected_row + 1]
            end_y = y_valid[self.selected_row + 1]

            self.plot_widget.plot([start_x, end_x], [start_y, end_y],
                                pen=pg.mkPen('#FFD700', width=5))

            start_point = pg.ScatterPlotItem([start_x], [start_y], symbol='s', size=18, brush='#1976D2', pen=pg.mkPen('white', width=3))
            end_point = pg.ScatterPlotItem([end_x], [end_y], symbol='o', size=18, brush='#D32F2F', pen=pg.mkPen('white', width=3))
            self.plot_widget.addItem(start_point)
            self.plot_widget.addItem(end_point)

        # Highlight نقطه کلیک شده
        if self.selected_point_pivot is not None:
            self.highlight_point.setData([self.selected_point_pivot], [self.selected_point_y])

        # خطوط عمودی مرز فایل‌ها
        if self.current_file_index == 0 and len(self.file_ranges) > 1:
            for fr in self.file_ranges:
                self.plot_widget.addItem(pg.InfiniteLine(pos=fr['start_pivot_row'], angle=90, pen=pg.mkPen('gray', style=Qt.PenStyle.DashLine)))
                self.plot_widget.addItem(pg.InfiniteLine(pos=fr['end_ pivot_row'], angle=90, pen=pg.mkPen('gray', style=Qt.PenStyle.DashLine)))

        self.plot_widget.autoRange()

    def get_data_between_rm(self):
        if self.selected_row < 0 or self.selected_row >= len(self.current_valid_pivot_indices) - 1: return pd.DataFrame()

        pivot_prev = self.current_valid_pivot_indices[self.selected_row]
        pivot_curr = self.current_valid_pivot_indices[self.selected_row + 1]

        min_row = self.positions_df[self.positions_df['pivot_index'] == pivot_prev]
        max_row = self.positions_df[self.positions_df['pivot_index'] == pivot_curr]

        if min_row.empty or max_row.empty: return pd.DataFrame()

        min_pos = min_row['min'].values[0]
        max_pos = max_row['max'].values[0]

        cond = (self.pivot_df['original_index'] > min_pos) & (self.pivot_df['original_index'] < max_pos) & self.pivot_df[self.selected_element].notna()
        data = self.pivot_df[cond].copy().sort_values('original_index')

        filter_text = self.filter_entry.text().strip().lower()
        if filter_text:
            filter_mask = data['Solution Label'].str.lower().str.contains(filter_text)
            data = data[filter_mask]
        return data

    def calculate_corrected_values(self, original_values, current_ratio):
        n = len(original_values)
        if n == 0: return np.array([])
        delta = current_ratio - 1.0
        step_delta = delta / n if n > 0 else 0.0
        stepwise = self.stepwise_checkbox.isChecked()
        return original_values * np.array([1.0 + step_delta * (j + 1) if stepwise else current_ratio for j in range(n)])

    def update_detail_table(self):
        model = QStandardItemModel()
        model.setHorizontalHeaderLabels(["Solution Label", "Original Value", "Corrected Value"])

        data = self.get_data_between_rm()
        if data.empty:
            self.detail_table.setModel(model)
            return

        orig = data[self.selected_element].values
        ratio = self.display_rm_values[self.selected_row + 1] / self.original_rm_values[self.selected_row + 1] if self.original_rm_values[self.selected_row + 1] != 0 else 1.0
        corr = self.calculate_corrected_values(orig, ratio)

        for i in range(len(data)):
            sl = data.iloc[i]['Solution Label']
            o = orig[i]
            c = corr[i]
            model.appendRow([QStandardItem(sl), QStandardItem(f"{o:.3f}"), QStandardItem(f"{c:.3f}")])

        self.detail_table.setModel(model)

    def update_slope(self, value):
        if len(self.display_rm_values) >= 2:
            delta = value - self.current_slope
            x = self.current_valid_pivot_indices
            x_norm = x - x[0]
            self.display_rm_values += delta * x_norm
            self.current_slope = value
            self.slope_label.setText(f"Current Slope: {self.current_slope:.3f}")
            self.update_rm_data(); self.update_plot();
            self.update_rm_table_ratios()
            self.update_detail_table()

    def rotate_up(self): 
        self.current_slope += 0.1; 
        self.slope_spinbox.setValue(self.current_slope); 
        self.update_slope(self.current_slope)

    def rotate_down(self): 
        self.current_slope -= 0.1; 
        self.slope_spinbox.setValue(self.current_slope); 
        self.update_slope(self.current_slope)

    def reset_to_original(self):
        self.display_rm_values = self.original_rm_values.copy()
        self.update_rm_data(); self.update_plot(); self.update_rm_table_ratios(); self.update_slope_from_data()
        self.update_detail_table()
        model = self.rm_table.model()
        for i in range(model.rowCount()):
            if i < len(self.display_rm_values):
                model.item(i, 4).setText(f"{self.display_rm_values[i]:.3f}")
        QMessageBox.information(self, "Info", "Reset to original values.")

    def auto_optimize_to_flat(self):
        if len(self.display_rm_values) == 0:
            return
        if self.current_file_index == 0 and self.per_file_checkbox.isChecked():
            self.auto_optimize_to_flat_per_file()
            return
        empty_set = set(self.empty_rows_from_check['original_index'].dropna().astype(int).tolist()) if not self.empty_rows_from_check.empty else set()
        rm_mask = (self.rm_df['rm_num'] == self.current_rm_num)
        if not rm_mask.any():
            return
        y = self.rm_df.loc[rm_mask, self.selected_element].astype(float).values
        pivot = self.rm_df.loc[rm_mask, 'pivot_index'].values
        is_empty = np.array([p in empty_set for p in pivot])
        normal_mask = ~is_empty & ~np.isnan(y)
        if normal_mask.sum() == 0:
            return
        seg_dict = dict(zip(self.positions_df['pivot_index'], self.positions_df['segment_id']))
        unique_segs = np.unique([seg_dict.get(p, -1) for p in pivot[normal_mask]])
        if self.global_optimize_checkbox.isChecked():
            first_idx = np.where(normal_mask)[0][0]
            first_val = y[first_idx]
            y[normal_mask] = first_val
        else:
            for seg_id in unique_segs:
                if seg_id == -1:
                    continue
                seg_mask = np.array([seg_dict.get(p, -1) == seg_id for p in pivot])
                seg_normal_mask = seg_mask & normal_mask
                if seg_normal_mask.sum() == 0:
                    continue
                first_idx = np.where(seg_normal_mask)[0][0]
                first_val = y[first_idx]
                y[seg_normal_mask] = first_val
        self.rm_df.loc[rm_mask, self.selected_element] = y
        self.sync_rm_to_all()
        self.update_displays()
        self.update_slope_from_data()
        QMessageBox.information(self, "Info", "Selected RM optimized to flat relative to the first valid point in each segment (or globally if checked).")

    def auto_optimize_to_flat_per_file(self):
        for file_idx, fr in enumerate(self.file_ranges):
            start = fr['start_pivot_row']
            end = fr['end_pivot_row']
            file_rm_mask = (self.all_rm_df['pivot_index'].between(start, end)) & (self.all_rm_df['rm_num'] == self.current_rm_num)
            if not file_rm_mask.any():
                continue
            y_file = self.all_rm_df.loc[file_rm_mask, self.selected_element].astype(float).values
            pivot_file = self.all_rm_df.loc[file_rm_mask, 'pivot_index'].values
            empty_set = set(self.empty_rows_from_check['original_index'].dropna().astype(int).tolist()) if not self.empty_rows_from_check.empty else set()
            is_empty_file = np.array([p in empty_set for p in pivot_file])
            normal_mask_file = ~is_empty_file & ~np.isnan(y_file)
            if normal_mask_file.sum() == 0:
                continue
            # Find reference RM in this file, assume first valid point
            first_idx = np.where(normal_mask_file)[0][0]
            first_val = y_file[first_idx]
            y_file[normal_mask_file] = first_val
            self.all_rm_df.loc[file_rm_mask, self.selected_element] = y_file
        if self.current_file_index == 0:
            self.rm_df[self.selected_element] = self.all_rm_df[self.selected_element]
        self.sync_rm_to_all()
        self.update_displays()
        self.update_slope_from_data()
        QMessageBox.information(self, "Info", "Optimized to flat per file based on reference RM in each file.")

    def auto_optimize_slope_to_zero(self):
        if len(self.display_rm_values) < 2:
            return
        if self.current_file_index == 0 and self.per_file_checkbox.isChecked():
            self.auto_optimize_slope_to_zero_per_file()
            return
        empty_set = set(self.empty_rows_from_check['original_index'].dropna().astype(int).tolist()) if not self.empty_rows_from_check.empty else set()
        rm_mask = (self.rm_df['rm_num'] == self.current_rm_num)
        if not rm_mask.any():
            return
        y = self.rm_df.loc[rm_mask, self.selected_element].astype(float).values
        pivot = self.rm_df.loc[rm_mask, 'pivot_index'].values
        is_empty = np.array([p in empty_set for p in pivot])
        normal_mask = ~is_empty & ~np.isnan(y)
        if normal_mask.sum() < 2:
            return
        seg_dict = dict(zip(self.positions_df['pivot_index'], self.positions_df['segment_id']))
        x = pivot
        if self.global_optimize_checkbox.isChecked():
            x_n = x[normal_mask]
            y_n = y[normal_mask].copy()
            if len(x_n) >= 2:
                slope, intercept = np.polyfit(x_n, y_n, 1)
                first_x = x_n[0]
                y_n -= slope * (x_n - first_x)
            y[normal_mask] = y_n
        else:
            unique_segs = np.unique([seg_dict.get(p, -1) for p in pivot[normal_mask]])
            for seg_id in unique_segs:
                if seg_id == -1:
                    continue
                seg_mask = np.array([seg_dict.get(p, -1) == seg_id for p in pivot])
                seg_normal_mask = seg_mask & normal_mask
                if seg_normal_mask.sum() < 2:
                    continue
                x_n = x[seg_normal_mask]
                y_n = y[seg_normal_mask].copy()
                if len(x_n) >= 2:
                    slope, intercept = np.polyfit(x_n, y_n, 1)
                    first_x = x_n[0]
                    y_n -= slope * (x_n - first_x)
                y[seg_normal_mask] = y_n
        self.rm_df.loc[rm_mask, self.selected_element] = y
        self.sync_rm_to_all()
        self.update_displays()
        self.update_slope_from_data()
        QMessageBox.information(self, "Info", "Slope optimized to zero for the selected RM in each segment (or globally if checked), preserving the starting point.")

    def auto_optimize_slope_to_zero_per_file(self):
        for file_idx, fr in enumerate(self.file_ranges):
            start = fr['start_pivot_row']
            end = fr['end_pivot_row']
            file_rm_mask = (self.all_rm_df['pivot_index'].between(start, end)) & (self.all_rm_df['rm_num'] == self.current_rm_num)
            if not file_rm_mask.any():
                continue
            y_file = self.all_rm_df.loc[file_rm_mask, self.selected_element].astype(float).values
            pivot_file = self.all_rm_df.loc[file_rm_mask, 'pivot_index'].values
            empty_set = set(self.empty_rows_from_check['original_index'].dropna().astype(int).tolist()) if not self.empty_rows_from_check.empty else set()
            is_empty_file = np.array([p in empty_set for p in pivot_file])
            normal_mask_file = ~is_empty_file & ~np.isnan(y_file)
            if normal_mask_file.sum() < 2:
                continue
            x_file = pivot_file
            x_n = x_file[normal_mask_file]
            y_n = y_file[normal_mask_file].copy()
            if len(x_n) >= 2:
                slope, intercept = np.polyfit(x_n, y_n, 1)
                first_x = x_n[0]
                y_n -= slope * (x_n - first_x)
            y_file[normal_mask_file] = y_n
            self.all_rm_df.loc[file_rm_mask, self.selected_element] = y_file
        if self.current_file_index == 0:
            self.rm_df[self.selected_element] = self.all_rm_df[self.selected_element]
        self.sync_rm_to_all()
        self.update_displays()
        self.update_slope_from_data()
        QMessageBox.information(self, "Info", "Slope optimized to zero per file based on reference RM in each file.")

    def apply_to_single_rm(self):
        if not self.selected_element or self.current_rm_num is None:
            QMessageBox.critical(self, "Error", "No element or RM number selected.")
            return
        self.undo_stack.append((self.app.results.last_filtered_data.copy(), self.rm_df.copy(), self.corrected_drift.copy()))
        self.undo_button.setEnabled(True)
        self.progress_dialog = QProgressDialog("Applying corrections...", "Cancel", 0, 100, self)
        self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        applier = ApplySingleRM(self.app, self.keyword, self.selected_element, self.current_rm_num, self.rm_df, self.initial_rm_df, self.segments, self.stepwise_checkbox.isChecked(), self.progress_dialog)
        results = applier.run()
        self.progress_dialog.close()
        if 'error' in results:
            QMessageBox.critical(self, "Error", results['error'])
        else:
            new_df = results['df']
            self.app.results.last_filtered_data = new_df
            self.rm_df = results['rm_df']
            self.sync_rm_to_all()
            self.corrected_drift.update(results['corrected_drift'])
            self.save_corrected_drift()
            self.results_update_requested.emit(new_df)
            self.update_displays()
            QMessageBox.information(self, "Success", "Corrections applied.")

    def sync_rm_to_all(self):
        for pivot, val in zip(self.rm_df['pivot_index'], self.rm_df[self.selected_element]):
            self.all_rm_df.loc[self.all_rm_df['pivot_index'] == pivot, self.selected_element] = val

    def save_corrected_drift(self):
        try:
            if not hasattr(self.app.results, 'corrected_drift'): self.app.results.corrected_drift = {}
            self.app.results.corrected_drift.update(self.corrected_drift)
            drift_data = [{'Solution Label': k[0], 'Element': k[1], 'Ratio': v} for k, v in self.corrected_drift.items()]
            drift_df = pd.DataFrame(drift_data)
            if not hasattr(self.app.results, 'report_change'): self.app.results.report_change = pd.DataFrame(columns=['Solution Label', 'Element', 'Ratio'])
            if not drift_df.empty:
                self.app.results.report_change = self.app.results.report_change[~self.app.results.report_change['Element'].isin(drift_df['Element'])]
                self.app.results.report_change = pd.concat([self.app.results.report_change, drift_df], ignore_index=True)
        except Exception as e:
            logger.error(f"Error saving corrected_drift: {str(e)}")

    def undo_correction(self):
        if self.undo_stack:
            df, self.rm_df, self.corrected_drift = self.undo_stack.pop()
            self.sync_rm_to_all()
            self.app.results.last_filtered_data = df
            self.save_corrected_drift()
            self.data_changed.emit(); self.app.notify_data_changed()
            self.update_displays()
            self.undo_button.setEnabled(bool(self.undo_stack))
            QMessageBox.information(self, "Success", "Last correction undone.")
            try:
                self.app.results.reset_cache()
                self.app.results.show_processed_data()
            except Exception as e:
                logger.error(f"Error updating results: {str(e)}")
        else:
            QMessageBox.warning(self, "Warning", "No corrections to undo.")