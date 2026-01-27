import os
import json
import random

with open("data.json", 'r', encoding='utf-8-sig') as f:
    json_data = json.load(f)

insensitive_json_data = {
    "questions": json_data["questions"],
    "r0": json_data["r0"]
}

series = random.randint(100000, 999999)

with open(f"dataset\\data_{series}.json", "w", encoding="utf-8") as f:
    json.dump(insensitive_json_data, f, ensure_ascii=False, indent=4)

dataset_path = 'dataset'
all_questions = []

for filename in os.listdir(dataset_path):
    if filename.endswith('.json'):
        file_path = os.path.join(dataset_path, filename)
        with open(file_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
            if 'questions' in data:
                all_questions.extend(data['questions'])

with open('json_merge.json', 'w', encoding='utf-8') as f:
    json.dump({"questions": all_questions}, f, ensure_ascii=False, indent=4)
