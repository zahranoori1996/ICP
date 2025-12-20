from PyQt6.QtWidgets import QTableView, QHeaderView, QAbstractItemView
from PyQt6.QtCore import Qt, QRect, QAbstractTableModel
from PyQt6 import QtCore
import logging

logger = logging.getLogger(__name__)

class FreezeTableWidget(QTableView):
    """A QTableView with configurable frozen columns and header click callback."""
    
    def __init__(self, model, frozen_columns=1, parent=None):
        super().__init__(parent)
        self.frozenTableView = QTableView(self)
        self.frozen_columns = max(1, frozen_columns)
        self._header_click_callback = None  # NEW: برای کلیک روی ستون فریز شده
        self._is_dialog_open = False  # جلوگیری از باز شدن چند دیالوگ

        self.setModel(model)
        self.frozenTableView.setModel(model)
        self.init_ui()

        # اتصالات
        self.horizontalHeader().sectionResized.connect(self.updateSectionWidth)
        self.verticalHeader().sectionResized.connect(self.updateSectionHeight)
        self.frozenTableView.verticalScrollBar().valueChanged.connect(self.frozenVerticalScroll)
        self.verticalScrollBar().valueChanged.connect(self.mainVerticalScroll)
        self.model().modelReset.connect(self.resetFrozenTable)

        # کلیک روی هدر فریز شده
        self.frozenTableView.horizontalHeader().sectionClicked.connect(self._on_frozen_header_clicked)

    def init_ui(self):
        self.frozenTableView.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.frozenTableView.verticalHeader().hide()
        self.frozenTableView.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Fixed)
        self.viewport().stackUnder(self.frozenTableView)
        self.frozenTableView.setStyleSheet("QTableView { border: none; selection-background-color: #999; }")
        self.frozenTableView.setSelectionModel(self.selectionModel())

        self.setHorizontalScrollMode(QAbstractItemView.ScrollMode.ScrollPerItem)
        self.setVerticalScrollMode(QAbstractItemView.ScrollMode.ScrollPerItem)
        self.frozenTableView.setHorizontalScrollMode(QAbstractItemView.ScrollMode.ScrollPerItem)
        self.frozenTableView.setVerticalScrollMode(QAbstractItemView.ScrollMode.ScrollPerItem)

        self.frozenTableView.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.frozenTableView.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)

        self.update_frozen_columns()
        self.updateFrozenTableGeometry()
        self.frozenTableView.show()

    # NEW: متد برای تنظیم callback
    def set_header_click_callback(self, callback):
        """Set callback for frozen header clicks: callback(section, col_name)"""
        self._header_click_callback = callback
        logger.debug(f"Header click callback set: {callback}")

    def _on_frozen_header_clicked(self, section):
        """Handle click on frozen header"""
        if section >= self.frozen_columns or self._is_dialog_open:
            return

        if not self._header_click_callback:
            logger.warning("No header click callback set")
            return

        self._is_dialog_open = True
        try:
            col_name = self.model().headerData(section, Qt.Orientation.Horizontal, Qt.ItemDataRole.DisplayRole)
            col_name = str(col_name) if col_name is not None else f"Column {section}"
            logger.debug(f"Frozen header clicked: section={section}, col_name={col_name}")
            self._header_click_callback(section, col_name)
        finally:
            self._is_dialog_open = False

    def update_frozen_columns(self):
        if not self.model():
            self.frozenTableView.hide()
            return
        total_cols = self.model().columnCount()
        for col in range(total_cols):
            hidden = col >= self.frozen_columns
            self.frozenTableView.setColumnHidden(col, hidden)
            if col < self.frozen_columns:
                width = self.columnWidth(col)
                self.frozenTableView.setColumnWidth(col, width)
        self.frozenTableView.show()
        self.updateFrozenTableGeometry()

    def resetFrozenTable(self):
        self.update_frozen_columns()
        self.updateFrozenTableGeometry()

    def updateSectionWidth(self, logicalIndex, oldSize, newSize):
        if logicalIndex < self.frozen_columns:
            self.frozenTableView.setColumnWidth(logicalIndex, newSize)
            self.updateFrozenTableGeometry()

    def updateSectionHeight(self, logicalIndex, oldSize, newSize):
        self.frozenTableView.setRowHeight(logicalIndex, newSize)

    def frozenVerticalScroll(self, value):
        self.verticalScrollBar().setValue(value)

    def mainVerticalScroll(self, value):
        self.frozenTableView.verticalScrollBar().setValue(value)

    def updateFrozenTableGeometry(self):
        if not self.model() or self.model().columnCount() == 0:
            return
        total_width = sum(self.columnWidth(col) for col in range(self.frozen_columns))
        self.frozenTableView.setGeometry(
            self.verticalHeader().width() + self.frameWidth(),
            self.frameWidth(),
            total_width,
            self.viewport().height() + self.horizontalHeader().height()
        )

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self.updateFrozenTableGeometry()

    def moveCursor(self, cursorAction, modifiers):
        current = super().moveCursor(cursorAction, modifiers)
        if cursorAction == QAbstractItemView.CursorAction.MoveLeft and current.column() >= self.frozen_columns:
            frozen_width = sum(self.columnWidth(col) for col in range(self.frozen_columns))
            visual_x = self.visualRect(current).topLeft().x()
            if visual_x < frozen_width:
                self.horizontalScrollBar().setValue(
                    self.horizontalScrollBar().value() + visual_x - frozen_width
                )
        return current

    def scrollTo(self, index, hint=QAbstractItemView.ScrollHint.EnsureVisible):
        if index.column() >= self.frozen_columns:
            super().scrollTo(index, hint)