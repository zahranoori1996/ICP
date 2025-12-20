from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QCheckBox, QLabel, QLineEdit, QPushButton,
    QMessageBox, QTreeView, QGroupBox, QGridLayout, QSlider, QDialog, QComboBox
)
from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QStandardItemModel, QStandardItem
import pyqtgraph as pg
import re
import pandas as pd
import logging
from datetime import datetime
from styles.common import common_styles
from utils.var_main import BLANK_PATTERN
from utils.utils import get_concentration_column,is_numeric,format_number
class PivotPlotWindow(QWidget):
    data_changed = pyqtSignal()
    results_update_requested = pyqtSignal(pd.DataFrame)

    def __init__(self, parent, annotations):
        super().__init__(parent)
        self.setWindowFlags(Qt.WindowType.Window)
        self.setStyleSheet(common_styles)
        self.parent = parent
        self.annotations = annotations
        self.setWindowTitle("Verification Plot")
        self.setGeometry(100, 100, 1300, 900)
        self.setMinimumSize(800, 600)
        self.logger = logging.getLogger(__name__)
        # =================================================================
        # 1. داده‌های اولیه (قبل از هر چیز!)
        # =================================================================
        self.file_ranges = getattr(self.parent.app, 'file_ranges', [])
        self.current_file_index = -1
        self.elements = []
        self.current_element_index = -1
        if getattr(self.parent.results_frame, 'last_filtered_data', None) is not None:
            df = self.parent.results_frame.last_filtered_data
            self.elements = [col for col in df.columns if col != 'Solution Label']
            if self.elements:
                self.current_element_index = 0
                self.selected_element = self.elements[self.current_element_index]
        self.all_pivot_data = (
            self.parent.results_frame.last_filtered_data.copy()
            if getattr(self.parent.results_frame, 'last_filtered_data', None) is not None
            else pd.DataFrame()
        )
        self.all_original_df = self.parent.app.get_data()
        self.pivot_data = self.all_pivot_data.copy()
        self.original_df = self.all_original_df.copy() if self.all_original_df is not None else pd.DataFrame()
        # =================================================================
        # 2. params با مقدار پیش‌فرض
        # =================================================================
        empty_outliers = {el: set() for el in self.elements} if self.elements else {}
        self.params = {}
        for i in range(-1, len(self.file_ranges)):
            self.params[i] = {
                'range_low': 2.0, 'range_mid': 20.0,
                'range_high1': 10.0, 'range_high2': 8.0,
                'range_high3': 5.0, 'range_high4': 3.0,
                'preview_blank': 0.0, 'preview_scale': 1.0,
                'excluded_outliers': empty_outliers.copy(),
                'excluded_from_correct': set(),
                'scale_above_50': False,
                'scale_range_min': None, 'scale_range_max': None,
            }
        # =================================================================
        # 3. مقداردهی اولیه ویژگی‌ها (قبل از UI!)
        # =================================================================
        self.range_low = 2.0
        self.range_mid = 20.0
        self.range_high1 = 10.0
        self.range_high2 = 8.0
        self.range_high3 = 5.0
        self.range_high4 = 3.0
        self.preview_blank = 0.0
        self.preview_scale = 1.0
        self.excluded_outliers = empty_outliers.copy()
        self.excluded_from_correct = set()
        self.scale_range_min = None
        self.scale_range_max = None
        self.calibration_range = "[0 to 0]"
        self.blank_labels = []
        # =================================================================
        # 4. ساخت UI (اولین و مهم‌ترین!)
        # =================================================================
        self.setup_ui() # ← اینجا همه ویجت‌ها ساخته میشن
        # =================================================================
        # 5. حالا که ویجت‌ها وجود دارند → load_params امن است
        # =================================================================
        self.load_params() # مقدارهای ذخیره‌شده رو اعمال می‌کنه
        # =================================================================
        # 6. تنظیم اولیه فایل سلکتور (آخرین مرحله!)
        # =================================================================
        if len(self.file_ranges) > 1:
            self.file_selector.blockSignals(True) # ← مهم!
            self.file_selector.setCurrentIndex(self.current_file_index + 1)
            self.file_selector.blockSignals(False) # ← مهم!
        else:
            self.file_selector.blockSignals(True)
            self.file_selector.setCurrentIndex(0)
            self.file_selector.blockSignals(False)
        # =================================================================
        # 7. به‌روزرسانی اولیه
        # =================================================================
        if self.elements:
            self.update_calibration_range()
        self.update_navigation_buttons()
        self.update_plot()

    def setup_ui(self):
        main_layout = QHBoxLayout(self)
      
        # Left panel for controls
        control_panel = QGroupBox("Controls")
        control_panel.setStyleSheet(common_styles)
        control_layout = QVBoxLayout(control_panel)
        control_layout.setSpacing(10)
      
        # File selector if multiple files
        self.file_selector_layout = QHBoxLayout()
        self.file_selector_label = QLabel("Select File:")
        self.file_selector = QComboBox()
        self.file_selector.currentIndexChanged.connect(self.on_file_selected)
        self.file_selector_layout.addWidget(self.file_selector_label)
        self.file_selector_layout.addWidget(self.file_selector)
        if len(self.file_ranges) > 1:
            self.file_selector.addItem("All")
            self.file_selector.addItems([fr['clean_name'] for fr in self.file_ranges])
            control_layout.addLayout(self.file_selector_layout)
      
        # ComboBox for Element Selection
        self.element_combo = QComboBox()
        self.element_combo.addItems(self.elements)
        self.element_combo.setCurrentIndex(self.current_element_index)
        self.element_combo.currentIndexChanged.connect(self.element_changed)
        control_layout.addWidget(self.element_combo)
      
        # Navigation Buttons
        nav_layout = QHBoxLayout()
        self.prev_btn = QPushButton("Previous")
        self.prev_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.prev_btn.clicked.connect(self.prev_element)
        nav_layout.addWidget(self.prev_btn)
      
        self.next_btn = QPushButton("Next")
        self.next_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.next_btn.clicked.connect(self.next_element)
        nav_layout.addWidget(self.next_btn)
      
        control_layout.addLayout(nav_layout)
        show_layout = QHBoxLayout()
        # Show options
        self.show_check_crm = QCheckBox("Certificate", checked=True)
        self.show_check_crm.toggled.connect(self.update_plot)
        show_layout.addWidget(self.show_check_crm)
      
        self.show_pivot_crm = QCheckBox("CRM", checked=True)
        self.show_pivot_crm.toggled.connect(self.update_plot)
        show_layout.addWidget(self.show_pivot_crm)
      
        self.show_range = QCheckBox("Acceptable Range", checked=True)
        self.show_range.toggled.connect(self.update_plot)
        show_layout.addWidget(self.show_range)
        control_layout.addLayout(show_layout)
        exclude_layout = QHBoxLayout()
        exclude_btn = QPushButton("Exclude from Correct")
        exclude_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        exclude_btn.clicked.connect(self.open_exclude_window)
        exclude_layout.addWidget(exclude_btn)
        # Scale above 50
        self.scale_above_50 = QCheckBox("Scale >50 only", checked=False)
        self.scale_above_50.toggled.connect(self.update_plot)
        exclude_layout.addWidget(self.scale_above_50)
      
        control_layout.addLayout(exclude_layout)
      
        setting_control = QHBoxLayout()
        # Acceptable Ranges Button
        range_btn = QPushButton("Ranges")
        range_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        range_btn.clicked.connect(self.open_range_dialog)
        setting_control.addWidget(range_btn)
      
        # Select Verifications
        select_crms_btn = QPushButton("Select Verifications")
        select_crms_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        select_crms_btn.clicked.connect(self.open_select_crms_window)
        setting_control.addWidget(select_crms_btn)
        control_layout.addLayout(setting_control)
      
        # Scale Application Range
        scale_range_group = QGroupBox("Scale Application Range")
        scale_range_layout = QHBoxLayout(scale_range_group)
        scale_range_layout.addWidget(QLabel("Min:"))
        self.scale_range_min_edit = QLineEdit("")
        self.scale_range_min_edit.setFixedWidth(60)
        self.scale_range_min_edit.textChanged.connect(self.update_scale_range)
        scale_range_layout.addWidget(self.scale_range_min_edit)
      
        scale_range_layout.addWidget(QLabel("Max:"))
        self.scale_range_max_edit = QLineEdit("")
        self.scale_range_max_edit.setFixedWidth(60)
        self.scale_range_max_edit.textChanged.connect(self.update_scale_range)
        scale_range_layout.addWidget(self.scale_range_max_edit)
      
        self.scale_range_display = QLabel("Scale Range: Not Set")
        scale_range_layout.addWidget(self.scale_range_display)
        control_layout.addWidget(scale_range_group)
      
        reset_blank_layout = QHBoxLayout()
        # Preview Blank
        reset_blank_layout.addWidget(QLabel("Preview Blank:"))
        self.blank_edit = QLineEdit("0.0")
        self.blank_edit.textChanged.connect(self.update_preview_params)
        reset_blank_layout.addWidget(self.blank_edit)
        # Reset Blank and Scale Button
        reset_blank_scale_btn = QPushButton("Reset Blank and Scale")
        reset_blank_scale_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        reset_blank_scale_btn.clicked.connect(self.reset_blank_and_scale)
        reset_blank_layout.addWidget(reset_blank_scale_btn)
      
        control_layout.addLayout(reset_blank_layout)
        # Preview Scale Slider
        control_layout.addWidget(QLabel("Preview Scale (0-2):"))
        self.scale_slider = QSlider(Qt.Orientation.Horizontal)
        self.scale_slider.setMinimum(0)
        self.scale_slider.setMaximum(200)
        self.scale_slider.setValue(100)
        self.scale_slider.setTickInterval(10)
        self.scale_slider.valueChanged.connect(self.update_preview_params)
        control_layout.addWidget(self.scale_slider)
        self.scale_label = QLabel("Scale: 1.00")
        control_layout.addWidget(self.scale_label)
      
        # Blank labels display
        self.blank_display = QLabel("Blanks: None")
        control_layout.addWidget(self.blank_display)
      
        # Calibration range display
        self.calibration_display = QLabel(f"Calibration: {self.calibration_range}")
        control_layout.addWidget(self.calibration_display)
      
        # New: Filter for plot points
        filter_layout = QHBoxLayout()
        filter_label = QLabel("Filter Labels (e.g., rm):")
        self.filter_entry = QLineEdit()
        self.filter_entry.setPlaceholderText("Enter filter text")
        self.filter_entry.textChanged.connect(self.update_plot)
        filter_layout.addWidget(filter_label)
        filter_layout.addWidget(self.filter_entry)
        control_layout.addLayout(filter_layout)
      
        our_model_layout = QHBoxLayout()
        # Buttons
        report_btn = QPushButton("Report")
        report_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        report_btn.clicked.connect(self.show_report)
        our_model_layout.addWidget(report_btn)
      
        apply_model_btn = QPushButton("Apply Our Model")
        apply_model_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        apply_model_btn.clicked.connect(self.apply_model)
        our_model_layout.addWidget(apply_model_btn)
        control_layout.addLayout(our_model_layout)
      
        apply_layout = QHBoxLayout()
        undo_btn = QPushButton("Undo")
        undo_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        undo_btn.clicked.connect(self.undo_correction)
        apply_layout.addWidget(undo_btn)
        correct_btn = QPushButton("Correct")
        correct_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        correct_btn.clicked.connect(self.correct_crm_callback)
        apply_layout.addWidget(correct_btn)
      
        control_layout.addLayout(apply_layout)
      
        control_layout.addStretch()
      
        main_layout.addWidget(control_panel, 1)
      
        # Right panel for plots (now with two plots: main and secondary)
        plots_layout = QVBoxLayout()
        self.main_plot = pg.PlotWidget()
        self.main_plot.setStyleSheet("background-color: #FFFFFF;")
        self.main_plot.setBackground('w')
        self.main_plot.setMouseEnabled(x=True, y=True)
        self.main_plot.setMenuEnabled(True)
        self.main_plot.enableAutoRange(x=False, y=False)
        plots_layout.addWidget(self.main_plot, 3)
      
        # New: Secondary plot below the main one
        self.secondary_plot = pg.PlotWidget()
        self.secondary_plot.setStyleSheet("background-color: #FFFFFF;")
        self.secondary_plot.setBackground('w')
        self.secondary_plot.setLabel('bottom', 'Index')
        self.secondary_plot.setLabel('left', 'Value')
        self.secondary_plot.setMouseEnabled(x=True, y=True)
        self.secondary_plot.setMenuEnabled(True)
        plots_layout.addWidget(self.secondary_plot, 2)
      
        main_layout.addLayout(plots_layout, 3)
      
        self.main_plot.scene().sigMouseMoved.connect(self.show_tooltip)
        self.main_plot.scene().sigMouseClicked.connect(self.handle_click)
      
        self.secondary_plot.scene().sigMouseMoved.connect(self.show_secondary_tooltip)
      
        self.main_plot.setFocus()
      
        self.update_navigation_buttons()
        self.update_plot()

    def element_changed(self, index):
        """Handle element selection change from QComboBox."""
        if index >= 0:
            self.save_params()
            self.current_element_index = index
            self.selected_element = self.elements[self.current_element_index]
            self.load_params()
            self.update_calibration_range()
            self.update_navigation_buttons()
            self.update_plot()

    def on_file_selected(self, index):
        self.save_params()
        if index == 0:
            # All
            self.filter_by_file(-1)
        else:
            self.filter_by_file(index - 1)
        self.current_file_index = -1 if index == 0 else index - 1
        self.load_params()
        self.update_calibration_range()
        self.update_plot()

    def filter_by_file(self, index):
        if index == -1:
            # نمایش همه فایل‌ها
            self.pivot_data = self.all_pivot_data.copy()
            self.original_df = self.all_original_df.copy() if self.all_original_df is not None else pd.DataFrame()
        else:
            fr = self.file_ranges[index]
            start = fr['start_pivot_row']
            end = fr['end_pivot_row']
            # اگر ستون pivot_index وجود نداشت، از ایندکس ردیف استفاده کن
            if 'pivot_index' in self.all_pivot_data.columns:
                mask = self.all_pivot_data['pivot_index'].between(start, end)
            else:
                # استفاده از ایندکس ردیف (index) دیتافریم
                mask = self.all_pivot_data.index.isin(range(start, end + 1))
            self.pivot_data = self.all_pivot_data[mask].copy()
            # برای original_df هم همین کار رو بکن
            if self.all_original_df is not None and not self.all_original_df.empty:
                if 'original_index' in self.all_original_df.columns:
                    orig_mask = self.all_original_df['original_index'].between(start, end)
                else:
                    orig_mask = self.all_original_df.index.isin(range(start, end + 1))
                self.original_df = self.all_original_df[orig_mask].copy()
            else:
                self.original_df = pd.DataFrame()

    def load_params(self):
        if not hasattr(self, 'blank_edit'): # یعنی هنوز UI ساخته نشده
            return
        params = self.params.get(self.current_file_index, {})
        self.range_low = params.get('range_low', 2.0)
        self.range_mid = params.get('range_mid', 20.0)
        self.range_high1 = params.get('range_high1', 10.0)
        self.range_high2 = params.get('range_high2', 8.0)
        self.range_high3 = params.get('range_high3', 5.0)
        self.range_high4 = params.get('range_high4', 3.0)
        self.preview_blank = params.get('preview_blank', 0.0)
        self.preview_scale = params.get('preview_scale', 1.0)
        self.excluded_outliers = params.get('excluded_outliers', self.excluded_outliers.copy())
        self.excluded_from_correct = params.get('excluded_from_correct', set())
        self.scale_range_min = params.get('scale_range_min')
        self.scale_range_max = params.get('scale_range_max')
        # اعمال به ویجت‌ها
        self.blank_edit.setText(f"{self.preview_blank:.3f}")
        self.scale_slider.setValue(int(self.preview_scale * 100))
        self.scale_label.setText(f"Scale: {self.preview_scale:.2f}")
        self.scale_above_50.setChecked(bool(params.get('scale_above_50', False)))
        self.scale_range_min_edit.setText("" if self.scale_range_min is None else str(self.scale_range_min))
        self.scale_range_max_edit.setText("" if self.scale_range_max is None else str(self.scale_range_max))

    def save_params(self):
        if not hasattr(self, 'scale_above_50'): # یعنی هنوز UI ساخته نشده
            return
        if self.current_file_index not in self.params:
            return
        self.params[self.current_file_index].update({
            'range_low': self.range_low,
            'range_mid': self.range_mid,
            'range_high1': self.range_high1,
            'range_high2': self.range_high2,
            'range_high3': self.range_high3,
            'range_high4': self.range_high4,
            'preview_blank': self.preview_blank,
            'preview_scale': self.preview_scale,
            'excluded_outliers': self.excluded_outliers,
            'excluded_from_correct': self.excluded_from_correct,
            'scale_above_50': self.scale_above_50.isChecked(),
            'scale_range_min': self.scale_range_min,
            'scale_range_max': self.scale_range_max,
        })

    def update_navigation_buttons(self):
        """Update the enabled state of navigation buttons."""
        self.prev_btn.setEnabled(self.current_element_index > 0)
        self.next_btn.setEnabled(self.current_element_index < len(self.elements) - 1)

    def update_calibration_range(self):
        # اگر هنوز UI ساخته نشده، هیچ کاری نکن
        if not hasattr(self, 'calibration_display'):
            return
        # بقیه کد مثل قبل...
        if self.original_df is not None and not self.original_df.empty:
            concentration_column = get_concentration_column(self.original_df)
            if concentration_column:
                element_name = self.selected_element[:-2] if len(self.selected_element) >= 2 and self.selected_element[-2] == '_' else self.selected_element
                std_data = self.original_df[
                    (self.original_df['Type'] == 'Std') &
                    (self.original_df['Element'] == element_name)
                ][concentration_column]
                std_data_numeric = [float(x) for x in std_data if is_numeric(x)]
                if std_data_numeric:
                    calibration_min = min(std_data_numeric)
                    calibration_max = max(std_data_numeric)
                    self.calibration_range = f"[{format_number(calibration_min)} to {format_number(calibration_max)}]"
                else:
                    self.calibration_range = "[0 to 0]"
            else:
                self.calibration_range = "[0 to 0]"
        else:
            self.calibration_range = "[0 to 0]"
        # حالا که مطمئنیم ویجت وجود داره
        self.calibration_display.setText(f"Calibration: {self.calibration_range}")

    def prev_element(self):
        """Navigate to the previous element."""
        if self.current_element_index > 0:
            self.save_params()
            self.current_element_index -= 1
            self.element_combo.setCurrentIndex(self.current_element_index)
            self.load_params()
            self.update_calibration_range()
            self.update_plot()

    def next_element(self):
        """Navigate to the next element."""
        if self.current_element_index < len(self.elements) - 1:
            self.save_params()
            self.current_element_index += 1
            self.element_combo.setCurrentIndex(self.current_element_index)
            self.load_params()
            self.update_calibration_range()
            self.update_plot()

    def open_range_dialog(self):
        """Open dialog to set acceptable ranges."""
        dialog = QDialog(self)
        dialog.setWindowFlags(Qt.WindowType.Dialog | Qt.WindowType.WindowCloseButtonHint)
        dialog.setStyleSheet(common_styles)
        dialog.setWindowTitle("تنظیم بازه‌های مجاز")
        dialog.setGeometry(200, 200, 400, 300)
        layout = QGridLayout(dialog)
        layout.setSpacing(5)
      
        layout.addWidget(QLabel("|x| <10 (±):"), 0, 0)
        self.range_low_edit = QLineEdit(str(self.range_low))
        self.range_low_edit.setFixedWidth(40)
        layout.addWidget(self.range_low_edit, 0, 1)
      
        layout.addWidget(QLabel("10<=|x|<100 (%):"), 0, 2)
        self.range_mid_edit = QLineEdit(str(self.range_mid))
        self.range_mid_edit.setFixedWidth(40)
        layout.addWidget(self.range_mid_edit, 0, 3)
      
        layout.addWidget(QLabel("100<=|x|<1000 (%):"), 1, 0)
        self.range_high1_edit = QLineEdit(str(self.range_high1))
        self.range_high1_edit.setFixedWidth(40)
        layout.addWidget(self.range_high1_edit, 1, 1)
      
        layout.addWidget(QLabel("1000<=|x|<10000 (%):"), 1, 2)
        self.range_high2_edit = QLineEdit(str(self.range_high2))
        self.range_high2_edit.setFixedWidth(40)
        layout.addWidget(self.range_high2_edit, 1, 3)
      
        layout.addWidget(QLabel("10000<=|x|<100000 (%):"), 2, 0)
        self.range_high3_edit = QLineEdit(str(self.range_high3))
        self.range_high3_edit.setFixedWidth(40)
        layout.addWidget(self.range_high3_edit, 2, 1)
      
        layout.addWidget(QLabel("|x|>=100000 (%):"), 2, 2)
        self.range_high4_edit = QLineEdit(str(self.range_high4))
        self.range_high4_edit.setFixedWidth(40)
        layout.addWidget(self.range_high4_edit, 2, 3)
      
        button_layout = QHBoxLayout()
        ok_btn = QPushButton("OK")
        ok_btn.clicked.connect(lambda: self.apply_ranges(dialog))
        button_layout.addWidget(ok_btn)
      
        cancel_btn = QPushButton("Cancel")
        cancel_btn.clicked.connect(dialog.reject)
        button_layout.addWidget(cancel_btn)
      
        layout.addLayout(button_layout, 3, 0, 1, 4)
      
        dialog.exec()

    def apply_ranges(self, dialog):
        """Apply the ranges from the dialog."""
        try:
            self.range_low = float(self.range_low_edit.text())
            self.range_mid = float(self.range_mid_edit.text())
            self.range_high1 = float(self.range_high1_edit.text())
            self.range_high2 = float(self.range_high2_edit.text())
            self.range_high3 = float(self.range_high3_edit.text())
            self.range_high4 = float(self.range_high4_edit.text())
            dialog.accept()
            self.update_plot()
        except ValueError:
            QMessageBox.warning(self, "Error", "Invalid range values. Please enter numbers.")

    def update_scale_range(self):
        """Update the scale application range based on user input."""
        try:
            min_val = self.scale_range_min_edit.text().strip()
            max_val = self.scale_range_max_edit.text().strip()
            self.scale_range_min = float(min_val) if min_val else None
            self.scale_range_max = float(max_val) if max_val else None
            if self.scale_range_min is not None and self.scale_range_max is not None and self.scale_range_min > self.scale_range_max:
                self.scale_range_min, self.scale_range_max = self.scale_range_max, self.scale_range_min
                self.scale_range_min_edit.setText(str(self.scale_range_min))
                self.scale_range_max_edit.setText(str(self.scale_range_max))
            range_text = f"Scale Range: [{format_number(self.scale_range_min)} to {format_number(self.scale_range_max)}]" if self.scale_range_min is not None and self.scale_range_max is not None else "Scale Range: Not Set"
            self.scale_range_display.setText(range_text)
            self.update_plot()
        except ValueError:
            self.scale_range_min = None
            self.scale_range_max = None
            self.scale_range_display.setText("Scale Range: Not Set")
            self.update_plot()

    def update_preview_params(self):
        """Update preview parameters based on user input."""
        try:
            self.preview_blank = float(self.blank_edit.text())
        except ValueError:
            self.preview_blank = 0.0
      
        self.preview_scale = self.scale_slider.value() / 100.0
        self.scale_label.setText(f"Scale: {self.preview_scale:.2f}")
      
        self.update_plot()

    def reset_blank_and_scale(self):
        """Reset blank and scale to default values."""
        self.preview_blank = 0.0
        self.blank_edit.setText("0.0")
        self.preview_scale = 1.0
        self.scale_slider.setValue(100)
        self.scale_label.setText(f"Scale: {self.preview_scale:.2f}")
        self.update_plot()

    def calculate_dynamic_range(self, value):
        """Calculate the dynamic range for a given value."""
        try:
            value = float(value)
            abs_value = abs(value)
            if abs_value < 10:
                return self.range_low
            elif 10 <= abs_value < 100:
                return abs_value * (self.range_mid / 100)
            elif 100 <= abs_value < 1000:
                return abs_value * (self.range_high1 / 100)
            elif 1000 <= abs_value < 10000:
                return abs_value * (self.range_high2 / 100)
            elif 10000 <= abs_value < 100000:
                return abs_value * (self.range_high3 / 100)
            else:
                return abs_value * (self.range_high4 / 100)
        except (ValueError, TypeError):
            return 0

    def show_tooltip(self, pos):
        """Show tooltip on mouse hover."""
        plot = self.main_plot.getPlotItem()
        if not plot:
            return
        vb = plot.getViewBox()
        mouse_point = vb.mapSceneToView(pos)
        for item in plot.items[:]:
            if isinstance(item, pg.TextItem):
                plot.removeItem(item)
        for item in plot.listDataItems():
            x, y = item.getData()
            for i in range(len(x)):
                if abs(vb.mapViewToScene(pg.Point(x[i], y[i])).x() - pos.x()) < 20 and abs(vb.mapViewToScene(pg.Point(x[i], y[i])).y() - pos.y()) < 20:
                    text = pg.TextItem(f"{item.name()}: {format_number(y[i])}", anchor=(0, 0))
                    text.setPos(x[i], y[i])
                    plot.addItem(text)
                    return

    def show_secondary_tooltip(self, pos):
        """Show tooltip on mouse hover for secondary plot."""
        plot = self.secondary_plot.getPlotItem()
        if not plot:
            return
        vb = plot.getViewBox()
        mouse_point = vb.mapSceneToView(pos)
        for item in plot.items[:]:
            if isinstance(item, pg.TextItem):
                plot.removeItem(item)
        for item in plot.listDataItems():
            x, y = item.getData()
            for i in range(len(x)):
                if abs(vb.mapViewToScene(pg.Point(x[i], y[i])).x() - pos.x()) < 20 and abs(vb.mapViewToScene(pg.Point(x[i], y[i])).y() - pos.y()) < 20:
                    solution_label = item.opts['data'][i] if 'data' in item.opts else "Unknown"
                    text = pg.TextItem(f"Solution Label: {solution_label}\nValue: {format_number(y[i])}", anchor=(0, 0))
                    text.setPos(x[i], y[i])
                    plot.addItem(text)
                    return

    def show_report(self):
        """Show the report dialog."""
        try:
            from ..report_dialog import ReportDialog
            self.logger.debug(f"Opening report with {len(self.annotations)} annotations")
            dialog = ReportDialog(self, self.annotations)
            result = dialog.exec()
            if result == QDialog.DialogCode.Accepted:
                self.logger.debug("Report dialog accepted")
            else:
                self.logger.debug("Report dialog closed without applying corrections")
        except Exception as e:
            self.logger.error(f"Error opening ReportDialog: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to open report: {str(e)}")

    def apply_model(self):
        """Apply the model corrections."""
        try:
            from ..report_dialog import ReportDialog
            dialog = ReportDialog(self, self.annotations)
            recommended_blank, recommended_scale = dialog.get_correction_parameters()
            self.blank_edit.setText(f"{recommended_blank:.3f}")
            self.scale_slider.setValue(int(recommended_scale * 100))
            self.update_preview_params()
        except Exception as e:
            self.logger.error(f"Error applying model: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to apply model: {str(e)}")
       
    def correct_crm_callback(self):
        # """Apply blank + scale correction to the selected element and instantly refresh the Results tab."""
        # try:
            if self.pivot_data is None or self.pivot_data.empty:
                QMessageBox.warning(self, "Error", "No data available to correct!")
                return
            column_to_correct = self.selected_element
            if column_to_correct not in self.pivot_data.columns:
                QMessageBox.warning(self, "Error", f"Column {column_to_correct} not found!")
                return
            # Backup current column for Undo functionality
            self.parent.backup_column(column_to_correct)
            corrected_count = 0
            # Apply correction to current view (pivot_data)
            for index, row in self.pivot_data.iterrows():
                solution_label = row['Solution Label']
                current_val = row[column_to_correct]
                if pd.notna(current_val) and is_numeric(current_val):
                    print("omid : ",current_val)
                    val = float(current_val)
                    # Check if this sample should be corrected
                    if (solution_label not in self.excluded_from_correct and
                        (self.scale_range_min is None or self.scale_range_max is None or
                        self.scale_range_min <= val <= self.scale_range_max) and
                        (not self.scale_above_50.isChecked() or val > 50)):
                        new_val = (val - self.preview_blank) * self.preview_scale
                        self.pivot_data.at[index, column_to_correct] = new_val
                        corrected_count += 1
                        # Save correction info for reporting
                        if not hasattr(self.parent.app.crm_check, 'corrected_crm'):
                            self.parent.app.crm_check.corrected_crm = {}
                        if self.selected_element not in self.parent.app.crm_check.corrected_crm:
                            self.parent.app.crm_check.corrected_crm[self.selected_element] = {}
                        self.parent.app.crm_check.corrected_crm[self.selected_element][solution_label] = {
                            'blank': self.preview_blank,
                            'scale': self.preview_scale
                        }
            # Update the global pivot data (all files or current file only)
            if self.current_file_index != -1: # Specific file
                self.all_pivot_data.loc[self.pivot_data.index, column_to_correct] = self.pivot_data[column_to_correct]
            # === Update the original long-format DataFrame (critical!) ===
            if self.original_df is not None and not self.original_df.empty:
                conc_col = self.get_concentration_column(self.original_df)
                if pd.notna(conc_col) and is_numeric(conc_col):
                    mask = self.original_df['Element'] == self.selected_element
                    self.original_df.loc[mask, conc_col] = self.original_df[mask].apply(
                        lambda r: (
                            (float(r[conc_col]) - self.preview_blank) * self.preview_scale
                            if (r['Solution Label'] not in self.excluded_from_correct and
                                is_numeric(r[conc_col]) and
                                (self.scale_range_min is None or self.scale_range_max is None or
                                self.scale_range_min <= float(r[conc_col]) <= self.scale_range_max) and
                                (not self.scale_above_50.isChecked() or float(r[conc_col]) > 50))
                            else float(r[conc_col])
                        ), axis=1
                    )
                    # Sync back to the master DataFrame
                    if self.current_file_index != -1:
                        self.all_original_df.loc[self.original_df.index, conc_col] = self.original_df[conc_col]
                    final_original_df = self.all_original_df if self.current_file_index != -1 else self.original_df
                    self.parent.app.set_data(final_original_df, for_results=True)
            # Prepare final pivot DataFrame for Results tab
            new_df = self.all_pivot_data.copy() if self.current_file_index != -1 else self.pivot_data.copy()
            self.parent.results_frame.last_filtered_data = new_df
            # === INSTANTLY REFRESH THE RESULTS TAB (THIS IS THE KEY!) ===
            if hasattr(self.parent.app, 'results') and self.parent.app.results:
                results_tab = self.parent.app.results
                results_tab.last_filtered_data = new_df
                results_tab.show_processed_data() # Forces full refresh — works 100%
            # Optional: refresh pivot display in CRM tab if needed
            if hasattr(self.parent, 'update_pivot_display'):
                self.parent.update_pivot_display()
            # Reset preview controls
            self.preview_blank = 0.0
            self.blank_edit.setText("0.0")
            self.preview_scale = 1.0
            self.scale_slider.setValue(100)
            self.scale_label.setText("Scale: 1.00")
            self.scale_range_min = None
            self.scale_range_max = None
            self.scale_range_min_edit.setText("")
            self.scale_range_max_edit.setText("")
            self.scale_range_display.setText("Scale Range: Not Set")
            # Refresh plot
            self.update_plot()
            # Success message
            range_text = (f"[{format_number(self.scale_range_min)} to {format_number(self.scale_range_max)}]"
                        if self.scale_range_min is not None and self.scale_range_max is not None else "All values")
            QMessageBox.information(
                self, "Success",
                f"Correction applied successfully!\n\n"
                f"Corrected samples: {corrected_count}\n"
                f"Blank: {format_number(self.preview_blank)}\n"
                f"Scale: {self.preview_scale:.4f}\n"
                f"Applied range: {range_text}\n"
                f"Only >50: {'Yes' if self.scale_above_50.isChecked() else 'No'}\n\n"
                f"Results tab has been updated instantly."
            )
        # except Exception as e:
        #     QMessageBox.critical(self, "Error", f"Failed to apply correction:\n{str(e)}")
          
    def undo_correction(self):
        """Undo the last correction on the current column."""
        column = self.selected_element
        self.parent.restore_column(column)
        self.preview_blank = 0.0
        self.blank_edit.setText("0.0")
        self.preview_scale = 1.0
        self.scale_slider.setValue(100)
        self.update_plot()

    def open_exclude_window(self):
        """Open window to exclude Solution Labels from correction."""
        w = QDialog(self)
        w.setWindowFlags(Qt.WindowType.Dialog | Qt.WindowType.WindowCloseButtonHint)
        w.setStyleSheet(common_styles)
        w.setWindowTitle("Exclude from Correct")
        w.setGeometry(200, 200, 400, 400)
        layout = QVBoxLayout(w)
        tree_view = QTreeView()
        model = QStandardItemModel()
        model.setHorizontalHeaderLabels(["Solution Label", "Value", "Exclude"])
        tree_view.setModel(model)
        tree_view.setRootIsDecorated(False)
        tree_view.header().resizeSection(0, 160)
        tree_view.header().resizeSection(1, 80)
        tree_view.header().resizeSection(2, 80)
        for label in sorted(self.pivot_data['Solution Label']):
            match = self.pivot_data[self.pivot_data['Solution Label'] == label]
            value = match[self.selected_element].iloc[0] if not match.empty else 'N/A'
            label_item = QStandardItem(label)
            value_item = QStandardItem(str(value))
            check_item = QStandardItem()
            check_item.setCheckable(True)
            check_item.setCheckState(Qt.CheckState.Checked if label in self.excluded_from_correct else Qt.CheckState.Unchecked)
            model.appendRow([label_item, value_item, check_item])
        tree_view.clicked.connect(lambda index: self.toggle_exclude_check(index, model))
        layout.addWidget(tree_view)
        close_btn = QPushButton("Close")
        close_btn.clicked.connect(w.accept)
        layout.addWidget(close_btn)
        w.exec()

    def toggle_exclude_check(self, index, model):
        """Toggle exclusion of a solution label."""
        if index.column() != 2:
            return
        label = model.item(index.row(), 0).text()
        if model.item(index.row(), 2).checkState() == Qt.CheckState.Checked:
            self.excluded_from_correct.add(label)
        else:
            self.excluded_from_correct.discard(label)
        self.update_plot()

    def update_plot(self):
        """Update the plot based on current settings."""
        if not self.selected_element or self.selected_element not in self.pivot_data.columns:
            self.logger.warning(f"Element '{self.selected_element}' not found in pivot data!")
            QMessageBox.warning(self, "Warning", f"Element '{self.selected_element}' not found!")
            return
        try:
            self.main_plot.clear()
            self.secondary_plot.clear()
            self.annotations = []
            def extract_crm_id(label):
                m = re.search(r'(?i)(?:\bCRM\b|\bOREAS\b)?[\s-]*(\d+[a-zA-Z]?)[\s-]*(?:\bpar\b)?', str(label))
                return m.group(1) if m else str(label)
            concentration_column = get_concentration_column(self.original_df) if self.original_df is not None else None
            if self.original_df is not None and not self.original_df.empty and concentration_column:
                sample_data = self.original_df[
                    (self.original_df['Type'].isin(['Samp', 'Sample'])) &
                    (self.original_df['Element'] == self.selected_element)
                ][concentration_column]
                sample_data_numeric = [float(x) for x in sample_data if is_numeric(x)]
                if not sample_data_numeric:
                    soln_conc_min = '---'
                    soln_conc_max = '---'
                    soln_conc_range = '---'
                    in_calibration_range_soln = False
                else:
                    soln_conc_min = min(sample_data_numeric)
                    soln_conc_max = max(sample_data_numeric)
                    soln_conc_range = f"[{format_number(soln_conc_min)} to {format_number(soln_conc_max)}]"
                    in_calibration_range_soln = (
                        float(self.calibration_range.split(' to ')[0][1:]) <= soln_conc_min <= float(self.calibration_range.split(' to ')[1][:-1]) and
                        float(self.calibration_range.split(' to ')[0][1:]) <= soln_conc_max <= float(self.calibration_range.split(' to ')[1][:-1])
                    ) if self.calibration_range != "[0 to 0]" else False
            else:
                soln_conc_min = '---'
                soln_conc_max = '---'
                soln_conc_range = '---'
                in_calibration_range_soln = False
            blank_rows = self.pivot_data[
                self.pivot_data['Solution Label'].str.contains(BLANK_PATTERN, case=False, na=False, regex=True)
            ]
            blank_val = 0
            blank_correction_status = "Not Applied"
            selected_blank_label = "None"
            self.blank_labels = []
            if not blank_rows.empty:
                best_blank_val = 0
                best_blank_label = "None"
                min_distance = float('inf')
                in_range_found = False
                for _, row in blank_rows.iterrows():
                    candidate_blank = row[self.selected_element] if pd.notna(row[self.selected_element]) else 0
                    candidate_label = row['Solution Label']
                    if not is_numeric(candidate_blank):
                        continue
                    candidate_blank = float(candidate_blank)
                    self.blank_labels.append(f"{candidate_label}: {format_number(candidate_blank)}")
                    in_range = False
                    for sol_label in self.parent._inline_crm_rows_display.keys():
                        if sol_label in blank_rows['Solution Label'].values:
                            continue
                        pivot_row = self.pivot_data[self.pivot_data['Solution Label'] == sol_label]
                        if pivot_row.empty:
                            continue
                        pivot_val = pivot_row.iloc[0][self.selected_element]
                        if not is_numeric(pivot_val):
                            continue
                        pivot_val_float = float(pivot_val)
                        for row_data, _ in self.parent._inline_crm_rows_display[sol_label]:
                            if isinstance(row_data, list) and row_data and row_data[0].endswith("CRM"):
                                val = row_data[self.pivot_data.columns.get_loc(self.selected_element)] if self.selected_element in self.pivot_data.columns else ""
                                if is_numeric(val):
                                    crm_val = float(val)
                                    range_val = self.calculate_dynamic_range(crm_val)
                                    lower, upper = crm_val - range_val, crm_val + range_val
                                    corrected_pivot = pivot_val_float - candidate_blank
                                    if lower <= corrected_pivot <= upper:
                                        in_range = True
                                        break
                        if in_range:
                            break
                    if in_range:
                        best_blank_val = candidate_blank
                        best_blank_label = candidate_label
                        in_range_found = True
                        break
                if not in_range_found:
                    for sol_label in self.parent._inline_crm_rows_display.keys():
                        if sol_label in blank_rows['Solution Label'].values:
                            continue
                        pivot_row = self.pivot_data[self.pivot_data['Solution Label'] == sol_label]
                        if pivot_row.empty:
                            continue
                        pivot_val = pivot_row.iloc[0][self.selected_element]
                        if not self.is_numeric(pivot_val):
                            continue
                        pivot_val_float = float(pivot_val)
                        for row_data, _ in self.parent._inline_crm_rows_display[sol_label]:
                            if isinstance(row_data, list) and row_data and row_data[0].endswith("CRM"):
                                val = row_data[self.pivot_data.columns.get_loc(self.selected_element)] if self.selected_element in self.pivot_data.columns else ""
                                if not is_numeric(val):
                                    continue
                                crm_val = float(val)
                                corrected_pivot = pivot_val_float - candidate_blank
                                distance = abs(corrected_pivot - crm_val)
                                if distance < min_distance:
                                    min_distance = distance
                                    best_blank_val = candidate_blank
                                    best_blank_label = candidate_label
                blank_val = best_blank_val
                selected_blank_label = best_blank_label
                blank_correction_status = "Applied" if blank_val != 0 else "Not Applied"
            self.blank_display.setText("Blanks:\n" + "\n".join(self.blank_labels) if self.blank_labels else "Blanks: None")
            crm_labels = [
                label for label in self.parent._inline_crm_rows_display.keys()
                if label not in blank_rows['Solution Label'].values
                and label in self.parent.included_crms and self.parent.included_crms[label].isChecked()
            ]
            crm_id_to_labels = {}
            for sol_label in crm_labels:
                crm_id = extract_crm_id(sol_label)
                if crm_id not in crm_id_to_labels:
                    crm_id_to_labels[crm_id] = []
                crm_id_to_labels[crm_id].append(sol_label)
            unique_crm_ids = sorted(crm_id_to_labels.keys())
            x_pos_map = {crm_id: i for i, crm_id in enumerate(unique_crm_ids)}
            certificate_values = {}
            sample_values = {}
            outlier_values = {}
            lower_bounds = {}
            upper_bounds = {}
            soln_concs = {}
            int_values = {}
            element_name = self.selected_element.split()[0]
            wavelength = ' '.join(self.selected_element.split()[1:]) if len(self.selected_element.split()) > 1 else ""
            analysis_date = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            for crm_id in unique_crm_ids:
                certificate_values[crm_id] = []
                sample_values[crm_id] = []
                outlier_values[crm_id] = []
                lower_bounds[crm_id] = []
                upper_bounds[crm_id] = []
                soln_concs[crm_id] = []
                int_values[crm_id] = []
                for sol_label in crm_id_to_labels[crm_id]:
                    pivot_row = self.pivot_data[self.pivot_data['Solution Label'] == sol_label]
                    if pivot_row.empty:
                        continue
                    pivot_val = pivot_row.iloc[0][self.selected_element]
                    if pd.isna(pivot_val) or not is_numeric(pivot_val):
                        pivot_val = 0
                    else:
                        pivot_val = float(pivot_val)
                    if self.original_df is not None and not self.original_df.empty and concentration_column:
                        sample_rows = self.original_df[
                            (self.original_df['Solution Label'] == sol_label) &
                            (self.original_df['Element'].str.startswith(element_name)) &
                            (self.original_df['Type'].isin(['Samp', 'Sample']))
                        ]
                        soln_conc = sample_rows[concentration_column].iloc[0] if not sample_rows.empty else '---'
                        int_val = sample_rows['Int'].iloc[0] if not sample_rows.empty else '---'
                    else:
                        soln_conc = '---'
                        int_val = '---'
                    for row_data, _ in self.parent._inline_crm_rows_display[sol_label]:
                        if isinstance(row_data, list) and row_data and row_data[0].endswith("CRM"):
                            val = row_data[self.pivot_data.columns.get_loc(self.selected_element)] if self.selected_element in self.pivot_data.columns else ""
                            if not val or not is_numeric(val):
                                if sol_label not in self.excluded_outliers.get(self.selected_element, set()):
                                    annotation = f"Verification ID: {crm_id} (Label: {sol_label})\n - Certificate Value: {val or 'N/A'}\n - Sample Value: {format_number(pivot_val)}\n - Acceptable Range: [N/A]\n - Status: Out of range (non-numeric data).\n - Blank Value: {format_number(blank_val)}\n - Blank Label: {selected_blank_label}\n - Blank Correction Status: {blank_correction_status}\n - Sample Value - Blank: {format_number(pivot_val)}\n - Corrected Range: [N/A]\n - Status after Blank Subtraction: Out of range (non-numeric data).\n - Soln Conc: {soln_conc if isinstance(soln_conc, str) else format_number(soln_conc)} {'in_range' if in_calibration_range_soln else 'out_range'}\n - Int: {int_val if isinstance(int_val, str) else format_number(int_val)}\n - Calibration Range: {self.calibration_range} {'in_range' if in_calibration_range_soln else 'out_range'}\n - CRM Source: NIST\n - Sample Matrix: Soil\n - Element Wavelength: {wavelength}\n - Analysis Date: {analysis_date}"
                                    self.annotations.append(annotation)
                                continue
                            crm_val = float(val)
                            pivot_val_float = float(pivot_val)
                            corrected_val = pivot_val_float
                            if (sol_label not in self.excluded_from_correct and
                                is_numeric(pivot_val) and
                                (self.scale_range_min is None or self.scale_range_max is None or
                                 self.scale_range_min <= float(pivot_val) <= self.scale_range_max) and
                                (not self.scale_above_50.isChecked() or float(pivot_val) > 50)):
                                corrected_val = (pivot_val_float - self.preview_blank) * self.preview_scale
                            range_val = self.calculate_dynamic_range(crm_val)
                            lower = crm_val - range_val
                            upper = crm_val + range_val
                            in_range = lower <= corrected_val <= upper
                            if sol_label not in self.excluded_outliers.get(self.selected_element, set()):
                                annotation = f"Verification ID: {crm_id} (Label: {sol_label})\n - Certificate Value: {format_number(crm_val)}\n - Sample Value: {format_number(pivot_val_float)}\n - Acceptable Range: [{format_number(lower)} to {format_number(upper)}]"
                                if in_range:
                                    annotation += f"\n - Status: In range (no adjustment needed)."
                                    annotation += f"\n - Blank Value: {format_number(blank_val)}\n - Blank Label: {selected_blank_label}\n - Blank Correction Status: Not Applied (in range)\n - Sample Value - Blank: {format_number(corrected_val)}\n - Corrected Range: [{format_number(lower)} to {format_number(upper)}]\n - Status after Blank Subtraction: In range."
                                else:
                                    annotation += f"\n - Status: Out of range without adjustment."
                                    annotation += f"\n - Blank Value: {format_number(blank_val)}\n - Blank Label: {selected_blank_label}\n - Blank Correction Status: {blank_correction_status}\n - Sample Value - Blank: {format_number(corrected_val)}\n - Corrected Range: [{format_number(lower)} to {format_number(upper)}]"
                                    corrected_in_range = lower <= corrected_val <= upper
                                    if corrected_in_range:
                                        annotation += f"\n - Status after Blank Subtraction: In range."
                                    else:
                                        annotation += f"\n - Status after Blank Subtraction: Out of range."
                                        if corrected_val != 0:
                                            if corrected_val < lower:
                                                scale_factor = lower / corrected_val
                                                direction = "increase"
                                            elif corrected_val > upper:
                                                scale_factor = upper / corrected_val
                                                direction = "decrease"
                                            else:
                                                scale_factor = 1.0
                                                direction = ""
                                            scale_percent = abs((scale_factor - 1) * 100)
                                            annotation += f"\n - Required Scaling: {scale_percent:.2f}% {direction} to fit within range."
                                            if scale_percent > 32:
                                                annotation += f"\n - Warning: Scaling exceeds 32% ({scale_percent:.2f}%)."
                                        else:
                                            annotation += f"\n - Scaling not applicable (corrected sample value is zero)."
                                annotation += f"\n - Soln Conc: {soln_conc if isinstance(soln_conc, str) else format_number(soln_conc)} {'in_range' if in_calibration_range_soln else 'out_range'}\n - Int: {int_val if isinstance(int_val, str) else format_number(int_val)}\n - Calibration Range: {self.calibration_range} {'in_range' if in_calibration_range_soln else 'out_range'}\n - CRM Source: NIST\n - Sample Matrix: Soil\n - Element Wavelength: {wavelength}\n - Analysis Date: {analysis_date}"
                                self.annotations.append(annotation)
                          
                            certificate_values[crm_id].append(crm_val)
                            if sol_label in self.excluded_outliers.get(self.selected_element, set()):
                                outlier_values[crm_id].append(corrected_val)
                            else:
                                sample_values[crm_id].append(corrected_val)
                            lower_bounds[crm_id].append(lower)
                            upper_bounds[crm_id].append(upper)
                            soln_concs[crm_id].append(soln_conc)
                            int_values[crm_id].append(int_val)
            if not unique_crm_ids:
                self.main_plot.clear()
                self.logger.warning(f"No valid Verification data for {self.selected_element}")
                QMessageBox.warning(self, "Warning", f"No valid Verification data for {self.selected_element}")
                return
            self.main_plot.setLabel('bottom', 'Verification ID')
            self.main_plot.setLabel('left', f'{self.selected_element} Value')
            self.main_plot.setTitle(f'Verification Values for {self.selected_element}')
            self.main_plot.getAxis('bottom').setTicks([[(i, f'V {id}') for i, id in enumerate(unique_crm_ids)]])
            all_y_values = []
            for crm_id in unique_crm_ids:
                all_y_values.extend(certificate_values.get(crm_id, []))
                all_y_values.extend(sample_values.get(crm_id, []))
                all_y_values.extend(outlier_values.get(crm_id, []))
                all_y_values.extend(lower_bounds.get(crm_id, []))
                all_y_values.extend(upper_bounds.get(crm_id, []))
            if all_y_values:
                y_min, y_max = min(all_y_values), max(all_y_values)
                margin = (y_max - y_min) * 0.1
                self.main_plot.setXRange(-0.5, len(unique_crm_ids) - 0.5)
                self.main_plot.setYRange(y_min - margin, y_max + margin)
            if self.show_check_crm.isChecked():
                for crm_id in unique_crm_ids:
                    x_pos = x_pos_map[crm_id]
                    cert_vals = certificate_values.get(crm_id, [])
                    if cert_vals:
                        x_vals = [x_pos] * len(cert_vals)
                        scatter = pg.PlotDataItem(
                            x=x_vals, y=cert_vals, pen=None, symbol='o', symbolSize=8,
                            symbolPen='g', symbolBrush='g', name='Certificate Value'
                        )
                        self.main_plot.addItem(scatter)
            if self.show_pivot_crm.isChecked():
                for crm_id in unique_crm_ids:
                    x_pos = x_pos_map[crm_id]
                    for idx, sol_label in enumerate(crm_id_to_labels[crm_id]):
                        samp_vals = sample_values.get(crm_id, [])
                        if idx < len(samp_vals):
                            scatter = pg.PlotDataItem(
                                x=[x_pos], y=[samp_vals[idx]], pen=None, symbol='t', symbolSize=8,
                                symbolPen='b', symbolBrush='b', name=sol_label
                            )
                            self.main_plot.addItem(scatter)
                        outlier_vals = outlier_values.get(crm_id, [])
                        if idx < len(outlier_vals):
                            scatter = pg.PlotDataItem(
                                x=[x_pos], y=[outlier_vals[idx]], pen=None, symbol='t', symbolSize=8,
                                symbolPen='#FFA500', symbolBrush='#FFA500', name=f"{sol_label} (Outlier)"
                            )
                            self.main_plot.addItem(scatter)
            if self.show_range.isChecked():
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
                            self.main_plot.addItem(line_lower)
                            self.main_plot.addItem(line_upper)
            self.main_plot.showGrid(x=True, y=True, alpha=0.3)
            
            # Secondary plot - فقط اینجا فیلتر اعمال می‌شود
            filter_text = self.filter_entry.text().strip().lower()
            if 'pivot_index' not in self.pivot_data.columns:
                self.pivot_data['pivot_index'] = self.pivot_data.index
            filtered_data = self.pivot_data.copy()
            if filter_text:
                filtered_data = filtered_data[filtered_data['Solution Label'].str.lower().str.contains(filter_text, na=False)]
            x_sec = filtered_data['pivot_index'].values
            y_sec = pd.to_numeric(filtered_data[self.selected_element], errors='coerce').fillna(0).values
            y_corrected = (y_sec - self.preview_blank) * self.preview_scale
            orig_scatter = pg.ScatterPlotItem(
                x=x_sec, y=y_sec,
                symbol='o', size=8, brush='#2196F3', pen='w',
                hoverable=True,
                tip=None,  # استفاده از tip داخلی pyqtgraph برای tooltip ساده
                data=filtered_data['Solution Label'].values
            )
            corr_scatter = pg.ScatterPlotItem(
                x=x_sec, y=y_corrected,
                symbol='x', size=10, brush='#F44336', pen='w',
                hoverable=True,
                data=filtered_data['Solution Label'].values
            )
            self.secondary_plot.addItem(orig_scatter)
            self.secondary_plot.addItem(corr_scatter)
            self.secondary_plot.showGrid(x=True, y=True, alpha=0.3)
            if len(x_sec) > 0:
                self.secondary_plot.setXRange(min(x_sec) - 1, max(x_sec) + 1)
            self.secondary_plot.autoRange()

        except Exception as e:
            self.main_plot.clear()
            self.secondary_plot.clear()
            self.logger.error(f"Failed to update plot: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to update plot: {str(e)}")

    def handle_click(self, event):
        """Handle mouse clicks on the plot."""
        if event.button() == Qt.MouseButton.LeftButton:
            pos = event.scenePos()
            vb = self.main_plot.getViewBox()
            mouse_point = vb.mapSceneToView(pos)
            mx, my = mouse_point.x(), mouse_point.y()
            crm_labels = [
                label for label in self.parent._inline_crm_rows_display.keys()
                if label not in self.pivot_data[
                    self.pivot_data['Solution Label'].str.contains(r'CRM\s*BLANK', case=False, na=False, regex=True)
                ]['Solution Label'].values
                and label in self.parent.included_crms and self.parent.included_crms[label].isChecked()
            ]
            crm_id_to_labels = {}
            for sol_label in crm_labels:
                crm_id = self.extract_crm_id(sol_label)
                if crm_id not in crm_id_to_labels:
                    crm_id_to_labels[crm_id] = []
                crm_id_to_labels[crm_id].append(sol_label)
            unique_crm_ids = sorted(crm_id_to_labels.keys())
            x_pos_map = {crm_id: i for i, crm_id in enumerate(unique_crm_ids)}
            min_dist = float('inf')
            selected_label = None
            for crm_id in unique_crm_ids:
                x_pos = x_pos_map[crm_id]
                if abs(mx - x_pos) > 0.5:
                    continue
                pivot_row = self.pivot_data[self.pivot_data['Solution Label'].isin(crm_id_to_labels[crm_id])]
                for idx, sol_label in enumerate(crm_id_to_labels[crm_id]):
                    pivot_val = pivot_row[pivot_row['Solution Label'] == sol_label].iloc[0][self.selected_element]
                    if pd.isna(pivot_val) or not is_numeric(pivot_val):
                        continue
                    pivot_val_float = float(pivot_val)
                    samp_val = pivot_val_float
                    if (self.scale_range_min is None or self.scale_range_max is None or
                        self.scale_range_min <= pivot_val_float <= self.scale_range_max) and (not self.scale_above_50.isChecked() or pivot_val_float > 50):
                        samp_val = (pivot_val_float - self.preview_blank) * self.preview_scale
                    dist = abs(my - samp_val)
                    if dist < min_dist and dist < 0.1 * (self.main_plot.getViewBox().viewRange()[1][1] - self.main_plot.getViewBox().viewRange()[1][0]):
                        min_dist = dist
                        selected_label = sol_label
            if selected_label:
                if selected_label in self.excluded_outliers.get(self.selected_element, set()):
                    self.excluded_outliers[self.selected_element].remove(selected_label)
                else:
                    if self.selected_element not in self.excluded_outliers:
                        self.excluded_outliers[self.selected_element] = set()
                    self.excluded_outliers[self.selected_element].add(selected_label)
                self.update_plot()

    def extract_crm_id(self, label):
        """Extract CRM ID from label."""
        m = re.search(r'(?i)(?:\bCRM\b|\bOREAS\b)?[\s-]*(\d+[a-zA-Z]?)[\s-]*(?:\bpar\b)?', str(label))
        return m.group(1) if m else str(label)

    def open_select_crms_window(self):
        """Open window to select verifications to include."""
        w = QDialog(self)
        w.setWindowFlags(Qt.WindowType.Dialog | Qt.WindowType.WindowCloseButtonHint)
        w.setStyleSheet(common_styles)
        w.setWindowTitle("Select Verifications to Include")
        w.setGeometry(200, 200, 300, 400)
        w.setModal(True)
        layout = QVBoxLayout(w)
        tree_view = QTreeView()
        model = QStandardItemModel()
        model.setHorizontalHeaderLabels(["Label", "Include"])
        tree_view.setModel(model)
        tree_view.setRootIsDecorated(False)
        tree_view.header().resizeSection(0, 160)
        tree_view.header().resizeSection(1, 80)
        for label in sorted(self.parent.included_crms.keys()):
            value_item = QStandardItem(label)
            check_item = QStandardItem()
            check_item.setCheckable(True)
            check_item.setCheckState(Qt.CheckState.Checked if self.parent.included_crms[label].isChecked() else Qt.CheckState.Unchecked)
            model.appendRow([value_item, check_item])
        tree_view.clicked.connect(lambda index: self.toggle_crm_check(index, model))
        layout.addWidget(tree_view)
        button_layout = QHBoxLayout()
        select_all_btn = QPushButton("Select All")
        select_all_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        select_all_btn.clicked.connect(lambda: self.set_all_crms(True, model))
        button_layout.addWidget(select_all_btn)
      
        deselect_all_btn = QPushButton("Deselect All")
        deselect_all_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        deselect_all_btn.clicked.connect(lambda: self.set_all_crms(False, model))
        button_layout.addWidget(deselect_all_btn)
      
        close_btn = QPushButton("Close")
        close_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        close_btn.clicked.connect(w.accept)
        button_layout.addWidget(close_btn)
      
        layout.addLayout(button_layout)
        w.exec()

    def toggle_crm_check(self, index, model):
        """Toggle CRM inclusion."""
        if index.column() != 1:
            return
        label = model.item(index.row(), 0).text()
        if label in self.parent.included_crms:
            self.parent.included_crms[label].setChecked(not self.parent.included_crms[label].isChecked())
            model.item(index.row(), 1).setCheckState(
                Qt.CheckState.Checked if self.parent.included_crms[label].isChecked() else Qt.CheckState.Unchecked)
            self.update_plot()

    def set_all_crms(self, value, model):
        """Set all CRMs to included or excluded."""
        for label, checkbox in self.parent.included_crms.items():
            checkbox.setChecked(value)
        model.clear()
        model.setHorizontalHeaderLabels(["Label", "Include"])
        for label in sorted(self.parent.included_crms.keys()):
            value_item = QStandardItem(label)
            check_item = QStandardItem()
            check_item.setCheckable(True)
            check_item.setCheckState(Qt.CheckState.Checked if self.parent.included_crms[label].isChecked() else Qt.CheckState.Unchecked)
            model.appendRow([value_item, check_item])
        self.update_plot()