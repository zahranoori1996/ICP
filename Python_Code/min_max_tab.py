import sys
import sqlite3
import pandas as pd
import numpy as np
from PyQt6.QtWidgets import (
    QApplication, QWidget, QVBoxLayout, QHBoxLayout, QSplitter,
    QListWidget, QListWidgetItem, QAbstractItemView, QLabel, QSlider, QFormLayout,
    QTableView, QHeaderView, QPushButton, QStatusBar, QMessageBox,
    QFileDialog, QComboBox, QToolBar, QProgressDialog, QLineEdit, QDialog
)
from PyQt6.QtCore import Qt, QAbstractTableModel, QModelIndex, pyqtSignal, QThread, pyqtSlot
from PyQt6.QtGui import QKeySequence, QAction, QColor
import pyqtgraph as pg
import pyqtgraph.exporters
import os
import time
import logging
import matplotlib
import matplotlib.pyplot as plt
from matplotlib.backends.backend_qtagg import FigureCanvasQTAgg as FigureCanvas
from matplotlib.figure import Figure
from db.db import get_elements_db,resource_path
from utils.var_main import EXCELS_ELEMENTS_PATH
# تنظیم لاگ برای دیباگینگ
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s - %(message)s', filename='app.log')



TABLE_NAME = "elements_data"
SAMPLE_ID_COL = "SAMPLE ID"
ESI_CODE_COL = "ESI CODE"

# -------------------------
class PandasModel(QAbstractTableModel):
    def __init__(self, df=pd.DataFrame(), parent=None):
        super().__init__(parent)
        self._data = df.copy()
        self.color_element = None
        self.color_min = 0.0
        self.color_max = 1.0

    def rowCount(self, parent=QModelIndex()):
        return len(self._data.index)

    def columnCount(self, parent=QModelIndex()):
        return len(self._data.columns)

    def data(self, index, role=Qt.ItemDataRole.DisplayRole):
        if not index.isValid():
            return None
        col_name = self._data.columns[index.column()]
        val = self._data.iat[index.row(), index.column()]
        if role == Qt.ItemDataRole.DisplayRole:
            return "" if pd.isna(val) else str(val)
        elif role == Qt.ItemDataRole.BackgroundRole and self.color_element is not None:
            if col_name == SAMPLE_ID_COL:
                return None  # بدون رنگ برای SAMPLE ID
            if col_name == self.color_element:
                if pd.isna(val):
                    return None
                norm = (val - self.color_min) / (self.color_max - self.color_min) if self.color_max > self.color_min else 0.5
                norm = np.clip(norm, 0, 1)
                cmap = matplotlib.colormaps['jet']
                r, g, b, _ = cmap(norm)
                return QColor(int(r * 255), int(g * 255), int(b * 255))
            else:
                return QColor(0, 0, 0)  # سیاه برای بقیه ستون‌ها
        elif role == Qt.ItemDataRole.ForegroundRole and self.color_element is not None:
            if col_name == SAMPLE_ID_COL:
                return None  # رنگ پیش‌فرض برای SAMPLE ID
            if col_name == self.color_element:
                return QColor(255, 255, 255)  # سفید برای متن روی رنگ jet
            else:
                return QColor(255, 255, 255)  # سفید برای متن بقیه
        return None

    def headerData(self, section, orientation, role=Qt.ItemDataRole.DisplayRole):
        if role != Qt.ItemDataRole.DisplayRole:
            return None
        if orientation == Qt.Orientation.Horizontal:
            return str(self._data.columns[section])
        else:
            return str(self._data.index[section] + 1)

    def setDataFrame(self, df):
        self.beginResetModel()
        self._data = df.copy()
        self.endResetModel()

    def set_color_params(self, element, min_val, max_val):
        self.color_element = element
        self.color_min = min_val
        self.color_max = max_val
        self.dataChanged.emit(self.createIndex(0, 0), self.createIndex(self.rowCount() - 1, self.columnCount() - 1))

# -------------------------
class DataLoaderThread(QThread):
    dataLoaded = pyqtSignal(dict)
    error = pyqtSignal(str)
    progress = pyqtSignal(int)

    def __init__(self, db_path, elements, parent=None):
        super().__init__(parent)
        self.db_path = db_path
        self.elements = elements

    def run(self):
        data = {}
        total = len(self.elements)
        conn = None
        try:
            conn =get_elements_db()
            for i, elem in enumerate(self.elements):
                try:
                    q = f"SELECT [{elem}] FROM {TABLE_NAME}"
                    df = pd.read_sql(q, conn)
                    y_vals = pd.to_numeric(df[elem], errors='coerce').dropna().values / 10000
                    x_idx = np.arange(len(y_vals)).tolist()
                    if len(y_vals) == 0:
                        logging.warning(f"No valid data for element {elem}")
                        continue
                    data[elem] = (x_idx, y_vals)
                    logging.debug(f"Loaded {elem}: {len(y_vals)} points, sample y_vals={y_vals[:5]}")
                    time.sleep(0.1)  # Simulate delay
                except Exception as e:
                    logging.error(f"Error loading {elem}: {e}")
                    self.error.emit(f"Error loading {elem}: {str(e)}")
                    return
                self.progress.emit(int((i + 1) / total * 100))
            if not data:
                logging.warning("No valid data loaded for any elements")
                self.error.emit("No valid data found for selected elements")
            self.dataLoaded.emit(data)
        except Exception as e:
            logging.error(f"Database error: {e}")
            self.error.emit(str(e))
        finally:
            if conn:
                pass

# -------------------------
class TableLoaderThread(QThread):
    dataLoaded = pyqtSignal(pd.DataFrame)
    error = pyqtSignal(str)
    progress = pyqtSignal(int)

    def __init__(self, db_path, elements, current_range, has_sample_id, y_range=None, parent=None):
        super().__init__(parent)
        self.db_path = db_path
        self.elements = elements
        self.current_range = current_range
        self.has_sample_id = has_sample_id
        self.y_range = y_range

    def run(self):
        conn = None
        try:
            conn =get_elements_db()
            cols = ", ".join([f"[{c}]" for c in self.elements])
            if self.has_sample_id:
                cols = f"[{SAMPLE_ID_COL}], {cols}"
            q = f"SELECT {cols} FROM {TABLE_NAME}"
            df = pd.read_sql(q, conn)
            for c in df.columns:
                if c != SAMPLE_ID_COL:
                    df[c] = pd.to_numeric(df[c], errors='coerce') / 10000
            min_idx, max_idx = self.current_range
            max_idx = min(max_idx, len(df))
            df_slice = df.iloc[min_idx:max_idx].copy()

            # اعمال فیلتر بازه Y اگر لازم باشد
            if self.y_range:
                min_y, max_y = self.y_range
                mask = True
                for elem in self.elements:
                    if elem in df_slice:
                        mask &= (df_slice[elem] >= min_y) & (df_slice[elem] <= max_y)
                df_slice = df_slice[mask].reset_index(drop=True)

            logging.debug(f"Table data loaded: {len(df_slice)} rows, columns={df_slice.columns.tolist()}")
            for i in range(101):
                time.sleep(0.01)
                self.progress.emit(i)
            self.dataLoaded.emit(df_slice)
        except Exception as e:
            logging.error(f"Table load error: {e}")
            self.error.emit(str(e))
        finally:
            if conn:
                pass

# -------------------------
class VisLoaderThread(QThread):
    finished = pyqtSignal(dict)
    progress = pyqtSignal(int)
    error = pyqtSignal(str)

    def __init__(self, df, parent=None):
        super().__init__(parent)
        self.df = df

    def run(self):
        try:
            data = {}
            self.progress.emit(0)

            # Heatmap data
            if not self.df.empty:
                numeric_df = self.df.select_dtypes(include=np.number)
                # بهینه‌سازی: اگر تعداد ردیف‌ها زیاد باشد، subsample کنیم
                max_rows = 500  # حداکثر ردیف برای نمایش بدون subsample
                if len(numeric_df) > max_rows:
                    numeric_df = numeric_df.sample(n=max_rows, random_state=42)
                data['heatmap'] = {
                    'values': numeric_df.values,
                    'xticklabels': numeric_df.columns.tolist(),
                    'yticklabels': self.df[SAMPLE_ID_COL].tolist() if SAMPLE_ID_COL in self.df else numeric_df.index.tolist()
                }
            else:
                data['heatmap'] = None
            self.progress.emit(50)

            # Violin data
            if not self.df.empty:
                numeric_df = self.df.select_dtypes(include=np.number)
                data['violin'] = {
                    'values': numeric_df.values,
                    'xticklabels': numeric_df.columns.tolist()
                }
            else:
                data['violin'] = None
            self.progress.emit(100)

            self.finished.emit(data)
        except Exception as e:
            self.error.emit(str(e))

# -------------------------
class PlotArea(QWidget):
    statusMessage = pyqtSignal(str)

    def __init__(self, db_path, elements, parent=None):
        super().__init__(parent)
        self.db_path = db_path
        self.elements = elements
        self.data_cache = {}
        self.total_rows = self._get_total_rows()
        self.has_sample_id = self._check_sample_id_column()
        self.sample_ids = self._load_sample_ids() if self.has_sample_id else []
        layout = QVBoxLayout(self)

        # Toolbar
        toolbar = QToolBar()
        self.plot_type_combo = QComboBox()
        self.plot_type_combo.addItems(["Line", "Scatter", "Bar", "Histogram", "Box Plot"])
        self.plot_type_combo.currentIndexChanged.connect(self.refresh_plot)
        toolbar.addWidget(QLabel("Plot Type:"))
        toolbar.addWidget(self.plot_type_combo)

        self.x_axis_combo = QComboBox()
        self.x_axis_combo.addItems(["Index", "Sample ID"] if self.has_sample_id else ["Index"])
        self.x_axis_combo.currentIndexChanged.connect(self.refresh_plot)
        toolbar.addWidget(QLabel("X-Axis:"))
        toolbar.addWidget(self.x_axis_combo)

        export_btn = QPushButton("Export Plot")
        export_btn.clicked.connect(self.export_plot)
        export_btn.setMaximumWidth(100)
        export_btn.setStyleSheet("padding: 5px;")
        toolbar.addWidget(export_btn)

        fit_btn = QPushButton("Fit Data")
        fit_btn.clicked.connect(self.fit_data)
        fit_btn.setMaximumWidth(100)
        fit_btn.setStyleSheet("padding: 5px;")
        toolbar.addWidget(fit_btn)

        export_dataset_btn = QPushButton("Export Dataset to CSV")
        export_dataset_btn.clicked.connect(self.export_dataset)
        export_dataset_btn.setMaximumWidth(150)
        export_dataset_btn.setStyleSheet("padding: 5px;")
        toolbar.addWidget(export_dataset_btn)

        vis_btn = QPushButton("Additional Visualizations")
        vis_btn.clicked.connect(self.start_vis_thread)
        vis_btn.setMaximumWidth(150)
        vis_btn.setStyleSheet("padding: 5px;")
        toolbar.addWidget(vis_btn)

        layout.addWidget(toolbar)

        # Plot
        self.plot_widget = pg.PlotWidget()
        self.plot_widget.setBackground("w")
        self.plot_widget.showGrid(x=True, y=True)
        self.plot_widget.setLabel("bottom", self.x_axis_combo.currentText())
        self.plot_widget.setLabel("left", "Concentration (divided by 10,000)")
        self.plot_widget.setMouseEnabled(x=True, y=True)
        self.plot_widget.scene().sigMouseClicked.connect(self.on_mouse_clicked)
        layout.addWidget(self.plot_widget)

        # Range Selector UI
        range_widget = QWidget()
        range_layout = QFormLayout(range_widget)
        self.min_y_slider = QSlider(Qt.Orientation.Horizontal)
        self.max_y_slider = QSlider(Qt.Orientation.Horizontal)
        self.min_y_label = QLabel("Min: 0.0")
        self.max_y_label = QLabel("Max: 0.0")

        for slider in (self.min_y_slider, self.max_y_slider):
            slider.setMinimum(0)
            slider.setMaximum(1000)
            slider.setSingleStep(1)
            slider.setValue(0)

        self.min_y_slider.valueChanged.connect(self._on_min_y_slider_changed)
        self.max_y_slider.valueChanged.connect(self._on_max_y_slider_changed)

        range_layout.addRow("Min Concentration:", self.min_y_label)
        range_layout.addRow(self.min_y_slider)
        range_layout.addRow("Max Concentration:", self.max_y_label)
        range_layout.addRow(self.max_y_slider)

        layout.addWidget(range_widget)

        # Apply and Reset Buttons
        btn_layout = QHBoxLayout()
        self.apply_btn = QPushButton("Apply Range")
        self.reset_btn = QPushButton("Reset Range")
        self.apply_btn.setMaximumWidth(100)
        self.reset_btn.setMaximumWidth(100)
        self.apply_btn.setStyleSheet("padding: 5px;")
        self.reset_btn.setStyleSheet("padding: 5px;")
        self.apply_btn.clicked.connect(self.apply_range_and_refresh)
        self.reset_btn.clicked.connect(self.reset_range)
        btn_layout.addWidget(self.apply_btn)
        btn_layout.addWidget(self.reset_btn)
        layout.addLayout(btn_layout)

        self.current_elements = []
        self.current_y_range = (0.0, 1.0)
        self.current_range = (0, self.total_rows)
        self.loader_thread = None
        self.progress_dialog = None
        self.info_text = pg.TextItem("", anchor=(0, 0), color=(0, 0, 0))
        self.plot_widget.addItem(self.info_text)
        self.info_text.hide()
        self.plot_items = []
        self.y_min_global = 0.0
        self.y_max_global = 1.0
        self.vis_thread = None
        self.vis_progress = None

    def _get_total_rows(self):
        conn = None
        try:
            conn =get_elements_db()
            q = f"SELECT COUNT(*) AS cnt FROM {TABLE_NAME}"
            df = pd.read_sql(q, conn)
            total = int(df['cnt'].iloc[0])
            logging.debug(f"Total rows in database: {total}")
            return total
        except Exception as e:
            logging.error(f"Error getting total rows: {e}")
            self.statusMessage.emit(f"Error accessing database: {str(e)}")
            return 0
        finally:
            if conn:
                pass

    def _check_sample_id_column(self):
        conn = None
        try:
            conn =get_elements_db()
            cursor = conn.cursor()
            cursor.execute(f"PRAGMA table_info({TABLE_NAME})")
            columns = [row[1] for row in cursor.fetchall()]
            logging.debug(f"Columns in table: {columns}")
            return SAMPLE_ID_COL in columns or ESI_CODE_COL in columns
        except Exception as e:
            logging.error(f"Error checking SampleID/ESI CODE column: {e}")
            self.statusMessage.emit(f"Error checking columns: {str(e)}")
            return False
        finally:
            if conn:
               pass 

    def _load_sample_ids(self):
        conn = None
        try:
            conn =get_elements_db()
            filename = os.path.basename(self.db_path)
            sample_id_col = SAMPLE_ID_COL if SAMPLE_ID_COL in pd.read_sql(f"SELECT * FROM {TABLE_NAME} LIMIT 1", conn).columns else ESI_CODE_COL
            df = pd.read_sql(f"SELECT [{sample_id_col}] FROM {TABLE_NAME}", conn)
            ids = df[sample_id_col].values.tolist()
            # اگر Sample ID خالی باشد, نشان نده - فیلتر ردیف‌هایی که ID ندارند
            non_null_ids = [f"{filename}_{id}" if pd.notna(id) else None for id in ids]
            logging.debug(f"Loaded {len(non_null_ids)} Sample IDs")
            return [id for id in non_null_ids if id is not None]
        except Exception as e:
            logging.error(f"Error loading Sample IDs: {e}")
            self.statusMessage.emit(f"Error loading Sample IDs: {str(e)}")
            return []
        finally:
            if conn:
               pass 

    def _update_y_range(self):
        y_min = float('inf')
        y_max = float('-inf')
        for elem in self.current_elements:
            if elem in self.data_cache:
                _, y_vals = self.data_cache[elem]
                if len(y_vals) > 0:
                    y_min = min(y_min, np.min(y_vals))
                    y_max = max(y_max, np.max(y_vals))
        if y_min == float('inf') or y_max == float('-inf'):
            y_min, y_max = 0.0, 1.0
        self.y_min_global = y_min
        self.y_max_global = y_max

        slider_max = 1000
        self.min_y_slider.setMinimum(0)
        self.min_y_slider.setMaximum(slider_max)
        self.max_y_slider.setMinimum(0)
        self.max_y_slider.setMaximum(slider_max)

        self.min_y_slider.setValue(0)
        self.max_y_slider.setValue(slider_max)
        self.current_y_range = (y_min, y_max)
        self.min_y_label.setText(f"Min: {y_min:.2f}")
        self.max_y_label.setText(f"Max: {y_max:.2f}")
        logging.debug(f"Y range updated: [{y_min}, {y_max}]")

    def _on_min_y_slider_changed(self, value):
        slider_max = self.max_y_slider.maximum()
        y_range = self.y_max_global - self.y_min_global if self.y_max_global > self.y_min_global else 1.0
        min_y = self.y_min_global + (value / slider_max) * y_range
        max_y = self.current_y_range[1]
        if min_y > max_y:
            min_y = max_y
            self.min_y_slider.setValue(int(((max_y - self.y_min_global) / y_range) * slider_max))
        self.current_y_range = (min_y, max_y)
        self.min_y_label.setText(f"Min: {min_y:.2f}")
        logging.debug(f"Min Y changed to {min_y}")
        self.plot_elements(self.current_elements)

    def _on_max_y_slider_changed(self, value):
        slider_max = self.max_y_slider.maximum()
        y_range = self.y_max_global - self.y_min_global if self.y_max_global > self.y_min_global else 1.0
        max_y = self.y_min_global + (value / slider_max) * y_range
        min_y = self.current_y_range[0]
        if max_y < min_y:
            max_y = min_y
            self.max_y_slider.setValue(int(((min_y - self.y_min_global) / y_range) * slider_max))
        self.current_y_range = (min_y, max_y)
        self.max_y_label.setText(f"Max: {max_y:.2f}")
        logging.debug(f"Max Y changed to {max_y}")
        self.plot_elements(self.current_elements)

    def apply_range_and_refresh(self):
        logging.debug("Applying Y range and refreshing plot")
        self.plot_elements(self.current_elements)

    def reset_range(self):
        self.current_y_range = (self.y_min_global, self.y_max_global)
        self.min_y_slider.setValue(0)
        self.max_y_slider.setValue(self.max_y_slider.maximum())
        self.min_y_label.setText(f"Min: {self.y_min_global:.2f}")
        self.max_y_label.setText(f"Max: {self.y_max_global:.2f}")
        self.current_range = (0, self.total_rows)
        logging.debug("Resetting Y range")
        self.plot_elements(self.current_elements)
        self.fit_data()

    def load_data_for_elements(self, elements):
        self.current_elements = elements or []
        missing = [el for el in elements if el not in self.data_cache]
        if not missing:
            logging.debug("No missing elements, plotting directly")
            self._update_y_range()
            self.plot_elements(elements)
            return

        self.progress_dialog = QProgressDialog("Loading data...", "Cancel", 0, 100, self)
        self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        self.progress_dialog.show()

        self.loader_thread = DataLoaderThread(self.db_path, missing)
        self.loader_thread.progress.connect(self.progress_dialog.setValue)
        self.loader_thread.dataLoaded.connect(self.on_data_loaded)
        self.loader_thread.error.connect(self.on_load_error)
        self.loader_thread.start()

    @pyqtSlot(dict)
    def on_data_loaded(self, data):
        self.data_cache.update(data)
        self.progress_dialog.hide()
        self._update_y_range()
        logging.debug(f"Data loaded, cache keys: {list(self.data_cache.keys())}")
        self.plot_elements(self.current_elements)
        self.fit_data()

    @pyqtSlot(str)
    def on_load_error(self, msg):
        self.progress_dialog.hide()
        QMessageBox.critical(self, "Load Error", msg)
        logging.error(f"Data load error: {msg}")

    def refresh_plot(self):
        self.plot_widget.setLabel("bottom", self.x_axis_combo.currentText())
        logging.debug("Refreshing plot")
        self._update_y_range()
        self.plot_elements(self.current_elements)
        self.fit_data()

    def plot_elements(self, elements):
        self.current_elements = elements or []
        self.plot_widget.clear()
        plot_type = self.plot_type_combo.currentText()
        x_type = self.x_axis_combo.currentText()
        if not elements:
            self.statusMessage.emit("No elements selected.")
            self.plot_widget.addItem(self.info_text)
            logging.debug("No elements selected for plotting")
            return

        colors = [(200, 50, 50), (50, 180, 50), (50, 50, 200), (200, 120, 0), (150, 50, 150), (50, 180, 180), (100, 100, 100)]
        gray = (150, 150, 150)
        min_idx, max_idx_excl = self.current_range
        legend = self.plot_widget.addLegend(offset=(10, 10)) if plot_type not in ["Histogram", "Box Plot"] else None
        self.plot_items = []
        any_data_plotted = False
        min_y, max_y = self.current_y_range

        for i, elem in enumerate(elements):
            if elem not in self.data_cache:
                logging.warning(f"Element {elem} not in cache")
                continue
            x_idx, y_vals = self.data_cache[elem]
            if len(y_vals) == 0:
                logging.warning(f"No data for element {elem}")
                continue

            max_idx_excl = min(max_idx_excl, len(y_vals)) if max_idx_excl > 0 else len(y_vals)
            min_idx = min(min_idx, len(y_vals))

            y_vals_slice = y_vals[min_idx:max_idx_excl]
            indices = np.arange(min_idx, max_idx_excl)
            if x_type == "Index":
                x_all = indices
            else:
                if len(self.sample_ids) < len(y_vals):
                    logging.warning(f"Sample IDs length ({len(self.sample_ids)}) is less than data length ({len(y_vals)})")
                    continue
                x_all = np.array(self.sample_ids)[indices].tolist()

            # همه نقاط
            x = x_all
            y = y_vals_slice

            if len(x) == 0 or len(y) == 0:
                logging.warning(f"No data for element {elem}")
                continue

            # ماسک برای داخل بازه
            mask_in = (y >= min_y) & (y <= max_y)
            mask_out = ~mask_in

            x_in = x[mask_in]
            y_in = y[mask_in]
            x_out = x[mask_out]
            y_out = y[mask_out]

            logging.debug(f"Plotting {elem}: in={len(x_in)}, out={len(x_out)}")

            any_data_plotted = True

            try:
                color = colors[i % len(colors)]
                if plot_type == "Line":
                    # out
                    if len(x_out) > 0:
                        item_out = self.plot_widget.plot(x_out, y_out, pen=pg.mkPen(color=gray, width=2), name=f"{elem} out")
                        self.plot_items.append((item_out, elem + " out", x_out, y_out))
                    # in
                    if len(x_in) > 0:
                        item_in = self.plot_widget.plot(x_in, y_in, pen=pg.mkPen(color=color, width=2), name=elem)
                        self.plot_items.append((item_in, elem, x_in, y_in))
                elif plot_type == "Scatter":
                    # out
                    if len(x_out) > 0:
                        item_out = self.plot_widget.plot(x_out, y_out, symbol='o', symbolBrush=gray, pen=None, name=f"{elem} out")
                        self.plot_items.append((item_out, elem + " out", x_out, y_out))
                    # in
                    if len(x_in) > 0:
                        item_in = self.plot_widget.plot(x_in, y_in, symbol='o', symbolBrush=color, pen=None, name=elem)
                        self.plot_items.append((item_in, elem, x_in, y_in))
                elif plot_type == "Bar":
                    # out
                    if len(x_out) > 0:
                        item_out = pg.BarGraphItem(x=x_out, height=y_out, width=0.6, brush=gray)
                        self.plot_widget.addItem(item_out)
                        self.plot_items.append((item_out, elem + " out", x_out, y_out))
                    # in
                    if len(x_in) > 0:
                        item_in = pg.BarGraphItem(x=x_in, height=y_in, width=0.6, brush=color)
                        self.plot_widget.addItem(item_in)
                        self.plot_items.append((item_in, elem, x_in, y_in))
                elif plot_type == "Histogram":
                    # برای Histogram, جدا برای in و out
                    if len(y_out) > 0:
                        hist_out, bins_out = np.histogram(y_out, bins=20)
                        item_out = pg.PlotCurveItem(bins_out, hist_out, stepMode=True, fillLevel=0, brush=gray)
                        self.plot_widget.addItem(item_out)
                    if len(y_in) > 0:
                        hist_in, bins_in = np.histogram(y_in, bins=20)
                        item_in = pg.PlotCurveItem(bins_in, hist_in, stepMode=True, fillLevel=0, brush=color)
                        self.plot_widget.addItem(item_in)
                elif plot_type == "Box Plot":
                    # برای Box, جدا برای out
                    if len(y_out) > 0:
                        q1, median, q3 = np.percentile(y_out, [25, 50, 75])
                        iqr = q3 - q1
                        lower = q1 - 1.5 * iqr
                        upper = q3 + 1.5 * iqr
                        box_out = pg.PlotDataItem([i-0.1, i-0.1], [q1, q3], pen=pg.mkPen(color=gray))
                        self.plot_widget.addItem(box_out)
                        med_out = pg.PlotDataItem([i - 0.3, i + 0.1], [median, median], pen=pg.mkPen(color=gray, width=3))
                        self.plot_widget.addItem(med_out)
                        whisk_low_out = pg.PlotDataItem([i-0.1, i-0.1], [max(lower, min(y_out)), q1], pen=pg.mkPen(color=gray))
                        self.plot_widget.addItem(whisk_low_out)
                        whisk_high_out = pg.PlotDataItem([i-0.1, i-0.1], [q3, min(upper, max(y_out))], pen=pg.mkPen(color=gray))
                        self.plot_widget.addItem(whisk_high_out)
                    # برای in
                    if len(y_in) > 0:
                        q1, median, q3 = np.percentile(y_in, [25, 50, 75])
                        iqr = q3 - q1
                        lower = q1 - 1.5 * iqr
                        upper = q3 + 1.5 * iqr
                        box = pg.PlotDataItem([i, i], [q1, q3], pen=pg.mkPen(color=color))
                        self.plot_widget.addItem(box)
                        med = pg.PlotDataItem([i - 0.2, i + 0.2], [median, median], pen=pg.mkPen(color=color, width=3))
                        self.plot_widget.addItem(med)
                        whisk_low = pg.PlotDataItem([i, i], [max(lower, min(y_in)), q1], pen=pg.mkPen(color=color))
                        self.plot_widget.addItem(whisk_low)
                        whisk_high = pg.PlotDataItem([i, i], [q3, min(upper, max(y_in))], pen=pg.mkPen(color=color))
                        self.plot_widget.addItem(whisk_high)
            except Exception as e:
                logging.error(f"Error plotting {elem}: {e}")
                self.statusMessage.emit(f"Error plotting {elem}: {str(e)}")

        if plot_type == "Box Plot":
            self.plot_widget.getAxis('bottom').setTicks([[(i, elem) for i, elem in enumerate(elements)]])

        if plot_type == "Histogram" and len(elements) > 1:
            self.statusMessage.emit("Histogram is per element; multiple may overlap.")

        if not any_data_plotted:
            self.statusMessage.emit("No valid data to plot in the selected Y range.")
            logging.warning("No valid data plotted")
        else:
            self.statusMessage.emit(f"Plotted {len(elements)} element(s) with {plot_type}.")

        self.plot_widget.addItem(self.info_text)
        self.fit_data()

    def fit_data(self):
        if not self.plot_items:
            logging.debug("No data to fit")
            self.plot_widget.enableAutoRange()
            return
        x_min = float('inf')
        x_max = float('-inf')
        y_min = self.y_min_global
        y_max = self.y_max_global
        x_type = self.x_axis_combo.currentText()

        for _, _, x, y in self.plot_items:
            if len(x) > 0:
                if x_type == "Index":
                    x_min = min(x_min, min(x))
                    x_max = max(x_max, max(x))
                else:
                    x_min = min(x_min, 0)
                    x_max = max(x_max, len(x) - 1)

        x_range = x_max - x_min if x_max > x_min else 1.0
        x_min -= x_range * 0.05
        x_max += x_range * 0.05

        self.plot_widget.setXRange(x_min, x_max)
        self.plot_widget.setYRange(y_min, y_max)
        logging.debug(f"Fitting data: X range [{x_min}, {x_max}], Y range [{y_min}, {y_max}]")

    def on_mouse_clicked(self, event):
        pos = event.scenePos()
        mouse_point = self.plot_widget.plotItem.vb.mapSceneToView(pos)
        x_val = mouse_point.x()
        y_val = mouse_point.y()

        closest_dist = float('inf')
        closest_sample_id = None
        closest_idx = None

        x_type = self.x_axis_combo.currentText()
        min_idx, _ = self.current_range

        for item, elem, x, y in self.plot_items:
            for i in range(len(x)):
                x_val_i = i if x_type == "Sample ID" else x[i]
                dist = (x_val_i - x_val)**2 + (y[i] - y_val)**2
                if dist < closest_dist:
                    closest_dist = dist
                    actual_idx = min_idx + i
                    closest_sample_id = self.sample_ids[actual_idx] if self.has_sample_id and actual_idx < len(self.sample_ids) else f"Index {actual_idx}"
                    closest_idx = actual_idx

        if closest_dist < 1e4:
            self.info_text.setText(f"Sample ID: {closest_sample_id}\nValue: {y_val:.2f}")
            self.info_text.setPos(x_val, y_val)
            self.info_text.show()
        else:
            self.info_text.hide()

    def export_plot(self):
        if not self.current_elements:
            QMessageBox.warning(self, "Export", "No plot to export.")
            return
        file_path, _ = QFileDialog.getSaveFileName(self, "Export Plot", "", "PNG (*.png);;PDF (*.pdf);;SVG (*.svg)")
        if file_path:
            try:
                if file_path.endswith('.pdf'):
                    exporter = pg.exporters.MatplotlibExporter(self.plot_widget.plotItem)
                    exporter.export(file_path)
                else:
                    exporter = pg.exporters.ImageExporter(self.plot_widget.plotItem)
                    exporter.export(file_path)
                self.statusMessage.emit("Plot exported successfully.")
            except Exception as e:
                QMessageBox.critical(self, "Export Error", str(e))
                logging.error(f"Export error: {e}")

    def export_dataset(self):
        if not self.current_elements:
            QMessageBox.warning(self, "Export", "No elements selected to export.")
            return

        file_path, _ = QFileDialog.getSaveFileName(self, "Export Dataset", "", "CSV (*.csv)")
        if not file_path:
            return

        self.progress_dialog = QProgressDialog("Exporting dataset...", "Cancel", 0, 100, self)
        self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        self.progress_dialog.show()

        self.table_loader_thread = TableLoaderThread(
            self.db_path, self.current_elements, self.current_range, self.has_sample_id, self.current_y_range
        )
        self.table_loader_thread.progress.connect(self.progress_dialog.setValue)
        self.table_loader_thread.dataLoaded.connect(lambda df: self._save_dataset(df, file_path))
        self.table_loader_thread.error.connect(self._on_export_error)
        self.table_loader_thread.start()

    @pyqtSlot(pd.DataFrame)
    def _save_dataset(self, df, file_path):
        try:
            if df.empty:
                self.progress_dialog.hide()
                QMessageBox.warning(self, "Export", "No data to export in the selected Y range.")
                logging.warning("No data to export in the selected Y range")
                return
            df.to_csv(file_path, index=False)
            self.progress_dialog.hide()
            self.statusMessage.emit("Dataset exported successfully.")
            logging.debug(f"Dataset exported to {file_path}")
        except Exception as e:
            self.progress_dialog.hide()
            QMessageBox.critical(self, "Export Error", str(e))
            logging.error(f"Export dataset error: {e}")

    @pyqtSlot(str)
    def _on_export_error(self, msg):
        self.progress_dialog.hide()
        QMessageBox.critical(self, "Export Error", msg)
        logging.error(f"Export dataset error: {msg}")

    def start_vis_thread(self):
        if not self.current_elements:
            QMessageBox.warning(self, "Visualizations", "No elements selected.")
            return

        df = self.window().current_df
        self.vis_progress = QProgressDialog("Loading visualizations...", "Cancel", 0, 100, self)
        self.vis_progress.setWindowModality(Qt.WindowModality.WindowModal)
        self.vis_progress.show()

        self.vis_thread = VisLoaderThread(df)
        self.vis_thread.progress.connect(self.vis_progress.setValue)
        self.vis_thread.finished.connect(self.on_vis_loaded)
        self.vis_thread.error.connect(self.on_vis_error)
        self.vis_thread.start()

    @pyqtSlot(dict)
    def on_vis_loaded(self, data):
        # برای جلوگیری از بسته شدن سریع پروگرس، کمی تأخیر مصنوعی اضافه می‌کنیم (اختیاری, اما برای بهبود تجربه کاربر)
        time.sleep(0.5)  # تأخیر کوتاه برای همگام‌سازی

        dialog = QDialog(self)
        dialog.setWindowTitle("Additional Visualizations")
        layout = QVBoxLayout(dialog)

        # Heatmap
        fig1 = Figure(figsize=(5, 4))
        canvas1 = FigureCanvas(fig1)
        ax1 = fig1.add_subplot(111)
        heatmap = data.get('heatmap')
        if heatmap:
            im = ax1.imshow(heatmap['values'], cmap='jet', aspect='auto')
            ax1.set_xticks(np.arange(len(heatmap['xticklabels'])))
            ax1.set_xticklabels(heatmap['xticklabels'], rotation=45)
            # بهینه‌سازی: اگر تعداد yticklabels زیاد باشد, آن‌ها را حذف یا ساده کنیم
            if len(heatmap['yticklabels']) > 50:
                ax1.set_yticks([])  # حذف لیبل‌های Y برای سرعت بیشتر
            else:
                ax1.set_yticks(np.arange(len(heatmap['yticklabels'])))
                ax1.set_yticklabels(heatmap['yticklabels'])
            fig1.colorbar(im)
            ax1.set_title("Heatmap of Concentrations")
            ax1.spines['top'].set_visible(False)
            ax1.spines['right'].set_visible(False)
            ax1.spines['bottom'].set_visible(False)
            ax1.spines['left'].set_visible(False)
            ax1.grid(False)
        layout.addWidget(canvas1)

        # Violin Plot
        fig2 = Figure(figsize=(5, 4))
        canvas2 = FigureCanvas(fig2)
        ax2 = fig2.add_subplot(111)
        violin = data.get('violin')
        if violin:
            parts = ax2.violinplot(violin['values'], showmeans=True, showmedians=True)
            for pc in parts['bodies']:
                pc.set_facecolor('red')
                pc.set_edgecolor('black')
            ax2.set_xticks(np.arange(1, len(violin['xticklabels']) + 1))
            ax2.set_xticklabels(violin['xticklabels'], rotation=45)
            ax2.set_title("Violin Plot of Concentrations")
        layout.addWidget(canvas2)

        self.vis_progress.hide()
        dialog.exec()

    @pyqtSlot(str)
    def on_vis_error(self, msg):
        self.vis_progress.hide()
        QMessageBox.critical(self, "Visualization Error", msg)
        logging.error(f"Visualization error: {msg}")

# -------------------------
class MinMaxTab(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.db_path = resource_path(EXCELS_ELEMENTS_PATH)
        self.elements = self._load_elements_from_db()
        self.current_df = pd.DataFrame()
        self.table_y_min = 0.0
        self.table_y_max = 1.0
        self.current_color_min = 0.0
        self.current_color_max = 1.0

        layout = QVBoxLayout(self)

        # Splitter
        splitter = QSplitter(Qt.Orientation.Horizontal)

        # Sidebar
        left_widget = QWidget()
        left_layout = QVBoxLayout(left_widget)
        left_layout.addWidget(QLabel("<b>Select elements</b>"))

        self.search_edit = QLineEdit()
        self.search_edit.setPlaceholderText("Search elements...")
        self.search_edit.textChanged.connect(self.filter_elements)
        left_layout.addWidget(self.search_edit)

        self.list_widget = QListWidget()
        self.list_widget.setSelectionMode(QAbstractItemView.SelectionMode.MultiSelection)
        for el in self.elements:
            self.list_widget.addItem(QListWidgetItem(el))
        self.list_widget.itemSelectionChanged.connect(self.on_selection_changed)
        left_layout.addWidget(self.list_widget)

        btn_clear = QPushButton("Clear Selection")
        btn_clear.clicked.connect(lambda: self.list_widget.clearSelection())
        btn_clear.setMaximumWidth(100)
        btn_clear.setStyleSheet("padding: 5px;")
        left_layout.addWidget(btn_clear)

        splitter.addWidget(left_widget)

        # Plot
        self.plot_area = PlotArea(self.db_path, self.elements, self)
        self.plot_area.statusMessage.connect(self.show_status)
        splitter.addWidget(self.plot_area)
        splitter.setStretchFactor(1, 3)

        layout.addWidget(splitter)

        # Table (بدون Dock, مستقیم به layout اضافه می‌شود)
        table_container = QWidget()
        table_layout = QVBoxLayout(table_container)
        self.table_widget = QTableView()
        self.table_model = PandasModel(pd.DataFrame())
        self.table_widget.setModel(self.table_model)
        self.table_widget.setSortingEnabled(True)
        self.table_widget.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        table_layout.addWidget(self.table_widget)

        # Color controls for table
        color_widget = QWidget()
        color_layout = QFormLayout(color_widget)
        self.color_element_combo = QComboBox()
        self.color_element_combo.currentIndexChanged.connect(self.on_color_element_changed)
        color_layout.addRow("Color Element:", self.color_element_combo)

        self.color_min_slider = QSlider(Qt.Orientation.Horizontal)
        self.color_min_slider.setMinimum(0)
        self.color_min_slider.setMaximum(1000)
        self.color_min_slider.setValue(0)
        self.color_min_slider.valueChanged.connect(self._on_color_min_slider_changed)
        self.color_min_label = QLabel("Min: 0.0")
        color_layout.addRow("Min Value:", self.color_min_label)
        color_layout.addRow(self.color_min_slider)

        self.color_max_slider = QSlider(Qt.Orientation.Horizontal)
        self.color_max_slider.setMinimum(0)
        self.color_max_slider.setMaximum(1000)
        self.color_max_slider.setValue(1000)
        self.color_max_slider.valueChanged.connect(self._on_color_max_slider_changed)
        self.color_max_label = QLabel("Max: 1.0")
        color_layout.addRow("Max Value:", self.color_max_label)
        color_layout.addRow(self.color_max_slider)

        apply_color_btn = QPushButton("Apply Color")
        apply_color_btn.clicked.connect(self.apply_table_color)
        color_layout.addRow(apply_color_btn)

        reset_color_btn = QPushButton("Reset Color")
        reset_color_btn.clicked.connect(self.reset_table_color)
        color_layout.addRow(reset_color_btn)

        table_layout.addWidget(color_widget)
        layout.addWidget(table_container)

        self.setLayout(layout)

        # Status bar (به عنوان widget ساده)
        self.status = QStatusBar()
        layout.addWidget(self.status)

        self.open_database()
        # Menu Bar (کامنت شده چون برای تب لازم نیست, اگر نیاز دارید فعال کنید)
        # menubar = QMenuBar(self)
        # layout.addWidget(menubar)
        # file_menu = menubar.addMenu("File")
        # open_db_act = QAction("Open Database", self)
        # open_db_act.triggered.connect(self.open_database)
        # file_menu.addAction(open_db_act)
        # export_data_act = QAction("Export Data to CSV", self)
        # export_data_act.triggered.connect(self.export_dataset)
        # file_menu.addAction(export_data_act)
        # exit_act = QAction("Exit", self)
        # exit_act.setShortcut(QKeySequence.StandardKey.Quit)
        # exit_act.triggered.connect(self.close)
        # file_menu.addAction(exit_act)

        # view_menu = menubar.addMenu("View")
        # toggle_table_act = QAction("Toggle Table", self, checkable=True)
        # toggle_table_act.setChecked(True)
        # toggle_table_act.triggered.connect(lambda checked: table_container.setVisible(checked))
        # view_menu.addAction(toggle_table_act)

    def _load_elements_from_db(self):
        conn = None
        try:
            conn =get_elements_db()
            cursor = conn.cursor()
            cursor.execute(f"PRAGMA table_info({TABLE_NAME})")
            columns = [row[1] for row in cursor.fetchall()]
            print("tyty :",columns)
            excluded_columns = {SAMPLE_ID_COL, ESI_CODE_COL}
            valid_elements = []
            for col in columns:
                if col not in excluded_columns:
                    query = f"SELECT [{col}] FROM {TABLE_NAME} WHERE [{col}] IS NOT NULL LIMIT 1"
                    print("tytyy:",query)
                    df = pd.read_sql(query, conn)
                    if not df.empty:
                        try:
                            pd.to_numeric(df[col].iloc[0], errors='raise')
                            valid_elements.append(col)
                        except (ValueError, TypeError):
                            logging.debug(f"Column {col} does not contain valid numeric data")
                            continue

            logging.debug(f"Loaded valid elements from database: {valid_elements}")
            return sorted(valid_elements)
        except Exception as e:
            logging.error(f"Error loading elements from database: {e}")
            # self.status.showMessage(f"Error loading elements: {str(e)}", 5000)
            return []
        finally:
            if conn:
                pass

    def filter_elements(self, text):
        for i in range(self.list_widget.count()):
            item = self.list_widget.item(i)
            item.setHidden(text.lower() not in item.text().lower())

    def show_status(self, msg):
        self.status.showMessage(msg, 5000)

    def export_dataset(self):
        self.plot_area.export_dataset()

    def on_selection_changed(self):
        elements = [it.text() for it in self.list_widget.selectedItems()]
        logging.debug(f"Selected elements: {elements}")
        self.plot_area.load_data_for_elements(elements)

        self.color_element_combo.clear()
        if elements:
            self.color_element_combo.addItems(elements)

        if elements:
            progress_dialog = QProgressDialog("Loading table data...", "Cancel", 0, 100, self)
            progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
            progress_dialog.show()

            self.table_loader_thread = TableLoaderThread(
                self.db_path, elements, self.plot_area.current_range, self.plot_area.has_sample_id, self.plot_area.current_y_range
            )
            self.table_loader_thread.progress.connect(progress_dialog.setValue)
            self.table_loader_thread.dataLoaded.connect(self.on_table_data_loaded)
            self.table_loader_thread.dataLoaded.connect(progress_dialog.hide)
            self.table_loader_thread.error.connect(self.on_table_load_error)
            self.table_loader_thread.error.connect(progress_dialog.hide)
            self.table_loader_thread.start()
        else:
            self.table_model.setDataFrame(pd.DataFrame())
            logging.debug("No elements selected, clearing table")

    @pyqtSlot(pd.DataFrame)
    def on_table_data_loaded(self, df):
        self.current_df = df
        self.table_model.setDataFrame(df)
        if not df.empty:
            numeric_cols = df.select_dtypes(include=np.number).columns
            if numeric_cols.any():
                self.table_y_min = df[numeric_cols].min().min()
                self.table_y_max = df[numeric_cols].max().max()
            else:
                self.table_y_min = 0.0
                self.table_y_max = 1.0
            self.current_color_min = self.table_y_min
            self.current_color_max = self.table_y_max
            self.color_min_label.setText(f"Min: {self.table_y_min:.2f}")
            self.color_max_label.setText(f"Max: {self.table_y_max:.2f}")
            self.color_min_slider.setValue(0)
            self.color_max_slider.setValue(1000)
        logging.debug("Table data updated")

    @pyqtSlot(str)
    def on_table_load_error(self, msg):
        QMessageBox.critical(self, "Error", msg)
        logging.error(f"Table load error: {msg}")

    def on_color_element_changed(self):
        element = self.color_element_combo.currentText()
        if element and element in self.current_df.columns:
            vals = self.current_df[element].dropna()
            if not vals.empty:
                self.table_y_min = vals.min()
                self.table_y_max = vals.max()
            else:
                self.table_y_min = 0.0
                self.table_y_max = 1.0
            self.current_color_min = self.table_y_min
            self.current_color_max = self.table_y_max
            self.color_min_label.setText(f"Min: {self.table_y_min:.2f}")
            self.color_max_label.setText(f"Max: {self.table_y_max:.2f}")
            self.color_min_slider.setValue(0)
            self.color_max_slider.setValue(1000)

    def _on_color_min_slider_changed(self, value):
        slider_max = 1000
        y_range = self.table_y_max - self.table_y_min if self.table_y_max > self.table_y_min else 1.0
        min_val = self.table_y_min + (value / slider_max) * y_range
        max_val = self.current_color_max
        if min_val > max_val:
            min_val = max_val
            self.color_min_slider.setValue(int(((max_val - self.table_y_min) / y_range) * slider_max))
        self.current_color_min = min_val
        self.color_min_label.setText(f"Min: {min_val:.2f}")

    def _on_color_max_slider_changed(self, value):
        slider_max = 1000
        y_range = self.table_y_max - self.table_y_min if self.table_y_max > self.table_y_min else 1.0
        max_val = self.table_y_min + (value / slider_max) * y_range
        min_val = self.current_color_min
        if max_val < min_val:
            max_val = min_val
            self.color_max_slider.setValue(int(((min_val - self.table_y_min) / y_range) * slider_max))
        self.current_color_max = max_val
        self.color_max_label.setText(f"Max: {max_val:.2f}")

    def apply_table_color(self):
        element = self.color_element_combo.currentText()
        if not element:
            QMessageBox.warning(self, "Color", "No element selected for coloring.")
            return
        self.table_model.set_color_params(element, self.current_color_min, self.current_color_max)
        self.show_status("Table colored based on selected element.")

    def reset_table_color(self):
        self.table_model.set_color_params(None, 0.0, 1.0)
        self.show_status("Table color reset.")

    def open_database(self):
            try:
                self.elements = self._load_elements_from_db()
                self.plot_area.elements = self.elements
                self.plot_area.total_rows = self.plot_area._get_total_rows()
                self.plot_area.has_sample_id = self.plot_area._check_sample_id_column()
                self.plot_area.sample_ids = self.plot_area._load_sample_ids() if self.plot_area.has_sample_id else []
                self.plot_area.x_axis_combo.clear()
                self.plot_area.x_axis_combo.addItems(["Index", "Sample ID"] if self.plot_area.has_sample_id else ["Index"])
                self.plot_area.data_cache.clear()
                self.plot_area.reset_range()

                self.list_widget.clear()
                for el in self.elements:
                    self.list_widget.addItem(QListWidgetItem(el))

                self.on_selection_changed()
                self.show_status("Database opened successfully.")
                logging.debug("Database opened successfully")

            except Exception as e:
                QMessageBox.critical(self, "DB Error", f"Cannot open DB: {e}")
                logging.error(f"DB open error: {e}")