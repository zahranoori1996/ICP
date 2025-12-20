import sys
import sqlite3
import pandas as pd
import re
import logging
from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QTableWidget, QTableWidgetItem, QHeaderView, QProgressBar, QMessageBox,
    QFileDialog, QLabel, QDialog, QComboBox, QPushButton, QListWidget, QListWidgetItem, QLineEdit, QCheckBox, QGridLayout, QFrame,QAbstractItemView,QButtonGroup
)
from PyQt6.QtCore import Qt, QThread, pyqtSignal, QTimer
from PyQt6.QtGui import QFont, QPixmap, QColor, QPalette
from pyqtgraph import PlotWidget, mkPen
from PyQt6.QtGui import QFont, QColor
from PyQt6.QtCore import Qt
import pandas as pd
import openpyxl
from openpyxl.styles import Font, PatternFill, Border, Side, Alignment
import numpy as np
from pathlib import Path
from PIL import Image
import csv
import shutil
import os
from db.db import get_db_connection,get_ver_db,resource_path
from utils.date import extract_date,validate_jalali_date
from screens.qc_tab.crm_visulation.out_of_range import OutOfRangeFilesDialog
from screens.qc_tab.crm_visulation.plot import plot_data,save_plot,on_mouse_moved
from styles.qc_crm import STYLES
from utils.utils import is_numeric,is_valid_crm_id,normalize_crm_id
from utils.var_main import CRM_DATA_PATH,CRM_BLANK_PATH,LOGO_PNG_PATH,CRM_IDS,BLANK_4AC
from persiantools.jdatetime import JalaliDate
from db.qc_queries import (
    init_settings_table, save_qc_settings, load_qc_settings,
    load_crm_data, get_verification_value, update_crm_record
)
# Setup logging with UTF-8 encoding
log_file = Path("crm_visualizer.log").resolve()
file_handler = logging.FileHandler(log_file, mode='w', encoding='utf-8')
file_handler.setLevel(logging.DEBUG)
file_handler.setFormatter(logging.Formatter('%(asctime)s - %(levelname)s - %(message)s'))
console_handler = logging.StreamHandler(sys.stdout)
console_handler.setLevel(logging.DEBUG)
console_handler.setFormatter(logging.Formatter('%(asctime)s - %(levelname)s - %(message)s'))
logger = logging.getLogger()
logger.setLevel(logging.DEBUG)
logger.handlers = []
logger.addHandler(file_handler)
logger.addHandler(console_handler)

def validate_percentage(text):
    """Validate percentage input (must be positive float)."""
    try:
        value = float(text)
        return value > 0
    except (ValueError, TypeError):
        return False

def split_element_name(element):
    """Split element name like 'Ce140' into 'Ce 140'."""
    if not isinstance(element, str):
        return element
    match = re.match(r'^([A-Za-z]+)(\d+\.?\d*)$', element.strip())
    if match:
        symbol, number = match.groups()
        return f"{symbol} {number}"
    return element


class DeviceSelectionDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Select Device")
        self.setFixedSize(300, 150)
      
        self.layout = QVBoxLayout()
        self.label = QLabel("Please select the device type for the imported file:")
        self.device_combo = QComboBox()
        self.device_combo.addItems(['mass', 'oes 4ac', 'oes fire'])
        self.button_layout = QHBoxLayout()
        self.ok_button = QPushButton("OK")
        self.cancel_button = QPushButton("Cancel")
      
        self.button_layout.addWidget(self.ok_button)
        self.button_layout.addWidget(self.cancel_button)
        self.layout.addWidget(self.label)
        self.layout.addWidget(self.device_combo)
        self.layout.addLayout(self.button_layout)
        self.setLayout(self.layout)
      
        self.ok_button.clicked.connect(self.accept)
        self.cancel_button.clicked.connect(self.reject)

    def get_device(self):
        return self.device_combo.currentText()

  
class EditRecordDialog(QDialog):
    def __init__(self, parent=None, record=None, db_path=None):
        super().__init__(parent)
        self.setWindowTitle("Edit Record")
        self.setFixedSize(400, 300)
        self.record = record
        self.db_path = db_path
      
        self.layout = QVBoxLayout()
      
        self.crm_id_label = QLabel("CRM ID:")
        self.crm_id_edit = QLineEdit()
        self.crm_id_edit.setText(str(record['crm_id']) if pd.notna(record['crm_id']) else "")
      
        self.solution_label = QLabel("Solution Label:")
        self.solution_edit = QLineEdit()
        self.solution_edit.setText(str(record['solution_label']) if pd.notna(record['solution_label']) else "")
      
        self.element_label = QLabel("Element:")
        self.element_edit = QLineEdit()
        self.element_edit.setText(str(record['element']) if pd.notna(record['element']) else "")
      
        self.value_label = QLabel("Value:")
        self.value_edit = QLineEdit()
        self.value_edit.setText(f"{record['value']:.2f}" if pd.notna(record['value']) else "")
      
        self.date_label = QLabel("Date (YYYY/MM/DD):")
        self.date_edit = QLineEdit()
        self.date_edit.setText(str(record['date']) if pd.notna(record['date']) else "")
      
        self.button_layout = QHBoxLayout()
        self.save_button = QPushButton("Save")
        self.cancel_button = QPushButton("Cancel")
      
        self.button_layout.addWidget(self.save_button)
        self.button_layout.addWidget(self.cancel_button)
        self.layout.addWidget(self.crm_id_label)
        self.layout.addWidget(self.crm_id_edit)
        self.layout.addWidget(self.solution_label)
        self.layout.addWidget(self.solution_edit)
        self.layout.addWidget(self.element_label)
        self.layout.addWidget(self.element_edit)
        self.layout.addWidget(self.value_label)
        self.layout.addWidget(self.value_edit)
        self.layout.addWidget(self.date_label)
        self.layout.addWidget(self.date_edit)
        self.layout.addLayout(self.button_layout)
        self.setLayout(self.layout)
      
        self.save_button.clicked.connect(self.accept)
        self.cancel_button.clicked.connect(self.reject)

    def get_updated_record(self):
        return {
            'crm_id': self.crm_id_edit.text(),
            'solution_label': self.solution_edit.text(),
            'element': self.element_edit.text(),
            'value': float(self.value_edit.text()) if is_numeric(self.value_edit.text()) else self.record['value'],
            'date': self.date_edit.text() if validate_jalali_date(self.date_edit.text()) else self.record['date']
        }

class DataLoaderThread(QThread):
    data_loaded = pyqtSignal(pd.DataFrame, pd.DataFrame)
    error_occurred = pyqtSignal(str)
    progress_updated = pyqtSignal(int)

    def __init__(self, db_path):
        super().__init__()
        self.db_path = db_path

    def run(self):
        try:
            logger.debug(f"Loading data from {self.db_path}")
            self.progress_updated.emit(20)
            crm_df, blank_df = load_crm_data()
            self.progress_updated.emit(100)
            self.data_loaded.emit(crm_df, blank_df)
        except Exception as e:
            logger.error(f"Data loading error: {str(e)}")
            self.error_occurred.emit(f"Failed to load data: {str(e)}")

class FilterThread(QThread):
    filtered_data = pyqtSignal(pd.DataFrame, pd.DataFrame)
    progress_updated = pyqtSignal(int)

    def __init__(self, crm_df, blank_df, filters):
        super().__init__()
        self.crm_df = crm_df
        self.blank_df = blank_df
        self.filters = filters

    def run(self):
        filtered_crm_df = self.crm_df.copy()
        filtered_blank_df = self.blank_df.copy()
        # Device
        if self.filters['device']:
            filtered_crm_df = filtered_crm_df[filtered_crm_df['folder_name'].str.contains(self.filters['device'], case=False, na=False)]
            filtered_blank_df = filtered_blank_df[filtered_blank_df['folder_name'].str.contains(self.filters['device'], case=False, na=False)]
        # CRM ID
        if self.filters['crm']:
            filtered_crm_df = filtered_crm_df[filtered_crm_df['norm_crm_id'] == self.filters['crm']]
        # Element
        if self.filters['element']:
            base_element = self.filters['element']
            filtered_crm_df = filtered_crm_df[
                filtered_crm_df['element'].str.startswith(base_element + ' ', na=False) |
                (filtered_crm_df['element'] == base_element)
            ]
            filtered_blank_df = filtered_blank_df[
                filtered_blank_df['element'].str.startswith(base_element + ' ', na=False) |
                (filtered_blank_df['element'] == base_element)
            ]
        # Date
        if self.filters['from_date']:
            filtered_crm_df = filtered_crm_df[filtered_crm_df['date'] >= self.filters['from_date'].strftime("%Y/%m/%d")]
            filtered_blank_df = filtered_blank_df[filtered_blank_df['date'] >= self.filters['from_date'].strftime("%Y/%m/%d")]
        if self.filters['to_date']:
            filtered_crm_df = filtered_crm_df[filtered_crm_df['date'] <= self.filters['to_date'].strftime("%Y/%m/%d")]
            filtered_blank_df = filtered_blank_df[filtered_blank_df['date'] <= self.filters['to_date'].strftime("%Y/%m/%d")]
        self.filtered_data.emit(filtered_crm_df, filtered_blank_df)

class QCTab(QWidget):
    
    def __init__(self, parent=None):
        super().__init__(parent)
        
        # Data & paths
        self.crm_df = pd.DataFrame()
        self.blank_df = pd.DataFrame()
        self.crm_db_path = resource_path(CRM_DATA_PATH)
        self.ver_db_path = resource_path(CRM_BLANK_PATH)
        self.filtered_crm_df_cache = None
        self.filtered_blank_df_cache = None
        self.plot_df_cache = None
        self.updating_filters = False
        self.verification_cache = {}
        self.plot_data_items = []
        self.logo_path = Path(LOGO_PNG_PATH)

        init_settings_table()

        # ====================== MAIN LAYOUT ======================
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setSpacing(0)
        self.main_layout.setContentsMargins(0, 0, 0, 0)

        # ====================== MODERN TOOLBAR ======================
        self.toolbar = QWidget()
        self.toolbar.setFixedHeight(70)
        self.toolbar.setStyleSheet(STYLES['toolbar'])
        toolbar_layout = QHBoxLayout(self.toolbar)
        toolbar_layout.setContentsMargins(30, 0, 30, 0)
        toolbar_layout.setSpacing(15)

        # App title
        title = QLabel("CRM Data Visualizer")
        title.setStyleSheet("color: white; font-size: 20px; font-weight: bold;")
        toolbar_layout.addWidget(title)
        toolbar_layout.addStretch()

        self.export_button = QPushButton("Export Table")
        self.edit_button = QPushButton("Edit Record")
        self.out_of_range_button = QPushButton("Out of Range")
        self.save_button = QPushButton("Save Plot")
        self.reset_button = QPushButton("Reset Filters")

        for btn in [self.export_button, self.edit_button, self.out_of_range_button,
                    self.save_button, self.reset_button]:
            btn.setStyleSheet(STYLES['toolbar_button'])
            btn.setCursor(Qt.CursorShape.PointingHandCursor)
            toolbar_layout.addWidget(btn)

        self.main_layout.addWidget(self.toolbar)

        # ====================== CONTENT AREA ======================
        content_widget = QWidget()
        content_layout = QVBoxLayout(content_widget)
        content_layout.setContentsMargins(30, 30, 30, 30)
        content_layout.setSpacing(25)

        # ====================== FILTER CARD (Single Row) ======================
        self.filter_card = QFrame()
        self.filter_card.setStyleSheet(STYLES['filter_card'])
        filter_layout = QVBoxLayout(self.filter_card)
        filter_layout.setSpacing(16)
        filter_layout.setContentsMargins(24, 20, 24, 20)

        # Filter title
        filter_title = QLabel("Filter Controls")
        filter_title.setStyleSheet("font-size: 17px; font-weight: bold; color: #1e293b;")
        filter_layout.addWidget(filter_title)

        # Horizontal row of filters
        filters_row = QHBoxLayout()
        filters_row.setSpacing(16)

        # Create widgets first
        self.device_combo = QComboBox()
        self.crm_combo = QComboBox()
        self.from_date_edit = QLineEdit()
        self.to_date_edit = QLineEdit()
        self.percentage_edit = QLineEdit("10")

        # Placeholders & sizes
        self.from_date_edit.setPlaceholderText("YYYY/MM/DD")
        self.to_date_edit.setPlaceholderText("YYYY/MM/DD")
        self.percentage_edit.setFixedWidth(90)

        # Apply style
        for widget in [self.device_combo, self.crm_combo,
                    self.from_date_edit, self.to_date_edit, self.percentage_edit]:
            widget.setStyleSheet(STYLES['input'])

        # Set minimum widths
        self.device_combo.setMinimumWidth(60)
        self.crm_combo.setMinimumWidth(60)
        self.from_date_edit.setFixedWidth(120)
        self.to_date_edit.setFixedWidth(120)

        # Add label + widget pairs
        filter_items = [
            ("Device:", self.device_combo),
            ("CRM ID:", self.crm_combo),
            ("From Date:", self.from_date_edit),
            ("To Date:", self.to_date_edit),
            ("± % Range:", self.percentage_edit),
        ]

        for label_text, widget in filter_items:
            label = QLabel(label_text)
            label.setStyleSheet("color: #475569; font-weight: 600; min-width: 80px;")
            filters_row.addWidget(label)
            filters_row.addWidget(widget)

        filters_row.addStretch()
        filter_layout.addLayout(filters_row)

        # Checkboxes row
        checkbox_row = QHBoxLayout()
        checkbox_row.setSpacing(30)

        self.best_wl_check = QCheckBox("Select Best Wavelength")
        self.apply_blank_check = QCheckBox("Apply Blank Correction")
        self.best_wl_check.setChecked(True)
        self.apply_blank_check.setChecked(False)

        for cb in (self.best_wl_check, self.apply_blank_check):
            cb.setStyleSheet(STYLES['checkbox'])

        checkbox_row.addWidget(self.best_wl_check)
        checkbox_row.addWidget(self.apply_blank_check)
        checkbox_row.addStretch()
        filter_layout.addLayout(checkbox_row)

        # ========== ELEMENT BUTTONS ==========
        element_section = QHBoxLayout()
        element_section.setSpacing(12)

        self.element_buttons_layout = QHBoxLayout()
        self.element_buttons_layout.setSpacing(6)
        element_section.addLayout(self.element_buttons_layout)
        element_section.addStretch()

        self.element_button_group = QButtonGroup()
        self.element_buttons = {}  # برای نگهداری دکمه‌ها
        self.selected_element = None  # عنصر انتخاب شده

        filter_layout.addLayout(element_section)
        content_layout.addWidget(self.filter_card)

        # Progress bar
        self.progress_bar = QProgressBar()
        self.progress_bar.setMaximum(100)
        self.progress_bar.setTextVisible(False)
        self.progress_bar.setVisible(False)
        self.progress_bar.setStyleSheet(STYLES['progress_bar'])
        content_layout.addWidget(self.progress_bar)

        # Plot widget
        self.plot_widget = PlotWidget()
        self.plot_widget.setTitle("CRM Measurement Values", color='#1e293b', size='18pt')
        self.plot_widget.setLabel('left', 'Value', color='#475569')
        self.plot_widget.setLabel('bottom', 'Observation', color='#475569')
        self.plot_widget.addLegend(offset=(10, 10))
        self.plot_widget.setBackground('w')
        self.plot_widget.showGrid(x=True, y=True, alpha=0.3)
        content_layout.addWidget(self.plot_widget, stretch=3)

        # Table widget
        self.table_widget = QTableWidget()
        self.table_widget.setColumnCount(9)
        self.table_widget.setHorizontalHeaderLabels([
            "ID", "CRM ID", "Solution Label", "Element", "Value",
            "Blank Value", "File Name", "Date", "Ref Proximity %"
        ])
        header = self.table_widget.horizontalHeader()
        header.setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        header.setStyleSheet(STYLES['table_header'])
        self.table_widget.setStyleSheet(STYLES['table'])
        self.table_widget.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        content_layout.addWidget(self.table_widget, stretch=2)

        # Status label
        self.status_label = QLabel("Loading data...")
        self.status_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.status_label.setStyleSheet(STYLES['status_label'])
        content_layout.addWidget(self.status_label)

        # Add content to main layout
        self.main_layout.addWidget(content_widget, stretch=1)

        # ====================== اتصال سیگنال‌ها ======================
        self.device_combo.currentTextChanged.connect(self.on_device_or_crm_changed)
        self.crm_combo.currentTextChanged.connect(self.on_device_or_crm_changed)
        self.from_date_edit.textChanged.connect(self.on_filter_changed)
        self.to_date_edit.textChanged.connect(self.on_filter_changed)
        self.percentage_edit.textChanged.connect(self.on_filter_changed)
        self.best_wl_check.stateChanged.connect(self.on_filter_changed)
        self.apply_blank_check.stateChanged.connect(self.on_filter_changed)

        self.export_button.clicked.connect(self.export_table)
        self.edit_button.clicked.connect(self.edit_record)
        self.out_of_range_button.clicked.connect(self.show_out_of_range_dialog)
        self.save_button.clicked.connect(lambda:save_plot(self))
        self.reset_button.clicked.connect(self.reset_filters)
        btn.toggled.connect(self.on_element_changed)
        # ========== TOOLTIP ==========
        self.tooltip_label = QLabel()
        self.tooltip_label.setStyleSheet(STYLES['tooltip'])
        self.tooltip_label.setVisible(False)
        self.tooltip_label.setWindowFlags(
            Qt.WindowType.ToolTip | 
            Qt.WindowType.FramelessWindowHint | 
            Qt.WindowType.WindowStaysOnTopHint
        )
        self.tooltip_label.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)

        # Tooltip رو به parent اضافه کن
        self.layout().addWidget(self.tooltip_label)
        self.plot_widget.scene().sigMouseMoved.connect(lambda x:on_mouse_moved(self,x))

        # ====================== شروع ======================
        self._pending_settings = load_qc_settings()
        self.load_data_thread()

    def auto_plot(self):
        """خودکار پلات کردن با تغییر element و تنظیم خودکار محورها"""
        if self.updating_filters:
            return
        plot_data(self)
        # فعال‌سازی Auto Range
        self.plot_widget.enableAutoRange()

    def on_device_or_crm_changed(self):
        """وقتی Device یا CRM تغییر کرد، عناصر رو آپدیت کن"""
        if self.updating_filters:
            return
        self.update_element_combo() # بدون پارامتر

    def load_data_thread(self):
        self.progress_bar.setVisible(True)
        self.loader_thread = DataLoaderThread(self.ver_db_path)
        self.loader_thread.data_loaded.connect(self.on_data_loaded)
        self.loader_thread.error_occurred.connect(self.on_data_error)
        self.loader_thread.progress_updated.connect(self.progress_bar.setValue)
        self.loader_thread.finished.connect(lambda: self.progress_bar.setVisible(False))
        self.loader_thread.start()

    def on_data_loaded(self, crm_df, blank_df):
        self.crm_df = crm_df
        self.blank_df = blank_df
        logger.info(f"Loaded {len(crm_df)} CRM records and {len(blank_df)} BLANK records")
      
        self.populate_filters()
        self.status_label.setText("Data loaded successfully")
        # بعد از پر شدن ComboBoxها، تنظیمات رو بازیابی کن
        if hasattr(self, '_loaded_settings'):
            QTimer.singleShot(100, self.restore_filters_after_load)

    def on_data_error(self, error_message):
        self.crm_df = pd.DataFrame()
        self.blank_df = pd.DataFrame()
        self.status_label.setText(error_message)
        logger.error(error_message)
        QMessageBox.critical(self, "Error", error_message)
        self.populate_filters()

    def closeEvent(self, event):
        """ذخیره تنظیمات هنگام بسته شدن پنجره."""
        try:
            logger.info("Settings saved on application close")
        except Exception as e:
            logger.error(f"Error saving settings on close: {str(e)}")
        finally:
            event.accept()

    def on_filter_changed(self):
        """فیلترها تغییر کردند → ذخیره فوری + آپدیت"""
        if self.updating_filters:
            return
        self.updating_filters = True
        try:
            # --- استخراج فیلترها ---
            from_date = None
            if validate_jalali_date(self.from_date_edit.text()):
                y, m, d = map(int, self.from_date_edit.text().split('/'))
                from_date = JalaliDate(y, m, d)
            to_date = None
            if validate_jalali_date(self.to_date_edit.text()):
                y, m, d = map(int, self.to_date_edit.text().split('/'))
                to_date = JalaliDate(y, m, d)
            filters = {
                'device': self.device_combo.currentText(),
                'element': self.selected_element,  # فقط عنصر انتخاب شده
                'crm': self.crm_combo.currentText(),
                'from_date': from_date,
                'to_date': to_date
            }
            # --- اجرای فیلتر در ترد جدا ---
            self.progress_bar.setVisible(True)
            self.filter_thread = FilterThread(self.crm_df, self.blank_df, filters)
            self.filter_thread.filtered_data.connect(self.on_filtered_data)
            self.filter_thread.progress_updated.connect(self.progress_bar.setValue)
            self.filter_thread.finished.connect(lambda: self.progress_bar.setVisible(False))
            self.filter_thread.start()
        except Exception as e:
            logger.error(f"Error in on_filter_changed: {str(e)}")
        finally:
            self.updating_filters = False

    def extract_device_name(self, folder_name):
        if not folder_name or not isinstance(folder_name, str):
            return None
        allowed_devices = {'mass', 'oes 4ac', 'oes fire'}
        normalized_name = folder_name.strip().lower()
        if normalized_name in allowed_devices:
            return normalized_name
        return None
  
    def restore_filters_after_load(self):
        """بازیابی مقادیر فیلتر بعد از لود داده و پر شدن ComboBoxها"""
        if not hasattr(self, '_loaded_settings'):
            return
        self.updating_filters = True
        try:
            device, element, crm_id, from_date, to_date, percentage, best_wl, apply_blank = self._loaded_settings
            # Device
            if device and self.device_combo.findText(device) != -1:
                self.device_combo.setCurrentText(device)
            # CRM
            if crm_id and self.crm_combo.findText(crm_id) != -1:
                self.crm_combo.setCurrentText(crm_id)
            # تاریخ
            if validate_jalali_date(from_date):
                self.from_date_edit.setText(from_date)
            if validate_jalali_date(to_date):
                self.to_date_edit.setText(to_date)
            # درصد
            if validate_percentage(percentage):
                self.percentage_edit.setText(percentage)
            # چک‌باکس‌ها
            self.best_wl_check.setChecked(bool(best_wl))
            self.apply_blank_check.setChecked(bool(apply_blank))
            # حالا element رو آپدیت کن (بعد از device و crm)
            QTimer.singleShot(100, lambda: self.update_element_combo(element))
        except Exception as e:
            logger.error(f"Error restoring filters: {str(e)}")
        finally:
            self.updating_filters = False
            # بعد از همه، فیلترها رو اعمال کن
            QTimer.singleShot(200, self.update_filters)

    def restore_element(self, target_element):
        self.update_element_combo() # اول آپدیت کن

    def apply_pending_settings(self):
        if not hasattr(self, '_pending_settings') or not self._pending_settings:
            return
        settings = self._pending_settings
        self.updating_filters = True
        try:
            # 1. Device
            if settings['device'] and self.device_combo.findText(settings['device']) != -1:
                self.device_combo.setCurrentText(settings['device'])
            # 2. CRM
            if settings['crm_id'] and self.crm_combo.findText(settings['crm_id']) != -1:
                self.crm_combo.setCurrentText(settings['crm_id'])
            # 3. تاریخ و درصد
            if validate_jalali_date(settings['from_date']):
                self.from_date_edit.setText(settings['from_date'])
            if validate_jalali_date(settings['to_date']):
                self.to_date_edit.setText(settings['to_date'])
            if validate_percentage(settings['percentage']):
                self.percentage_edit.setText(settings['percentage'])
            # 4. چک‌باکس‌ها
            self.best_wl_check.setChecked(bool(settings['best_wl']))
            self.apply_blank_check.setChecked(bool(settings['apply_blank']))
            # 5. Element — بعد از device و crm
            QTimer.singleShot(150, lambda: self.restore_element(settings['element']))
        except Exception as e:
            logger.error(f"Error applying settings: {str(e)}")
        finally:
            self.updating_filters = False
            QTimer.singleShot(300, self.update_filters) # بعد از همه، فیلتر کن
          
    def on_data_loaded(self, crm_df, blank_df):
        self.crm_df = crm_df
        self.blank_df = blank_df
        self.populate_filters()
        self.status_label.setText("Data loaded")
        # apply_pending_settings در populate_filters فراخوانی میشه

    def populate_filters(self):
        if self.crm_df.empty and self.blank_df.empty:
            return
        self.updating_filters = True
        try:
            # --- Device ---
            self.device_combo.blockSignals(True)
            self.device_combo.clear()
            self.device_combo.addItems(['mass', 'oes 4ac', 'oes fire'])
            self.device_combo.blockSignals(False)
            # --- CRM ---
            self.crm_combo.blockSignals(True)
            self.crm_combo.clear()
            crms = sorted(self.crm_df['norm_crm_id'].dropna().unique())
            self.crm_combo.addItems(crms)
            self.crm_combo.blockSignals(False)

        except Exception as e:
            logger.error(f"Error in populate_filters: {str(e)}")
        finally:
            self.updating_filters = False
        # بعد از پر شدن ComboBoxها، تنظیمات رو اعمال کن
        QTimer.singleShot(100, self.apply_pending_settings)
      
    def update_filters(self):
        if self.updating_filters:
            return
        self.updating_filters = True
        try:
            if self.crm_df.empty and self.blank_df.empty:
                self.table_widget.setRowCount(0)
                self.status_label.setText("No data available")
                logger.warning("No data available for filtering")
                return
            from_date = None
            if validate_jalali_date(self.from_date_edit.text()):
                y, m, d = map(int, self.from_date_edit.text().split('/'))
                from_date = JalaliDate(y, m, d)
            to_date = None
            if validate_jalali_date(self.to_date_edit.text()):
                y, m, d = map(int, self.to_date_edit.text().split('/'))
                to_date = JalaliDate(y, m, d)
            filters = {
                'device': self.device_combo.currentText(),
                'element': self.selected_element,
                'crm': self.crm_combo.currentText(),
                'from_date': from_date,
                'to_date': to_date
            }
            # logger.debug(f"Updating filters: {filters}")
            # فقط در صورتی که فیلترها تغییر کرده باشند، ذخیره کن
            current_settings = {
                'device': self.device_combo.currentText(),
                'element': self.selected_element,
                'crm_id': self.crm_combo.currentText(),
                'from_date': self.from_date_edit.text(),
                'to_date': self.to_date_edit.text(),
                'percentage': self.percentage_edit.text(),
                'best_wl_checked': 1 if self.best_wl_check.isChecked() else 0,
                'apply_blank_checked': 1 if self.apply_blank_check.isChecked() else 0
            }
            # بررسی تغییرات با تنظیمات قبلی
            conn = get_ver_db()
            cursor = conn.cursor()
            cursor.execute("SELECT * FROM settings WHERE id = 1")
            saved_settings = cursor.fetchone()
            
            if saved_settings:
                saved_settings_dict = {
                    'device': saved_settings[1],
                    'element': saved_settings[2],
                    'crm_id': saved_settings[3],
                    'from_date': saved_settings[4],
                    'to_date': saved_settings[5],
                    'percentage': saved_settings[6],
                    'best_wl_checked': saved_settings[7],
                    'apply_blank_checked': saved_settings[8]
                }
                if current_settings != saved_settings_dict:
                   save_qc_settings(current_settings)
            self.progress_bar.setVisible(True)
            self.filter_thread = FilterThread(self.crm_df, self.blank_df, filters)
            self.filter_thread.filtered_data.connect(self.on_filtered_data)
            self.filter_thread.progress_updated.connect(self.progress_bar.setValue)
            self.filter_thread.finished.connect(lambda: self.progress_bar.setVisible(False))
            self.filter_thread.start()
        finally:
            self.updating_filters = False

    def on_filtered_data(self, filtered_crm_df, filtered_blank_df):
        self.filtered_crm_df_cache = filtered_crm_df
        self.filtered_blank_df_cache = filtered_blank_df
      
        self.status_label.setText(f"Filtered: {len(filtered_crm_df)} CRM, {len(filtered_blank_df)} BLANK records")
        logger.info(f"فیلتر اعمال شد: {len(filtered_crm_df)} CRM")
        # پلات و جدول هر دو از داده‌های فیلتر شده استفاده کنند
        self.auto_plot() # این plot_df_cache را پر می‌کند
        self.update_table(filtered_crm_df, filtered_blank_df) # این جدول را هم پر می‌کند

    def on_element_changed(self, checked):
        """وقتی دکمه عنصر تغییر کرد"""
        # self.sender() رو استفاده کن که دکمه رو برمی‌گردونه
        button = self.sender()
        if button and checked:  # فقط وقتی checked=True
            # Uncheck همه دکمه‌های دیگه
            for btn_name, btn in self.element_buttons.items():
                if btn != button:
                    btn.setChecked(False)
            
            self.selected_element = button.text()
            logger.info(f"Element selected: {self.selected_element}")
            self.on_filter_changed()

    def update_element_combo(self, target_element=None):
        """ایجاد دکمه‌های عناصر بر اساس Device و CRM - حالا در چند ردیف"""
        if self.updating_filters:
            return
        
        self.updating_filters = True
        try:
            # پاک کردن دکمه‌های قبلی
            for btn_name, btn in list(self.element_buttons.items()):
                try:
                    btn.toggled.disconnect(self.on_element_changed)
                except:
                    pass
                btn.deleteLater()
                del self.element_buttons[btn_name]
            
            # پاک کردن layout قبلی
            while self.element_buttons_layout.count():
                child = self.element_buttons_layout.takeAt(0)
                if child.widget():
                    child.widget().deleteLater()

            # === تغییر مهم: استفاده از QGridLayout به جای QHBoxLayout ===
            grid_layout = QGridLayout()
            grid_layout.setSpacing(8)
            grid_layout.setContentsMargins(0, 0, 0, 0)

            current_device = self.device_combo.currentText()
            current_crm = self.crm_combo.currentText()
            
            # فیلتر داده
            mask = pd.Series([True] * len(self.crm_df), index=self.crm_df.index)
            if current_device:
                mask &= self.crm_df['folder_name'].str.contains(current_device, case=False, na=False)
            if current_crm:
                mask &= (self.crm_df['norm_crm_id'] == current_crm)
            
            filtered = self.crm_df[mask]
            
            if not filtered.empty:
                elements = sorted({
                    el.split()[0] for el in filtered['element'].dropna().unique()
                    if isinstance(el, str) and ' ' in el
                })

                # تنظیمات چیدمان: حداکثر ستون‌ها در هر ردیف
                max_columns = 35  # می‌تونی این رو تغییر بدی (۱۰ تا ۱۵ مناسب است)

                first_selected = False
                for i, element in enumerate(elements):
                    btn = QPushButton(element)
                    btn.setCheckable(True)
                    btn.setStyleSheet(STYLES['btn_smalls'])
                    btn.setFixedHeight(32)  # ارتفاع ثابت برای زیبایی

                    # قرار دادن در گرید: ردیف و ستون
                    row = i // max_columns
                    col = i % max_columns
                    grid_layout.addWidget(btn, row, col)

                    self.element_buttons[element] = btn
                    btn.toggled.connect(self.on_element_changed)

                    # انتخاب خودکار اولین یا هدف
                    if (not self.selected_element and i == 0) or (target_element == element):
                        btn.setChecked(True)
                        self.selected_element = element
                        first_selected = True

                # اگر هیچ‌کدام انتخاب نشده، اولین رو انتخاب کن
                if not self.selected_element and self.element_buttons:
                    first_element = list(self.element_buttons.keys())[0]
                    self.element_buttons[first_element].setChecked(True)
                    self.selected_element = first_element

                # جایگزینی layout قدیم با گرید جدید
                self.element_buttons_layout.addLayout(grid_layout)

            logger.info(f"Element buttons created: {len(self.element_buttons)} elements in grid layout")
            
        except Exception as e:
            logger.error(f"Error in update_element_combo: {str(e)}")
        finally:
            self.updating_filters = False
            QTimer.singleShot(50, self.on_filter_changed)

    def update_table(self, filtered_crm_df=None, filtered_blank_df=None):
        self.table_widget.blockSignals(True)
      
        # استفاده از داده‌های فیلتر شده (نه فقط پلات شده)
        crm_df = filtered_crm_df if filtered_crm_df is not None else self.filtered_crm_df_cache
        blank_df = filtered_blank_df if filtered_blank_df is not None else self.filtered_blank_df_cache
        if crm_df is None or crm_df.empty:
            self.table_widget.setRowCount(0)
            self.table_widget.blockSignals(False)
            return
        # ادغام با BLANK اگر لازم باشد
        combined_df = crm_df.copy()
        if self.apply_blank_check.isChecked():
            combined_df['blank_value'] = pd.NA
            for i, row in combined_df.iterrows():
                blank_value, _ = self.select_best_blank(row, blank_df, None) # ver_value لازم نیست
                combined_df.at[i, 'blank_value'] = blank_value
        # مرتب‌سازی
        combined_df = combined_df.sort_values(['date', 'norm_crm_id', 'element'])
        # محاسبه نزدیکی به مقدار مرجع (اگر ممکن)
        current_element = self.selected_element
        current_crm = self.crm_combo.currentText()
        combined_df['ref_proximity'] = pd.NA
        if current_element != "All Elements" and current_crm != "All CRM IDs":
            ver_value = self.get_verification_value(current_crm, current_element)
            if ver_value is not None:
                target_col = 'value'
                if self.apply_blank_check.isChecked():
                    # اگر blank اعمال شده، از value (که corrected است) استفاده کن
                    pass # قبلاً در plot_data اعمال شده
                combined_df['ref_proximity'] = (ver_value - combined_df[target_col] ) / ver_value * 100
        # پر کردن جدول
        self.table_widget.setRowCount(len(combined_df))
        for i, row in combined_df.iterrows():
            self.table_widget.setItem(i, 0, QTableWidgetItem(str(row.get('id', ''))))
            self.table_widget.setItem(i, 1, QTableWidgetItem(str(row['crm_id'])))
            self.table_widget.setItem(i, 2, QTableWidgetItem(str(row['solution_label'])))
            self.table_widget.setItem(i, 3, QTableWidgetItem(str(row['element'])))
            self.table_widget.setItem(i, 4, QTableWidgetItem(f"{row['value']:.6f}" if pd.notna(row['value']) else ""))
            self.table_widget.setItem(i, 5, QTableWidgetItem(f"{row.get('blank_value', ''):.6f}" if pd.notna(row.get('blank_value')) else ""))
            self.table_widget.setItem(i, 6, QTableWidgetItem(str(row['file_name'])))
            self.table_widget.setItem(i, 7, QTableWidgetItem(str(row['date']) if pd.notna(row['date']) else ""))
            self.table_widget.setItem(i, 8, QTableWidgetItem(f"{row['ref_proximity']:.2f}%" if pd.notna(row['ref_proximity']) else ""))
        self.status_label.setText(f"Table: {len(combined_df)} records")
        logger.info(f"جدول با {len(combined_df)} رکورد به‌روزرسانی شد")
        self.table_widget.blockSignals(False)

    def export_table(self):
        if self.plot_df_cache is None or self.plot_df_cache.empty:
            QMessageBox.warning(self, "Warning", "No data to export")
            return
        fname, _ = QFileDialog.getSaveFileName(self, "Save CSV", "", "CSV (*.csv)")
        if fname:
            try:
                self.plot_df_cache.to_csv(fname, index=False, encoding='utf-8')
                self.status_label.setText("Table exported successfully")
                logger.info(f"Table exported to {fname}")
            except Exception as e:
                logger.error(f"Error exporting table: {str(e)}")
                QMessageBox.critical(self, "Error", f"Failed to export table: {str(e)}")

    def get_verification_value(self, crm_id, element):
        return get_verification_value(crm_id, element)
                
    def select_best_blank(self, crm_row, blank_df, ver_value):
        if blank_df.empty or ver_value is None:
            logger.debug(f"No blank correction applied: empty blank_df={blank_df.empty}, ver_value={ver_value}")
            return None, crm_row['value']
      
        relevant_blanks = blank_df[
            (blank_df['file_name'] == crm_row['file_name']) &
            (blank_df['folder_name'] == crm_row['folder_name']) &
            (blank_df['element'] == crm_row['element'])
        ]
      
        if relevant_blanks.empty:
            logger.debug(f"No relevant blanks found for CRM: file={crm_row['file_name']}, folder={crm_row['folder_name']}, element={crm_row['element']}")
            return None, crm_row['value']
      
        # Valid BLANK pattern: without 'par', usually with 1-2 letters
        blank_valid_pattern = re.compile(BLANK_4AC, re.IGNORECASE)
      
        valid_blanks = relevant_blanks[relevant_blanks['solution_label'].apply(lambda x: bool(blank_valid_pattern.match(str(x).strip())))]
        print(relevant_blanks,'valid blank :',valid_blanks)
        if valid_blanks.empty:
            logger.debug(f"No valid blanks found for CRM row {crm_row['id']}")
            return None, crm_row['value']
      
        initial_diff = abs(crm_row['value'] - ver_value)
        best_blank_value = None
        best_diff = initial_diff
        corrected_value = crm_row['value']
      
        for _, blank_row in valid_blanks.iterrows():
            blank_value = blank_row['value']
            if pd.notna(blank_value):
                try:
                    corrected = crm_row['value'] - blank_value
                    new_diff = abs(ver_value - corrected)
                    # print(corrected,ver_value,blank_value)
                    # logger.debug(f"Blank: solution_label={blank_row['solution_label']}, value={blank_value}, corrected={corrected}, new_diff={new_diff}, initial_diff={initial_diff}")
                    if new_diff < initial_diff:
                        best_diff = new_diff
                        best_blank_value = blank_value
                        corrected_value = corrected
                except (TypeError, ValueError) as e:
                    logger.warning(f"Invalid blank value {blank_value} for CRM row {crm_row['id']}: {str(e)}")
                    continue
      
        if best_blank_value is not None:
            logger.info(f"Selected blank value {best_blank_value} for CRM row {crm_row['id']}, corrected value={corrected_value}, diff={best_diff}")
        else:
            logger.debug(f"No valid blank value selected for CRM row {crm_row['id']}, using original value={crm_row['value']}")
      
        return best_blank_value, corrected_value

    def show_out_of_range_dialog(self):
        all_df = pd.concat([self.crm_df, self.blank_df])
        file_names = all_df['file_name'].unique()
        if len(file_names) == 0:
            QMessageBox.warning(self, "Warning", "No files available")
            return
      
        percentage = float(self.percentage_edit.text()) if validate_percentage(self.percentage_edit.text()) else 10.0
        dialog = OutOfRangeFilesDialog(self, file_names, self.crm_db_path, percentage, self.ver_db_path)
        dialog.exec()

    def edit_record(self):
        """ویرایش رکورد انتخاب شده در جدول"""
        selected_row = self.table_widget.currentRow()
        if selected_row < 0:
            QMessageBox.warning(self, "Warning", "Please select a record to edit.")
            return

        # ترکیب داده‌های فیلتر شده CRM و BLANK
        all_df = pd.concat([self.filtered_crm_df_cache, self.filtered_blank_df_cache], ignore_index=True)
        if all_df.empty or selected_row >= len(all_df):
            QMessageBox.warning(self, "Warning", "No valid record selected.")
            return

        record = all_df.iloc[selected_row]

        # باز کردن دیالوگ ویرایش
        dialog = EditRecordDialog(self, record.to_dict())
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return  # کاربر لغو کرد

        updated_record = dialog.get_updated_record()

        # استفاده از تابع متمرکز در qc_queries
        success = update_crm_record(
            record_id=int(record['id']),
            updates={
                'crm_id': updated_record['crm_id'],
                'solution_label': updated_record['solution_label'],
                'element': updated_record['element'],
                'value': updated_record['value'],
                'date': updated_record['date']
            }
        )

        if success:
            logger.info(f"Successfully updated record ID {record['id']}")
            self.status_label.setText("Record updated successfully.")
            # رفرش کامل داده‌ها (بهترین روش برای اطمینان از همگامی)
            self.load_data_thread()
        else:
            logger.error(f"Failed to update record ID {record['id']}")
            QMessageBox.critical(self, "Error", "Failed to update record in database.")

    def reset_filters(self):
        if self.updating_filters:
            return
        # ریست فیلترها
        if self.device_combo.count() > 0:
            self.device_combo.setCurrentIndex(0)
        if self.crm_combo.count() > 0:
            self.crm_combo.setCurrentIndex(0)
        self.from_date_edit.clear()
        self.to_date_edit.clear()
        self.percentage_edit.setText("10")
        self.best_wl_check.setChecked(True)
        self.apply_blank_check.setChecked(False)
        logger.debug("FiltersG reset and saved")
        self.update_filters()