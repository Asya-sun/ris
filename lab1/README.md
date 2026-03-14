# CrackHash - Distributed Hash Cracking System

## Описание API

### External API (Manager) - для клиентов
Этот интерфейс предназначен для внешних клиентов, желающих отправить задание на взлом хэша или проверить его статус.

#### POST /api/hash/crack

Создание запроса на взлом хэша.

**Request:**

```json
{
  "hash": "e2fc714c4727ee9395f324cd2e7f331f",
  "maxLength": 4
}
```

**Response (200 OK):**

```json
{
  "requestId": "0160c0ac-5c32-4145-ac08-0ff3f9042401"
}
```

**Status Codes:**

- `200 OK` - запрос принят
- `400 Bad Request` - неверный формат запроса
- `500 Internal Server Error` - нет доступных воркеров или внутренняя ошибка

---

#### GET /api/hash/status

Получение статуса выполнения запроса.

**Parameters:**

- `crackId` - UUID запроса, полученный при создании

**Response (IN_PROGRESS):**

```json
{
  "status": "IN_PROGRESS",
  "progress": 65,
  "data": null
}
```

**Response (READY):**

```json
{
  "status": "READY",
  "progress": 100,
  "data": [
    "abcd"
  ]
}
```

**Response (ERROR):**

```json
{
  "status": "ERROR",
  "progress": 50,
  "data": null
}
```
---

### Internal API (Manager) - для воркеров
Этот интерфейс используется воркерами для регистрации в системе менеджера и отправки результатов. Не предназначен для внешних клиентов.


**Request:**

```json
{
    "workerName": "CoolName",
    "url": "http://worker-1:5000"
}
```

**Response (200 OK):**

```json
{
    "workerId": "a1b2c3d4-1111-2222-3333-444444444444"
}
```

#### POST /api/tasks/progress

Прием результатов выполнения части задачи от воркера.

**Request:**

```json
{
  "taskRequestId": "0160c0ac-5c32-4145-ac08-0ff3f9042401",
  "foundWords": [],
  "startIndex": 5000,
  "endIndex": 10000,
  "checkedCount": ,
  "isRequestDone": false,
}
```

---


### Internal API (Worker) - для менеджера



#### POST /api/v1/tasks/

Отправка воркеру части диапазона для перебора

**Request:**

```json
{
  "taskRequestId": "0160c0ac-5c32-4145-ac08-0ff3f9042401",
  "hash": "e2fc714c4727ee9395f324cd2e7f331f",
  "maxLength": 4,
  "startIndex": 0,
  "endIndex": 500000
}
```

## Инструкция по запуску

### Предварительные требования
- установленные  Docker и Docker Compose
- Python 3.12 (для запуска теста crack-test)


### Запуск системы в Docker
1. Настройка Docker

```
cd lab1/

# Добавить пользователя в группу docker
sudo usermod -aG docker $USER

# Применить изменения группы (или перелогиниться)
newgrp docker

# Проверить, что Docker работает
docker ps
```


2. Сборка и запуск контейнеров
```
# Остановить текущие контейнеры (если есть)
docker compose down

# Пересобрать образы
docker compose build --no-cache

# Запустить контейнеры
docker compose up
# или с сохранением логов в файл и выводом к консоль
docker compose up 2>&1 | tee logs.txt
```

### Запуск тестов
#### Через программу на python 
Микро тест для проверки =)
1. Настройка окружения для тестов
```
# Перейти в директорию с тестами
cd lab1/crack-test

# Установить venv (если не установлен)
sudo apt update
sudo apt install python3.12-venv

# Создать виртуальное окружение
python3 -m venv venv

# Активировать виртуальное окружение
source venv/bin/activate

# Установить зависимости
pip install requests
```

2. Запуск тестов
```
# Запустить тесты (убедитесь, что Docker контейнеры уже запущены)
python test_crack.py
```

3. Завершение работы с тестами
```
# Деактивировать виртуальное окружение
deactivate
```
#### Посмотреть через Swagger
если программа запущена в docker
[для воркера](http://localhost:8081/swagger/index.html) 
[для менеджера](http://localhost:8080/swagger/index.html) 