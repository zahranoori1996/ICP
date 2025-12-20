# screens/pivot/pivot_creator.py
import re
import math
from functools import reduce
import numpy as np
import pandas as pd
from PyQt6.QtWidgets import QMessageBox


def gcd_list(numbers):
    if not numbers:
        return 1
    return reduce(math.gcd, numbers)


class PivotCreator:
    """ساخت پیوت فقط با NumPy — در آخر به pandas تبدیل میشود تا با کد قدیمی سازگار باشد"""

    def __init__(self, pivot_tab):
        self.pivot_tab = pivot_tab

    def create_pivot(self):
        df = self.pivot_tab.app.init_data
        if df is None or len(df) == 0:
            QMessageBox.warning(self.pivot_tab, "هشدار", "داده‌ای برای نمایش وجود ندارد!")
            self.pivot_tab.pivot_data = None
            self.pivot_tab.update_pivot_display()
            return

        try:
            records = df.to_dict('records')
            samples = [r for r in records if str(r.get('Type', '')).strip() in ('Samp', 'Sample')]
            if not samples:
                QMessageBox.warning(self.pivot_tab, "هشدار", "هیچ نمونه‌ای (Sample/Samp) یافت نشد!")
                self.pivot_tab.pivot_data = None
                self.pivot_tab.update_pivot_display()
                return

            # حفظ ترتیب اصلی
            for i, rec in enumerate(samples):
                rec['_orig_index'] = i

            # تمیزکاری
            for rec in samples:
                label = rec.get('Solution Label')
                rec['Solution Label'] = 'Unknown' if not label or str(label).strip() in ('', 'nan') else str(label).strip()
                elem = str(rec.get('Element', ''))
                rec['Element'] = elem.split('_')[0]

            value_col = 'Int' if self.pivot_tab.use_int_var.isChecked() else 'Corr Con'

            # گروه‌بندی بر اساس Solution Label
            solution_groups = {}
            for rec in samples:
                sl = rec['Solution Label']
                solution_groups.setdefault(sl, []).append(rec)

            # محاسبه اندازه مجموعه
            most_common_sizes = {}
            for sl, group in solution_groups.items():
                counts = {}
                for r in group:
                    counts[r['Element']] = counts.get(r['Element'], 0) + 1
                values = list(counts.values())
                g = gcd_list(values)
                total = len(group)
                most_common_sizes[sl] = total // g if g > 1 and total % g == 0 else total

            # تشخیص تکرار عنصر
            has_repeats = False
            check = {}
            for rec in samples:
                sl = rec['Solution Label']
                pos = next(i for i, r in enumerate(solution_groups[sl]) if r is rec)
                gid_approx = pos // most_common_sizes[sl]
                key = (sl, gid_approx, rec['Element'])
                check[key] = check.get(key, 0) + 1
                if check[key] > 1:
                    has_repeats = True
                    break

            def label_key(x):
                s = str(x).replace(' ', '')
                m = re.search(r'(\d+)', s)
                return (s.lower() if not m else s[:m.start()].lower(), int(m.group(1)) if m else 0)

            final_rows = []

            if has_repeats:
                # حالت تکرار: گروه‌بندی دقیق
                for rec in samples:
                    sl = rec['Solution Label']
                    pos = next(i for i, r in enumerate(solution_groups[sl]) if r is rec)
                    rec['_group_id'] = pos // most_common_sizes[sl]

                # شمارش تکرار
                occ_count = {}
                for rec in samples:
                    k = (rec['Solution Label'], rec['_group_id'], rec['Element'])
                    occ_count[k] = occ_count.get(k, 0) + 1

                occ_counter = {}
                for rec in samples:
                    k = (rec['Solution Label'], rec['_group_id'], rec['Element'])
                    n = occ_count[k]
                    idx = occ_counter.get(k, 0) + 1
                    occ_counter[k] = idx
                    rec['_col'] = f"{rec['Element']}_{idx}" if n > 1 else rec['Element']

                row_map = {}
                for rec in samples:
                    rid = (rec['Solution Label'], rec['_group_id'])
                    row_map.setdefault(rid, {'Solution Label': rec['Solution Label']})
                    row_map[rid][rec['_col']] = rec.get(value_col)

                def first_index(rid):
                    sl, gid = rid
                    return min(r['_orig_index'] for r in samples if r['Solution Label'] == sl and r.get('_group_id') == gid)

                ordered = sorted(row_map.items(), key=lambda x: first_index(x[0]))
        
                final_rows = [row for _, row in ordered]

                first_full = next((row for _, row in row_map.items()
                                 if len(row) - 1 >= most_common_sizes.get(row['Solution Label'], 1)), None)
                self.pivot_tab.element_order = sorted(
                    [k for k in (first_full or final_rows[0]).keys() if k != 'Solution Label'],
                    key=label_key
                )

            else:
                # بدون تکرار
                uid_map = {}
                for rec in samples:
                    k = (rec['Solution Label'], rec['Element'])
                    uid_map[k] = uid_map.get(k, -1) + 1
                    rec['_uid'] = uid_map[k]

                row_map = {}
                for rec in samples:
                    rid = (rec['Solution Label'], rec['_uid'])
                    row_map.setdefault(rid, {'Solution Label': rec['Solution Label']})
                    row_map[rid][rec['Element']] = rec.get(value_col)

                def first_index(rid):
                    sl, uid = rid
                    return min((r['_orig_index'] for r in samples if r['Solution Label'] == sl and r.get('_uid') == uid), default=999999)

                ordered = sorted(row_map.items(), key=lambda x: first_index(x[0]))
                final_rows = [row for _, row in ordered]

                cols = set()
                for r in final_rows:
                    cols.update(k for k in r if k != 'Solution Label')
                self.pivot_tab.element_order = sorted(cols, key=label_key)

            self.pivot_tab.solution_label_order = sorted(
                {r['Solution Label'] for r in final_rows}, key=label_key
            )

            # تبدیل اکسید
            if self.pivot_tab.use_oxide_var.isChecked():
                from .oxide_factors import oxide_factors
                new_rows = []
                for row in final_rows:
                    nr = {'Solution Label': row['Solution Label']}
                    for k, v in row.items():
                        if k == 'Solution Label':
                            continue
                        try:
                            val = float(v) if v not in (None, '', 'nan') else None
                        except:
                            val = None
                        elem = k.split('_')[0]
                        suffix = '_' + k.split('_', 1)[1] if '_' in k and has_repeats else ''
                        if elem in oxide_factors:
                            oxide, factor = oxide_factors[elem]
                            new_k = f"{oxide}{suffix}" if suffix else oxide
                            nr[new_k] = val * factor if val is not None else None
                        else:
                            nr[k] = val
                    new_rows.append(nr)
                final_rows = new_rows

            # ساخت DataFrame نهایی (مهم!)
            if not final_rows:
                pivot_df = pd.DataFrame({'Solution Label': ['بدون داده']})
            else:
                pivot_df = pd.DataFrame(final_rows)

            # اعمال ترتیب دلخواه ستون‌ها
            if hasattr(self.pivot_tab, 'element_order') and self.pivot_tab.element_order:
                cols = ['Solution Label'] + [c for c in self.pivot_tab.element_order if c in pivot_df.columns]
                missing = [c for c in pivot_df.columns if c not in cols]
                pivot_df = pivot_df[cols + missing]

            # ذخیره نهایی
            self.pivot_tab.pivot_data = pivot_df
            self.pivot_tab.column_widths.clear()
            self.pivot_tab.cached_formatted.clear()
            self.pivot_tab.row_filter_values.clear()
            self.pivot_tab.column_filter_values.clear()
            self.pivot_tab.update_pivot_display()

        except Exception as e:
            import traceback
            traceback.print_exc()
            QMessageBox.critical(self.pivot_tab, "خطا", f"خطا در ساخت جدول پیوت:\n{e}")
            self.pivot_tab.pivot_data = None
            self.pivot_tab.update_pivot_display()