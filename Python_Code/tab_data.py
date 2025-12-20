from screens.notification_tab import NotificationTab
def create_tab(self):
    tab_info = {
        "File": {
            "Save Project": self.save_project,
            "Load Project": self.load_project,
            "New": self.new_window,
            "Close": self.close_window,
            "Logout": self.logout,
            "Software Update": self.update_tab
        },
        "Find similarity": {"display": self.compare_tab},
        "Process": {
            "Weight Check": self.weight_check,
            "Volume Check": self.volume_check,
            "DF check": self.df_check,
            "Empty check": self.empty_check,
            "CRM Calibraton": self.crm_check,
            "Drift Calibraton": self.rm_check,
            "Calibraton Pro": self.master_verification,
            "Result": self.results,
            "Report": self.report
        },
        "Elements": {"Display": self.elements_tab},
        "Raw Data": {"Display": self.pivot_tab},
        "CRM": {"CRM": self.crm_tab}
    }

    # File access based on role
    if self.user_role in ['report_manager', 'lab_manager', 'admin', 'qc']:
        tab_info["File"]["Open"] = self.file_tab.open_existing_file
    else:
        tab_info["File"]["Upload File"] = self.file_tab.show_upload_dialog

    # Management tab
    if self.user_role in ['lab_manager', 'admin']:
        from screens.management.management_tab import ManagementTab
        self.management_tab = ManagementTab(self, self.results)
        tab_info["Management"] = {"display": self.management_tab}

    # QC Tab — only for authorized users
    if self.user_role in ['qc', 'admin']:
        tab_info["QC"] = {
            "Display": self.qc_tab,
            "Min Max": self.min_max_tab,
            "Statistics": self.static_tab_qc
        }

    # Notification tab
    if self.user_role in ['device_operator', 'report_manager']:
        self.notification_tab = NotificationTab(self)
        tab_info["Notifications"] = {"display": self.notification_tab}

                # System tray and notifications
        self.notification_tab.setup_system_tray()
        self.notification_tab.start_notification_checker()

    return tab_info


def apply_role_restrictions(self):
    allowed_tabs = {
        "device_operator": ["File", "Raw Data", "Elements", "CRM", "Notifications"],
        "viewer": ["Raw Data"],
        "report_manager": ["File", "Raw Data", "Elements", "CRM", "Process", "Find similarity", "Notifications"],
        "lab_manager": None,
        "admin": None,
        "guest": ["Raw Data"],
        "qc": ["File", "Elements", "CRM", "QC"]
    }

    allowed = allowed_tabs.get(self.user_role, None)
    if allowed is None:
        return

    buttons = self.main_content.tab_buttons
    contents = self.main_content.tabs

    for tab_name, btn in buttons.items():
        if tab_name not in allowed:
            btn.hide()
            if tab_name in contents:
                contents[tab_name].hide()

    if self.main_content.current_tab not in allowed:
        first_allowed = next((t for t in allowed if t in buttons), None)
        if first_allowed:
            self.main_content.switch_tab(first_allowed)