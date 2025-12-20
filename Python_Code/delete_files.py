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
from db.qc_queries import get_file_record_counts,get_out_of_range_data
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


class LoadDeleteFilesDialogThread(QThread):
    dialog_ready = pyqtSignal(list, dict) # list of file_names, dict of record_counts
    error_occurred = pyqtSignal(str)
    progress_updated = pyqtSignal(int)

    def __init__(self, db_path, file_names):
        super().__init__()
        self.db_path = db_path
        self.file_names = list(set(file_names)) # Remove duplicates upfront
        self.record_counts = {}

    def run(self):
        try:
            logger.info(f"Loading {len(self.file_names)} unique files for deletion dialog")
            self.progress_updated.emit(10)
            results = get_file_record_counts(self.file_names)
            self.progress_updated.emit(80)
          
            # Create record_counts dictionary from results
            for file_name, count in results:
                self.record_counts[file_name] = count
          
            # For files not found in results, count is 0
            for file_name in self.file_names:
                if file_name not in self.record_counts:
                    self.record_counts[file_name] = 0
          
            
            self.progress_updated.emit(100)
          
            logger.info(f"Loaded record counts for {len(self.record_counts)} files")
            self.dialog_ready.emit(sorted(self.file_names), self.record_counts)
          
        except Exception as e:
            logger.error(f"Error loading delete files dialog: {str(e)}")
            self.error_occurred.emit(f"Failed to load files for deletion: {str(e)}")
            self.progress_updated.emit(100)

class DeleteFilesDialog(QDialog):
    def __init__(self, parent=None, file_names=None, db_path=None):
        super().__init__(parent)
        self.setWindowTitle("Delete Files")
        self.setFixedSize(500, 500)
        self.db_path = db_path
        self.record_counts = {}
      
        self.layout = QVBoxLayout()
      
        # Progress bar and status label
        self.progress_bar = QProgressBar()
        self.progress_bar.setMaximum(100)
        self.progress_bar.setVisible(False)
      
        self.status_label = QLabel("Loading files... Please wait.")
        self.status_label.setAlignment(Qt.AlignCenter)
      
        # Main content
        self.main_label = QLabel("Select files to delete (number of records shown):")
        self.file_list = QListWidget()
      
        self.button_layout = QHBoxLayout()
        self.delete_button = QPushButton("Delete Selected")
        self.cancel_button = QPushButton("Cancel")
        self.refresh_button = QPushButton("Refresh")
      
        self.button_layout.addWidget(self.delete_button)
        self.button_layout.addWidget(self.refresh_button)
        self.button_layout.addWidget(self.cancel_button)
      
        # Add widgets to layout
        self.layout.addWidget(self.status_label)
        self.layout.addWidget(self.progress_bar)
        self.layout.addWidget(self.main_label)
        self.layout.addWidget(self.file_list)
        self.layout.addLayout(self.button_layout)
        self.setLayout(self.layout)
      
        # Connections
        self.delete_button.clicked.connect(self.accept)
        self.cancel_button.clicked.connect(self.reject)
        self.refresh_button.clicked.connect(self.refresh_files)
      
        # Load files if provided and non-empty, otherwise show empty message
        if file_names is not None and len(file_names) > 0: # Fix: Check length explicitly
            self.load_files_async(file_names)
        else:
            self.status_label.setText("No files provided")
            self.progress_bar.setVisible(False)
            self.delete_button.setEnabled(False)
            self.refresh_button.setEnabled(False)
  
    def load_files_async(self, file_names):
        """Load files asynchronously with progress"""
        self.progress_bar.setVisible(True)
        self.delete_button.setEnabled(False)
        self.cancel_button.setEnabled(False)
        self.refresh_button.setEnabled(False)
        self.file_list.clear()
      
        self.loader_thread = LoadDeleteFilesDialogThread(self.db_path, file_names)
        self.loader_thread.dialog_ready.connect(self.on_files_loaded)
        self.loader_thread.error_occurred.connect(self.on_load_error)
        self.loader_thread.progress_updated.connect(self.progress_bar.setValue)
        self.loader_thread.finished.connect(self.on_loading_finished)
        self.loader_thread.start()
  
    def on_files_loaded(self, file_names, record_counts):
        """Handle successful file loading"""
        self.file_names = file_names
        self.record_counts = record_counts
      
        # Populate file list
        self.file_list.clear()
        for file_name in file_names:
            count = record_counts.get(file_name, 0)
            item = QListWidgetItem(f"{file_name} ({count:,} records)")
            item.setData(32, file_name)
            item.setCheckState(Qt.Unchecked)
            item.setFlags(Qt.ItemIsUserCheckable | Qt.ItemIsEnabled)
            self.file_list.addItem(item)
      
        logger.info(f"Populated dialog with {len(file_names)} files")
  
    def on_load_error(self, error_message):
        """Handle loading error"""
        self.status_label.setText(f"Error: {error_message}")
        logger.error(f"Delete dialog load error: {error_message}")
        QMessageBox.critical(self, "Error", error_message)
  
    def on_loading_finished(self):
        """Enable UI after loading completes"""
        self.progress_bar.setVisible(False)
        self.delete_button.setEnabled(True)
        self.cancel_button.setEnabled(True)
        self.refresh_button.setEnabled(True)
        self.status_label.setText(f"Loaded {len(self.record_counts)} files")
  
    def refresh_files(self):
        """Refresh file list"""
        if hasattr(self, 'file_names') and self.file_names:
            self.load_files_async(self.file_names)
  
    def get_selected_files(self):
        """Get selected files for deletion"""
        selected = []
        for i in range(self.file_list.count()):
            item = self.file_list.item(i)
            if item.checkState() == Qt.Checked:
                selected.append(item.data(32))
        return selected
  
    def get_total_records(self):
        """Get total records for selected files"""
        total = 0
        for file_name in self.get_selected_files():
            total += self.record_counts.get(file_name, 0)
        return total