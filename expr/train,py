import json
import pandas as pd
import xgboost as xgb
import onnxmltools
from onnxmltools.convert.common.data_types import FloatTensorType

with open("json_merge.json", 'r', encoding='utf-8-sig') as f:
    data = json.load(f)
    
rows = []

for item in data["questions"]:
    status_list = item["status"]
    
    for i in range(len(status_list)):
        current_status = status_list[i]

        sx = 100 if current_status["s"] >= 56 else current_status["s"]

        rows.append({
            "speed_ratio": current_status["speed"] / 91.99920667582167,
            "s": sx,
            "q": current_status["q"],
        })

df = pd.DataFrame(rows)

X = df[['speed_ratio', 's']]
y = df['q']

clf_xgb = xgb.XGBClassifier(
    objective='multi:softprob', 
    eval_metric='mlogloss', 
    random_state=42
)

clf_xgb.fit(X, y)


clf_xgb.get_booster().feature_names = None
feature_count = 2
initial_type = [('float_input', FloatTensorType([None, feature_count]))]

onnx_model = onnxmltools.convert_xgboost(
    clf_xgb, 
    initial_types=initial_type,
    target_opset=15
)

with open("xgboost_model.onnx", "wb") as f:
    f.write(onnx_model.SerializeToString())
