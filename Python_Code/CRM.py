import sys
import pandas as pd
import sqlite3
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QComboBox, QLabel, QTableView,
    QFrame, QScrollArea, QGridLayout, QDialog, QMessageBox, QHeaderView,
    QLineEdit, QAbstractItemView, QCheckBox
)
from PyQt6.QtCore import Qt, QAbstractTableModel, QModelIndex
from PyQt6.QtGui import QFont, QPixmap
import logging
import os
from .Common.Freeze_column import FreezeTableWidget
from db.db import get_db_connection,resource_path
from utils.var_main import LOGO_PNG_PATH 
# Setup logging
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

class CRMTableModel(QAbstractTableModel):
    """Custom model for QTableView to display CRM data efficiently."""
    def __init__(self, crm_tab, df=None, decimal_places=2):
        super().__init__()
        self.crm_tab = crm_tab
        self._df = df if df is not None else pd.DataFrame()
        self.decimal_places = decimal_places
        self.column_widths = {}

    def set_data(self, df):
        self.beginResetModel()
        self._df = df.copy()
        self.endResetModel()

    def rowCount(self, parent=QModelIndex()):
        return self._df.shape[0]

    def columnCount(self, parent=QModelIndex()):
        return self._df.shape[1]

    def data(self, index, role=Qt.ItemDataRole.DisplayRole):
        if not index.isValid() or not (0 <= index.row() < self._df.shape[0] and 0 <= index.column() < self._df.shape[1]):
            return None

        value = self._df.iloc[index.row(), index.column()]
        col_name = self._df.columns[index.column()]

        if role == Qt.ItemDataRole.DisplayRole:
            dec = self.decimal_places
            if col_name != 'CRM ID' and pd.notna(value):
                try:
                    return f"{float(value):.{dec}f}"
                except (ValueError, TypeError):
                    return "" if pd.isna(value) else str(value)
            return str(value) if pd.notna(value) else ""
        
        elif role == Qt.ItemDataRole.TextAlignmentRole:
            return Qt.AlignmentFlag.AlignLeft if col_name == 'CRM ID' else Qt.AlignmentFlag.AlignCenter

        return None

    def headerData(self, section, orientation, role=Qt.ItemDataRole.DisplayRole):
        if role == Qt.ItemDataRole.DisplayRole:
            if orientation == Qt.Orientation.Horizontal:
                return str(self._df.columns[section])
            return str(section + 1)
        return None

class CRMTab(QWidget):
    """CRMTab for managing pivoted CRM data with SQLite."""
    def __init__(self, app, parent_frame):
        super().__init__(parent_frame)
        self.app = app
        self.parent_frame = parent_frame
        self.conn = None
        self.pivot_data = None
        self.table_view = None
        self.search_var = QLineEdit()
        self.filter_var = QComboBox()
        self.decimal_places = QComboBox()
        self.our_oreas_checkbox = QCheckBox("Our OREAS")
        self.column_widths = {}
        self.sort_column = None
        self.sort_reverse = False
        self.ui_initialized = False
        self.setup_ui()
        self.init_db()
        self.load_and_display()

    def setup_ui(self):
        """Setup UI with controls in a subtab bar and content area."""
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)

        # Subtab bar
        subtab_bar = QWidget()
        subtab_bar.setFixedHeight(50)
        subtab_bar.setStyleSheet("background-color: #e0f7fa;")  # Match 'CRM' tab color
        subtab_layout = QHBoxLayout()
        subtab_layout.setContentsMargins(8, 6, 8, 6)
        subtab_layout.setSpacing(8)
        subtab_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
        subtab_bar.setLayout(subtab_layout)

        # Analysis Method Filter
        subtab_layout.addWidget(QLabel("Analysis Method:"))
        self.filter_var.setFixedWidth(150)
        self.filter_var.currentTextChanged.connect(self.update_display)
        subtab_layout.addWidget(self.filter_var)

        # Decimal Places
        subtab_layout.addWidget(QLabel("Decimals:"))
        self.decimal_places.addItems(["0", "1", "2", "3"])
        self.decimal_places.setCurrentText("2")
        self.decimal_places.setFixedWidth(60)
        self.decimal_places.currentTextChanged.connect(self.update_display)
        subtab_layout.addWidget(self.decimal_places)

        # Search Field
        self.search_var.setPlaceholderText("Search by any field...")
        self.search_var.setFixedWidth(150)
        self.search_var.textChanged.connect(self.update_display)
        subtab_layout.addWidget(self.search_var)

        # Our OREAS checkbox
        self.our_oreas_checkbox.stateChanged.connect(self.update_display)
        subtab_layout.addWidget(self.our_oreas_checkbox)

        # CRUD Buttons
        add_btn = QPushButton("Add Record")
        add_btn.setFixedSize(100, 30)
        add_btn.clicked.connect(self.open_add_window)
        subtab_layout.addWidget(add_btn)

        edit_btn = QPushButton("Edit Record")
        edit_btn.setFixedSize(100, 30)
        edit_btn.clicked.connect(self.open_edit_window)
        subtab_layout.addWidget(edit_btn)

        delete_btn = QPushButton("Delete Selected")
        delete_btn.setFixedSize(100, 30)
        delete_btn.clicked.connect(self.delete_selected)
        subtab_layout.addWidget(delete_btn)

        # Add logo to subtab bar
        subtab_layout.addStretch()
        logo_label = QLabel()
        logo_label.setPixmap(QPixmap(LOGO_PNG_PATH).scaled(100, 40, Qt.AspectRatioMode.KeepAspectRatio))
        logo_label.setAlignment(Qt.AlignmentFlag.AlignRight)
        subtab_layout.addWidget(logo_label)

        # Indicator
        indicator = QWidget()
        indicator.setFixedHeight(3)
        indicator.setStyleSheet("background-color: #008b8b;")  # Match 'CRM' tab indicator color

        # Content area
        content_area = QWidget()
        content_layout = QVBoxLayout()
        content_layout.setContentsMargins(0, 0, 0, 0)
        content_layout.setSpacing(0)
        content_area.setLayout(content_layout)

        # Placeholder label
        self.placeholder_label = QLabel("Loading data from database...")
        self.placeholder_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        content_layout.addWidget(self.placeholder_label)

        # Add subtab bar, indicator, and content to main layout
        main_layout.addWidget(subtab_bar)
        main_layout.addWidget(indicator)
        main_layout.addWidget(content_area, 1)

    def setup_full_ui(self):
        """Setup full UI with QTableView and frozen first column."""
        if self.ui_initialized:
            return

        if self.placeholder_label:
            self.placeholder_label.deleteLater()
            self.placeholder_label = None

        # Find the content area widget
        content_area = None
        for i in range(self.layout().count()):
            item = self.layout().itemAt(i)
            if item.widget() and item.widget().layout() and isinstance(item.widget().layout(), QVBoxLayout):
                content_area = item.widget()
                break

        if not content_area:
            logger.error("Content area not found in setup_full_ui")
            return

        content_layout = content_area.layout()

        self.table_view = FreezeTableWidget(CRMTableModel(self))
        self.table_view.setAlternatingRowColors(True)
        self.table_view.setSelectionMode(QTableView.SelectionMode.SingleSelection)
        self.table_view.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
        self.table_view.setSortingEnabled(True)
        self.table_view.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        self.table_view.horizontalHeader().setMinimumSectionSize(100)
        self.table_view.horizontalHeader().setStretchLastSection(True)
        self.table_view.verticalHeader().setVisible(False)
        self.table_view.setEnabled(True)
        self.table_view.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        self.table_view.setShowGrid(True)
        content_layout.addWidget(self.table_view)

        self.ui_initialized = True
        
    def init_db(self):
        """Initialize SQLite database."""
        try:
            # Use resource_path to get the correct path to the database
            self.conn =get_db_connection()
        except Exception as e:
            logger.error(f"Failed to connect to SQLite database: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to connect to database:\n{str(e)}")

    def load_and_display(self):
        """Load data from SQLite and display pivot table."""
        if self.conn is None:
            self.init_db()

        try:
            cursor = self.conn.cursor()
            cursor.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='pivot_crm'")
            if not cursor.fetchone():
                QMessageBox.warning(self, "Warning", "Pivot table not found in database. Please ensure the pivot_crm table exists.")
                self.placeholder_label.setText("No pivot_crm table found in database.")
                return

            self.setup_full_ui()
            self.update_filter_options()
            self.update_display()
        except Exception as e:
            logger.error(f"Failed to load CRM data: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to load CRM data:\n{str(e)}")
            self.placeholder_label.setText("Error loading data from database.")

    def update_filter_options(self):
        """Update the Analysis Method filter dropdown with unique values."""
        if self.conn is None:
            self.filter_var.addItem("All")
            self.filter_var.setCurrentText("All")
            return

        try:
            cursor = self.conn.cursor()
            cursor.execute("SELECT DISTINCT [Analysis Method] FROM pivot_crm WHERE [Analysis Method] IS NOT NULL")
            unique_methods = ["All"] + sorted([row[0] for row in cursor.fetchall()])
            self.filter_var.clear()
            self.filter_var.addItems(unique_methods)
            if self.filter_var.currentText() not in unique_methods:
                self.filter_var.setCurrentText("All")
        except Exception as e:
            logger.error(f"Failed to update filter options: {str(e)}")
            self.filter_var.clear()
            self.filter_var.addItem("All")
            self.filter_var.setCurrentText("All")

    def update_display(self, event=None):
        """Update QTableView display with pivot table data from 'pivot_crm'."""
        if self.conn is None:
            self._set_status_table("No database connection")
            return

        try:
            cursor = self.conn.cursor()

            # If Our OREAS checkbox is checked, show only records from our_oreas
            if self.our_oreas_checkbox.isChecked():
                query = """
                    SELECT p.*
                    FROM pivot_crm p
                    INNER JOIN our_oreas o ON p.[CRM ID] = o.name
                """
                params = []
            else:
                query = "SELECT * FROM pivot_crm"
                params = []

            conditions = []
            search_text = self.search_var.text().strip().lower()
            filter_method = self.filter_var.currentText()

            cursor.execute("PRAGMA table_info(pivot_crm)")
            columns = [info[1] for info in cursor.fetchall()]

            # === جستجو فقط در ستون CRM ID ===
            if search_text:
                if "CRM ID" in columns:
                    conditions.append("LOWER([CRM ID]) LIKE ?")
                    params.append(f"%{search_text}%")
                else:
                    logger.warning("Column 'CRM ID' not found in pivot_crm table")

            if filter_method != "All" and "Analysis Method" in columns:
                conditions.append("[Analysis Method] = ?")
                params.append(filter_method)

            if conditions:
                query += " WHERE " + " AND ".join(conditions)

            df = pd.read_sql_query(query, self.conn, params=params)
            if df.empty:
                self._set_status_table("No data after filtering")
                return

            # ادامه کد بدون تغییر...
            self.pivot_data = df
            model = CRMTableModel(self, df, decimal_places=int(self.decimal_places.currentText()))
            self.table_view.setModel(model)
            self.table_view.frozenTableView.setModel(model)
            self.table_view.update_frozen_columns
            
            for col_idx, col in enumerate(df.columns):
                max_width = max([len(str(x)) for x in df[col].dropna()] + [len(str(col))], default=10)
                pixel_width = min(max_width * 9, 150)
                self.column_widths[col] = pixel_width
                self.table_view.setColumnWidth(col_idx, pixel_width)
                if col_idx == 0:
                    self.table_view.frozenTableView.setColumnWidth(0, pixel_width)

            self.table_view.model().layoutChanged.emit()
            self.table_view.frozenTableView.model().layoutChanged.emit()

        except Exception as e:
            logger.error(f"Failed to update display: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to update display:\n{str(e)}")

    def _set_status_table(self, text):
        """Set status message in the table."""
        if self.table_view:
            self.table_view.setModel(None)
        # Update the placeholder label in the content area
        if self.placeholder_label:
            self.placeholder_label.setText(text)
        else:
            content_area = None
            for i in range(self.layout().count()):
                item = self.layout().itemAt(i)
                if item.widget() and item.widget().layout() and isinstance(item.widget().layout(), QVBoxLayout):
                    content_area = item.widget()
                    break
            if content_area:
                self.placeholder_label = QLabel(text)
                self.placeholder_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
                content_area.layout().addWidget(self.placeholder_label)

    def open_add_window(self):
        """Open a window to add a new record to 'pivot_crm'."""
        if self.conn is None:
            QMessageBox.warning(self, "Warning", "No database connection!")
            return

        dialog = QDialog(self)
        dialog.setWindowTitle("Add New Pivoted CRM Record")
        dialog.setGeometry(200, 200, 400, 500)
        dialog.setModal(True)
        layout = QVBoxLayout(dialog)
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(10)

        cursor = self.conn.cursor()
        cursor.execute("PRAGMA table_info(pivot_crm)")
        columns = [info[1] for info in cursor.fetchall()]
        entry_vars = {col: QLineEdit() for col in columns}

        scroll_area = QScrollArea()
        scroll_widget = QWidget()
        grid_layout = QGridLayout(scroll_widget)
        grid_layout.setContentsMargins(5, 5, 5, 5)
        grid_layout.setSpacing(10)
        for i, col in enumerate(columns):
            label = QLabel(f"{col}:")
            label.setFixedWidth(150)
            grid_layout.addWidget(label, i, 0, Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignTop)
            entry = entry_vars[col]
            entry.setFixedWidth(180)
            grid_layout.addWidget(entry, i, 1)

        scroll_widget.setLayout(grid_layout)
        scroll_area.setWidget(scroll_widget)
        scroll_area.setWidgetResizable(True)
        layout.addWidget(scroll_area)

        button_layout = QHBoxLayout()
        button_layout.setSpacing(10)
        save_btn = QPushButton("Save")
        save_btn.setFixedSize(100, 30)
        save_btn.clicked.connect(lambda: self.save_record(entry_vars, dialog))
        button_layout.addWidget(save_btn)

        cancel_btn = QPushButton("Cancel")
        cancel_btn.setFixedSize(100, 30)
        cancel_btn.clicked.connect(dialog.accept)
        button_layout.addWidget(cancel_btn)

        layout.addLayout(button_layout)
        dialog.exec()

    def save_record(self, entry_vars, dialog):
        """Save new record to 'pivot_crm'."""
        try:
            cursor = self.conn.cursor()
            cursor.execute("PRAGMA table_info(pivot_crm)")
            columns = [info[1] for info in cursor.fetchall()]
            values = [entry_vars[col].text() or None for col in columns]
            query = f"INSERT INTO pivot_crm ({', '.join(f'[{col}]' for col in columns)}) VALUES ({', '.join(['?'] * len(columns))})"
            cursor.execute(query, values)
            self.conn.commit()
            logger.info("Added new record to pivot_crm table")
            self.update_display()
            QMessageBox.information(dialog, "Success", "Record added successfully!")
            dialog.accept()
        except Exception as e:
            logger.error(f"Failed to add record: {str(e)}")
            QMessageBox.warning(dialog, "Error", f"Failed to add record:\n{str(e)}")

    def open_edit_window(self):
        """Open a window to edit the selected record in 'pivot_crm'."""
        if self.conn is None:
            QMessageBox.warning(self, "Warning", "No database connection!")
            return

        selected = self.table_view.selectionModel().selectedRows()
        if not selected:
            QMessageBox.warning(self, "Warning", "Please select a record to edit!")
            return
        if len(selected) > 1:
            QMessageBox.warning(self, "Warning", "Please select only one record to edit!")
            return

        row = selected[0].row()
        id_value = self.table_view.model().data(self.table_view.model().index(row, 0))

        dialog = QDialog(self)
        dialog.setWindowTitle("Edit Pivoted CRM Record")
        dialog.setGeometry(200, 200, 400, 500)
        dialog.setModal(True)
        layout = QVBoxLayout(dialog)
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(10)

        cursor = self.conn.cursor()
        cursor.execute("SELECT * FROM pivot_crm WHERE [CRM ID] = ?", (id_value,))
        record = cursor.fetchone()
        columns = [desc[0] for desc in cursor.description]
        entry_vars = {col: QLineEdit(str(record[i]) if record[i] is not None else "") for i, col in enumerate(columns)}

        scroll_area = QScrollArea()
        scroll_widget = QWidget()
        grid_layout = QGridLayout(scroll_widget)
        grid_layout.setContentsMargins(5, 5, 5, 5)
        grid_layout.setSpacing(10)
        for i, col in enumerate(columns):
            label = QLabel(f"{col}:")
            label.setFixedWidth(150)
            grid_layout.addWidget(label, i, 0, Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignTop)
            entry = entry_vars[col]
            entry.setFixedWidth(180)
            grid_layout.addWidget(entry, i, 1)

        scroll_widget.setLayout(grid_layout)
        scroll_area.setWidget(scroll_widget)
        scroll_area.setWidgetResizable(True)
        layout.addWidget(scroll_area)

        button_layout = QHBoxLayout()
        button_layout.setSpacing(10)
        save_btn = QPushButton("Save")
        save_btn.setFixedSize(100, 30)
        save_btn.clicked.connect(lambda: self.save_edit(entry_vars, id_value, dialog))
        button_layout.addWidget(save_btn)

        cancel_btn = QPushButton("Cancel")
        cancel_btn.setFixedSize(100, 30)
        cancel_btn.clicked.connect(dialog.accept)
        button_layout.addWidget(cancel_btn)

        layout.addLayout(button_layout)
        dialog.exec()

    def save_edit(self, entry_vars, id_value, dialog):
        """Save edited record to 'pivot_crm'."""
        try:
            cursor = self.conn.cursor()
            cursor.execute("PRAGMA table_info(pivot_crm)")
            columns = [info[1] for info in cursor.fetchall()]
            set_clause = ", ".join(f"[{col}] = ?" for col in columns if col != 'CRM ID')
            query = f"UPDATE pivot_crm SET {set_clause} WHERE [CRM ID] = ?"
            update_values = [entry_vars[col].text() or None for col in columns if col != 'CRM ID'] + [id_value]
            cursor.execute(query, update_values)
            self.conn.commit()
            logger.info(f"Updated record with CRM ID = {id_value}")
            self.update_display()
            QMessageBox.information(dialog, "Success", "Record updated successfully!")
            dialog.accept()
        except Exception as e:
            logger.error(f"Failed to update record: {str(e)}")
            QMessageBox.warning(dialog, "Error", f"Failed to update record:\n{str(e)}")

    def delete_selected(self):
        """Delete selected records from 'pivot_crm'."""
        if self.conn is None:
            QMessageBox.warning(self, "Warning", "No database connection!")
            return

        selected = self.table_view.selectionModel().selectedRows()
        if not selected:
            QMessageBox.warning(self, "Warning", "Please select at least one record to delete!")
            return

        if QMessageBox.question(self, "Confirm Delete", "Are you sure you want to delete the selected records?") != QMessageBox.StandardButton.Yes:
            return

        try:
            cursor = self.conn.cursor()
            id_col = 'CRM ID'
            for row in selected:
                id_value = self.table_view.model().data(self.table_view.model().index(row.row(), 0))
                cursor.execute(f"DELETE FROM pivot_crm WHERE [{id_col}] = ?", (id_value,))
                logger.info(f"Deleted record with {id_col} = {id_value}")
            self.conn.commit()
            self.update_display()
            QMessageBox.information(self, "Success", "Selected records deleted successfully!")
        except Exception as e:
            logger.error(f"Failed to delete records: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to delete records:\n{str(e)}")

    def reset_cache(self):
        """Reset cache and close database connection."""
        if self.conn:
            logger.info("SQLite database connection closed")
        self.conn = None
        self.pivot_data = None
        self.column_widths = {}
        self.search_var.clear()
        self.filter_var.setCurrentText("All")
        self.ui_initialized = False

    def __del__(self):
        """Ensure database connection is closed when object is destroyed."""
        if self.conn:
            logger.info("SQLite database connection closed in destructor")
