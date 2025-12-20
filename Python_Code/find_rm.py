from PyQt6.QtCore import Qt, QThread, pyqtSignal
import re
from typing import Any, Dict, List, Optional, Tuple
import pandas as pd
import logging

logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

def extract_rm_info(label, keyword="RM"):
    label = str(label).strip()
    label_lower = label.lower()
    cleaned = re.sub(rf'^{re.escape(keyword)}\s*[-_]?\s*', '', label_lower, flags=re.IGNORECASE)
    rm_type = 'Base'
    rm_number = 0
    type_match = re.search(r'(chek|check|cone)', cleaned)
    if type_match:
        typ = type_match.group(1)
        rm_type = 'Check' if typ in ['chek', 'check'] else 'Cone'
        before_text = cleaned[:type_match.start()]
    else:
        before_text = cleaned
    numbers = re.findall(r'\d+', before_text)
    if numbers:
        rm_number = int(numbers[-1])
    return rm_number, rm_type

class CheckRMThread(QThread):
    progress = pyqtSignal(int)
    finished = pyqtSignal(dict)
    error = pyqtSignal(str)

    def __init__(self, app, keyword: str):
        super().__init__()
        self.app = app
        self.keyword = keyword.strip()

    def run(self):
        try:
            df = self.app.results.last_filtered_data.copy(deep=True)
            if df is None or df.empty:
                self.error.emit("No data loaded.")
                return

            required_columns = ['Solution Label']
            missing_columns = [col for col in required_columns if col not in df.columns]
            if missing_columns:
                self.error.emit(f"Missing required columns: {missing_columns}")
                return

            if 'pivot_index' in df.columns:
                df['original_index'] = df['pivot_index']
            else:
                df['original_index'] = df.index

            pivot_df = df.sort_values('original_index').reset_index(drop=True)
            pivot_df['pivot_index'] = pivot_df.index

            for col in pivot_df.columns:
                if col not in ['Solution Label', 'original_index', 'pivot_index']:
                    pivot_df[col] = pd.to_numeric(pivot_df[col], errors='coerce')

            element_cols = [col for col in pivot_df.columns if pd.api.types.is_numeric_dtype(pivot_df[col]) and col not in ['original_index', 'pivot_index', 'Solution Label']]

            if not element_cols:
                self.error.emit("No numeric element columns found.")
                return

            rm_df = pivot_df[
                pivot_df['Solution Label'].str.match(rf'^{re.escape(self.keyword)}', na=False, flags=re.IGNORECASE)
            ].copy()

            rm_df['row_id'] = 0
            solution_labels = sorted(
                rm_df['Solution Label'].unique(),
                key=lambda x: extract_rm_info(x, self.keyword)[0]
            )

            if rm_df.empty:
                labels = pivot_df['Solution Label'].unique().tolist()
                self.error.emit(f"No {self.keyword} found. Labels: {labels[:10]}{'...' if len(labels)>10 else ''}")
                return

            rm_df = self._add_rm_num_and_type(rm_df)
            positions_df = self._create_segment_positions(rm_df)
            segments = self._create_segments(positions_df)

            results = {
                'rm_df': rm_df,
                'positions_df': positions_df,
                'segments': segments,
                'pivot_df': pivot_df,
                'solution_labels': solution_labels,
                'elements': element_cols
            }
            self.finished.emit(results)
        except Exception as e:
            logger.error(f"Error in CheckRMThread: {str(e)}", exc_info=True)
            self.error.emit(str(e))

    def _add_rm_num_and_type(self, rm_df: pd.DataFrame) -> pd.DataFrame:
        info = rm_df['Solution Label'].apply(extract_rm_info, keyword=self.keyword)
        rm_df[['rm_num', 'rm_type']] = pd.DataFrame(info.tolist(), index=rm_df.index)
        rm_df['rm_num'] = rm_df['rm_num'].astype(int)
        return rm_df

    def _create_segment_positions(self, rm_df: pd.DataFrame) -> pd.DataFrame:
        rm_df = rm_df.sort_values('original_index').reset_index(drop=True)
        positions_list = []
        current_segment = 0
        ref_rm_num = None
        for idx, row in rm_df.iterrows():
            rm_type = row['rm_type']
            rm_num = row['rm_num']
            if rm_type == 'Cone':
                current_segment += 1
                ref_rm_num = None
            if ref_rm_num is None and rm_type in ['Base', 'Check']:
                ref_rm_num = rm_num

            min_pos = rm_df.iloc[idx-1]['original_index'] if idx > 0 else -1
            max_pos = row['original_index']

            positions_list.append({
                'Solution Label': row['Solution Label'],
                'row_id': row['row_id'],
                'pivot_index': row['pivot_index'],
                'min': min_pos,
                'max': max_pos,
                'rm_num': rm_num,
                'rm_type': rm_type,
                'segment_id': current_segment,
                'ref_rm_num': ref_rm_num if ref_rm_num is not None else rm_num
            })

        positions_df = pd.DataFrame(positions_list)
        if not positions_df.empty:
            positions_df.loc[0, 'min'] = -1
        return positions_df

    def _create_segments(self, positions_df: pd.DataFrame) -> List[Dict[str, Any]]:
        segments = []
        for seg_id in positions_df['segment_id'].unique():
            seg_df = positions_df[positions_df['segment_id'] == seg_id].copy()
            ref_num = seg_df['ref_rm_num'].iloc[0]
            segments.append({
                'segment_id': seg_id,
                'ref_rm_num': ref_num,
                'positions': seg_df
            })
        return segments