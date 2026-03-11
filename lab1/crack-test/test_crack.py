#!/usr/bin/env python3
import requests
import hashlib
import sys
import time
import json

MANAGER_URL = "http://localhost:8080"

def calculate_md5(text):
    return hashlib.md5(text.encode('utf-8')).hexdigest()

def send_crack_request(hash_value, max_length):
    url = f"{MANAGER_URL}/api/hash/crack"
    payload = {"hash": hash_value, "maxLength": max_length}
    
    print(f"\nОтправляю запрос на {url}")
    print(f"Тело запроса: {json.dumps(payload, indent=2)}")
    
    try:
        response = requests.post(url, json=payload)
        response.raise_for_status()
        result = response.json()
        request_id = result.get("requestId")
        print(f"Успех! Request ID: {request_id}")
        return request_id
    except Exception as e:
        print(f"Ошибка: {e}")
        return None

def check_status(request_id):
    url = f"{MANAGER_URL}/api/hash/status"
    params = {"crackId": request_id} 
    
    try:
        response = requests.get(url, params=params)
        response.raise_for_status()
        return response.json()
    except Exception as e:
        print(f"Ошибка при проверке статуса: {e}")
        return None

def wait_for_result(request_id):
    print(f"\nОжидание результата...")
    
    while True:
        status_data = check_status(request_id)
        if not status_data:
            break
            
        status = status_data.get("status")
        progress = status_data.get("progress")
        
        progress_bar = "***" * (progress // 10) + "░" * (10 - (progress // 10))
        print(f"\r### Прогресс: [{progress_bar}] {progress}% | Статус: {status}", end="", flush=True)
        
        if status in ["READY", "ERROR"]:
            print(f"RESULT: {status_data}")
            break
            
        time.sleep(2)

def test_word(word):
    print(f"\n{'='*50}")
    print(f"Тест слова: '{word}'")
    print(f"{'='*50}")
    
    hash_value = calculate_md5(word)
    print(f"Хэш: {hash_value}")
    
    request_id = send_crack_request(hash_value, len(word))

    time.sleep(2)
    if request_id:
        wait_for_result(request_id)

if __name__ == "__main__":
    print("Тестирование CrackHash Manager")
    print(f"Адрес менеджера: {MANAGER_URL}")
    
    
    # Ручной ввод
    word = input("\nфлфавит = abcdefghijklmnopqrstuvwxyz0123456789\nВведите слово для теста (или Enter для выхода): ").strip()
    if word:
        test_word(word)
    
    print("\nТестирование завершено!")