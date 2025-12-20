from PyQt6.QtCore import Qt, QAbstractTableModel, QModelIndex
from PyQt6.QtGui import QColor
from .oxide_factors import oxide_factors
import pandas as pd
import logging

class PivotTableModel(QAbstractTableModel):
    """Custom table model for pivot table, optimized for large datasets with editable cells."""
    def __init__(self, pivot_tab, df=None, crm_rows=None):
        super().__init__()
        self.logger = logging.getLogger(__name__)
        self.pivot_tab = pivot_tab
        self._df = df if df is not None else pd.DataFrame()
        self._crm_rows = crm_rows if crm_rows is not None else []
        self._row_info = []
        self._column_widths = {}
        self._build_row_info()

    def set_data(self, df, crm_rows=None):
        self.logger.debug("Setting new data in PivotTableModel")
        self.beginResetModel()
        self._df = df.copy()
        self._crm_rows = crm_rows if crm_rows is not None else []
        self._build_row_info()
        self.endResetModel()

    def _build_row_info(self):
        self._row_info = []
        for row_idx in range(len(self._df)):
            self._row_info.append({'type': 'pivot', 'index': row_idx})
            sol_label = self._df.iloc[row_idx]['Solution Label']
            for grp_idx, (sl, cdata) in enumerate(self._crm_rows):
                if sl == sol_label:
                    for sub in range(len(cdata)):
                        self._row_info.append({'type': 'crm', 'group': grp_idx, 'sub': sub})
                    break

    def rowCount(self, parent=QModelIndex()):
        return len(self._row_info)

    def columnCount(self, parent=QModelIndex()):
        return self._df.shape[1]

    def data(self, index, role=Qt.ItemDataRole.DisplayRole):
        if not index.isValid() or index.row() >= len(self._row_info):
            return None

        row = index.row()
        col = index.column()
        col_name = self._df.columns[col]
        info = self._row_info[row]

        # تعیین ردیف اصلی در df
        if info['type'] == 'pivot':
            actual_df_row = info['index']
            value = self._df.iloc[actual_df_row, col]
            is_crm_row = False
            is_diff_row = False
            tags = None
        else:
            group_idx = info['group']
            sub_idx = info['sub']
            crm_row_data, tags = self._crm_rows[group_idx][1][sub_idx]
            value = crm_row_data[col] if col < len(crm_row_data) else ""
            is_crm_row = sub_idx % 2 == 0
            is_diff_row = sub_idx % 2 == 1
            sol_label = self._crm_rows[group_idx][0]
            actual_df_row = self._df.index[self._df['Solution Label'] == sol_label][0]

        # ————————————————————
        # فقط ستون اول (Row Header) رنگی بشه
        # ————————————————————
        if role == Qt.ItemDataRole.BackgroundRole:
            # ستون اول = شماره سطر یا Solution Label
            if col == 0:  # فقط ستون اول رنگی بشه
                if is_crm_row:
                    return QColor("#FFF5E4")  
                if is_diff_row:
                    return QColor("#E6E6FA")      # diff row

                # رنگ فایل فقط در ستون اول
                if hasattr(self.pivot_tab, 'row_header_colors'):
                    color = self.pivot_tab.row_header_colors[actual_df_row]
                    return QColor(color)

            # بقیه ستون‌ها: فقط duplicate/diff رنگی بمونن، بقیه سفید یا zebra
            if is_crm_row:
                return QColor("#FFF5E4")
            if is_diff_row:
                if isinstance(tags, dict) and col_name in tags and col_name != "Solution Label":
                    return QColor("#ECFFC4") if tags[col_name] == "in_range" else QColor("#FFCCCC")
                return QColor("#E6E6FA")

            # ردیف‌های معمولی: فقط zebra (متناوب)
            return QColor("#f9f9f9") if actual_df_row % 2 == 0 else QColor("white")

        # ————————————————————
        # نمایش متن
        # ————————————————————
        if role in (Qt.ItemDataRole.DisplayRole, Qt.ItemDataRole.EditRole):
            if pd.isna(value) or value == "":
                return ""

            if is_diff_row and col_name != "Solution Label":
                try:
                    return f"{float(value):.1f}%"
                except:
                    return str(value)

            if info['type'] == 'pivot' and col_name != "Solution Label":
                try:
                    dec = int(self.pivot_tab.decimal_places.currentText())
                    return f"{float(value):.{dec}f}"
                except:
                    return str(value)

            return str(value)

        # ————————————————————
        # تراز متن
        # ————————————————————
        if role == Qt.ItemDataRole.TextAlignmentRole:
            return Qt.AlignmentFlag.AlignCenter  # همه چیز وسط چین (حتی Solution Label)

        return None

    def flags(self, index):
        """Make all cells editable except diff rows."""
        if not index.isValid():
            return Qt.ItemFlag.NoItemFlags
        info = self._row_info[index.row()]
        if info['type'] == 'crm' and info['sub'] % 2 == 1:  # diff row
            return Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable
        return Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable | Qt.ItemFlag.ItemIsEditable

    def setData(self, index, value, role=Qt.ItemDataRole.EditRole):
        """Update the underlying DataFrame with edited values."""
        if not index.isValid() or role != Qt.ItemDataRole.EditRole:
            self.logger.debug(f"Invalid index or role: {index}, {role}")
            return False

        row = index.row()
        col = index.column()
        col_name = self._df.columns[col]
        info = self._row_info[row]
        self.logger.debug(f"setData called for row {row}, col {col} ({col_name}), value: '{value}'")

        if info['type'] != 'pivot':
            self.logger.warning("Editing CRM or diff rows is not allowed")
            return False

        try:
            # Get the solution label from the view
            solution_label = self._df.iloc[info['index']]['Solution Label']
            # Find the row in the full pivot_data
            full_df = self.pivot_tab.pivot_data
            full_row_idx = full_df[full_df['Solution Label'] == solution_label].index
            if full_row_idx.empty:
                self.logger.warning(f"Solution Label '{solution_label}' not found in pivot_data")
                return False
            full_row_idx = full_row_idx[0]

            if value.strip() == "":
                full_df.at[full_row_idx, col_name] = pd.NA
                self.logger.debug(f"Set value at {full_row_idx}, {col_name} to NA")
            else:
                if col_name == 'Solution Label':
                    full_df.at[full_row_idx, col_name] = str(value).strip()
                    self.logger.debug(f"Updated Solution Label at {full_row_idx} to '{value}'")
                else:
                    try:
                        full_df.at[full_row_idx, col_name] = float(value)
                        self.logger.debug(f"Updated numeric value at {full_row_idx}, {col_name} to {value}")
                    except ValueError:
                        self.logger.warning(f"Invalid numeric value '{value}' for column {col_name}")
                        return False

            # Emit dataChanged and refresh UI
            self.dataChanged.emit(index, index, [Qt.ItemDataRole.DisplayRole, Qt.ItemDataRole.BackgroundRole])
            self.logger.debug("Emitted dataChanged signal")
            self.pivot_tab.update_pivot_display()
            self.logger.debug("Called update_pivot_display")
            return True
        except Exception as e:
            self.logger.error(f"Failed to set data at row {row}, col {col}: {str(e)}")
            return False

    def headerData(self, section, orientation, role=Qt.ItemDataRole.DisplayRole):
        if role == Qt.ItemDataRole.DisplayRole:
            if orientation == Qt.Orientation.Horizontal:
                return str(self._df.columns[section])
            return str(section + 1)
        return None

    def set_column_width(self, col, width):
        self._column_widths[col] = width