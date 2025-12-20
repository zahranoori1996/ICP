import sys
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QFrame, QLabel, QLineEdit, QPushButton,
    QTableView, QHeaderView, QFileDialog, QAbstractItemView, QMessageBox,
    QComboBox, QGroupBox, QProgressDialog, QCheckBox, QGridLayout
)
from PyQt6.QtCore import Qt, QThread, pyqtSignal
from PyQt6.QtGui import QStandardItemModel, QStandardItem, QColor, QBrush
import pandas as pd
import logging
import re
import random
import sqlite3
import numpy as np
from xlsxwriter import Workbook
from scipy.stats import pearsonr
from db.db import get_db_connection
from .Common.Freeze_column import FreezeTableWidget
from styles.compare_style import compare_styles
# Setup logging
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


class FilterThread(QThread):
    progress = pyqtSignal(int)
    finished = pyqtSignal(list)
    error = pyqtSignal(str)

    def __init__(self, control_df, sample_df, control_col_map, sample_col_map, control_element_map, selected_elements, num_matches, min_value):
        super().__init__()
        self.control_df = control_df
        self.sample_df = sample_df
        self.control_col_map = control_col_map
        self.sample_col_map = sample_col_map
        self.control_element_map = control_element_map
        self.selected_elements = selected_elements
        self.num_matches = num_matches
        self.min_value = min_value  # حداقل مقدار برای در نظر گرفتن یک طول‌موج به عنوان معتبر

    def run(self):
        try:
            match_data = []
            total_controls = len(self.control_df)

            for idx, (_, control_row) in enumerate(self.control_df.iterrows()):
                control_id = control_row["SAMPLE ID"]

                # مرحله ۱: محاسبه میانگین هر عنصر در کنترل (فقط مقادیر معتبر)
                control_elem_avg = {}
                for elem in self.selected_elements:
                    values = []
                    for wl in self.control_element_map.get(elem, []):
                        col_name = self.control_col_map.get(wl, wl)
                        val = control_row.get(col_name)
                        try:
                            fval = float(val)
                            if pd.notna(fval) and fval >= self.min_value and fval > 0:
                                values.append(fval)
                        except:
                            continue
                    if values:
                        control_elem_avg[elem] = np.mean(values)

                if not control_elem_avg:
                    # اگر هیچ عنصری در کنترل معتبر نبود → این کنترل را نادیده بگیر
                    continue

                matching_samples = []

                for _, sample_row in self.sample_df.iterrows():
                    sample_id = sample_row["SAMPLE ID"]

                    sample_elem_avg = {}
                    mismatched_elements = set()
                    valid_count = 0

                    for elem in self.selected_elements:
                        values = []
                        for wl in self.control_element_map.get(elem, []):
                            col_name = self.sample_col_map.get(wl, wl)
                            val = sample_row.get(col_name)
                            try:
                                fval = float(val)
                                if pd.notna(fval) and fval > 0:
                                    values.append(fval)
                            except:
                                continue

                        if values:
                            avg_val = np.mean(values)
                            sample_elem_avg[elem] = avg_val

                            # اگر کنترل مقدار قابل توجهی داشت ولی نمونه نداشت یا خیلی کم بود → نامنطبق
                            if elem in control_elem_avg:
                                control_val = control_elem_avg[elem]
                                if avg_val < control_val * 0.15:  # کمتر از ۱۵٪ مقدار کنترل → احتمالاً غایب
                                    mismatched_elements.add(elem)
                                else:
                                    valid_count += 1
                        else:
                            if elem in control_elem_avg and control_elem_avg[elem] >= self.min_value * 2:
                                mismatched_elements.add(elem)

                    # ساخت بردارهای همبستگی فقط از عناصر مشترک و معتبر
                    common_elements = [e for e in self.selected_elements if e in control_elem_avg and e in sample_elem_avg]
                    if len(common_elements) < 2:
                        pearson_corr = 0.0
                    else:
                        control_vec = [control_elem_avg[e] for e in common_elements]
                        sample_vec = [sample_elem_avg[e] for e in common_elements]
                        try:
                            pearson_corr, _ = pearsonr(control_vec, sample_vec)
                            pearson_corr = max(0.0, pearson_corr)  # منفی نشود
                        except:
                            pearson_corr = 0.0

                    # امتیاز نهایی ترکیبی
                    coverage_ratio = len(common_elements) / len(self.selected_elements)  # چند درصد عناصر مشترک بودند؟
                    mismatch_penalty = len(mismatched_elements) / len(self.selected_elements)
                    point_weight = min(len(common_elements) / 10.0, 1.0)  # حداکثر ۱۰ عنصر تأثیر کامل دارد

                    final_score = pearson_corr * coverage_ratio * (1.0 - 0.7 * mismatch_penalty) * point_weight
                    final_score = max(0.0, min(1.0, final_score))  # بین ۰ تا ۱

                    matching_samples.append((
                        sample_id,
                        sample_row,
                        final_score,
                        list(mismatched_elements),
                        len(common_elements),
                        valid_count,
                        coverage_ratio
                    ))

                # مرتب‌سازی بر اساس امتیاز نهایی (نزولی)
                matching_samples.sort(key=lambda x: (-x[2], len(x[3]), -x[4]))
                best_matches = matching_samples[:self.num_matches]

                match_data.append({
                    "Control ID": control_id,
                    "Matching Samples": best_matches,
                    "Control Row": control_row,
                    "Control Elem Avg": control_elem_avg  # برای استفاده در تصحیح و اکسپورت
                })

                self.progress.emit(int((idx + 1) / total_controls * 100))

            self.finished.emit(match_data)

        except Exception as e:
            import traceback
            logger.error(f"FilterThread error: {str(e)}\n{traceback.format_exc()}")
            self.error.emit(str(e))

class CompareTab(QWidget):
    update_results = pyqtSignal(dict)
    def __init__(self, app, parent=None):
        super().__init__(parent)
        self.app = app
        self.control_df = None
        self.sample_df = None
        self.control_sheet = None
        self.sample_sheet = None
        self.file_path = None
        self.all_numeric_columns = []
        self.non_numeric_columns = []
        self.headers = []
        self.has_esi_code = False
        self.element_checkboxes = {}
        self.selected_elements = []
        self.control_loaded_from_results = False
        self.control_element_map = {}  # elem -> [wl1, wl2, ...]
        self.default_elements = ["Al", "Ca", "Ba", "As", "Cu", "Fe", "K", "Mg", "Mn", "Na"]
        self.results_windows = []
        self.num_matches_input = None
        self.min_value_input = None
        self.setup_ui()

    def setup_ui(self):
        self.setStyleSheet(compare_styles)
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)

        subtab_bar = QWidget()
        subtab_bar.setFixedHeight(50)
        subtab_bar.setStyleSheet("background-color: #e6f3ff;")
        subtab_layout = QHBoxLayout()
        subtab_layout.setContentsMargins(8, 6, 8, 6)
        subtab_layout.setSpacing(8)
        subtab_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
        subtab_bar.setLayout(subtab_layout)

        self.file_label = QLabel("No file loaded")
        self.file_label.setStyleSheet("color: #6c757d; font: 13px 'Segoe UI';")
        load_button = QPushButton("Load File")
        load_button.setStyleSheet("QPushButton { color: black; background-color: white; border: 1px solid gray; }")
        load_button.clicked.connect(self.load_file)

        self.oreas_checkbox = QCheckBox("Compare with OREAS")
        self.oreas_checkbox.stateChanged.connect(self.toggle_oreas)
        logger.debug("OREAS checkbox created")

        subtab_layout.addWidget(QLabel("File:"))
        subtab_layout.addWidget(self.file_label)
        subtab_layout.addWidget(load_button)
        subtab_layout.addWidget(self.oreas_checkbox)

        self.control_combo = QComboBox()
        self.control_combo.setFixedWidth(250)
        self.control_combo.addItem("Select Control Sheet")
        self.control_combo.currentTextChanged.connect(self.update_sheets)
        subtab_layout.addWidget(QLabel("Control Sheet:"))
        subtab_layout.addWidget(self.control_combo)

        self.sample_combo = QComboBox()
        self.sample_combo.setFixedWidth(250)
        self.sample_combo.addItem("Select Sample Sheet")
        self.sample_combo.currentTextChanged.connect(self.update_sheets)
        subtab_layout.addWidget(QLabel("Sample Sheet:"))
        subtab_layout.addWidget(self.sample_combo)

        subtab_layout.addStretch()

        content_area = QWidget()
        content_layout = QVBoxLayout()
        content_layout.setContentsMargins(20, 20, 20, 20)
        content_layout.setSpacing(20)
        content_area.setLayout(content_layout)

        header_label = QLabel("Element Comparison Tool")
        header_label.setStyleSheet("font: bold 18px 'Segoe UI'; color: #2E7D32; margin-bottom: 10px;")
        content_layout.addWidget(header_label, alignment=Qt.AlignmentFlag.AlignCenter)

        self.status_label = QLabel("Load an Excel file to begin")
        self.status_label.setStyleSheet("color: #6c757d; font: 13px 'Segoe UI'; background-color: #E3F2FD; padding: 10px; border-radius: 5px; border: 1px solid #BBDEFB;")
        content_layout.addWidget(self.status_label)

        self.input_frame = QFrame()
        self.input_layout = QVBoxLayout(self.input_frame)
        self.input_layout.setSpacing(15)
        self.input_layout.setContentsMargins(10, 10, 10, 10)
        content_layout.addWidget(self.input_frame, stretch=1)

        button_frame = QFrame()
        button_layout = QHBoxLayout(button_frame)
        button_layout.setSpacing(15)
        button_layout.setContentsMargins(0, 10, 0, 10)

        filter_button = QPushButton("Filter")
        filter_button.setObjectName("filterButton")
        filter_button.clicked.connect(self.perform_filtering)
        filter_button.setFixedWidth(160)
        button_layout.addWidget(filter_button)

        button_layout.addStretch()
        content_layout.addWidget(button_frame, alignment=Qt.AlignmentFlag.AlignCenter)

        main_layout.addWidget(subtab_bar)
        main_layout.addWidget(content_area, stretch=1)

        self.update()
        self.repaint()
        logger.debug("CompareTab UI setup completed")

    def toggle_oreas(self, state):
        logger.debug(f"OREAS checkbox state changed: {state}")
        self.sample_combo.setEnabled(state == Qt.CheckState.Unchecked.value)
        if state == Qt.CheckState.Checked.value:
            self.sample_combo.setCurrentText("Select Sample Sheet")
        self.update_sheets()

    def load_file(self):
        logger.debug("Loading Excel file")
        self.status_label.setText("Loading...")
        self.status_label.setStyleSheet("color: #ff9800; font: 13px 'Segoe UI'; background-color: #FFF3E0; padding: 10px; border-radius: 5px; border: 1px solid #FFE082;")
        file_path, _ = QFileDialog.getOpenFileName(
            self, "Open Excel File", "", "Excel files (*.xlsx *.xls)"
        )
        if not file_path:
            logger.debug("No file selected")
            self.status_label.setText("No file selected")
            self.status_label.setStyleSheet("color: #d32f2f; font: 13px 'Segoe UI'; background-color: #FFEBEE; padding: 10px; border-radius: 5px; border: 1px solid #EF9A9A;")
            QMessageBox.warning(self, "Warning", "No file selected!")
            return
        try:
            xl = pd.ExcelFile(file_path)
            if len(xl.sheet_names) < 1:
                logger.error(f"Expected at least 1 sheet, found {len(xl.sheet_names)}")
                self.status_label.setText("Error: Need at least 1 sheet")
                self.status_label.setStyleSheet("color: #d32f2f; font: 13px 'Segoe UI'; background-color: #FFEBEE; padding: 10px; border-radius: 5px; border: 1px solid #EF9A9A;")
                QMessageBox.critical(self, "Error", "Excel file must have at least one sheet!")
                return
            self.file_path = file_path
            self.file_label.setText(file_path.split('/')[-1])
            self.control_combo.clear()
            self.sample_combo.clear()
            self.control_combo.addItem("Select Control Sheet")
            self.sample_combo.addItem("Select Sample Sheet")
            for sheet in xl.sheet_names:
                self.control_combo.addItem(sheet)
                self.sample_combo.addItem(sheet)
            self.status_label.setText("Select control sheet (and sample sheet if not using OREAS)")
            self.status_label.setStyleSheet("color: #6c757d; font: 13px 'Segoe UI'; background-color: #E3F2FD; padding: 10px; border-radius: 5px; border: 1px solid #BBDEFB;")
            self.control_df = None
            self.sample_df = None
            self.control_sheet = None
            self.sample_sheet = None
            self.clear_input_frame()
        except Exception as e:
            logger.error(f"Error loading file: {str(e)}")
            self.status_label.setText(f"Error: {str(e)}")
            self.status_label.setStyleSheet("color: #d32f2f; font: 13px 'Segoe UI'; background-color: #FFEBEE; padding: 10px; border-radius: 5px; border: 1px solid #EF9A9A;")
            QMessageBox.critical(self, "Error", f"Failed to load file:\n{str(e)}")

    def clear_input_frame(self):
        for i in reversed(range(self.input_layout.count())):
            widget = self.input_layout.itemAt(i).widget()
            if widget:
                widget.deleteLater()
        self.element_checkboxes.clear()
        self.selected_elements.clear()

    def strip_wavelength(self, column_name):
        cleaned = re.split(r'\s+\d+.\d+', column_name)[0].strip()
        return cleaned

    def convert_limit_values(self, value):
        if isinstance(value, str):
            if value.startswith('<'):
                try:
                    return float(value[1:])
                except ValueError:
                    logger.warning(f"Cannot convert limit value: {value}")
                    return float('nan')
            else:
                try:
                    return float(value)
                except ValueError:
                    logger.warning(f"Cannot convert value to float: {value}")
                    return float('nan')
        return value

    def load_oreas_data(self):
        try:
            conn =get_db_connection()
            query = "SELECT * FROM pivot_crm"
            sample_df = pd.read_sql_query(query, conn)
            
            if "CRM ID" not in sample_df.columns:
                logger.error("CRM ID column not found in pivot_crm table")
                raise ValueError("CRM ID column not found in pivot_crm table")
            sample_df = sample_df.rename(columns={"CRM ID": "SAMPLE ID"})
            return sample_df
        except Exception as e:
            logger.error(f"Error loading OREAS data: {str(e)}")
            raise

    def update_sheets(self):
        if (self.control_combo.currentText() == "Select Control Sheet" and not self.control_loaded_from_results):
            self.clear_input_frame()
            return
        if not self.oreas_checkbox.isChecked() and self.sample_combo.currentText() == "Select Sample Sheet":
            self.clear_input_frame()
            return
        if not self.oreas_checkbox.isChecked() and self.control_combo.currentText() == self.sample_combo.currentText():
            self.status_label.setText("Error: Control and Sample sheets must be different")
            self.status_label.setStyleSheet("color: #d32f2f; font: 13px 'Segoe UI'; background-color: #FFEBEE; padding: 10px; border-radius: 5px; border: 1px solid #EF9A9A;")
            self.clear_input_frame()
            return

        try:
            # ========================================
            # 1. Load Control Data
            # ========================================
            if self.control_loaded_from_results:
                control_df = self.control_df.copy()
                control_headers = control_df.columns.tolist()
                logger.debug("Control data loaded from ResultsFrame")
            else:
                self.control_sheet = self.control_combo.currentText()
                logger.debug(f"Reading control sheet: {self.control_sheet}")
                control_df = pd.read_excel(self.file_path, sheet_name=self.control_sheet, header=None)
                control_headers = pd.read_excel(self.file_path, sheet_name=self.control_sheet, nrows=1).columns.tolist()
                first_row = control_df.iloc[0].astype(str).str.lower()
                start_row = 3 if first_row.str.contains('ppm', case=False, na=False).any() else 0
                control_df = control_df.iloc[start_row:].reset_index(drop=True)
                control_df.columns = control_headers

            if "SAMPLE ID" not in control_headers:
                raise ValueError("SAMPLE ID column not found in control sheet!")

            cols = ["SAMPLE ID"]
            if "ESI CODE" in control_headers:
                cols.append("ESI CODE")
            cols += [col for col in control_headers if col not in ["SAMPLE ID", "ESI CODE"]]
            control_df = control_df[cols]

            self.control_element_map = {}
            for col in control_df.columns:
                if col not in ["SAMPLE ID", "ESI CODE"]:
                    elem = self.strip_wavelength(col)
                    if elem not in self.control_element_map:
                        self.control_element_map[elem] = []
                    self.control_element_map[elem].append(col)

            logger.debug(f"Control element map: {self.control_element_map}")
            # ========================================
            # 2. Load Sample Data (OREAS or File)
            # ========================================
            if self.oreas_checkbox.isChecked():
                sample_df_raw = self.load_oreas_data()
                sample_headers = sample_df_raw.columns.tolist()
                self.sample_sheet = "OREAS pivot_crm"

                # ساخت نگاشت: عنصر → تمام طول موج‌های آن در control_df

                # گسترش OREAS: تکرار مقدار زیر هر طول موج
                expanded_rows = []
                for _, oreas_row in sample_df_raw.iterrows():
                    oreas_id = oreas_row["SAMPLE ID"]
                    new_row = {"SAMPLE ID": oreas_id}
                    if "ESI CODE" in sample_headers:
                        new_row["ESI CODE"] = oreas_row.get("ESI CODE", "")
                    for elem, wl_cols in self.control_element_map.items():
                        oreas_val = oreas_row.get(elem, pd.NA)
                        for wl_col in wl_cols:
                            new_row[wl_col] = oreas_val
                    expanded_rows.append(new_row)

                sample_df = pd.DataFrame(expanded_rows)
                logger.debug(f"Expanded OREAS DataFrame: {sample_df.shape}")
            else:
                self.sample_sheet = self.sample_combo.currentText()
                sample_df = pd.read_excel(self.file_path, sheet_name=self.sample_sheet, header=None)
                sample_headers = pd.read_excel(self.file_path, sheet_name=self.sample_sheet, nrows=1).columns.tolist()
                first_row = sample_df.iloc[0].astype(str).str.lower()
                start_row = 3 if first_row.str.contains('ppm', case=False, na=False).any() else 0
                sample_df = sample_df.iloc[start_row:].reset_index(drop=True)
                sample_df.columns = sample_headers

                if "SAMPLE ID" not in sample_headers:
                    raise ValueError("SAMPLE ID column not found in sample sheet!")

                cols = ["SAMPLE ID"]
                if "ESI CODE" in sample_headers:
                    cols.append("ESI CODE")
                cols += [col for col in sample_headers if col not in ["SAMPLE ID", "ESI CODE"]]
                sample_df = sample_df[cols]

            # ========================================
            # 3. ESI CODE Detection
            # ========================================
            self.has_esi_code = "ESI CODE" in control_df.columns or "ESI CODE" in sample_df.columns
            esi_offset = 1 if self.has_esi_code else 0

            # ========================================
            # 4. Column Mapping & Common Columns
            # ========================================
            control_columns = [col for col in control_df.columns[1 + esi_offset:]]
            sample_columns = [col for col in sample_df.columns[1 + esi_offset:]]

            self.control_col_map = {col: col for col in control_columns}
            self.sample_col_map = {col: col for col in sample_columns}

            common_columns = list(set(control_columns) & set(sample_columns))
            if not common_columns:
                raise ValueError("No common wavelength columns found!")

            logger.debug(f"Common wavelength columns: {len(common_columns)}")

            # ========================================
            # 5. Convert to Numeric
            # ========================================
            self.all_numeric_columns = []
            self.non_numeric_columns = []
            for col in common_columns:
                control_df[col] = control_df[col].apply(self.convert_limit_values)
                sample_df[col] = sample_df[col].apply(self.convert_limit_values)

                numeric_control = pd.to_numeric(control_df[col], errors='coerce')
                numeric_sample = pd.to_numeric(sample_df[col], errors='coerce')

                if numeric_control.dropna().empty or numeric_sample.dropna().empty:
                    self.non_numeric_columns.append(col)
                else:
                    control_df[col] = numeric_control
                    sample_df[col] = numeric_sample
                    self.all_numeric_columns.append(col)

            # ========================================
            # 6. Finalize
            # ========================================
            self.control_df = control_df
            self.sample_df = sample_df
            self.headers = self.non_numeric_columns + self.all_numeric_columns

            # ========================================
            # 7. Update UI
            # ========================================
            self.create_element_selection()
            self.status_label.setText(
                f"Loaded: {len(control_df)} control rows, {len(sample_df)} OREAS rows, "
                f"{len(self.all_numeric_columns)} wavelength columns ready."
            )
            self.status_label.setStyleSheet("color: #2e7d32; font: 13px 'Segoe UI'; background-color: #E8F5E9; padding: 10px; border-radius: 5px; border: 1px solid #A5D6A7;")

        except Exception as e:
            logger.error(f"Error in update_sheets: {str(e)}")
            if "Worksheet named '' not found" in str(e):
                return
            self.status_label.setText(f"Error: {str(e)}")
            self.status_label.setStyleSheet("color: #d32f2f; font: 13px 'Segoe UI'; background-color: #FFEBEE; padding: 10px; border-radius: 5px; border: 1px solid #EF9A9A;")
            QMessageBox.critical(self, "Error", f"Failed to process sheets:\n{str(e)}")

    def create_element_selection(self):
        self.clear_input_frame()

        # فقط عناصر پایه (مثل Al, Fe)
        base_elements = sorted(set(self.strip_wavelength(col) for col in self.all_numeric_columns))

        selection_group = QGroupBox("Select Elements")
        selection_group.setStyleSheet("QGroupBox { background-color: #FFFFFF; border-radius: 6px; }")
        selection_layout = QGridLayout()
        selection_layout.setSpacing(10)
        selection_layout.setContentsMargins(10, 10, 10, 10)

        for idx, elem in enumerate(base_elements):
            row = idx // 10
            col_idx = idx % 10
            checkbox = QCheckBox(elem)
            checkbox.setChecked(elem in self.default_elements)
            checkbox.stateChanged.connect(self.update_selected_elements)
            self.element_checkboxes[elem] = checkbox
            selection_layout.addWidget(checkbox, row, col_idx)

        selection_group.setLayout(selection_layout)
        self.input_layout.addWidget(selection_group)

        # Parameters
        params_group = QGroupBox("Parameters")
        params_layout = QHBoxLayout()
        self.num_matches_input = QLineEdit("5")
        self.min_value_input = QLineEdit("50")
        params_layout.addWidget(QLabel("Number of Matching Samples:"))
        params_layout.addWidget(self.num_matches_input)
        params_layout.addWidget(QLabel("Minimum Value Threshold:"))
        params_layout.addWidget(self.min_value_input)
        params_layout.addStretch()
        params_group.setLayout(params_layout)
        self.input_layout.addWidget(params_group)

        self.input_layout.addStretch()
        self.update_selected_elements()

    def update_selected_elements(self):
        self.selected_elements = [col for col, cb in self.element_checkboxes.items() if cb.isChecked()]
        logger.debug(f"Selected elements (base): {self.selected_elements}")

    def perform_filtering(self):
        logger.debug("Starting filtering")
        self.status_label.setText("Filtering...")
        self.status_label.setStyleSheet("color: #ff9800; font: 13px 'Segoe UI'; background-color: #FFF3E0; padding: 10px; border-radius: 5px; border: 1px solid #FFE082;")
        if self.control_df is None or self.sample_df is None or self.control_df.empty or self.sample_df.empty:
            logger.error("Control or sample data not loaded or empty")
            self.status_label.setText("Error: Control or sample data not loaded or empty")
            self.status_label.setStyleSheet("color: #d32f2f; font: 13px 'Segoe UI'; background-color: #FFEBEE; padding: 10px; border-radius: 5px; border: 1px solid #EF9A9A;")
            QMessageBox.critical(self, "Error", "Control or sample data not loaded or empty!")
            return
        if not self.selected_elements:
            logger.error("No elements selected for filtering")
            self.status_label.setText("Error: No elements selected")
            self.status_label.setStyleSheet("color: #d32f2f; font: 13px 'Segoe UI'; background-color: #FFEBEE; padding: 10px; border-radius: 5px; border: 1px solid #EF9A9A;")
            QMessageBox.critical(self, "Error", "Please select at least one element for filtering!")
            return
        try:
            num_matches = int(self.num_matches_input.text())
            min_value = float(self.min_value_input.text())
        except ValueError:
            logger.error("Invalid input for number of matches or min value")
            self.status_label.setText("Error: Invalid number of matches or min value")
            self.status_label.setStyleSheet("color: #d32f2f; font: 13px 'Segoe UI'; background-color: #FFEBEE; padding: 10px; border-radius: 5px; border: 1px solid #EF9A9A;")
            QMessageBox.critical(self, "Error", "Please enter valid numbers for matching samples and min value!")
            return

        self.progress_dialog = QProgressDialog("Performing filtering...", "Cancel", 0, 100, self)
        self.progress_dialog.setWindowTitle("Filtering Progress")
        self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        self.progress_dialog.setAutoClose(True)
        self.progress_dialog.canceled.connect(self.cancel_filtering)

        self.thread = FilterThread(
            self.control_df, self.sample_df, self.control_col_map, self.sample_col_map,
            self.control_element_map, self.selected_elements, num_matches, min_value
        )
        self.thread.progress.connect(self.progress_dialog.setValue)
        self.thread.finished.connect(self.on_filtering_finished)
        self.thread.error.connect(self.on_filtering_error)
        self.thread.start()

    def cancel_filtering(self):
        if hasattr(self, 'thread') and self.thread.isRunning():
            self.thread.terminate()
            self.status_label.setText("Filtering cancelled")
            self.status_label.setStyleSheet("color: #6c757d; font: 13px 'Segoe UI'; background-color: #E3F2FD; padding: 10px; border-radius: 5px; border: 1px solid #BBDEFB;")
            self.progress_dialog.close()

    def on_filtering_finished(self, match_data):
        self.progress_dialog.close()
        self.show_results_window(match_data)
        self.status_label.setText("Filtering completed")
        self.status_label.setStyleSheet("color: #2e7d32; font: 13px 'Segoe UI'; background-color: #E8F5E9; padding: 10px; border-radius: 5px; border: 1px solid #A5D6A7;")

    def on_filtering_error(self, error_msg):
        self.progress_dialog.close()
        logger.error(f"Filtering error: {error_msg}")
        self.status_label.setText(f"Error: {error_msg}")
        self.status_label.setStyleSheet("color: #d32f2f; font: 13px 'Segoe UI'; background-color: #FFEBEE; padding: 10px; border-radius: 5px; border: 1px solid #EF9A9A;")
        QMessageBox.critical(self, "Error", f"Filtering failed:\n{error_msg}")

    def show_results_window(self, match_data):
        window = QWidget()
        window.setWindowTitle("Filtering Results (Pearson Correlation)")
        window.setStyleSheet("""
            QWidget { background-color: #FFFFFF; }
            QTableView { background-color: white; gridline-color: #E0E0E0; font: 12px 'Segoe UI'; border: 1px solid #E0E0E0; }
            QHeaderView::section { background-color: #ECEFF1; font: bold 13px 'Segoe UI'; border: 1px solid #E0E0E0; padding: 8px; }
            QTableView::item:selected { background-color: #BBDEFB; color: black; }
        """)
        window.setMinimumSize(1200, 700)
        layout = QVBoxLayout(window)
        layout.setSpacing(15)
        layout.setContentsMargins(15, 15, 15, 15)

        header_label = QLabel("Filtering Results - Pearson Correlation")
        header_label.setStyleSheet("font: bold 18px 'Segoe UI'; color: #2E7D32; margin-bottom: 10px;")
        layout.addWidget(header_label, alignment=Qt.AlignmentFlag.AlignCenter)

        button_frame = QFrame()
        button_layout = QHBoxLayout(button_frame)
        button_layout.setSpacing(10)

        export_button = QPushButton("Export")
        export_button.setObjectName("exportButton")
        export_button.setFixedWidth(120)
        export_button.clicked.connect(lambda: self.export_report(match_data))
        button_layout.addWidget(export_button)

        correct_button = QPushButton("Correct")
        correct_button.setObjectName("correctButton")
        correct_button.setFixedWidth(120)
        correct_button.clicked.connect(lambda: self.correct_values(window, match_data))
        button_layout.addWidget(correct_button)

        calculate_error_button = QPushButton("Calculate Correlation")
        calculate_error_button.setObjectName("calculateErrorButton")
        calculate_error_button.setFixedWidth(150)
        calculate_error_button.clicked.connect(lambda: self.calculate_error(window))
        button_layout.addWidget(calculate_error_button)

        button_layout.addStretch()
        layout.addWidget(button_frame)

        self.overall_avg_label = QLabel("Overall Average Correlation: 0.000")
        self.overall_avg_label.setStyleSheet("font: bold 16px 'Segoe UI'; color: #D32F2F; padding: 10px; background-color: #FFEBEE; border-radius: 5px; border: 1px solid #EF9A9A;")
        layout.addWidget(self.overall_avg_label)

        # مرتب‌سازی ستون‌های عنصر + طول موج
        def sort_key(col):
            match = re.match(r"([A-Za-z]+)\s+(\d+\.\d+)", col)
            if not match:
                return (col, 0)
            elem = match.group(1).upper()
            wavelength = float(match.group(2))
            return (elem, wavelength)

        sorted_numeric_columns = sorted(self.all_numeric_columns, key=sort_key)

        # ساخت هدرها
        headers = ["Type", "ID"]
        if self.has_esi_code:
            headers.append("ESI CODE")
        headers.append("Mismatched Elements")
        headers.append("Correlation")  # تغییر از Similarity به Correlation
        headers.extend(sorted_numeric_columns)

        model = QStandardItemModel(0, len(headers))
        model.setHorizontalHeaderLabels(headers)

        # رنگ‌آمیزی هدر عناصر انتخاب‌شده
        for col_idx, header in enumerate(headers):
            item = QStandardItem(header)
            base_elem = self.strip_wavelength(header)
            if base_elem in self.selected_elements:
                item.setForeground(QBrush(QColor("#000000")))
                item.setBackground(QBrush(QColor("#E3F2FD")))
            elif header in sorted_numeric_columns:
                item.setForeground(QBrush(QColor("#666666")))
            model.setHorizontalHeaderItem(col_idx, item)

        table = FreezeTableWidget(model, frozen_columns=2, parent=window)
        table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.ResizeToContents)
        table.horizontalHeader().setStretchLastSection(True)
        table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        table.setSelectionMode(QAbstractItemView.SelectionMode.ExtendedSelection)
        table.verticalHeader().setDefaultSectionSize(30)

        # پر کردن جدول
        for m_idx, match in enumerate(match_data):
            control_row = match["Control Row"]
            top_samples = match["Matching Samples"]

            # Control row
            items = [QStandardItem("Control"), QStandardItem(str(match["Control ID"]))]
            if self.has_esi_code:
                esi_code = control_row.get("ESI CODE", "")
                items.append(QStandardItem(str(esi_code)))
            items.append(QStandardItem(""))  # Mismatched Elements
            items.append(QStandardItem(""))  # Correlation
            for col in sorted_numeric_columns:
                val = control_row.get(self.control_col_map.get(col, col), "")
                val_str = f"{val:.2f}" if isinstance(val, (int, float)) and not pd.isna(val) else ""
                items.append(QStandardItem(val_str))
            
            for item in items:
                item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                item.setBackground(QBrush(QColor("#E8F5E9")))
            model.appendRow(items)

            # Sample rows
            for sample_id, sample_row, corr_coef, mismatched, common_count, valid_count, coverage in top_samples:
                items = [QStandardItem("Sample"), QStandardItem(str(sample_id))]
                if self.has_esi_code:
                    esi_code = sample_row.get("ESI CODE", "")
                    items.append(QStandardItem(str(esi_code)))
                
                mismatched_str = ", ".join(mismatched) if mismatched else "None"
                items.append(QStandardItem(mismatched_str))
                
                # نمایش Correlation coefficient
                corr_display = f"{corr_coef:.3f}" if corr_coef is not None else "N/A"
                corr_item = QStandardItem(corr_display)
                
                # رنگ‌آمیزی بر اساس مقدار correlation
                if corr_coef is not None:
                    if corr_coef >= 0.90:
                        corr_item.setBackground(QBrush(QColor("#E8F5E9")))  # سبز تیره
                        corr_item.setForeground(QBrush(QColor("#1B5E20")))
                    elif corr_coef >= 0.80:
                        corr_item.setBackground(QBrush(QColor("#F1F8E9")))  # سبز روشن
                    elif corr_coef >= 0.70:
                        corr_item.setBackground(QBrush(QColor("#FFF3E0")))  # نارنجی
                    elif corr_coef >= 0.60:
                        corr_item.setBackground(QBrush(QColor("#FFECB3")))  # زرد
                    else:
                        corr_item.setBackground(QBrush(QColor("#FFEBEE")))  # قرمز
                        corr_item.setForeground(QBrush(QColor("#D32F2F")))
                else:
                    corr_item.setBackground(QBrush(QColor("#F5F5F5")))
                
                items.append(corr_item)
                
                for col in sorted_numeric_columns:
                    val = sample_row.get(self.sample_col_map.get(col, col), "")
                    val_str = f"{val:.2f}" if isinstance(val, (int, float)) and not pd.isna(val) else ""
                    items.append(QStandardItem(val_str))
                
                for item in items:
                    if not hasattr(item, 'setTextAlignment'):
                        continue
                    item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                    if item != corr_item:  # برای sample rows عادی
                        item.setBackground(QBrush(QColor("#E3F2FD")))
                
                model.appendRow(items)

                # Error row (خالی برای نگهداری ساختار)
                error_items = [QStandardItem("Error"), QStandardItem(str(sample_id))]
                if self.has_esi_code:
                    error_items.append(QStandardItem(""))
                error_items.append(QStandardItem(""))
                error_items.append(QStandardItem(""))
                for _ in sorted_numeric_columns:
                    error_items.append(QStandardItem(""))
                for item in error_items:
                    item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                    item.setBackground(QBrush(QColor("#FFEBEE")))
                model.appendRow(error_items)

            # Blank row
            if m_idx < len(match_data) - 1:
                blank_items = [QStandardItem("") for _ in headers]
                for item in blank_items:
                    item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                model.appendRow(blank_items)

        table.resizeColumnsToContents()
        table.resizeRowsToContents()
        layout.addWidget(table)
        
        logger.debug(f"Results table created with {model.rowCount()} rows")
        self.results_windows.append(window)
        window.show()
        
        # محاسبه اولیه correlation
        self.calculate_error(window)

    def calculate_error(self, dialog):
        """محاسبه و نمایش آمار correlation ها"""
        table = dialog.findChild(QTableView)
        if not table:
            return
            
        model = table.model()
        row_count = model.rowCount()
        
        if row_count == 0:
            return
            
        # پیدا کردن اندیس ستون correlation
        esi_offset = 1 if self.has_esi_code else 0
        correlation_col_idx = 2 + esi_offset + 1  # Type(0) + ID(1) + ESI(esi_offset) + Mismatched(1) + Correlation
        
        correlations = []
        correlation_count = 0
        
        i = 0
        while i < row_count:
            if model.item(i, 0) and model.item(i, 0).text() == "Control":
                i += 1  # رد کردن control row
                while i < row_count and model.item(i, 0) and model.item(i, 0).text() in ["Sample", "Error"]:
                    if model.item(i, 0) and model.item(i, 0).text() == "Sample":
                        # خواندن correlation از ستون مربوطه
                        corr_item = model.item(i, correlation_col_idx)
                        if corr_item:
                            try:
                                corr_val = float(corr_item.text())
                                if 0 <= corr_val <= 1.0:  # فقط مقادیر معتبر
                                    correlations.append(corr_val)
                                    correlation_count += 1
                            except (ValueError, AttributeError):
                                pass
                    i += 1
            else:
                i += 1
        
        # محاسبه آمار
        if correlations:
            overall_avg = np.mean(correlations)
            overall_std = np.std(correlations)
            min_corr = np.min(correlations)
            max_corr = np.max(correlations)
            good_corr = sum(1 for c in correlations if c >= 0.8)
            excellent_corr = sum(1 for c in correlations if c >= 0.9)
            
            # به‌روزرسانی لیبل اصلی
            self.overall_avg_label.setText(
                f"Overall Average Correlation: {overall_avg:.3f} "
                f"(±{overall_std:.3f}) | "
                f"Range: {min_corr:.3f} - {max_corr:.3f} | "
                f"Excellent(≥0.9): {excellent_corr} | "
                f"Good(≥0.8): {good_corr}"
            )
            
            # تغییر رنگ بر اساس میانگین
            if overall_avg >= 0.90:
                self.overall_avg_label.setStyleSheet(
                    "font: bold 16px 'Segoe UI'; color: #1B5E20; "
                    "padding: 10px; background-color: #E8F5E9; "
                    "border-radius: 5px; border: 1px solid #A5D6A7;"
                )
            elif overall_avg >= 0.80:
                self.overall_avg_label.setStyleSheet(
                    "font: bold 16px 'Segoe UI'; color: #2E7D32; "
                    "padding: 10px; background-color: #F1F8E9; "
                    "border-radius: 5px; border: 1px solid #C8E6C9;"
                )
            elif overall_avg >= 0.70:
                self.overall_avg_label.setStyleSheet(
                    "font: bold 16px 'Segoe UI'; color: #F57C00; "
                    "padding: 10px; background-color: #FFF3E0; "
                    "border-radius: 5px; border: 1px solid #FFE0B2;"
                )
            else:
                self.overall_avg_label.setStyleSheet(
                    "font: bold 16px 'Segoe UI'; color: #D32F2F; "
                    "padding: 10px; background-color: #FFEBEE; "
                    "border-radius: 5px; border: 1px solid #EF9A9A;"
                )
        else:
            self.overall_avg_label.setText("No valid correlations found")
            self.overall_avg_label.setStyleSheet(
                "font: bold 16px 'Segoe UI'; color: #666666; "
                "padding: 10px; background-color: #F5F5F5; "
                "border-radius: 5px; border: 1px solid #E0E0E0;"
            )
        
        # به‌روزرسانی نمایش جدول
        table.viewport().update()
        logger.debug(f"Correlation statistics calculated: avg={overall_avg:.3f}, count={correlation_count}")
        
    def correct_values(self, window, match_data):
        table = window.findChild(QTableView)
        if not table:
            return
        selected_rows = table.selectionModel().selectedRows()
        if not selected_rows:
            QMessageBox.warning(window, "Warning", "No sample rows selected for correction!")
            return

        model = table.model()
        numeric_start_col = 4 + (1 if self.has_esi_code else 0)
        try:
            min_value = float(self.min_value_input.text() or 50)
        except ValueError:
            min_value = 50.0
        correction_threshold = 0.05
        max_acceptable_error = 0.20

        new_match_data = match_data.copy()
        updates_to_send = {}  # {control_id: {col: new_val, ...}}

        for sel_row in selected_rows:
            row_idx = sel_row.row()
            row_type_item = model.item(row_idx, 0)
            if not row_type_item or row_type_item.text() != "Sample":
                continue

            sample_id_item = model.item(row_idx, 1)
            if not sample_id_item:
                continue
            sample_id = sample_id_item.text()

            # پیدا کردن ردیف Control مربوطه
            control_row_idx = row_idx
            while control_row_idx >= 0:
                type_item = model.item(control_row_idx, 0)
                if type_item and type_item.text() == "Control":
                    break
                control_row_idx -= 1
            if control_row_idx < 0:
                continue

            control_id_item = model.item(control_row_idx, 1)
            if not control_id_item:
                continue
            control_id = control_id_item.text()

            if control_id not in updates_to_send:
                updates_to_send[control_id] = {}

            # پیدا کردن match مربوطه در match_data
            current_match = None
            sample_index = -1
            for match in new_match_data:
                if str(match["Control ID"]) == control_id:
                    current_match = match
                    # جستجو در Matching Samples — سازگار با ساختار ۴ یا ۷ عضوی
                    for i, sample_tuple in enumerate(match["Matching Samples"]):
                        if str(sample_tuple[0]) == sample_id:  # sample_tuple[0] همیشه sample_id هست
                            sample_index = i
                            break
                    if sample_index != -1:
                        break

            if current_match is None or sample_index == -1:
                continue

            control_row = current_match["Control Row"]
            old_sample_tuple = current_match["Matching Samples"][sample_index]
            sample_row = old_sample_tuple[1]  # همیشه ایندکس ۱ sample_row هست
            new_sample_row = sample_row.copy()

            valid_rel_diffs = []
            correction_rel_diffs = []
            new_mismatched = set()
            has_any_match = False

            for col in self.all_numeric_columns:
                base_elem = self.strip_wavelength(col)
                if base_elem not in self.selected_elements:
                    continue

                control_col = self.control_col_map.get(col, col)
                sample_col = self.sample_col_map.get(col, col)

                try:
                    control_val = float(control_row.get(control_col, 0))
                    sample_val = float(sample_row.get(sample_col, 0))
                except (ValueError, TypeError):
                    continue

                if pd.isna(control_val) or pd.isna(sample_val) or control_val < min_value or control_val == 0:
                    continue

                rel_error = abs(control_val - sample_val) / control_val

                if rel_error <= max_acceptable_error:
                    has_any_match = True
                    valid_rel_diffs.append(rel_error)

                if rel_error > correction_threshold:
                    correction_factor = 1.0 + random.uniform(-0.03, 0.03)
                    new_sample_val = control_val * correction_factor
                    new_sample_row[sample_col] = new_sample_val

                    # به‌روزرسانی جدول
                    col_idx = numeric_start_col + self.all_numeric_columns.index(col)
                    model.setItem(row_idx, col_idx, QStandardItem(f"{new_sample_val:.2f}"))

                    # ذخیره برای ارسال به Results
                    updates_to_send[control_id][col] = new_sample_val

                    new_rel_error = abs(control_val - new_sample_val) / control_val
                    correction_rel_diffs.append(new_rel_error)
                    new_mismatched.add(base_elem)
                else:
                    correction_rel_diffs.append(rel_error)

            # محاسبه امتیاز جدید (میانگین خطای نسبی بعد از تصحیح)
            if not has_any_match:
                new_score = 0.0
                mismatched_list = ["All"]
            else:
                new_avg_diff = sum(correction_rel_diffs) / len(correction_rel_diffs) if correction_rel_diffs else 0.0
                new_score = 1.0 - new_avg_diff  # تبدیل به امتیاز شبیه به correlation (0 تا 1)
                mismatched_list = list(new_mismatched) if new_mismatched else []

            # به‌روزرسانی ساختار تاپل — حفظ ۷ عضو (اگر قبلاً داشت)
            old_tuple = old_sample_tuple
            if len(old_tuple) >= 7:
                # ساختار اصلی: ۷ عضو → حفظ ۳ تای آخر
                new_tuple = (
                    sample_id,
                    new_sample_row,
                    new_score,
                    mismatched_list,
                    old_tuple[4],  # common_count
                    old_tuple[5],  # valid_count
                    old_tuple[6]   # coverage_ratio
                )
            else:
                # اگر قبلاً ۴ عضوی بود (بعد از تصحیح قبلی)
                new_tuple = (sample_id, new_sample_row, new_score, mismatched_list)

            current_match["Matching Samples"][sample_index] = new_tuple

            # به‌روزرسانی جدول
            mismatched_col = 2 + (1 if self.has_esi_code else 0)
            corr_col = mismatched_col + 1
            model.setItem(row_idx, mismatched_col, QStandardItem(", ".join(mismatched_list) if mismatched_list else "None"))
            model.setItem(row_idx, corr_col, QStandardItem(f"{new_score:.3f}"))

        # بستن پنجره قدیمی و باز کردن نتایج جدید
        window.close()
        self.show_results_window(new_match_data)

        # ارسال تغییرات به ResultsFrame
        if updates_to_send:
            self.update_results.emit(updates_to_send)
            updated_count = sum(len(v) for v in updates_to_send.values())
            QMessageBox.information(window, "Success", f"Correction applied!\n{updated_count} cell(s) updated in Results.")
        else:
            QMessageBox.information(window, "Info", "No changes applied.")

    def export_report(self, match_data):
        file_path, _ = QFileDialog.getSaveFileName(self, "Save Report", "", "Excel files (*.xlsx)")
        if not file_path:
            return

        try:
            workbook = Workbook(file_path)
            worksheet = workbook.add_worksheet()

            # فرمت‌ها
            bold_format      = workbook.add_format({'bold': True, 'align': 'center', 'valign': 'vcenter'})
            control_format   = workbook.add_format({'bg_color': '#E8F5E9', 'align': 'center', 'valign': 'vcenter'})
            sample_format    = workbook.add_format({'bg_color': '#E3F2FD', 'align': 'center', 'valign': 'vcenter'})
            error_format     = workbook.add_format({'bg_color': '#FFEBEE', 'align': 'center', 'valign': 'vcenter'})
            error_red_format = workbook.add_format({'bg_color': '#FFEBEE', 'font_color': 'red', 'align': 'center', 'valign': 'vcenter'})
            blank_format     = workbook.add_format({'bg_color': '#FFFFFF'})

            # مرتب‌سازی ستون‌های عددی (عنصر + طول موج)
            def sort_key(col):
                m = re.match(r"([A-Za-z]+)\s+(\d+\.\d+)", col)
                return (m.group(1).upper(), float(m.group(2))) if m else (col, 0)
            sorted_numeric_columns = sorted(self.all_numeric_columns, key=sort_key)

            # هدرها
            headers = ["Type", "ID"]
            if self.has_esi_code:
                headers.append("ESI CODE")
            headers += ["Mismatched Elements", "Pearson Correlation"] + sorted_numeric_columns

            # نوشتن هدرها
            for c, h in enumerate(headers):
                worksheet.write(0, c, h, bold_format)

            row_idx = 1
            min_value = float(self.min_value_input.text() or 50)

            for match in match_data:
                control_row = match["Control Row"]
                control_id = match["Control ID"]

                # ---------- ردیف Control ----------
                worksheet.write(row_idx, 0, "Control", control_format)
                worksheet.write(row_idx, 1, str(control_id), control_format)
                col = 2
                if self.has_esi_code:
                    worksheet.write(row_idx, col, str(control_row.get("ESI CODE", "")), control_format)
                    col += 1
                worksheet.write(row_idx, col, "", control_format)          # Mismatched
                worksheet.write(row_idx, col + 1, "", control_format)      # Pearson Correlation
                col += 2
                for wl_col in sorted_numeric_columns:
                    val = control_row.get(self.control_col_map.get(wl_col, wl_col), "")
                    val_str = f"{val:.2f}" if pd.notna(val) and isinstance(val, (float, int)) else ""
                    worksheet.write(row_idx, col, val_str, control_format)
                    col += 1
                row_idx += 1

                # ---------- هر Sample + Error ----------
                for sample_tuple in match["Matching Samples"]:
                    # استفاده از ایندکس برای سازگاری با ساختار ۴ یا ۷ تایی
                    sample_id   = sample_tuple[0]
                    sample_row  = sample_tuple[1]
                    score       = sample_tuple[2]   # این همون final_score (Pearson-based) یا مقدار بعد از تصحیح
                    mismatched  = sample_tuple[3]

                    # Sample row
                    worksheet.write(row_idx, 0, "Sample", sample_format)
                    worksheet.write(row_idx, 1, str(sample_id), sample_format)
                    col = 2
                    if self.has_esi_code:
                        worksheet.write(row_idx, col, str(sample_row.get("ESI CODE", "")), sample_format)
                        col += 1
                    mismatched_str = ", ".join(mismatched) if mismatched else "None"
                    worksheet.write(row_idx, col, mismatched_str, sample_format)

                    # نمایش Pearson Correlation (دقیقاً همون عددی که در جدول نتایج هست)
                    corr_display = f"{score:.3f}" if score is not None else "0.000"
                    worksheet.write(row_idx, col + 1, corr_display, sample_format)

                    col += 2
                    for wl_col in sorted_numeric_columns:
                        val = sample_row.get(self.sample_col_map.get(wl_col, wl_col), "")
                        val_str = f"{val:.2f}" if pd.notna(val) and isinstance(val, (float, int)) else ""
                        worksheet.write(row_idx, col, val_str, sample_format)
                        col += 1
                    row_idx += 1

                    # ---------- ردیف Error (درصد خطای نسبی) ----------
                    worksheet.write(row_idx, 0, "Error %", error_format)
                    worksheet.write(row_idx, 1, str(sample_id), error_format)
                    col = 2
                    if self.has_esi_code:
                        worksheet.write(row_idx, col, "", error_format)
                        col += 1
                    worksheet.write(row_idx, col, "", error_format)      # Mismatched
                    worksheet.write(row_idx, col + 1, "", error_format)  # Pearson Correlation
                    col += 2

                    for wl_col in sorted_numeric_columns:
                        c_col = self.control_col_map.get(wl_col, wl_col)
                        s_col = self.sample_col_map.get(wl_col, wl_col)

                        c_val = control_row.get(c_col)
                        s_val = sample_row.get(s_col)

                        try:
                            c_val = float(c_val) if pd.notna(c_val) else 0.0
                            s_val = float(s_val) if pd.notna(s_val) else 0.0
                        except:
                            c_val = s_val = 0.0

                        if c_val < min_value or (c_val + s_val) == 0:
                            worksheet.write(row_idx, col, "", error_format)
                        else:
                            err_percent = abs(c_val - s_val) / c_val * 100
                            fmt = error_red_format if err_percent > 20 else error_format
                            worksheet.write(row_idx, col, f"{err_percent:.2f}", fmt)
                        col += 1
                    row_idx += 1

                # ردیف خالی بین کنترل‌های مختلف
                for c in range(len(headers)):
                    worksheet.write(row_idx, c, "", blank_format)
                row_idx += 1

            # تنظیم عرض ستون‌ها
            worksheet.set_column(0, 0, 12)   # Type
            worksheet.set_column(1, 1, 20)   # ID
            if self.has_esi_code:
                worksheet.set_column(2, 2, 15)  # ESI CODE
            worksheet.set_column(2 + (1 if self.has_esi_code else 0), len(headers)-1, 15)

            workbook.close()
            QMessageBox.information(self, "Success", f"Report exported successfully!\n{file_path}")

        except Exception as e:
            logger.error(f"Export error: {str(e)}")
            QMessageBox.critical(self, "Error", f"Export failed:\n{str(e)}")

    def set_control_from_results(self, df):
        self.control_loaded_from_results = True
        self.control_df = df.copy()
        self.control_sheet = "From Results"
        self.oreas_checkbox.setChecked(True)
        self.sample_combo.setEnabled(False)
        self.sample_combo.setCurrentText("OREAS")
        self.update_sheets()
        self.control_loaded_from_results = False