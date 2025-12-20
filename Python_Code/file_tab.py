# screens/file/file_tab.py
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QPushButton, QLabel, QComboBox,
    QLineEdit, QFileDialog, QMessageBox, QFormLayout, QDialog,
    QDialogButtonBox, QProgressDialog, QTextEdit, QTableWidget,
    QTableWidgetItem, QHeaderView, QHBoxLayout, QDateEdit, QGroupBox,
    QGridLayout, QTabWidget, QScrollArea,QFrame, QAbstractItemView
)
from PyQt6.QtCore import Qt, QThread, QDate, QRegularExpression
from PyQt6.QtGui import QRegularExpressionValidator
import pandas as pd
import sqlite3
import os
import logging
import re
from collections import defaultdict
from utils.load_file import FileLoaderThread
from screens.pivot.pivot_creator import PivotCreator
from db.db import get_db_connection,get_elements_db,resource_path
logger = logging.getLogger(__name__)
from utils.var_main import CRM_DATA_PATH,CRM_IDS,CRM_PATTERN,BLANK_PATTERN
from utils.date import miladi_to_jalali
from db.file_queries import (
    get_all_devices,
    save_uploaded_file_metadata,
    get_uploaded_files_for_operator,
    get_all_uploaded_files_with_filters,
    get_all_uploaded_files_for_report_manager,
    get_file_metadata_for_edit,
    update_file_metadata,
    save_pivoted_elements_data,
    safe_import_crm_data,delete_file_and_related_data
)

# --- UploadDialog (با اضافه کردن CRUD حرفه‌ای) ---
class UploadDialog(QDialog):
    def __init__(self, parent=None, devices=None, user_id=None):
        super().__init__(parent)
        self.setWindowTitle("File Management (Upload & CRUD)")
        self.setFixedSize(1200, 800)  # اندازه بزرگ‌تر برای CRUD
        self.devices = devices or []
        self.user_id = user_id  # برای فیلتر فایل‌های کاربر
        self.selected_file = None
        self.contracts_text = ""
        self.crms_text = ""
        self.blanks_text = ""
        self.temp_df = None
        self.valid_crm_labels = set()
        self.valid_blank_labels = set()
        self.init_ui()

    def init_ui(self):
        main_layout = QVBoxLayout(self)

        # تب ویجت برای جداسازی Upload و Manage
        self.tab_widget = QTabWidget()
        self.tab_widget.setStyleSheet("QTabWidget::pane { border: 1px solid #bdc3c7; } QTabBar::tab { padding: 10px; }")

        # تب Upload
        self.upload_tab = QWidget()
        self.init_upload_tab()
        self.tab_widget.addTab(self.upload_tab, "Upload New File")

        # تب Manage (CRUD)
        self.manage_tab = QWidget()
        self.init_manage_tab()
        self.tab_widget.addTab(self.manage_tab, "Manage Existing Files")

        main_layout.addWidget(self.tab_widget)

        # دکمه‌های پایین (برای کل دیالوگ)
        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Close
        )
        buttons.rejected.connect(self.reject)
        main_layout.addWidget(buttons)

    def init_upload_tab(self):
        layout = QFormLayout(self.upload_tab)

        self.file_label = QLabel("No file selected")
        self.file_btn = QPushButton("Select File")
        self.file_btn.clicked.connect(self.select_file)
        file_layout = QVBoxLayout()
        file_layout.addWidget(self.file_label)
        file_layout.addWidget(self.file_btn)
        layout.addRow("Raw Data File:", file_layout)

        self.contracts_display = QTextEdit()
        self.contracts_display.setReadOnly(True)
        self.contracts_display.setPlaceholderText("Contracts will be displayed here after file selection...")
        layout.addRow("Extracted Contracts:", self.contracts_display)

        self.crms_display = QTextEdit()
        self.crms_display.setReadOnly(True)
        self.crms_display.setPlaceholderText("CRMs will be displayed here after file selection...")
        layout.addRow("Extracted CRMs:", self.crms_display)

        self.blanks_display = QTextEdit()
        self.blanks_display.setReadOnly(True)
        self.blanks_display.setPlaceholderText("Blanks will be displayed here after file selection...")
        layout.addRow("Extracted Blanks:", self.blanks_display)

        self.device_combo = QComboBox()
        self.device_combo.addItems([d[1] for d in self.devices])
        layout.addRow("Device:", self.device_combo)

        self.file_type = QComboBox()
        self.file_type.addItems(["oes 4ac", "oes fire", "mass"])
        layout.addRow("File Type:", self.file_type)

        self.description = QLineEdit()
        self.description.setPlaceholderText("Optional notes...")
        layout.addRow("Description:", self.description)

        upload_btn = QPushButton("Upload")
        upload_btn.clicked.connect(self.upload_file)
        layout.addRow(upload_btn)

    def init_manage_tab(self):
        layout = QVBoxLayout(self.manage_tab)

        # گروه فیلترها
        filter_group = QGroupBox("Search Filters")
        filter_grid = QGridLayout()

        row = 0

        # قرارداد
        filter_grid.addWidget(QLabel("Contract:"), row, 0)
        self.contract_search = QLineEdit()
        self.contract_search.setPlaceholderText("Enter contract number...")
        filter_grid.addWidget(self.contract_search, row, 1)

        # تاریخ آپلود
        filter_grid.addWidget(QLabel("Upload From:"), row, 2)
        self.from_date = QDateEdit()
        self.from_date.setCalendarPopup(True)
        self.from_date.setDate(QDate.currentDate().addMonths(-1))
        filter_grid.addWidget(self.from_date, row, 3)

        filter_grid.addWidget(QLabel("Upload To:"), row, 4)
        self.to_date = QDateEdit()
        self.to_date.setCalendarPopup(True)
        self.to_date.setDate(QDate.currentDate())
        filter_grid.addWidget(self.to_date, row, 5)

        row += 1

        # تاریخ جلالی
        filter_grid.addWidget(QLabel("Jalali From:"), row, 0)
        self.jalali_from_edit = QLineEdit()
        self.jalali_from_edit.setPlaceholderText("1404-01-01")
        filter_grid.addWidget(self.jalali_from_edit, row, 1)

        filter_grid.addWidget(QLabel("Jalali To:"), row, 2)
        self.jalali_to_edit = QLineEdit()
        self.jalali_to_edit.setPlaceholderText("1404-12-29")
        filter_grid.addWidget(self.jalali_to_edit, row, 3)

        # وضعیت
        filter_grid.addWidget(QLabel("Status:"), row, 4)
        self.status_combo = QComboBox()
        self.status_combo.addItems(["All", "Active", "Archived"])
        filter_grid.addWidget(self.status_combo, row, 5)

        row += 1

        # دکمه جستجو
        search_btn = QPushButton("Search")
        search_btn.clicked.connect(self.load_filtered_files)
        filter_grid.addWidget(search_btn, row, 0, 1, 6)

        filter_group.setLayout(filter_grid)
        layout.addWidget(filter_group)

        # اعتبارسنجی تاریخ جلالی
        jalali_validator = QRegularExpressionValidator(QRegularExpression(r"\d{4}-\d{2}-\d{2}"))
        self.jalali_from_edit.setValidator(jalali_validator)
        self.jalali_to_edit.setValidator(jalali_validator)

        # جدول نتایج
        self.search_table = QTableWidget()
        self.search_table.setColumnCount(8)
        self.search_table.setHorizontalHeaderLabels([
            "ID", "Jalali Date", "File Name", "Uploaded By", "Device", "Upload Date", "Status", "Contracts"
        ])
        self.search_table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.search_table.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOn)
        self.search_table.setHorizontalScrollMode(QTableWidget.ScrollMode.ScrollPerPixel)
        self.search_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        self.search_table.horizontalHeader().setStretchLastSection(False)
        self.search_table.setWordWrap(False)
        self.search_table.setColumnHidden(0, True)  # پنهان کردن ID

        scroll_area = QScrollArea()
        scroll_area.setWidget(self.search_table)
        scroll_area.setWidgetResizable(True)
        layout.addWidget(scroll_area)

        # دکمه‌های CRUD
        btn_layout = QHBoxLayout()
        edit_btn = QPushButton("Edit")
        edit_btn.clicked.connect(self.edit_selected_file)
        # delete_btn = QPushButton("Delete")
        # delete_btn.clicked.connect(self.delete_selected_file)
        refresh_btn = QPushButton("Refresh")
        refresh_btn.clicked.connect(self.load_filtered_files)
        btn_layout.addStretch()
        btn_layout.addWidget(edit_btn)
        # btn_layout.addWidget(delete_btn)
        btn_layout.addWidget(refresh_btn)
        layout.addLayout(btn_layout)

        # بارگذاری اولیه
        self.load_filtered_files()

    def select_file(self):
        file_path, _ = QFileDialog.getOpenFileName(
            self, "Select Data File", "", "Data Files (*.xlsx *.xls *.csv *.rep)"
        )
        if file_path:
            self.selected_file = file_path
            self.file_label.setText(os.path.basename(file_path))
            self.load_and_display_contracts(file_path)

    def load_and_display_contracts(self, file_path):
        try:
            self.progress_dialog = QProgressDialog("Analyzing file...", "Cancel", 0, 100, self)
            self.progress_dialog.setWindowTitle("Processing")
            self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
            self.progress_dialog.setMinimumDuration(0)
            self.progress_dialog.show()

            self.temp_worker = FileLoaderThread(file_path, self)
            self.temp_worker.progress.connect(self.update_temp_progress)
            self.temp_worker.finished.connect(lambda df, _: self.on_temp_file_loaded(df, file_path))
            self.temp_worker.error.connect(self.on_temp_load_error)
            self.progress_dialog.canceled.connect(self.temp_worker.cancel)
            self.temp_worker.start()

        except Exception as e:
            logger.error(f"Error: {e}")
            self.contracts_display.setText("Error loading file.")
            self.contracts_display.setStyleSheet("color: red;")

    def update_temp_progress(self, value, message):
        self.progress_dialog.setValue(value)
        self.progress_dialog.setLabelText(message)

    def on_temp_file_loaded(self, df, file_path):
        try:
            self.progress_dialog.close()

            if df is None or df.empty:
                self.contracts_display.setText("No data in file.")
                self.contracts_display.setStyleSheet("color: red;")
                return

            self.temp_df = df

            solution_col = None
            for col in df.columns:
                if 'solution' in str(col).lower() and 'label' in str(col).lower():
                    solution_col = col
                    break

            if solution_col is None:
                self.contracts_display.setText("Column 'Solution Label' not found.")
                self.contracts_display.setStyleSheet("color: orange;")
                return

            labels = df[solution_col].dropna().astype(str).unique()

            # استخراج contracts (همان کد قبلی)
            contracts = defaultdict(list)

            # الگوهای انعطاف‌پذیر با re.search (نه match)
            range_pattern = re.compile(r'(\d{4})\s*\(\s*(\d+)\s*-\s*(\d+)\s*\)', re.IGNORECASE)
            simple_pattern = re.compile(r'(\d{4})\s*-\s*(\d+)', re.IGNORECASE)
            contract_only_pattern = re.compile(r'^(\d{4})$')

            for raw_label in labels:
                label = str(raw_label).strip()
                if not label:
                    continue

                # 1. تاریخ‌ها را رد کن (مثل 2025-01-01)
                if re.match(r'^\d{4}-\d{2}-\d{2}$', label):
                    continue

                # 2. الگوی دامنه: 1666(01-05) — حتی با فاصله یا متن اطراف
                range_match = range_pattern.search(label)
                if range_match:
                    contract_num = range_match.group(1)
                    start = int(range_match.group(2))
                    end = int(range_match.group(3))
                    for i in range(start, end + 1):
                        contracts[contract_num].append(f"{contract_num}-{i:02d}")
                    continue  # این label پردازش شد

                # 3. الگوی ساده: 1666-242 — حتی با متن قبل/بعد
                simple_match = simple_pattern.search(label)
                if simple_match:
                    contract_num = simple_match.group(1)
                    variety = simple_match.group(2).zfill(2)  # اطمینان از دو رقمی بودن
                    contracts[contract_num].append(f"{contract_num}-{variety}")
                    continue

                # 4. فقط شماره قرارداد: 1666
                if contract_only_pattern.match(label):
                    contract_num = label
                    contracts[contract_num] = []  # بدون واریته
                    continue

                # اگر هیچ‌کدام نبود، نادیده بگیر

            # نمایش contracts
            display_lines = []
            for contract, varieties in sorted(contracts.items(), key=lambda x: x[0]):
                if varieties:
                    varieties = sorted(set(varieties))  # حذف تکراری + مرتب‌سازی
                    varieties_str = ', '.join(varieties)
                    display_lines.append(f"{contract} with varieties: {varieties_str}")
                else:
                    display_lines.append(f"{contract}")

            if not display_lines:
                self.contracts_text = "No valid contracts found in 'Solution Label'."
                self.contracts_display.setText(self.contracts_text)
                self.contracts_display.setStyleSheet("color: orange;")
            else:
                self.contracts_text = "\n".join(display_lines)
                self.contracts_display.setText(self.contracts_text)
                self.contracts_display.setStyleSheet("color: green; font-family: Consolas; font-size: 10pt;")

            # استخراج CRMs
            crm_pattern = re.compile(CRM_PATTERN)
            crms = set()

            for raw_label in labels:
                label = str(raw_label).strip()
                if not label:
                    continue
                match = crm_pattern.search(label)
                if match:
                    crms.add(match.group(0).strip())  # کل مطابقت را اضافه کن (با پیشوند اگر باشد)

            self.valid_crm_labels = crms

            # نمایش CRMs
            if not crms:
                self.crms_text = "No valid CRMs found in 'Solution Label'."
                self.crms_display.setText(self.crms_text)
                self.crms_display.setStyleSheet("color: orange;")
            else:
                self.crms_text = "\n".join(sorted(crms))
                self.crms_display.setText(self.crms_text)
                self.crms_display.setStyleSheet("color: green; font-family: Consolas; font-size: 10pt;")

            # استخراج Blanks
            blank_pattern = re.compile(BLANK_PATTERN, re.IGNORECASE)
            blanks = set()

            for raw_label in labels:
                label = str(raw_label).strip()
                if not label:
                    continue
                if blank_pattern.search(label):
                    blanks.add(label.strip())  # کل label را اضافه کن

            self.valid_blank_labels = blanks

            # نمایش Blanks
            if not blanks:
                self.blanks_text = "No valid Blanks found in 'Solution Label'."
                self.blanks_display.setText(self.blanks_text)
                self.blanks_display.setStyleSheet("color: orange;")
            else:
                self.blanks_text = "\n".join(sorted(blanks))
                self.blanks_display.setText(self.blanks_text)
                self.blanks_display.setStyleSheet("color: green; font-family: Consolas; font-size: 10pt;")

        except Exception as e:
            logger.error(f"Error extracting contracts/CRMs/Blanks: {e}")
            self.contracts_display.setText(f"Error: {str(e)}")
            self.contracts_display.setStyleSheet("color: red;")

    def on_temp_load_error(self, message):
        self.progress_dialog.close()
        logger.error(f"Temp file load failed: {message}")
        self.contracts_display.setText(f"Error: {message}")
        self.contracts_display.setStyleSheet("color: red;")

    def upload_file(self):
        if not self.selected_file:
            QMessageBox.warning(self, "No File", "Please select a file first.")
            return
        data = self.get_data()
        if data:
            self.parent().process_upload(data)
            self.load_filtered_files()  # به‌روزرسانی لیست پس از آپلود

    def get_data(self):
        if not self.selected_file:
            return None
        device_id = self.devices[self.device_combo.currentIndex()][0] if self.devices else None
        device_name = self.device_combo.currentText()
        return {
            "file_path": self.selected_file,
            "device_id": device_id,
            "device_name": device_name,
            "file_type": self.file_type.currentText(),
            "description": self.description.text().strip(),
            "contracts": self.contracts_text,
            "df": self.temp_df,
            "crm_labels": self.valid_crm_labels,
            "blank_labels": self.valid_blank_labels
        }

    def load_filtered_files(self):
        """بارگذاری فایل‌های آپلود شده با تمام فیلترهای اعمال شده (میلادی + جلالی + قرارداد + وضعیت)"""
        try:
            # دریافت داده‌ها با فیلترهای اصلی از دیتابیس
            df = get_uploaded_files_for_operator(self.user_id)

            if df.empty:
                self.search_table.setRowCount(0)
                logger.info("No files found for this user.")
                return

            # تبدیل به لیست برای فیلترهای اضافی
            records = df.to_records(index=False)

            # فیلترهای اضافی که در دیتابیس انجام نمی‌شوند (تاریخ جلالی)
            jalali_from = self.jalali_from_edit.text().strip()
            jalali_to = self.jalali_to_edit.text().strip()
            has_jalali_filter = bool(jalali_from or jalali_to)

            # فیلتر قرارداد (در صورت نیاز دوباره اعمال شود — اگر در query اعمال نشده)
            contract_query = self.contract_search.text().strip().lower()

            # فیلتر وضعیت (در صورت نیاز دوباره — اما قبلاً در query اعمال شده)
            status = self.status_combo.currentText()

            filtered_records = []
            for rec in records:
                file_id, original_filename, full_name, device_name, created_at, is_archived, contracts = rec

                # فیلتر وضعیت (دوباره برای اطمینان)
                if status == "Active" and is_archived:
                    continue
                if status == "Archived" and not is_archived:
                    continue

                # فیلتر قرارداد
                if contract_query and contracts and contract_query not in str(contracts).lower():
                    continue

                # فیلتر تاریخ جلالی از نام فایل
                jalali_date, _ = self.parent().parse_filename(original_filename)

                if has_jalali_filter and jalali_date:
                    if jalali_from and jalali_date < jalali_from:
                        continue
                    if jalali_to and jalali_date > jalali_to:
                        continue
                elif has_jalali_filter and not jalali_date:
                    continue  # اگر تاریخ جلالی نداشته باشد و فیلتر فعال باشد، حذف شود

                filtered_records.append(rec)

            # نمایش در جدول
            self.search_table.setRowCount(len(filtered_records))

            header = self.search_table.horizontalHeader()
            header.setSectionResizeMode(1, QHeaderView.ResizeMode.ResizeToContents)
            header.setSectionResizeMode(2, QHeaderView.ResizeMode.Interactive)
            header.setSectionResizeMode(7, QHeaderView.ResizeMode.ResizeToContents)
            header.resizeSection(7, 350)  # ستون قراردادها

            for i, row in enumerate(filtered_records):
                file_id, original_filename, full_name, device_name, created_at, is_archived, contracts = row
                jalali_date, clean_name = self.parent().parse_filename(original_filename)
                archive_status = "Archived" if is_archived else "Active"

                self.search_table.setItem(i, 0, QTableWidgetItem(str(file_id)))
                self.search_table.setItem(i, 1, QTableWidgetItem(jalali_date or "N/A"))
                self.search_table.setItem(i, 2, QTableWidgetItem(clean_name))
                self.search_table.setItem(i, 3, QTableWidgetItem(full_name or "Unknown"))
                self.search_table.setItem(i, 4, QTableWidgetItem(device_name or "Unknown"))
                self.search_table.setItem(i, 5, QTableWidgetItem(created_at[:19] if created_at else "N/A"))
                self.search_table.setItem(i, 6, QTableWidgetItem(archive_status))
                self.search_table.setItem(i, 7, QTableWidgetItem(contracts or ""))

            logger.info(f"Displayed {len(filtered_records)} files after all filters applied.")

        except Exception as e:
            logger.error(f"Failed to load filtered files: {e}")
            QMessageBox.critical(self, "Error", f"Failed to load files:\n{e}")

    def edit_selected_file(self):
        selected_row = self.search_table.currentRow()
        if selected_row < 0:
            QMessageBox.warning(self, "No Selection", "Please select a file to edit.")
            return

        file_id = int(self.search_table.item(selected_row, 0).text())
        self.open_edit_dialog(file_id)

    def open_edit_dialog(self, file_id):
        try:
            result = get_file_metadata_for_edit(file_id)
            if not result:
                QMessageBox.warning(self, "Error", "File not found.")
                return

            description, file_type, device_id, device_name = result

            edit_dialog = QDialog(self)
            edit_dialog.setWindowTitle("Edit File Metadata")
            edit_layout = QFormLayout(edit_dialog)

            desc_edit = QLineEdit(description or "")
            edit_layout.addRow("Description:", desc_edit)

            type_combo = QComboBox()
            type_combo.addItems(["oes 4ac", "oes fire", "mass"])
            type_combo.setCurrentText(file_type)
            edit_layout.addRow("File Type:", type_combo)

            device_combo = QComboBox()
            device_combo.addItems([d[1] for d in self.devices])
            device_combo.setCurrentText(device_name)
            edit_layout.addRow("Device:", device_combo)

            buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Save | QDialogButtonBox.StandardButton.Cancel)
            buttons.accepted.connect(lambda: self.save_edit(file_id, desc_edit.text(), type_combo.currentText(), self.devices[device_combo.currentIndex()][0], edit_dialog))
            buttons.rejected.connect(edit_dialog.reject)
            edit_layout.addRow(buttons)

            edit_dialog.exec()

        except Exception as e:
            logger.error(f"Error opening edit dialog: {e}")
            QMessageBox.warning(self, "Error", f"Failed to open edit dialog: {e}")

    def save_edit(self, file_id, new_desc, new_type, new_device_id, dialog):
        if update_file_metadata(file_id, new_desc, new_type, new_device_id):
            QMessageBox.information(self, "Success", "File metadata updated.")
            self.load_filtered_files()
            dialog.accept()
        else:
            QMessageBox.critical(self, "Error", "Failed to update metadata.")

    def delete_selected_file(self):
        selected_row = self.search_table.currentRow()
        if selected_row < 0:
            QMessageBox.warning(self, "No Selection", "Please select a file to delete.")
            return

        file_id = int(self.search_table.item(selected_row, 0).text())
        file_name = self.search_table.item(selected_row, 2).text()  # clean_name

        confirm = QMessageBox.question(
            self,
            "Confirm Delete",
            f"Are you sure you want to delete '<b>{file_name}</b>'?\n\n"
            f"This will permanently remove:\n"
            f"• The file record\n"
            f"• All associated CRM data\n"
            f"• All associated element measurements\n\n"
            f"This action cannot be undone.",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No
        )
        if confirm == QMessageBox.StandardButton.Yes:
            self.perform_delete(file_id, file_name)

    def perform_delete(self, file_id: int, file_name: str):
        success, message = delete_file_and_related_data(file_id, file_name)

        if success:
            QMessageBox.information(self, "Success", message)
            self.load_filtered_files()  # رفرش لیست
        else:
            QMessageBox.critical(self, "Error", message)

# --- FileTab با به‌روزرسانی Result تب ---
class FileTab(QWidget):
    def __init__(self, main_window):
        super().__init__()
        self.main_window = main_window
        self.init_ui()

    def parse_filename(self, filename):
        """استخراج تاریخ جلالی و نام تمیز از نام فایل"""
        filename = filename.strip()
        match = re.match(r'(\d{4}-\d{2}-\d{2})\s*(.+)', filename, re.IGNORECASE)
        if match:
            return match.group(1), match.group(2).strip()
        match_no_space = re.match(r'(\d{4}-\d{2}-\d{2})(.+)', filename, re.IGNORECASE)
        if match_no_space:
            return match_no_space.group(1), match_no_space.group(2).strip()
        return None, filename
    
    def init_ui(self):
        layout = QVBoxLayout(self)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

        title = QLabel("File Management")
        title.setStyleSheet("font: bold 24px; color: #2c3e50;")
        layout.addWidget(title, alignment=Qt.AlignmentFlag.AlignCenter)

        self.file_btn = QPushButton("Upload Raw Data File")
        self.file_btn.setFixedSize(300, 50)
        self.file_btn.setStyleSheet("""
            QPushButton {
                background: #3498db; color: white; font: bold 16px;
                border-radius: 10px; padding: 10px;
            }
            QPushButton:hover { background: #2980b9; }
        """)
        self.file_btn.clicked.connect(self.on_file_button_clicked)

        # جدول فایل‌ها
        self.files_table = QTableWidget()
        self.files_table.setColumnCount(7)
        self.files_table.setHorizontalHeaderLabels([
            "ID", "Jalali Date", "File Name", "Uploaded By", "Device", "Upload Date", "Status"
        ])
        self.files_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.files_table.hide()

        self.refresh_btn = QPushButton("Refresh List")
        self.refresh_btn.setFixedSize(300, 40)
        self.refresh_btn.clicked.connect(self.load_uploaded_files_list)
        self.refresh_btn.hide()

        layout.addWidget(self.file_btn, alignment=Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self.refresh_btn, alignment=Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self.files_table)
        layout.addStretch()

        self.apply_role_restrictions()

    def apply_role_restrictions(self):
        role = self.main_window.user_role
        if role == "report_manager":
            self.file_btn.setText("Select Raw Data File")
            self.refresh_btn.show()
            self.files_table.show()
            self.load_uploaded_files_list()
        elif role == "device_operator":
            self.file_btn.setText("Manage Files (Upload & CRUD)")
            self.refresh_btn.hide()
            self.files_table.hide()
        else:
            self.file_btn.hide()
            self.refresh_btn.hide()
            self.files_table.hide()

    def on_file_button_clicked(self):
        role = self.main_window.user_role
        if role == "device_operator":
            self.show_upload_dialog()
        elif role == "report_manager":
            self.open_existing_file()

    def show_upload_dialog(self):
        devices = get_all_devices()
        if not devices:
            QMessageBox.warning(self, "No Devices", "No devices are registered. Contact admin.")
            return

        dialog = UploadDialog(self, devices, self.main_window.user_id_from_username)
        dialog.exec()

    def process_upload(self, upload_data):
        file_path = upload_data["file_path"]
        df = upload_data.get("df")
        crm_labels = upload_data.get("crm_labels", set())
        blank_labels = upload_data.get("blank_labels", set())
        file_type = upload_data["file_type"]

        if df is None or df.empty:
            QMessageBox.critical(self, "Empty File", "The selected file is empty or could not be read.")
            return

        try:
            # 1. ذخیره متادیتا فایل در جدول uploaded_files
            self.save_to_db(upload_data, file_path)

            # 2. مسیر دیتابیس
            db_path = resource_path(CRM_DATA_PATH)

            # 3. ستون Solution Label
            solution_col = 'Solution Label'
            if solution_col not in df.columns:
                raise ValueError(f"Column '{solution_col}' not found in file.")

            # 4. فیلتر ردیف‌های حاوی CRM یا Blank با regex
            patterns = []

            # --- CRM: استخراج اعداد معتبر از labelهای شناسایی‌شده ---
            for label in crm_labels:
                m = re.search(r'\b(\d{3})\b', str(label))
                if m and m.group(1) in CRM_IDS:
                    patterns.append(rf'\b{m.group(1)}\b')

            # --- Blank: الگوی ساده ---
            if blank_labels:
                patterns.append(BLANK_PATTERN)

            df_crm_raw = pd.DataFrame()
            if patterns:
                print("omid patt :",patterns)
                combined_regex = re.compile('|'.join(patterns))
                mask = df[solution_col].astype(str).str.contains(combined_regex, na=False, regex=True)
                df_crm_raw = df[mask].copy()
                logger.debug(f"CRM/Blank regex: {combined_regex.pattern}")
                logger.debug(f"Matched {len(df_crm_raw)} rows with CRM/Blank patterns.")
            else:
                logger.info("No CRM or Blank patterns to match. Skipping crm_data import.")

            if df_crm_raw.empty:
                logger.info("No CRM/Blank rows found in data. Skipping import.")
            else:
                # 5. چک ستون‌های ضروری
                required_cols = ['Element', 'Corr Con']
                missing = [col for col in required_cols if col not in df_crm_raw.columns]
                if missing:
                    raise ValueError(f"Missing required columns in CRM/Blank data: {missing}")

                # 6. استخراج داده‌ها
                df_crm = df_crm_raw[[solution_col, 'Element', 'Corr Con']].copy()
                df_crm = df_crm.rename(columns={
                    solution_col: 'solution_label',
                    'Element': 'element',
                    'Corr Con': 'value'
                })

                # تبدیل مقدار به عدد
                df_crm['value'] = pd.to_numeric(df_crm['value'], errors='coerce')
                df_crm = df_crm.dropna(subset=['value', 'element'])
                df_crm = df_crm[df_crm['element'].str.strip() != '']

                if df_crm.empty:
                    logger.warning("No valid numeric values in 'Corr Con' for CRM/Blank rows.")
                else:
                    # 7. تعیین crm_id با تابع قوی
                    def get_crm_id(label):
                        label_str = str(label).strip().upper()
                        if re.search(BLANK_PATTERN, label_str):
                            return "BLANK"
                        match = re.search(r'\b(\d{3})\b', label_str)
                        if match:
                            num = match.group(1)
                            if num in CRM_IDS:
                                return num
                        return None

                    df_crm['crm_id'] = df_crm['solution_label'].apply(get_crm_id)
                    df_crm = df_crm.dropna(subset=['crm_id'])

                    if df_crm.empty:
                        logger.warning("No valid CRM IDs after normalization. All rows dropped.")
                    else:
                        # 8. اضافه کردن ستون‌های باقی‌مانده
                        jalali_date, _ = self.parse_filename(os.path.basename(file_path))
                        df_crm['file_name'] = os.path.basename(file_path)
                        df_crm['folder_name'] = file_type
                        df_crm['date'] = jalali_date or ""

                        # 9. ترتیب نهایی ستون‌ها
                        df_crm_final = df_crm[[
                            'crm_id', 'solution_label', 'element', 'value',
                            'file_name', 'folder_name', 'date'
                        ]]

                        # 10. ذخیره در دیتابیس
                        self.safe_import_crm_data(df_crm_final, db_path)
                        logger.info(f"Successfully imported {len(df_crm_final)} CRM/Blank records into crm_data.")

            # --- اضافه کردن: ذخیره تمام عناصر در excels_elements.db ---
            self.save_all_elements_to_db(df, file_path)

            # 11. بارگذاری داده در UI
            self.main_window.reset_app_state()
            self.main_window.data = df.copy()
            self.main_window.init_data = df.copy()
            self.main_window.file_path = file_path

            _, clean_name = self.parse_filename(os.path.basename(file_path))
            self.main_window.file_path_label.setText(f"File: {clean_name}")
            self.main_window.setWindowTitle(f"RASF Data Processor - {clean_name}")

            self.main_window.notify_data_changed()

            # به‌روزرسانی تب‌ها
            if hasattr(self.main_window, 'elements_tab') and self.main_window.elements_tab:
                self.main_window.elements_tab.process_blk_elements()

            if hasattr(self.main_window, 'pivot_tab') and self.main_window.pivot_tab:
                try:
                    PivotCreator(self.main_window.pivot_tab).create_pivot()
                except Exception as e:
                    logger.warning(f"Could not update pivot: {e}")

            if hasattr(self.main_window, 'results') and hasattr(self.main_window.results, 'show_processed_data'):
                try:
                    self.main_window.results.show_processed_data()
                except Exception as e:
                    logger.warning(f"Could not update Result tab: {e}")

            QMessageBox.information(self, "Success", "File uploaded and CRM/Blank data imported successfully.")

        except Exception as e:
            logger.error(f"Upload failed: {e}", exc_info=True)
            QMessageBox.critical(self, "Error", f"Failed to process file:\n{str(e)}")

    def save_all_elements_to_db(self, df, file_path):
        """ذخیره تمام solution_label و عناصر در excels_elements.db با ساختار pivot شده (wide format)"""
        try:
            # چک ستون‌های ضروری
            solution_col = 'Solution Label'
            if solution_col not in df.columns or 'Element' not in df.columns or 'Corr Con' not in df.columns:
                raise ValueError("Missing required columns for elements data.")

            # استخراج داده‌ها
            df_elements = df[[solution_col, 'Element', 'Corr Con']].copy()
            df_elements = df_elements.rename(columns={
                solution_col: 'sample_id',
                'Element': 'element',
                'Corr Con': 'value'
            })
            df_elements['value'] = pd.to_numeric(df_elements['value'], errors='coerce')
            df_elements = df_elements.dropna(subset=['value', 'element', 'sample_id'])

            # pivot به wide format با مدیریت تکراری‌ها (میانگین)
            df_pivoted = df_elements.pivot_table(index='sample_id', columns='element', values='value', aggfunc='mean').reset_index()
            df_pivoted['file_name'] = os.path.basename(file_path)

            success = save_pivoted_elements_data(df_pivoted, os.path.basename(file_path))
            if not success:
                logger.warning("Failed to save pivoted elements data.")

        except Exception as e:
            print(e)
                

    def safe_import_crm_data(self, df_crm_final, db_path):
        success = safe_import_crm_data(df_crm_final)
        if not success:
            raise Exception("CRM data import failed")
                

    def save_to_db(self, upload_data, file_path):
        clean_name = os.path.basename(file_path).replace(" ", "_")
        success = save_uploaded_file_metadata(
            original_filename=os.path.basename(file_path),
            clean_filename=clean_name,
            file_path=file_path,
            device_id=upload_data["device_id"],
            file_type=upload_data["file_type"],
            description=upload_data["description"],
            contracts=upload_data["contracts"],
            uploaded_by=self.main_window.user_id_from_username
        )
        if not success:
            QMessageBox.critical(self, "DB Error", "Failed to save file metadata.")

    def open_existing_file(self):
        try:
            dialog = QDialog(self)
            dialog.setWindowTitle("Select Existing File")
            dialog.setFixedSize(1200, 700)  # اندازه قبلی
            layout = QVBoxLayout(dialog)

            # --- گروه فیلترها (دقیقاً مثل قبل، فقط بدون تاریخ آپلود میلادی) ---
            filter_group = QGroupBox("Search Filters")
            filter_grid = QGridLayout()
            row = 0

            # قرارداد
            filter_grid.addWidget(QLabel("Contract:"), row, 0)
            self.contract_search = QLineEdit()
            self.contract_search.setPlaceholderText("Enter contract number...")
            filter_grid.addWidget(self.contract_search, row, 1)

            # تاریخ جلالی
            filter_grid.addWidget(QLabel("From:"), row, 2)
            self.jalali_from_edit = QLineEdit()
            self.jalali_from_edit.setPlaceholderText("1404-01-01")
            filter_grid.addWidget(self.jalali_from_edit, row, 3)

            filter_grid.addWidget(QLabel("To:"), row, 4)
            self.jalali_to_edit = QLineEdit()
            self.jalali_to_edit.setPlaceholderText("1404-12-29")
            filter_grid.addWidget(self.jalali_to_edit, row, 5)

            # وضعیت
            filter_grid.addWidget(QLabel("Status:"), row, 6)
            self.status_combo = QComboBox()
            self.status_combo.addItems(["All", "Active", "Archived"])
            filter_grid.addWidget(self.status_combo, row, 7)


            filter_grid.addWidget(QLabel("File Type:"), row, 8)
            self.file_type_combo = QComboBox()
            self.file_type_combo.addItems(["All", "oes 4ac", "oes fire", "mass"])
            filter_grid.addWidget(self.file_type_combo, row, 9)
            # دکمه جستجو (همون جای قبلی)
            search_btn = QPushButton("Search")
            search_btn.clicked.connect(lambda: self.load_filtered_files(dialog))
            filter_grid.addWidget(search_btn, row, 10)  # همون جای قبلی

            filter_group.setLayout(filter_grid)
            layout.addWidget(filter_group)

            # --- جدول نتایج (اضافه کردن ستون Upload Date (Jalali)) ---
            self.search_table = QTableWidget()
            self.search_table.setColumnCount(9)  # اضافه کردن یک ستون برای تاریخ آپلود جلالی
            self.search_table.setHorizontalHeaderLabels([
                "ID", "Date", "File Name", "Uploaded By","Upload Date", "Device", "File Type", "Status", "Contracts"
            ])
            self.search_table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
            self.search_table.setSelectionMode(QAbstractItemView.SelectionMode.ExtendedSelection)
            self.search_table.setColumnHidden(0, True)
            self.search_table.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOn)
            self.search_table.setHorizontalScrollMode(QTableWidget.ScrollMode.ScrollPerPixel)
            self.search_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
            self.search_table.horizontalHeader().setStretchLastSection(False)
            self.search_table.setWordWrap(False)
            layout.addWidget(self.search_table)

            # --- دکمه‌های پایین (بدون تغییر) ---
            btn_layout = QHBoxLayout()
            btn_layout.addStretch()

            pivoted_btn = QPushButton("Open Pivoted File")
            pivoted_btn.clicked.connect(lambda: self.load_pivoted_file())
            btn_layout.addWidget(pivoted_btn)

            select_btn = QPushButton("Select")
            select_btn.clicked.connect(lambda: self.select_and_load_file(dialog))
            btn_layout.addWidget(select_btn)

            cancel_btn = QPushButton("Cancel")
            cancel_btn.clicked.connect(dialog.reject)
            btn_layout.addWidget(cancel_btn)

            layout.addLayout(btn_layout)

            # بارگذاری اولیه
            self.load_filtered_files(dialog)
            dialog.exec()

        except Exception as e:
            logger.error(f"Error opening existing files dialog: {e}")
            QMessageBox.warning(self, "Error", f"Failed to open dialog: {e}")

    def load_pivoted_file(self):
        file_path, _ = QFileDialog.getOpenFileName(
            self, "Open Pivoted File", "", "Data Files (*.xlsx *.xls *.csv)"
        )
        if not file_path:
            return

        try:
            progress = QProgressDialog("Loading pivoted file...", "Cancel", 0, 100, self)
            progress.setWindowTitle("Loading...")
            progress.setWindowModality(Qt.WindowModality.WindowModal)
            progress.setMinimumDuration(0)
            progress.show()

            # مهم: is_pivoted=True
            worker = FileLoaderThread(file_path, self, is_pivoted=True)

            def on_progress(v, msg):
                progress.setValue(v)
                progress.setLabelText(msg)
                if progress.wasCanceled():
                    worker.cancel()

            def on_finished(df, path):
                try:
                    if df is None or df.empty:
                        QMessageBox.warning(self, "Invalid File", "Pivoted file is empty or invalid.")
                        return

                    # --- ۱. آپدیت داده اصلی از main_window ---
                    self.main_window.data = df.copy()                     # این خط درست
                    self.main_window.file_path = path                     # برای عنوان

                    # --- ۲. تنظیم data_type در ResultsFrame ---
                    if hasattr(self.main_window, 'results') and self.main_window.results:
                        self.main_window.results.data_type = 'wide'       # این خط حیاتی
                        self.main_window.results.last_filtered_data = df.copy()
                        self.main_window.results.show_processed_data()    # نمایش مستقیم

                    # --- ۳. آپدیت PivotTab ---
                    if hasattr(self.main_window, 'pivot_tab') and self.main_window.pivot_tab:
                        self.main_window.pivot_tab.pivot_data = df.copy()
                        self.main_window.pivot_tab.update_pivot_display()

                    # --- ۴. آپدیت عنوان ---
                    _, clean_name = self.parse_filename(os.path.basename(path))
                    self.main_window.file_path_label.setText(f"Pivoted: {clean_name}")
                    self.main_window.setWindowTitle(f"RASF Data Processor - Pivoted: {clean_name}")

                    progress.setValue(100)
                    QMessageBox.information(self, "Success", f"Pivoted file loaded:\n{clean_name}")

                except Exception as e:
                    logger.error(f"Pivoted file load failed: {e}")
                    QMessageBox.critical(self, "Error", f"Failed to load pivoted file:\n{e}")
                finally:
                    progress.close()

            def on_error(msg):
                progress.close()
                QMessageBox.critical(self, "Error", f"Failed to read file:\n{msg}")

            worker.progress.connect(on_progress)
            worker.finished.connect(on_finished)
            worker.error.connect(on_error)
            worker.start()

        except Exception as e:
            logger.error(f"Unexpected error in load_pivoted_file: {e}")
            QMessageBox.critical(self, "Error", f"Unexpected error:\n{e}")

    def load_filtered_files(self, dialog):
        """بارگذاری فایل‌های آپلود شده برای report_manager با تمام فیلترها (نوع فایل، وضعیت، قرارداد، تاریخ جلالی)"""
        try:
            # دریافت داده‌ها با فیلترهای اصلی از دیتابیس
            df = get_all_uploaded_files_with_filters(
                contract=self.contract_search.text().strip(),
                file_type=self.file_type_combo.currentText(),
                status=self.status_combo.currentText()
            )

            if df.empty:
                self.search_table.setRowCount(0)
                logger.info("No files found matching the filters.")
                return

            # تبدیل به records برای پردازش آسان‌تر
            records = df.to_records(index=False)

            # فیلتر اضافی تاریخ جلالی (از نام فایل)
            jalali_from = self.jalali_from_edit.text().strip()
            jalali_to = self.jalali_to_edit.text().strip()
            has_jalali_filter = bool(jalali_from or jalali_to)

            filtered_records = []
            for rec in records:
                (file_id, original_filename, full_name, device_name, file_type,
                 is_archived, contracts, created_at) = rec

                jalali_date, _ = self.parse_filename(original_filename)
                if not jalali_date:
                    jalali_date = "0000-00-00"  # فایل‌های بدون تاریخ همیشه نمایش داده شوند

                include = True
                if has_jalali_filter:
                    if jalali_from and jalali_date < jalali_from:
                        include = False
                    if jalali_to and jalali_date > jalali_to:
                        include = False

                if include:
                    filtered_records.append(rec)

            # نمایش نتایج در جدول
            self.search_table.setRowCount(len(filtered_records))

            header = self.search_table.horizontalHeader()
            header.setSectionResizeMode(1, QHeaderView.ResizeMode.ResizeToContents)   # Date (Jalali from filename)
            header.setSectionResizeMode(2, QHeaderView.ResizeMode.Interactive)       # File Name
            header.setSectionResizeMode(7, QHeaderView.ResizeMode.ResizeToContents)  # Status
            header.setSectionResizeMode(8, QHeaderView.ResizeMode.ResizeToContents)  # Contracts
            header.resizeSection(7, 350)  # قراردادها
            header.resizeSection(8, 120)  # تاریخ آپلود جلالی

            for i, row in enumerate(filtered_records):
                file_id, original_filename, full_name, device_name, file_type, \
                is_archived, contracts, created_at = row

                jalali_date, clean_name = self.parse_filename(original_filename)
                status_text = "Archived" if is_archived else "Active"
                upload_date_jalali = miladi_to_jalali(created_at) if created_at else "N/A"

                self.search_table.setItem(i, 0, QTableWidgetItem(str(file_id)))
                self.search_table.setItem(i, 1, QTableWidgetItem(jalali_date or "N/A"))          # تاریخ جلالی از نام فایل
                self.search_table.setItem(i, 2, QTableWidgetItem(clean_name))
                self.search_table.setItem(i, 3, QTableWidgetItem(full_name or "Unknown"))
                self.search_table.setItem(i, 4, QTableWidgetItem(upload_date_jalali))           # تاریخ آپلود به جلالی
                self.search_table.setItem(i, 5, QTableWidgetItem(device_name or "Unknown"))
                self.search_table.setItem(i, 6, QTableWidgetItem(file_type or "Unknown"))
                self.search_table.setItem(i, 7, QTableWidgetItem(status_text))
                self.search_table.setItem(i, 8, QTableWidgetItem(contracts or ""))

            logger.info(f"Displayed {len(filtered_records)} files for report_manager after applying all filters.")

        except Exception as e:
            logger.error(f"load_filtered_files error (report_manager): {e}")
            QMessageBox.critical(dialog, "خطا", f"بارگذاری فایل‌ها ناموفق:\n{e}")

    def select_and_load_file(self, dialog):
        selected_indexes = self.search_table.selectionModel().selectedRows()
        if not selected_indexes:
            QMessageBox.warning(dialog, "No Selection", "Please select at least one file.")
            return

        file_paths = []
        clean_names = []
        for index in selected_indexes:
            row = index.row()
            file_id = int(self.search_table.item(row, 0).text())
            clean_name = self.search_table.item(row, 2).text()
            try:
                conn = get_db_connection()
                cur = conn.cursor()
                cur.execute("SELECT file_path FROM uploaded_files WHERE id = ?", (file_id,))
                result = cur.fetchone()
                
                if result and os.path.exists(result[0]):
                    file_paths.append(result[0])
                    clean_names.append(clean_name)
                else:
                    QMessageBox.warning(self, "File Error", f"File for {clean_name} not found or inaccessible. Skipping.")
            except Exception as e:
                logger.error(f"Error fetching file path for ID {file_id}: {e}")
                QMessageBox.warning(self, "Error", f"Failed to fetch file path for {clean_name}: {e}. Skipping.")

        if not file_paths:
            return

        dialog.accept()  # بستن دیالوگ انتخاب
        self.chain_load(file_paths, clean_names)

    def chain_load(self, file_paths, clean_names, index=0):
        if index >= len(file_paths):
            # به‌روزرسانی نهایی UI پس از لود همه فایل‌ها
            try:
                self.main_window.notify_data_changed()

                if hasattr(self.main_window, 'elements_tab') and self.main_window.elements_tab:
                    self.main_window.elements_tab.process_blk_elements()

                if hasattr(self.main_window, 'pivot_tab') and self.main_window.pivot_tab:
                    PivotCreator(self.main_window.pivot_tab).create_pivot()  # پیوت نهایی

                if hasattr(self.main_window, 'results') and hasattr(self.main_window.results, 'show_processed_data'):
                    self.main_window.results.show_processed_data()

                combined_names = ' + '.join(clean_names)
                self.main_window.file_path_label.setText(f"Files: {combined_names}")
                self.main_window.setWindowTitle(f"RASF Data Processor - {combined_names}")

                QMessageBox.information(self, "Success", "All selected files loaded and concatenated successfully.")

            except Exception as e:
                logger.error(f"Final UI update failed after loading multiple files: {e}")
                QMessageBox.warning(self, "Error", f"Failed to finalize UI update: {e}")
            return

        file_path = file_paths[index]
        is_first = (index == 0)

        progress_dialog = QProgressDialog(f"Loading file {index + 1}/{len(file_paths)}: {os.path.basename(file_path)}...", "Cancel", 0, 100, self)
        progress_dialog.setWindowTitle("Processing")
        progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        progress_dialog.setMinimumDuration(0)
        progress_dialog.show()

        worker = FileLoaderThread(file_path, self)

        def on_progress(value, message):
            progress_dialog.setValue(value)
            progress_dialog.setLabelText(f"Loading file {index + 1}/{len(file_paths)}: {os.path.basename(file_path)}..."+"\n"+message )
            if progress_dialog.wasCanceled():
                worker.cancel()

        def on_finished(df, loaded_file_path):
            try:
                if df is None or not isinstance(df, pd.DataFrame):
                    raise ValueError("Loaded DataFrame is None or invalid")
                if df.empty:
                    raise ValueError("Loaded DataFrame is empty")


                _, clean_name = self.parse_filename(os.path.basename(loaded_file_path))

                # --- مرحله ۱: محاسبه تعداد سطرهای پیوت این فایل تنها ---
                original_data = self.main_window.data  # ذخیره وضعیت فعلی
                original_init_data = self.main_window.init_data

                # فقط داده این فایل رو موقتاً ست کن
                self.main_window.data = df.copy()
                self.main_window.init_data = df.copy()

                # پیوت این فایل تنها رو بساز
                pivot_creator = PivotCreator(self.main_window.pivot_tab)
                pivot_creator.create_pivot()  # هیچ چیزی return نمی‌کنه، فقط pivot_data رو پر می‌کنه

                # حالا تعداد سطرهای پیوت رو از pivot_data بگیر
                current_pivot_df = self.main_window.pivot_tab.pivot_data
                pivot_row_count = len(current_pivot_df) if current_pivot_df is not None and not current_pivot_df.empty else 0

                # fallback: اگر به هر دلیلی pivot_data ساخته نشد
                if pivot_row_count == 0 and 'Solution Label' in df.columns:
                    pivot_row_count = df[df['Type'].isin(['Samp', 'Sample'])]['Solution Label'].nunique()
                print(clean_name,pivot_row_count)
                # --- مرحله ۲: محاسبه start_pivot_row ---
                if not hasattr(self.main_window, 'file_ranges'):
                    self.main_window.file_ranges = []

                current_start = sum(fr.get('pivot_row_count', 0) for fr in self.main_window.file_ranges)

                # === CRM Detection - Your Lab's Exact Regex ===
                crm_pattern = re.compile(CRM_PATTERN)
                labels = df['Solution Label'].dropna().astype(str)
                has_crm = labels.str.contains(crm_pattern, regex=True).any()

                if len(self.main_window.file_ranges) >=1 and not has_crm :
                    prev_name=self.main_window.file_ranges[-1]["clean_name"]
                    reply = QMessageBox.question(
                        self,
                        "No CRM Detected",
                        f"<b>No CRM found</b> in:\n\n<b>{clean_name}</b>\n\n"
                        f"Append to previous file?\n→ <b>{prev_name}</b>",
                        QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                        QMessageBox.StandardButton.Yes
                    )
                    append_to_previous = (reply == QMessageBox.StandardButton.Yes)
                    if append_to_previous :
                        self.main_window.file_ranges[-1]["file_path"]+=" + "+ loaded_file_path
                        self.main_window.file_ranges[-1]["clean_name"]+=" + "+ clean_name
                        self.main_window.file_ranges[-1]["end_pivot_row"]+=pivot_row_count 
                        self.main_window.file_ranges[-1]["pivot_row_count"]+=pivot_row_count
                else:
                    # --- مرحله ۳: ذخیره در file_ranges ---
                    self.main_window.file_ranges.append({
                        "file_path": loaded_file_path,
                        "clean_name": clean_name,
                        "start_pivot_row": current_start,
                        "end_pivot_row": current_start + pivot_row_count - 1 if pivot_row_count > 0 else current_start,
                        "pivot_row_count": pivot_row_count,
                    })

                logger.debug(f"[Pivot Range] {clean_name}: {pivot_row_count} rows → "
                            f"rows {current_start}–{current_start + pivot_row_count - 1}")


                print(self.main_window.file_ranges)
                # --- مرحله ۴: برگرداندن داده اصلی + concat ---
                self.main_window.data = original_data
                self.main_window.init_data = original_init_data

                if is_first:
                    self.main_window.reset_app_state()
                    self.main_window.data = df.copy()
                    self.main_window.init_data = df.copy()
                    self.main_window.file_path = loaded_file_path
                    self.main_window.file_ranges.append({
                    "file_path": loaded_file_path,
                    "clean_name": clean_name,
                    "start_pivot_row": current_start,
                    "end_pivot_row": current_start + pivot_row_count - 1 if pivot_row_count > 0 else current_start,
                    "pivot_row_count": pivot_row_count,
                })
                else:
                    if self.main_window.data is None:
                        self.main_window.data = df.copy()
                        self.main_window.init_data = df.copy()
                    else:
                        self.main_window.data = pd.concat([self.main_window.data, df], ignore_index=True)
                        self.main_window.init_data = self.main_window.data.copy()

                progress_dialog.close()
                self.chain_load(file_paths, clean_names, index + 1)

            except Exception as e:
                progress_dialog.close()
                logger.error(f"Error processing file {os.path.basename(loaded_file_path)}: {e}")
                QMessageBox.critical(self, "خطا", f"فایل پردازش نشد:\n{e}")

        def on_error(error_message):
            progress_dialog.close()
            logger.error(f"Failed to load file {os.path.basename(file_path)}: {error_message}")
            QMessageBox.critical(self, "Error", f"Failed to load file {os.path.basename(file_path)}:\n{error_message}")

        worker.progress.connect(on_progress)
        worker.finished.connect(on_finished)
        worker.error.connect(on_error)
        worker.start()

    def load_uploaded_files_list(self):
        """بارگذاری لیست فایل‌های آپلود شده برای report_manager در جدول اصلی FileTab"""
        try:
            df = get_all_uploaded_files_for_report_manager()

            if df.empty:
                self.files_table.setRowCount(0)
                logger.info("No uploaded files found for display.")
                return

            self.files_table.setRowCount(len(df))

            header = self.files_table.horizontalHeader()
            header.setSectionResizeMode(1, QHeaderView.ResizeMode.ResizeToContents)  # Jalali Date
            header.setSectionResizeMode(2, QHeaderView.ResizeMode.Interactive)       # File Name

            for i, row in df.iterrows():
                jalali_date, clean_name = self.parse_filename(row['original_filename'])
                archive_status = "Archived" if row['is_archived'] else "Active"

                self.files_table.setItem(i, 0, QTableWidgetItem(str(row['id'])))
                self.files_table.setItem(i, 1, QTableWidgetItem(jalali_date or "N/A"))
                self.files_table.setItem(i, 2, QTableWidgetItem(clean_name))
                self.files_table.setItem(i, 3, QTableWidgetItem(row['full_name'] or "Unknown"))
                self.files_table.setItem(i, 4, QTableWidgetItem(row['name'] or "Unknown"))
                self.files_table.setItem(i, 5, QTableWidgetItem(row['created_at'][:19] if row['created_at'] else "N/A"))
                self.files_table.setItem(i, 6, QTableWidgetItem(archive_status))

            logger.info(f"Loaded {len(df)} uploaded files into main list for report_manager.")

        except Exception as e:
            logger.error(f"Failed to load uploaded files list: {e}")
            QMessageBox.critical(self, "Error", f"Failed to load files:\n{e}")