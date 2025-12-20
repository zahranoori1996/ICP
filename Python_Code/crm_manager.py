from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QTableView, QHeaderView, 
    QGroupBox, QMessageBox, QLineEdit, QLabel, QComboBox, QFileDialog,
    QDialog, QRadioButton, QCheckBox, QButtonGroup, QDialogButtonBox
)
from PyQt6.QtCore import pyqtSignal
import pandas as pd
import re
import logging
import sqlite3
from ...Common.Freeze_column import FreezeTableWidget
from ..pivot_table_model import PivotTableModel
from ..verification.crm_calibration import PivotPlotWindow
from openpyxl.styles import PatternFill
from openpyxl.utils import get_column_letter
from utils.var_main import CRM_PATTERN,CRM_IDS

logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

class CRMManager:
    """Manages CRM-related operations for the PivotTab."""
    def __init__(self, pivot_tab):
        self.pivot_tab = pivot_tab
        self.logger = logger
        self.crm_selections = {}  # Now uses (label, row_index) as key

    def check_rm(self):
        """Check Reference Materials (RM) against the CRM database and update inline CRM rows."""
        if not self._has_pivot_data():
            return

        file_id = self._get_current_file_id()
        user_id = self.pivot_tab.app.user_id_from_username
        
        try:
            conn = self._ensure_db_connection()
            if conn is None:
                return

            # Ensure the crm_selections table has row_index column
            self._ensure_crm_selections_table(conn)

            crm_rows = self._extract_crm_rows()
            if crm_rows.empty:
                QMessageBox.information(self.pivot_tab, "Info", "No CRM rows found in pivot data!")
                return

            self._reset_crm_state()
            element_columns = self._build_element_to_columns_map()

            for idx, row in crm_rows.iterrows():
                label = row['Solution Label']
                crm_id = self._extract_crm_id_from_label(label)
                if not crm_id:
                    continue

                crm_options = self._load_crm_options_from_db(conn, crm_id)
                if not crm_options:
                    continue

                selected_key = self._get_or_prompt_crm_selection(
                    conn, file_id, user_id, label, idx, crm_options
                )

                self._apply_selected_crm_to_pivot(
                    label, idx, selected_key, crm_options, element_columns
                )

            if not self.pivot_tab._inline_crm_rows:
                QMessageBox.information(self.pivot_tab, "Info", "No matching CRM elements found!")
                return

            self._finalize_crm_display()
            self.pivot_tab.data_changed.emit()

        except Exception as e:
            self.logger.error(f"Failed to check RM: {str(e)}")
            QMessageBox.warning(self.pivot_tab, "Error", f"Failed to check RM: {str(e)}")

    def _ensure_crm_selections_table(self, conn):
        """Ensure crm_selections table exists with row_index column."""
        cursor = conn.cursor()
        # Check if table exists and has row_index column
        cursor.execute("PRAGMA table_info(crm_selections)")
        columns = [col[1] for col in cursor.fetchall()]
        
        if 'row_index' not in columns and columns:
            # Add row_index column if table exists but doesn't have it
            cursor.execute("ALTER TABLE crm_selections ADD COLUMN row_index INTEGER")
            conn.commit()
            self.logger.info("Added row_index column to crm_selections table")

    def _has_pivot_data(self):
        data = self.pivot_tab.results_frame.last_filtered_data
        if data is None or data.empty:
            QMessageBox.warning(self.pivot_tab, "Warning", "No pivot data available!")
            self.logger.warning("No pivot data available in check_rm")
            return False
        return True

    def _get_current_file_id(self):
        try:
            return self.pivot_tab.app.management_tab.file_combo.currentData()
        except AttributeError:
            self.logger.warning("management_tab not available, trying to get latest file_id from DB")
            return None

    def _ensure_db_connection(self):
        conn = self.pivot_tab.app.crm_tab.conn
        if conn is None:
            self.pivot_tab.app.crm_tab.init_db()
            conn = self.pivot_tab.app.crm_tab.conn
        
        if conn is None:
            QMessageBox.warning(self.pivot_tab, "Error", "Failed to connect to CRM database!")
            self.logger.error("Failed to connect to CRM database")
        return conn

    def _extract_crm_rows(self):
        data = self.pivot_tab.results_frame.last_filtered_data

        def is_crm_label(label):
            label = str(label).strip().lower()
            for cid in CRM_IDS:
                if re.search(CRM_PATTERN, label):
                    return True
            return False

        return data[data['Solution Label'].apply(is_crm_label)].copy()

    def _extract_crm_id_from_label(self, label):
        label_str = str(label)
        for cid in CRM_IDS:
            m = re.search(CRM_PATTERN, label_str)
            if m:
                return m.group(1).strip()
        return None

    def _load_crm_options_from_db(self, conn, crm_id):
        cursor = conn.cursor()
        cursor.execute("SELECT * FROM pivot_crm WHERE [CRM ID] LIKE ?", (f"OREAS {crm_id}%",))
        rows = cursor.fetchall()
        if not rows:
            return {}

        cursor.execute("PRAGMA table_info(pivot_crm)")
        cols = [x[1] for x in cursor.fetchall()]
        non_element_cols = {'CRM ID', 'Solution Label', 'Analysis Method', 'Type'}
        allowed_methods = {'4-Acid Digestion', 'Aqua Regia Digestion'}

        options = {}
        filtered = {}
        
        for row in rows:
            crm_id_db = row[cols.index('CRM ID')]
            method = row[cols.index('Analysis Method')]
            key = f"{crm_id_db} ({method})"
            options[key] = []
            if method in allowed_methods:
                filtered[key] = []

            for col in cols:
                if col in non_element_cols:
                    continue
                value = row[cols.index(col)]
                if value in (None, ''):
                    continue
                try:
                    symbol = col.split('_')[0].strip()
                    val = float(value)
                    options[key].append((symbol, val))
                    if method in allowed_methods:
                        filtered[key].append((symbol, val))
                except (ValueError, TypeError):
                    pass

        return {"all": options, "filtered": filtered}

    def _get_or_prompt_crm_selection(self, conn, file_id, user_id, label, row_index, crm_options):
        all_opts = crm_options["all"]
        filtered_opts = crm_options["filtered"]
        
        row_key = (label, row_index)

        # Check in-memory cache first
        selected = self.crm_selections.get(row_key)
        
        # Then check database
        if selected is None and file_id is not None:
            cursor = conn.cursor()
            cursor.execute(
                "SELECT selected_crm_key FROM crm_selections WHERE file_id = ? AND solution_label = ? AND row_index = ?",
                (file_id, label, row_index)
            )
            result = cursor.fetchone()
            selected = result[0] if result else None

        # Show dialog if multiple options and no selection
        if selected is None and len(filtered_opts) > 1:
            selected = self._show_crm_selection_dialog(label, all_opts, filtered_opts, selected)
        elif selected is None and filtered_opts:
            selected = next(iter(filtered_opts))
        elif selected is None and all_opts:
            selected = next(iter(all_opts))

        # Save to database
        if file_id and selected:
            cursor = conn.cursor()
            cursor.execute("""
                INSERT OR REPLACE INTO crm_selections (file_id, solution_label, row_index, selected_crm_key, selected_by)
                VALUES (?, ?, ?, ?, ?)
            """, (file_id, label, row_index, selected, user_id))
            conn.commit()

        self.crm_selections[row_key] = selected
        return selected

    def _show_crm_selection_dialog(self, label, all_opts, filtered_opts, preselected=None):
        dialog = QDialog(self.pivot_tab)
        dialog.setWindowTitle(f"Select CRM for {label}")
        layout = QVBoxLayout(dialog)
        layout.addWidget(QLabel(f"Multiple CRMs found for {label}. Please select one:"))

        radio_group = QButtonGroup()
        container = QWidget()
        radio_layout = QVBoxLayout(container)

        more_cb = QCheckBox("Show all methods (including non-preferred)")
        layout.addWidget(more_cb)
        layout.addWidget(container)

        def refresh_radios():
            for i in reversed(range(radio_layout.count())):
                radio_layout.itemAt(i).widget().setParent(None)
            radio_group = QButtonGroup()
            opts = all_opts if more_cb.isChecked() else filtered_opts
            for key in sorted(opts.keys()):
                rb = QRadioButton(key)
                radio_layout.addWidget(rb)
                radio_group.addButton(rb)
                if key == preselected or (preselected is None and key == next(iter(opts), None)):
                    rb.setChecked(True)
            container.updateGeometry()
            dialog.adjustSize()

        more_cb.toggled.connect(lambda: refresh_radios())
        refresh_radios()

        btns = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        btns.accepted.connect(dialog.accept)
        btns.rejected.connect(dialog.reject)
        layout.addWidget(btns)

        if dialog.exec() == QDialog.DialogCode.Accepted:
            return radio_group.checkedButton().text() if radio_group.checkedButton() else next(iter(filtered_opts or all_opts))
        return None

    def _apply_selected_crm_to_pivot(self, label, row_index, selected_key, crm_options, element_columns):
        data = crm_options["all"].get(selected_key, []) or crm_options["filtered"].get(selected_key, [])
        crm_dict = {symbol: grade for symbol, grade in data}
        
        row_data = {'Solution Label': selected_key}
        for element, columns in element_columns.items():
            value = crm_dict.get(element)
            if value is not None:
                for col in columns:
                    row_data[col] = value

        if len(row_data) > 1:
            row_key = (label, row_index)
            self.pivot_tab._inline_crm_rows[row_key] = [row_data]
            checkbox_key = f"{label}_{row_index}"
            self.pivot_tab.included_crms[checkbox_key] = QCheckBox(label, checked=True)

    def _build_element_to_columns_map(self):
        cols = self.pivot_tab.results_frame.last_filtered_data.columns
        mapping = {}
        for col in cols:
            if col == 'Solution Label':
                continue
            element = col.split()[0].strip()
            mapping.setdefault(element, []).append(col)
        return mapping

    def _reset_crm_state(self):
        self.crm_selections = {}
        self.pivot_tab._inline_crm_rows.clear()
        self.pivot_tab.included_crms.clear()

    def _finalize_crm_display(self):
        columns = list(self.pivot_tab.results_frame.last_filtered_data.columns)
        self.pivot_tab._inline_crm_rows_display = self._build_crm_row_lists_for_columns(columns)
        self.pivot_tab.update_pivot_display()

    def open_manual_crm_dialog(self, solution_label, row_index):
        """Open a dialog to search and select a CRM manually."""
        file_id = None
        try:
            file_id = self.pivot_tab.app.management_tab.file_combo.currentData()
        except AttributeError:
            logger.warning("management_tab not available, attempting to retrieve latest file_id from database.")

        user_id = self.pivot_tab.app.user_id_from_username
        row_key = (solution_label, row_index)

        try:
            conn = self.pivot_tab.app.crm_tab.conn
            if conn is None:
                self.pivot_tab.app.crm_tab.init_db()
                conn = self.pivot_tab.app.crm_tab.conn
                if conn is None:
                    QMessageBox.warning(self.pivot_tab, "Error", "Failed to connect to CRM database!")
                    self.logger.error("Failed to connect to CRM database")
                    return

            # Ensure table has row_index column
            self._ensure_crm_selections_table(conn)
            cursor = conn.cursor()

            if file_id is None:
                cursor.execute("SELECT id FROM uploaded_files ORDER BY created_at DESC LIMIT 1")
                result = cursor.fetchone()
                if result:
                    file_id = result[0]
                    logger.info(f"Retrieved latest file_id: {file_id}")
                else:
                    logger.warning("No uploaded files found, skipping database operations for CRM selections.")

            # Check for existing selection with row_index
            if file_id is not None:
                cursor.execute(
                    "SELECT selected_crm_key FROM crm_selections WHERE file_id = ? AND solution_label = ? AND row_index = ?",
                    (file_id, solution_label, row_index)
                )
                db_result = cursor.fetchone()
                if db_result:
                    selected_crm_key = db_result[0]
                    self.add_manual_crm(solution_label, row_index, selected_crm_key)
                    return

            cursor.execute("SELECT DISTINCT [CRM ID] FROM pivot_crm WHERE [CRM ID] LIKE 'OREAS%'")
            crm_ids = [row[0] for row in cursor.fetchall()]

            if not crm_ids:
                QMessageBox.warning(self.pivot_tab, "Warning", "No CRMs found in the database!")
                self.logger.warning("No CRMs found in the database")
                return

            dialog = QDialog(self.pivot_tab)
            dialog.setWindowTitle(f"Select CRM for {solution_label} (Row {row_index})")
            layout = QVBoxLayout(dialog)
            layout.setSpacing(5)
            layout.setContentsMargins(10, 10, 10, 10)

            # Search input and button
            search_layout = QHBoxLayout()
            search_label = QLabel("Search OREAS:")
            search_input = QLineEdit()
            search_input.setPlaceholderText("Enter OREAS ID (e.g., 258)")
            search_button = QPushButton("Search")
            search_layout.addWidget(search_label)
            search_layout.addWidget(search_input)
            search_layout.addWidget(search_button)
            layout.addLayout(search_layout)

            # CRM selection
            radio_container = QWidget()
            radio_group_layout = QVBoxLayout(radio_container)
            radio_group_layout.setSpacing(2)
            radio_group_layout.setContentsMargins(0, 0, 0, 0)
            layout.addWidget(radio_container)

            radio_buttons = []
            radio_button_group = []

            def update_crm_list():
                for rb in radio_button_group:
                    rb.setParent(None)
                radio_button_group.clear()
                radio_buttons.clear()

                search_text = search_input.text().strip()
                if not search_text:
                    radio_container.updateGeometry()
                    layout.invalidate()
                    dialog.adjustSize()
                    confirm_btn.setEnabled(False)
                    return

                filtered_crms = [crm_id for crm_id in crm_ids if search_text.lower() in crm_id.lower()]
                for crm_id in sorted(filtered_crms):
                    cursor.execute(
                        "SELECT DISTINCT [Analysis Method] FROM pivot_crm WHERE [CRM ID] = ?",
                        (crm_id,)
                    )
                    methods = [row[0] for row in cursor.fetchall()]
                    for method in sorted(methods):
                        key = f"{crm_id} ({method})"
                        rb = QRadioButton(key)
                        rb.setStyleSheet("margin:0px; padding:0px;")
                        radio_group_layout.addWidget(rb)
                        radio_button_group.append(rb)
                        radio_buttons.append((key, rb))

                if radio_buttons:
                    radio_buttons[0][1].setChecked(True)
                    confirm_btn.setEnabled(True)
                else:
                    confirm_btn.setEnabled(False)
                radio_container.updateGeometry()
                layout.invalidate()
                dialog.adjustSize()

            search_button.clicked.connect(update_crm_list)

            # Buttons
            button_layout = QHBoxLayout()
            button_layout.setSpacing(10)
            button_layout.setContentsMargins(0, 8, 0, 0)
            confirm_btn = QPushButton("Confirm")
            confirm_btn.setEnabled(False)
            cancel_btn = QPushButton("Cancel")
            button_layout.addWidget(confirm_btn)
            button_layout.addWidget(cancel_btn)
            layout.addLayout(button_layout)

            def on_confirm():
                selected_crm_key = None
                for key, rb in radio_buttons:
                    if rb.isChecked():
                        selected_crm_key = key
                        break
                if selected_crm_key:
                    self.crm_selections[row_key] = selected_crm_key

                    if file_id is not None:
                        cursor.execute("""
                            INSERT OR REPLACE INTO crm_selections (file_id, solution_label, row_index, selected_crm_key, selected_by)
                            VALUES (?, ?, ?, ?, ?)
                        """, (file_id, solution_label, row_index, selected_crm_key, user_id))
                        conn.commit()

                    self.add_manual_crm(solution_label, row_index, selected_crm_key)
                dialog.accept()

            confirm_btn.clicked.connect(on_confirm)
            cancel_btn.clicked.connect(dialog.reject)
            dialog.exec()

        except Exception as e:
            self.logger.error(f"Failed to open manual CRM dialog: {str(e)}")
            QMessageBox.warning(self.pivot_tab, "Error", f"Failed to open manual CRM dialog: {str(e)}")

    def add_manual_crm(self, solution_label, row_index, selected_crm_key):
        """Add manually selected CRM to the pivot table."""
        try:
            conn = self.pivot_tab.app.crm_tab.conn
            cursor = conn.cursor()
            cursor.execute("PRAGMA table_info(pivot_crm)")
            db_columns = [x[1] for x in cursor.fetchall()]
            non_element_columns = ['CRM ID', 'Solution Label', 'Analysis Method', 'Type']

            crm_id = selected_crm_key.split(' (')[0]
            cursor.execute(
                "SELECT * FROM pivot_crm WHERE [CRM ID] = ? AND [Analysis Method] = ?",
                (crm_id, selected_crm_key.split(' (')[1][:-1])
            )
            crm_data = cursor.fetchall()
            if not crm_data:
                self.logger.warning(f"No data found for CRM {selected_crm_key}")
                return

            element_to_columns = {}
            for col in self.pivot_tab.results_frame.last_filtered_data.columns:
                if col == 'Solution Label':
                    continue
                element = col.split()[0].strip()
                element_to_columns.setdefault(element, []).append(col)

            crm_dict = {}
            for db_row in crm_data:
                for col in db_columns:
                    if col in non_element_columns:
                        continue
                    value = db_row[db_columns.index(col)]
                    if value not in (None, ''):
                        try:
                            symbol = col.split('_')[0].strip()
                            val = float(value)
                            crm_dict[symbol] = val
                        except (ValueError, TypeError):
                            continue

            crm_values = {'Solution Label': selected_crm_key}
            for element, columns in element_to_columns.items():
                value = crm_dict.get(element)
                if value is not None:
                    for col in columns:
                        crm_values[col] = value

            if len(crm_values) > 1:
                row_key = (solution_label, row_index)
                self.pivot_tab._inline_crm_rows[row_key] = [crm_values]
                checkbox_key = f"{solution_label}_{row_index}"
                self.pivot_tab.included_crms[checkbox_key] = QCheckBox(solution_label, checked=True)
                self.pivot_tab._inline_crm_rows_display = self._build_crm_row_lists_for_columns(
                    list(self.pivot_tab.results_frame.last_filtered_data.columns)
                )
                self.pivot_tab.update_pivot_display()
                self.pivot_tab.data_changed.emit()
                self.logger.debug(f"Manually added CRM {selected_crm_key} for {solution_label} at row {row_index}")

        except Exception as e:
            self.logger.error(f"Failed to add manual CRM: {str(e)}")
            QMessageBox.warning(self.pivot_tab, "Error", f"Failed to add manual CRM: {str(e)}")

    def check_rm_with_diff_range(self, min_diff, max_diff):
        """Update CRM rows display based on the specified difference range."""
        self.pivot_tab._inline_crm_rows_display = self._build_crm_row_lists_for_columns(
            list(self.pivot_tab.results_frame.last_filtered_data.columns)
        )
        self.pivot_tab.update_pivot_display()
        self.pivot_tab.data_changed.emit()
        self.logger.debug("Emitted data_changed signal after check_rm_with_diff_range")

    def _build_crm_row_lists_for_columns(self, columns):
        """Build CRM row lists for display."""
        crm_display = {}
        try:
            dec = int(self.pivot_tab.results_frame.decimal_combo.currentText())
        except (AttributeError, ValueError):
            self.logger.warning("decimal_combo not available or invalid, using default decimal places (1)")
            dec = 1

        try:
            min_diff = float(self.pivot_tab.crm_diff_min.text())
            max_diff = float(self.pivot_tab.crm_diff_max.text())
        except ValueError:
            min_diff, max_diff = -12, 12

        pivot_data = self.pivot_tab.results_frame.last_filtered_data
        
        for row_key, list_of_dicts in self.pivot_tab._inline_crm_rows.items():
            sol_label, row_idx = row_key
            crm_display[row_key] = []
            
            # Get the specific pivot row using row_idx
            if row_idx >= len(pivot_data):
                continue
                
            pivot_row = pivot_data.iloc[row_idx:row_idx+1]
            if pivot_row.empty:
                continue
            pivot_values = pivot_row.iloc[0].to_dict()

            for d in list_of_dicts:
                crm_row_list = []
                for col in columns:
                    if col == 'Solution Label':
                        crm_row_list.append(f"{d.get('Solution Label', sol_label)} CRM")
                    else:
                        val = d.get(col, "")
                        if pd.isna(val) or val == "":
                            crm_row_list.append("")
                        else:
                            try:
                                crm_row_list.append(f"{float(val):.{dec}f}")
                            except Exception:
                                crm_row_list.append(str(val))
                crm_display[row_key].append((crm_row_list, ["crm"] * len(columns)))

                diff_row_list = []
                diff_tags = []
                for col in columns:
                    if col == 'Solution Label':
                        diff_row_list.append(f"{sol_label} Diff (%)")
                        diff_tags.append("diff")
                    else:
                        pivot_val = pivot_values.get(col, None)
                        crm_val = d.get(col, None)
                        if pivot_val is not None and crm_val is not None:
                            try:
                                pivot_val = float(pivot_val)
                                crm_val = float(crm_val)
                                if crm_val != 0:
                                    diff = ((crm_val - pivot_val) / crm_val) * 100
                                    diff_row_list.append(f"{diff:.{dec}f}")
                                    diff_tags.append("in_range" if min_diff <= diff <= max_diff else "out_range")
                                else:
                                    diff_row_list.append("N/A")
                                    diff_tags.append("diff")
                            except Exception:
                                diff_row_list.append("")
                                diff_tags.append("diff")
                        else:
                            diff_row_list.append("")
                            diff_tags.append("diff")
                crm_display[row_key].append((diff_row_list, diff_tags))

        return crm_display