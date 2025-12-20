from PyQt6.QtWidgets import QApplication
import pandas as pd
import re
import logging
import numpy as np
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)
class ApplySingleRM:
    def __init__(self, app, keyword, element, rm_num, rm_df, initial_rm_df, segments, stepwise, progress_dialog=None):
        self.app = app
        self.keyword = keyword
        self.element = element
        self.rm_num = rm_num
        self.rm_df = rm_df.copy(deep=True)
        self.initial_rm_df = initial_rm_df.copy(deep=True)
        self.segments = segments
        self.stepwise = stepwise
        self.corrected_drift = {}
        self.progress_dialog = progress_dialog

    def run(self):
        try:
            df = self.app.results.last_filtered_data.copy(deep=True)
            if 'original_index' not in df.columns:
                df['original_index'] = df.index if 'pivot_index' not in df.columns else df['pivot_index']
            df = df.sort_values('original_index').reset_index(drop=True)
            df['pivot_index'] = df.index

            if df.empty:
                return {'error': "No data to process."}

            total_steps = len(self.segments)
            step = 0

            for seg_idx, segment in enumerate(self.segments):
                ref_rm_num = segment['ref_rm_num']
                pos_df = segment['positions'].copy()
                rm_pos = pos_df[pos_df['rm_num'] == self.rm_num].copy()

                if rm_pos.empty:
                    step += 1
                    if self.progress_dialog:
                        self.progress_dialog.setValue(int(step / total_steps * 100))
                        QApplication.processEvents()
                    continue

                # فقط RMهایی که rm_num >= ref_rm_num هستند (یعنی بعد از مرجع)
                valid = rm_pos[rm_pos['rm_num'] >= ref_rm_num].copy()

                if hasattr(self.app, 'rm_check') and self.app.rm_check:
                    ignored_set = self.app.rm_check.ignored_pivots
                    valid = valid[~valid['pivot_index'].isin(ignored_set)]
                if valid.empty:
                    step += 1
                    if self.progress_dialog:
                        self.progress_dialog.setValue(int(step / total_steps * 100))
                        QApplication.processEvents()
                    continue

                # مهم: فقط بر اساس موقعیت واقعی در داده مرتب کن (نه row_id!)
                valid = valid.sort_values('pivot_index').reset_index(drop=True)

                # حالا پیدا کردن RM مرجع (اولین RM معتبر)
                allowed = {'RM', 'RM RM', 'RM 1', 'RM1'}
                ref_candidates = valid[valid['Solution Label'].str.fullmatch(rf'{re.escape(self.keyword)}\s*\d*', na=False, flags=re.IGNORECASE)]
                if not ref_candidates.empty:
                    ref_row = ref_candidates.iloc[0]
                else:
                    ref_row = valid.iloc[0]  # اگر نبود، اولین را بگیر

                start_pivot = ref_row['pivot_index']
                logger.debug(f"Reference RM at pivot_index = {start_pivot}, row_id = {ref_row['row_id']}")

                # فقط RMهایی که بعد از مرجع هستند (شامل خود مرجع نمی‌شود برای تصحیح)
                correction_rm = valid[valid['pivot_index'] > start_pivot].copy()
                if correction_rm.empty:
                    step += 1
                    continue

                # پیش‌محاسبه ratioها بر اساس اندیس در valid اصلی (نه در correction_rm!)
                init_series = self.initial_rm_df[self.initial_rm_df['rm_num'] == self.rm_num][self.element]
                curr_series = self.rm_df[self.rm_df['rm_num'] == self.rm_num][self.element]
                init_vals = pd.to_numeric(init_series, errors='coerce').fillna(0).values
                curr_vals = pd.to_numeric(curr_series, errors='coerce').fillna(0).values

                # نقشه از pivot_index به اندیس در آرایه init_vals/curr_vals
                pivot_to_idx = dict(zip(valid['pivot_index'], valid.index))

                prev_pivot = start_pivot

                for _, rm_row in correction_rm.iterrows():
                    cur_pivot = rm_row['pivot_index']

                    if cur_pivot <= prev_pivot:
                        continue  # جلوگیری از عقب‌گرد

                    # پیدا کردن اندیس درست در init_vals و curr_vals
                    orig_idx = pivot_to_idx.get(cur_pivot)
                    if orig_idx is None or orig_idx >= len(init_vals) or orig_idx >= len(curr_vals):
                        prev_pivot = cur_pivot
                        continue

                    i_val = init_vals[orig_idx]
                    c_val = curr_vals[orig_idx]
                    ratio = c_val / i_val if i_val != 0 else 1.0

                    # بازه بین دو RM
                    cond = (
                        (df['pivot_index'] >= prev_pivot) &
                        (df['pivot_index'] < cur_pivot) &
                        ~df['Solution Label'].str.fullmatch(rf'{re.escape(self.keyword)}\s*\d*', na=False, flags=re.IGNORECASE)
                    )
                    to_correct = df[cond]

                    if not to_correct.empty:
                        orig_vals = pd.to_numeric(to_correct[self.element], errors='coerce').fillna(0).values
                        new_vals = self.calculate_corrected_values(orig_vals, ratio)
                        df.loc[to_correct.index, self.element] = new_vals

                        for idx in to_correct.index:
                            sl = df.at[idx, 'Solution Label']
                            self.corrected_drift[(sl, self.element)] = ratio

                        logger.debug(f"RM {rm_row['row_id']}: ratio={ratio:.6f} [{prev_pivot} → {cur_pivot})")

                    prev_pivot = cur_pivot

                step += 1
                if self.progress_dialog:
                    self.progress_dialog.setValue(int(step / total_steps * 100))
                    QApplication.processEvents()

            return {
                'df': df,
                'rm_df': self.rm_df,
                'corrected_drift': self.corrected_drift
            }

        except Exception as e:
            logger.error(f"ApplySingleRM error: {e}", exc_info=True)
            return {'error': str(e)}

    def calculate_corrected_values(self, original_values, ratio):
        values = np.array(original_values, dtype=float)
        n = len(values)
        if n == 0:
            return np.array([])
        if self.stepwise and n > 1:
            delta = ratio - 1.0
            step_delta = delta / n
            factors = 1.0 + step_delta * np.arange(1, n + 1)
        else:
            factors = np.full(n, ratio)
        return values * factors