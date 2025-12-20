from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QMessageBox, QTableView, QHeaderView, QGroupBox, QFileDialog, QProgressDialog
)
from PyQt6.QtGui import QBrush, QColor
from PyQt6.QtCore import Qt, QAbstractTableModel, QThread, pyqtSignal
import pandas as pd
from collections import defaultdict
import logging
from styles.common import common_styles
from utils.utils import is_numeric
class LoadReportThread(QThread):
    """Thread for loading report data asynchronously."""
    progress = pyqtSignal(int)
    finished = pyqtSignal(object)
    error = pyqtSignal(str)

    def __init__(self, report_tab):
        super().__init__()
        self.report_tab = report_tab

    def run(self):
        try:
            pivot_tab = getattr(self.report_tab.results_frame, 'pivot_tab', self.report_tab.results_frame)
            pivot_data = getattr(pivot_tab, 'last_filtered_data', None)

            if pivot_data is None:
                self.error.emit("No pivot data available in ResultsFrame! Please load data in the Pivot tab.")
                return

            if pivot_data.empty:
                self.error.emit("Pivot data is empty! Please check filters or load new data.")
                return

            self.progress.emit(30)  # Initial progress
            report_data = self.report_tab.generate_report_data()
            if report_data is None:
                self.error.emit("Failed to generate report data! Please ensure data is loaded in the Pivot tab.")
                return

            if report_data.empty:
                self.error.emit("Generated report data is empty! No valid data available.")
                return

            self.progress.emit(90)  # Near completion
            self.finished.emit(report_data)
            self.progress.emit(100)  # Ensure progress bar reaches 100%
        except Exception as e:
            self.error.emit(f"Failed to load report data: {str(e)}")

class ExportReportThread(QThread):
    """Thread for exporting report data asynchronously."""
    progress = pyqtSignal(int)
    finished = pyqtSignal()
    error = pyqtSignal(str)

    def __init__(self, report_tab, file_path):
        super().__init__()
        self.report_tab = report_tab
        self.file_path = file_path

    def run(self):
        try:
            if self.report_tab.report_data is None or self.report_tab.report_data.empty:
                self.error.emit("No report data to export!")
                return

            export_data = pd.DataFrame(index=self.report_tab.report_data.index, 
                                      columns=['Solution Label'] + list(self.report_tab.base_elements.keys()))
            export_data['Solution Label'] = self.report_tab.report_data['Solution Label']

            self.progress.emit(30)  # Update progress
            for row in range(len(self.report_tab.report_data)):
                row_label = self.report_tab.report_data.index[row]
                for base_elem in self.report_tab.base_elements:
                    best_wl = self.report_tab.best_wavelengths_per_row.get(row, {}).get(base_elem)
                    if best_wl and best_wl in self.report_tab.report_data.columns:
                        value = self.report_tab.report_data.iloc[row][best_wl]
                        if not pd.isna(value) and self.report_tab.is_numeric(value):
                            export_data.at[row_label, base_elem] = float(value)
                self.progress.emit(30 + int(50 * (row + 1) / len(self.report_tab.report_data)))

            if export_data.shape[1] <= 1:
                self.error.emit("No valid data to export!")
                return

            self.progress.emit(90)  # Update progress
            if self.file_path.endswith('.xlsx'):
                export_data.to_excel(self.file_path, index=False)
            else:
                export_data.to_csv(self.file_path, index=False)
            self.finished.emit()
            self.progress.emit(100)  # Ensure progress bar reaches 100%
        except Exception as e:
            self.error.emit(f"Failed to export report: {str(e)}")

class PivotTableModel(QAbstractTableModel):
    """Custom table model for displaying pivot data with cell-based highlighting."""
    def __init__(self, parent, data=None, calibration_ranges=None, selected_columns=None, best_wavelengths_per_row=None):
        super().__init__(parent)
        self._data = data if isinstance(data, pd.DataFrame) else pd.DataFrame()
        self._calibration_ranges = calibration_ranges if calibration_ranges is not None else {}
        self._selected_columns = selected_columns if selected_columns is not None else []
        self._best_wavelengths_per_row = best_wavelengths_per_row if best_wavelengths_per_row is not None else {}

    def rowCount(self, parent=None):
        return len(self._data.index)

    def columnCount(self, parent=None):
        return len(self._data.columns)

    def data(self, index, role=Qt.ItemDataRole.DisplayRole):
        if not index.isValid():
            return None

        row = index.row()
        col = index.column()
        value = self._data.iloc[row, col]
        column_name = self._data.columns[col]

        if role == Qt.ItemDataRole.DisplayRole:
            return self.parent().format_value(value)

        elif role == Qt.ItemDataRole.BackgroundRole:
            if pd.isna(value) or not self.parent().is_numeric(value):
                return QBrush(QColor(255, 255, 255))  # White for invalid values
            base_elem = column_name.split()[0] if column_name != 'Solution Label' else None
            if base_elem and row in self._best_wavelengths_per_row:
                if self._best_wavelengths_per_row[row].get(base_elem) == column_name:
                    return QBrush(QColor(200, 255, 200))  # Light green for best wavelength
            return QBrush(QColor(255, 255, 255))  # White for others

        elif role == Qt.ItemDataRole.TextAlignmentRole:
            return Qt.AlignmentFlag.AlignCenter

        return None

    def headerData(self, section, orientation, role=Qt.ItemDataRole.DisplayRole):
        if role != Qt.ItemDataRole.DisplayRole:
            return None

        if orientation == Qt.Orientation.Horizontal:
            return str(self._data.columns[section])
        else:
            return str(self._data.index[section])

class ReportTab(QWidget):
    """Tab for displaying report table with one wavelength per row highlighted and export functionality."""
    def __init__(self, app, results_frame, parent=None):
        super().__init__(parent)
        self.app = app
        self.results_frame = results_frame
        self.report_data = None
        self.current_view_df = None
        self.selected_columns = []
        self.calibration_ranges = {}
        self.best_wavelengths_per_row = {}
        self.base_elements = {}
        self.column_widths = {}
        self.logger = logging.getLogger(__name__)  # Initialize logger
        self.setup_ui()

    def setup_ui(self):
        """Set up the UI for ReportTab."""
        self.setStyleSheet(common_styles)

        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(15, 15, 15, 15)
        main_layout.setSpacing(15)

        # Control group
        control_group = QGroupBox("Report Controls")
        control_layout = QHBoxLayout(control_group)
        control_layout.setSpacing(10)

        # Load button
        load_btn = QPushButton("Load Report")
        load_btn.setMinimumWidth(80)
        load_btn.setEnabled(False)
        load_btn.clicked.connect(self.start_load_report)
        control_layout.addWidget(load_btn)

        # Export button
        export_btn = QPushButton("Export Report")
        export_btn.setMinimumWidth(80)
        export_btn.setEnabled(False)
        export_btn.clicked.connect(self.start_export_report)
        control_layout.addWidget(export_btn)

        control_layout.addStretch()
        main_layout.addWidget(control_group)

        # Table group
        table_group = QGroupBox("Report Table")
        table_layout = QVBoxLayout(table_group)
        table_layout.setSpacing(10)

        # Table view
        self.table_view = QTableView()
        self.table_view.setAlternatingRowColors(True)
        self.table_view.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
        self.table_view.setSortingEnabled(True)
        self.table_view.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        table_layout.addWidget(self.table_view)

        main_layout.addWidget(table_group, stretch=1)

    def generate_report_data(self):
        """Generate report DataFrame and select best wavelengths per row."""
        pivot_tab = getattr(self.results_frame, 'pivot_tab', self.results_frame)
        pivot_data = getattr(pivot_tab, 'last_filtered_data', None)
        original_df = getattr(pivot_tab, 'original_df', None) or getattr(self.app, 'data', None)

        if pivot_data is None or original_df is None:
            self.logger.warning("Pivot data or original DataFrame is None")
            return None

        # Group columns by base element
        self.base_elements = defaultdict(list)
        for col in pivot_data.columns:
            if col != 'Solution Label':
                base_elem = col.split()[0]
                self.base_elements[base_elem].append(col)

        # Calculate calibration ranges for each wavelength
        self.calibration_ranges = {}
        concentration_column = self.get_concentration_column(original_df) if original_df is not None else None
        if concentration_column is None:
            self.logger.warning("No valid concentration column found, setting all calibration ranges to [0 to 0]")
            for col in pivot_data.columns:
                if col != 'Solution Label':
                    self.calibration_ranges[col] = "[0 to 0]"
        else:
            for col in pivot_data.columns:
                if col != 'Solution Label':
                    # Extract base element name (check for '_' in last two characters)
                    element_name = col[:-2] if len(col) >= 2 and col[-2] == '_' else col
                    std_data = original_df[
                        (original_df['Type'] == 'Std') & 
                        (original_df['Element'] == element_name)
                    ][concentration_column]
                    # Filter valid numeric values
                    std_data_numeric = [float(x) for x in std_data if isinstance(x, (int, float, str)) and str(x).replace('.', '', 1).isdigit()]
                    if not std_data_numeric:
                        self.calibration_ranges[col] = "[0 to 0]"
                    else:
                        calibration_min = min(std_data_numeric)
                        calibration_max = max(std_data_numeric)
                        # Format calibration range as string
                        self.calibration_ranges[col] = f"[{calibration_min:.2f} to {calibration_max:.2f}]"
        # Select best wavelength for each base element per row
        self.best_wavelengths_per_row = {}
        self.selected_columns = ['Solution Label']

        for row in range(len(pivot_data)):
            self.best_wavelengths_per_row[row] = {}
            row_label = pivot_data.index[row]
            for base_elem, wavelengths in self.base_elements.items():
                best_wavelength = self.select_best_wavelength_for_row(row, base_elem, wavelengths, pivot_data)
                if best_wavelength:
                    self.best_wavelengths_per_row[row][base_elem] = best_wavelength
                    if best_wavelength not in self.selected_columns:
                        self.selected_columns.append(best_wavelength)

        return pivot_data.copy()

    def select_best_wavelength_for_row(self, row, base_elem, wavelengths, pivot_data):
        """Select the best wavelength for a base element in a specific row based on concentration."""
        if not wavelengths:
            return None

        valid_wavelengths = []
        distances = []
        # Use the 'Solution Label' column from pivot_data instead of index
        row_label = pivot_data['Solution Label'].iloc[row]
        
        # Access original_df from pivot_tab or app
        pivot_tab = getattr(self.results_frame, 'pivot_tab', self.results_frame)
        original_df = getattr(pivot_tab, 'original_df', None) or getattr(self.app, 'data', None)
        
        if original_df is None:
            self.logger.warning(f"No original DataFrame available for row {row_label}")
            return None

        # Filter original_df for the specific Solution Label and Sample/Samp type
        df_subset = original_df[
            (original_df['Solution Label'] == row_label) &
            (original_df['Type'].isin(['Sample', 'Samp']))
        ]

        # Determine which concentration column to use
        conc_column = self.get_concentration_column(df_subset)
        if conc_column is None:
            self.logger.warning(f"No valid concentration column for {row_label}")
            return None

        for wl in wavelengths:
            # Extract base element name (remove trailing '_1', '_2', etc.)
            element_name = wl[:-2] if len(wl) >= 2 and wl[-2] == '_' else wl

            # Get concentration from df_subset for this Solution Label and Element
            conc_data = df_subset[df_subset['Element'] == element_name][conc_column]
            if conc_data.empty:
                self.logger.debug(f"No {conc_column} data for {row_label}, {element_name}")
                continue

            # Use the first concentration value (assuming one per Solution Label and Element)
            conc = conc_data.iloc[0]
            if pd.isna(conc) or not self.is_numeric(conc):
                self.logger.debug(f"Invalid {conc_column} for {row_label}, {element_name}: {conc}")
                continue

            try:
                conc = float(conc)
            except (ValueError, TypeError):
                self.logger.debug(f"Cannot convert {conc_column} to float for {row_label}, {element_name}: {conc}")
                continue

            # Get calibration range for the wavelength
            cal_range = self.calibration_ranges.get(wl, "[0 to 0]")
            try:
                range_parts = cal_range.strip('[]').split(' to ')
                if len(range_parts) != 2:
                    self.logger.warning(f"Invalid calibration range format for {wl}: {cal_range}")
                    continue
                cal_min = float(range_parts[0])
                cal_max = float(range_parts[1])
            except (ValueError, TypeError, IndexError) as e:
                self.logger.warning(f"Failed to parse calibration range for {wl}: {cal_range}, error: {str(e)}")
                cal_min, cal_max = 0, float('inf')

            if cal_min == float('inf') or cal_max == float('-inf'):
                continue

            # Get Corr Con from pivot_data to display
            corr_con = pivot_data.iloc[row][wl]
            if pd.isna(corr_con) or not self.is_numeric(corr_con):
                self.logger.debug(f"Invalid Corr Con for {row_label}, {wl}: {corr_con}")
                continue

            # Compare concentration against calibration range
            if cal_min <= conc <= cal_max:
                valid_wavelengths.append((wl, corr_con, 0))  # Store Corr Con for display
            else:
                distance = min(abs(conc - cal_min), abs(conc - cal_max))
                distances.append((wl, corr_con, distance))  # Store Corr Con for display

        if len(valid_wavelengths) == 1:
            return valid_wavelengths[0][0]
        elif valid_wavelengths or distances:
            candidates = valid_wavelengths + distances
            return min(candidates, key=lambda x: x[2])[0]  # Select wavelength with minimum distance
        return None

    def update_report_display(self):
        """Update the report table display with one wavelength per row highlighted."""
        if self.report_data is None or self.report_data.empty:
            self.table_view.setModel(None)
            return

        self.current_view_df = self.report_data.copy()
        model = PivotTableModel(
            self,
            self.current_view_df,
            calibration_ranges=self.calibration_ranges,
            selected_columns=self.selected_columns,
            best_wavelengths_per_row=self.best_wavelengths_per_row
        )
        self.table_view.setModel(model)
        self.table_view.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        for col, width in self.column_widths.items():
            if col < len(self.current_view_df.columns):
                self.table_view.horizontalHeader().resizeSection(col, width)
        self.table_view.viewport().update()

    def start_load_report(self):
        """Start the report loading process in a separate thread with a progress bar."""
        self.progress_dialog = QProgressDialog("Loading report data...", "Cancel", 0, 100, self)
        self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        self.progress_dialog.setMinimumDuration(0)
        self.progress_dialog.setValue(0)

        self.load_thread = LoadReportThread(self)
        self.load_thread.progress.connect(self.progress_dialog.setValue)
        self.load_thread.finished.connect(self.on_load_report_finished)
        self.load_thread.error.connect(self.on_load_report_error)
        self.progress_dialog.canceled.connect(self.load_thread.terminate)
        self.load_thread.start()

    def on_load_report_finished(self, report_data):
        """Handle completion of report loading."""
        self.report_data = report_data
        self.progress_dialog.setValue(100)  # Ensure progress bar completes
        self.update_report_display()
        self.progress_dialog.close()  # Close progress dialog after table update
        QMessageBox.information(self, "Success", "Report data loaded successfully!")

    def on_load_report_error(self, error_message):
        """Handle errors during report loading."""
        self.progress_dialog.close()
        QMessageBox.warning(self, "Error", error_message)

    def start_export_report(self):
        """Start the report exporting process in a separate thread with a progress bar."""
        if self.report_data is None or self.report_data.empty:
            QMessageBox.warning(self, "Warning", "No report data to export!")
            return

        file_path, _ = QFileDialog.getSaveFileName(self, "Save Report", "", "Excel Files (*.xlsx);;CSV Files (*.csv)")
        if not file_path:
            return

        self.progress_dialog = QProgressDialog("Exporting report...", "Cancel", 0, 100, self)
        self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        self.progress_dialog.setMinimumDuration(0)
        self.progress_dialog.setValue(0)

        self.export_thread = ExportReportThread(self, file_path)
        self.export_thread.progress.connect(self.progress_dialog.setValue)
        self.export_thread.finished.connect(lambda: [self.progress_dialog.setValue(100), self.progress_dialog.close(), QMessageBox.information(self, "Success", "Report exported successfully!")])
        self.export_thread.error.connect(lambda error: [self.progress_dialog.close(), QMessageBox.warning(self, "Error", error)])
        self.progress_dialog.canceled.connect(self.export_thread.terminate)
        self.export_thread.start()

    def format_value(self, x):
        """Format value for display."""
        try:
            pivot_tab = getattr(self.results_frame, 'pivot_tab', None)
            d = int(pivot_tab.decimal_places.currentText()) if pivot_tab and hasattr(pivot_tab, 'decimal_places') else 1
            return f"{float(x):.{d}f}"
        except (ValueError, TypeError):
            return "" if pd.isna(x) or x is None else str(x)

    def calculate_dynamic_range(self, value):
        """Calculate dynamic range for a value."""
        try:
            value = float(value)
            abs_value = abs(value)
            if abs_value < 10:
                return 2
            elif 10 <= abs_value < 100:
                return abs_value * 0.2
            else:
                return abs_value * 0.05
        except (ValueError, TypeError):
            return 0

    def reset_state(self):
        """Reset all internal state and UI elements to their initial values."""
        # Reset internal state variables
        self.report_data = None
        self.current_view_df = None
        self.selected_columns = []
        self.calibration_ranges = {}
        self.best_wavelengths_per_row = {}
        self.base_elements = {}
        self.column_widths = {}

        # Reset UI elements
        if hasattr(self, 'table_view'):
            self.table_view.setModel(None)  # Clear the table view
            self.table_view.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)

        # Stop any running threads
        if hasattr(self, 'load_thread') and self.load_thread is not None and self.load_thread.isRunning():
            self.load_thread.terminate()
            self.load_thread = None
            if hasattr(self, 'progress_dialog') and self.progress_dialog is not None:
                self.progress_dialog.close()

        if hasattr(self, 'export_thread') and self.export_thread is not None and self.export_thread.isRunning():
            self.export_thread.terminate()
            self.export_thread = None
            if hasattr(self, 'progress_dialog') and self.progress_dialog is not None:
                self.progress_dialog.close()

        # Notify data change to refresh dependent components
        if hasattr(self.app, 'notify_data_changed'):
            self.app.notify_data_changed()

        self.logger.debug("ReportTab state reset")