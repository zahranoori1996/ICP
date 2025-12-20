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
logger = logging.getLogger()
logger.setLevel(logging.DEBUG)

def validate_percentage(text):
    """Validate percentage input (must be positive float)."""
    try:
        value = float(text)
        return value > 0
    except (ValueError, TypeError):
        return False
    
def plot_data(self):
    self.plot_widget.clear()
    self.plot_data_items = []
    filtered_crm_df = self.filtered_crm_df_cache if self.filtered_crm_df_cache is not None else self.crm_df
    filtered_blank_df = self.filtered_blank_df_cache if self.filtered_blank_df_cache is not None else self.blank_df
    if filtered_crm_df.empty and filtered_blank_df.empty:
        self.status_label.setText("No data to plot")
        logger.info("No data to plot due to empty filtered dataframes")
        self.plot_df_cache = pd.DataFrame()
        self.update_table(pd.DataFrame(), pd.DataFrame())
        return
    percentage = 10.0
    if validate_percentage(self.percentage_edit.text()):
        percentage = float(self.percentage_edit.text())
    else:
        logger.warning(f"Invalid percentage value: {self.percentage_edit.text()}, using default 10%")
        self.percentage_edit.setText("10")
    filtered_crm_df = filtered_crm_df.sort_values('date')
    colors = ['#FF6B6B', '#4ECDC4', '#45B7D1', '#96CEB4', '#FFEEAD', '#D4A5A5', '#9B59B6']
    plot_df = pd.DataFrame()
    plotted_records = 0
    crm_ids = [self.crm_combo.currentText()] if self.crm_combo.currentText() != "All CRM IDs" else filtered_crm_df['norm_crm_id'].unique()
    logger.debug(f"Plotting for CRM IDs: {crm_ids}")
    for idx, crm_id in enumerate(crm_ids):
        crm_df = filtered_crm_df[filtered_crm_df['norm_crm_id'] == crm_id]
        if crm_df.empty:
            logger.debug(f"No data for CRM ID {crm_id}")
            continue
        current_element = self.selected_element
        ver_value = self.get_verification_value(crm_id, current_element) if current_element != "All Elements" else None
        if current_element != "All Elements" and self.best_wl_check.isChecked() and ver_value is not None:
            def select_best(group):
                group['diff'] = abs(group['value'] - ver_value)
                return group.loc[group['diff'].idxmin()]
            crm_df = crm_df.groupby(['year', 'month', 'day']).apply(select_best).reset_index(drop=True)
        original_df = crm_df.copy()
        if self.apply_blank_check.isChecked() and current_element != "All Elements" and self.crm_combo.currentText() != "All CRM IDs" and ver_value is not None:
            crm_df = crm_df.copy()
            crm_df['original_value'] = crm_df['value']
            crm_df['blank_value'] = pd.NA
            for i, row in crm_df.iterrows():
                blank_value, corrected_value = self.select_best_blank(row, filtered_blank_df, ver_value)
                crm_df.at[i, 'value'] = corrected_value
                crm_df.at[i, 'blank_value'] = blank_value
        indices = np.arange(len(crm_df))
        values = crm_df['value'].values
        original_values = original_df['value'].values if self.apply_blank_check.isChecked() else None
        date_labels = [d for d in crm_df['date']]
        logger.debug(f"CRM {crm_id}: {len(indices)} points, values range: {min(values, default=0):.2f} - {max(values, default=0):.2f}")
        # Adjust x_range for single point
        min_x = 0
        max_x = max(indices, default=0)
        if len(indices) == 1:
            max_x = 1
        x_range = [min_x, max_x]
        pen = mkPen(color=colors[idx % len(colors)], width=2)
        plot_item = self.plot_widget.plot(indices, values, pen=pen, symbol='o', symbolSize=8, name=f"CRM {crm_id} (Corrected)" if self.apply_blank_check.isChecked() else f"CRM {crm_id}")
        self.plot_data_items.append((plot_item, crm_df, indices, date_labels))
        if self.apply_blank_check.isChecked() and original_values is not None:
            original_pen = mkPen(color=colors[(idx + 1) % len(colors)], width=1, style=Qt.PenStyle.DashLine)
            original_plot_item = self.plot_widget.plot(indices, original_values, pen=original_pen, symbol='x', symbolSize=6, name=f"CRM {crm_id} (Original)")
            self.plot_data_items.append((original_plot_item, original_df, indices, date_labels))
        logger.debug(f"Plotted {len(crm_df)} points for CRM ID {crm_id}")
        plotted_records += len(crm_df)
        if current_element != "All Elements" and self.crm_combo.currentText() != "All CRM IDs":
            ver_value = self.get_verification_value(crm_id, current_element)
            if ver_value is not None and not pd.isna(ver_value):
                delta = ver_value * (percentage / 100) / 3
                self.plot_widget.plot(x_range, [ver_value * (1 - percentage / 100)] * 2, pen=mkPen('#FF6B6B', width=2, style=Qt.PenStyle.DotLine), name="LCL")
                self.plot_widget.plot(x_range, [ver_value - 2 * delta] * 2, pen=mkPen('#4ECDC4', width=1, style=Qt.PenStyle.DotLine), name="-2LS")
                self.plot_widget.plot(x_range, [ver_value - delta] * 2, pen=mkPen('#4ECDC4', width=1, style=Qt.PenStyle.DotLine), name="-1LS")
                self.plot_widget.plot(x_range, [ver_value] * 2, pen=mkPen('#000000', width=3, style=Qt.PenStyle.DashLine), name=f"Ref Value ({ver_value:.3f})")
                self.plot_widget.plot(x_range, [ver_value + delta] * 2, pen=mkPen('#45B7D1', width=1, style=Qt.PenStyle.DotLine), name="1LS")
                self.plot_widget.plot(x_range, [ver_value + 2 * delta] * 2, pen=mkPen('#45B7D1', width=1, style=Qt.PenStyle.DotLine), name="2LS")
                self.plot_widget.plot(x_range, [ver_value * (1 + percentage / 100)] * 2, pen=mkPen('#FF6B6B', width=2, style=Qt.PenStyle.DotLine), name="UCL")
                logger.info(f"Plotted control lines for CRM {crm_id}, Element {current_element}")
        plot_df = pd.concat([plot_df, crm_df], ignore_index=True)
    
    self.plot_df_cache = plot_df
    self.update_table(self.filtered_crm_df_cache, self.filtered_blank_df_cache)
    if plotted_records == 0:
        self.status_label.setText("No data to plot")
        logger.info("No data plotted")
    else:
        self.status_label.setText(f"Plotted {plotted_records} records")
        logger.info(f"Plotted {plotted_records} records")
    self.plot_widget.enableAutoRange() # فعال‌سازی Auto Range
    self.update_table(plot_df, pd.DataFrame())

def on_mouse_clicked(self, event):
    if event.button() == Qt.LeftButton:
        pos = self.plot_widget.getViewBox().mapSceneToView(event.scenePos())
        x, y = pos.x(), pos.y()
        logger.debug(f"Click at view coordinates: x={x:.2f}, y={y:.2f}")
        closest_dist = float('inf')
        closest_info = None
        for plot_item, crm_df, indices, date_labels in self.plot_data_items:
            for i, (idx, value, date) in enumerate(zip(indices, crm_df['value'], date_labels)):
                dist = ((idx - x) ** 2 + (value - y) ** 2) ** 0.5
                logger.debug(f"Point {i}: index={idx}, value={value:.2f}, dist={dist:.2f}")
                if dist < 10:
                    closest_dist = dist
                    element = crm_df.iloc[i]['element']
                    file_name = crm_df.iloc[i]['file_name']
                    folder_name = crm_df.iloc[i]['folder_name']
                    solution_label = crm_df.iloc[i]['solution_label']
                    blank_value = crm_df.iloc[i].get('blank_value')
                    original_value = crm_df.iloc[i].get('original_value', value)
                    blank_info = ""
                    if not self.filtered_blank_df_cache.empty:
                        relevant_blanks = self.filtered_blank_df_cache[
                            (self.filtered_blank_df_cache['file_name'] == file_name) &
                            (self.filtered_blank_df_cache['folder_name'] == folder_name) &
                            (self.filtered_blank_df_cache['element'] == element)
                        ]
                        if not relevant_blanks.empty:
                            blank_info = "\nBLANK Data:\n"
                            for _, blank_row in relevant_blanks.iterrows():
                                blank_info += f" - Solution Label: {blank_row['solution_label']}, Value: {blank_row['value']:.2f}\n"
                    closest_info = (
                        f"Element: {element}\n"
                        f"File: {file_name}\n"
                        f"Date: {date}\n"
                        f"Solution Label: {solution_label}\n"
                        f"Value: {value:.2f}\n"
                        f"Original Value: {original_value:.2f}\n" if blank_value is not None else f"Value: {value:.2f}\n"
                        f"Blank Value Applied: {blank_value:.2f}\n" if blank_value is not None else ""
                        f"{blank_info}"
                    )
        if closest_info:
            QMessageBox.information(self, "Point Info", closest_info)
            logger.debug(f"Clicked point info: {closest_info}")
        else:
            logger.debug("No point found near click position")

def on_mouse_moved(self, pos):
        try:
            pos = self.plot_widget.getViewBox().mapSceneToView(pos)
            x, y = pos.x(), pos.y()
            closest_dist = float('inf')
            closest_info = None
            closest_point = None
            # Get current view range for normalization
            view_box = self.plot_widget.getViewBox()
            x_min, x_max = view_box.viewRange()[0]
            y_min, y_max = view_box.viewRange()[1]
            x_range = x_max - x_min if x_max != x_min else 1
            y_range = y_max - y_min if y_max != y_min else 1
            for plot_item, crm_df, indices, date_labels in self.plot_data_items:
                plot_data = plot_item.getData()
                if plot_data is None:
                    continue
                plot_x, plot_y = plot_data
                # Normalized dist
                dx = (plot_x - x) / x_range
                dy = (plot_y - y) / y_range
                distances = np.sqrt(dx**2 + dy**2)
                min_dist_idx = np.argmin(distances)
                min_dist = distances[min_dist_idx]
                if min_dist < closest_dist:
                    closest_dist = min_dist
                    i = min_dist_idx
                    value = crm_df.iloc[i]['value']
                    date = date_labels[i]
                    file_name = crm_df.iloc[i]['file_name']
                    folder_name = crm_df.iloc[i]['folder_name']
                    crm_id = crm_df.iloc[i]['norm_crm_id']
                    element = crm_df.iloc[i]['element']
                    solution_label = crm_df.iloc[i]['solution_label']
                    blank_value = crm_df.iloc[i].get('blank_value')
                    original_value = crm_df.iloc[i].get('original_value', value)
                    blank_info = ""
                    if not self.filtered_blank_df_cache.empty:
                        relevant_blanks = self.filtered_blank_df_cache[
                            (self.filtered_blank_df_cache['file_name'] == file_name) &
                            (self.filtered_blank_df_cache['folder_name'] == folder_name) &
                            (self.filtered_blank_df_cache['element'] == element)
                        ]
                        if not relevant_blanks.empty:
                            blank_info = "\nBLANK Data:\n"
                            for _, blank_row in relevant_blanks.iterrows():
                                blank_info += f" - {blank_row['solution_label']}: {blank_row['value']:.6f}\n"
                    closest_info = (
                        f"CRM ID: {crm_id}\n"
                        f"Element: {element}\n"
                        f"Date: {date}\n"
                        f"Value: {value:.6f}\n"
                        f"Original Value: {original_value:.6f}\n" if blank_value is not None else f"Value: {value:.6f}\n"
                        f"Blank Value Applied: {blank_value:.6f}\n" if blank_value is not None else ""
                        f"Solution Label: {solution_label}\n"
                        f"File: {file_name}\n"
                        f"{blank_info}"
                    )
                    closest_point = (plot_x[min_dist_idx], plot_y[min_dist_idx])
            if closest_info and closest_dist < 0.05: # Adjusted normalized threshold
                self.tooltip_label.setText(closest_info)
                self.tooltip_label.adjustSize()
                tooltip_pos = self.plot_widget.getViewBox().mapFromView(pos)
                self.tooltip_label.move(int(tooltip_pos.x() + 15), int(tooltip_pos.y() - self.tooltip_label.height() / 2))
                self.tooltip_label.setVisible(True)
            else:
                self.tooltip_label.setVisible(False)
        except Exception as e:
            logger.error(f"Error in on_mouse_moved: {str(e)}")
            self.tooltip_label.setVisible(False)

def save_plot(self):
    try:
        import pyqtgraph.exporters
        temp_file = 'temp_crm_plot.png'
        exporter = pyqtgraph.exporters.ImageExporter(self.plot_widget.getPlotItem())
        exporter.parameters()['width'] = 1200
        exporter.export(temp_file)
        im = Image.open(temp_file)
        if self.logo_path.exists():
            logo = Image.open(self.logo_path)
            logo = logo.resize((100, 100))
            box = (im.width - 110, 10)
            if logo.mode == 'RGBA':
                im.paste(logo, box, logo)
            else:
                im.paste(logo, box)
            im.save('crm_plot.png')
            os.remove(temp_file)
            self.status_label.setText("Plot saved as crm_plot.png")
            logger.info("Plot saved as crm_plot.png")
    except Exception as e:
        logger.error(f"Error saving plot: {str(e)}")
        self.status_label.setText("Failed to save plot")
        QMessageBox.critical(self, "Error", f"Failed to save plot: {str(e)}")
