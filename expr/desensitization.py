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