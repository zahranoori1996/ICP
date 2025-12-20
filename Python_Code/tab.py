
from PyQt6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLabel, QMessageBox
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QPixmap
import logging
from utils.var_main import LOGO_PNG_PATH
# Setup logging
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

# Color definitions for tabs
TAB_COLORS = {
    "File": {"bg": "#fdf6e3", "indicator": "#b58900"},  # Warm beige bg, amber indicator
    "Find similarity": {"bg": "#e6e6fa", "indicator": "#483d8b"},  # Light lavender bg, darker slate blue indicator
    "Elements": {"bg": "#e8f6f3", "indicator": "#2e8b57"},  # Light teal bg, sea green indicator
    "Raw Data": {"bg": "#f0e6ff", "indicator": "#7b68ee"},  # Soft purple bg, medium slate blue indicator
    "CRM": {"bg": "#e0f7fa", "indicator": "#008b8b"},  # Light cyan bg, dark cyan indicator
    "Process": {"bg": "#f5e6e8", "indicator": "#c71585"}   # Light pink bg, magenta indicator
}

class RibbonTabButton(QPushButton):
    def __init__(self, text, parent=None, bg_color="#fdf6e3", text_color="black"):
        super().__init__(text, parent)
        self.bg_color = bg_color
        self.text_color = text_color
        self.setFixedSize(120, 30)
        self.setStyleSheet(f"""
            QPushButton {{
                background-color: transparent;
                color: {self.text_color};
                font: bold 14px 'Segoe UI';
                border: none;
                padding: 5px;
                text-align: center;
            }}
            QPushButton:hover {{
                background-color: #dddddd;
            }}
        """)
        
    def select(self):
        self.setStyleSheet(f"""
            QPushButton {{
                background-color: {self.bg_color};
                color: {self.text_color};
                font: bold 14px 'Segoe UI';
                border: none;
                padding: 5px;
                text-align: center;
            }}
        """)
        
    def deselect(self):
        self.setStyleSheet(f"""
            QPushButton {{
                background-color: transparent;
                color: {self.text_color};
                font: bold 14px 'Segoe UI';
                border: none;
                padding: 5px;
                text-align: center;
            }}
            QPushButton:hover {{
                background-color: #dddddd;
            }}
        """)

class SubTabButton(QPushButton):
    def __init__(self, text, parent=None, text_color="black"):
        super().__init__(text, parent)
        self.text_color = text_color
        self.setFixedSize(110, 40)
        self.setStyleSheet(f"""
            QPushButton {{
                background-color: transparent;
                color: {self.text_color};
                font: 14px 'Segoe UI';
                border: none;
                padding: 5px;
                text-align: center;
            }}
            QPushButton:hover {{
                background-color: #eeeeee;
            }}
        """)
        
    def select(self):
        self.setStyleSheet(f"""
            QPushButton {{
                background-color: #dddddd;
                color: black;
                font: 14px 'Segoe UI';
                border: none;
                padding: 5px;
                text-align: center;
            }}
        """)
        
    def deselect(self):
        self.setStyleSheet(f"""
            QPushButton {{
                background-color: transparent;
                color: {self.text_color};
                font: 14px 'Segoe UI';
                border: none;
                padding: 5px;
                text-align: center;
            }}
            QPushButton:hover {{
                background-color: #eeeeee;
            }}
        """)

class MainTabContent(QWidget):
    def __init__(self, tab_info, parent=None):
        super().__init__(parent)
        self.current_tab = None
        self.tab_subtab_map = {}
        logger.debug("Initializing MainTabContent")

        # Main layout
        main_layout = QVBoxLayout()
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)
        
        # Ribbon bar
        self.ribbon = QWidget()
        self.ribbon.setFixedHeight(30)
        self.ribbon.setStyleSheet("background-color: white;")
        ribbon_layout = QHBoxLayout()
        ribbon_layout.setContentsMargins(0, 0, 0, 0)
        ribbon_layout.setSpacing(0)
        ribbon_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.ribbon.setLayout(ribbon_layout)
        
        # Content frame
        self.content_frame = QWidget()
        content_layout = QVBoxLayout()
        content_layout.setContentsMargins(0, 0, 0, 0)
        content_layout.setSpacing(0)
        self.content_frame.setLayout(content_layout)
        
        main_layout.addWidget(self.ribbon)
        main_layout.addWidget(self.content_frame, 1)
        
        # Tab definitions
        self.tabs = {}
        self.tab_buttons = {}
        
        # Create tabs and buttons
        for t_name, subtabs in tab_info.items():
            colors = TAB_COLORS.get(t_name, {"bg": "white", "indicator": "black"})
            btn = RibbonTabButton(t_name, bg_color=colors["bg"], text_color="black")
            btn.clicked.connect(lambda checked, n=t_name: self.switch_tab(n))
            ribbon_layout.addWidget(btn)
            self.tab_buttons[t_name] = btn
            
            # Create tab content
            tab_content = QWidget()
            tab_content_layout = QVBoxLayout()
            tab_content_layout.setContentsMargins(0, 0, 0, 0)
            tab_content_layout.setSpacing(0)
            tab_content.setLayout(tab_content_layout)
            self.tabs[t_name] = tab_content
            content_layout.addWidget(tab_content)
            tab_content.hide()
            
            # Check if the tab has only one subtab
            if len(subtabs) == 1:
                subtab_name, subtab_content = list(subtabs.items())[0]
                logger.debug(f"Tab {t_name} has single subtab: {subtab_name}")
                
                # If single subtab is a widget, add it directly
                if isinstance(subtab_content, QWidget):
                    tab_content_layout.addWidget(subtab_content)
                    subtab_widgets = {subtab_name: subtab_content}
                    self.tab_subtab_map[t_name] = {
                        "buttons": {},  # No buttons for single subtab
                        "widgets": subtab_widgets,
                        "content": tab_content,
                        "current_subtab": subtab_name,
                        "has_subtab_bar": False
                    }
                # If single subtab is a function, create a button to trigger it
                elif callable(subtab_content):
                    subtab_bar = QWidget()
                    subtab_bar.setFixedHeight(50)
                    subtab_bar.setStyleSheet(f"background-color: {colors['bg']};")
                    subtab_layout = QHBoxLayout()
                    subtab_layout.setContentsMargins(8, 6, 8, 6)
                    subtab_layout.setSpacing(8)
                    subtab_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
                    subtab_bar.setLayout(subtab_layout)
                    
                    btn = SubTabButton(subtab_name, text_color="black")
                    btn.clicked.connect(subtab_content)  # Connect function directly
                    subtab_layout.addWidget(btn)
                    
                    # Add logo to subtab bar
                    subtab_layout.addStretch()
                    logo_label = QLabel()
                    logo_label.setPixmap(QPixmap(LOGO_PNG_PATH).scaled(40, 40, Qt.AspectRatioMode.KeepAspectRatio))
                    logo_label.setAlignment(Qt.AlignmentFlag.AlignRight)
                    subtab_layout.addWidget(logo_label)
                    
                    indicator = QWidget()
                    indicator.setFixedHeight(3)
                    indicator.setStyleSheet(f"background-color: {colors['indicator']};")
                    
                    subtab_content_area = QWidget()
                    subtab_content_layout = QVBoxLayout()
                    subtab_content_layout.setContentsMargins(0, 0, 0, 0)
                    subtab_content_area.setLayout(subtab_content_layout)
                    
                    tab_content_layout.addWidget(subtab_bar)
                    tab_content_layout.addWidget(indicator)
                    tab_content_layout.addWidget(subtab_content_area, 1)
                    
                    subtab_widgets = {subtab_name: subtab_content}
                    self.tab_subtab_map[t_name] = {
                        "buttons": {subtab_name: btn},
                        "widgets": subtab_widgets,
                        "content": subtab_content_area,
                        "current_subtab": subtab_name,
                        "has_subtab_bar": True
                    }
                    btn.select()  # Select the button by default
                else:  # Handle string content
                    frame = QWidget()
                    frame_layout = QVBoxLayout()
                    frame_layout.setContentsMargins(25, 25, 25, 25)
                    frame_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
                    label = QLabel(subtab_content)
                    label.setStyleSheet("font: 18px 'Segoe UI'; color: black;")
                    frame_layout.addWidget(label)
                    frame.setLayout(frame_layout)
                    tab_content_layout.addWidget(frame)
                    subtab_widgets = {subtab_name: frame}
                    self.tab_subtab_map[t_name] = {
                        "buttons": {},
                        "widgets": subtab_widgets,
                        "content": tab_content,
                        "current_subtab": subtab_name,
                        "has_subtab_bar": False
                    }
            else:
                # Subtab bar for multiple subtabs
                subtab_bar = QWidget()
                subtab_bar.setFixedHeight(50)
                subtab_bar.setStyleSheet(f"background-color: {colors['bg']};")
                subtab_layout = QHBoxLayout()
                subtab_layout.setContentsMargins(8, 6, 8, 6)
                subtab_layout.setSpacing(8)
                subtab_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
                subtab_bar.setLayout(subtab_layout)
                
                # Subtab buttons
                subtab_buttons = {}
                for name, content in subtabs.items():
                    btn = SubTabButton(name, text_color="black")
                    btn.clicked.connect(lambda checked, n=name, tn=t_name: self.switch_subtab(n, tn))
                    subtab_layout.addWidget(btn)
                    subtab_buttons[name] = btn  # Store the button directly
                
                # Add logo to subtab bar
                subtab_layout.addStretch()
                logo_label = QLabel()
                logo_label.setPixmap(QPixmap(LOGO_PNG_PATH).scaled(100, 40, Qt.AspectRatioMode.KeepAspectRatio))
                logo_label.setAlignment(Qt.AlignmentFlag.AlignRight)
                subtab_layout.addWidget(logo_label)
                
                # Indicator
                indicator = QWidget()
                indicator.setFixedHeight(3)
                indicator.setStyleSheet(f"background-color: {colors['indicator']};")
                
                # Subtab content area
                subtab_content = QWidget()
                subtab_content_layout = QVBoxLayout()
                subtab_content_layout.setContentsMargins(0, 0, 0, 0)
                subtab_content_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
                subtab_content.setLayout(subtab_content_layout)
                
                tab_content_layout.addWidget(subtab_bar)
                tab_content_layout.addWidget(indicator)
                tab_content_layout.addWidget(subtab_content, 1)
                
                # Subtab content
                subtab_widgets = {}
                for name, content in subtabs.items():
                    if callable(content):
                        subtab_widgets[name] = content
                    elif isinstance(content, str):
                        frame = QWidget()
                        frame_layout = QVBoxLayout()
                        frame_layout.setContentsMargins(25, 25, 25, 25)
                        frame_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
                        label = QLabel(content)
                        label.setStyleSheet("font: 18px 'Segoe UI'; color: black;")
                        frame_layout.addWidget(label)
                        frame.setLayout(frame_layout)
                        subtab_widgets[name] = frame
                        subtab_content_layout.addWidget(frame)
                        frame.hide()
                    else:
                        subtab_widgets[name] = content
                        subtab_content_layout.addWidget(content)
                        content.hide()
                
                self.tab_subtab_map[t_name] = {
                    "buttons": subtab_buttons,
                    "widgets": subtab_widgets,
                    "content": subtab_content,
                    "current_subtab": None,
                    "has_subtab_bar": True
                }
                
                # Only switch to first subtab if it's not a callable
                if subtabs:
                    first_subtab = list(subtabs.keys())[0]
                    if not callable(subtabs[first_subtab]):
                        self.switch_subtab(first_subtab, t_name)
        
        self.setLayout(main_layout)
        
        if tab_info:
            first_tab = list(tab_info.keys())[0]
            if not all(callable(content) for content in tab_info[first_tab].values()):
                self.switch_tab(first_tab)
    
    def switch_subtab(self, name, tab_name):
        if tab_name not in self.tab_subtab_map:
            logger.warning(f"Tab {tab_name} not found in tab_subtab_map")
            return
        
        subtab_buttons = self.tab_subtab_map[tab_name]["buttons"]
        subtab_widgets = self.tab_subtab_map[tab_name]["widgets"]
        current_subtab = self.tab_subtab_map[tab_name]["current_subtab"]
        
        # Deselect current subtab button
        if current_subtab and current_subtab in subtab_buttons:
            subtab_buttons[current_subtab].deselect()
        
        # Handle new subtab
        content = subtab_widgets.get(name)
        if callable(content):
            try:
                logger.debug(f"Executing function for subtab {name} in tab {tab_name}")
                content()  # Call the function
            except Exception as e:
                logger.error(f"Failed to execute {name}: {str(e)}")
                QMessageBox.warning(self, "Error", f"Failed to execute {name}: {str(e)}")
        elif isinstance(content, QWidget):
            # Hide all other widgets in the same tab
            for other_name, other_content in subtab_widgets.items():
                if isinstance(other_content, QWidget) and other_name != name:
                    other_content.hide()
            content.show()
            logger.debug(f"Switched to subtab {name} in tab {tab_name}")
        
        if name in subtab_buttons and isinstance(subtab_buttons[name], SubTabButton):
            subtab_buttons[name].select()
        else:
            logger.warning(f"No valid SubTabButton for subtab {name} in tab {tab_name}")
        self.tab_subtab_map[tab_name]["current_subtab"] = name
    
    def switch_tab(self, tab_name):
        if self.current_tab and self.current_tab in self.tabs:
            self.tabs[self.current_tab].hide()
            self.tab_buttons[self.current_tab].deselect()
            
        self.tabs[tab_name].show()  # این مهم است
        self.tab_buttons[tab_name].select()
        self.current_tab = tab_name

        # رنگ‌ها
        colors = TAB_COLORS.get(tab_name, {"bg": "white", "indicator": "black"})
        if self.tab_subtab_map[tab_name].get("has_subtab_bar", False):
            layout = self.tabs[tab_name].layout()
            if layout.count() > 0:
                layout.itemAt(0).widget().setStyleSheet(f"background-color: {colors['bg']};")
            if layout.count() > 1:
                layout.itemAt(1).widget().setStyleSheet(f"background-color: {colors['indicator']};")
        
        logger.debug(f"Switched to tab {tab_name}")