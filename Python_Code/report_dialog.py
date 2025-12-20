from PyQt6.QtWidgets import QDialog, QVBoxLayout, QHBoxLayout, QPushButton, QTextEdit, QCheckBox, QScrollArea, QWidget, QLabel, QMessageBox
from PyQt6.QtGui import QGuiApplication
from PyQt6.QtCore import Qt
import re
import numpy as np
from scipy.optimize import differential_evolution
from scipy.special import huber
from collections import defaultdict
import logging

class ReportDialog(QDialog):
    """Dialog to display table-based CRM analysis report with scrollable column visibility toggles and textual decision analysis."""
    def __init__(self, parent, annotations):
        super().__init__(parent)
        self.logger = logging.getLogger(__name__)
        self.annotations = annotations
        self.setWindowTitle("Professional CRM Analysis Report")

        # Set window to full width
        screen = QGuiApplication.primaryScreen().size()
        self.setGeometry(0, 100, 1000, 700)
        
        # Initialize column visibility dictionary
        self.column_visibility = {
            'Verification ID': True,
            'Certificate Value': True,
            'Sample Value': True,
            'Acceptable Range': True,
            'Blank Value': False,
            'Blank Label': False,
            'Blank Correction Status': False,
            'Sample Value - Blank': False,
            'Soln Conc': True,
            'Int': False,
            'Calibration Range': True,
            'ICP Recovery (%)': False,
            'ICP Status': False,
            'ICP Detection Limit': False,
            'ICP RSD%': False,
            'Required Scaling (%)': False
        }
        
        # Store decision analysis results
        self.decision_data = {
            'recommended_blank': 0.0,
            'recommended_scale': 1.0,
            'recommended_in_range': 0,
            'recommended_avg_distance': "N/A",
            'final_decision': "Calculating..."
        }
        
        self.setup_ui()
        self.logger.debug(f"Initialized ReportDialog with {len(annotations)} annotations")

    def setup_ui(self):
        layout = QVBoxLayout(self)
        
        # Add scrollable checkbox area
        scroll_area = QScrollArea()
        scroll_area.setWidgetResizable(True)
        scroll_area.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        scroll_area.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        scroll_area.setFixedHeight(50)
        
        checkbox_widget = QWidget()
        checkbox_layout = QHBoxLayout(checkbox_widget)
        checkbox_layout.addWidget(QLabel("Show Columns:"))
        self.checkboxes = {}
        for column in self.column_visibility.keys():
            checkbox = QCheckBox(column)
            checkbox.setChecked(self.column_visibility[column])
            checkbox.toggled.connect(self.update_report)
            self.checkboxes[column] = checkbox
            checkbox_layout.addWidget(checkbox)
        checkbox_layout.addStretch()
        
        scroll_area.setWidget(checkbox_widget)
        layout.addWidget(scroll_area)
        
        self.text_edit = QTextEdit()
        self.text_edit.setReadOnly(True)
        self.text_edit.setStyleSheet("""
            QTextEdit {
                background-color: #f8f9fa;
                border: 1px solid #dee2e6;
                font-family: 'Segoe UI', sans-serif;
                font-size: 14px;
                padding: 10px;
            }
        """)
        layout.addWidget(self.text_edit)
        
        close_btn = QPushButton("Close")
        close_btn.clicked.connect(self.accept)
        close_btn.setStyleSheet("""
            QPushButton {
                background-color: #007bff;
                color: white;
                border: none;
                padding: 8px 16px;
                border-radius: 4px;
            }
            QPushButton:hover {
                background-color: #0056b3;
            }
        """)
        layout.addWidget(close_btn)
        
        self.generate_html_report()

    def update_report(self):
        """Update column visibility and regenerate the report."""
        try:
            for column, checkbox in self.checkboxes.items():
                self.column_visibility[column] = checkbox.isChecked()
            self.generate_html_report()
            self.logger.debug("Updated report with new column visibility")
        except Exception as e:
            self.logger.error(f"Error updating report: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to update report: {str(e)}")


    def is_numeric_range(self, range_str):
        """Check if a range string is in the format [number to number], allowing negative numbers and decimals."""
        if not range_str or not isinstance(range_str, str):
            return False
        return bool(re.match(r'\[(-?[\d.]+) to (-?[\d.]+)\]', range_str))

    def generate_html_report(self):
        """Generate HTML report with only visible columns and add textual decision analysis."""
        try:
            crm_data = []
            html = """
            <html>
            <head>
                <style>
                    body { font-family: 'Segoe UI', sans-serif; color: #333; line-height: 1.6; }
                    h1 { color: #007bff; text-align: center; margin-bottom: 20px; }
                    h2 { color: #0056b3; margin-top: 30px; }
                    .problematic { color: #dc3545; font-weight: bold; }
                    .in-range { background-color: #d4edda; }
                    .out-range { background-color: #f8d7da; }
                    table { width: 100%; border-collapse: collapse; margin-bottom: 20px; }
                    th, td { border: 1px solid #dee2e6; padding: 8px; text-align: left; color: #333; }
                    th { background-color: #007bff; color: white; }
                    .decision-section { margin-top: 40px; padding: 15px; background-color: #e9ecef; border-radius: 8px; }
                    .decision-section ul { list-style-type: disc; margin-left: 20px; }
                </style>
            </head>
            <body>
                <h1>CRM Analysis Report</h1>
                <h2>Table Report</h2>
                <table>
                    <tr>
            """
            
            # Add table headers for visible columns only
            for column in self.column_visibility:
                if self.column_visibility[column]:
                    html += f"<th>{column}</th>"
            html += "</tr>"

            for annotation in self.annotations:
                lines = annotation.split('\n')
                if not lines:
                    self.logger.warning("Empty annotation encountered")
                    continue
                
                verification_id_match = re.match(r'Verification ID: (\w+) \(Label: .+\)', lines[0].strip())
                verification_id = verification_id_match.group(1) if verification_id_match else "Unknown"
                
                # Initialize variables for all columns
                certificate_val = sample_val = acceptable_range = blank_val = blank_label = blank_correction_status = corrected_sample_val = soln_conc = int_val = calibration_range = icp_recovery = icp_status = detection_limit = rsd_percent = scaling = ""
                sample_val_class = corrected_sample_val_class = soln_conc_class = icp_status_class = ""
                
                # Parse annotation lines
                for line in lines[1:]:
                    line = line.strip()
                    if not line:
                        continue
                    if "Certificate Value:" in line:
                        certificate_val = line.split(": ")[1].strip()
                    elif "Sample Value:" in line:
                        sample_val = line.split(": ")[1].strip()
                    elif "Acceptable Range:" in line:
                        acceptable_range = line.split(": ")[1].strip()
                    elif "Status: In range" in line:
                        sample_val_class = 'in-range'
                    elif "Status: Out of range" in line:
                        sample_val_class = 'out-range'
                    elif "Blank Value:" in line:
                        blank_val = line.split(": ")[1].strip()
                    elif "Blank Label:" in line:
                        blank_label = line.split(": ")[1].strip()
                    elif "Blank Correction Status:" in line:
                        blank_correction_status = line.split(": ")[1].strip()
                    elif "Sample Value - Blank:" in line:
                        corrected_sample_val = line.split(": ")[1].strip()
                        if self.is_numeric(corrected_sample_val) and self.is_numeric_range(acceptable_range):
                            try:
                                corrected_val = float(corrected_sample_val)
                                range_match = re.match(r'\[(-?[\d.]+) to (-?[\d.]+)\]', acceptable_range)
                                if range_match:
                                    lower, upper = float(range_match.group(1)), float(range_match.group(2))
                                    corrected_sample_val_class = 'in-range' if lower <= corrected_val <= upper else 'out-range'
                            except (ValueError, TypeError) as e:
                                self.logger.warning(f"Failed to verify corrected sample value: {corrected_sample_val}, error: {str(e)}")
                                corrected_sample_val_class = 'out-range'
                    elif "Soln Conc:" in line:
                        parts = line.split(": ", 1)[1].rsplit(" ", 1)
                        soln_conc = parts[0].strip()
                        soln_conc_status = parts[1].strip() if len(parts) > 1 else ""
                        soln_conc_class = 'in-range' if soln_conc_status == 'in_range' else 'out-range'
                    elif "Int:" in line:
                        int_val = line.split(": ")[1].strip()
                    elif "Calibration Range:" in line:
                        calibration_range = line.split(": ", 1)[1].rsplit(" ", 1)[0].strip()
                    elif "ICP Recovery:" in line:
                        icp_recovery = line.split(": ", 1)[1].rsplit(" ", 1)[0].strip()
                    elif "ICP Status:" in line:
                        icp_status = line.split(": ")[1].strip()
                        icp_status_class = 'in-range' if icp_status == 'In Range' else 'out-range'
                    elif "ICP Detection Limit:" in line:
                        detection_limit = line.split(": ")[1].strip()
                    elif "ICP RSD%:" in line:
                        rsd_percent = line.split(": ")[1].strip()
                    elif "Required Scaling:" in line:
                        scaling_match = re.search(r'Required Scaling: ([\d.]+)% (increase|decrease)', line)
                        if scaling_match:
                            scaling = f"{scaling_match.group(1)}% {scaling_match.group(2)}"
                            if "Scaling exceeds" in line:
                                scaling = f'<span class="problematic">{scaling} (Problematic)</span>'
                
                # Map column names to their values and classes
                column_data = {
                    'Verification ID': (verification_id, ''),
                    'Certificate Value': (certificate_val, ''),
                    'Sample Value': (sample_val, sample_val_class),
                    'Acceptable Range': (acceptable_range, ''),
                    'Blank Value': (blank_val, ''),
                    'Blank Label': (blank_label, ''),
                    'Blank Correction Status': (blank_correction_status, ''),
                    'Sample Value - Blank': (corrected_sample_val, corrected_sample_val_class),
                    'Soln Conc': (soln_conc, soln_conc_class),
                    'Int': (int_val, ''),
                    'Calibration Range': (calibration_range, ''),
                    'ICP Recovery (%)': (icp_recovery, ''),
                    'ICP Status': (icp_status, icp_status_class),
                    'ICP Detection Limit': (detection_limit, ''),
                    'ICP RSD%': (rsd_percent, ''),
                    'Required Scaling (%)': (scaling, '')
                }
                
                # Generate table row with only visible columns
                html += "<tr>"
                for column, (value, css_class) in column_data.items():
                    if self.column_visibility[column]:
                        html += f'<td class="{css_class}">{value}</td>'
                html += "</tr>"
                
                # Collect for decision analysis
                try:
                    if (self.is_numeric(certificate_val) and 
                        self.is_numeric(sample_val) and 
                        self.is_numeric_range(acceptable_range)):
                        range_match = re.match(r'\[(-?[\d.]+) to (-?[\d.]+)\]', acceptable_range)
                        if range_match:
                            crm_entry = {
                                'id': verification_id,
                                'cert_val': float(certificate_val),
                                'sample_val': float(sample_val),
                                'lower': float(range_match.group(1)),
                                'upper': float(range_match.group(2)),
                                'blank_val': float(blank_val) if self.is_numeric(blank_val) else 0,
                                'soln_conc': float(soln_conc) if self.is_numeric(soln_conc) else None,
                                'wavelength': 'default'
                            }
                            crm_data.append(crm_entry)
                        else:
                            self.logger.warning(f"Invalid range format for {verification_id}: {acceptable_range}")
                    else:
                        self.logger.warning(f"Non-numeric or missing data for {verification_id}: cert_val={certificate_val}, sample_val={sample_val}, range={acceptable_range}")
                except Exception as e:
                    self.logger.error(f"Error parsing CRM data for {verification_id}: {str(e)}")
            
            html += "</table>"
            
            # Add textual decision analysis section
            html += self.generate_decision_analysis(crm_data)
            
            html += """
            </body>
            </html>
            """
            self.text_edit.setHtml(html)
            self.logger.debug("Generated HTML report successfully")
        except Exception as e:
            self.logger.error(f"Error generating HTML report: {str(e)}")
            self.text_edit.setHtml(f"<html><body><p>Error generating report: {str(e)}</p></body></html>")
            QMessageBox.warning(self, "Error", f"Failed to generate report: {str(e)}")

    def generate_decision_analysis(self, crm_data):
        """Generate textual decision analysis based on conditions, line by line, with final professional decision using three models."""
        if not crm_data:
            self.logger.warning("No sufficient data for decision analysis")
            self.decision_data['final_decision'] = "No sufficient data for analysis."
            return "<div class='decision-section'><h2>Decision Analysis</h2><p>No sufficient data for analysis.</p></div>"
        
        analysis_html = "<div class='decision-section'><h2>Decision Analysis</h2><ul>"
        
        # Condition 1: Check if subtracting blank brings into range
        in_range_with_blank = sum(d['lower'] <= (d['sample_val'] - d['blank_val']) <= d['upper'] for d in crm_data)
        total = len(crm_data)
        analysis_html += f"<li>Condition 1: With blank subtraction, {in_range_with_blank}/{total} CRMs are in range. "
        if in_range_with_blank / total >= 0.75:
            analysis_html += "This is sufficient for most cases; prefer blank adjustment over scaling.</li>"
        else:
            analysis_html += "Insufficient; consider additional adjustments.</li>"
        
        # Condition 2: Try dynamic blank adjustment
        def objective_cond2(p):
            adjust = p[0]
            in_range = sum(d['lower'] <= (d['sample_val'] - d['blank_val'] + adjust) <= d['upper'] for d in crm_data)
            return -in_range
        
        max_val = max([abs(d['sample_val']) for d in crm_data] + [abs(d['blank_val']) for d in crm_data] + [1])
        adjust_bounds = [(-10 * max_val, 10 * max_val)]
        
        try:
            res_cond2 = differential_evolution(objective_cond2, adjust_bounds)
            best_blank_adjust = res_cond2.x[0] if res_cond2.success else 0
            best_in_range = -res_cond2.fun if res_cond2.success else in_range_with_blank
        except Exception as e:
            self.logger.error(f"Error in Condition 2 optimization: {str(e)}")
            best_blank_adjust = 0
            best_in_range = in_range_with_blank
        
        analysis_html += f"<li>Condition 2: Adjusting blank by {best_blank_adjust:.3f}, achieves {best_in_range}/{total} in range. "
        if best_in_range / total >= 0.75:
            analysis_html += "No scaling needed; apply this blank adjustment globally.</li>"
        else:
            analysis_html += "Still insufficient; proceed to scaling.</li>"
        
        # Condition 3: Check initial in-range count
        initial_in_range = sum(d['lower'] <= d['sample_val'] <= d['upper'] for d in crm_data)
        analysis_html += f"<li>Condition 3: Initially, {initial_in_range}/{total} in range. With blank: {in_range_with_blank}/{total}. "
        if initial_in_range / total >= 0.75 or in_range_with_blank / total >= 0.75:
            analysis_html += "Majority in range; no scaling required.</li>"
        else:
            analysis_html += "Minority in range; scaling may be necessary.</li>"
        
        # Condition 4: Check duplicate CRM IDs
        id_groups = defaultdict(list)
        for d in crm_data:
            id_groups[d['id']].append(d)
        good_groups = sum(any(d['lower'] <= d['sample_val'] <= d['upper'] for d in group) for group in id_groups.values())
        analysis_html += f"<li>Condition 4: For duplicate CRM IDs, {good_groups}/{len(id_groups)} groups have at least one in range. "
        if len(id_groups) == 0 or good_groups / len(id_groups) >= 0.75:
            analysis_html += "Sufficient coverage; no scaling needed.</li>"
        else:
            analysis_html += "Insufficient; consider scaling.</li>"
        
        # Condition 5: Different magnitudes
        def get_magnitude(val):
            return np.floor(np.log10(abs(val))) if val != 0 else 0
        mag_groups = defaultdict(list)
        for d in crm_data:
            mag = get_magnitude(d['cert_val'])
            mag_groups[mag].append(d)
        analysis_html += f"<li>Condition 5: {len(mag_groups)} magnitude groups detected. "
        for mag, group in mag_groups.items():
            in_range = sum(d['lower'] <= d['sample_val'] <= d['upper'] for d in group)
            analysis_html += f"Magnitude 10^{mag}: {in_range}/{len(group)} in range. "
        if len(mag_groups) > 1:
            analysis_html += "Apply adjustments selectively to low-performing groups.</li>"
        else:
            analysis_html += "Uniform magnitude; global adjustment safe.</li>"
        
        # Condition 6: Multiple wavelengths
        analysis_html += "<li>Condition 6: Assuming single wavelength. If multiple, select best Soln Conc.</li>"
        
        # Condition 7: Average scaling
        max_corr = 0.3
        possible_scales = [d['cert_val'] / d['sample_val'] if d['sample_val'] != 0 else 1 for d in crm_data]
        possible_scales = [s for s in possible_scales if 1 - max_corr <= s <= 1 + max_corr]
        if possible_scales:
            avg_scale = np.mean(possible_scales)
            in_range_with_avg = sum(d['lower'] <= d['sample_val'] * avg_scale <= d['upper'] for d in crm_data)
            analysis_html += f"<li>Condition 7: Average scale {avg_scale:.3f} brings {in_range_with_avg}/{total} in range (allowing 1-2 outliers).</li>"
        else:
            analysis_html += "<li>Condition 7: No valid scales within bounds.</li>"
        
        # Condition 8: Best blank selection
        analysis_html += f"<li>Condition 8: Best blank adjustment selected as {best_blank_adjust:.3f}, maximizing in-range to {best_in_range}/{total}.</li>"
        
        analysis_html += "</ul>"
        
        # Compute bounds for optimization
        avg_cert = np.mean([d['cert_val'] for d in crm_data])
        max_val = max([abs(d['sample_val']) for d in crm_data] + [abs(d['blank_val']) for d in crm_data] + [abs(avg_cert), 1])
        blank_bounds_wide = (-10 * max_val, 10 * max_val)
        scale_bounds = (1 - max_corr, 1 + max_corr)
        
        # Model 1: Maximize in-range count
        def objective_a(params):
            blank_adjust, scale = params
            in_range_count = 0
            for d in crm_data:
                adjusted_val = (d['sample_val'] - d['blank_val'] + blank_adjust) * scale
                if d['lower'] <= adjusted_val <= d['upper']:
                    in_range_count += 1
            reg = 10 * abs(scale - 1)
            return -in_range_count + reg / total
        
        bounds_a = [blank_bounds_wide, scale_bounds]
        
        try:
            res_a = differential_evolution(objective_a, bounds_a)
            if res_a.success:
                blank_adjust_a, scale_a = res_a.x
                in_range_after_a = 0
                for d in crm_data:
                    adjusted_val = (d['sample_val'] - d['blank_val'] + blank_adjust_a) * scale_a
                    if d['lower'] <= adjusted_val <= d['upper']:
                        in_range_after_a += 1
                def objective_blank_a(p):
                    return objective_a([p[0], 1])
                res_blank_a = differential_evolution(objective_blank_a, [blank_bounds_wide])
                if res_blank_a.success:
                    blank_adjust_blank_a = res_blank_a.x[0]
                    in_range_blank_a = 0
                    for d in crm_data:
                        adjusted_val = (d['sample_val'] - d['blank_val'] + blank_adjust_blank_a) * 1
                        if d['lower'] <= adjusted_val <= d['upper']:
                            in_range_blank_a += 1
                    if in_range_blank_a > in_range_after_a or (in_range_blank_a == in_range_after_a and in_range_blank_a / total >= 0.75):
                        blank_adjust_a = blank_adjust_blank_a
                        scale_a = 1.0
                        in_range_after_a = in_range_blank_a
                analysis_html += f'<div class="model-comparison"><strong>Model A (In-Range Maximization):</strong> Blank adjust: {blank_adjust_a:.3f}, Scale: {scale_a:.3f}, In-range: {in_range_after_a}/{total}.</div>'
            else:
                analysis_html += '<div class="model-comparison"><strong>Model A:</strong> Optimization failed.</div>'
                self.logger.warning("Model A optimization failed")
        except Exception as e:
            analysis_html += f'<div class="model-comparison"><strong>Model A:</strong> Error: {str(e)}.</div>'
            self.logger.error(f"Error in Model A optimization: {str(e)}")
        
        # Model 2: Minimize distances with Huber loss
        def objective_b(params):
            blank_adjust, scale = params
            total_distance = 0.0
            for d in crm_data:
                adjusted_val = (d['sample_val'] - d['blank_val'] + blank_adjust) * scale
                if adjusted_val < d['lower']:
                    dist = d['lower'] - adjusted_val
                elif adjusted_val > d['upper']:
                    dist = adjusted_val - d['upper']
                else:
                    dist = 0.0
                total_distance += huber(1.0, dist)
            reg = 0.1 * (abs(scale - 1) * np.mean([abs(d['sample_val']) for d in crm_data]))
            return (total_distance / total) + reg
        
        bounds_b = [blank_bounds_wide, scale_bounds]
        
        try:
            res_b = differential_evolution(objective_b, bounds_b)
            if res_b.success:
                blank_adjust_b, scale_b = res_b.x
                distances_b = []
                in_range_after_b = 0
                for d in crm_data:
                    adjusted_val = (d['sample_val'] - d['blank_val'] + blank_adjust_b) * scale_b
                    if d['lower'] <= adjusted_val <= d['upper']:
                        in_range_after_b += 1
                        distances_b.append(0)
                    else:
                        dist = min(abs(adjusted_val - d['lower']), abs(adjusted_val - d['upper']))
                        distances_b.append(dist)
                avg_distance_b = np.mean(distances_b)
                def objective_blank_b(p):
                    return objective_b([p[0], 1])
                res_blank_b = differential_evolution(objective_blank_b, [blank_bounds_wide])
                if res_blank_b.success:
                    blank_adjust_blank_b = res_blank_b.x[0]
                    distances_blank_b = []
                    in_range_blank_b = 0
                    for d in crm_data:
                        adjusted_val = (d['sample_val'] - d['blank_val'] + blank_adjust_blank_b) * 1
                        if d['lower'] <= adjusted_val <= d['upper']:
                            in_range_blank_b += 1
                            distances_blank_b.append(0)
                        else:
                            dist = min(abs(adjusted_val - d['lower']), abs(adjusted_val - d['upper']))
                            distances_blank_b.append(dist)
                    avg_distance_blank_b = np.mean(distances_blank_b)
                    if in_range_blank_b > in_range_after_b or (in_range_blank_b == in_range_after_b and in_range_blank_b / total >= 0.75):
                        blank_adjust_b = blank_adjust_blank_b
                        scale_b = 1.0
                        in_range_after_b = in_range_blank_b
                        avg_distance_b = avg_distance_blank_b
                analysis_html += f'<div class="model-comparison"><strong>Model B (Distance Minimization):</strong> Blank adjust: {blank_adjust_b:.3f}, Scale: {scale_b:.3f}, In-range: {in_range_after_b}/{total}, Avg distance: {avg_distance_b:.3f}.</div>'
            else:
                analysis_html += '<div class="model-comparison"><strong>Model B:</strong> Optimization failed.</div>'
                self.logger.warning("Model B optimization failed")
        except Exception as e:
            analysis_html += f'<div class="model-comparison"><strong>Model B:</strong> Error: {str(e)}.</div>'
            self.logger.error(f"Error in Model B optimization: {str(e)}")

        # Model 3: Minimize sum of squared errors
        def objective_c(params):
            blank_adjust, scale = params
            total_sse = 0.0
            for d in crm_data:
                adjusted_val = (d['sample_val'] - d['blank_val'] + blank_adjust) * scale
                total_sse += (adjusted_val - d['cert_val']) ** 2
            reg = 0.1 * (abs(scale - 1) * np.mean([abs(d['sample_val']) for d in crm_data]))
            return (total_sse / total) + reg
        
        bounds_c = [blank_bounds_wide, scale_bounds]
        
        try:
            res_c = differential_evolution(objective_c, bounds_c)
            if res_c.success:
                blank_adjust_c, scale_c = res_c.x
                distances_c = []
                in_range_after_c = 0
                for d in crm_data:
                    adjusted_val = (d['sample_val'] - d['blank_val'] + blank_adjust_c) * scale_c
                    if d['lower'] <= adjusted_val <= d['upper']:
                        in_range_after_c += 1
                        distances_c.append(0)
                    else:
                        dist = min(abs(adjusted_val - d['lower']), abs(adjusted_val - d['upper']))
                        distances_c.append(dist)
                avg_distance_c = np.mean(distances_c)
                def objective_blank_c(p):
                    return objective_c([p[0], 1])
                res_blank_c = differential_evolution(objective_blank_c, [blank_bounds_wide])
                if res_blank_c.success:
                    blank_adjust_blank_c = res_blank_c.x[0]
                    distances_blank_c = []
                    in_range_blank_c = 0
                    for d in crm_data:
                        adjusted_val = (d['sample_val'] - d['blank_val'] + blank_adjust_blank_c) * 1
                        if d['lower'] <= adjusted_val <= d['upper']:
                            in_range_blank_c += 1
                            distances_blank_c.append(0)
                        else:
                            dist = min(abs(adjusted_val - d['lower']), abs(adjusted_val - d['upper']))
                            distances_blank_c.append(dist)
                    avg_distance_blank_c = np.mean(distances_blank_c)
                    if in_range_blank_c > in_range_after_c or (in_range_blank_c == in_range_after_c and in_range_blank_c / total >= 0.75):
                        blank_adjust_c = blank_adjust_blank_c
                        scale_c = 1.0
                        in_range_after_c = in_range_blank_c
                        avg_distance_c = avg_distance_blank_c
                analysis_html += f'<div class="model-comparison"><strong>Model C (SSE Minimization to Cert Val):</strong> Blank adjust: {blank_adjust_c:.3f}, Scale: {scale_c:.3f}, In-range: {in_range_after_c}/{total}, Avg distance: {avg_distance_c:.3f}.</div>'
            else:
                analysis_html += '<div class="model-comparison"><strong>Model C:</strong> Optimization failed.</div>'
                self.logger.warning("Model C optimization failed")
        except Exception as e:
            analysis_html += f'<div class="model-comparison"><strong>Model C:</strong> Error: {str(e)}.</div>'
            self.logger.error(f"Error in Model C optimization: {str(e)}")
        
        # Model Comparison and Final Decision
        initial_distances = [0 if d['lower'] <= d['sample_val'] <= d['upper'] else min(abs(d['sample_val'] - d['lower']), abs(d['sample_val'] - d['upper'])) for d in crm_data]
        avg_distance_initial = np.mean(initial_distances)
        
        models = []
        if 'in_range_after_a' in locals():
            models.append(('A', in_range_after_a, blank_adjust_a, scale_a, 'N/A' if scale_a == 1 else avg_distance_b if 'avg_distance_b' in locals() else float('inf')))
        if 'in_range_after_b' in locals():
            models.append(('B', in_range_after_b, blank_adjust_b, scale_b, avg_distance_b))
        if 'in_range_after_c' in locals():
            models.append(('C', in_range_after_c, blank_adjust_c, scale_c, avg_distance_c))
        
        if models:
            best_model = max(models, key=lambda m: (m[1], -m[4] if isinstance(m[4], float) else float('inf')))
            blank_adjust = best_model[2]
            recommended_scale = best_model[3]
            recommended_in_range = best_model[1]
            recommended_avg_distance = best_model[4]
            model_used = best_model[0]
            # Calculate effective recommended_blank = blank_val - blank_adjust (modified blank)
            blank_val = crm_data[0]['blank_val'] if crm_data else 0.0
            recommended_blank = blank_val - blank_adjust
        else:
            blank_adjust = best_blank_adjust
            recommended_scale = 1.0
            recommended_in_range = best_in_range
            recommended_avg_distance = "N/A"
            model_used = "Fallback"
            blank_val = crm_data[0]['blank_val'] if crm_data else 0.0
            recommended_blank = blank_val - blank_adjust
        
        # Update decision data
        self.decision_data.update({
            'recommended_blank': recommended_blank,
            'recommended_scale': recommended_scale,
            'recommended_in_range': recommended_in_range,
            'recommended_avg_distance': recommended_avg_distance if isinstance(recommended_avg_distance, float) else "N/A",
            'final_decision': "Calculating..."
        })
        
        analysis_html += f"<p><strong>Model Comparison Summary:</strong> Initial in-range: {initial_in_range}/{total}, Avg distance initial: {avg_distance_initial:.3f}.</p>"
        analysis_html += f"<p><strong>Recommended Correction (Model {model_used}):</strong> Effective blank to subtract: {recommended_blank:.3f}, Scale: {recommended_scale:.3f}, achieving {recommended_in_range}/{total} in range (avg distance: {recommended_avg_distance}).</p>"
        
        # Final decision
        if recommended_in_range > initial_in_range or recommended_in_range == total:
            if recommended_in_range == total:
                recommended_action = "Apply the recommended correction; all data will be in range."
            elif recommended_in_range >= max(0.75 * total, total - 1):  # Allow at most one outlier
                recommended_action = f"Apply the recommended correction; majority ({recommended_in_range}/{total}) in range."
            else:
                recommended_action = f"Apply the recommended correction for improvement; {recommended_in_range}/{total} in range."
        else:
            recommended_action = "No improvement; do not apply changes. Consider manual review if needed."
        
        self.decision_data['final_decision'] = recommended_action
        analysis_html += f"<p><strong>Final Decision:</strong> {recommended_action} This ensures the majority of data falls within acceptable ranges with the least disruption, aligning with the goal of optimal data integrity.</p></div>"
        
        self.logger.debug(f"Decision Analysis: Final Decision = {recommended_action}, Blank = {recommended_blank:.3f}, Scale = {recommended_scale:.3f}, In-range = {recommended_in_range}/{total}")
        return analysis_html

    def get_final_decision(self):
        """Return the Final Decision for external use."""
        try:
            # Ensure decision_data is populated
            if self.decision_data['final_decision'] == "Calculating...":
                crm_data = []
                for annotation in self.annotations:
                    lines = annotation.split('\n')
                    if not lines:
                        continue
                    verification_id_match = re.match(r'Verification ID: (\w+) \(Label: .+\)', lines[0].strip())
                    verification_id = verification_id_match.group(1) if verification_id_match else "Unknown"
                    certificate_val = sample_val = acceptable_range = blank_val = ""
                    for line in lines[1:]:
                        line = line.strip()
                        if "Certificate Value:" in line:
                            certificate_val = line.split(": ")[1].strip()
                        elif "Sample Value:" in line:
                            sample_val = line.split(": ")[1].strip()
                        elif "Acceptable Range:" in line:
                            acceptable_range = line.split(": ")[1].strip()
                        elif "Blank Value:" in line:
                            blank_val = line.split(": ")[1].strip()
                    if (self.is_numeric(certificate_val) and 
                        self.is_numeric(sample_val) and 
                        self.is_numeric_range(acceptable_range)):
                        range_match = re.match(r'\[(-?[\d.]+) to (-?[\d.]+)\]', acceptable_range)
                        if range_match:
                            crm_entry = {
                                'id': verification_id,
                                'cert_val': float(certificate_val),
                                'sample_val': float(sample_val),
                                'lower': float(range_match.group(1)),
                                'upper': float(range_match.group(2)),
                                'blank_val': float(blank_val) if self.is_numeric(blank_val) else 0,
                                'soln_conc': None,
                                'wavelength': 'default'
                            }
                            crm_data.append(crm_entry)
                self.generate_decision_analysis(crm_data)  # Populate decision_data
            self.logger.debug(f"Returning Final Decision: {self.decision_data['final_decision']}")
            return self.decision_data['final_decision']
        except Exception as e:
            self.logger.error(f"Error retrieving Final Decision: {str(e)}")
            return "Error determining decision"

    def get_correction_parameters(self):
        """Return recommended blank and scale for compatibility."""
        try:
            self.logger.debug(f"Returning correction parameters: blank={self.decision_data['recommended_blank']:.3f}, scale={self.decision_data['recommended_scale']:.3f}")
            return self.decision_data['recommended_blank'], self.decision_data['recommended_scale']
        except Exception as e:
            self.logger.error(f"Error retrieving correction parameters: {str(e)}")
            return 0.0, 1.0  # Fallback values