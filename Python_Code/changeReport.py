# screens/process/changes_report.py
from PyQt6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QPushButton, QTableView, QHeaderView,
    QMessageBox, QComboBox, QLabel, QProgressDialog
)
from PyQt6.QtCore import Qt, QThread, pyqtSignal
from PyQt6.QtGui import QStandardItemModel, QStandardItem
import numpy as np
import pandas as pd
import logging
import sqlite3
import os
from datetime import datetime
from db.db import get_db_connection,resource_path
from styles.common import common_styles
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

class ReportGenerationThread(QThread):
    progress = pyqtSignal(int)
    finished = pyqtSignal(list)
    error = pyqtSignal(str)

    def __init__(self, app, results_frame, selected_column):
        super().__init__()
        self.app = app
        self.results_frame = results_frame
        self.selected_column = selected_column

    def get_weight_corrections(self):
        corrections = {}
        if hasattr(self.app, 'weight_check') and hasattr(self.app.weight_check, 'corrected_weights'):
            corrections = self.app.weight_check.corrected_weights.copy()
        return corrections

    def get_volume_corrections(self):
        corrections = {}
        if hasattr(self.app, 'volume_check') and hasattr(self.app.volume_check, 'corrected_volumes'):
            for sl, params in self.app.volume_check.corrected_volumes.items():
                if 'old_volume' in params and 'new_volume' in params:
                    corrections[sl] = {'old_volume': params['old_volume'], 'new_volume': params['new_volume']}
        return corrections

    def get_df_corrections(self):
        corrections = {}
        if hasattr(self.app, 'df_check') and hasattr(self.app.df_check, 'corrected_dfs'):
            corrections = self.app.df_check.corrected_dfs.copy()
        return corrections

    def get_crm_corrections(self, column):
        """استخراج ضرایب CRM از report_change"""
        corrections = {}
        try:
            if hasattr(self.app.results, 'report_change'):
                df = self.app.results.report_change
                if df.empty or 'Element' not in df.columns:
                    return {}
                
                # فقط CRM entries (دارای Scale یا Blank)
                mask = (
                    (df['Element'] == column) & 
                    (df['Scale'].notna() | df['Blank'].notna())
                )
                
                if mask.any():
                    for _, row in df[mask].iterrows():
                        sl = row['Solution Label']
                        scale = row.get('Scale', 1.0)
                        blank = row.get('Blank', 0.0)
                        if pd.notna(sl):
                            corrections[str(sl)] = {
                                'scale': float(scale) if pd.notna(scale) else 1.0,
                                'blank': float(blank) if pd.notna(blank) else 0.0
                            }
                logger.debug(f"Found {len(corrections)} CRM corrections for {column}")
        except Exception as e:
            logger.error(f"Error getting CRM corrections: {str(e)}")
        
        return corrections

    def get_drift_corrections(self, column):
        corrections = {}
        try:
            if hasattr(self.app.results, 'report_change'):
                df = self.app.results.report_change
                # *** SAFE CHECK FOR COLUMNS ***
                if df.empty or 'Element' not in df.columns:
                    logger.debug("report_change empty or missing 'Element' column")
                    return {}
                
                mask = df['Element'] == column
                if mask.any():
                    for _, row in df[mask].iterrows():
                        sl = row['Solution Label']
                        ratio = row['Ratio']
                        if pd.notna(ratio) and pd.notna(sl):
                            corrections[str(sl)] = float(ratio)
                logger.debug(f"Found {len(corrections)} drift corrections for {column}")
            else:
                logger.debug("No report_change attribute found")
        except Exception as e:
            logger.error(f"Error getting drift corrections for {column}: {str(e)}")
        
        return corrections

    def run(self):
        try:
            # --- دریافت داده اصلی (pivoted) ---
            last_filtered = getattr(self.results_frame, 'last_filtered_data', None)
            if last_filtered is None or last_filtered.empty:
                self.error.emit("No filtered data available for report")
                return

            # --- دریافت داده اولیه (init_data) ---
            init_data = self.app.init_data
            if init_data is None or init_data.empty:
                self.error.emit("No initial data available for report")
                return

            # --- استخراج Original Value (SAFE) ---
            orig_cond = (
                (init_data['Element'] == self.selected_column) & 
                (init_data['Type'].isin(['Samp', 'Sample']))
            )
            orig_rows = init_data[orig_cond]
            if orig_rows.empty:
                self.error.emit(f"No original data found for element: {self.selected_column}")
                return
                
            orig_df = orig_rows[['Solution Label', 'Corr Con']].copy()
            orig_df['Corr Con'] = pd.to_numeric(orig_df['Corr Con'], errors='coerce')
            orig_df = orig_df.rename(columns={'Corr Con': 'Original Value'})
            orig_df = orig_df.sort_index().set_index('Solution Label')

            # --- استخراج New Value (SAFE) ---
            if 'Solution Label' not in last_filtered.columns:
                self.error.emit("last_filtered_data missing 'Solution Label' column")
                return
                
            if self.selected_column not in last_filtered.columns:
                self.error.emit(f"Selected column '{self.selected_column}' not found in pivoted data")
                return

            last_filtered_sl = last_filtered.set_index('Solution Label')
            new_series = pd.to_numeric(last_filtered_sl[self.selected_column], errors='coerce')
            orig_df['New Value'] = new_series
            orig_df = orig_df.reset_index()

            # --- دریافت corrections ---
            weight_corrections = self.get_weight_corrections()
            volume_corrections = self.get_volume_corrections()
            df_corrections = self.get_df_corrections()
            crm_corrections = self.get_crm_corrections(self.selected_column)
            drift_corrections = self.get_drift_corrections(self.selected_column)

            # --- ساخت ردیف‌ها ---
            rows = []
            total_rows = len(orig_df)
            for i, row in orig_df.iterrows():
                sl = row['Solution Label']
                orig_val = row['Original Value']
                new_val = row['New Value']
                
                # Safe formatting
                orig_text = f"{orig_val:.3f}" if pd.notna(orig_val) else "N/A"
                new_text = f"{new_val:.3f}" if pd.notna(new_val) else "N/A"
                
                # Corrections
                wp = weight_corrections.get(str(sl), {})
                weight_text = (
                    f"Old: {wp.get('old_weight', ''):.3f}, New: {wp.get('new_weight', ''):.3f}" 
                    if wp and 'old_weight' in wp and 'new_weight' in wp else ""
                )

                vp = volume_corrections.get(str(sl), {})
                volume_text = (
                    f"Old: {vp.get('old_volume', ''):.3f}, New: {vp.get('new_volume', ''):.3f}" 
                    if vp and 'old_volume' in vp and 'new_volume' in vp else ""
                )

                dfp = df_corrections.get(str(sl), {})
                df_text = (
                    f"Old: {dfp.get('old_df', ''):.3f}, New: {dfp.get('new_df', ''):.3f}" 
                    if dfp and 'old_df' in dfp and 'new_df' in dfp else ""
                )

                cp = crm_corrections.get(str(sl), {})
                if cp:
                    scale = cp.get('scale', 1.0)
                    blank = cp.get('blank', 0.0)
                    crm_text = f"Scale: {scale:.3f}, Blank: {blank:.3f}"
                else:
                    crm_text = ""

                dp = drift_corrections.get(str(sl), None)
                drift_text = f"Ratio: {dp:.3f}" if isinstance(dp, (int, float)) and not pd.isna(dp) else ""

                row_items = [
                    str(sl),
                    orig_text,
                    new_text,
                    weight_text,
                    volume_text,
                    df_text,
                    crm_text,
                    drift_text
                ]
                rows.append(row_items)
                
                if total_rows > 10:
                    self.progress.emit(int((i + 1) / total_rows * 100))

            self.finished.emit(rows)
            logger.info(f"✅ Report generated successfully: {len(rows)} rows")

        except Exception as e:
            logger.exception("Error in report generation")
            self.error.emit(f"Report generation failed: {str(e)}")


class ChangesReportDialog(QDialog):
    def __init__(self, app, results_frame, parent=None):
        super().__init__(parent)
        self.app = app
        self.results_frame = results_frame
        self.selected_column = None
        self.file_id = None
        self.setup_ui()

    def setup_ui(self):
        self.setStyleSheet(common_styles)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(15, 15, 15, 15)
        layout.setSpacing(10)

        top_layout = QHBoxLayout()
        top_layout.addWidget(QLabel("Select Column:"))
        self.column_combo = QComboBox()
        self.column_combo.setMaximumWidth(250)
        top_layout.addWidget(self.column_combo)
        show_button = QPushButton("Show Report")
        show_button.setFixedWidth(120)
        show_button.clicked.connect(self.start_report_generation)
        top_layout.addWidget(show_button)
        top_layout.addStretch()
        layout.addLayout(top_layout)

        self.report_table = QTableView()
        self.report_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        layout.addWidget(self.report_table)

        self.setMinimumSize(1200, 800)
        self.setWindowTitle("Changes Report")
        self.update_column_combo()

    def get_valid_data(self):
        if (hasattr(self.results_frame, 'last_filtered_data') and
            self.results_frame.last_filtered_data is not None and
            not self.results_frame.last_filtered_data.empty):
            return self.results_frame.last_filtered_data
        else:
            data = self.app.get_data()
            if data is not None and not data.empty:
                return data
            return None

    def update_column_combo(self):
        data = self.get_valid_data()
        if data is None:
            logger.warning("No data available to populate column combo")
            self.column_combo.clear()
            return
        
        # *** SAFE COLUMN EXTRACTION ***
        valid_columns = []
        if 'Solution Label' in data.columns:
            valid_columns = [col for col in data.columns if col != 'Solution Label']
        else:
            # Fallback for wide format without proper columns
            valid_columns = [col for col in data.columns if col != data.columns[0]]
        
        self.column_combo.clear()
        self.column_combo.addItems(valid_columns)
        logger.debug(f"Updated column combo with {len(valid_columns)} columns: {valid_columns}")

    def start_report_generation(self):
        self.selected_column = self.column_combo.currentText()
        if not self.selected_column:
            QMessageBox.warning(self, "Warning", "Please select a column!")
            return
        data = self.get_valid_data()
        if data is None:
            QMessageBox.warning(self, "Warning", "No pivoted data available!")
            return

        self.file_id = self.get_file_id()
        if not self.file_id:
            QMessageBox.warning(self, "Warning", "Could not find file in database. Please save project first.")
            return

        self.progress_dialog = QProgressDialog("Generating report...", "Cancel", 0, 100, self)
        self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        self.progress_dialog.setAutoClose(True)
        self.progress_dialog.setMinimumDuration(0)

        self.thread = ReportGenerationThread(self.app, self.results_frame, self.selected_column)
        self.progress_dialog.canceled.connect(self.thread.terminate)
        self.thread.progress.connect(self.progress_dialog.setValue)
        self.thread.finished.connect(self.on_report_finished)
        self.thread.error.connect(self.on_report_error)
        self.thread.start()

    def get_file_id(self):
        if not self.app.file_path:
            return None
        try:
            conn =get_db_connection()
            cur = conn.cursor()
            cur.execute("""
                SELECT id FROM uploaded_files 
                WHERE file_path = ? OR original_filename = ?
            """, (self.app.file_path, os.path.basename(self.app.file_path)))
            result = cur.fetchone()
            
            return result[0] if result else None
        except Exception as e:
            logger.error(f"Error finding file_id: {e}")
            return None

    def get_measurement_id(self, sample_id, element):
        if not self.file_id:
            return None
        try:
            conn =get_db_connection()
            cur = conn.cursor()
            cur.execute("""
                SELECT id FROM measurements
                WHERE file_id = ? AND sample_id = ? AND element = ?
            """, (self.file_id, sample_id, element))
            result = cur.fetchone()
            
            return result[0] if result else None
        except Exception as e:
            logger.error(f"Error finding measurement_id: {e}")
            return None

    def on_report_finished(self, rows):
        model = QStandardItemModel()
        headers = [
            "Solution Label", "Original Value", "New Value",
            "Weight Correction", "Volume Correction", "DF Correction",
            "CRM Calibration", "Drift Calibration"
        ]
        model.setHorizontalHeaderLabels(headers)
        for row_items in rows:
            model.appendRow([QStandardItem(str(item)) for item in row_items])
        self.report_table.setModel(model)
        self.report_table.resizeColumnsToContents()
        self.progress_dialog.close()
        QMessageBox.information(self, "Success", f"Report generated with {len(rows)} rows")
        self.save_changes_to_db(rows)

    def save_changes_to_db(self, rows):
        try:
            user_id = getattr(self.app, 'current_user_id', None)
            if not user_id:
                try:
                    user_id = self.app.user_id_from_username
                except:
                    pass
            if not user_id:
                logger.warning("Could not determine user_id for logging changes")
                return

            conn =get_db_connection()
            cursor = conn.cursor()

            for i,row_items in enumerate(rows):
                sl = row_items[0]
                orig_val_str = row_items[1]
                new_val_str = row_items[2]
                weight_text = row_items[3]
                volume_text = row_items[4]
                df_text = row_items[5]
                crm_text = row_items[6]
                drift_text = row_items[7]

                # تبدیل به float یا None
                orig_val = float(orig_val_str) if orig_val_str != "N/A" else None
                new_val = float(new_val_str) if new_val_str != "N/A" else None

                # ساخت details
                details_parts = []
                if weight_text: details_parts.append(f"Weight: {weight_text}")
                if volume_text: details_parts.append(f"Volume: {volume_text}")
                if df_text: details_parts.append(f"DF: {df_text}")
                if crm_text: details_parts.append(f"CRM: {crm_text}")
                if drift_text: details_parts.append(f"Drift: {drift_text}")
                details = "; ".join(details_parts) if details_parts else "No corrections applied"

                # پیدا کردن یا ایجاد measurement
                measurement_id = self.get_measurement_id(sl, self.selected_column)
                if not measurement_id and self.file_id:
                    # همیشه ایجاد کنیم، حتی اگر new_val None باشد
                    cursor.execute('''
                        INSERT OR IGNORE INTO measurements (file_id, sample_id, element, current_value)
                        VALUES (?, ?, ?, ?)
                    ''', (self.file_id, sl, self.selected_column, new_val))
                    measurement_id = cursor.lastrowid if cursor.rowcount > 0 else self.get_measurement_id(sl, self.selected_column)

                # ثبت در changes_log همیشه
                cursor.execute('''
                    INSERT INTO changes_log (
                        user_id, action, entity_type, entity_id,
                        file_path, column_name, solution_label,
                        original_value, new_value, details, stage,pivot_index
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?,?)
                ''', (
                    user_id,
                    'correction',
                    'measurement',
                    measurement_id or 0,
                    os.path.basename(self.app.file_path or ""),
                    self.selected_column,
                    sl,
                    str(orig_val) if orig_val is not None else "N/A",
                    str(new_val) if new_val is not None else "N/A",
                    details,
                    'pending_approval'
                    ,i
                ))

                # ثبت در measurement_versions همیشه اگر measurement_id وجود داشته باشد
                if measurement_id:
                    next_version = self.get_next_version_number(measurement_id)
                    cursor.execute('''
                        INSERT INTO measurement_versions
                        (measurement_id, version_number, value, changed_by, stage, reason)
                        VALUES (?, ?, ?, ?, ?, ?)
                    ''', (measurement_id, next_version, new_val, user_id, 'correction', details))

            conn.commit()
            
            logger.info(f"Successfully saved {len(rows)} changes to database.")
        except Exception as e:
            logger.error(f"DB save error: {e}")
            QMessageBox.warning(self, "DB Error", f"Could not save changes: {e}")

    def get_next_version_number(self, measurement_id):
        try:
            conn =get_db_connection()
            cur = conn.cursor()
            cur.execute("SELECT COALESCE(MAX(version_number), 0) + 1 FROM measurement_versions WHERE measurement_id = ?", (measurement_id,))
            next_ver = cur.fetchone()[0]
            
            return next_ver
        except:
            return 1

    def on_report_error(self, error_msg):
        QMessageBox.warning(self, "Error", f"Failed to generate report: {error_msg}")
        logger.error(f"Report error: {error_msg}")
        self.progress_dialog.close()